using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>The dim layer behind the control center: it covers the whole monitor, and clicking it closes the control center.</summary>
internal sealed class ControlBackdrop : Window
{
    internal ControlBackdrop(ControlCenter center)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Theme.Brush("#B0090704");
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Title = "Desktop Deck control center backdrop";
        Opacity = 0;
        // Clicking outside the panel closes it, unless one of its dialogs (an editor, a file picker, a confirmation) is open.
        MouseLeftButtonDown += (_, e) => { if (!center.HasDialog) { center.Close(); e.Handled = true; } };
    }
}

/// <summary>
/// The control center: one centered panel with a tab for each part of the deck (Layouts, Widgets, Appearance, Reminders, History, Settings), over a dim
/// backdrop on the monitor the control button is on. Both are ordinary top-level windows, so typing works; dialogs it opens are owned by it
/// (<see cref="DeckHub.DialogOwner"/>) so they stay in front. Esc closes it, Ctrl+Tab moves between tabs.
/// </summary>
internal sealed partial class ControlCenter : Window
{
    internal const double PanelWidth = 920, PanelHeight = 640, ShadowRoom = 24;
    private const double NavWidth = 216, NavNarrow = 68;

    internal static readonly (string Id, string Title, string Glyph, string Subtitle)[] Tabs =
    [
        ("layouts", "Layouts", "", "Switch between arrangements, and let rules do it for you."),
        ("widgets", "Widgets", "", "Show, hide, customize or find any widget."),
        ("appearance", "Appearance", "", "How cards and the deck background look."),
        ("reminders", "Reminders", "\uE823", "One-off or repeating, with snooze."),
        ("history", "History", "\uE81C", "Undo changes and restore earlier days."),
        ("settings", "Settings", "", "Start-up, display, accounts and your data."),
    ];

    /// <summary>Tabs the simple view (Settings → Show advanced features off) leaves out. A request for one of them opens Widgets instead.</summary>
    private static readonly string[] AdvancedTabs = ["layouts"];

    /// <summary>The tabs in the navigation now: all of them, or without <see cref="AdvancedTabs"/> in the simple view.</summary>
    private (string Id, string Title, string Glyph, string Subtitle)[] VisibleTabs => TabsFor(hub.Advanced);

    internal static (string Id, string Title, string Glyph, string Subtitle)[] TabsFor(bool advanced) => advanced ? Tabs : Tabs.Where(t => !AdvancedTabs.Contains(t.Id)).ToArray();

    /// <summary>The tab a control center opens on when none was asked for: Layouts, or Widgets in the simple view.</summary>
    private string DefaultTab => hub.Advanced ? "layouts" : "widgets";

    /// <summary>Tabs that were merged into another one: asking for the old id opens the new home.</summary>
    private static string Canonical(string id) => id switch { "display" => "settings", "automation" => "layouts", _ => id };

    private readonly DeckHub hub;
    private readonly bool smoke;
    private readonly DisplayInfo display;
    private readonly ControlBackdrop? backdrop;
    private readonly Grid root = new();
    private readonly Border panel = new();
    private readonly Grid inner = new();
    private readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
    private readonly TextBlock title = Theme.Label("", 22);
    private readonly TextBlock subtitle = Theme.Label("", 12.5, Theme.Muted);
    private readonly Dictionary<string, FrameworkElement> cache = new();
    private readonly Dictionary<string, (Border Item, TextBlock Icon, TextBlock Label)> navItems = new();
    private readonly ScaleTransform pop = new(0.97, 0.97);
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private SettingsContent? settingsContent;
    private StackPanel? navList;
    private bool builtAdvanced;
    private string current = "";
    private nint handle;
    private bool narrow, closed;
    private int quiet;
    private PixelRect target;

    /// <summary>The tab being shown (one of the ids in <see cref="Tabs"/>).</summary>
    internal string CurrentTab => current;
    internal bool HasDialog => OwnedWindows.Count > 0;

    internal ControlCenter(DeckHub hub, DisplayInfo display, string tab, bool smoke = false)
    {
        this.hub = hub;
        this.display = display;
        this.smoke = smoke;
        Title = "Desktop Deck control center";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = !smoke;
        FontFamily = Theme.UiFont;
        Foreground = Theme.Text;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        System.Windows.Automation.AutomationProperties.SetName(this, "Desktop Deck control center");

        // Smaller than the screen: about 92% of the work area, never above the design size (shrinks to fit small and portrait screens).
        double scale = Math.Max(0.5, display.Scale);
        double workW = display.WorkArea.Width / scale, workH = display.WorkArea.Height / scale;
        double w = Math.Min(PanelWidth, Math.Max(360, workW * 0.94 - 2 * ShadowRoom)), h = Math.Min(PanelHeight, Math.Max(300, workH * 0.92 - 2 * ShadowRoom));
        narrow = w < 720;
        Width = w + 2 * ShadowRoom;
        Height = h + 2 * ShadowRoom;
        target = new PixelRect(
            display.WorkArea.X + (int)Math.Round((display.WorkArea.Width - Width * scale) / 2), display.WorkArea.Y + (int)Math.Round((display.WorkArea.Height - Height * scale) / 2),
            (int)Math.Round(Width * scale), (int)Math.Round(Height * scale));

        BuildFrame();
        Content = root;
        if (!smoke)
        {
            backdrop = new ControlBackdrop(this);
            hub.StateChanged += OnStateChanged;
        }
        refresh.Tick += (_, _) => { refresh.Stop(); RefreshStale(); };
        PreviewKeyDown += OnKey;
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            if (!smoke) NativeWindow.Place(handle, target, true);
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!closed && !smoke && handle != 0) NativeWindow.Place(handle, target, true); }));
        Loaded += (_, _) => { if (!smoke) Appear(); };
        Closed += OnClosed;
        ShowTab(Tabs.Any(t => t.Id == Canonical(tab)) ? tab : DefaultTab);
    }

    // ---------- Showing, animation, closing ----------

    /// <summary>Opens the backdrop and the panel on top of everything and gives the panel the keyboard.</summary>
    internal void Present()
    {
        if (backdrop is not null)
        {
            backdrop.Left = display.Bounds.X; backdrop.Top = display.Bounds.Y; backdrop.Width = 200; backdrop.Height = 200;
            backdrop.SourceInitialized += (_, _) => NativeWindow.Place(new WindowInteropHelper(backdrop).Handle, display.Bounds, true);
            backdrop.Show();
            Owner = backdrop;
        }
        Show();
        NativeWindow.Foreground(handle);
        Activate();
        Focus();
    }

    /// <summary>One short fade and grow-in (120 ms); nothing animates after it.</summary>
    private void Appear()
    {
        var length = TimeSpan.FromMilliseconds(120);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        backdrop?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, length));
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, length) { EasingFunction = ease });
        pop.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, length) { EasingFunction = ease });
        pop.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, length) { EasingFunction = ease });
    }

    /// <summary>Hides the panel and the backdrop for a moment (to point at something on screen) and brings them back.</summary>
    internal void Conceal(bool hidden)
    {
        if (closed) return;
        if (hidden) { backdrop?.Hide(); Hide(); return; }
        backdrop?.Show();
        Show();
        NativeWindow.Foreground(handle);
        Activate();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        closed = true;
        refresh.Stop();
        hub.StateChanged -= OnStateChanged;
        settingsContent?.Dispose();
        backdrop?.Close();
        hub.ControlCenterClosed(this);
    }

    private void OnKey(object sender, KeyEventArgs e)
    {
        if (hub.TryHistoryKey(e)) return;
        if (e.Key == Key.Escape)
        {
            // In a search box Esc first clears it.
            if (Keyboard.FocusedElement is TextBox { Text.Length: > 0 } box && box.Tag as string == "search") { box.Clear(); e.Handled = true; return; }
            if (galleryLayer is not null) { CloseGallery(); e.Handled = true; return; }
            if (HasDialog) return;
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var tabs = VisibleTabs;
            int at = Array.FindIndex(tabs, t => t.Id == current);
            int step = (Keyboard.Modifiers & ModifierKeys.Shift) != 0 ? -1 : 1;
            ShowTab(tabs[(at + step + tabs.Length) % tabs.Length].Id);
            e.Handled = true;
        }
    }

    // ---------- The frame: navigation on the left, a header and the scrolling content on the right ----------

    private void BuildFrame()
    {
        root.Margin = new Thickness(0);
        panel.Margin = new Thickness(ShadowRoom);
        panel.CornerRadius = new CornerRadius(16);
        panel.Background = Theme.Background;
        panel.BorderBrush = Theme.LineStrong;
        panel.BorderThickness = new Thickness(1);
        panel.Effect = PanelKit.Shadow(40, 12, 0.6);
        panel.RenderTransformOrigin = new Point(0.5, 0.5);
        panel.RenderTransform = pop;
        root.Children.Add(panel);

        // Clip the contents to the rounded corners (the shadow lives on the outer border, so it is not clipped).
        panel.Child = inner;
        inner.SizeChanged += (_, _) => inner.Clip = new RectangleGeometry(new Rect(0, 0, inner.ActualWidth, inner.ActualHeight), 15, 15);
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(narrow ? NavNarrow : NavWidth) });
        inner.ColumnDefinitions.Add(new ColumnDefinition());

        // ---------- Navigation ----------
        var nav = new Border { Background = Theme.Surface, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(PanelKit.S12, PanelKit.S16, PanelKit.S12, PanelKit.S12) };
        var navStack = new DockPanel();
        nav.Child = navStack;
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(PanelKit.S8, 4, 0, PanelKit.S16) };
        var mark = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderBrush = Theme.Primary, BorderThickness = new Thickness(2), Background = Theme.Surface3, Child = Theme.Icon("", 14, Theme.Text) };
        brand.Children.Add(mark);
        if (!narrow)
        {
            var name = new StackPanel { Margin = new Thickness(PanelKit.S12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var brandName = Theme.Label("Desktop Deck", 14);
            brandName.FontWeight = FontWeights.SemiBold;
            name.Children.Add(brandName);
            name.Children.Add(Theme.Label("Control center", 11.5, Theme.Muted));
            brand.Children.Add(name);
        }
        DockPanel.SetDock(brand, Dock.Top);
        navStack.Children.Add(brand);
        var foot = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(PanelKit.S8, 0, 0, 0) };
        if (!narrow)
        {
            foot.Children.Add(Theme.Label("Desktop Deck " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? ""), 11.5, Theme.Muted));
            var keys = Theme.Label("Esc closes · Ctrl+Tab switches tab", 11, Theme.Dim);
            keys.TextWrapping = TextWrapping.Wrap;
            keys.Margin = new Thickness(0, 3, 0, 0);
            foot.Children.Add(keys);
        }
        DockPanel.SetDock(foot, Dock.Bottom);
        navStack.Children.Add(foot);
        navList = new StackPanel();
        FillNav();
        navStack.Children.Add(navList);
        inner.Children.Add(nav);

        // ---------- Header and content ----------
        var content = new Grid { Margin = new Thickness(0) };
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition());
        Grid.SetColumn(content, 1);
        inner.Children.Add(content);
        var header = new Grid { Margin = new Thickness(PanelKit.S24, PanelKit.S24, PanelKit.S16, PanelKit.S12) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        title.FontWeight = FontWeights.SemiBold;
        subtitle.TextWrapping = TextWrapping.Wrap;
        subtitle.Margin = new Thickness(0, 4, PanelKit.S16, 0);
        heading.Children.Add(title);
        heading.Children.Add(subtitle);
        header.Children.Add(heading);
        var close = Theme.IconButton("", "Close (Esc)", 32);
        close.Focusable = true;
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        content.Children.Add(header);
        Grid.SetRow(scroll, 1);
        scroll.Padding = new Thickness(PanelKit.S24, PanelKit.S8, PanelKit.S16, PanelKit.S24);
        content.Children.Add(scroll);
    }

    private void FillNav()
    {
        builtAdvanced = hub.Advanced;
        navList!.Children.Clear();
        navItems.Clear();
        foreach (var tab in VisibleTabs) navList.Children.Add(BuildNavItem(tab.Id, tab.Title, tab.Glyph));
    }

    private Border BuildNavItem(string id, string text, string glyph)
    {
        var icon = Theme.Icon(glyph, 16, Theme.Text2);
        icon.Width = 22;
        var label = Theme.Label(text, 13.5, Theme.Text2);
        label.FontWeight = FontWeights.Medium;
        label.Margin = new Thickness(PanelKit.S12, 0, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        row.Children.Add(icon);
        if (!narrow) row.Children.Add(label);
        var item = new Border { Height = 40, CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(narrow ? 0 : 12, 0, 0, 0), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1), Child = row, ToolTip = narrow ? text : null };
        PanelKit.MakeFocusable(item, () => ShowTab(id), text);
        item.MouseLeftButtonUp += (_, e) => { ShowTab(id); e.Handled = true; };
        item.MouseEnter += (_, _) => { if (current != id) item.Background = PanelKit.Hover; };
        item.MouseLeave += (_, _) => { if (current != id) item.Background = Brushes.Transparent; };
        item.LostKeyboardFocus += (_, _) => item.BorderBrush = Brushes.Transparent;
        navItems[id] = (item, icon, label);
        return item;
    }

    // ---------- Tabs ----------

    /// <summary>Shows a tab, building it the first time (the Settings tab is kept so a Google sign-in in progress survives switching away).</summary>
    internal void ShowTab(string id)
    {
        id = Canonical(id);
        if (closed || !Tabs.Any(t => t.Id == id)) return;
        if (!VisibleTabs.Any(t => t.Id == id)) id = "widgets";
        current = id;
        foreach (var (key, parts) in navItems)
        {
            bool on = key == id;
            parts.Item.Background = on ? Theme.Tint(Theme.PrimaryHex, 0.16) : Brushes.Transparent;
            parts.Icon.Foreground = on ? Theme.Primary : Theme.Text2;
            parts.Label.Foreground = on ? Theme.Text : Theme.Text2;
        }
        var info = Tabs.First(t => t.Id == id);
        title.Text = info.Title;
        subtitle.Text = info.Subtitle;
        if (!cache.TryGetValue(id, out var view)) cache[id] = view = BuildTab(id);
        scroll.Content = view;
        if (id == "settings") RefreshDisplayIfStale();
        scroll.ScrollToTop();
    }

    private FrameworkElement BuildTab(string id) => id switch
    {
        "layouts" => BuildLayoutsTab(),
        "widgets" => BuildWidgetsTab(),
        "appearance" => BuildAppearanceTab(),
        "reminders" => BuildRemindersTab(),
        "history" => BuildHistoryTab(),
        _ => BuildSettingsTab(),
    };

    private FrameworkElement BuildAppearanceTab()
    {
        var stack = new StackPanel();
        ThemePanel.FillThemes(PanelKit.Section(stack, "Theme", "Colors and font for the deck, the menus and this window. Changes apply at once."), hub);
        // Saved looks only matter while every widget keeps its own look; with the shared look on (or none saved) there is nothing to show here.
        if (hub.Presets.Items.Count > 0 && hub.Settings.Appearance?.ApplyToAll != true)
        {
            var saved = PanelKit.Collapsible(stack, "appearance.saved", $"Saved looks ({hub.Presets.Items.Count})");
            var note = PanelKit.Hint("Saved from a widget's Customize window. Apply one there, or pass a look between widgets with right-click \u203A Look.");
            note.Margin = new Thickness(0, 0, 0, PanelKit.S8);
            saved.Body.Children.Add(note);
            ThemePanel.FillPresets(saved.Body, hub, this, () => Rebuild("appearance"));
        }
        var card = new Border { Background = Theme.Surface, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(PanelKit.S16, PanelKit.S16, PanelKit.S16, PanelKit.S8), Margin = new Thickness(0, PanelKit.S24, 0, 0), Child = AppearancePanel.Build(hub, this, () => Rebuild("appearance")) };
        stack.Children.Add(card);
        return stack;
    }

    private FrameworkElement BuildSettingsTab()
    {
        settingsContent?.Dispose();
        displayHost = null;
        settingsContent = SettingsContent.Build(hub, this, AddDisplaySections);
        displayDirty = false;
        return settingsContent.Root;
    }

    /// <summary>The Settings tab as it would look signed out / in (smoke test).</summary>
    internal StackPanel? SettingsRoot => cache.TryGetValue("settings", out var view) ? view as StackPanel : null;

    // ---------- Staying current when something outside changes the layouts or widgets ----------

    private readonly HashSet<string> stale = new();

    private void OnStateChanged(StateChange change)
    {
        if (closed) return;
        if (builtAdvanced != hub.Advanced) { ApplyAdvanced(); return; }
        if (quiet > 0)
        {
            // The change came from the tab being used (it updates itself); the other tabs just forget what they showed.
            foreach (var id in new[] { "layouts", "widgets" }.Concat(change == StateChange.Layouts ? ["appearance"] : []).Where(id => id != current)) cache.Remove(id);
            displayDirty = true;
            return;
        }
        stale.Add("layouts"); stale.Add("widgets");
        displayDirty = true;
        if (change == StateChange.Layouts) stale.Add("appearance");
        refresh.Stop();
        refresh.Start();
    }

    /// <summary>
    /// Settings → Show advanced features changed: the navigation gets (or loses) its advanced tabs and every tab is built again the next time it is shown.
    /// The Settings tab itself is kept (a Google sign-in in progress survives); only its Display part is drawn again.
    /// </summary>
    private void ApplyAdvanced()
    {
        string tab = current;
        FillNav();
        foreach (var id in cache.Keys.Where(id => id != "settings").ToList()) cache.Remove(id);
        stale.Clear();
        current = "";
        ShowTab(tab);
        if (tab == "settings") RefillDisplay();
    }

    private void RefreshStale()
    {
        if (closed) return;
        double offset = scroll.VerticalOffset;
        if (current == "settings") RefreshDisplayIfStale();
        foreach (var id in stale.ToList())
        {
            if (!cache.Remove(id)) continue;
            if (id != current) continue;
            // The tab being looked at is rebuilt in place, keeping its scroll position.
            var view = cache[id] = BuildTab(id);
            scroll.Content = view;
            scroll.UpdateLayout();
            scroll.ScrollToVerticalOffset(offset);
        }
        stale.Clear();
    }

    /// <summary>Runs something that changes the deck without rebuilding the tab the change came from (a switch or button the user just pressed).</summary>
    private void Quietly(Action action)
    {
        quiet++;
        try { action(); }
        finally { quiet--; }
    }

    /// <summary>A layout was switched, made, renamed or deleted from this tab: redraw the tabs that show layouts, widgets or the background.</summary>
    private void AfterLayoutChange()
    {
        cache.Remove("widgets");
        cache.Remove("appearance");
        displayDirty = true;
        if (current is "widgets" or "appearance") Rebuild(current);
        Rebuild("layouts");
    }

    /// <summary>Raises the backdrop and then the panel above other topmost windows, without activating anything (an open dialog keeps the keyboard).</summary>
    internal void RaiseOnly()
    {
        if (closed || smoke || handle == 0) return;
        if (backdrop is not null) NativeWindow.Raise(new WindowInteropHelper(backdrop).Handle);
        NativeWindow.Raise(handle);
        foreach (Window owned in OwnedWindows) NativeWindow.Raise(new WindowInteropHelper(owned).Handle);
    }

    /// <summary>Redraws one tab now (after an action in it).</summary>
    private void Rebuild(string id)
    {
        cache.Remove(id);
        if (id != current) return;
        double offset = scroll.VerticalOffset;
        var view = cache[id] = BuildTab(id);
        scroll.Content = view;
        scroll.UpdateLayout();
        scroll.ScrollToVerticalOffset(offset);
    }

    // ---------- Smoke test ----------

    private static FrameworkElement? FindByTag(DependencyObject root, string tag)
    {
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement { Tag: string t } element && t == tag) return element;
            if (FindByTag(child, tag) is { } found) return found;
        }
        return null;
    }

    /// <summary>Smoke test: every collapsible section under <paramref name="node"/>, outer ones first.</summary>
    private static void CollectDisclosures(DependencyObject node, List<Disclosure> found)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            if (child is Disclosure section) found.Add(section);
            CollectDisclosures(child, found);
        }
    }

    /// <summary>Smoke test: presses the element with this automation name (raises its mouse-up like a click).</summary>
    private static bool ClickByName(DependencyObject root, string name)
    {
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(root); i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement element && System.Windows.Automation.AutomationProperties.GetName(element) == name)
            {
                element.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                return true;
            }
            if (ClickByName(child, name)) return true;
        }
        return false;
    }

    /// <summary>Renders the panel and then the whole tab (not just what fits in the scroll area) to PNGs.</summary>
    internal void RenderForSmoke(string folder, Action<FrameworkElement, string> snap)
    {
        UpdateLayout();
        if (current == "widgets" && hub.Primary is not null)
        {
            // The "+ Add widget" gallery, drawn over the panel.
            hub.ToggleFavoriteWidget("timer");
            hub.ToggleFavoriteWidget("github");
            hub.NoteRecentWidget("note");
            hub.NoteRecentWidget("focus");
            hub.NoteRecentWidget("timer");
            ShowGallery(hub.PrimaryDisplay);
            UpdateLayout();
            root.Background = Theme.Brush("#0C0A07");
            snap(root, Path.Combine(folder, "panel-widgets-add.png"));
            // The category tabs and the search box, driven the way a click and typing would.
            foreach (var (chip, file) in new[] { ("★ Favorites widgets", "panel-widgets-add-favorites.png"), ("Recent widgets", "panel-widgets-add-recent.png"), ("Study widgets", "panel-widgets-add-study.png") })
            {
                ClickByName(galleryLayer!, chip);
                UpdateLayout();
                snap(root, Path.Combine(folder, file));
            }
            ClickByName(galleryLayer!, "All widgets");
            if (FindByTag(galleryLayer!, "search") is TextBox searchBox) { searchBox.Text = "time"; UpdateLayout(); snap(root, Path.Combine(folder, "panel-widgets-add-search.png")); }
            root.Background = null;
            CloseGallery();
        }
        else if (current == "layouts" && hub.Primary is not null)
        {
            // Templates: a floating and a background layout made from them, shown in the list; then the background one switched on and the deck drawn.
            var student = WidgetTemplates.Find("student")!;
            var made = hub.CreateTemplateLayout(student, LayoutLayers.Background);
            hub.CreateTemplateLayout(WidgetTemplates.Find("minimal")!, LayoutLayers.Floating);
            if (made is not null) { templateLayoutId = made.Id; templateNote = $"Added \u201C{made.Name}\u201D as a desktop layout with {made.Entries.Count} widgets. It's in the list above."; }
            cache.Remove("layouts");
            ShowTab("layouts");
            UpdateLayout();
            if (made is not null)
            {
                var before = LayoutEngine.Active(hub.Settings, LayoutLayers.Background)!;
                hub.SwitchLayout(LayoutLayers.Background, made.Id);
                hub.Primary.UpdateLayout();
                snap(hub.Primary, Path.Combine(folder, "deck-template-student.png"));
                var vibe = hub.CreateTemplateLayout(WidgetTemplates.Find("vibe-coding")!, LayoutLayers.Background);
                if (vibe is not null) { hub.SwitchLayout(LayoutLayers.Background, vibe.Id); hub.Primary.UpdateLayout(); snap(hub.Primary, Path.Combine(folder, "deck-template-vibe.png")); }
                hub.SwitchLayout(LayoutLayers.Background, before.Id);
            }
        }
        if (current == "layouts") RenderAutomationForSmoke(folder, snap);
        root.Background = Theme.Brush("#0C0A07");
        UpdateLayout();
        // --panel-scroll <pixels>: the panel shot starts that far down the tab (README screenshots of a lower section).
        var smokeArgs = Environment.GetCommandLineArgs();
        int scrollAt = Array.IndexOf(smokeArgs, "--panel-scroll");
        if (scrollAt >= 0 && scrollAt + 1 < smokeArgs.Length && double.TryParse(smokeArgs[scrollAt + 1], System.Globalization.CultureInfo.InvariantCulture, out double scrollBy))
        {
            scroll.ScrollToVerticalOffset(scrollBy);
            UpdateLayout();
        }
        snap(root, Path.Combine(folder, $"panel-{current}.png"));
        root.Background = null;
        if (scroll.Content is FrameworkElement view)
        {
            double width = scroll.ViewportWidth;
            scroll.Content = null;
            var host = new Border { Background = Theme.Background, Child = view, Width = width, Padding = new Thickness(PanelKit.S24, PanelKit.S8, PanelKit.S16, PanelKit.S24) };
            host.Measure(new Size(width, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, width, host.DesiredSize.Height));
            host.UpdateLayout();
            snap(host, Path.Combine(folder, $"panel-{current}-full.png"));
            // The same tab with every collapsed section opened, to check what is inside them.
            var sections = new List<Disclosure>();
            CollectDisclosures(view, sections);
            if (sections.Count > 0)
            {
                var before = sections.Select(d => d.IsOpen).ToList();
                foreach (var section in sections) section.IsOpen = true;
                host.Measure(new Size(width, double.PositiveInfinity));
                host.Arrange(new Rect(0, 0, width, host.DesiredSize.Height));
                host.UpdateLayout();
                snap(host, Path.Combine(folder, $"panel-{current}-expanded.png"));
                for (int i = 0; i < sections.Count; i++) sections[i].IsOpen = before[i];
                PanelKit.ForgetOpenSections();
            }
            host.Child = null;
            scroll.Content = view;
        }
    }
}
