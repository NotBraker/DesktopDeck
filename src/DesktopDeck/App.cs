using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DesktopDeck;

internal sealed class App : Application
{
    private const int HotkeyId = 0xD0C, ControlHotkeyId = 0xD0D, LayoutHotkeyBase = 0xD10;
    private const string ListenerTitle = "DesktopDeck lifecycle";
    private Mutex? mutex;
    private Forms.NotifyIcon? tray;
    private LauncherService? launcher;
    private SettingsStore store = null!;
    private DeckSettings settings = null!;
    private DeckHub? hub;
    private HwndSource? listener;
    private bool exiting, preview, smoke, hostTest, smokeEdit, hotkeyRegistered, controlHotkeyRegistered, widgetShots;
    private DateTime lastErrorBox = DateTime.MinValue;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private readonly uint showAppMessage = RegisterWindowMessage("DesktopDeck.ShowApp");
    private readonly uint exitMessage = RegisterWindowMessage("DesktopDeck.Exit");
    private int ExitCode;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--exit"))
        {
            // Asks a running deck to close cleanly (it saves and stops what it started), e.g. before an update.
            return SignalRunningInstance("DesktopDeck.Exit") ? 0 : 1;
        }
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.preview = args.Contains("--preview");
        app.smoke = args.Contains("--smoke-test");
        app.hostTest = args.Contains("--host-test");
        app.smokeEdit = args.Contains("--edit");
        app.widgetShots = args.Contains("--widgets");
        if (app.hostTest || app.widgetShots) app.smoke = true;
        return app.Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Theme.InstallStyles(this);
        Widgets.WidgetRegistry.Install();
        bool isolated = preview || smoke;
        // Widget renders use their own mutex and settings folder so several can run at once (e.g. in parallel worktrees).
        string instance = widgetShots ? "Widgets" + Guid.NewGuid().ToString("N") : isolated ? "Preview" : "";
        mutex = new Mutex(true, "Local\\DesktopDeck" + instance, out bool first);
        if (!first)
        {
            // Launching Desktop Deck again brings the running deck's widgets forward.
            if (!isolated) SignalRunningInstance("DesktopDeck.ShowApp");
            Shutdown();
            return;
        }
        // A widget that keeps failing (a ticker, a click handler) must not bury the screen in message boxes: show one every half minute at most.
        DispatcherUnhandledException += (_, a) =>
        {
            Log(a.Exception);
            if (DateTime.UtcNow - lastErrorBox > TimeSpan.FromSeconds(30)) { lastErrorBox = DateTime.UtcNow; MessageBox.Show(a.Exception.Message, "Desktop Deck"); }
            a.Handled = true;
        };
        // --welcome starts from nothing in its own folder, like a first run (the shared preview folder is left alone).
        string previewFolder = smoke && Environment.GetCommandLineArgs().Contains("--welcome") ? "DesktopDeck-preview-welcome" : "DesktopDeck-preview";
        store = widgetShots ? new SettingsStore(Path.Combine(Path.GetTempPath(), "DesktopDeck-" + instance))
            : isolated ? new SettingsStore(Path.Combine(Path.GetTempPath(), previewFolder)) : new SettingsStore();
        if (previewFolder.EndsWith("welcome", StringComparison.Ordinal)) try { File.Delete(store.FilePath); } catch (IOException) { }
        if (smoke && Environment.GetCommandLineArgs().Contains("--sample")) SampleDeck.Write(store);
        var firstRun = !File.Exists(store.FilePath);
        try { settings = store.Load(); }
        catch (Exception ex) { Log(ex); MessageBox.Show(ex.Message, "Desktop Deck — settings could not be loaded"); Shutdown(1); return; }
        // The saved theme goes in before any window exists. --smoke-test --theme <name> shows another one without saving it.
        if (smoke && SmokeTheme(Environment.GetCommandLineArgs()) is { } smokeTheme)
        {
            settings.Theme.Name = smokeTheme;
            if (smokeTheme == ThemeSettings.IdCustom) settings.Theme.Custom = new CustomPalette { Background = "#0F2A24", Surface = "#173A32", Text = "#E6F4EF", Muted = "#8FB5A8", Accent = "#2EC4A0" };
        }
        if (smoke) SmokeLook.Apply(settings, Environment.GetCommandLineArgs());
        // --smoke-test --simple renders the simple view (Settings → Show advanced features off); without it the advanced view, whatever an earlier run saved in the shared preview folder.
        if (smoke) settings.AdvancedFeatures = !Environment.GetCommandLineArgs().Contains("--simple");
        // --smoke-test --icons renders with Settings → Display → Show desktop icons on (the gear is left out, the background is the Windows wallpaper); without it off, whatever an earlier run saved.
        if (smoke) settings.ShowDesktopIcons = Environment.GetCommandLineArgs().Contains("--icons");
        Theme.Apply(settings.Theme, this);
        launcher = new LauncherService();
        // A hidden top-level HWND receives shell broadcasts and the hotkey even though the decks are children of the desktop.
        listener = new HwndSource(new HwndSourceParameters(ListenerTitle + (isolated ? " (preview)" : "")) { Width = 0, Height = 0, WindowStyle = 0 });
        listener.AddHook(OnMessage);
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        SystemEvents.PowerModeChanged += OnPowerChanged;

        hub = new DeckHub(settings, store, launcher, preview: preview || (smoke && !hostTest), framed: preview && !smoke, offscreen: widgetShots);
        hub.ExitRequested += ExitDeck;
        hub.Notify += (title, text) => tray?.ShowBalloonTip(6000, title, text, Forms.ToolTipIcon.Info);
        hub.AppHotkeyChanged += SetHotkey;
        hub.LayoutHotkeysChanged += SyncLayoutHotkeys;
        // Snapshots show made-up calendar events; no smoke test ever contacts Google or a calendar host.
        // Every smoke run is inert, with or without --sample (the shared preview folder may hold fake links from an earlier sample run).
        if (smoke) hub.Calendar.UseSample(Environment.GetCommandLineArgs().Contains("--sample") ? SampleDeck.Events(Planner.Today()) : []);
        if (smoke)
        {
            // No smoke test syncs tasks or reads mail. The made-up account has Calendar, Tasks and Gmail but not Classroom, so Settings shows an Enable button.
            hub.Tasks.Inert = true;
            // Nor does one read what is playing on this PC: the Media widget shows a made-up track, or nothing without --sample.
            MediaService.Instance.UseSample(Environment.GetCommandLineArgs().Contains("--sample") ? SampleDeck.Track() : MediaService.Snapshot.None);
            if (Environment.GetCommandLineArgs().Contains("--sample")) hub.Google.SampleScopes = [.. GoogleScopes.Calendar, GoogleScopes.Tasks, GoogleScopes.GmailReadOnly];
        }
        hub.Start();
        if (!preview && !smoke) hub.StartAutomation();
        MainWindow = hub.Primary;
        if (!preview && !smoke && !hub.AllAttached)
            Log(new InvalidOperationException("Desktop attachment unavailable for at least one display; using bottom windows. Use tray Reconnect to desktop."));

        if (!isolated)
        {
            try { new StartupService().SetEnabled(settings.StartAtLogin); } catch (Exception ex) { Log(ex); }
            SetHotkey(settings.AppHotkey);
            SyncLayoutHotkeys();
            BuildTray();
        }
        // First start (no settings file): a welcome with starter layouts instead of importing the desktop's shortcuts.
        if (firstRun && !isolated && settings.Items.Count == 0) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => WelcomeWindow.ShowFirstRun(hub!)));
        if (widgetShots) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RunWidgetShots));
        else if (smoke) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(RunSmoke));
    }


    private static string? SmokeTheme(string[] args)
    {
        int at = Array.IndexOf(args, "--theme");
        return at >= 0 && at + 1 < args.Length && ThemeSettings.Names.Contains(args[at + 1].ToLowerInvariant()) ? args[at + 1].ToLowerInvariant() : null;
    }

    /// <summary>The icon built into the exe (the tile from DesktopDeck.ico); the stock application icon if it can't be read.</summary>
    private static System.Drawing.Icon AppIcon()
    {
        try { if (Environment.ProcessPath is { } exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon) return icon; }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) { }
        return System.Drawing.SystemIcons.Application;
    }

    private void BuildTray()
    {
        tray = new Forms.NotifyIcon { Text = "Desktop Deck", Icon = AppIcon(), Visible = true };
        var menu = new Forms.ContextMenuStrip();
        var advancedOnly = new List<Forms.ToolStripItem>(); // left out of the simple view (Settings → Show advanced features); checked each time the menu opens
        menu.Items.Add("Control center… (Ctrl+Alt+S)", null, (_, _) => Dispatcher.Invoke(() => hub?.OpenControlCenter()));
        menu.Items.Add("New reminder…", null, (_, _) => Dispatcher.Invoke(() => hub?.PromptNewReminder()));
        // The layouts of each layer, switched with one click (the checked one is in use).
        foreach (var (layer, title) in new[] { (LayoutLayers.Floating, "Pinned layout"), (LayoutLayers.Background, "Desktop layout") })
        {
            var submenu = new Forms.ToolStripMenuItem(title);
            advancedOnly.Add(submenu);
            string forLayer = layer;
            submenu.DropDownOpening += (_, _) =>
            {
                submenu.DropDownItems.Clear();
                if (hub is null) return;
                string active = LayoutEngine.ActiveId(hub.Settings, forLayer);
                foreach (var layout in LayoutEngine.Of(hub.Settings, forLayer))
                {
                    string id = layout.Id;
                    var entry = new Forms.ToolStripMenuItem(layout.Name) { Checked = id == active };
                    entry.Click += (_, _) => Dispatcher.Invoke(() => hub?.SwitchLayout(forLayer, id));
                    submenu.DropDownItems.Add(entry);
                }
            };
            submenu.DropDownItems.Add("…");
            menu.Items.Add(submenu);
        }
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Show widgets in front (Ctrl+Alt+D)", null, (_, _) => Dispatcher.Invoke(() => hub?.Peek(true)));
        menu.Items.Add("Edit layout", null, (_, _) => Dispatcher.Invoke(() => { hub?.Peek(true); hub?.SetEditing(true); }));
        // The less used entries live in "More".
        var more = new Forms.ToolStripMenuItem("More");
        var showAs = new Forms.ToolStripMenuItem("Show as");
        advancedOnly.Add(showAs);
        string[] modes = ["background", "window", "ontop"];
        foreach (var (mode, label) in new[] { ("background", "On the desktop"), ("window", "Over my apps (Alt+Tab)"), ("ontop", "Always on top of my apps") })
            showAs.DropDownItems.Add(label, null, (_, _) => Dispatcher.Invoke(() => hub?.SetMode(mode)));
        showAs.DropDownOpening += (_, _) => { for (int i = 0; i < 3; i++) ((Forms.ToolStripMenuItem)showAs.DropDownItems[i]).Checked = modes[i] == hub?.Mode; };
        more.DropDownItems.Add(showAs);
        var hideFloats = new Forms.ToolStripMenuItem("Hide pinned widgets");
        hideFloats.Click += (_, _) => Dispatcher.Invoke(() => hub?.SetFloatsVisible(!(hub?.FloatsVisible ?? true) ? true : false));
        more.DropDownItems.Add(hideFloats);
        var showOrb = new Forms.ToolStripMenuItem("Show the control button") { Visible = false };
        showOrb.Click += (_, _) => Dispatcher.Invoke(() => hub?.SetShowOrb(true));
        more.DropDownItems.Add(showOrb);
        more.DropDownOpening += (_, _) =>
        {
            hideFloats.Checked = hub is { FloatsVisible: false };
            showOrb.Visible = hub is { Settings.ShowOrb: false };
        };
        more.DropDownItems.Add("Settings\u2026", null, (_, _) => Dispatcher.Invoke(() => hub?.OpenSettings()));
        more.DropDownItems.Add("Reconnect to desktop", null, (_, _) => Dispatcher.Invoke(() => hub?.Reconnect()));
        menu.Items.Add(more);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit Desktop Deck", null, (_, _) => Dispatcher.Invoke(ExitDeck));
        menu.Opening += (_, _) => { foreach (var item in advancedOnly) item.Visible = hub?.Advanced ?? true; };
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => hub?.Peek(true));
    }

    // ---------- Smoke and host tests ----------

    /// <summary>Where smoke renders go: verification beside the exe, verification-simple for the simple view, verification-icons with desktop icons shown (so one run never overwrites another's images).</summary>
    private static string SmokeFolder()
    {
        var args = Environment.GetCommandLineArgs();
        string name = "verification" + (args.Contains("--simple") ? "-simple" : "") + (args.Contains("--icons") ? "-icons" : "");
        return Path.Combine(AppContext.BaseDirectory, name);
    }

    private void RunSmoke()
    {
        try
        {
            var deck = hub!.Primary!;
            string folder = SmokeFolder();
            Directory.CreateDirectory(folder);
            var args = Environment.GetCommandLineArgs();
            // --switch-theme <name> changes the theme after everything is up (the way the control center does), to check that a running deck redraws completely.
            int switchAt = Array.IndexOf(args, "--switch-theme");
            if (switchAt >= 0 && switchAt + 1 < args.Length && ThemeSettings.Names.Contains(args[switchAt + 1]))
            {
                hub.ThemeOptions.Name = args[switchAt + 1];
                if (args[switchAt + 1] == ThemeSettings.IdCustom) hub.ThemeOptions.Custom = new CustomPalette { Background = "#0F2A24", Surface = "#173A32", Text = "#E6F4EF", Muted = "#8FB5A8", Accent = "#2EC4A0" };
                hub.ApplyTheme();
                for (int i = 0; i < 4; i++) { Pump(); Thread.Sleep(30); }
            }
            if (smokeEdit) hub.SetEditing(true);
            // --select shows a sample selection (outlines and the toolbar) in the edit-layout snapshot.
            if (smokeEdit && args.Contains("--select")) deck.SelectSampleForSmoke(3);
            deck.UpdateLayout();
            Snap(deck, Path.Combine(folder, "desktop-deck.png"));
            string report = hostTest ? ExerciseHost() : "Preview (no desktop attachment)\nchrome " + deck.ChromeScreenBounds() + " " + deck.ChromeDiagnostics();
            if (args.Contains("--settings"))
            {
                SnapSettings(Path.Combine(folder, "settings.png"));
                // The same page as it looks before a Google account is connected (setup guide).
                if (hub.Google.SampleMode)
                {
                    hub.Google.SampleMode = false;
                    SnapSettings(Path.Combine(folder, "settings-signed-out.png"));
                    hub.Google.SampleMode = true;
                }
            }
            int panelAt = Array.IndexOf(args, "--panel");
            if (panelAt >= 0)
            {
                // --panel <tab> renders one tab of the control center (panel-<tab>.png and the whole tab as panel-<tab>-full.png); --panel all renders every tab.
                string which = panelAt + 1 < args.Length && !args[panelAt + 1].StartsWith("--", StringComparison.Ordinal) ? args[panelAt + 1] : "all";
                if (which == "history" && args.Contains("--sample")) hub!.SeedHistorySample();
                foreach (var tab in ControlCenter.TabsFor(hub.Advanced).Select(t => t.Id).Where(id => which == "all" || which == id)) SnapPanel(tab, folder);
                if (which == "history" && args.Contains("--sample")) report += "\n" + hub!.VerifyHistory();
            }
            if (smokeEdit && args.Contains("--select-test")) {
                string exercise = deck.ExerciseSelectionForSmoke(name => Snap(deck, Path.Combine(folder, name)));
                report += Environment.NewLine + exercise;
                if (exercise.Contains("FAIL ", StringComparison.Ordinal)) ExitCode = 1;
            }
            if (args.Contains("--customize") && !SmokeLook.SnapCustomize(deck, settings, folder, Snap)) ExitCode = 1;
            if (args.Contains("--link-test"))
            {
                string linkReport = hub!.VerifyLinks(Pump, (element, file) =>
                {
                    if (element is not Window)
                    {
                        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                        element.Arrange(new Rect(element.DesiredSize));
                    }
                    element.UpdateLayout();
                    Snap(element, Path.Combine(folder, file));
                });
                report += Environment.NewLine + linkReport;
                if (linkReport.Contains("FAIL ", StringComparison.Ordinal)) ExitCode = 1;
            }
            if (args.Contains("--welcome"))
            {
                string welcome = WelcomeWindow.RenderForSmoke(hub, folder, Pump, Snap);
                report += Environment.NewLine + welcome;
                if (welcome.Contains("FAIL ", StringComparison.Ordinal)) ExitCode = 1;
            }
            if (args.Contains("--toast"))
            {
                string toasts = ReminderToasts.RenderForSmoke(hub!, folder, Snap);
                report += Environment.NewLine + toasts;
                if (toasts.Contains("FAIL ", StringComparison.Ordinal)) ExitCode = 1;
            }
            if (args.Contains("--dialogs")) SnapCalendarDialogs(deck, folder);
            if (args.Contains("--menus"))
            {
                string menus = deck.RenderMenusForSmoke(folder);
                report += "\n" + menus;
                if (menus.Contains("FAIL ", StringComparison.Ordinal)) ExitCode = 1;
            }
            if (args.Contains("--front") && !hostTest)
            {
                hub.Peek(true);
                var front = hub.PrimaryFront!;
                front.UpdateLayout();
                Snap(front, Path.Combine(folder, "front.png"));
                hub.Peek(false);
            }
            int number = 0;
            foreach (var floating in hub.Floats.Windows.ToList())
            {
                floating.UpdateLayout();
                Snap((FrameworkElement)floating.Content, Path.Combine(folder, $"float-{++number}.png"));
                report += Environment.NewLine + $"float {number}: {floating.Title} topmost={floating.Topmost} {floating.ActualWidth}x{floating.ActualHeight} at {floating.Bounds}";
            }
            File.WriteAllText(Path.Combine(folder, "host.txt"), report);
        }
        catch (Exception ex) { Log(ex); ExitCode = 1; File.WriteAllText(Path.Combine(SmokeFolder(), "host.txt"), "FAILED: " + ex); }
        ExitDeck();
    }

    /// <summary>
    /// --smoke-test --widgets [--only kind]: checks every plug-in widget's definition and renders each of its samples
    /// (and its minimum size) to verification/widgets/kind-N.png, with a report in verification/widgets/report.txt.
    /// Exit code 1 if any widget is invalid or throws while drawing.
    /// </summary>
    private void RunWidgetShots()
    {
        var args = Environment.GetCommandLineArgs();
        int onlyAt = Array.IndexOf(args, "--only");
        string? only = onlyAt >= 0 && onlyAt + 1 < args.Length ? args[onlyAt + 1] : null;
        string folder = Path.Combine(AppContext.BaseDirectory, "verification", "widgets");
        Directory.CreateDirectory(folder);
        var surface = hub!.Primary!;
        var report = new List<string>();
        var shots = new List<(FrameworkElement Host, string Path)>();
        void Fail(string text) { report.Add("FAIL " + text); ExitCode = 1; }
        var widgets = Widgets.WidgetRegistry.All.Where(w => only is null || w.Kind == only).ToList();
        if (widgets.Count == 0) Fail(only is null ? "no plug-in widgets found" : $"no widget with kind \"{only}\"");
        if (only is null) foreach (var kind in Widgets.OptionTiers.UnknownKinds(Widgets.WidgetRegistry.All.Select(w => w.Kind))) Fail($"OptionTiers names the widget \"{kind}\", which does not exist");
        foreach (var widget in widgets)
        {
            // Definition checks.
            if (widget.Options.Count < 8) Fail($"{widget.Kind}: expected at least eight widget-specific options");
            foreach (var problem in Widgets.OptionTiers.Problems(widget.Kind, widget.Options.Select(o => (o.Key, o.Group)).ToList())) Fail(problem);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var spec in widget.Options)
            {
                if (!keys.Add(spec.Key) || spec.Key.StartsWith('_')) Fail($"{widget.Kind}: option key \"{spec.Key}\" is duplicated or starts with _");
                if (spec.Type == Widgets.OptionType.Choice && !spec.Choices.Contains(spec.Default)) Fail($"{widget.Kind}: choice \"{spec.Key}\" default \"{spec.Default}\" is not one of its choices");
                if (spec.Type == Widgets.OptionType.Number && (!double.TryParse(spec.Default, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) || d < spec.Min || d > spec.Max)) Fail($"{widget.Kind}: number \"{spec.Key}\" default is outside {spec.Min}–{spec.Max}");
                if (spec.ChoiceLabels is { } labels && labels.Length != spec.Choices.Length) Fail($"{widget.Kind}: \"{spec.Key}\" has {labels.Length} labels for {spec.Choices.Length} choices");
            }
            if (widget.DefaultSize.Width < widget.MinimumSize.Width || widget.DefaultSize.Height < widget.MinimumSize.Height) Fail($"{widget.Kind}: default size is smaller than the minimum");
            if (Array.IndexOf(Widgets.WidgetRegistry.Sections, widget.Section) < 0) Fail($"{widget.Kind}: unknown section \"{widget.Section}\"");

            int index = 0;
            var samples = widget.Samples.ToList();
            if (samples.Count < 2) Fail($"{widget.Kind}: expected at least two sample variants");
            // Also exercise a newly added widget with no saved options or sample data, then its shared appearance.
            samples.Add(_ => { });
            samples.Add(item => { item.Width = widget.MinimumSize.Width; item.Height = widget.MinimumSize.Height; });
            samples.Add(item =>
            {
                item.Options["_textScale"] = "150";
                item.Options["_padding"] = "4";
                item.Options["_radius"] = "0";
                item.Options["_opacity"] = "50";
                item.Options["_textColor"] = "#E8EDF5";
                item.Options["_cardColor"] = "#18334A";
                item.ShowHeader = false;
            });
            foreach (var sample in samples.Append(null))
            {
                var item = new LauncherItem { Kind = widget.Kind, Name = widget.Name, Accent = widget.Accent };
                string name;
                double w, h;
                if (sample is null)
                {
                    // The first sample again, at the minimum size.
                    try { samples.FirstOrDefault()?.Invoke(item); } catch (Exception) { }
                    (w, h) = widget.MinimumSize;
                    name = $"{widget.Kind}-min.png";
                }
                else
                {
                    try { sample(item); }
                    catch (Exception ex) { Fail($"{widget.Kind} sample {index}: {ex.GetType().Name}: {ex.Message}"); index++; continue; }
                    (w, h) = item.Width > 0 && item.Height > 0 ? (item.Width, item.Height) : widget.DefaultSize;
                    name = $"{widget.Kind}-{index++}.png";
                }
                FrameworkElement content;
                try { content = widget.Build(new Widgets.WidgetContext(surface, item, widget, w, h, snapshot: true)); }
                catch (Exception ex) { Fail($"{widget.Kind} {name}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"); continue; }
                var box = new System.Windows.Controls.Grid { Width = w, Height = h };
                box.Children.Add(content);
                var host = new System.Windows.Controls.Border { Background = Theme.Background, Padding = new Thickness(14), Child = box };
                shots.Add((host, Path.Combine(folder, name)));
                report.Add($"OK   {widget.Kind} {name} {w}x{h} ({widget.Options.Count} options)");
            }
        }
        // Let deferred work (icons, layout) settle, then render everything.
        var settle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            foreach (var (host, path) in shots)
            {
                try
                {
                    host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    host.Arrange(new Rect(host.DesiredSize));
                    host.UpdateLayout();
                    Snap(host, path);
                }
                catch (Exception ex) { Fail($"{Path.GetFileName(path)} render: {ex.Message}"); }
            }
            report.Insert(0, $"{widgets.Count} widget(s), {shots.Count} image(s), {report.Count(l => l.StartsWith("FAIL", StringComparison.Ordinal))} failure(s)");
            File.WriteAllLines(Path.Combine(folder, "report.txt"), report);
            ExitDeck();
        };
        foreach (var (host, _) in shots) { host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); host.Arrange(new Rect(host.DesiredSize)); }
        settle.Start();
    }

    /// <summary>Renders one tab of the control center (offscreen) to verification/panel-&lt;tab&gt;.png, and the whole tab, not just the part the panel shows, to panel-&lt;tab&gt;-full.png.</summary>
    private void SnapPanel(string tab, string folder)
    {
        var center = new ControlCenter(hub!, hub!.PrimaryDisplay, tab, smoke: true) { Left = -4000, Top = 0, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        center.Show();
        for (int i = 0; i < 4; i++) { Pump(); Thread.Sleep(30); }
        center.RenderForSmoke(folder, Snap);
        center.Close();
    }

    /// <summary>The Settings tab as one tall image (settings.png), for checking the whole page.</summary>
    private void SnapSettings(string path)
    {
        var center = new ControlCenter(hub!, hub!.PrimaryDisplay, "settings", smoke: true) { Left = -4000, Top = 0, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        center.Show();
        for (int i = 0; i < 4; i++) { Pump(); Thread.Sleep(30); }
        center.RenderForSmoke(Path.GetDirectoryName(path)!, (element, file) =>
        {
            // The full-height image is the one wanted here; the panel-sized one is only a by-product.
            if (file.EndsWith("-full.png", StringComparison.Ordinal)) Snap(element, path);
        });
        center.Close();
    }

    /// <summary>--smoke-test --sample --dialogs: renders the calendar editor, details and "local or Google?" windows (sample data only).</summary>
    private void SnapCalendarDialogs(MainWindow owner, string folder)
    {
        var today = Planner.Today();
        var events = SampleDeck.Events(today);
        var google = events.First(e => e.Source == CalendarSource.Google && e.Title == "Dentist");
        var repeating = google with { Title = "Weekly sync", RecurringEventId = "series1", Description = "Agenda is in the shared doc. Bring questions." };
        var link = events.First(e => e.Source == CalendarSource.Ics && e.Title == "Chemistry lab") with { Description = "Bring goggles and the lab notebook." };
        var windows = new (string Name, Window Window)[]
        {
            ("dialog-add", new AddChoiceWindow(owner, today)),
            ("dialog-editor-new", new EventEditorWindow(owner, hub!.Calendar, null, today, 14 * 60)),
            ("dialog-editor-edit", new EventEditorWindow(owner, hub.Calendar, google, google.FirstDay, null)),
            ("dialog-editor-repeating", new EventEditorWindow(owner, hub.Calendar, repeating, repeating.FirstDay, null)),
            ("dialog-details", new EventDetailsWindow(owner, link)),
            ("dialog-link", new LinkDialog(owner, "Homework", [new LinkChoice(new LauncherItem(), "Homework", "Desktop · Display 2", "8 items"), new LinkChoice(new LauncherItem(), "Homework (floating)", "Floating · Display 1 · linked with 1 other", "5 items")])),
            ("dialog-keep", new KeepDataDialog(owner, new LinkChoice(new LauncherItem(), "Homework", "Desktop · Display 2", "8 items"), new LinkChoice(new LauncherItem(), "Homework (floating)", "Floating · Display 1", "5 items"))),
        };
        foreach (var (name, window) in windows)
        {
            window.Left = -4000;
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Show();
            window.UpdateLayout();
            double width = window.ActualWidth;
            FrameworkElement inner = window.Content is System.Windows.Controls.ScrollViewer { Content: FrameworkElement scrolled } scroll ? scrolled : (FrameworkElement)window.Content;
            if (window.Content is System.Windows.Controls.ScrollViewer sv) sv.Content = null;
            window.Content = null;
            var host = new System.Windows.Controls.Border { Background = Theme.Background, Child = inner, Width = width };
            host.Measure(new Size(width, double.PositiveInfinity));
            host.Arrange(new Rect(0, 0, width, host.DesiredSize.Height));
            host.UpdateLayout();
            Snap(host, Path.Combine(folder, name + ".png"));
            window.Close();
        }
    }

    /// <summary>Saves a top-level window, frame included, as a PNG (PrintWindow with PW_RENDERFULLCONTENT).</summary>
    private static void CaptureWindow(nint hwnd, string path)
    {
        var rect = NativeWindow.Rect(hwnd);
        using var bitmap = new System.Drawing.Bitmap(Math.Max(1, rect.Width), Math.Max(1, rect.Height));
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            var hdc = graphics.GetHdc();
            try { PrintWindow(hwnd, hdc, 2); }
            finally { graphics.ReleaseHdc(hdc); }
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static void Snap(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)element.ActualWidth), Math.Max(1, (int)element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    /// <summary>
    /// On the real desktop: checks every monitor's deck (attached, exact work area), the floating layer (own windows, saved positions,
    /// moving, hiding, shown in every mode), each display mode's deck window (framed, taskbar, topmost only when asked, the desktop decks
    /// untouched), peek cycles and the top-right controls staying on screen.
    /// </summary>
    private string ExerciseHost()
    {
        var lines = new List<string>();
        try { ExerciseHostCore(lines); }
        catch (Exception ex) { lines.Add("FAIL the host test stopped: " + ex); ExitCode = 1; }
        return string.Join("\n", lines);
    }

    private void ExerciseHostCore(List<string> lines)
    {
        // Each result is also appended to host-progress.txt as it happens, so a run that dies half way still shows how far it got.
        string progress = Path.Combine(AppContext.BaseDirectory, "verification", "host-progress.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(progress)!);
        File.WriteAllText(progress, "");
        void Check(bool ok, string what) { string line = $"{(ok ? "OK  " : "FAIL")} {what}"; lines.Add(line); try { File.AppendAllText(progress, line + Environment.NewLine); } catch (IOException) { } if (!ok) ExitCode = 1; }
        void Settle(int rounds = 8) { for (int i = 0; i < rounds; i++) { Pump(); Thread.Sleep(40); } }
        var h = hub!;
        Check(h.Backgrounds.Count() == h.Displays.Count, $"{h.Displays.Count} display(s), {h.Backgrounds.Count()} background deck(s)");
        bool DesktopsIntact() => h.Displays.All(d => h.HostFor(d.Id) is { IsAttached: true, IsChild: true } host && host.ScreenRect() == d.WorkArea);
        foreach (var display in h.Displays)
        {
            var host = h.HostFor(display.Id);
            var rect = host?.ScreenRect();
            Check(host is { IsAttached: true, IsChild: true } && rect == display.WorkArea, $"{display.Name}: attached={host?.IsAttached} child={host?.IsChild} rect={rect} work={display.WorkArea} — {host?.Diagnostics}");
        }

        // The top-right controls (Edit layout and friends) stay inside every surface, in every state.
        bool ChromeFits(MainWindow surface, out string detail)
        {
            var chrome = surface.ChromeScreenBounds();
            var area = surface.Display.WorkArea;
            detail = $"{surface.Display.Name}: controls {chrome} in {area}";
            return chrome.Width > 0 && chrome.Right <= area.Right - 8 && chrome.X >= area.X && chrome.Y >= area.Y;
        }
        void ChromeCheck(string when)
        {
            Settle(3);
            foreach (var surface in h.Surfaces)
            {
                bool fits = ChromeFits(surface, out string detail);
                Check(fits, $"{when}: {(surface.IsFront ? "front" : "desktop deck")} controls inside the screen ({detail}) {surface.ChromeDiagnostics()}");
            }
        }
        ChromeCheck("start");

        // Floating widgets: one on the main monitor placed from its old deck spot (an older Keep on top), one saved for the second monitor.
        var clock = new LauncherItem { Kind = ItemKinds.Clock, Name = "Host test clock", X = 0.3, Y = 0.3, OnTop = true };
        var lost = new LauncherItem { Kind = ItemKinds.Counter, Name = "Host test lost", X = 0.5, Y = 0.5, Display = "unplugged-monitor" };
        var second = h.Displays.FirstOrDefault(d => !d.IsPrimary);
        var away = second is null ? null : new LauncherItem { Kind = ItemKinds.Counter, Name = "Host test second", X = 0.2, Y = 0.2, Display = second.Id };
        var floater = second is null ? null : new LauncherItem { Kind = ItemKinds.Counter, Name = "Host test float two", OnTop = true, FloatDisplay = second.Id, FloatX = 0.1, FloatY = 0.2, FloatWidth = 200, FloatHeight = 150 };
        settings.Items.Add(clock); settings.Items.Add(lost); if (away is not null) settings.Items.Add(away); if (floater is not null) settings.Items.Add(floater);
        h.RenderAll();
        h.Primary!.UpdateLayout();
        Settle(4);
        var primaryDisplay = h.PrimaryDisplay;
        Check(h.Primary.Shows(lost.Id), "item on an unplugged display shows on the main display");
        if (away is not null && second is not null) Check(h.Backgrounds.Single(b => b.Display.Id == second.Id).Shows(away.Id) && !h.Primary.Shows(away.Id), $"item saved for {second.Name} shows there only");
        Check(h.Surfaces.All(sf => !sf.Shows(clock.Id)) && h.Surfaces.All(sf => floater is null || !sf.Shows(floater.Id)), "kept-on-top widgets take no room on any deck");

        var floating = h.Floats.Find(clock.Id);
        long floatStyle = NativeWindow.ExStyle(floating?.Handle ?? 0);
        Check(floating is not null && (floatStyle & NativeWindow.ExToolWindow) != 0 && (floatStyle & NativeWindow.ExNoActivate) != 0 && (floatStyle & NativeWindow.ExTopmost) != 0,
            $"kept-on-top widget floats: tool+noactivate+topmost rect={NativeWindow.Rect(floating?.Handle ?? 0)} expected={floating?.Bounds}");
        var (clockW, clockH) = FloatLayer.SizeFor(clock);
        var clockExpected = h.Floats.BoundsFor(clock, primaryDisplay, clockW, clockH);
        Check(floating is not null && NativeWindow.Rect(floating.Handle) == clockExpected && clockExpected.X == primaryDisplay.WorkArea.X + (int)Math.Round(0.3 * primaryDisplay.WorkArea.Width), $"float sits at its own saved spot (FloatX 0.3 of the work area): rect={NativeWindow.Rect(floating?.Handle ?? 0)} expected={clockExpected}");
        if (floater is not null && second is not null)
        {
            var window = h.Floats.Find(floater.Id);
            var expected = new PixelRect(second.WorkArea.X + (int)Math.Round(0.1 * second.WorkArea.Width), second.WorkArea.Y + (int)Math.Round(0.2 * second.WorkArea.Height), (int)Math.Round(200 * second.Scale), (int)Math.Round(150 * second.Scale));
            Check(window is not null && NativeWindow.Rect(window.Handle) == expected, $"float saved for {second.Name} sits there: rect={NativeWindow.Rect(window?.Handle ?? 0)} expected={expected}");
        }
        int floatTotal = h.Floats.Windows.Count;

        // Fade when idle and click-through: the window style and the fade target follow the options; Ctrl brings clicks back, and it never takes focus.
        if (floating is not null)
        {
            var inside = (floating.Bounds.X + floating.Bounds.Width / 2, floating.Bounds.Y + floating.Bounds.Height / 2);
            var outside = (primaryDisplay.WorkArea.Right + 3000, primaryDisplay.WorkArea.Bottom + 3000);
            var later = DateTime.UtcNow.AddSeconds(30);
            settings.ClickThroughHintShown = false;
            clock.FloatClickThrough = true; clock.FloatIdleFade = true; clock.FloatIdleOpacity = 35;
            h.Floats.Sync(false);
            Settle(2);
            Check(settings.ClickThroughHintShown, "the first click-through float shows its \"Hold Ctrl\" hint once and remembers it");
            floating.Poll(outside, false, later);
            Check(floating.PassesClicks && Math.Abs(floating.FadeTarget - 0.35) < 0.001 && (NativeWindow.ExStyle(floating.Handle) & NativeWindow.ExNoActivate) != 0, $"click-through float: transparent style on, idle target 35%, still no-activate (target {floating.FadeTarget})");
            floating.Poll(inside, false, later);
            Check(floating.PassesClicks && Math.Abs(floating.FadeTarget - 0.35) < 0.001, "pointer over a click-through float without Ctrl: clicks still pass, it stays faded");
            floating.Poll(inside, true, later);
            Check(!floating.PassesClicks && floating.FadeTarget == 1, "Ctrl with the pointer over it: clicks are accepted and it is fully visible");
            floating.Poll(inside, false, later);
            Check(!floating.PassesClicks, "after letting go of Ctrl it stays usable while the pointer is over it");
            floating.Poll(outside, false, later);
            Check(floating.PassesClicks, "when the pointer leaves, clicks pass through again");
            clock.FloatClickThrough = false;
            h.Floats.Sync(false);
            Check(!floating.PassesClicks, "turning click-through off (menu or undo) removes the transparent style at once");
            floating.Poll(outside, false, later.AddSeconds(30));
            Check(Math.Abs(floating.FadeTarget - 0.35) < 0.001, "idle fade alone: a float nobody is pointing at fades to its chosen opacity");
            clock.FloatIdleFade = false;
            h.Floats.Sync(false);
            Check(floating.FadeTarget == 1 && !floating.NeedsPolling, "turning fade off brings it back to full and stops the pointer watch for it");
            clock.FloatIdleOpacity = 35;
        }

        // Moving: the same path as a drag (grip or Alt+drag) followed by the mouse release.
        if (floating is not null)
        {
            var work = primaryDisplay.WorkArea;
            floating.MoveTo(work.X + 1200, work.Y + 500);
            floating.Finish();
            Settle(2);
            var movedTo = NativeWindow.Rect(floating.Handle);
            Check(clock.FloatDisplay == primaryDisplay.Id && Math.Abs(clock.FloatX - 1200.0 / work.Width) < 0.0005 && Math.Abs(clock.FloatY - 500.0 / work.Height) < 0.0005 && movedTo == new PixelRect(work.X + 1200, work.Y + 500, clockExpected.Width, clockExpected.Height),
                $"moving a float saves display/position and keeps the window there: FloatX={clock.FloatX:0.####} FloatY={clock.FloatY:0.####} rect={movedTo}");
            Check(h.Store.Load().Items.FirstOrDefault(i => i.Id == clock.Id) is { } saved && saved.FloatDisplay == clock.FloatDisplay && saved.FloatX == clock.FloatX && saved.FloatY == clock.FloatY && saved.FloatWidth > 0, "the moved float's spot is in the saved settings file");
            floating.MoveTo(work.Right + 4000, work.Bottom + 4000);
            floating.Finish();
            Settle(2);
            var clamped = NativeWindow.Rect(floating.Handle);
            Check(clamped.X >= work.X && clamped.Right <= work.Right && clamped.Y >= work.Y && clamped.Bottom <= work.Bottom, $"a float dropped far off screen is brought back inside ({clamped})");
            floating.MoveTo(work.X + 5, work.Y + 7);
            floating.Finish();
            Check(NativeWindow.Rect(floating.Handle) is { } snapped && snapped.X == work.X && snapped.Y == work.Y && clock.FloatX == 0 && clock.FloatY == 0, "a float dropped near a screen corner snaps to it");
            if (second is not null)
            {
                floating.MoveTo(second.WorkArea.X + 100, second.WorkArea.Y + 300);
                floating.Finish();
                Settle(2);
                Check(clock.FloatDisplay == second.Id && NativeWindow.Rect(floating.Handle) == new PixelRect(second.WorkArea.X + 100, second.WorkArea.Y + 300, clockExpected.Width, clockExpected.Height), $"dragging a float onto another monitor moves it there (FloatDisplay={clock.FloatDisplay == second.Id})");
                // The monitor it floats on is unplugged: it shows on the main monitor, at a position kept inside, without losing its saved monitor.
                string savedDisplay = clock.FloatDisplay;
                clock.FloatDisplay = "unplugged-monitor";
                clock.FloatX = 0.99; clock.FloatY = 0.99;
                h.Floats.Sync(false);
                var onMain = NativeWindow.Rect(floating.Handle);
                Check(onMain.X >= work.X && onMain.Right <= work.Right && onMain.Y >= work.Y && onMain.Bottom <= work.Bottom && clock.FloatDisplay == "unplugged-monitor", $"a float whose monitor is gone shows on the main monitor, inside it ({onMain})");
                clock.FloatDisplay = savedDisplay;
            }
            // Put it where the later checks expect it.
            clock.FloatDisplay = primaryDisplay.Id; clock.FloatX = 0.3; clock.FloatY = 0.3;
            h.Floats.Sync(false);
        }

        // The real thing, with real input events: hover the slim grip bar at a float's top edge, press, drag, release. Skipped when something
        // else covers the spot or the machine doesn't deliver the input (for example someone is using the mouse at that moment).
        // It moves the real mouse pointer, so it only runs when asked for (--real-mouse): never surprise someone using the PC.
        if (!Environment.GetCommandLineArgs().Contains("--real-mouse")) lines.Add("SKIP real mouse drag: add --real-mouse to move the real pointer");
        else if (h.Floats.Find(clock.Id) is { } dragged)
        {
            var home = new PixelRect(primaryDisplay.WorkArea.X + 700, primaryDisplay.WorkArea.Y + 600, dragged.Bounds.Width, dragged.Bounds.Height);
            dragged.MoveTo(home.X, home.Y);
            dragged.Finish();
            Settle(3);
            GetCursorPos(out var savedCursor);
            int gripX = home.X + home.Width / 2, gripY = home.Y + 8;
            bool shown = false;
            for (int attempt = 0; attempt < 4 && !shown; attempt++)
            {
                MouseMoveTo(gripX + 3, gripY + 2);
                Settle(2);
                MouseMoveTo(gripX, gripY);
                Settle(5);
                shown = dragged.MoveGripShown;
            }
            nint underCursor = WindowFromPoint(new NativePoint(gripX, gripY));
            if (underCursor != dragged.Handle) lines.Add($"SKIP real mouse drag: another window covers the float's top edge ({Describe(underCursor)})");
            else if (!shown) { GetCursorPos(out var nowAt); lines.Add($"SKIP real mouse drag: the pointer did not reach the float (the grip bar never appeared; wanted {gripX},{gripY}, pointer is at {nowAt.X},{nowAt.Y}, float {NativeWindow.Rect(dragged.Handle)} mouseOver={dragged.IsMouseOver} visible={dragged.IsVisible})"); }
            else
            {
                MouseEvent(0x0002);
                Settle(3);
                if (!dragged.IsGesturing) { MouseEvent(0x0004); lines.Add("SKIP real mouse drag: the mouse press did not start a drag"); }
                else
                {
                    GetCursorPos(out var start);
                    for (int step = 1; step <= 12; step++) { SetCursorPos(start.X + step * 20, start.Y + step * 10); Pump(); Thread.Sleep(15); }
                    GetCursorPos(out var end);
                    MouseEvent(0x0004);
                    Settle(4);
                    var landed = NativeWindow.Rect(dragged.Handle);
                    int expectX = home.X + (end.X - start.X), expectY = home.Y + (end.Y - start.Y);
                    Check(!dragged.IsGesturing && landed.X == expectX && landed.Y == expectY && expectX > home.X + 200 && Math.Abs(clock.FloatX - (landed.X - primaryDisplay.WorkArea.X) / (double)primaryDisplay.WorkArea.Width) < 1e-6,
                        $"dragging the grip bar with the real mouse moves the float, follows the pointer to the end, and saves where it was dropped ({home.X},{home.Y} -> {landed.X},{landed.Y}, FloatX={clock.FloatX:0.####})");
                }
            }
            SetCursorPos(savedCursor.X, savedCursor.Y);
            clock.FloatDisplay = primaryDisplay.Id; clock.FloatX = 0.3; clock.FloatY = 0.3;
            h.Floats.Sync(false);
            Settle(2);
        }

        // Resize (corner grip path): never below the minimum size, the content follows once settled, and the new size is saved.
        if (h.Floats.Find(clock.Id) is { } resizable)
        {
            var (minW, minH) = ItemKinds.MinimumSize(clock.Kind);
            resizable.ResizeTo(1, 1);
            resizable.Finish();
            Settle(3);
            var smallest = NativeWindow.Rect(resizable.Handle);
            Check(Math.Abs(clock.FloatWidth - minW) < 1 && Math.Abs(clock.FloatHeight - minH) < 1 && smallest.Width == (int)Math.Round(minW * primaryDisplay.Scale), $"resizing a float stops at the widget's minimum size ({clock.FloatWidth}x{clock.FloatHeight}, window {smallest.Width}x{smallest.Height})");
            resizable.ResizeTo((int)Math.Round(420 * primaryDisplay.Scale), (int)Math.Round(200 * primaryDisplay.Scale));
            resizable.Finish();
            Settle(3);
            Check(Math.Abs(clock.FloatWidth - 420) < 1 && Math.Abs(clock.FloatHeight - 200) < 1 && h.Floats.Find(clock.Id)!.ContentSize == (420, 200), $"a resized float saves its size ({clock.FloatWidth}x{clock.FloatHeight})");
            clock.FloatWidth = clock.FloatHeight = 0;
            h.Floats.Sync(false);
        }

        // Keep on top ON copies the deck spot; OFF puts it back near where it floated.
        var deckSpot = h.Primary.DeckPlacement(lost);
        h.SetOnTop(lost, true);
        Settle(3);
        var lostWindow = h.Floats.Find(lost.Id);
        Check(deckSpot is { } spot && lost.OnTop && lostWindow is not null && Math.Abs(lost.FloatX - spot.X) < 1e-6 && Math.Abs(lost.FloatY - spot.Y) < 1e-6 && lost.FloatWidth == spot.Width && lost.FloatHeight == spot.Height && !h.Primary.Shows(lost.Id) && lost.FloatDisplay == primaryDisplay.Id,
            $"Keep on top ON: floats from its exact deck spot (deck {deckSpot?.X:0.###},{deckSpot?.Y:0.###} -> FloatX={lost.FloatX:0.###} FloatY={lost.FloatY:0.###} {lost.FloatWidth}x{lost.FloatHeight}), leaves the deck");
        lostWindow?.MoveTo(primaryDisplay.WorkArea.X + 1500, primaryDisplay.WorkArea.Y + 300);
        lostWindow?.Finish();
        double floatedX = lost.FloatX, floatedY = lost.FloatY;
        h.SetOnTop(lost, false);
        Settle(3);
        Check(!lost.OnTop && h.Floats.Find(lost.Id) is null && h.Primary.Shows(lost.Id) && Math.Abs(lost.X - floatedX) < 0.1 && Math.Abs(lost.Y - floatedY) < 0.1, $"Keep on top OFF: back on the deck near where it floated (floated {floatedX:0.###},{floatedY:0.###} -> deck {lost.X:0.###},{lost.Y:0.###})");

        // Hiding: one widget, then the whole floating layer.
        clock.Hidden = true; h.RenderAll();
        Check(h.Floats.Find(clock.Id) is null && h.Floats.Windows.Count == floatTotal - 1, "a hidden float is not shown");
        clock.Hidden = false; h.RenderAll();
        Check(h.Floats.Find(clock.Id) is not null && h.Floats.Windows.Count == floatTotal, "an unhidden float is back");
        h.SetFloatsVisible(false);
        Check(h.Floats.Windows.Count == 0 && settings.FloatsHidden, "Hide floating widgets: none are shown, the switch is saved");
        h.SetFloatsVisible(true);
        Settle(2);
        Check(h.Floats.Windows.Count == floatTotal && !settings.FloatsHidden && h.Floats.Windows.All(w => NativeWindow.Rect(w.Handle) == w.Bounds), "Show floating widgets: all are back, each where it was");
        lost.Hidden = true; h.RenderAll();
        Check(!h.Primary.Shows(lost.Id) && h.HiddenCount == 1, "a hidden widget is not on any deck");
        h.SetItemHidden(lost, false);
        Check(h.Primary.Shows(lost.Id) && h.HiddenCount == 0, "Showing a hidden widget (the control center's Visible switch) brings it back");

        // Every display mode: Window and Always on top are one transparent overlay per monitor (background-layer widgets only); the desktop decks and the floats never change.
        string[] sequence = ["window", "ontop", "background", "ontop", "window", "background"];
        foreach (var mode in sequence)
        {
            h.SetMode(mode);
            Settle(6);
            bool desktops = DesktopsIntact();
            bool floatsOk = h.Floats.Windows.Count == floatTotal && h.Floats.Windows.All(w => w.IsVisible && NativeWindow.Rect(w.Handle) == w.Bounds && (NativeWindow.ExStyle(w.Handle) & NativeWindow.ExTopmost) != 0);
            if (mode == "background")
            {
                Check(!h.Fronts.Any() && desktops && floatsOk, $"mode background: no overlay, desktop decks attached, floats shown (desktops={desktops} floats={floatsOk})");
                continue;
            }
            bool overlays = h.Fronts.Count() == h.Displays.Count;
            foreach (var display in h.Displays)
            {
                var front = h.Fronts.FirstOrDefault(f => f.Display.Id == display.Id);
                if (front is null) { overlays = false; continue; }
                long ex = NativeWindow.ExStyle(front.Hwnd);
                bool topmost = (ex & NativeWindow.ExTopmost) != 0;
                bool layered = (ex & NativeWindow.ExLayered) != 0;
                bool noFloats = !settings.Items.Any(i => (i.OnTop || i.Hidden) && front.Shows(i.Id));
                bool ok = front.IsVisible && layered && topmost == (mode == "ontop") && front.ShowInTaskbar == (mode == "window" && display.IsPrimary) && noFloats;
                lines.Add($"     {mode}: {display.Name} overlay rect={NativeWindow.Rect(front.Hwnd)} layered={layered} topmost={topmost} taskbar={front.ShowInTaskbar} noFloatsInIt={noFloats}");
                overlays &= ok;
            }
            Check(overlays && desktops && floatsOk, $"mode {mode}: one transparent overlay per display ({(mode == "ontop" ? "topmost" : "not topmost, only the main display's in the taskbar")}, no floating widget in it), desktop decks untouched, floats still at their saved spots (overlays={overlays} desktops={desktops} floats={floatsOk})");
            ChromeCheck($"mode {mode}");
        }

        // Editing: more controls, still on screen, in a window and on the desktop decks.
        h.SetMode("window");
        Settle(4);
        h.SetEditing(true);
        ChromeCheck("editing in window mode");
        Check(h.Floats.Windows.Count == floatTotal, "floats stay shown while editing");
        h.SetMode("ontop");
        Settle(3);
        h.SetEditing(true);
        ChromeCheck("editing, always on top");
        h.SetMode("background");
        Settle(3);
        h.SetEditing(true);
        ChromeCheck("editing on the desktop decks");
        h.SetEditing(false);

        // The tray's "Edit layout" is peek + edit: the widest set of controls on every monitor's front layer.
        h.Peek(true);
        if (h.PrimaryFront is { } editFront) ForceForeground(new WindowInteropHelper(editFront).Handle);
        Settle(4);
        h.SetEditing(true);
        if (h.Fronts.Count() == h.Displays.Count) ChromeCheck("peek + Edit layout (tray)");
        else lines.Add("SKIP peek + Edit layout chrome check: another app took the focus, which ended the peek layer");
        h.SetEditing(false);
        h.Peek(false);

        // A display re-sync must not rebuild the front layer, or take focus.
        h.Peek(true);
        if (h.PrimaryFront is { } peekFront) ForceForeground(new WindowInteropHelper(peekFront).Handle);
        Settle(4);
        if (!h.Peeking) { h.Peek(true); if (h.PrimaryFront is { } again) ForceForeground(new WindowInteropHelper(again).Handle); Settle(2); }
        var before = h.Fronts.Select(f => new WindowInteropHelper(f).Handle).OrderBy(x => x).ToList();
        nint foreground = GetForegroundWindow();
        h.SyncDisplays();
        var after = h.Fronts.Select(f => new WindowInteropHelper(f).Handle).OrderBy(x => x).ToList();
        Check(before.SequenceEqual(after) && before.Count == h.Displays.Count && GetForegroundWindow() == foreground, $"display re-sync keeps the front windows and focus ({before.Count} windows)");
        Check(h.Floats.Windows.Count == floatTotal && h.Fronts.All(f => !f.Shows(clock.Id) && (floater is null || !f.Shows(floater.Id))), "peek: floats keep their own windows and the front layer does not repeat them");

        // Clicks: on a widget they hit the front layer; on empty space they go to whatever is underneath.
        // The peek layer is an ordinary window (not topmost) that ends when another app takes focus, so this run must really be the foreground app, as it is after Ctrl+Alt+D.
        MainWindow? primaryFront = null;
        for (int attempt = 0; attempt < 4 && primaryFront is null; attempt++)
        {
            if (!h.Peeking) h.Peek(true);
            var candidate = h.PrimaryFront;
            if (candidate is null) continue;
            candidate.UpdateLayout();
            ForceForeground(new WindowInteropHelper(candidate).Handle);
            // A new layered window shows nothing until its first frame is presented; let rendering catch up.
            for (int i = 0; i < 12; i++) { Pump(); Thread.Sleep(50); }
            if (h.Peeking && h.PrimaryFront == candidate && candidate.IsLoaded) primaryFront = candidate;
        }
        if (primaryFront is null) lines.Add("SKIP click checks: another app kept taking the focus, which ends the peek layer");
        else
        {
        var frontHandle = new WindowInteropHelper(primaryFront).Handle;
        var floatRects = h.Floats.Windows.Select(w => NativeWindow.Rect(w.Handle)).ToList();
        bool NearFloat(Point p) => floatRects.Any(r => p.X >= r.X - 30 && p.X <= r.Right + 30 && p.Y >= r.Y - 30 && p.Y <= r.Bottom + 30);
        var laid = primaryFront.LaidOut.ToList();
        var widgetRect = laid.Cast<TileRect?>().FirstOrDefault(r => !NearFloat(primaryFront.Surface.PointToScreen(new Point(r!.Value.X + r.Value.Width / 2, r.Value.Y + r.Value.Height / 2))));
        if (widgetRect is { } widget)
        {
            var onWidget = primaryFront.Surface.PointToScreen(new Point(widget.X + widget.Width / 2, widget.Y + widget.Height / 2));
            Point? empty = null;
            for (double y = 120; y < primaryFront.Surface.ActualHeight - 80 && empty is null; y += 37)
                for (double x = 80; x < primaryFront.Surface.ActualWidth - 80 && empty is null; x += 41)
                    if (laid.All(r => x < r.X - 24 || x > r.Right + 24 || y < r.Y - 24 || y > r.Bottom + 24) && !NearFloat(primaryFront.Surface.PointToScreen(new Point(x, y)))) empty = new Point(x, y);
            nint hitWidget = WindowFromPoint(new NativePoint((int)onWidget.X, (int)onWidget.Y));
            var hitClass = new System.Text.StringBuilder(64);
            GetClassName(hitWidget, hitClass, hitClass.Capacity);
            bool coveredByOther = hitWidget != frontHandle && GetWindowThreadProcessId(hitWidget, out uint hitPid) != 0 && hitPid != Environment.ProcessId && (NativeWindow.ExStyle(hitWidget) & NativeWindow.ExTopmost) != 0;
            if (coveredByOther)
            {
                // Another app's always-on-top window covers this point and the peek layer is (by design) not topmost.
                lines.Add($"SKIP click check on a widget: covered by another app's topmost window ({Describe(hitWidget)})");
            }
            else if (hitClass.ToString() == "LockScreenBackstopFrame")
            {
                // The PC is locked: the lock screen covers everything, so click targets can't be checked now.
                lines.Add("SKIP click checks: the screen is locked");
            }
            else
            {
            Check(hitWidget == frontHandle, $"a click on a widget reaches the front layer (hit {Describe(hitWidget)}, front {frontHandle}, point {onWidget}, widget {widget})");
            if (empty is { } e)
            {
                var screen = primaryFront.Surface.PointToScreen(e);
                nint hitEmpty = WindowFromPoint(new NativePoint((int)screen.X, (int)screen.Y));
                Check(hitEmpty != frontHandle, $"a click on empty space passes through the front layer (hit {hitEmpty} at {screen})");
            }
            }
        }
        }
        h.Peek(false);

        for (int i = 1; i <= 3; i++)
        {
            h.Peek(true);
            bool front = h.Peeking && h.Fronts.Count() == h.Displays.Count && h.Fronts.All(f => (NativeWindow.ExStyle(new WindowInteropHelper(f).Handle) & NativeWindow.ExTopmost) == 0);
            h.Peek(false);
            bool back = !h.Peeking && !h.Fronts.Any() && h.Backgrounds.All(b => h.HostFor(b.Display.Id)?.IsAttached == true);
            Check(front && back, $"peek cycle {i}");
        }
        // --shots <folder> also saves what each monitor looks like during the icons checks (a monitor an app covers shows that app).
        var hostArgs = Environment.GetCommandLineArgs();
        int shotsAt = Array.IndexOf(hostArgs, "--shots");
        IconsHostTest.Run(h, lines, Check, Settle, shotsAt >= 0 && shotsAt + 1 < hostArgs.Length ? hostArgs[shotsAt + 1] : null);
        ExerciseControlCenter(h, lines, Check, Settle, clock, lost);
        settings.Items.Remove(clock); settings.Items.Remove(lost); if (away is not null) settings.Items.Remove(away); if (floater is not null) settings.Items.Remove(floater);
        h.RenderAll();
        h.Save();
    }

    /// <summary>The control button, the control center and live layouts, on the real desktop (never moves the real mouse).</summary>
    private void ExerciseControlCenter(DeckHub h, List<string> lines, Action<bool, string> Check, Action<int> Settle, LauncherItem clock, LauncherItem lost)
    {
        var primary = h.PrimaryDisplay;
        var work = primary.WorkArea;

        // The control button: its own topmost, no-activate, tool window (not in Alt+Tab), on screen, saved when it is dropped.
        var orb = h.Orb;
        long orbStyle = NativeWindow.ExStyle(orb?.Handle ?? 0);
        var orbRect = NativeWindow.Rect(orb?.Handle ?? 0);
        Check(orb is not null && (orbStyle & NativeWindow.ExToolWindow) != 0 && (orbStyle & NativeWindow.ExNoActivate) != 0 && (orbStyle & NativeWindow.ExTopmost) != 0 && orb.IsVisible && !orb.ShowInTaskbar,
            $"control button exists: tool + no-activate + topmost, not in the taskbar (style 0x{orbStyle:X}, rect {orbRect})");
        Check(orb is not null && orbRect == orb.Bounds && orbRect.X >= work.X && orbRect.Right <= work.Right && orbRect.Y >= work.Y && orbRect.Bottom <= work.Bottom, $"the control button is inside the main monitor's work area ({orbRect} in {work})");
        string shots = Path.Combine(AppContext.BaseDirectory, "verification");
        if (orb is not null) { Settle(2); CaptureWindow(orb.Handle, Path.Combine(shots, "orb.png")); }
        if (orb is not null)
        {
            orb.MoveTo(work.X + 640, work.Y + 380);
            orb.Commit();
            Settle(2);
            var moved = NativeWindow.Rect(orb.Handle);
            Check(moved == new PixelRect(work.X + 640, work.Y + 380, orbRect.Width, orbRect.Height) && settings.OrbDisplay == primary.Id && Math.Abs(settings.OrbX - 640.0 / work.Width) < 0.0005 && Math.Abs(settings.OrbY - 380.0 / work.Height) < 0.0005,
                $"dropping the control button saves its monitor and place and keeps the window there ({moved}, OrbX={settings.OrbX:0.####} OrbY={settings.OrbY:0.####})");
            Check(h.Store.Load() is { } reloaded && Math.Abs(reloaded.OrbX - settings.OrbX) < 1e-9 && reloaded.OrbDisplay == settings.OrbDisplay, "the control button's place is in the saved settings file");
            orb.MoveTo(work.X + 2, work.Y + 3);
            orb.Commit();
            Check(NativeWindow.Rect(orb.Handle) is { } snapped && snapped.X == work.X && snapped.Y == work.Y, "a control button dropped near a screen corner snaps to it");
            h.ResetOrb();
            Settle(2);
            Check(settings.OrbX == DeckSettings.DefaultOrbX && NativeWindow.Rect(h.Orb!.Handle) == h.Orb.Bounds && h.Orb.Bounds.X > work.X + work.Width / 2, "Reset button position puts it back near the bottom-right corner");
            h.SetShowOrb(false);
            Check(h.Orb is null && !settings.ShowOrb, "turning the control button off removes it");
            h.SetShowOrb(true);
            Settle(2);
            Check(h.Orb is { IsVisible: true } && settings.ShowOrb, "turning it back on shows it again");
        }

        // Opening and closing the control center: a backdrop and a panel, both ordinary topmost windows, dialogs owned by the panel.
        h.OpenControlCenter();
        Settle(8);
        var center = h.Center;
        long panelEx = NativeWindow.ExStyle(center is null ? 0 : new WindowInteropHelper(center).Handle);
        var panelRect = NativeWindow.Rect(center is null ? 0 : new WindowInteropHelper(center).Handle);
        var shown = DisplayMap.Resolve(settings.OrbDisplay, h.Displays);
        Check(center is { IsVisible: true } && (panelEx & NativeWindow.ExTopmost) != 0 && (panelEx & NativeWindow.ExNoActivate) == 0 && h.DialogOwner == center
              && Math.Abs(panelRect.X + panelRect.Width / 2.0 - (shown.WorkArea.X + shown.WorkArea.Width / 2.0)) < 3 && Math.Abs(panelRect.Y + panelRect.Height / 2.0 - (shown.WorkArea.Y + shown.WorkArea.Height / 2.0)) < 3,
            $"the control center opens centered on the control button's monitor, topmost and activatable ({panelRect} in {shown.WorkArea})");
        if (center is not null) CaptureWindow(new WindowInteropHelper(center).Handle, Path.Combine(shots, "panel-live.png"));
        foreach (var tab in ControlCenter.Tabs)
        {
            center?.ShowTab(tab.Id);
            Settle(1);
            Check(center?.CurrentTab == tab.Id, $"control center tab {tab.Id} shows (shows {center?.CurrentTab ?? "no window"}, window loaded={center?.IsLoaded})");
        }
        h.OpenControlCenter("layouts");
        Check(h.Center == center && center?.CurrentTab == "layouts", "opening it again brings the same window forward on the asked tab");
        center?.Close();
        Settle(4);
        Check(h.Center is null && h.DialogOwner is null, "closing the control center releases the dialog owner");

        // Live layouts. Floating: arrange, save as a new layout, change, switch back and forth.
        var floatHome = LayoutEngine.Active(settings, LayoutLayers.Floating)!;
        var clockWindow = h.Floats.Find(clock.Id);
        double homeX = clock.FloatX;
        var focus = h.NewLayout(LayoutLayers.Floating, "Host test focus");
        Check(settings.ActiveFloatingLayout == focus.Id && focus.Entries.TryGetValue(clock.Id, out var focusEntry) && focusEntry.Visible && Math.Abs(focusEntry.X - homeX) < 1e-9, "New layout from current (floating) captures the floats as they are and becomes active");
        int floatsBefore = h.Floats.Windows.Count;
        h.SetItemHidden(clock, true);
        Check(h.Floats.Find(clock.Id) is null && h.Floats.Windows.Count == floatsBefore - 1, "hiding a float takes its window away");
        h.Save();
        Check(!focus.Entries[clock.Id].Visible && floatHome.Entries[clock.Id].Visible, "the active floating layout follows the change, the other one does not");
        h.OpenControlCenter("layouts");
        Settle(6);
        var openCenter = h.Center;
        h.SwitchLayout(LayoutLayers.Floating, floatHome.Id);
        Settle(3);
        Check(!clock.Hidden && h.Floats.Find(clock.Id) is not null && h.Floats.Windows.Count == floatsBefore, "switching the floating layout shows the float again, at once");
        if (h.Floats.Find(clock.Id) is { } under && openCenter is not null)
        {
            var at = NativeWindow.Rect(under.Handle);
            nint hit = WindowFromPoint(new NativePoint(at.X + at.Width / 2, at.Y + at.Height / 2));
            nint panelHandle = new WindowInteropHelper(openCenter).Handle;
            Check(hit != under.Handle && (hit == panelHandle || WindowTitle(hit).StartsWith("Desktop Deck control center", StringComparison.Ordinal)), $"floats re-placed while the control center is open stay below it (point hit {Describe(hit)})");
        }
        openCenter?.Close();
        Settle(3);
        if (h.Floats.Find(clock.Id) is { } again)
        {
            again.MoveTo(work.X + 900, work.Y + 700);
            again.Finish();
            Settle(2);
        }
        h.SwitchLayout(LayoutLayers.Floating, focus.Id);
        Settle(3);
        Check(clock.Hidden && h.Floats.Find(clock.Id) is null, "and switching back to the other layout hides it again (absent or hidden = not shown)");
        h.SwitchLayout(LayoutLayers.Floating, floatHome.Id);
        Settle(3);
        Check(h.Floats.Find(clock.Id) is { } restored && NativeWindow.Rect(restored.Handle) == restored.Bounds && Math.Abs(clock.FloatX - 900.0 / work.Width) < 0.0005, "a layout remembers each float's place (moved while it was active)");
        h.DeleteLayout(focus.Id);
        Check(LayoutEngine.Find(settings, focus.Id) is null && settings.ActiveFloatingLayout == floatHome.Id, "deleting a layout removes it");

        // Background: hide a deck widget and change the background in a new layout, then swap.
        var deckHome = LayoutEngine.Active(settings, LayoutLayers.Background)!;
        var quiet = h.NewLayout(LayoutLayers.Background, "Host test quiet");
        var swatch = new BackgroundStyle { Kind = "solid", Color = "#123456" };
        settings.Appearance.Backgrounds[""] = swatch;
        h.SetItemHidden(lost, true);
        h.Save();
        Settle(3);
        Check(!h.Primary!.Shows(lost.Id) && h.Primary.Surface.Background is SolidColorBrush { Color: { R: 0x12, G: 0x34, B: 0x56 } }, "a background layout: the deck widget is hidden and the new background shows");
        h.SwitchLayout(LayoutLayers.Background, deckHome.Id);
        Settle(3);
        Check(h.Primary.Shows(lost.Id) && !(h.Primary.Surface.Background is SolidColorBrush { Color: { R: 0x12, G: 0x34, B: 0x56 } }) && !settings.Appearance.Backgrounds.Values.Any(b => b.Color == "#123456" && b.Kind == "solid"), "switching the background layout brings back the widget and the other background");
        h.SwitchLayout(LayoutLayers.Background, quiet.Id);
        Settle(3);
        Check(!h.Primary.Shows(lost.Id) && h.Primary.Surface.Background is SolidColorBrush { Color: { R: 0x12, G: 0x34, B: 0x56 } }, "and back again (round trip)");
        h.SwitchLayout(LayoutLayers.Background, deckHome.Id);
        h.DeleteLayout(quiet.Id);
        Settle(3);
        Check(LayoutEngine.Find(settings, quiet.Id) is null && h.Primary.Shows(lost.Id) && h.Floats.Windows.Count == floatsBefore, "layout tests leave the deck as it was");
        lines.Add($"     layouts now: {string.Join(", ", settings.Layouts.Select(l => l.Layer + ":" + l.Name))}");
    }

    // ---------- Running ----------

    private void SetHotkey(bool enabled)
    {
        if (listener is null || preview || smoke) return;
        if (hotkeyRegistered) { UnregisterHotKey(listener.Handle, HotkeyId); hotkeyRegistered = false; }
        if (controlHotkeyRegistered) { UnregisterHotKey(listener.Handle, ControlHotkeyId); controlHotkeyRegistered = false; }
        if (!enabled) return;
        // Ctrl+Alt+D (widgets in front) and Ctrl+Alt+S (control center), without auto-repeat.
        hotkeyRegistered = RegisterHotKey(listener.Handle, HotkeyId, 0x0002 | 0x0001 | 0x4000, 0x44);
        if (!hotkeyRegistered) Log(new InvalidOperationException("Ctrl+Alt+D is already used by another app; the deck can still be opened from the tray or by launching it again."));
        controlHotkeyRegistered = RegisterHotKey(listener.Handle, ControlHotkeyId, 0x0002 | 0x0001 | 0x4000, 0x53);
        if (!controlHotkeyRegistered) Log(new InvalidOperationException("Ctrl+Alt+S is already used by another app; the control center can still be opened from the control button or the tray."));
    }

    /// <summary>Ctrl+Alt+1…9 for the floating layouts in list order (only when switched on, only for as many layouts as there are, and only keys that are free).</summary>
    private void SyncLayoutHotkeys()
    {
        if (listener is null || preview || smoke || hub is null) return;
        foreach (int number in hub.RegisteredLayoutHotkeys.ToList()) UnregisterHotKey(listener.Handle, LayoutHotkeyBase + number);
        hub.RegisteredLayoutHotkeys.Clear();
        if (!settings.LayoutHotkeys) return;
        int count = Math.Min(9, LayoutEngine.Of(settings, LayoutLayers.Floating).Count());
        for (int number = 1; number <= count; number++)
            if (RegisterHotKey(listener.Handle, LayoutHotkeyBase + number, 0x0002 | 0x0001 | 0x4000, (uint)(0x30 + number))) hub.RegisteredLayoutHotkeys.Add(number);
    }

    private static bool SignalRunningInstance(string message)
    {
        nint target = FindWindow(null, ListenerTitle);
        if (target == 0) return false;
        // We were just launched by the user, so we may hand the foreground to the running deck.
        GetWindowThreadProcessId(target, out uint pid);
        AllowSetForegroundWindow(pid);
        return PostMessage(target, RegisterWindowMessage(message), 0, 0);
    }

    private nint OnMessage(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == taskbarCreated) Dispatcher.BeginInvoke(new Action(() => hub?.Reconnect()));
        if ((uint)msg == showAppMessage) { Dispatcher.BeginInvoke(new Action(() => hub?.Peek(true))); handled = true; }
        if ((uint)msg == exitMessage) { Dispatcher.BeginInvoke(new Action(ExitDeck)); handled = true; }
        if (msg == 0x0312 && wParam == HotkeyId) { Dispatcher.BeginInvoke(new Action(() => hub?.TogglePeek())); handled = true; }
        if (msg == 0x0312 && wParam == ControlHotkeyId) { Dispatcher.BeginInvoke(new Action(() => hub?.ToggleControlCenter())); handled = true; }
        if (msg == 0x0312 && wParam >= LayoutHotkeyBase + 1 && wParam <= LayoutHotkeyBase + 9)
        {
            int number = (int)wParam - LayoutHotkeyBase;
            Dispatcher.BeginInvoke(new Action(() => { if (hub is not null && LayoutEngine.Of(settings, LayoutLayers.Floating).ElementAtOrDefault(number - 1) is { } layout) hub.SwitchLayout(LayoutLayers.Floating, layout.Id); }));
            handled = true;
        }
        // WM_SETTINGCHANGE is broadcast for many reasons; only a work-area change (SPI_SETWORKAREA) moves the decks.
        if (msg == 0x001A && wParam == 0x2F) Dispatcher.BeginInvoke(new Action(() => hub?.SyncDisplays()));
        // SPI_SETDESKWALLPAPER (also sent by slideshows and Spotlight): the decks draw the Windows wallpaper again. Nothing here ever changes it.
        if (msg == 0x001A && wParam == 0x14) Dispatcher.BeginInvoke(new Action(() => hub?.WallpaperChanged()));
        if (msg == 0x0011) return new nint(1); // Allow session shutdown.
        if (msg == 0x0016 && wParam != 0) Dispatcher.BeginInvoke(new Action(ExitDeck));
        return 0;
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() => hub?.SyncDisplays()));

    private void OnPowerChanged(object sender, PowerModeChangedEventArgs e)
    { if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(new Action(() => { hub?.Reconnect(); hub?.RefreshDay(); })); }

    private void ExitDeck() { if (exiting) return; exiting = true; Shutdown(ExitCode); }

    protected override void OnExit(ExitEventArgs e)
    {
        exiting = true;
        try { hub?.Shutdown(); } catch (Exception ex) { Log(ex); }
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        if (hotkeyRegistered && listener is not null) UnregisterHotKey(listener.Handle, HotkeyId);
        if (controlHotkeyRegistered && listener is not null) UnregisterHotKey(listener.Handle, ControlHotkeyId);
        if (listener is not null && hub is not null) foreach (int number in hub.RegisteredLayoutHotkeys) UnregisterHotKey(listener.Handle, LayoutHotkeyBase + number);
        tray?.Dispose(); listener?.Dispose(); launcher?.Dispose(); mutex?.Dispose();
        base.OnExit(e);
    }

    internal static void Log(Exception ex)
    {
        try { var dir = LogFolder(); Directory.CreateDirectory(dir); File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTimeOffset.Now:u} {ex}\n"); } catch { }
    }

    /// <summary>Where errors.log goes: the owner's folder for a normal run, the isolated settings folder for --smoke-test and --preview, and a temp folder when no app is running (test programs).</summary>
    private static string LogFolder()
    {
        if (Current is not App app) return Path.Combine(Path.GetTempPath(), "DesktopDeck-Tests");
        if (app.smoke || app.preview) return Path.GetDirectoryName(app.store.FilePath)!;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopDeck");
    }

    /// <summary>Runs queued dispatcher work (layout, rendering) before continuing a synchronous test.</summary>
    private void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Makes a window the foreground window even when this process isn't (briefly shares the input queue of the current foreground thread).</summary>
    private static void ForceForeground(nint hwnd)
    {
        nint current = GetForegroundWindow();
        uint currentThread = current == 0 ? 0 : GetWindowThreadProcessId(current, out _);
        uint mine = GetCurrentThreadId();
        bool attached = currentThread != 0 && currentThread != mine && AttachThreadInput(mine, currentThread, true);
        SetForegroundWindow(hwnd);
        if (attached) AttachThreadInput(mine, currentThread, false);
    }

    private static string WindowTitle(nint hwnd) { var t = new System.Text.StringBuilder(256); GetWindowText(hwnd, t, t.Capacity); return t.ToString(); }

    private static string Describe(nint hwnd)
    {
        var title = new System.Text.StringBuilder(256);
        GetWindowText(hwnd, title, title.Capacity);
        GetWindowThreadProcessId(hwnd, out uint pid);
        string process = "";
        try { process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { }
        return $"{hwnd} '{title}' ({process} {pid}{(pid == Environment.ProcessId ? ", this process" : "")})";
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int count);
    [StructLayout(LayoutKind.Sequential)] private readonly record struct NativePoint(int X, int Y);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll", EntryPoint = "mouse_event")] private static extern void MouseEventNative(uint flags, uint dx, uint dy, uint data, nint extra);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    private static void MouseEvent(uint flags) => MouseEventNative(flags, 0, 0, 0, 0);
    /// <summary>Moves the real pointer with a real (absolute, whole-desktop) mouse input event, so windows get genuine mouse messages.</summary>
    private static void MouseMoveTo(int x, int y)
    {
        const uint Move = 0x0001, Absolute = 0x8000, VirtualDesk = 0x4000;
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77), width = Math.Max(2, GetSystemMetrics(78)), height = Math.Max(2, GetSystemMetrics(79));
        uint nx = (uint)Math.Clamp((long)Math.Round((x - left) * 65535.0 / (width - 1)), 0, 65535), ny = (uint)Math.Clamp((long)Math.Round((y - top) * 65535.0 / (height - 1)), 0, 65535);
        MouseEventNative(Move | Absolute | VirtualDesk, nx, ny, 0, 0);
    }
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint thread, uint attachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string? title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hwnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
