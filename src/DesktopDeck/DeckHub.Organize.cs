using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

// Organizing widgets: the gallery's favorites and recents, widget groups, and starter layouts (templates).
internal sealed partial class DeckHub
{
    // ---------- The gallery ----------

    /// <summary>Opens the control center on Widgets with the "+ Add" gallery for a monitor's deck (typing works there). False when that isn't possible.</summary>
    public bool ShowAddGallery(DisplayInfo? target = null)
    {
        if (closing || Displays.Count == 0 || Primary is null) return false;
        OpenControlCenter("widgets");
        if (center is null) return false;
        center.ShowGallery(target ?? PrimaryDisplay);
        return true;
    }

    /// <summary>Stars or un-stars a widget kind in the gallery; returns whether it is a favorite now.</summary>
    public bool ToggleFavoriteWidget(string kind)
    {
        bool on = WidgetOrganizer.ToggleFavorite(Settings, kind);
        Save();
        return on;
    }

    /// <summary>A widget of this kind was just added from the gallery.</summary>
    public void NoteRecentWidget(string kind)
    {
        WidgetOrganizer.NoteRecent(Settings, kind);
        Save();
    }

    // ---------- Groups ----------

    private static string NameOf(LauncherItem item) => string.IsNullOrWhiteSpace(item.Name) ? "The widget" : item.Name;

    /// <summary>Stores where a widget that was just put back on a deck is drawn now (it may have been moved to a free spot).</summary>
    private void RememberPlacement(LauncherItem item)
    {
        if (!item.OnTop) backgrounds.GetValueOrDefault(DisplayOf(item).Id)?.RememberItem(item);
    }

    /// <summary>A new group card where the widget is, holding it as the first tab.</summary>
    public LauncherItem? NewGroupWith(LauncherItem item, string name)
    {
        var group = WidgetOrganizer.NewGroupAround(Settings, item, name);
        if (group is null) return null;
        RenderAll();
        Save();
        SetStatus($"Made the group \"{group.Name}\" with {NameOf(item)}. Right-click another widget → Add to group to put it in too.");
        return group;
    }

    public bool AddToGroup(LauncherItem item, LauncherItem group)
    {
        if (!WidgetOrganizer.AddToGroup(Settings, item, group)) return false;
        RenderAll();
        Save();
        SetStatus($"{NameOf(item)} is now a tab of \"{group.Name}\".");
        return true;
    }

    public bool RemoveFromGroup(LauncherItem child)
    {
        var group = WidgetOrganizer.GroupOf(Settings, child);
        if (!WidgetOrganizer.Release(Settings, child)) return false;
        RenderAll();
        RememberPlacement(child);
        Save();
        SetStatus($"{NameOf(child)} is back on its own{(group is null ? "" : $", out of \"{group.Name}\"")}.");
        return true;
    }

    public void RenameGroup(LauncherItem group, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0 || !WidgetOrganizer.IsGroup(group)) return;
        group.Name = name.Length > WidgetOrganizer.MaxGroupName ? name[..WidgetOrganizer.MaxGroupName].TrimEnd() : name;
        RenderAll();
        Save();
    }

    /// <summary>Removes a group card; its widgets go back to the deck (or the floating layer), shown.</summary>
    public void DissolveGroup(LauncherItem group)
    {
        if (!WidgetOrganizer.IsGroup(group)) return;
        var released = WidgetOrganizer.Dissolve(Settings, group);
        RenderAll();
        foreach (var child in released) RememberPlacement(child);
        Save();
        SetStatus(released.Count == 0 ? $"Removed the group \"{group.Name}\"." : $"Removed the group \"{group.Name}\". Its {released.Count} widget{(released.Count == 1 ? " is" : "s are")} back on the {(group.OnTop ? "pinned layer" : "desktop")}.");
    }

    // ---------- Starter layouts ----------

    /// <summary>
    /// "Start from a template": adds a new layout of the layer with the template's widgets placed on the main display (and creates those widgets). The
    /// widgets are hidden everywhere else, and nothing changes on screen until the layout is switched on. Null when there is no screen to place on.
    /// </summary>
    public LayoutProfile? CreateTemplateLayout(WidgetTemplate template, string layer)
    {
        if (!LayoutLayers.IsValid(layer) || Displays.Count == 0) return null;
        bool floating = layer == LayoutLayers.Floating;
        var (display, width, height, items, spots) = PlanTemplate(template, floating);

        LayoutEngine.CaptureActive(Settings);
        var layout = new LayoutProfile { Layer = layer, Name = template.Name, Icon = template.Icon };
        if (!floating && LayoutEngine.Active(Settings, LayoutLayers.Background) is { } current) layout.Backgrounds = current.Copy().Backgrounds;
        int placed = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is not { } item || spots[i] is not { } spot) continue;
            var (dw, dh) = ItemKinds.DefaultSize(item.Kind);
            double w = Math.Abs(spot.Width - dw) < 0.5 ? 0 : spot.Width, h = Math.Abs(spot.Height - dh) < 0.5 ? 0 : spot.Height;
            double x = Math.Clamp(spot.X / width, 0, 1), y = Math.Clamp(spot.Y / height, 0, 1);
            item.Hidden = true;
            item.OnTop = floating;
            if (floating) { item.FloatDisplay = display.Id; item.FloatX = x; item.FloatY = y; item.FloatWidth = w; item.FloatHeight = h; }
            else { item.Display = display.IsPrimary ? "" : display.Id; item.X = x; item.Y = y; item.Width = w; item.Height = h; }
            layout.Entries[item.Id] = new LayoutEntry
            {
                Visible = true, Display = floating ? display.Id : item.Display, X = x, Y = y, Width = w, Height = h,
            };
            Settings.Items.Add(item);
            placed++;
        }
        Settings.Layouts.Add(layout);
        LayoutEngine.Rename(Settings, layout.Id, template.Name);
        int missed = template.Kinds.Length - placed;
        RenderAll();
        LayoutsChanged($"Added the layout \"{layout.Name}\" with {placed} widget{(placed == 1 ? "" : "s")}{(missed > 0 ? $" ({missed} didn't fit on this screen)" : "")}. Switch to it from the control center → Layouts.");
        return layout;
    }

    /// <summary>Creates a template's widgets and finds their places on the main display: the display, the size laid out in, the new widgets (null for a kind that doesn't exist) and a spot per widget (null: no room).</summary>
    private (DisplayInfo Display, double Width, double Height, List<LauncherItem?> Items, List<TileRect?> Spots) PlanTemplate(WidgetTemplate template, bool floating)
    {
        var display = PrimaryDisplay;
        double scale = Math.Max(0.5, display.Scale);
        double width = display.WorkArea.Width / scale, height = display.WorkArea.Height / scale;
        var reserved = new List<TileRect>();
        if (!floating && Primary is { } deck)
        {
            var (w, h, areas) = deck.TemplateViewport();
            if (w >= 100 && h >= 100) { width = w; height = h; reserved = areas; }
        }
        var items = template.Kinds.Select(MainWindow.NewItemOfKind).ToList();
        var slots = items.Select(item =>
        {
            var (dw, dh) = ItemKinds.DefaultSize(item?.Kind ?? "");
            var (mw, mh) = ItemKinds.MinimumSize(item?.Kind ?? "");
            return new TemplateSlot(dw, dh, Math.Min(dw, mw), Math.Min(dh, mh));
        }).ToList();
        var spots = TemplatePlacer.Place(slots, width, height, reserved, !floating && Settings.KeepCenterClear, DeckLayout.Grid);
        return (display, width, height, items, spots);
    }
}
