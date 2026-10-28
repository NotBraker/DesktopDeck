using System;
using System.Linq;
using System.Windows.Controls;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

// The rest of "Place on screen ›": what only pinned widgets have (fade when idle, let clicks through) and the quick sizes.
public sealed partial class MainWindow
{
    /// <summary>Pinned widgets only: fade when idle (and how far) and let clicks pass through.</summary>
    private void AddFloatOptions(MenuItem place, LauncherItem item)
    {
        AddToggle(place, "Fade when idle", item.FloatIdleFade, value =>
        {
            item.FloatIdleFade = value;
            ChangedFloat(value ? $"{ItemName(item)} fades when you aren't using it." : $"{ItemName(item)} stays fully visible.");
        }).ToolTip = "After a few seconds without the pointer over it, the widget turns see-through. It comes back as soon as you point at it.";
        if (item.FloatIdleFade)
        {
            var level = new MenuItem { Header = "Fade to" };
            foreach (double percent in FloatBehavior.IdleChoices)
            {
                AddToggle(level, $"{percent:0}%", Math.Abs(FloatBehavior.ClampIdleOpacity(item.FloatIdleOpacity) - percent) < 0.5, _ =>
                {
                    item.FloatIdleOpacity = percent;
                    ChangedFloat($"{ItemName(item)} fades to {percent:0}%.");
                });
            }
            place.Items.Add(level);
        }
        AddToggle(place, "Let clicks pass through", item.FloatClickThrough, value =>
        {
            item.FloatClickThrough = value;
            ChangedFloat(value ? $"{ItemName(item)} lets clicks through. Hold Ctrl with the pointer over it to use it again." : $"{ItemName(item)} takes clicks again.");
        }).ToolTip = "Clicks go to the window underneath, so the widget never gets in your way. To use it, hold Ctrl with the pointer over it; then right-click it to turn this off.";
    }

    private void ChangedFloat(string status)
    {
        hub.Save();
        hub.Floats.Sync(false);
        SetStatus(status);
    }

    /// <summary>"Size ›": Small, Medium, Large and Default, worked out from the widget's own default and smallest size.</summary>
    private void AddSizeMenu(ItemsControl place, LauncherItem item)
    {
        if (item.GroupId.Length > 0) return;
        bool locked = !item.OnTop && item.Locked;
        var size = new MenuItem { Header = "Size" };
        var (currentW, currentH) = item.OnTop ? FloatLayer.SizeFor(item) : SizeOf(item);
        foreach (var preset in SizePresets.All)
        {
            var (w, h) = PresetSize(item, preset);
            var entry = AddMenuItem(size, SizePresets.Label(preset), () => ApplySize(item, preset));
            entry.IsEnabled = !locked && (Math.Abs(w - currentW) > 0.5 || Math.Abs(h - currentH) > 0.5);
        }
        if (locked) size.ToolTip = "Unlock the position to change the size.";
        place.Items.Add(size);
    }

    /// <summary>A preset's size for this widget, kept within the display (floating) or the deck (on a deck).</summary>
    private (double Width, double Height) PresetSize(LauncherItem item, SizePreset preset)
    {
        double roomW = canvas.ActualWidth, roomH = canvas.ActualHeight;
        if (item.OnTop && hub.Displays.Count > 0)
        {
            var display = DisplayMap.Resolve(item.FloatDisplay, hub.Displays);
            roomW = display.WorkArea.Width / display.Scale;
            roomH = display.WorkArea.Height / display.Scale;
        }
        return SizePresets.For(preset, ItemKinds.MinimumSize(item.Kind), ItemKinds.DefaultSize(item.Kind), roomW, roomH);
    }

    private void ApplySize(LauncherItem item, SizePreset preset)
    {
        var (w, h) = PresetSize(item, preset);
        string done = $"{ItemName(item)} is now {SizePresets.Label(preset).ToLowerInvariant()}.";
        if (item.OnTop)
        {
            item.FloatWidth = w;
            item.FloatHeight = h;
            hub.Save();
            hub.Floats.Sync(false);
            SetStatus(done);
            return;
        }
        if (item.Locked || !rects.TryGetValue(item.Id, out var current)) return;
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        var others = OccupiedExcept(item.Id);
        var wanted = current with { Width = w, Height = h };
        // Grow or shrink in place when it fits; otherwise the nearest free spot for the new size, or nothing at all.
        if (!DeckLayout.IsAllowed(wanted, width, height, settings.KeepCenterClear) || others.Any(o => DeckLayout.Overlaps(wanted, o)))
        {
            if (DeckLayout.FindSpace(current.X, current.Y, w, h, width, height, others, settings.KeepCenterClear) is not { } spot) { SetStatus(NoRoom); return; }
            wanted = wanted with { X = spot.X, Y = spot.Y };
        }
        rects[item.Id] = wanted;
        Store(item, wanted);
        // "Default" follows the kind's default from now on (no size of its own).
        if (preset == SizePreset.Default) item.Width = item.Height = 0;
        SaveNow();
        SetStatus(done);
        hub.RenderAll();
    }
}
