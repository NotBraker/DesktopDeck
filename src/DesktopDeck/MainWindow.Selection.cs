using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Shapes = System.Windows.Shapes;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

// Edit layout with several widgets at once: Ctrl/Shift-click and a selection box to select, drag or nudge them together,
// and a small toolbar to align, distribute, resize, hide, lock, remove and customize them together.
public sealed partial class MainWindow
{
    private static readonly Brush SelectionTint = Theme.Tint(Theme.PrimaryHex, 0.2);
    private readonly List<string> selection = new();
    private readonly Dictionary<string, (Border Overlay, LauncherItem Item)> overlays = new();
    private readonly Shapes.Rectangle marquee = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Fill = SelectionTint, Stroke = Theme.Primary, StrokeThickness = 1 };
    private readonly Border selectionBar = new()
    {
        Background = Theme.Surface, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
        Padding = new Thickness(5), Visibility = Visibility.Collapsed,
        Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.5, Color = Colors.Black },
    };
    private readonly DispatcherTimer nudgeTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private Point marqueeStart;
    private bool marqueeActive, marqueeMoved, marqueeAdditive;
    private List<string> marqueeBase = new();
    private double gridBrushFor = double.NaN;

    /// <summary>The widgets being moved together by one drag (the locked ones stay where they are).</summary>
    private sealed class GroupDrag
    {
        public required List<(LauncherItem Item, Grid View, TileRect Origin)> Members;
        public required HashSet<string> Ids;
        public double Dx, Dy;
    }

    /// <summary>Called once by the constructor, after the chrome exists.</summary>
    private void InitSelection()
    {
        Panel.SetZIndex(marquee, 6);
        canvas.Children.Add(marquee);
        Panel.SetZIndex(selectionBar, 7);
        selectionBar.MouseLeftButtonDown += (_, e) => e.Handled = true;
        canvas.Children.Add(selectionBar);
        canvas.Focusable = true;
        canvas.MouseLeftButtonDown += OnCanvasDown;
        canvas.MouseMove += OnCanvasMove;
        canvas.MouseLeftButtonUp += OnCanvasUp;
        canvas.LostMouseCapture += (_, _) => EndMarquee(false);
        nudgeTimer.Tick += (_, _) =>
        {
            nudgeTimer.Stop();
            SaveNow();
            hub.RenderAll();
        };
    }

    /// <summary>The grid size and gap come from the settings; the dots follow the grid size.</summary>
    private void ApplyGridSettings()
    {
        DeckLayout.Configure(settings.GridSize, settings.GridGap);
        if (gridBrushFor == DeckLayout.Grid) return;
        gridLayer.Fill = GridBrush();
        gridBrushFor = DeckLayout.Grid;
    }

    // ---------- The selection ----------

    private bool IsSelected(string id) => selection.Contains(id);

    private void SetSelection(IEnumerable<string> ids)
    {
        var next = ids.Distinct().ToList();
        if (next.SequenceEqual(selection)) return;
        selection.Clear();
        selection.AddRange(next);
        RefreshSelection();
    }

    private void ClearSelection() => SetSelection([]);

    private void ToggleSelection(string id)
    {
        if (!selection.Remove(id)) selection.Add(id);
        RefreshSelection();
    }

    private void RefreshSelection()
    {
        foreach (var (id, entry) in overlays) StyleOverlay(entry.Overlay, entry.Item, IsSelected(id));
        UpdateSelectionChrome();
    }

    private static void StyleOverlay(Border overlay, LauncherItem item, bool selected)
    {
        overlay.BorderBrush = selected ? Theme.Primary : Theme.Tint(item.Accent, 0.6);
        overlay.BorderThickness = new Thickness(selected ? 3 : 1.5);
        overlay.Background = selected ? SelectionTint : Brushes.Transparent;
    }

    /// <summary>Called for each edit overlay as it is built.</summary>
    private void RegisterOverlay(LauncherItem item, Border overlay)
    {
        overlays[item.Id] = (overlay, item);
        if (IsSelected(item.Id)) StyleOverlay(overlay, item, true);
    }

    /// <summary>Ctrl or Shift toggles the widget in the selection. Returns true when the click was only a selection click.</summary>
    private bool SelectionClick(LauncherItem item)
    {
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
        {
            ToggleSelection(item.Id);
            return true;
        }
        if (!IsSelected(item.Id)) SetSelection([item.Id]);
        return false;
    }

    /// <summary>A plain click (no movement) on a widget that is part of a bigger selection selects just that one.</summary>
    private void ClickedWithoutMoving(LauncherItem item)
    {
        if (selection.Count > 1 && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0) SetSelection([item.Id]);
    }

    /// <summary>After a redraw: forget widgets that are gone (hidden, removed, moved to another monitor) and show the toolbar again.</summary>
    private void AfterRenderSelection()
    {
        if (!IsEditing) selection.Clear();
        else selection.RemoveAll(id => !rects.ContainsKey(id));
        UpdateSelectionChrome();
        if (IsEditing && selection.Count > 0 && Keyboard.FocusedElement is null && IsActive) canvas.Focus();
    }

    private IEnumerable<LauncherItem> SelectedItems() =>
        selection.Select(id => settings.Items.FirstOrDefault(i => i.Id == id)).OfType<LauncherItem>().Where(i => rects.ContainsKey(i.Id));

    /// <summary>The selected widgets that may move: locked ones keep their place.</summary>
    private List<LauncherItem> MovableSelected() => SelectedItems().Where(i => !i.Locked).ToList();

    private TileRect? SelectionBounds()
    {
        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
        bool any = false;
        foreach (var item in SelectedItems())
        {
            var rect = rects[item.Id];
            l = Math.Min(l, rect.X); t = Math.Min(t, rect.Y); r = Math.Max(r, rect.Right); b = Math.Max(b, rect.Bottom);
            any = true;
        }
        return any ? new TileRect(l, t, r - l, b - t) : null;
    }

    private List<TileRect> OccupiedExceptSet(ICollection<string> ids) =>
        Reserved().Concat(rects.Where(p => !ids.Contains(p.Key)).Select(p => p.Value)).ToList();

    private bool FitsAll(IReadOnlyDictionary<string, TileRect> proposed, List<TileRect> others)
    {
        double width = canvas.ActualWidth, height = canvas.ActualHeight;
        var list = proposed.Values.ToList();
        for (int i = 0; i < list.Count; i++)
        {
            if (!DeckLayout.IsAllowed(list[i], width, height, settings.KeepCenterClear) || others.Any(o => DeckLayout.Overlaps(list[i], o))) return false;
            for (int j = 0; j < i; j++) if (DeckLayout.Overlaps(list[i], list[j])) return false;
        }
        return true;
    }

    // ---------- Selecting with a box on the empty space ----------

    private void OnCanvasDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEditing || e.OriginalSource != canvas || marqueeActive) return;
        marqueeStart = e.GetPosition(canvas);
        marqueeAdditive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        marqueeBase = marqueeAdditive ? selection.ToList() : new List<string>();
        marqueeActive = true;
        marqueeMoved = false;
        canvas.Focus();
        canvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnCanvasMove(object sender, MouseEventArgs e)
    {
        if (!marqueeActive) return;
        var point = e.GetPosition(canvas);
        if (!marqueeMoved && (point - marqueeStart).Length < 4) return;
        marqueeMoved = true;
        var box = new Rect(marqueeStart, point);
        marquee.Visibility = Visibility.Visible;
        Canvas.SetLeft(marquee, box.X);
        Canvas.SetTop(marquee, box.Y);
        marquee.Width = box.Width;
        marquee.Height = box.Height;
        var hit = rects.Where(p => new Rect(p.Value.X, p.Value.Y, p.Value.Width, p.Value.Height).IntersectsWith(box)).Select(p => p.Key);
        SetSelection(marqueeBase.Concat(hit));
    }

    private void OnCanvasUp(object sender, MouseButtonEventArgs e)
    {
        if (!marqueeActive) return;
        EndMarquee(true);
        e.Handled = true;
    }

    private void EndMarquee(bool completed)
    {
        if (!marqueeActive) return;
        marqueeActive = false;
        marquee.Visibility = Visibility.Collapsed;
        if (canvas.IsMouseCaptured) canvas.ReleaseMouseCapture();
        // A plain click on the empty space clears the selection.
        if (completed && !marqueeMoved && !marqueeAdditive) ClearSelection();
    }

    // ---------- Keys ----------

    /// <summary>Esc clears the selection first, arrows nudge it, Ctrl+A selects everything, Delete removes it. True when the key was used.</summary>
    private bool HandleEditKey(KeyEventArgs e)
    {
        if (!IsEditing) return false;
        bool typing = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox;
        var mods = Keyboard.Modifiers;
        if (e.Key == Key.Escape && selection.Count > 0) { ClearSelection(); e.Handled = true; return true; }
        if (typing || InModal) return false;
        switch (e.Key)
        {
            case Key.Left: Nudge(-1, 0, mods); break;
            case Key.Right: Nudge(1, 0, mods); break;
            case Key.Up: Nudge(0, -1, mods); break;
            case Key.Down: Nudge(0, 1, mods); break;
            case Key.A when mods == ModifierKeys.Control: SetSelection(rects.Keys); break;
            case Key.Delete when selection.Count > 1: RemoveSelection(); break;
            default: return false;
        }
        e.Handled = true;
        return true;
    }

    /// <summary>Moves the selected widgets one grid step (Shift = one pixel, Ctrl = four steps). Nothing moves if that would not fit.</summary>
    private void Nudge(int dx, int dy, ModifierKeys mods)
    {
        var items = MovableSelected();
        if (items.Count == 0) return;
        double step = (mods & ModifierKeys.Shift) != 0 ? 1 : DeckLayout.Grid * ((mods & ModifierKeys.Control) != 0 ? 4 : 1);
        var proposed = items.ToDictionary(i => i.Id, i => rects[i.Id] with { X = rects[i.Id].X + dx * step, Y = rects[i.Id].Y + dy * step });
        if (!FitsAll(proposed, OccupiedExceptSet(proposed.Keys.ToList()))) return;
        foreach (var item in items)
        {
            var rect = proposed[item.Id];
            rects[item.Id] = rect;
            Store(item, rect);
            if (views.TryGetValue(item.Id, out var view)) { Canvas.SetLeft(view, rect.X); Canvas.SetTop(view, rect.Y); }
        }
        UpdateSelectionChrome();
        // Saved and shown on the other layers once the keys are let go (and saved on exit if that comes first).
        saveTimer.Stop();
        saveTimer.Start();
        nudgeTimer.Stop();
        nudgeTimer.Start();
    }

    // ---------- Moving the selection together ----------

    private GroupDrag? StartGroupDrag(LauncherItem leader, bool resize)
    {
        if (resize || selection.Count < 2 || !IsSelected(leader.Id)) return null;
        var members = MovableSelected().Where(i => views.ContainsKey(i.Id)).Select(i => (i, views[i.Id], rects[i.Id])).ToList();
        if (members.Count < 2) return null;
        foreach (var (_, view, _) in members) Panel.SetZIndex(view, 2);
        selectionBar.Visibility = Visibility.Collapsed;
        return new GroupDrag { Members = members, Ids = members.Select(m => m.i.Id).ToHashSet() };
    }

    /// <summary>Moves the whole block with the pointer, keeping the widgets' places relative to each other. If a spot doesn't fit it stops at the last one that does.</summary>
    private void MoveGroup(Point point)
    {
        var state = drag!;
        var group = state.Group!;
        double grid = GridStep;
        double Snap(double v) => grid > 0 ? Math.Round(v / grid) * grid : v;
        var leader = state.Origin;
        double dx = Snap(leader.X + (point.X - state.Start.X)) - leader.X, dy = Snap(leader.Y + (point.Y - state.Start.Y)) - leader.Y;
        var others = OccupiedExceptSet(group.Ids);
        Dictionary<string, TileRect>? Fits(double x, double y)
        {
            var map = group.Members.ToDictionary(m => m.Item.Id, m => m.Origin with { X = m.Origin.X + x, Y = m.Origin.Y + y });
            return FitsAll(map, others) ? map : null;
        }
        var found = Fits(dx, dy);
        double ax = dx, ay = dy;
        if (found is null)
        {
            // Slide from the last good place toward the pointer and stop where it no longer fits...
            ax = group.Dx;
            ay = group.Dy;
            double lo = 0, hi = 1;
            for (int i = 0; i < 8; i++)
            {
                double mid = (lo + hi) / 2;
                double mx = Snap(leader.X + group.Dx + (dx - group.Dx) * mid) - leader.X, my = Snap(leader.Y + group.Dy + (dy - group.Dy) * mid) - leader.Y;
                if (Fits(mx, my) is { } ok) { lo = mid; found = ok; ax = mx; ay = my; }
                else hi = mid;
            }
            // ...then keep following the pointer along whichever axis still has room (sliding along a wall).
            if (Fits(dx, ay) is { } alongX) { found = alongX; ax = dx; }
            else if (Fits(ax, dy) is { } alongY) { found = alongY; ay = dy; }
        }
        if (found is null) return;
        group.Dx = ax;
        group.Dy = ay;
        foreach (var (item, view, _) in group.Members)
        {
            var rect = found[item.Id];
            rects[item.Id] = rect;
            Canvas.SetLeft(view, rect.X);
            Canvas.SetTop(view, rect.Y);
        }
    }

    private void FinishGroupDrag(DragState state, bool commit)
    {
        var group = state.Group!;
        foreach (var (item, view, origin) in group.Members)
        {
            Panel.SetZIndex(view, 0);
            if (commit) continue;
            rects[item.Id] = origin;
            Canvas.SetLeft(view, origin.X);
            Canvas.SetTop(view, origin.Y);
        }
        if (commit)
        {
            foreach (var (item, _, _) in group.Members) Store(item, rects[item.Id]);
            SaveNow();
            Dispatcher.BeginInvoke(new Action(hub.RenderAll), DispatcherPriority.Background);
        }
        UpdateSelectionChrome();
    }

    // ---------- The toolbar ----------

    private const string IconLeft = "M1,1 H2.5 V15 H1 Z M5,3 H14 V6 H5 Z M5,9 H10 V12 H5 Z";
    private const string IconRight = "M13.5,1 H15 V15 H13.5 Z M2,3 H11 V6 H2 Z M6,9 H11 V12 H6 Z";
    private const string IconTop = "M1,1 H15 V2.5 H1 Z M3,5 H6 V14 H3 Z M9,5 H12 V10 H9 Z";
    private const string IconBottom = "M1,13.5 H15 V15 H1 Z M3,2 H6 V11 H3 Z M9,6 H12 V11 H9 Z";
    private const string IconSpreadH = "M1,1 H2.5 V15 H1 Z M13.5,1 H15 V15 H13.5 Z M5.5,4 H10.5 V12 H5.5 Z";
    private const string IconSpreadV = "M1,1 H15 V2.5 H1 Z M1,13.5 H15 V15 H1 Z M4,5.5 H12 V10.5 H4 Z";
    private const string IconSameW = "M1,2 H2.5 V14 H1 Z M13.5,2 H15 V14 H13.5 Z M4.5,7.25 H11.5 V8.75 H4.5 Z M6,5.5 L3.5,8 L6,10.5 Z M10,5.5 L12.5,8 L10,10.5 Z";
    private const string IconSameH = "M2,1 H14 V2.5 H2 Z M2,13.5 H14 V15 H2 Z M7.25,4.5 H8.75 V11.5 H7.25 Z M5.5,6 L8,3.5 L10.5,6 Z M5.5,10 L8,12.5 L10.5,10 Z";

    private void UpdateSelectionChrome()
    {
        var bounds = SelectionBounds();
        var items = SelectedItems().ToList();
        if (!IsEditing || items.Count < 2 || bounds is not { } box || canvas.ActualWidth < 1 || (drag?.Group is not null))
        {
            selectionBar.Visibility = Visibility.Collapsed;
            return;
        }
        selectionBar.Child = BuildSelectionBar(items);
        selectionBar.Visibility = Visibility.Visible;
        selectionBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = selectionBar.DesiredSize;
        double x = Math.Clamp(box.X + box.Width / 2 - size.Width / 2, 8, Math.Max(8, canvas.ActualWidth - size.Width - 8));
        double y = box.Y - size.Height - 8;
        if (y < ChromeTop + ChromeHeight + 8) y = box.Bottom + 8;
        y = Math.Clamp(y, 8, Math.Max(8, canvas.ActualHeight - size.Height - 8));
        Canvas.SetLeft(selectionBar, x);
        Canvas.SetTop(selectionBar, y);
    }

    private FrameworkElement BuildSelectionBar(List<LauncherItem> items)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var count = Theme.Label($"{items.Count} selected", 12, Theme.Text2);
        count.Margin = new Thickness(8, 0, 8, 0);
        row.Children.Add(count);
        int movable = items.Count(i => !i.Locked);
        row.Children.Add(BarSeparator());
        row.Children.Add(BarIcon(IconLeft, "Align left edges", () => Align(left: true), movable >= 2));
        row.Children.Add(BarIcon(IconRight, "Align right edges", () => Align(right: true), movable >= 2));
        row.Children.Add(BarIcon(IconTop, "Align top edges", () => Align(top: true), movable >= 2));
        row.Children.Add(BarIcon(IconBottom, "Align bottom edges", () => Align(bottom: true), movable >= 2));
        row.Children.Add(BarSeparator());
        row.Children.Add(BarIcon(IconSpreadH, "Distribute horizontally (equal gaps; needs 3 or more)", () => Distribute(true), movable >= 3));
        row.Children.Add(BarIcon(IconSpreadV, "Distribute vertically (equal gaps; needs 3 or more)", () => Distribute(false), movable >= 3));
        row.Children.Add(BarSeparator());
        row.Children.Add(BarIcon(IconSameW, "Same width as the first one selected", () => SameSize(true), movable >= 2));
        row.Children.Add(BarIcon(IconSameH, "Same height as the first one selected", () => SameSize(false), movable >= 2));
        row.Children.Add(BarSeparator());
        row.Children.Add(BarText("Hide", "Take these widgets off the screen. The control center → Widgets shows them again.", () => hub.HideItems(items)));
        bool allLocked = items.All(i => i.Locked);
        row.Children.Add(BarText(allLocked ? "Unlock" : "Lock", allLocked ? "Let these widgets move again." : "Fix these widgets in place.", () => LockSelection(!allLocked)));
        var remove = BarText("Remove", "Delete these widgets (asks first).", RemoveSelection);
        ((TextBlock)remove.Child).Foreground = Theme.Danger;
        row.Children.Add(remove);
        row.Children.Add(BarSeparator());
        var together = BarText("Customize together", "Change the look of all of them at once.", CustomizeSelection);
        together.Background = Theme.Primary;
        ((TextBlock)together.Child).Foreground = Theme.OnPrimary;
        row.Children.Add(together);
        return row;
    }

    private static Border BarSeparator() => new() { Width = 1, Margin = new Thickness(5, 5, 5, 5), Background = Theme.Line };

    private static Border BarButtonShell(string tip, Action run, bool enabled)
    {
        var shell = new Border { CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Margin = new Thickness(1, 0, 1, 0), Cursor = enabled ? Cursors.Hand : Cursors.Arrow, ToolTip = tip, Opacity = enabled ? 1 : 0.35 };
        AutomationProperties.SetName(shell, tip);
        if (!enabled) return shell;
        shell.MouseEnter += (_, _) => shell.Background = Theme.Surface3;
        shell.MouseLeave += (_, _) => shell.Background = shell.Tag as Brush ?? Brushes.Transparent;
        shell.MouseLeftButtonUp += (_, e) => { e.Handled = true; run(); };
        return shell;
    }

    private static Border BarIcon(string data, string tip, Action run, bool enabled)
    {
        var shell = BarButtonShell(tip, run, enabled);
        shell.Width = shell.Height = 30;
        shell.Child = new Shapes.Path { Data = Geometry.Parse(data), Fill = Theme.Text2, Width = 16, Height = 16, Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        return shell;
    }

    private static Border BarText(string text, string tip, Action run)
    {
        var shell = BarButtonShell(tip, run, true);
        shell.Height = 30;
        shell.Padding = new Thickness(10, 0, 10, 0);
        var label = Theme.Label(text, 12, Theme.Text);
        label.FontWeight = FontWeights.SemiBold;
        shell.Child = label;
        return shell;
    }

    // ---------- What the toolbar does ----------

    private const string NoRoom = "That doesn't fit: the widgets would overlap or leave the screen. Move them apart or make room first.";

    /// <summary>Applies new places or sizes to selected widgets, all or nothing.</summary>
    private void CommitTargets(Dictionary<string, TileRect> targets, string done)
    {
        if (targets.Count == 0) return;
        if (targets.All(t => rects[t.Key] == t.Value)) { SetStatus("They already are."); return; }
        if (!FitsAll(targets, OccupiedExceptSet(targets.Keys.ToList()))) { SetStatus(NoRoom); return; }
        foreach (var item in SelectedItems().Where(i => targets.ContainsKey(i.Id)))
        {
            rects[item.Id] = targets[item.Id];
            Store(item, targets[item.Id]);
        }
        SaveNow();
        SetStatus(done);
        Dispatcher.BeginInvoke(new Action(hub.RenderAll), DispatcherPriority.Background);
    }

    private void Align(bool left = false, bool right = false, bool top = false, bool bottom = false)
    {
        var items = MovableSelected();
        if (items.Count < 2) return;
        var all = items.Select(i => rects[i.Id]).ToList();
        double minX = all.Min(r => r.X), maxRight = all.Max(r => r.Right), minY = all.Min(r => r.Y), maxBottom = all.Max(r => r.Bottom);
        var targets = items.ToDictionary(i => i.Id, i =>
        {
            var r = rects[i.Id];
            if (left) return r with { X = minX };
            if (right) return r with { X = maxRight - r.Width };
            if (top) return r with { Y = minY };
            return r with { Y = maxBottom - r.Height };
        });
        CommitTargets(targets, left ? "Aligned to the left." : right ? "Aligned to the right." : top ? "Aligned to the top." : "Aligned to the bottom.");
    }

    private void Distribute(bool horizontal)
    {
        var items = MovableSelected();
        if (items.Count < 3) return;
        var ordered = items.OrderBy(i => horizontal ? rects[i.Id].X : rects[i.Id].Y).ToList();
        var first = rects[ordered[0].Id];
        var last = rects[ordered[^1].Id];
        double span = horizontal ? last.Right - first.X : last.Bottom - first.Y;
        double used = ordered.Sum(i => horizontal ? rects[i.Id].Width : rects[i.Id].Height);
        double gap = (span - used) / (ordered.Count - 1);
        Dictionary<string, TileRect> Build(bool snap)
        {
            var map = new Dictionary<string, TileRect>();
            double at = horizontal ? first.X : first.Y;
            foreach (var item in ordered)
            {
                var r = rects[item.Id];
                double pos = snap && settings.SnapToGrid ? Math.Round(at / DeckLayout.Grid) * DeckLayout.Grid : at;
                map[item.Id] = horizontal ? r with { X = pos } : r with { Y = pos };
                at += (horizontal ? r.Width : r.Height) + gap;
            }
            // The ends stay where they are.
            map[ordered[0].Id] = first;
            map[ordered[^1].Id] = last;
            return map;
        }
        var snapped = Build(true);
        var targets = settings.SnapToGrid && FitsAll(snapped, OccupiedExceptSet(snapped.Keys.ToList())) ? snapped : Build(false);
        CommitTargets(targets, horizontal ? "Spread out evenly, left to right." : "Spread out evenly, top to bottom.");
    }

    private void SameSize(bool width)
    {
        var items = MovableSelected();
        if (items.Count < 2) return;
        // The first widget selected sets the size (if it is locked, the first one that can change does).
        var anchor = rects[(SelectedItems().FirstOrDefault(i => !i.Locked) ?? items[0]).Id];
        var targets = items.ToDictionary(i => i.Id, i =>
        {
            var (minW, minH) = ItemKinds.MinimumSize(i.Kind);
            var r = rects[i.Id];
            return width ? r with { Width = Math.Max(minW, anchor.Width) } : r with { Height = Math.Max(minH, anchor.Height) };
        });
        CommitTargets(targets, width ? "Same width." : "Same height.");
    }

    private void LockSelection(bool locked)
    {
        var items = SelectedItems().ToList();
        foreach (var item in items) item.Locked = locked;
        SaveNow();
        hub.RenderAll();
        SetStatus(locked ? $"Locked {items.Count} widgets in place." : $"{items.Count} widgets can move again.");
    }

    private void RemoveSelection()
    {
        var items = SelectedItems().ToList();
        if (items.Count == 0) return;
        if (!Confirm($"Remove {items.Count} widgets? Their settings and anything saved in them are deleted.", "Remove")) return;
        EndDrag(false);
        foreach (var item in items)
        {
            settings.Items.Remove(item);
            CleanupMedia(item);
        }
        selection.Clear();
        hub.RenderAll();
        SaveNow();
        SetStatus($"Removed {items.Count} widgets.");
    }

    private void CustomizeSelection()
    {
        var items = SelectedItems().ToList();
        if (items.Count < 2) return;
        EndDrag(false);
        var editor = new ItemEditor(DialogOwner, items, settings);
        if (Modal(editor.ShowDialog) != true || editor.GroupResult is not { } changes) return;
        foreach (var item in items) changes.ApplyTo(item);
        hub.RenderAll();
        SaveNow();
        SetStatus($"Customized {items.Count} widgets.");
    }

    /// <summary>
    /// Smoke test (--select-test): runs the selection operations on the sample deck and reports what each did. Uses the isolated
    /// preview settings only. <paramref name="snap"/> saves a picture of the deck under a file name.
    /// </summary>
    internal string ExerciseSelectionForSmoke(Action<string> snap)
    {
        var log = new List<string>();
        void Check(bool ok, string what) => log.Add((ok ? "ok   " : "FAIL ") + what);
        LauncherItem Named(string name) => settings.Items.First(i => i.Name == name);
        bool Disjoint(IEnumerable<string> ids) => ids.All(a => ids.Where(b => b != a).All(b => !DeckLayout.Overlaps(rects[a], rects[b])));

        // Moving with the arrow keys: one grid step, one pixel with Shift, refused when it would not fit.
        var row = new[] { Named("Time"), Named("Daily bits"), Named("Trip to Japan") };
        SetSelection(row.Select(i => i.Id));
        var before = row.ToDictionary(i => i.Id, i => rects[i.Id]);
        Nudge(1, 0, ModifierKeys.None);
        Check(row.All(i => rects[i.Id].X == before[i.Id].X + DeckLayout.Grid && rects[i.Id].Y == before[i.Id].Y), "arrow moves the selection one grid step together");
        Nudge(-1, 0, ModifierKeys.Shift);
        Check(row.All(i => rects[i.Id].X == before[i.Id].X + DeckLayout.Grid - 1), "Shift+arrow moves one pixel");
        Nudge(0, -1, ModifierKeys.Control);
        Check(row.All(i => rects[i.Id].Y == before[i.Id].Y), "a move that would leave the screen or hit the controls is refused");
        foreach (var item in row) { rects[item.Id] = before[item.Id]; Store(item, before[item.Id]); }

        // Aligning things that would then overlap is refused and changes nothing.
        var pair = new[] { Named("Time"), Named("Now playing") };
        SetSelection(pair.Select(i => i.Id));
        var pairBefore = pair.ToDictionary(i => i.Id, i => rects[i.Id]);
        Align(left: true);
        Check(pair.All(i => rects[i.Id] == pairBefore[i.Id]), "align that would collide changes nothing");

        // Three counters in a loose row: align, same size and distribute.
        var added = new[]
        {
            new LauncherItem { Kind = ItemKinds.Counter, Name = "T1", X = 80 / canvas.ActualWidth, Y = 700 / canvas.ActualHeight, Width = 180, Height = 108 },
            new LauncherItem { Kind = ItemKinds.Counter, Name = "T2", X = 420 / canvas.ActualWidth, Y = 756 / canvas.ActualHeight, Width = 252, Height = 108 },
            new LauncherItem { Kind = ItemKinds.Counter, Name = "T3", X = 1000 / canvas.ActualWidth, Y = 728 / canvas.ActualHeight, Width = 180, Height = 108 },
        };
        foreach (var item in added) settings.Items.Add(item);
        // Clear the bottom of the sample deck for them.
        var cleared = settings.Items.Where(i => i.Name is "This week" or "Agenda" && !i.Hidden).ToList();
        foreach (var item in cleared) item.Hidden = true;
        hub.RenderAll();
        try
        {
            SetSelection(added.Select(i => i.Id));
            Check(added.All(i => rects.ContainsKey(i.Id)), "test widgets are on the deck: " + string.Join(" ", added.Select(i => rects.TryGetValue(i.Id, out var r) ? $"[{r.X:0.#},{r.Y:0.#} {r.Width:0.#}x{r.Height:0.#}]" : "[none]")));
            Align(top: true);
            double top = added.Min(i => rects[i.Id].Y);
            Check(added.All(i => rects[i.Id].Y == top), "align top puts them on one line: " + string.Join(" ", added.Select(i => $"[{rects[i.Id].X:0.#},{rects[i.Id].Y:0.#} {rects[i.Id].Width:0.#}x{rects[i.Id].Height:0.#}]")) + " " + toastText.Text);
            SameSize(false);
            Check(added.Select(i => rects[i.Id].Height).Distinct().Count() == 1, "same height makes them equal");
            Distribute(true);
            var ordered = added.OrderBy(i => rects[i.Id].X).ToList();
            var gaps = new[] { rects[ordered[1].Id].X - rects[ordered[0].Id].Right, rects[ordered[2].Id].X - rects[ordered[1].Id].Right };
            Check(Math.Abs(gaps[0] - gaps[1]) <= DeckLayout.Grid && gaps.All(g => g >= DeckLayout.Gap - 0.001) && Disjoint(added.Select(i => i.Id).ToList()), $"distribute horizontally leaves even gaps ({gaps[0]:0.#} and {gaps[1]:0.#})");
            // Dragging the selection: members keep their places relative to each other, stop where it no longer fits, and commit or cancel together.
            SetSelection(added.Select(i => i.Id));
            var dragIds = added.Select(i => i.Id).ToList();
            var lead = added[0];
            DragState Begin()
            {
                var state = new DragState { Item = lead, Handle = views[lead.Id], View = views[lead.Id], Resize = false, Start = new Point(100, 100), Origin = rects[lead.Id], Current = rects[lead.Id] };
                drag = state;
                state.Group = StartGroupDrag(lead, false);
                return state;
            }
            bool Together(Dictionary<string, TileRect> from, out double dx, out double dy)
            {
                double ddx = rects[lead.Id].X - from[lead.Id].X, ddy = rects[lead.Id].Y - from[lead.Id].Y;
                dx = ddx;
                dy = ddy;
                return dragIds.All(id => Math.Abs(rects[id].X - from[id].X - ddx) < 0.001 && Math.Abs(rects[id].Y - from[id].Y - ddy) < 0.001);
            }
            var start0 = dragIds.ToDictionary(id => id, id => rects[id]);
            Check(Begin().Group is { Members.Count: 3 }, "dragging a selected widget starts a group drag of all three");
            MoveGroup(new Point(100, 124));
            Check(Together(start0, out _, out double down) && down > 0, $"a free move shifts every member by the same amount ({down:0.#} down)");
            MoveGroup(new Point(100, 700));
            double slid = rects[lead.Id].Y - start0[lead.Id].Y;
            Check(Together(start0, out _, out _) && slid >= 60 && slid < 600 && FitsAll(dragIds.ToDictionary(id => id, id => rects[id]), OccupiedExceptSet(dragIds)), $"a move too far stops at the last spot that fits ({slid:0.#} down), together");
            var moved0 = dragIds.ToDictionary(id => id, id => rects[id]);
            EndDrag(true);
            Check(added.All(i => Math.Abs(i.Y * canvas.ActualHeight - moved0[i.Id].Y) < 0.01 && Math.Abs(i.X * canvas.ActualWidth - moved0[i.Id].X) < 0.01) && drag is null, "letting go stores every member's place");
            Begin();
            MoveGroup(new Point(100, 124));
            EndDrag(false);
            Check(added.All(i => rects[i.Id] == moved0[i.Id]) && drag is null, "cancelling a group drag puts every member back");
            hub.RenderAll();

            // The selection survives a redraw, and a hidden widget leaves it.
            hub.RenderAll();
            Check(selection.Count == 3, "the selection survives a redraw");
            hub.HideItems([added[2]]);
            Check(selection.Count == 2 && !selection.Contains(added[2].Id), "a hidden widget leaves the selection");
            // Customize together: only the changed fields apply.
            var changes = new GroupChanges("outline", null, null, new Dictionary<string, string?> { ["_radius"] = "30", ["_opacity"] = null });
            added[0].Options["_opacity"] = "50";
            string accentBefore = added[1].Accent;
            foreach (var item in added.Take(2)) changes.ApplyTo(item);
            Check(added[0].CardStyle == "outline" && added[0].Options["_radius"] == "30" && !added[0].Options.ContainsKey("_opacity") && added[1].Accent == accentBefore && !added[1].Options.ContainsKey("_opacity") && added[1].Options["_radius"] == "30", "customize together applies only what was changed");
            hub.RenderAll();
            UpdateLayout();
            UpdateSelectionChrome();
            snap("selection-test.png");
        }
        finally
        {
            foreach (var item in added) settings.Items.Remove(item);
            foreach (var item in cleared) item.Hidden = false;
            ClearSelection();
            hub.RenderAll();
        }

        // Live preview: the deck draws the unsaved copy, then the real widget again.
        var clock = Named("Time");
        var draft = clock.Copy();
        draft.CardStyle = "outline";
        draft.Options["_textScale"] = "140";
        hub.PreviewItem(draft);
        Check(ReferenceEquals(hub.Previewed(clock), draft) && !ReferenceEquals(clock.Options, draft.Options) && clock.CardStyle != "outline", "preview draws the copy and leaves the real widget alone");
        UpdateLayout();
        snap("preview-test.png");
        hub.ClearPreview();
        Check(ReferenceEquals(hub.Previewed(clock), clock), "closing Customize draws the real widget again");

        // The Customize window itself: typing in a field previews on the deck within a moment; a half-typed value is skipped quietly.
        LauncherItem? received = null;
        var editor = new ItemEditor(this, clock, false, settings, null, d => { received = d; hub.PreviewItem(d); }) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0 };
        editor.Show();
        void Pump()
        {
            for (int i = 0; i < 8; i++)
            {
                var frame = new DispatcherFrame();
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                System.Threading.Thread.Sleep(40);
            }
        }
        System.Windows.Controls.TextBox? Box(string label)
        {
            IEnumerable<object> Walk(object node)
            {
                yield return node;
                if (node is DependencyObject d) foreach (var child in LogicalTreeHelper.GetChildren(d)) foreach (var x in Walk(child)) yield return x;
            }
            var nodes = Walk(editor).ToList();
            int at = nodes.FindIndex(n => n is TextBlock t && t.Text.StartsWith(label, StringComparison.Ordinal));
            return at < 0 ? null : nodes.Skip(at).OfType<System.Windows.Controls.TextBox>().FirstOrDefault();
        }
        if (Box("Text size") is { } sizeBox)
        {
            sizeBox.Text = "170";
            Pump();
            Check(received is { } r1 && r1.Options.TryGetValue("_textScale", out var scale) && scale == "170" && hub.PreviewDraft is not null, "typing in Customize previews on the deck");
            received = null;
            sizeBox.Text = "17x";
            Pump();
            Check(received is null || received.Options["_textScale"] == "170", "a half-typed value does not preview and does not complain");
            Check(hub.PreviewDraft!.Options["_textScale"] == "170" && clock.Options.ContainsKey("_textScale") == false, "the real widget is untouched while previewing");
        }
        else Check(false, "found the Text size field in Customize");
        editor.Close();
        hub.ClearPreview();

        // The Customize together window.
        var together = new ItemEditor(this, [clock, Named("Daily bits"), Named("Trip to Japan")], settings) { ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -4000, Top = 0 };
        together.Show();
        Pump();
        if (together.Content is ScrollViewer { Content: FrameworkElement body })
        {
            body.Measure(new Size(540, double.PositiveInfinity));
            body.Arrange(new Rect(body.DesiredSize));
            body.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(body.ActualWidth), (int)Math.Ceiling(body.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(body);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = System.IO.File.Create(System.IO.Path.Combine(AppContext.BaseDirectory, "verification", "customize-together.png"));
            png.Save(file);
        }
        together.Close();

        // Grid size and gap apply to every surface at once.
        hub.SetGrid(16, 4, false);
        Check(DeckLayout.Grid == 16 && DeckLayout.Gap == 4 && settings.GridSize == 16 && settings.GridGap == 4, "grid size and gap apply live");
        UpdateLayout();
        snap("grid-16-4.png");
        hub.SetGrid(24, 12, false);
        Check(DeckLayout.Grid == 24 && DeckLayout.Gap == 12, "and go back");
        // The preview settings folder is shared with other smoke runs: leave it as the sample deck again.
        SaveNow();
        return string.Join(Environment.NewLine, log);
    }

    /// <summary>Smoke test: selects the first widgets on this deck so the selection outline and toolbar can be rendered.</summary>
    internal void SelectSampleForSmoke(int count)
    {
        SetSelection(rects.Keys.Take(count));
        UpdateLayout();
        UpdateSelectionChrome();
    }
}
