using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>What the person chose in the welcome window: a starter (null = start empty) and whether to also add their desktop shortcuts as launchers.</summary>
internal sealed record WelcomeChoice(WidgetTemplate? Starter, bool AddShortcuts);

/// <summary>
/// The first-start welcome: one line about the app, a card per starter (Student, Developer, Minimal), "Start empty", a switch to also bring in desktop
/// shortcuts, and a row of tips. Esc (or closing the window) skips it and leaves the deck empty. <see cref="Choice"/> is null when skipped.
/// </summary>
internal sealed class WelcomeWindow : Window
{
    internal static readonly string[] StarterIds = ["student", "developer", "minimal"];
    internal const string Tips = "Right-click any widget to customize it  ·  The round button opens the control center  ·  Use + Add to find more widgets";

    private readonly Border card;
    private readonly Grid shell = new();
    private readonly Layout.DisplayInfo display;
    private nint handle;
    private bool addShortcuts, done;
    /// <summary>Desktop icons stay visible, so the person's shortcuts are already on the desktop and importing them as launchers is not offered.</summary>
    private readonly bool iconsShown;

    internal WelcomeChoice? Choice { get; private set; }

    /// <summary>The window's content, for smoke renders.</summary>
    internal Grid Surface => shell;

    /// <summary>Where the window sits on screen, in pixels (for the smoke test).</summary>
    internal Layout.PixelRect Placed { get; private set; }

    private void Center()
    {
        if (handle == 0 || done) return;
        var work = display.WorkArea;
        int w = (int)Math.Round(Width * display.Scale), h = (int)Math.Round(Height * display.Scale);
        Placed = new Layout.PixelRect(work.X + (work.Width - w) / 2, work.Y + (work.Height - h) / 2, w, h);
        NativeWindow.Place(handle, Placed, true);
    }

    internal WelcomeWindow(Layout.DisplayInfo display, bool iconsShown = false)
    {
        this.display = display;
        this.iconsShown = iconsShown;
        Title = "Welcome to Desktop Deck";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Topmost = true;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;
        System.Windows.Automation.AutomationProperties.SetName(this, "Welcome to Desktop Deck");

        card = BuildCard();
        card.Margin = new Thickness(PanelKit.S24);
        card.Effect = PanelKit.Shadow(32, 8, 0.6);
        shell.Children.Add(card);
        Content = shell;
        // Centered on the primary monitor's work area (CenterScreen would follow the mouse to another monitor), sized up front so the pixel size is exact at any DPI.
        shell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Width = shell.DesiredSize.Width;
        Height = shell.DesiredSize.Height;
        SourceInitialized += (_, _) => { handle = new System.Windows.Interop.WindowInteropHelper(this).Handle; Center(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(Center));

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Finish(null); } };
        card.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not DependencyObject source || !OnControl(source)) DragMove(); };
        Loaded += (_, _) => { Activate(); card.Focus(); };
    }

    /// <summary>True when a click landed on a control (a starter card, a button, the switch) rather than the empty background, so dragging the window doesn't swallow it.</summary>
    private bool OnControl(DependencyObject source)
    {
        for (var node = source; node is not null && !ReferenceEquals(node, card); node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is Button || node is Border { Cursor: { } cursor } && ReferenceEquals(cursor, Cursors.Hand)) return true;
        return false;
    }

    private void Finish(WelcomeChoice? choice)
    {
        if (done) return;
        done = true;
        Choice = choice;
        Close();
    }

    /// <summary>The first start: shows the welcome over the deck and puts what was chosen on the main screen. Skipping (Esc) leaves the deck empty; either way the settings are saved so it isn't shown again.</summary>
    internal static void ShowFirstRun(DeckHub hub)
    {
        var window = new WelcomeWindow(hub.PrimaryDisplay, iconsShown: true); // a fresh install keeps the desktop icons (see StartWithDesktopIcons)
        window.Closed += (_, _) =>
        {
            try
            {
                hub.Settings.StartSimple(); // a fresh install starts in the simple view; Settings → Show advanced features brings the rest back
                hub.Settings.StartWithDesktopIcons(); // and keeps the desktop icons visible (Settings → Display → Show desktop icons)
                if (window.Choice is { } choice) hub.ApplyWelcome(choice.Starter, choice.AddShortcuts ? Services.DesktopImporter.Discover() : null);
                else hub.Save();
            }
            catch (Exception ex) { App.Log(ex); }
        };
        window.Show();
    }

    /// <summary>--smoke-test --welcome: draws the window, then each starter on an empty deck, into <paramref name="folder"/>. Returns report lines; "FAIL " marks a problem.</summary>
    internal static string RenderForSmoke(DeckHub hub, string folder, Action pump, Action<FrameworkElement, string> snap)
    {
        var report = new System.Text.StringBuilder();
        var window = new WelcomeWindow(hub.PrimaryDisplay, hub.Settings.ShowDesktopIcons);
        window.Show();
        window.UpdateLayout();
        pump();
        window.Surface.Background = Theme.Brush("#0C0A07");
        snap(window.Surface, Path.Combine(folder, "welcome.png"));
        window.Surface.Background = null;
        var placed = window.Placed;
        var work = hub.PrimaryDisplay.WorkArea;
        report.AppendLine($"welcome window {placed.Width}x{placed.Height} px at {placed.X},{placed.Y}; topmost={window.Topmost}; primary work area {work.X},{work.Y} {work.Width}x{work.Height}");
        bool centered = Math.Abs(placed.X + placed.Width / 2 - (work.X + work.Width / 2)) <= 1 && Math.Abs(placed.Y + placed.Height / 2 - (work.Y + work.Height / 2)) <= 1;
        if (placed.Width < 300 || placed.X < work.X || placed.Right > work.Right || placed.Y < work.Y || placed.Bottom > work.Bottom || !centered) report.AppendLine("FAIL welcome window isn't centered on the primary screen");
        window.Close();
        if (window.Choice is not null) report.AppendLine("FAIL a closed window should be a skip");

        // Every starter's widgets exist in this build, and with their real sizes fit the common screens without overlapping.
        foreach (var id in StarterIds)
        {
            var template = WidgetTemplates.Find(id)!;
            var missing = template.Kinds.Where(k => MainWindow.NewItemOfKind(k) is null).ToList();
            if (missing.Count > 0) report.AppendLine($"FAIL starter {id} uses widgets this edition doesn't have: {string.Join(", ", missing)}");
            var slots = template.Kinds.Select(k =>
            {
                var (dw, dh) = ItemKinds.DefaultSize(k);
                var (mw, mh) = ItemKinds.MinimumSize(k);
                return new Layout.TemplateSlot(dw, dh, Math.Min(dw, mw), Math.Min(dh, mh));
            }).ToList();
            foreach (var (w, h) in new[] { (1920.0, 1080.0), (1366.0, 768.0) })
            {
                var spots = Layout.TemplatePlacer.Place(slots, w, h, [new(-100000, -100000, 100000 + 120, 100000 + 60), new(w - 330, -100000, 400000, 100000 + 60)], keepCenterClear: false, grid: Layout.DeckLayout.Grid);
                var rects = spots.Where(s => s is not null).Select(s => s!.Value).ToList();
                bool apart = rects.Select((r, i) => rects.Skip(i + 1).All(o => !Layout.DeckLayout.Overlaps(r, o))).All(ok => ok);
                report.AppendLine($"starter {id} on {w:0}x{h:0}: {rects.Count} of {slots.Count} placed, overlap-free={apart}");
                if (rects.Count != slots.Count || !apart) report.AppendLine($"FAIL starter {id} doesn't fit {w:0}x{h:0}");
            }
        }

        foreach (var id in StarterIds)
        {
            hub.Settings.Items.Clear();
            int added = hub.ApplyWelcome(WidgetTemplates.Find(id), null);
            hub.Primary!.UpdateLayout();
            pump();
            snap(hub.Primary!, Path.Combine(folder, $"welcome-deck-{id}.png"));
            int expected = WidgetTemplates.Find(id)!.Kinds.Length;
            report.AppendLine($"starter {id}: {added} of {expected} widgets placed");
            if (added != expected) report.AppendLine($"FAIL starter {id} placed {added} of {expected}");
        }
        // A starter plus desktop shortcuts: 25 made-up launchers at the importer's grid positions (which stack up past 20), placed in the room the starter leaves.
        hub.Settings.Items.Clear();
        var shortcuts = Enumerable.Range(0, 25).Select(i => new LauncherItem
        {
            Name = "Shortcut " + (i + 1), Target = $@"C:\Smoke\Shortcut{i + 1}.lnk", X = 0.04 + (i % 4) * 0.23, Y = 0.05 + ((i / 4) % 5) * 0.18,
        }).ToList();
        int total = hub.ApplyWelcome(WidgetTemplates.Find("minimal"), shortcuts);
        hub.Primary!.UpdateLayout();
        pump();
        snap(hub.Primary!, Path.Combine(folder, "welcome-deck-minimal-shortcuts.png"));
        var (viewW, viewH, _) = hub.Primary!.TemplateViewport();
        var onDeck = hub.Settings.Items.Select(item =>
        {
            var (dw, dh) = ItemKinds.DefaultSize(item.Kind);
            return new Layout.TileRect(item.X * viewW, item.Y * viewH, item.Width > 0 ? item.Width : dw, item.Height > 0 ? item.Height : dh);
        }).ToList();
        bool separate = onDeck.Select((r, i) => onDeck.Skip(i + 1).All(o => !Layout.DeckLayout.Overlaps(r, o))).All(ok => ok);
        bool inside = onDeck.All(r => r.X >= -0.5 && r.Y >= -0.5 && r.Right <= viewW + 0.5 && r.Bottom <= viewH + 0.5);
        int launchers = hub.Settings.Items.Count(i => i.Kind == ItemKinds.Launcher);
        report.AppendLine($"minimal + shortcuts: {total} items on the deck ({launchers} of {shortcuts.Count} shortcuts placed), overlap-free={separate}, on screen={inside}");
        if (!separate || !inside || launchers == 0 || total != hub.Settings.Items.Count) report.AppendLine("FAIL a starter with desktop shortcuts overlaps or leaves the screen");

        hub.Settings.Items.Clear();
        hub.ApplyWelcome(null, null);
        return report.ToString().TrimEnd();
    }

    private Border BuildCard()
    {
        var starters = StarterIds.Select(WidgetTemplates.Find).Where(t => t is not null).Select(t => t!).ToList();
        var stack = new StackPanel { Width = 680 };

        var header = new Grid { Margin = new Thickness(0, 0, 0, PanelKit.S16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var tile = PanelKit.IconTile("", "#C4684F", 52);
        tile.VerticalAlignment = VerticalAlignment.Top;
        tile.Margin = new Thickness(0, 0, PanelKit.S16, 0);
        header.Children.Add(tile);
        var heading = new StackPanel();
        var title = Theme.Label("Welcome to Desktop Deck", 24);
        title.FontWeight = FontWeights.SemiBold;
        heading.Children.Add(title);
        var intro = Theme.Label("Desktop Deck puts clocks, tasks, timers, launchers and more right on your desktop. Pick a starting point; you can change anything later.", 13.5, Theme.Text2);
        intro.TextWrapping = TextWrapping.Wrap;
        intro.Margin = new Thickness(0, PanelKit.S4, 0, 0);
        heading.Children.Add(intro);
        Grid.SetColumn(heading, 1);
        header.Children.Add(heading);
        stack.Children.Add(header);

        var cards = new UniformGrid { Columns = starters.Count };
        foreach (var starter in starters) cards.Children.Add(StarterCard(starter, starter == starters[^1]));
        stack.Children.Add(cards);

        var footer = new Grid { Margin = new Thickness(0, PanelKit.S16, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition());
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var option = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var toggle = Theme.Switch(false, on => addShortcuts = on);
        toggle.VerticalAlignment = VerticalAlignment.Center;
        System.Windows.Automation.AutomationProperties.SetName(toggle, "Also add my desktop shortcuts as launchers");
        option.Children.Add(toggle);
        var optionLabel = Theme.Label("Also add my desktop shortcuts as launchers", 12.5, Theme.Text2);
        optionLabel.Margin = new Thickness(PanelKit.S8, 0, 0, 0);
        option.Children.Add(optionLabel);
        if (!iconsShown) footer.Children.Add(option);
        var empty = PanelKit.Outline("Start empty", () => Finish(new WelcomeChoice(null, addShortcuts)), "Nothing added yet. Use + Add or the control center when you're ready.");
        Grid.SetColumn(empty, 1);
        footer.Children.Add(empty);
        stack.Children.Add(footer);

        var divider = new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, PanelKit.S16, 0, PanelKit.S12) };
        stack.Children.Add(divider);
        var tips = Theme.Label(Tips, 12, Theme.Muted);
        tips.TextWrapping = TextWrapping.Wrap;
        stack.Children.Add(tips);

        return new Border
        {
            Background = Theme.Background, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
            Padding = new Thickness(PanelKit.S24), Child = stack, Focusable = true, FocusVisualStyle = null,
        };
    }

    private FrameworkElement StarterCard(WidgetTemplate starter, bool last)
    {
        var stack = new StackPanel();
        var tile = PanelKit.IconTile(starter.Icon, starter.Accent, 52, emoji: true);
        tile.HorizontalAlignment = HorizontalAlignment.Left;
        stack.Children.Add(tile);
        var name = Theme.Label(starter.Name, 17);
        name.FontWeight = FontWeights.SemiBold;
        name.Margin = new Thickness(0, PanelKit.S12, 0, 0);
        stack.Children.Add(name);
        var description = Theme.Label(starter.Description, 12, Theme.Muted);
        description.TextWrapping = TextWrapping.Wrap;
        description.Margin = new Thickness(0, PanelKit.S4, 0, PanelKit.S8);
        stack.Children.Add(description);
        var chips = new WrapPanel();
        foreach (var kind in starter.Kinds.Take(6))
        {
            var badge = PanelKit.Badge(ControlCenter.KindName(new LauncherItem { Kind = kind }), starter.Accent);
            badge.Margin = new Thickness(0, 0, PanelKit.S4, PanelKit.S4);
            chips.Children.Add(badge);
        }
        stack.Children.Add(chips);

        var cardView = PanelKit.HoverCard(stack, radius: 14);
        cardView.Padding = new Thickness(PanelKit.S16);
        cardView.Margin = new Thickness(0, 0, last ? 0 : PanelKit.S12, 0);
        PanelKit.MakeFocusable(cardView, () => Finish(new WelcomeChoice(starter, addShortcuts)), "Start with " + starter.Name);
        cardView.MouseLeftButtonUp += (_, e) => { Finish(new WelcomeChoice(starter, addShortcuts)); e.Handled = true; };
        return cardView;
    }
}
