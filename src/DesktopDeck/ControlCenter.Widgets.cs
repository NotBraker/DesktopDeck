using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

// The Widgets tab: every widget grouped by layer and display (by display only in the simple view), with Visible, Customize, Locate and Remove; and "+ Add widget".
internal sealed partial class ControlCenter
{
    private string widgetFilter = "";
    private string widgetScope = "all";
    private StackPanel? widgetList;
    private Border? galleryLayer;

    private FrameworkElement BuildWidgetsTab()
    {
        var stack = new StackPanel();
        var bar = new Grid { Margin = new Thickness(0, 0, 0, PanelKit.S16) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var (host, box) = PanelKit.SearchBox("Search widgets", 240);
        host.HorizontalAlignment = HorizontalAlignment.Left;
        box.Tag = "search";
        box.Text = widgetFilter;
        box.TextChanged += (_, _) => { widgetFilter = box.Text; FillWidgetList(); };
        bar.Children.Add(host);
        string[] scopes = ["all", "shown", "hidden"];
        var filter = Theme.Segmented(["All", "Shown", "Hidden"], Math.Max(0, Array.IndexOf(scopes, widgetScope)), index => { widgetScope = scopes[index]; Rebuild("widgets"); }, 30);
        filter.Width = 210;
        filter.Margin = new Thickness(PanelKit.S12, 0, PanelKit.S12, 0);
        Grid.SetColumn(filter, 1);
        bar.Children.Add(filter);
        var add = Theme.Button("+ Add widget", true);
        add.MinHeight = 32;
        add.Click += (_, _) => OpenAddMenu(add);
        Grid.SetColumn(add, 2);
        bar.Children.Add(add);
        stack.Children.Add(bar);
        widgetList = new StackPanel();
        stack.Children.Add(widgetList);
        FillWidgetList();
        return stack;
    }

    private bool Matches(LauncherItem item)
    {
        if (widgetScope == "shown" && item.Hidden) return false;
        if (widgetScope == "hidden" && !item.Hidden) return false;
        string needle = widgetFilter.Trim();
        if (needle.Length == 0) return true;
        bool Hit(LauncherItem x) => DisplayName(x).Contains(needle, StringComparison.CurrentCultureIgnoreCase) || KindName(x).Contains(needle, StringComparison.CurrentCultureIgnoreCase);
        // A group is found by its own name or by any widget in it.
        return Hit(item) || (WidgetOrganizer.IsGroup(item) && WidgetOrganizer.Children(hub.Settings, item.Id).Any(Hit));
    }

    private void FillWidgetList()
    {
        if (widgetList is null) return;
        widgetList.Children.Clear();
        // Widgets inside a group are listed under it, not on their own.
        var items = hub.Settings.Items.Where(i => i.GroupId.Length == 0 && Matches(i)).ToList();
        if (hub.Settings.Items.Count == 0) { widgetList.Children.Add(PanelKit.Empty("No widgets yet. Press + Add widget.")); return; }
        if (items.Count == 0) { widgetList.Children.Add(PanelKit.Empty(widgetFilter.Trim().Length > 0 ? $"No widget matches \"{widgetFilter.Trim()}\"." : "Nothing here.")); return; }
        bool several = hub.Displays.Count > 1, first = true;
        // The simple view lists the widgets by monitor only; the layers (pinned or on the desktop) are an advanced idea.
        (bool? Pinned, string Title)[] layers = hub.Advanced ? [(true, "Pinned"), (false, "Desktop")] : [(null, "Widgets")];
        foreach (var (floating, title) in layers)
        {
            var layer = items.Where(i => floating is null || i.OnTop == floating).ToList();
            if (layer.Count == 0) continue;
            foreach (var group in layer.GroupBy(i => hub.CurrentDisplayOf(i).Id).OrderBy(g => hub.Displays.ToList().FindIndex(d => d.Id == g.Key)))
            {
                var info = hub.Displays.FirstOrDefault(d => d.Id == group.Key);
                string heading = several && info is not null ? (floating is null ? info.Name : $"{title}  ·  {info.Name}") : title;
                widgetList.Children.Add(GroupHeader(heading, group.Count(), floating == true, first));
                first = false;
                foreach (var item in group)
                {
                    widgetList.Children.Add(WidgetRow(item));
                    if (WidgetOrganizer.IsGroup(item)) foreach (var child in WidgetOrganizer.Children(hub.Settings, item.Id)) widgetList.Children.Add(ChildRow(child, item));
                }
            }
        }
    }

    private static FrameworkElement GroupHeader(string text, int count, bool floating, bool first)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, first ? 0 : PanelKit.S16, 0, PanelKit.S8) };
        row.Children.Add(Theme.Icon(floating ? "" : "", 12, Theme.Primary));
        var label = Theme.Label(text, 13);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(PanelKit.S8, 0, 0, 0);
        row.Children.Add(label);
        row.Children.Add(Theme.Label($"  {count}", 12, Theme.Muted));
        return row;
    }

    private FrameworkElement WidgetRow(LauncherItem item)
    {
        var accent = string.IsNullOrWhiteSpace(item.Accent) ? Theme.PrimaryHex : item.Accent;
        var grid = new Grid();
        for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });

        var tile = PanelKit.IconTile(KindGlyph(item), accent, 36);
        tile.Margin = new Thickness(0, 0, PanelKit.S12, 0);
        grid.Children.Add(tile);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, PanelKit.S8, 0), Opacity = item.Hidden ? 0.55 : 1 };
        var name = Theme.Label(DisplayName(item), 13.5);
        name.FontWeight = FontWeights.SemiBold;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.ToolTip = DisplayName(item);
        text.Children.Add(name);
        var (w, h) = item.OnTop ? FloatLayer.SizeFor(item) : (item.Width > 0 ? item.Width : ItemKinds.DefaultSize(item.Kind).Width, item.Height > 0 ? item.Height : ItemKinds.DefaultSize(item.Kind).Height);
        int inside = WidgetOrganizer.IsGroup(item) ? WidgetOrganizer.Children(hub.Settings, item.Id).Count : 0;
        var kind = Theme.Label(WidgetOrganizer.IsGroup(item) ? $"Group · {inside} {(inside == 1 ? "widget" : "widgets")} · {w:0}×{h:0}" : $"{KindName(item)} · {w:0}×{h:0}" + (WidgetLinks.IsLinked(item, hub.Settings.Items) ? " · Linked" : ""), 11.5, Theme.Muted);
        kind.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(kind);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var switches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        switches.Children.Add(Labeled("Visible", item.Hidden ? false : true, on => { Quietly(() => hub.SetItemHidden(item, !on)); FillWidgetList(); }, "Show or hide it in the current layout"));
        Grid.SetColumn(switches, 2);
        grid.Children.Add(switches);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(PanelKit.S8, 0, 0, 0) };
        actions.Children.Add(PanelKit.Quiet("Customize\u2026", () => { hub.CustomizeItem(item, this); }, tip: "Change this widget's settings"));
        bool canLocate = hub.ScreenRectOf(item) is not null;
        actions.Children.Add(PanelKit.More("More: pin on top, locate, remove", menu =>
        {
            PanelKit.MenuEntry(menu, "Pin on top", () => { hub.SetOnTop(item, !item.OnTop); Rebuild("widgets"); }, tip: "Keep it above your windows", check: item.OnTop);
            PanelKit.MenuEntry(menu, "Locate", () => Locate(item), canLocate, tip: canLocate ? "Briefly outline it on your screen" : item.Hidden ? "Hidden: turn Visible on to find it" : item.OnTop ? "Pinned widgets are hidden" : "It isn't on a desktop right now");
            menu.Items.Add(new Separator());
            PanelKit.MenuEntry(menu, "Remove\u2026", () => { if (hub.RemoveItem(item, this)) Rebuild("widgets"); }, danger: true, tip: "Remove this widget");
        }));
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        var card = PanelKit.HoverCard(grid);
        card.Padding = new Thickness(PanelKit.S12, PanelKit.S8, PanelKit.S8, PanelKit.S8);
        card.Margin = new Thickness(0, 0, 0, PanelKit.S8 - 2);
        return card;
    }

    /// <summary>A widget inside a group: indented under the group, with Customize, Take out and Remove.</summary>
    private FrameworkElement ChildRow(LauncherItem child, LauncherItem group)
    {
        var accent = string.IsNullOrWhiteSpace(child.Accent) ? Theme.PrimaryHex : child.Accent;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var tile = PanelKit.IconTile(KindGlyph(child), accent, 28);
        tile.Margin = new Thickness(0, 0, PanelKit.S12, 0);
        grid.Children.Add(tile);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = Theme.Label(DisplayName(child), 13);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(name);
        text.Children.Add(Theme.Label($"{KindName(child)} · a tab of {group.Name}", 11.5, Theme.Muted));
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(PanelKit.Quiet("Customize…", () => hub.CustomizeItem(child, this), tip: "Change this widget's settings"));
        actions.Children.Add(PanelKit.Quiet("Take out", () => { Quietly(() => hub.RemoveFromGroup(child)); Rebuild("widgets"); }, tip: "Put it back on the desktop by itself"));
        actions.Children.Add(PanelKit.More("More: remove", menu => PanelKit.MenuEntry(menu, "Remove\u2026", () => { if (hub.RemoveItem(child, this)) Rebuild("widgets"); }, danger: true, tip: "Remove this widget")));
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        var card = new Border { Background = Brushes.Transparent, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(PanelKit.S12, PanelKit.S4 + 2, PanelKit.S8, PanelKit.S4 + 2), Margin = new Thickness(PanelKit.S24, 0, 0, PanelKit.S8 - 2), Child = grid };
        card.MouseEnter += (_, _) => card.Background = PanelKit.Hover;
        card.MouseLeave += (_, _) => card.Background = Brushes.Transparent;
        return card;
    }

    private static FrameworkElement Labeled(string label, bool value, Action<bool> changed, string tip)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(PanelKit.S8, 0, PanelKit.S4, 0), ToolTip = tip };
        var text = Theme.Label(label, 12, Theme.Text2);
        text.Margin = new Thickness(0, 0, PanelKit.S8, 0);
        row.Children.Add(text);
        row.Children.Add(Theme.Switch(value, changed));
        return row;
    }

    // ---------- Names and icons ----------

    internal static string DisplayName(LauncherItem item) => string.IsNullOrWhiteSpace(item.Name) ? (item.Kind == ItemKinds.Image ? "Picture" : KindName(item)) : item.Name;

    internal static string KindName(LauncherItem item)
    {
        if (Widgets.WidgetRegistry.Find(item.Kind) is { } plugin) return plugin.Name;
        return item.Kind switch
        {
            ItemKinds.Launcher => "Launcher", ItemKinds.Image => "Picture", ItemKinds.Tasks => "Today's tasks", ItemKinds.Habits => "Habits", ItemKinds.Counter => "Counter",
            ItemKinds.Clock => "Time", ItemKinds.Timer => "Timer", ItemKinds.Schedule => "Schedule", ItemKinds.Goals => "Weekly goals",
            ItemKinds.Note => "Sticky note", ItemKinds.Countdown => "Countdown", ItemKinds.Media => "Media player", ItemKinds.Folder => "Folder",
            ItemKinds.Weather => "Weather", ItemKinds.Group => "Group", _ => "Widget",
        };
    }

    internal static string KindGlyph(LauncherItem item)
    {
        if (Widgets.WidgetRegistry.Find(item.Kind) is { } plugin) return plugin.Glyph;
        return item.Kind switch
        {
            ItemKinds.Launcher => Theme.Glyphs.App, ItemKinds.Image => Theme.Glyphs.Picture, ItemKinds.Tasks => Theme.Glyphs.Tasks, ItemKinds.Habits => Theme.Glyphs.Habits, ItemKinds.Counter => Theme.Glyphs.Counter,
            ItemKinds.Clock => Theme.Glyphs.Time, ItemKinds.Timer => Theme.Glyphs.Timer, ItemKinds.Schedule => Theme.Glyphs.Calendar, ItemKinds.Goals => Theme.Glyphs.Goals,
            ItemKinds.Note => Theme.Glyphs.Note, ItemKinds.Countdown => Theme.Glyphs.Countdown, ItemKinds.Media => Theme.Glyphs.Music, ItemKinds.Folder => Theme.Glyphs.Folder,
            ItemKinds.Weather => Theme.Glyphs.Sun, ItemKinds.Group => "\uE8A9", _ => Theme.Glyphs.App,
        };
    }

    // ---------- + Add widget ----------

    private void OpenAddMenu(FrameworkElement anchor)
    {
        if (hub.Displays.Count <= 1) { ShowGallery(hub.PrimaryDisplay); return; }
        var menu = MainWindow.Menu();
        foreach (var display in hub.Displays)
        {
            var target = display;
            MainWindow.AddMenuItem(menu, "Add to " + display.Name, () => ShowGallery(target), Theme.Glyphs.Monitor);
        }
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>The widget gallery over the panel, adding to the chosen monitor's deck. Clicking outside it or pressing Esc closes it.</summary>
    internal void ShowGallery(DisplayInfo target)
    {
        var surface = hub.Backgrounds.FirstOrDefault(b => b.Display.Id == target.Id) ?? hub.Primary;
        if (surface is null) return;
        CloseGallery();
        var gallery = surface.BuildGalleryForPanel(CloseGallery, Math.Max(240, ActualHeight - 2 * ShadowRoom - 120));
        var layer = new Border { Background = Theme.Brush("#C0090704"), Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = gallery, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(16) } };
        layer.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource == layer || e.OriginalSource is Viewbox) CloseGallery(); };
        layer.CornerRadius = new CornerRadius(15);
        galleryLayer = layer;
        Grid.SetColumnSpan(layer, 2);
        inner.Children.Add(layer);
    }

    private void CloseGallery()
    {
        if (galleryLayer is null) return;
        inner.Children.Remove(galleryLayer);
        galleryLayer = null;
    }

    // ---------- Locate ----------

    /// <summary>Hides the panel for a moment and outlines the widget where it is on screen, then brings the panel back.</summary>
    private void Locate(LauncherItem item)
    {
        if (hub.ScreenRectOf(item) is not { } rect) return;
        Conceal(true);
        var flash = new LocateFlash(rect, DisplayName(item), item.Accent);
        flash.Finished += () => { if (!closed) Conceal(false); };
        flash.Start();
    }
}

/// <summary>A short outline around a widget on screen ("here it is"): click-through, never takes focus, fades by itself after about two seconds.</summary>
internal sealed class LocateFlash : Window
{
    private readonly PixelRect rect;
    private bool done;
    internal event Action? Finished;

    internal LocateFlash(PixelRect rect, string name, string accent)
    {
        this.rect = rect;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Desktop Deck locate";
        FontFamily = Theme.UiFont;
        var ring = new Border { BorderBrush = Theme.Accent(accent), BorderThickness = new Thickness(4), CornerRadius = new CornerRadius(16), Background = Theme.Tint(accent, 0.16), Margin = new Thickness(6) };
        var pill = new Border { CornerRadius = new CornerRadius(14), Background = Theme.Surface3, BorderBrush = Theme.Accent(accent), BorderThickness = new Thickness(1.5), Padding = new Thickness(14, 6, 14, 7), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Child = Theme.Label(name, 13.5) };
        var host = new Grid();
        host.Children.Add(ring);
        host.Children.Add(pill);
        Content = host;
        ring.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9, Color = ((SolidColorBrush)Theme.Accent(accent)).Color };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            NativeWindow.AddExStyle(handle, NativeWindow.ExToolWindow | NativeWindow.ExNoActivate | NativeWindow.ExTransparent);
            NativeWindow.Place(handle, new PixelRect(rect.X - 8, rect.Y - 8, rect.Width + 16, rect.Height + 16), true);
        };
        Opacity = 0;
    }

    internal void Start()
    {
        Show();
        var pulse = new DoubleAnimationUsingKeyFrames();
        foreach (var (at, value) in new[] { (0, 0.0), (140, 1.0), (520, 0.45), (900, 1.0), (1280, 0.45), (1660, 1.0), (2000, 0.0) })
            pulse.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(at))));
        pulse.Completed += (_, _) => Finish();
        BeginAnimation(OpacityProperty, pulse);
    }

    private void Finish()
    {
        if (done) return;
        done = true;
        Close();
        Finished?.Invoke();
    }
}
