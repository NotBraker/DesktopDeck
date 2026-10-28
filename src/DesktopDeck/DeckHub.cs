using System.Windows;
using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Models;
using DesktopDeck.Services;
using DesktopDeck.Widgets;

namespace DesktopDeck;

/// <summary>What changed, for windows (the control center) that keep a view of it.</summary>
internal enum StateChange { Items, Layouts, Mode }

/// <summary>
/// Owns every deck surface: one background deck per monitor (part of the desktop, never changed by any mode), the
/// deck window (a framed, scaled copy of one monitor's deck, for Window and Always on top), a transparent widgets-only front
/// surface per monitor while peeking, and the floating layer (kept-on-top widgets in their own windows, shown in every mode).
/// Also owns the control button (the orb) and the control center, and applies layouts.
/// Shared state lives here: settings, edit mode, the display mode, the control center and the midnight timer.
/// </summary>
internal sealed partial class DeckHub
{
    private readonly Dictionary<string, MainWindow> backgrounds = new();
    private readonly Dictionary<string, MainWindow> fronts = new();
    private readonly Dictionary<string, NativeDesktopHost> hosts = new();
    private readonly DispatcherTimer dayTimer = new();
    private readonly bool preview, attach, framed, offscreen;
    private ControlCenter? center;
    private OrbWindow? orb;
    private string lastTab = "layouts";
    private bool closing;

    public DeckSettings Settings { get; }
    public SettingsStore Store { get; }
    public LauncherService Launcher { get; }
    public SecretStore Secrets { get; }
    /// <summary>Google sign-in (Settings → Connected accounts) and the shared calendar cache every Schedule and Agenda widget reads.</summary>
    public GoogleAuth Google { get; }
    public CalendarFeed Calendar { get; }
    /// <summary>Google Tasks sync for Today's Tasks, plus the read-only Gmail and Classroom services their widgets use.</summary>
    public GoogleTasksService TasksApi { get; }
    public GoogleTasksFeed Tasks { get; }
    public GmailService Gmail { get; }
    public ClassroomService Classroom { get; }
    public IReadOnlyList<DisplayInfo> Displays { get; private set; } = [];
    /// <summary>"background" (just the desktop decks), "window" (a widgets overlay on every monitor you can Alt+Tab to) or "ontop" (that overlay kept above everything).</summary>
    public string Mode { get; private set; } = "background";
    /// <summary>True while the widgets-only front layer is shown temporarily (Ctrl+Alt+D) in background mode.</summary>
    public bool Peeking { get; private set; }
    public bool IsEditing { get; private set; }
    /// <summary>The kept-on-top widgets: one window each, independent of every deck and of the display mode.</summary>
    public FloatLayer Floats { get; }
    /// <summary>False while "Hide floating widgets" is on.</summary>
    public bool FloatsVisible => !Settings.FloatsHidden;
    /// <summary>What is really on screen: the floats are also hidden for a while when a fullscreen app is in front (see DeckHub.Automation).</summary>
    public bool FloatsShown => FloatsVisible && !FloatsSuppressed;
    /// <summary>False in the simple view (Settings → Show advanced features is off): the menus, tabs and buttons that only advanced use needs are left out.</summary>
    public bool Advanced => Settings.AdvancedFeatures;

    public event Action? ExitRequested;
    public event Action<string, string>? Notify;
    public event Action<bool>? AppHotkeyChanged;
    /// <summary>The widgets, the layouts or the display mode changed (anything the control center shows). Raised on the UI thread.</summary>
    public event Action<StateChange>? StateChanged;
    /// <summary>Layout shortcuts (Ctrl+Alt+1…9) were switched on or off, or the list of floating layouts changed.</summary>
    public event Action? LayoutHotkeysChanged;
    /// <summary>The control button, or null while it is turned off (and in preview renders).</summary>
    public OrbWindow? Orb => orb;
    /// <summary>The control center, or null.</summary>
    public ControlCenter? Center => center;
    /// <summary>While the control center is open, dialogs of every deck are owned by it, so they open in front of it.</summary>
    public Window? DialogOwner { get; private set; }
    /// <summary>Adding from the control center does not switch the decks to Edit mode.</summary>
    public bool QuietAdd { get; set; }
    /// <summary>The Ctrl+Alt+digit shortcuts the app managed to register (set by the app).</summary>
    public HashSet<int> RegisteredLayoutHotkeys { get; } = new();
    public bool LayoutHotkeyWorks(int number) => RegisteredLayoutHotkeys.Contains(number);

    /// <param name="preview">One ordinary window instead of the desktop (preview and smoke renders).</param>
    /// <param name="framed">Give the preview window a normal frame.</param>
    public DeckHub(DeckSettings settings, SettingsStore store, LauncherService launcher, bool preview, bool framed, bool offscreen = false)
    {
        this.offscreen = offscreen;
        Settings = settings;
        Store = store;
        Launcher = launcher;
        Secrets = new SecretStore(Path.GetDirectoryName(store.FilePath)!);
        Google = new GoogleAuth(Secrets);
        Calendar = new CalendarFeed(Google, new GoogleCalendarService(Google), Secrets, settings) { Log = App.Log };
        Calendar.Updated += OnCalendarUpdated;
        TasksApi = new GoogleTasksService(Google);
        Tasks = new GoogleTasksFeed(TasksApi, () => Google.IsConnected && !Google.SampleMode && Google.HasScopes(GoogleScopes.Tasks));
        Gmail = new GmailService(Google);
        Classroom = new ClassroomService(Google);
        Floats = new FloatLayer(this);
        this.preview = preview;
        this.framed = framed;
        attach = !preview;
        dayTimer.Tick += (_, _) => RefreshDay();
    }

    public IEnumerable<MainWindow> Backgrounds => backgrounds.Values;
    public IEnumerable<MainWindow> Fronts => fronts.Values;
    public IEnumerable<MainWindow> Surfaces => backgrounds.Values.Concat(fronts.Values).ToList();
    public DisplayInfo PrimaryDisplay => Displays.FirstOrDefault(d => d.IsPrimary) ?? Displays[0];
    public MainWindow? Primary => Displays.Count > 0 && backgrounds.TryGetValue(PrimaryDisplay.Id, out var s) ? s : backgrounds.Values.FirstOrDefault();
    public MainWindow? PrimaryFront => Displays.Count > 0 && fronts.TryGetValue(PrimaryDisplay.Id, out var s) ? s : null;
    public NativeDesktopHost? HostFor(string displayId) => hosts.GetValueOrDefault(displayId);
    public DisplayInfo DisplayOf(LauncherItem item) => DisplayMap.Resolve(item.Display, Displays);
    /// <summary>The monitor an item is on right now: where it floats if it is kept on top, otherwise its deck's monitor.</summary>
    public DisplayInfo CurrentDisplayOf(LauncherItem item) => DisplayMap.Resolve(item.OnTop ? item.FloatDisplay : item.Display, Displays);

    public void Start()
    {
        RollOverLists();
        SyncDisplays();
        ScheduleDayTimer();
        StartReminders();
        StartHistory();
    }

    // ---------- Displays ----------

    /// <summary>Creates a deck for every connected monitor, removes decks for unplugged ones and moves the rest.</summary>
    public void SyncDisplays()
    {
        Displays = preview
            ? [new DisplayInfo("preview", 1, true, new PixelRect(offscreen ? -32000 : 40, 40, 1440, 900), new PixelRect(offscreen ? -32000 : 40, 40, 1440, 900), 1)]
            : DesktopDeck.Displays.Current();
        foreach (var id in backgrounds.Keys.Where(id => Displays.All(d => d.Id != id)).ToList())
        {
            var gone = backgrounds[id];
            backgrounds.Remove(id);
            if (hosts.Remove(id, out var host)) host.Dispose();
            gone.CloseSurface();
        }
        foreach (var display in Displays)
        {
            if (backgrounds.TryGetValue(display.Id, out var surface) && surface.IsLoaded)
            {
                surface.Display = display;
                if (hosts.TryGetValue(display.Id, out var host)) { host.Area = display.WorkArea; host.Reconnect(); }
            }
            else CreateBackground(display);
        }
        if (fronts.Count > 0)
        {
            // Update the front layer in place: rebuilding it would flicker and take focus from the app you're using.
            foreach (var id in fronts.Keys.Where(id => Displays.All(d => d.Id != id) || fronts[id].IsClosed).ToList())
            {
                fronts[id].CloseSurface();
                fronts.Remove(id);
            }
            foreach (var front in fronts.Values) { front.Display = Displays.First(d => d.Id == front.Display.Id); front.PlaceOnDisplay(); }
            if (Displays.Any(d => !fronts.ContainsKey(d.Id))) ShowFronts(activate: false);
        }
        RenderAll();
        SyncOrb();
        OnDisplaysSynced();
    }

    private void CreateBackground(DisplayInfo display)
    {
        if (backgrounds.Remove(display.Id, out var old)) old.CloseSurface();
        var surface = new MainWindow(this, display, SurfaceKind.Background);
        backgrounds[display.Id] = surface;
        if (preview)
        {
            surface.Left = display.WorkArea.X; surface.Top = display.WorkArea.Y;
            surface.Width = display.WorkArea.Width; surface.Height = display.WorkArea.Height;
            surface.ShowInTaskbar = true;
            if (framed) surface.WindowStyle = WindowStyle.SingleBorderWindow;
            surface.Closed += (_, _) => { if (!closing && backgrounds.GetValueOrDefault(display.Id) == surface) ExitRequested?.Invoke(); };
        }
        else
        {
            surface.Left = display.WorkArea.X / display.Scale; surface.Top = display.WorkArea.Y / display.Scale;
            surface.Width = display.WorkArea.Width / display.Scale; surface.Height = display.WorkArea.Height / display.Scale;
            if (attach)
            {
                var desktopHost = new NativeDesktopHost(surface, display.WorkArea);
                // Attached (or re-attached after Explorer restarted): the deck's region is set again.
                desktopHost.Attached += () => surface.ScheduleRegion();
                hosts[display.Id] = desktopHost;
            }
        }
        surface.Show();
        if (hosts.TryGetValue(display.Id, out var host)) host.Reposition();
    }

    /// <summary>Tray "Reconnect": re-attaches every deck and recreates any that Explorer destroyed.</summary>
    public void Reconnect()
    {
        bool lost = backgrounds.Values.Any(b => !b.IsLoaded) || hosts.Values.Any(h => !h.HasWindow);
        if (lost) { foreach (var b in backgrounds.Values) b.CloseSurface(); backgrounds.Clear(); foreach (var h in hosts.Values) h.Dispose(); hosts.Clear(); }
        SyncDisplays();
        foreach (var background in backgrounds.Values) background.ScheduleRegion();
    }

    public bool AllAttached => !attach || (hosts.Count > 0 && hosts.Values.All(h => h.IsAttached));

    // ---------- Display mode, peek and the front layer ----------

    /// <summary>
    /// Background: only the desktop decks. Window / Always on top: also the front layer, one transparent overlay per monitor showing only the
    /// background-layer widgets (Window: normal windows, the main one in the taskbar and Alt+Tab; Always on top: topmost).
    /// Nothing here ever changes the desktop decks or the floating widgets (those keep their own windows above everything).
    /// </summary>
    public void SetMode(string mode)
    {
        Mode = mode is "window" or "ontop" ? mode : "background";
        Peeking = false;
        CloseFronts();
        if (Mode == "background") SetEditing(false);
        else ShowFronts();
        Present();
        StateChanged?.Invoke(StateChange.Mode);
    }

    /// <summary>Ctrl+Alt+D: show the widgets in front of your windows until you switch away (background mode only). In the overlay modes it just comes forward.</summary>
    public void Peek(bool on)
    {
        if (Mode != "background") { if (on) ActivateFront(); return; }
        if (on == Peeking) { if (on) ActivateFront(); return; }
        Peeking = on;
        if (on) ShowFronts();
        else { CloseFronts(); SetEditing(false); }
        Present();
    }

    public void TogglePeek() => Peek(Mode == "background" ? !Peeking : true);

    /// <summary>
    /// The widgets-only layer: one transparent surface per monitor. Window mode: normal windows, only the main monitor's in the taskbar (the
    /// others are owned by it, so they come forward together). Always on top: the same, topmost. The floating widgets and the control button are raised above afterwards.
    /// </summary>
    private void ShowFronts(bool activate = true)
    {
        bool top = Mode == "ontop", app = Mode == "window";
        MainWindow? primaryFront = null;
        foreach (var display in Displays.OrderByDescending(d => d.IsPrimary))
        {
            if (!fronts.TryGetValue(display.Id, out var front))
            {
                front = new MainWindow(this, display, SurfaceKind.Front) { ShowInTaskbar = app && display.IsPrimary, Topmost = top };
                // One taskbar button: the other monitors' fronts follow the main one.
                if (primaryFront is not null) front.Owner = primaryFront;
                // A topmost overlay that is clicked would come above the floating widgets, so put them back on top.
                front.Activated += (_, _) => { if (Mode == "ontop") Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RaiseAbove)); };
                fronts[display.Id] = front;
                front.Show();
            }
            front.Topmost = top;
            front.PlaceOnDisplay();
            primaryFront ??= front;
        }
        if (activate) ActivateFront();
        RaiseAbove();
    }

    /// <summary>Keeps the floating widgets and the control button above the overlay (matters when the overlay is topmost).</summary>
    private void RaiseAbove()
    {
        Floats.Raise();
        orb?.Raise();
    }

    private void ActivateFront()
    {
        var front = PrimaryFront ?? fronts.Values.FirstOrDefault();
        if (front is null) return;
        front.Activate();
        front.Focus();
        RaiseAbove();
    }

    private void CloseFronts()
    {
        // Owned fronts close with their owner; close those first so nothing is left behind.
        foreach (var front in fronts.Values.OrderBy(f => f.Display.IsPrimary).ToList()) front.CloseSurface();
        fronts.Clear();
    }

    /// <summary>Updates each surface's mode label and back button.</summary>
    private void Present()
    {
        foreach (var surface in Surfaces) surface.SetPresentation(Mode, Peeking);
        foreach (var background in backgrounds.Values) background.RenderSurface();
    }

    /// <summary>Peek ends when focus goes to another app (not to one of our own windows or dialogs).</summary>
    public void CheckLeave()
    {
        if (!Peeking) return;
        if (Application.Current.Windows.Cast<Window>().Any(w => w.IsActive)) return;
        if (Surfaces.Any(s => s.InModal)) return;
        Peek(false);
    }

    // ---------- Shared editing and rendering ----------

    public void SetEditing(bool value)
    {
        if (IsEditing == value) return;
        IsEditing = value;
        foreach (var surface in Surfaces) surface.ApplyEditing(value);
        if (!value) Save();
    }

    public void RenderAll()
    {
        foreach (var surface in Surfaces) surface.RenderSurface();
        Floats.Sync(true);
        StateChanged?.Invoke(StateChange.Items);
    }

    /// <summary>One item's data changed on <paramref name="source"/>; redraw its other copies.</summary>
    public void ItemChanged(LauncherItem item, MainWindow? source)
    {
        foreach (var surface in Surfaces) if (surface != source) surface.RebuildItem(item);
    }

    /// <summary>Redraws every widget of one kind on every surface (e.g. after a usage check finishes).</summary>
    public void RebuildKind(string kind)
    {
        var items = Settings.Items.Where(i => i.Kind == kind).ToList();
        foreach (var surface in Surfaces) foreach (var item in items) surface.RebuildItem(item);
    }

    private readonly Dispatcher uiDispatcher = Dispatcher.CurrentDispatcher;
    private int calendarRedrawPending;

    /// <summary>Calendar data changed (raised on a background thread): redraw the Schedule and Agenda widgets once, on the UI thread.</summary>
    private void OnCalendarUpdated()
    {
        if (closing || Interlocked.Exchange(ref calendarRedrawPending, 1) == 1) return;
        uiDispatcher.BeginInvoke(new Action(() => { Volatile.Write(ref calendarRedrawPending, 0); if (!closing) RedrawCalendars(); }));
    }

    public void RedrawCalendars() { RebuildKind(ItemKinds.Schedule); RebuildKind("agenda"); }

    // ---------- Google Tasks sync ----------

    private readonly Dictionary<string, int> taskFollowUps = new(StringComparer.Ordinal);

    /// <summary>
    /// Syncs one Today's Tasks list with Google Tasks in the background (at most once every 90 seconds, or right now with
    /// <paramref name="force"/>), then merges the result into the list on the UI thread. Call on the UI thread. Does nothing when sync is off,
    /// no account with the Tasks permission is connected, or a sync of this list is already running.
    /// </summary>
    public void RequestTasksSync(LauncherItem item, bool force)
    {
        if (closing || item.Kind != ItemKinds.Tasks || !item.GoogleTasksSync) return;
        item = WidgetLinks.TaskSyncOwner(item, Settings.Items); // linked lists share one sync, run by one of them
        if (!Tasks.TryBegin(item.Id, force)) return;
        var snapshot = TaskSyncSnapshot.From(item, DateTimeOffset.UtcNow);
        string id = item.Id;
        _ = Task.Run(async () =>
        {
            TaskSyncOutcome? outcome = null;
            try { outcome = await Tasks.RunAsync(id, snapshot).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OutOfMemoryException) { App.Log(ex); }
            _ = uiDispatcher.BeginInvoke(new Action(() => FinishTasksSync(id, outcome)));
        });
    }

    private void FinishTasksSync(string id, TaskSyncOutcome? outcome)
    {
        if (closing) return;
        // The widget may have been edited (Customize replaces the item) or removed while the sync ran: look it up again by id.
        var item = Settings.Items.FirstOrDefault(i => i.Id == id && i.Kind == ItemKinds.Tasks);
        bool changed = false;
        if (item is not null && outcome is not null && item.GoogleTasksSync && string.Equals(outcome.ListId, item.GoogleTaskListId, StringComparison.Ordinal))
        {
            changed = GoogleTasksSync.Apply(item, outcome);
            if (changed) { SyncLinked(item); Save(); }
        }
        RebuildKind(ItemKinds.Tasks);
        // Something was changed again while the sync ran: send that now, a few times at most.
        if (item is not null && outcome is not null && outcome.Pushes.All(p => p.Error is null) && GoogleTasksSync.PendingCount(item) > 0 && taskFollowUps.GetValueOrDefault(id) < 3)
        {
            taskFollowUps[id] = taskFollowUps.GetValueOrDefault(id) + 1;
            RequestTasksSync(item, true);
        }
        else taskFollowUps.Remove(id);
    }

    /// <summary>Asks Google for the account's task lists (asking for the Tasks permission first if needed) and remembers them for the picker.</summary>
    public async Task<List<GoogleTaskListChoice>> LoadTaskListsAsync(CancellationToken cancel = default)
    {
        await Google.EnsureScopesAsync([GoogleScopes.Tasks], cancel);
        if (!Google.HasScopes(GoogleScopes.Tasks)) throw new GoogleApiException("Google did not allow Tasks access. Press Enable for Tasks again and keep that box ticked.", GoogleApiProblem.ScopeMissing);
        var lists = await TasksApi.ListTaskListsAsync(cancel);
        Settings.GoogleTaskLists = lists;
        Save();
        return lists;
    }

    /// <summary>The Google account was disconnected: nothing here may point at its tasks, and downloaded mail and school data must go.</summary>
    public void GoogleDisconnected()
    {
        foreach (var item in Settings.Items)
        {
            if (item.Kind == ItemKinds.Tasks) { GoogleTasksSync.Unlink(item); Tasks.Forget(item.Id); }
            else if (item.Kind is "gmail" or "classroom") ConnectedKit.ClearCache(item);
        }
        Settings.GoogleTaskLists = new List<GoogleTaskListChoice>();
        Save();
        RebuildKind(ItemKinds.Tasks);
        RebuildKind("gmail");
        RebuildKind("classroom");
    }

    /// <summary>Accounts, links or calendar switches changed in Settings: forget cached events, save, and redraw.</summary>
    public void CalendarSettingsChanged()
    {
        Calendar.Clear(raise: false);
        Save();
        RedrawCalendars();
    }

    /// <summary>Moves an item to another monitor, near a physical-pixel point (default: the middle).</summary>
    public bool MoveItem(LauncherItem item, DisplayInfo target, Point? screenPoint, bool front)
    {
        if (item.OnTop) return MoveFloat(item, target, screenPoint);
        var surface = front && fronts.TryGetValue(target.Id, out var f) ? f : backgrounds.GetValueOrDefault(target.Id);
        if (surface is null) return false;
        string previous = item.Display;
        item.Display = target.Id;
        if (!surface.PlaceFromScreen(item, screenPoint))
        {
            item.Display = previous;
            surface.Inform("There is no free room on that display. Make something smaller or remove an item, then try again.", "Display is full");
            RenderAll();
            return false;
        }
        Save();
        RenderAll();
        surface.SetStatus($"Moved {(string.IsNullOrWhiteSpace(item.Name) ? "item" : item.Name)} to {target.Name}.");
        return true;
    }

    /// <summary>Moves a floating widget to another monitor: centered on the pointer if there is one, otherwise in the middle of that monitor.</summary>
    private bool MoveFloat(LauncherItem item, DisplayInfo target, Point? screenPoint)
    {
        var (w, h) = FloatLayer.SizeFor(item);
        var work = target.WorkArea;
        double wpx = w * target.Scale, hpx = h * target.Scale;
        double cx = screenPoint?.X ?? work.X + work.Width / 2.0, cy = screenPoint?.Y ?? work.Y + work.Height / 2.0;
        var rect = FloatLayer.KeepOnScreen(new PixelRect((int)Math.Round(cx - wpx / 2), (int)Math.Round(cy - hpx / 2), (int)Math.Round(wpx), (int)Math.Round(hpx)), work, inside: true);
        item.FloatDisplay = target.Id;
        Floats.Commit(item, rect, w, h);
        SetStatus($"Moved {(string.IsNullOrWhiteSpace(item.Name) ? "item" : item.Name)} to {target.Name}.");
        return true;
    }

    // ---------- The floating layer, hiding ----------

    /// <summary>
    /// Keep on top ON: the widget leaves its deck and floats where it was (monitor, position and size are copied from its spot on the deck).
    /// OFF: it goes back to the deck of the monitor it floated on, at a free spot near where it floated.
    /// </summary>
    public void SetOnTop(LauncherItem item, bool value)
    {
        if (item.OnTop == value) return;
        string name = string.IsNullOrWhiteSpace(item.Name) ? "Widget" : item.Name;
        if (value)
        {
            var display = DisplayOf(item);
            var spot = backgrounds.GetValueOrDefault(display.Id)?.DeckPlacement(item);
            var (dw, dh) = FloatLayer.SizeFor(new LauncherItem { Kind = item.Kind, Width = item.Width, Height = item.Height });
            item.FloatDisplay = display.Id;
            item.FloatX = Math.Clamp(spot?.X ?? (double.IsFinite(item.X) ? item.X : 0), 0, 1);
            item.FloatY = Math.Clamp(spot?.Y ?? (double.IsFinite(item.Y) ? item.Y : 0), 0, 1);
            item.FloatWidth = spot?.Width ?? dw;
            item.FloatHeight = spot?.Height ?? dh;
            item.OnTop = true;
            RenderAll();
            Save();
            SetStatus(FloatsVisible ? $"{name} is now pinned above your windows, on its own. Drag its top edge (or hold Alt and drag) to move it." : $"{name} will be pinned above your windows once you show pinned widgets again.");
            return;
        }
        var floated = DisplayMap.Resolve(item.FloatDisplay, Displays);
        item.Display = floated.IsPrimary ? "" : floated.Id;
        // The deck coordinates are fractions of the same work area, so the widget lands where it floated; the deck moves it only if that spot is taken.
        item.X = item.FloatX;
        item.Y = item.FloatY;
        var (fw, fh) = FloatLayer.SizeFor(item);
        item.Width = fw;
        item.Height = fh;
        item.OnTop = false;
        RenderAll();
        backgrounds.GetValueOrDefault(floated.Id)?.RememberItem(item);
        Save();
        SetStatus($"{name} is back on the desktop.");
    }

    /// <summary>Hides one widget everywhere (deck, window and floating); it keeps its place for when it is shown again.</summary>
    public void HideItem(LauncherItem item)
    {
        SetItemHidden(item, true);
        SetStatus($"{(string.IsNullOrWhiteSpace(item.Name) ? "The widget" : item.Name)} is hidden. Show it again from the control center → Widgets.");
    }

    /// <summary>Shows or hides one widget (the control center's Visible switch). The active layout of its layer records it when saved.</summary>
    public void SetItemHidden(LauncherItem item, bool hidden)
    {
        if (item.Hidden == hidden) return;
        item.Hidden = hidden;
        RenderAll();
        Save();
    }

    public int HiddenCount => Settings.Items.Count(i => i.Hidden && i.GroupId.Length == 0);

    /// <summary>"Hide floating widgets": switches the floating layer off or on. Their positions are kept.</summary>
    public void SetFloatsVisible(bool visible)
    {
        if (Settings.FloatsHidden == !visible) return;
        Settings.FloatsHidden = !visible;
        Floats.Sync(true);
        Save();
        SetStatus(visible ? "Pinned widgets are back." : "Pinned widgets are hidden. Show them again from the control button's menu, the tray menu or the control center → Display.");
        StateChanged?.Invoke(StateChange.Items);
    }

    public void SetStatus(string text) { foreach (var surface in Surfaces) surface.SetStatus(text); }

    public bool Save()
    {
        try { Store.Save(Settings); return true; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not save the layout"); return false; }
    }

    public void FlushAll() { foreach (var surface in Surfaces) surface.FlushSave(); }

    // ---------- The control center and the control button ----------

    /// <summary>
    /// Opens the control center on the monitor the control button is on (or brings it forward). It is the one place for layouts, widgets,
    /// appearance, display and settings; the deck's gear, the tray, the control button and Ctrl+Alt+S all open it.
    /// </summary>
    public void OpenControlCenter() => OpenControlCenter(null);

    public void OpenControlCenter(string? tab)
    {
        if (closing || Displays.Count == 0) return;
        if (center is not null)
        {
            if (tab is not null) center.ShowTab(tab);
            center.Conceal(false);
            return;
        }
        var display = DisplayMap.Resolve(Settings.OrbDisplay, Displays);
        center = new ControlCenter(this, display, tab ?? lastTab);
        DialogOwner = center;
        center.Present();
    }

    /// <summary>Puts the control center (and its backdrop) back above the floats and the control button, without taking the keyboard.</summary>
    public void KeepCenterOnTop() => center?.RaiseOnly();

    /// <summary>Ctrl+Alt+S: opens the control center, or closes it when it is already open.</summary>
    public void ToggleControlCenter()
    {
        if (center is not null) center.Close(); else OpenControlCenter();
    }

    /// <summary>Settings now live in the control center's Settings tab.</summary>
    public void OpenSettings() => OpenControlCenter("settings");

    internal void ControlCenterClosed(ControlCenter closed)
    {
        if (center != closed) return;
        lastTab = closed.CurrentTab;
        center = null;
        DialogOwner = null;
        Floats.Raise();
        orb?.Raise();
    }

    /// <summary>Creates, moves or closes the control button so it matches the settings (and the monitors that are connected).</summary>
    public void SyncOrb()
    {
        // Preview and render runs keep it away from the real desktop, like the floating widgets.
        bool want = Settings.ShowOrb && !OrbSuppressed && (!preview || framed) && !offscreen && Displays.Count > 0 && !closing;
        if (!want)
        {
            orb?.CloseOrb();
            orb = null;
            return;
        }
        orb ??= new OrbWindow(this);
        orb.Place();
        KeepCenterOnTop();
    }

    /// <summary>True while Windows' desktop icons stay visible: each desktop deck is cut down to its widgets and draws the Windows wallpaper behind them.</summary>
    public bool ShowDesktopIcons => Settings.ShowDesktopIcons;

    /// <summary>Settings → Display → Show desktop icons: live, on every deck (the gear, the background and the region follow); nothing restarts.</summary>
    public void SetShowDesktopIcons(bool value)
    {
        if (Settings.ShowDesktopIcons == value) return;
        Settings.ShowDesktopIcons = value;
        Save();
        foreach (var surface in Surfaces) surface.ApplyDesktopIcons();
        RenderAll();
        StateChanged?.Invoke(StateChange.Layouts);
    }

    /// <summary>Windows changed the wallpaper (the user, a slideshow or Spotlight): read it again and redraw the decks.</summary>
    public void WallpaperChanged()
    {
        WindowsWallpaper.Invalidate();
        RenderAll();
    }

    /// <summary>Settings → Show advanced features. The menus and the control center rebuild themselves from the "layouts changed" event; nothing on screen changes.</summary>
    public void SetAdvanced(bool value)
    {
        if (Settings.AdvancedFeatures == value) return;
        Settings.AdvancedFeatures = value;
        Save();
        foreach (var surface in Surfaces) surface.ApplyAdvanced();
        RenderAll();
        StateChanged?.Invoke(StateChange.Layouts);
    }

    public void SetShowOrb(bool value)
    {
        if (Settings.ShowOrb == value) return;
        Settings.ShowOrb = value;
        SyncOrb();
        Save();
        StateChanged?.Invoke(StateChange.Items);
        SetStatus(value ? "The control button is back." : "The control button is hidden. Bring it back from the tray menu or with Ctrl+Alt+S → Settings.");
    }

    /// <summary>Puts the control button back near the bottom-right corner of the main monitor.</summary>
    public void ResetOrb()
    {
        Settings.OrbDisplay = "";
        Settings.OrbX = DeckSettings.DefaultOrbX;
        Settings.OrbY = DeckSettings.DefaultOrbY;
        SyncOrb();
        Save();
    }

    // ---------- Layouts ----------

    private void LayoutsChanged(string? status = null)
    {
        Save();
        if (status is not null) SetStatus(status);
        StateChanged?.Invoke(StateChange.Layouts);
        LayoutHotkeysChanged?.Invoke();
    }

    /// <summary>Applies a layout to its layer right now: the old one is brought up to date first, then widgets and (for background layouts) the deck background change.</summary>
    public bool SwitchLayout(string layer, string id)
    {
        if (!LayoutEngine.Switch(Settings, layer, id)) return false;
        RenderAll();
        var layout = LayoutEngine.Find(Settings, id);
        LayoutsChanged($"{(layer == LayoutLayers.Floating ? "Pinned" : "Desktop")} layout: {layout?.Name}");
        return true;
    }

    /// <summary>"New layout from current": saves the layer as it is now and makes it the active layout.</summary>
    public LayoutProfile NewLayout(string layer, string name)
    {
        var made = LayoutEngine.CreateFromCurrent(Settings, layer, name);
        LayoutsChanged($"Saved \"{made.Name}\".");
        return made;
    }

    public void RenameLayout(string id, string name) { if (LayoutEngine.Rename(Settings, id, name)) LayoutsChanged(); }

    public void DuplicateLayout(string id) { if (LayoutEngine.Duplicate(Settings, id) is { } copy) LayoutsChanged($"Copied to \"{copy.Name}\"."); }

    public void MoveLayout(string id, int delta) { if (LayoutEngine.Move(Settings, id, delta)) LayoutsChanged(); }

    public void SetLayoutIcon(string id, string icon) { if (LayoutEngine.SetIcon(Settings, id, icon)) LayoutsChanged(); }

    /// <summary>Deletes a layout (never the last of its layer). Pictures only it used are removed from the media folder.</summary>
    public bool DeleteLayout(string id)
    {
        if (LayoutEngine.Delete(Settings, id) is not { } removed) return false;
        RenderAll();
        foreach (var path in removed.Backgrounds.Values.Select(b => b.ImagePath).Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase))
            Appearance.ReleasePicture(Settings, Store, path);
        LayoutsChanged($"Deleted \"{removed.Name}\".");
        return true;
    }

    public void SetLayoutHotkeys(bool value)
    {
        Settings.LayoutHotkeys = value;
        Save();
        LayoutHotkeysChanged?.Invoke();
        StateChanged?.Invoke(StateChange.Layouts);
    }

    // ---------- Widgets (the control center's Widgets tab) ----------

    /// <summary>Opens a widget's Customize window, owned by <paramref name="owner"/> so it opens in front of the control center.</summary>
    public void CustomizeItem(LauncherItem item, Window owner) => Primary?.CustomizeFrom(owner, item);

    /// <summary>Removes a widget after asking (always, from the control center), from every layout too.</summary>
    public bool RemoveItem(LauncherItem item, Window owner) => Primary?.RemoveFrom(owner, item) ?? false;

    /// <summary>Where a widget is on screen right now (physical pixels), or null when it is hidden or not laid out anywhere.</summary>
    public PixelRect? ScreenRectOf(LauncherItem item)
    {
        if (item.Hidden) return null;
        if (item.OnTop) return FloatsShown && Floats.Find(item.Id) is { } window ? window.Bounds : null;
        foreach (var surface in backgrounds.Values)
            if (surface.ScreenRectOf(item.Id) is { } rect) return rect;
        return null;
    }

    // ---------- Settings shared by every surface ----------

    public void SetSnap(bool value)
    {
        Settings.SnapToGrid = value;
        RenderAll();
        Save();
        SetStatus(value ? "Snapping to the grid. Hold Alt while dragging to place freely." : "Free placement. Items go exactly where you drop them.");
    }

    public void SetKeepCenterClear(bool value)
    {
        Settings.KeepCenterClear = value;
        RenderAll();
        foreach (var background in backgrounds.Values) background.RememberAll();
        Save();
        SetStatus(value ? "The center is kept clear; items there moved to the edges." : "Items may now go anywhere, including the center.");
    }

    public void SetStartAtLogin(bool value)
    {
        try { new StartupService().SetEnabled(value); Settings.StartAtLogin = value; Save(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not change sign-in startup"); }
    }

    public void SetAppHotkey(bool value)
    {
        Settings.AppHotkey = value;
        Save();
        AppHotkeyChanged?.Invoke(value);
    }

    public void RequestExit() => ExitRequested?.Invoke();
    public void RaiseNotify(string title, string text) => Notify?.Invoke(title, text);

    public void ImportDesktop() => Primary?.ImportDesktop();

    // ---------- Days ----------

    private bool RollOverLists()
    {
        bool changed = false;
        foreach (var item in Settings.Items.Where(i => i.Kind == ItemKinds.Tasks).ToList())
            if (Planner.RollOver(item, Planner.Today())) { changed = true; SyncLinked(item); }
        return changed;
    }

    /// <summary>Re-renders date-dependent widgets. Called at local midnight and after resume.</summary>
    public void RefreshDay()
    {
        ScheduleDayTimer();
        PurgeTrash();
        if (RollOverLists()) Save();
        foreach (var surface in Surfaces) surface.RefreshDay();
        ReminderTick();
    }

    private void ScheduleDayTimer()
    {
        // A single timer aimed at the next midnight; nothing polls in between.
        dayTimer.Stop();
        dayTimer.Interval = DateTime.Today.AddDays(1).AddSeconds(2) - DateTime.Now;
        dayTimer.Start();
    }

    public void Shutdown()
    {
        closing = true;
        StopAutomation();
        dayTimer.Stop();
        StopReminders();
        FlushAll();
        center?.Close();
        orb?.CloseOrb();
        CloseFronts();
        Floats.CloseAll();
        foreach (var host in hosts.Values) host.Dispose();
        foreach (var background in backgrounds.Values.ToList()) background.CloseSurface();
    }
}
