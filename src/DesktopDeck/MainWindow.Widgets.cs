using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

// Element content. Each builder returns the visual for one item at one size; the edit overlay
// (drag, resize) sits above it, so content only handles its own everyday interactions.
public sealed partial class MainWindow
{
    private readonly Dictionary<string, int> weekOffsets = new();
    private readonly Dictionary<string, int> dayOffsets = new();
    private readonly Dictionary<string, int> goalOffsets = new();
    private readonly HashSet<string> expandedGoals = new();

    /// <summary>One item's content, with its text color and text size applied (plug-ins apply their own through WidgetContext).</summary>
    private FrameworkElement BuildContent(LauncherItem item, double w, double h)
    {
        item = hub.Previewed(item); // Customize shows its unsaved draft here
        using var frost = FrostScope(item);
        var content = BuildContentCore(item, w, h);
        if (!ItemKinds.All.Contains(item.Kind)) return content;
        try { Appearance.StyleText(content, settings.Appearance, item); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { App.Log(ex); }
        return content;
    }

    private FrameworkElement BuildContentCore(LauncherItem item, double w, double h) => item.Kind switch
    {
        ItemKinds.Image => BuildPicture(item, w, h),
        ItemKinds.Tasks => BuildTasks(item, w, h),
        ItemKinds.Habits => BuildHabits(item, w, h),
        ItemKinds.Counter => BuildCounter(item, w, h),
        ItemKinds.Clock => BuildClock(item, w, h),
        ItemKinds.Timer => BuildTimer(item, w, h),
        ItemKinds.Schedule => BuildSchedule(item, w, h),
        ItemKinds.Goals => BuildGoals(item, w, h),
        ItemKinds.Note => BuildNote(item, w, h),
        ItemKinds.Countdown => BuildCountdown(item, w, h),
        ItemKinds.Media => BuildMedia(item, w, h),
        ItemKinds.Folder => BuildFolder(item, w, h),
        ItemKinds.Weather => BuildWeather(item, w, h),
        ItemKinds.Group => BuildGroup(item, w, h),
        _ when Widgets.WidgetRegistry.Find(item.Kind) is { } plugin => BuildPlugin(plugin, item, w, h),
        _ when IsUnavailable(item) => BuildUnavailable(item, w, h),
        _ => BuildLauncher(item, w, h),
    };

    /// <summary>The widget card in the item's chosen style (solid, glass, outline or none), with its own and the deck-wide appearance settings.</summary>
    private Border Card(LauncherItem? item = null) => Appearance.Card(settings.Appearance, item);

    /// <summary>The standard widget card: small uppercase title (unless hidden), optional badge, actions on the right, body below.</summary>
    private (Border Card, DockPanel Body) Frame(LauncherItem item, FrameworkElement? badge, params FrameworkElement[] actions)
    {
        var card = Card(item);
        double pad = Appearance.Padding(settings.Appearance, item);
        var dock = new DockPanel { Margin = new Thickness(pad + 2, pad - 1, pad, pad), LastChildFill = true };
        card.Child = dock;
        if (!item.ShowHeader && badge is null && actions.Length == 0) return (card, dock);
        var head = new Grid { Margin = new Thickness(0, 0, 0, 10), MinHeight = 24 };
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (item.ShowHeader) head.Children.Add(Theme.WidgetTitle(item.Name, Appearance.TitleBrush(settings.Appearance, item)));
        if (badge is not null) { badge.Margin = new Thickness(8, 0, 6, 0); Grid.SetColumn(badge, 1); head.Children.Add(badge); }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var action in actions) { action.Margin = new Thickness(4, 0, 0, 0); buttons.Children.Add(action); }
        Grid.SetColumn(buttons, 2);
        head.Children.Add(buttons);
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        return (card, dock);
    }

    /// <summary>A small rounded count such as "2/5".</summary>
    private static Border Pill(string text, string? accent = null)
    {
        var label = Theme.Label(text, 11, accent is null ? Theme.Text2 : Theme.Accent(accent));
        label.FontWeight = FontWeights.SemiBold;
        return new Border { CornerRadius = new CornerRadius(9), Background = accent is null ? Theme.Surface3 : Theme.Tint(accent, 0.16), Padding = new Thickness(8, 1, 8, 2), Child = label, VerticalAlignment = VerticalAlignment.Center };
    }

    private static Button AddButton(string tip, Action add)
    {
        var button = Theme.IconButton(Theme.Glyphs.Add, tip, 24, accent: true);
        button.Click += (_, _) => add();
        return button;
    }

    private static Button NavButton(string glyph, string tip, Action go)
    {
        var button = Theme.IconButton(glyph, tip, 24);
        button.Click += (_, _) => go();
        return button;
    }

    /// <summary>A rounded checkbox drawn to match the theme.</summary>
    private static Border CheckBox(bool done, Brush accent, double size = 17)
    {
        var box = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1.5),
            BorderBrush = done ? accent : Theme.Muted, Background = done ? accent : Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand,
        };
        if (done) box.Child = Theme.Icon(Theme.Glyphs.Check, size * 0.58, Theme.Background);
        return box;
    }

    private static Border Row(UIElement child)
    {
        var row = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 5, 6, 5), Background = Brushes.Transparent, Child = child };
        row.MouseEnter += (_, _) => row.Background = Theme.SurfaceHover;
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        return row;
    }

    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Margin = new Thickness(-6, 0, -4, 0) };

    private static TextBlock EmptyNote(string text)
    {
        var note = Theme.Label(text, 12, Theme.Muted);
        note.TextWrapping = TextWrapping.Wrap;
        note.Margin = new Thickness(6, 6, 6, 0);
        return note;
    }

    private double Scale => PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? VisualTreeHelper.GetDpi(this).DpiScaleX;

    private int Pixels(double size, int bucket, int max) => Math.Clamp((int)Math.Ceiling(size * Scale / bucket) * bucket, bucket, max);

    // ---------- Launcher ----------

    private FrameworkElement BuildLauncher(LauncherItem item, double w, double h)
    {
        var look = Appearance.Look(settings.Appearance, item);
        var card = Appearance.Card(look);
        var restBackground = card.Background;
        var restBorder = card.BorderBrush;
        var accent = Theme.Accent(item.Accent);
        bool compact = w < 120 || h < 56;
        bool tall = !compact && h >= 116 && w / h < 1.8;
        double iconSize = compact ? Math.Clamp(Math.Min(w, h) * 0.56, 20, 128)
            : tall ? Math.Clamp(Math.Min(w * 0.5, h - 66), 32, 176)
            : Math.Clamp(h - 42, 28, 56);
        var icon = LauncherIcon(item, iconSize);
        var name = Theme.Label(item.Name, tall ? Math.Clamp(w / 12, 13, 17) : 13.5);
        name.FontWeight = FontWeights.SemiBold;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var state = Theme.Label("", 11, Theme.Muted);
        state.TextTrimming = TextTrimming.CharacterEllipsis;
        state.Margin = new Thickness(0, 3, 0, 0);
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = accent, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0), Visibility = Visibility.Collapsed };

        var layout = new Grid();
        if (compact) layout.Children.Add(icon);
        else if (tall)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 10, 12, 10) };
            icon.Margin = new Thickness(0, 0, 0, 10);
            name.HorizontalAlignment = state.HorizontalAlignment = HorizontalAlignment.Center;
            name.TextAlignment = TextAlignment.Center;
            stack.Children.Add(icon);
            stack.Children.Add(name);
            stack.Children.Add(state);
            layout.Children.Add(stack);
        }
        else
        {
            layout.Margin = new Thickness(14, 0, 14, 0);
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(iconSize + 12) });
            layout.ColumnDefinitions.Add(new ColumnDefinition());
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            layout.Children.Add(icon);
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(name);
            labels.Children.Add(state);
            Grid.SetColumn(labels, 1);
            layout.Children.Add(labels);
        }
        layout.Children.Add(dot);
        card.Child = layout;
        AutomationProperties.SetName(card, item.Name);

        void Update()
        {
            string s = launcher.GetStatus(item.Id);
            bool ready = string.IsNullOrWhiteSpace(s) || s.Equals("Ready", StringComparison.OrdinalIgnoreCase);
            state.Text = ready ? "" : s;
            state.Visibility = ready || compact ? Visibility.Collapsed : Visibility.Visible;
            dot.Visibility = s.StartsWith("Running", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            card.ToolTip = item.Name + "\n" + item.Target + (ready ? "" : "\n" + s);
        }
        statusUpdaters[KeyFor(item)] = Update;
        Update();

        if (IsEditing) return card;
        card.Cursor = Cursors.Hand;
        card.Focusable = true;
        bool pressed = false;
        card.MouseEnter += (_, _) => { card.Background = look.Hover; card.BorderBrush = accent; };
        card.MouseLeave += (_, _) => { card.Background = restBackground; card.BorderBrush = restBorder; };
        card.GotKeyboardFocus += (_, _) => card.BorderBrush = Theme.Primary;
        card.LostKeyboardFocus += (_, _) => card.BorderBrush = restBorder;
        card.MouseLeftButtonDown += (_, e) => { pressed = true; card.Focus(); card.CaptureMouse(); e.Handled = true; };
        card.MouseLeftButtonUp += (_, e) =>
        {
            bool launch = pressed && card.IsMouseOver;
            pressed = false;
            card.ReleaseMouseCapture();
            if (launch) Launch(item);
            e.Handled = true;
        };
        card.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Launch(item); e.Handled = true; } };
        return card;
    }

    /// <summary>The custom icon, else the shell icon (loaded after first paint), else a glyph.</summary>
    private FrameworkElement LauncherIcon(LauncherItem item, double size)
    {
        var host = new Grid { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        host.Children.Add(image);
        int pixels = Pixels(size, 32, 256);
        if (!string.IsNullOrWhiteSpace(item.IconPath))
        {
            image.Source = ImageCache.Picture(item.IconPath, pixels);
            if (image.Source is not null) return host;
        }
        string target = item.Target;
        if (ImageCache.TryGet(ImageCache.IconKey(target, pixels), out var cached) && cached is not null) { image.Source = cached; return host; }

        string ext = Path.GetExtension(target).ToLowerInvariant();
        var fallback = new Border
        {
            CornerRadius = new CornerRadius(size * 0.26), Background = Theme.Tint(item.Accent, 0.16),
            Child = Theme.Icon(ext is ".lnk" or ".url" ? Theme.Glyphs.Link : ext is ".exe" ? Theme.Glyphs.App : Theme.Glyphs.Script, size * 0.46, Theme.Accent(item.Accent)),
        };
        host.Children.Insert(0, fallback);
        // Windows gives every script the same generic document icon; the accent glyph reads better.
        if (ext is ".bat" or ".cmd" or ".ps1" or ".py" or ".js" or ".vbs") return host;
        // Shell icon extraction can take tens of milliseconds per file; do it after the deck is visible.
        void Load(bool retry)
        {
            var source = ImageCache.ShellIcon(target, pixels);
            if (source is not null) { image.Source = source; host.Children.Remove(fallback); return; }
            if (!retry) return;
            var again = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            again.Tick += (_, _) => { again.Stop(); if (host.IsLoaded) Load(false); };
            again.Start();
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => Load(true)));
        return host;
    }

    // ---------- Picture ----------

    private FrameworkElement BuildPicture(LauncherItem item, double w, double h)
    {
        var source = ImageCache.Picture(item.ImagePath, Pixels(Math.Max(w, h), 128, 4096));
        var root = new Grid();
        if (source is null)
        {
            var card = Card(item);
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            stack.Children.Add(Theme.Icon(Theme.Glyphs.Picture, 26, Theme.Muted));
            var missing = Theme.Label("Picture not found", 12, Theme.Muted);
            missing.Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(missing);
            card.Child = stack;
            root.Children.Add(card);
            return root;
        }
        bool fit = item.ImageFit == "fit";
        // A picture is not a card: it takes the deck-wide corner roundness, but opacity and border color only from its own settings.
        // The card color shows behind a letterboxed picture.
        double radius = Appearance.Radius(settings.Appearance, item);
        root.Opacity = Math.Clamp(Appearance.OptionNumber(item, "_opacity") ?? 100, 0, 100) / 100.0;
        if (fit) root.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = Appearance.Look(settings.Appearance, item, solidOpaque: true).Fill });
        var brush = new ImageBrush(source) { Stretch = fit ? Stretch.Uniform : Stretch.UniformToFill };
        RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.HighQuality);
        brush.Freeze();
        root.Children.Add(new Border { CornerRadius = new CornerRadius(radius), Background = brush });
        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            var shade = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(170, 0, 0, 0), 90);
            shade.Freeze();
            var caption = Theme.Label(item.Name, 13, Brushes.White);
            caption.FontWeight = FontWeights.SemiBold;
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            root.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom, CornerRadius = new CornerRadius(0, 0, radius, radius),
                Background = shade, Padding = new Thickness(14, 22, 14, 11), Child = caption,
            });
        }
        if (Appearance.OptionColor(item, "_borderColor") is { } edge)
            root.Children.Add(new Border { CornerRadius = new CornerRadius(radius), BorderBrush = Appearance.Solid(edge), BorderThickness = new Thickness(1.5), IsHitTestVisible = false });
        root.ToolTip = string.IsNullOrWhiteSpace(item.Name) ? null : item.Name;
        return root;
    }

    // ---------- Today's tasks ----------

    private FrameworkElement BuildTasks(LauncherItem item, double w, double h)
    {
        var accent = Theme.Accent(item.Accent);
        var all = Planner.VisibleTasks(item);
        int done = all.Count(t => t.Done);
        bool sync = item.GoogleTasksSync;
        var now = DateTimeOffset.Now;

        // Header: the done/total count, then (with Google sync on) a quiet note when offline or not connected.
        var badge = new StackPanel { Orientation = Orientation.Horizontal };
        if (all.Count > 0) badge.Children.Add(Pill($"{done}/{all.Count}", done == all.Count ? "#9B9A5E" : null));
        if (sync && TasksSyncNote(item, now) is { } note) badge.Children.Add(note);
        var actions = new List<FrameworkElement>();
        if (sync)
        {
            var status = hub.Tasks.StatusOf(item.Id);
            var refresh = Theme.IconButton(Theme.Glyphs.Refresh, status.Syncing ? "Syncing with Google Tasks…" : "Sync with Google Tasks now", 24);
            refresh.Click += (_, _) => { hub.RequestTasksSync(item, true); SetStatus("Syncing with Google Tasks…"); };
            actions.Add(refresh);
        }
        actions.Add(AddButton("Add a task", () => AddTask(item)));
        var (card, body) = Frame(item, badge.Children.Count == 0 ? null : badge, actions.ToArray());
        var list = new StackPanel();
        foreach (var task in all.Where(t => !item.HideCompleted || !t.Done).OrderBy(t => t.Done).ToList())
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(CheckBox(task.Done, accent));
            var text = Theme.Label(task.Text, 13, task.Done ? Theme.Muted : Theme.Text);
            text.TextWrapping = TextWrapping.Wrap;
            if (task.Done) text.TextDecorations = TextDecorations.Strikethrough;
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (sync && task.IsLinked)
            {
                // A small "G" marks tasks that live on Google Tasks too; it is dimmer while a change is waiting to be sent.
                var mark = Theme.Label("G", 10.5, Theme.Accent("#4285f4"));
                mark.FontWeight = FontWeights.Bold;
                mark.Margin = new Thickness(8, 0, 2, 0);
                mark.VerticalAlignment = VerticalAlignment.Center;
                mark.Opacity = task.Modified is null ? 0.85 : 0.4;
                mark.ToolTip = task.Modified is null ? $"Synced with Google Tasks ({item.GoogleTaskListName})" : "Waiting to sync with Google Tasks";
                Grid.SetColumn(mark, 2);
                grid.Children.Add(mark);
            }
            var row = Row(grid);
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (_, e) => { task.Done = !task.Done; TaskEdited(item, task); e.Handled = true; };
            var menu = Menu();
            AddMenuItem(menu, task.Done ? "Mark not done" : "Mark done", () => { task.Done = !task.Done; TaskEdited(item, task); });
            AddMenuItem(menu, "Rename…", () => { if (Ask("Rename task", "Task", task.Text) is { } renamed) { task.Text = renamed; TaskEdited(item, task); } });
            AddMenuItem(menu, "Delete", () => { GoogleTasksSync.Delete(item, task); Changed(item); if (sync) hub.RequestTasksSync(item, true); });
            row.ContextMenu = menu;
            list.Children.Add(row);
        }
        if (all.Count == 0) list.Children.Add(EmptyNote(sync ? "Nothing on your Google Tasks list. Press + to add a task." : item.DailyReset ? "Nothing planned for today. Press + to add a task." : "Nothing here yet. Press + to add a task."));
        body.Children.Add(Scroll(list));
        if (sync) card.ToolTip = $"Synced with the Google Tasks list \"{item.GoogleTaskListName}\"." + (item.DailyReset ? " Finished tasks clear here at midnight but stay on Google." : "");
        else if (item.DailyReset) card.ToolTip = "Finished tasks clear at midnight; unfinished ones carry over.";
        if (sync)
        {
            // Pick up changes made on Google within about two minutes while this is on screen.
            tickers[KeyFor(item)] = (() => false, _ => hub.RequestTasksSync(item, false));
            hub.RequestTasksSync(item, false);
        }
        return card;
    }

    /// <summary>The quiet note next to the count: "Offline — synced 10:42", or why syncing cannot start. Null when all is well (and in smoke renders).</summary>
    private FrameworkElement? TasksSyncNote(LauncherItem item, DateTimeOffset now)
    {
        if (hub.Tasks.Inert) return null;
        string? text;
        bool problem;
        if (!hub.Tasks.Ready) { text = "Google Tasks isn't connected"; problem = true; }
        else
        {
            var status = hub.Tasks.StatusOf(item.Id);
            text = status.Note(now);
            problem = status.NeedsAction;
        }
        if (text is null) return null;
        var label = Theme.Label(text, 10.5, problem ? Theme.Danger : Theme.Muted);
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        label.MaxWidth = 200;
        label.Margin = new Thickness(8, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.ToolTip = text;
        return label;
    }

    /// <summary>A task was changed here: mark it for Google, save, redraw and send it now.</summary>
    private void TaskEdited(LauncherItem item, TaskEntry task)
    {
        GoogleTasksSync.Touch(item, task, DateTimeOffset.UtcNow);
        Changed(item);
        if (item.GoogleTasksSync) hub.RequestTasksSync(item, true);
    }

    private void AddTask(LauncherItem item)
    {
        if (Ask("Add task", "What needs doing?", "", "Add") is not { } text) return;
        var task = new TaskEntry { Text = text };
        item.Tasks.Add(task);
        TaskEdited(item, task);
    }

    // ---------- Habits ----------

    private static string HabitColor(LauncherItem item, HabitEntry habit) => string.IsNullOrWhiteSpace(habit.Color) ? item.Accent : habit.Color;

    private FrameworkElement BuildHabits(LauncherItem item, double w, double h)
    {
        var today = Planner.Today();
        bool week = item.View == "week";
        int doneToday = item.Habits.Count(x => Trackers.IsDone(x, today));
        var tabs = Theme.Segmented(["Today", "Week"], week ? 1 : 0, i => { item.View = i == 1 ? "week" : "today"; Changed(item); }, 24);
        tabs.Width = 120;
        var (card, body) = Frame(item, item.Habits.Count == 0 ? null : Pill($"{doneToday}/{item.Habits.Count}", doneToday == item.Habits.Count ? "#9B9A5E" : null), tabs, AddButton("Add a habit", () => AddHabit(item)));
        if (item.Habits.Count == 0) { body.Children.Add(EmptyNote("No habits yet. Press + to add one, then tick it off each day.")); return card; }
        body.Children.Add(Scroll(week ? HabitWeek(item, w, today) : HabitToday(item, today)));
        return card;
    }

    private StackPanel HabitToday(LauncherItem item, DateOnly today)
    {
        var list = new StackPanel();
        foreach (var habit in item.Habits.ToList())
        {
            var color = Theme.Accent(HabitColor(item, habit));
            bool done = Trackers.IsDone(habit, today);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(CheckBox(done, color));
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = color, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(dot, 1);
            grid.Children.Add(dot);
            var name = Theme.Label(habit.Name, 13, done ? Theme.Text2 : Theme.Text);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 2);
            grid.Children.Add(name);
            int streak = Trackers.Streak(habit, today);
            var streakLabel = Theme.Label(streak > 0 ? $"{streak} day{(streak == 1 ? "" : "s")}" : "—", 11.5, streak > 0 ? color : Theme.Dim);
            streakLabel.FontWeight = FontWeights.SemiBold;
            streakLabel.ToolTip = "Current streak";
            Grid.SetColumn(streakLabel, 3);
            grid.Children.Add(streakLabel);
            var row = Row(grid);
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += (_, e) => { Trackers.Toggle(habit, today, settings.History.DaysFor(HistoryKeys.Habits)); Changed(item); e.Handled = true; };
            row.ContextMenu = HabitMenu(item, habit);
            list.Children.Add(row);
        }
        return list;
    }

    private Grid HabitWeek(LauncherItem item, double w, DateOnly today)
    {
        const double cell = 20, pitch = 25, streakWidth = 34;
        int days = Math.Clamp((int)((w - 28 - 96 - streakWidth) / pitch), 3, 14);
        var table = new Grid();
        table.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < days; i++) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pitch) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(streakWidth) });
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < days; i++)
        {
            var day = today.AddDays(i - days + 1);
            var label = Theme.Label(day.ToString("ddd", CultureInfo.CurrentCulture)[..1], 10, day == today ? Theme.Primary : Theme.Muted);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 0, 0, 4);
            Grid.SetColumn(label, i + 1);
            table.Children.Add(label);
        }
        int row = 1;
        foreach (var habit in item.Habits.ToList())
        {
            var colorHex = HabitColor(item, habit);
            var color = Theme.Accent(colorHex);
            table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(pitch + 2) });
            var name = Theme.Label(habit.Name, 12.5);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.Margin = new Thickness(6, 0, 8, 0);
            name.ToolTip = habit.Name;
            name.ContextMenu = HabitMenu(item, habit);
            Grid.SetRow(name, row);
            table.Children.Add(name);
            for (int i = 0; i < days; i++)
            {
                var day = today.AddDays(i - days + 1);
                bool done = Trackers.IsDone(habit, day);
                var dot = new Border
                {
                    Width = cell, Height = cell, CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand,
                    Background = done ? color : Theme.Surface3,
                    BorderBrush = day == today ? color : Brushes.Transparent, BorderThickness = new Thickness(1.5),
                    ToolTip = day.ToString("dddd, MMM d", CultureInfo.CurrentCulture) + (done ? " — done" : ""),
                };
                if (done) dot.Child = Theme.Icon(Theme.Glyphs.Check, 9, Theme.Background);
                dot.MouseLeftButtonUp += (_, e) => { Trackers.Toggle(habit, day, settings.History.DaysFor(HistoryKeys.Habits)); Changed(item); e.Handled = true; };
                Grid.SetRow(dot, row);
                Grid.SetColumn(dot, i + 1);
                table.Children.Add(dot);
            }
            int streak = Trackers.Streak(habit, today);
            var streakLabel = Theme.Label(streak > 0 ? $"{streak}d" : "—", 11.5, streak > 0 ? color : Theme.Dim);
            streakLabel.FontWeight = FontWeights.SemiBold;
            streakLabel.HorizontalAlignment = HorizontalAlignment.Right;
            streakLabel.ToolTip = $"{streak}-day streak";
            Grid.SetRow(streakLabel, row);
            Grid.SetColumn(streakLabel, days + 1);
            table.Children.Add(streakLabel);
            row++;
        }
        return table;
    }

    private ContextMenu HabitMenu(LauncherItem item, HabitEntry habit)
    {
        var menu = Menu();
        AddMenuItem(menu, "Rename…", () => { if (Ask("Rename habit", "Habit", habit.Name) is { } renamed) { habit.Name = renamed; Changed(item); } });
        var colors = new MenuItem { Header = "Color", Padding = new Thickness(10, 7, 10, 7) };
        foreach (var swatch in Theme.Swatches)
        {
            var entry = new MenuItem { Header = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = Theme.Accent(swatch) }, Padding = new Thickness(10, 5, 10, 5) };
            entry.Click += (_, _) => { habit.Color = swatch; Changed(item); };
            colors.Items.Add(entry);
        }
        menu.Items.Add(colors);
        AddMenuItem(menu, "Delete", () =>
        {
            if (Confirm($"Delete \"{habit.Name}\" and its history?", "Delete habit")) { item.Habits.Remove(habit); Changed(item); }
        });
        return menu;
    }

    private void AddHabit(LauncherItem item)
    {
        if (Ask("Add habit", "Something to do every day", "", "Add") is not { } name) return;
        // New habits cycle through the palette so they are easy to tell apart.
        item.Habits.Add(new HabitEntry { Name = name, Color = Theme.Swatches[item.Habits.Count % Theme.Swatches.Length] });
        Changed(item);
    }

    // ---------- Counter ----------

    private FrameworkElement BuildCounter(LauncherItem item, double w, double h)
    {
        var accent = Theme.Accent(item.Accent);
        var (card, body) = Frame(item, null);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        double buttonSize = Math.Clamp(Math.Min(h * 0.26, w * 0.2), 28, 48);
        double numberSize = Math.Clamp(Math.Min(h * 0.4, (w - 2 * buttonSize - 40) / 3.2), 18, 110);
        var number = Theme.Label("", numberSize, Theme.Text);
        void ShowCount()
        {
            number.Inlines.Clear();
            number.Inlines.Add(new System.Windows.Documents.Run(item.Count.ToString("N0", CultureInfo.CurrentCulture)));
            if (item.CountGoal <= 0) return;
            number.Inlines.Add(new System.Windows.Documents.Run(" / " + item.CountGoal.ToString("N0", CultureInfo.CurrentCulture)) { FontSize = numberSize * 0.45, Foreground = Theme.Muted });
            number.Foreground = item.Count >= item.CountGoal ? accent : Theme.Text;
        }
        ShowCount();
        number.FontWeight = FontWeights.SemiBold;
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(number, 1);
        grid.Children.Add(number);
        void Bump(int direction)
        {
            Trackers.Bump(item, direction);
            ShowCount();
            Changed(item, rerender: false);
        }
        var minus = Theme.IconButton(Theme.Glyphs.Remove, $"Subtract {item.Step}", buttonSize);
        minus.Click += (_, _) => Bump(-1);
        var plus = Theme.IconButton(Theme.Glyphs.Add, $"Add {item.Step}", buttonSize);
        plus.Background = accent;
        plus.BorderBrush = accent;
        ((TextBlock)plus.Content).Foreground = Theme.OnPrimary;
        plus.Click += (_, _) => Bump(1);
        Grid.SetColumn(plus, 2);
        grid.Children.Add(minus);
        grid.Children.Add(plus);
        body.Children.Add(grid);
        return card;
    }

    // ---------- Time ----------

    private FrameworkElement BuildClock(LauncherItem item, double w, double h)
    {
        var card = Card(item);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        string Format(DateTime now) => item.Use24Hour
            ? now.ToString(item.ShowSeconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture)
            : now.ToString(item.ShowSeconds ? "h:mm:ss tt" : "h:mm tt", CultureInfo.InvariantCulture);
        int chars = Format(new DateTime(2000, 1, 1, 12, 59, 59)).Length;
        double size = Math.Clamp(Math.Min(h * 0.42, (w - 32) / (chars * 0.6)), 16, 140);
        var time = Theme.Label(Format(DateTime.Now), size, Theme.Text);
        time.FontWeight = FontWeights.SemiBold;
        time.HorizontalAlignment = HorizontalAlignment.Center;
        System.Windows.Documents.Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        var date = Theme.Label(DateTime.Now.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture), Math.Clamp(size * 0.24, 11, 20), Theme.Text2);
        date.HorizontalAlignment = HorizontalAlignment.Center;
        date.Margin = new Thickness(0, size * 0.04, 0, 0);
        stack.Children.Add(time);
        if (h >= 96 && item.ShowDate) stack.Children.Add(date);
        card.Child = stack;
        tickers[KeyFor(item)] = (() => item.ShowSeconds, now =>
        {
            time.Text = Format(now.LocalDateTime);
            date.Text = now.LocalDateTime.ToString("dddd, MMMM d, yyyy", CultureInfo.CurrentCulture);
        });
        return card;
    }

    // ---------- Timer (pomodoro) ----------

    private FrameworkElement BuildTimer(LauncherItem item, double w, double h)
    {
        var p = item.Pomodoro;
        var accent = Theme.Accent(item.Accent);
        var (card, body) = Frame(item, null);
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        int modeIndex = p.Mode switch { "break" => 1, "long" => 2, _ => 0 };
        var tabs = Theme.Segmented(w >= 260 ? ["Focus", "Break", "Long Break"] : ["Focus", "Break", "Long"], modeIndex,
            i => { Planner.SetMode(p, i switch { 1 => "break", 2 => "long", _ => "focus" }); Changed(item); }, 26, item.Accent);
        grid.Children.Add(tabs);

        var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        double size = Math.Clamp(Math.Min(h * 0.2, w * 0.2), 24, 84);
        string Clock(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
        var time = Theme.Label(Clock(Planner.Remaining(p, DateTimeOffset.Now)), size, Theme.Text);
        time.FontWeight = FontWeights.SemiBold;
        time.HorizontalAlignment = HorizontalAlignment.Center;
        System.Windows.Documents.Typography.SetNumeralAlignment(time, FontNumeralAlignment.Tabular);
        var mode = Theme.WidgetTitle(p.Mode switch { "break" => "Short break", "long" => "Long break", _ => "Focus" }, accent);
        mode.HorizontalAlignment = HorizontalAlignment.Center;
        center.Children.Add(time);
        center.Children.Add(mode);
        Grid.SetRow(center, 1);
        grid.Children.Add(center);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 8) };
        var reset = Theme.IconButton(Theme.Glyphs.Reset, "Reset", 32);
        reset.Click += (_, _) => { Planner.Reset(p); Changed(item); };
        bool running = Planner.IsRunning(p);
        var play = new Border
        {
            Width = 52, Height = 52, CornerRadius = new CornerRadius(26), Background = accent, Cursor = Cursors.Hand, Margin = new Thickness(16, 0, 16, 0),
            Child = Theme.Icon(running ? Theme.Glyphs.Pause : Theme.Glyphs.Play, 20, Theme.OnPrimary), ToolTip = running ? "Pause" : "Start",
        };
        play.MouseLeftButtonUp += (_, e) =>
        {
            if (Planner.IsRunning(p)) Planner.Pause(p, DateTimeOffset.Now); else Planner.Start(p, DateTimeOffset.Now);
            Changed(item);
            e.Handled = true;
        };
        var skip = Theme.IconButton(Theme.Glyphs.Next, "Skip to the next session", 32);
        skip.Click += (_, _) => { Planner.Advance(p, Planner.Today(), completed: false, settings.History.DaysFor(HistoryKeys.Timer)); Changed(item); };
        buttons.Children.Add(reset);
        buttons.Children.Add(play);
        buttons.Children.Add(skip);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);

        var footer = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
        for (int i = 0; i < Math.Max(1, p.LongBreakEvery); i++)
            dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Margin = new Thickness(3, 0, 3, 0), Fill = i < p.Cycle ? accent : Theme.Surface3 });
        footer.Children.Add(dots);
        int sessions = Planner.SessionsOn(p, Planner.Today());
        var today = Theme.Label($"Today: {sessions} session{(sessions == 1 ? "" : "s")}", 11, Theme.Muted);
        today.HorizontalAlignment = HorizontalAlignment.Center;
        footer.Children.Add(today);
        if (h >= 250) { Grid.SetRow(footer, 3); grid.Children.Add(footer); }
        body.Children.Add(grid);

        tickers[KeyFor(item)] = (() => Planner.IsRunning(p), now =>
        {
            if (Planner.IsFinished(p, now))
            {
                string finished = Planner.Advance(p, Planner.Today(), completed: true, settings.History.DaysFor(HistoryKeys.Timer));
                if (item.AutoNext) Planner.Start(p, now);
                // Linked timers: the peers take the new state right now, so only this one announces the end.
                hub.SyncLinked(item);
                SaveNow();
                RebuildContent(item);
                if (item.TimerSound) SystemSounds.Asterisk.Play();
                hub.RaiseNotify(item.Name, finished == "focus"
                    ? $"Focus session done. Time for a {(p.Mode == "long" ? p.LongBreakMinutes : p.BreakMinutes)}-minute break."
                    : "Break's over. Ready to focus?");
                return;
            }
            time.Text = Clock(Planner.Remaining(p, now));
        });
        return card;
    }

    // ---------- Schedule ----------

    private FrameworkElement BuildSchedule(LauncherItem item, double w, double h)
    {
        bool dayView = item.View == "day" || (item.View != "week" && w < 520);
        return dayView ? ScheduleDay(item, w, h) : ScheduleWeek(item, w, h);
    }

    private FrameworkElement ScheduleWeek(LauncherItem item, double w, double h)
    {
        int offset = weekOffsets.GetValueOrDefault(item.Id);
        var today = Planner.Today();
        var monday = Planner.Monday(today).AddDays(7 * offset);
        var label = Theme.Label(offset == 0 ? "This week" : Planner.WeekRange(monday), 11.5, Theme.Text2);
        label.Margin = new Thickness(6, 0, 6, 0);
        label.Cursor = Cursors.Hand;
        label.ToolTip = "Back to this week";
        label.MouseLeftButtonUp += (_, _) => { weekOffsets.Remove(item.Id); RebuildContent(item); };
        // Google and iCal events come from memory (the feed fetches in the background and redraws this widget when they arrive).
        var external = CalendarData.Query(monday, monday.AddDays(7), QueryFor(item));
        var (card, body) = Frame(item, CalendarNote(external),
            NavButton(Theme.Glyphs.Left, "Previous week", () => { weekOffsets[item.Id] = offset - 1; RebuildContent(item); }),
            label,
            NavButton(Theme.Glyphs.Right, "Next week", () => { weekOffsets[item.Id] = offset + 1; RebuildContent(item); }),
            AddButton("Add a block or event", () => AddEntry(item, offset == 0 ? today : monday, null)));
        int dayCount = item.HideWeekends ? 5 : 7;
        var timedEvents = external.Events.Where(e => !e.AllDay).ToList();
        var allDayLayout = item.ShowAllDay ? AllDayLayout(external.Events, monday, dayCount) : ([], 0, 0);
        double stripHeight = allDayLayout.Rows == 0 ? 0 : allDayLayout.Rows * 18 + 4;

        // Hours shown: at least 8 AM–6 PM, widened to fit every block and event.
        var minutes = item.Blocks.SelectMany(b => new[] { Planner.Minutes(b.Start), Planner.Minutes(b.End) }).OfType<int>().ToList();
        for (int d = 0; d < dayCount; d++)
            foreach (var ev in timedEvents)
                if (ev.OnDay(monday.AddDays(d))) { var (s0, e0) = ev.MinutesOn(monday.AddDays(d)); minutes.Add(s0); minutes.Add(e0); }
        int startHour = item.StartHour >= 0 ? item.StartHour : Math.Clamp(minutes.Count > 0 ? Math.Min(8, minutes.Min() / 60) : 8, 0, 23);
        int endHour = item.EndHour > startHour ? item.EndHour : Math.Clamp(minutes.Count > 0 ? Math.Max(18, (minutes.Max() + 59) / 60) : 18, startHour + 1, 24);
        const double gutter = 38, headerHeight = 34;
        double innerW = w - 26 - gutter, innerH = h - 23 - 34 - headerHeight - stripHeight;
        double colW = innerW / dayCount, perMinute = Math.Max(0.05, innerH / ((endHour - startHour) * 60.0));

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(headerHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(stripHeight) });
        grid.RowDefinitions.Add(new RowDefinition());
        var heads = new Canvas();
        var canvasBody = new Canvas { ClipToBounds = true, Background = Brushes.Transparent };
        Grid.SetRow(canvasBody, 2);
        grid.Children.Add(heads);
        if (allDayLayout.Rows > 0)
        {
            var strip = AllDayStrip(item, allDayLayout.Bars, allDayLayout.Hidden, gutter, colW, stripHeight);
            Grid.SetRow(strip, 1);
            grid.Children.Add(strip);
        }
        grid.Children.Add(canvasBody);

        for (int d = 0; d < dayCount; d++)
        {
            var date = monday.AddDays(d);
            bool isToday = date == today;
            double x = gutter + d * colW;
            if (isToday) { var band = new Border { Width = colW, Height = Math.Max(0, innerH), Background = Theme.Tint(Theme.PrimaryHex, 0.05) }; Canvas.SetLeft(band, x); canvasBody.Children.Add(band); }
            var head = new StackPanel { Width = colW };
            var dn = Theme.Label(Planner.DayNames[d].ToUpperInvariant(), 9.5, isToday ? Theme.Primary : Theme.Muted);
            dn.FontWeight = FontWeights.SemiBold;
            dn.HorizontalAlignment = HorizontalAlignment.Center;
            var num = Theme.Label(date.Day.ToString(CultureInfo.CurrentCulture), 11.5, isToday ? Theme.OnPrimary : Theme.Text2);
            num.FontWeight = FontWeights.SemiBold;
            var numHost = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = isToday ? Theme.Primary : Brushes.Transparent, Child = num, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 1, 0, 0) };
            num.HorizontalAlignment = HorizontalAlignment.Center;
            head.Children.Add(dn);
            head.Children.Add(numHost);
            Canvas.SetLeft(head, x);
            heads.Children.Add(head);
        }

        // Hour lines and labels; labels thin out when rows get short.
        int every = perMinute * 60 >= 22 ? 1 : perMinute * 60 >= 12 ? 2 : 3;
        for (int hour = startHour; hour <= endHour; hour++)
        {
            double y = (hour - startHour) * 60 * perMinute;
            var line = new System.Windows.Shapes.Rectangle { Width = Math.Max(0, innerW), Height = 1, Fill = Theme.Line };
            Canvas.SetLeft(line, gutter);
            Canvas.SetTop(line, y);
            canvasBody.Children.Add(line);
            if ((hour - startHour) % every != 0 || hour == endHour) continue;
            var text = Theme.Label(hour == 0 || hour == 24 ? "12 AM" : hour < 12 ? $"{hour} AM" : hour == 12 ? "12 PM" : $"{hour - 12} PM", 9, Theme.Muted);
            Canvas.SetLeft(text, 0);
            Canvas.SetTop(text, y - 2);
            canvasBody.Children.Add(text);
        }

        for (int d = 0; d < dayCount; d++)
        {
            var date = monday.AddDays(d);
            // Local blocks and external events share lanes, so nothing overlaps.
            var slots = Planner.Occurrences(item.Blocks, date).Select(b => new Slot(b, null, Planner.Minutes(b.Start)!.Value, Planner.Minutes(b.End)!.Value)).ToList();
            foreach (var ev in timedEvents.Where(x => x.OnDay(date)))
            {
                var (es, ee) = ev.MinutesOn(date);
                slots.Add(new Slot(null, ev, es, ee));
            }
            foreach (var (slot, lane, lanes) in CalendarLanes.Assign(slots, x => x.Start, x => x.End))
            {
                int s = slot.Start, e = slot.End;
                if (e <= s) e = s + 15;
                double top = (s - startHour * 60) * perMinute, height = Math.Max(4, (e - s) * perMinute - 2);
                double laneW = (colW - 4) / lanes;
                var view = slot.Block is { } block ? BlockView(item, block, date, laneW - 2, height, h < 360) : ExternalBlock(item, slot.Event!, date, laneW - 2, height, h < 360);
                Canvas.SetLeft(view, gutter + d * colW + 2 + lane * laneW);
                Canvas.SetTop(view, top + 1);
                canvasBody.Children.Add(view);
            }
        }

        // The current time, moved once a minute by rebuilding this widget.
        if (offset == 0)
        {
            var now = DateTime.Now;
            int nowMinutes = now.Hour * 60 + now.Minute;
            if (nowMinutes >= startHour * 60 && nowMinutes <= endHour * 60)
            {
                int d = ((int)today.DayOfWeek + 6) % 7;
                double y = (nowMinutes - startHour * 60) * perMinute;
                if (d >= dayCount) goto noNowLine;
                var nowLine = new System.Windows.Shapes.Rectangle { Width = colW, Height = 2, Fill = Theme.Primary, IsHitTestVisible = false };
                Canvas.SetLeft(nowLine, gutter + d * colW);
                Canvas.SetTop(nowLine, y - 1);
                var nowDot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Theme.Primary, IsHitTestVisible = false };
                Canvas.SetLeft(nowDot, gutter + d * colW - 4);
                Canvas.SetTop(nowDot, y - 4);
                Panel.SetZIndex(nowLine, 5);
                Panel.SetZIndex(nowDot, 5);
                canvasBody.Children.Add(nowLine);
                canvasBody.Children.Add(nowDot);
            }
        }
        noNowLine:

        // Double-click an empty slot to add a block there.
        canvasBody.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 2 || e.OriginalSource != canvasBody) return;
            var point = e.GetPosition(canvasBody);
            int d = (int)Math.Floor((point.X - gutter) / colW);
            if (d < 0 || d >= dayCount) return;
            int start = startHour * 60 + (int)(Math.Round(point.Y / perMinute / 30) * 30);
            start = Math.Clamp(start, 0, 23 * 60);
            AddEntry(item, monday.AddDays(d), start);
            e.Handled = true;
        };
        canvasBody.ToolTip = item.Blocks.Count == 0 && timedEvents.Count == 0 ? "Double-click a time to add a block, or press +." : null;
        body.Children.Add(grid);
        tickers[KeyFor(item)] = (() => false, _ => RebuildContent(item));
        return card;
    }

    /// <summary>One thing in a day column: a local block or an external event, with its minutes.</summary>
    private readonly record struct Slot(ScheduleBlock? Block, CalendarEvent? Event, int Start, int End);

    private Border BlockView(LauncherItem item, ScheduleBlock block, DateOnly date, double width, double height, bool tight)
    {
        var color = Theme.Accent(block.Color);
        var stack = new StackPanel();
        string range = $"{Planner.Format(block.Start, false)} – {Planner.Format(block.End, false)}";
        if (height >= 13)
        {
            var title = Theme.Label(block.Title, tight ? 9.5 : 10.5, Theme.Text);
            title.FontWeight = FontWeights.SemiBold;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(title);
        }
        if (height >= 30 && width >= 60)
        {
            var time = Theme.Label(range, 9, Theme.Text2);
            time.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(time);
        }
        var view = new Border
        {
            Width = Math.Max(4, width), Height = height, CornerRadius = new CornerRadius(5), Background = Theme.Tint(block.Color, 0.2),
            BorderBrush = color, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(5, 2, 3, 2), ClipToBounds = true,
            Child = stack, Cursor = Cursors.Hand,
        };
        var tip = new StackPanel();
        var tipTitle = Theme.Label(block.Title, 13, Theme.Text);
        tipTitle.FontWeight = FontWeights.SemiBold;
        tip.Children.Add(tipTitle);
        tip.Children.Add(Theme.Label(range, 12, Theme.Text2));
        foreach (var part in block.Events.OrderBy(e => Planner.Minutes(e.Start)))
            tip.Children.Add(Theme.Label($"{Planner.Format(part.Start, false)}  {part.Title}", 11.5, Theme.Muted));
        view.ToolTip = new ToolTip { Content = tip, Background = Theme.Surface3, BorderBrush = Theme.LineStrong };
        view.MouseLeftButtonUp += (_, e) => { EditBlock(item, block); e.Handled = true; };
        var menu = Menu();
        AddMenuItem(menu, "Edit…", () => EditBlock(item, block));
        if (string.IsNullOrEmpty(block.Date))
            AddMenuItem(menu, $"Skip on {date.ToString("ddd, MMM d", CultureInfo.CurrentCulture)}", () => { block.CancelledDates.Add(Planner.Key(date)); Changed(item); });
        AddMenuItem(menu, "Delete block", () => { if (Confirm($"Delete \"{block.Title}\" from every day?", "Delete block")) { item.Blocks.Remove(block); Changed(item); } });
        view.ContextMenu = menu;
        return view;
    }

    private FrameworkElement ScheduleDay(LauncherItem item, double w, double h)
    {
        int offset = dayOffsets.GetValueOrDefault(item.Id);
        var today = Planner.Today();
        var date = today.AddDays(offset);
        string name = offset switch { 0 => "Today", 1 => "Tomorrow", -1 => "Yesterday", _ => date.ToString("ddd, MMM d", CultureInfo.CurrentCulture) };
        var label = Theme.Label(name, 11.5, Theme.Text2);
        label.Margin = new Thickness(6, 0, 6, 0);
        label.Cursor = Cursors.Hand;
        label.MouseLeftButtonUp += (_, _) => { dayOffsets.Remove(item.Id); RebuildContent(item); };
        var external = CalendarData.Query(date, date.AddDays(1), QueryFor(item));
        var (card, body) = Frame(item, CalendarNote(external),
            NavButton(Theme.Glyphs.Left, "Previous day", () => { dayOffsets[item.Id] = offset - 1; RebuildContent(item); }),
            label,
            NavButton(Theme.Glyphs.Right, "Next day", () => { dayOffsets[item.Id] = offset + 1; RebuildContent(item); }),
            AddButton("Add a block or event", () => AddEntry(item, date, null)));
        var list = new StackPanel();
        var now = DateTime.Now;
        int nowMinutes = now.Hour * 60 + now.Minute;
        var allDayEvents = item.ShowAllDay ? external.Events.Where(e => e.AllDay).ToList() : [];
        if (allDayEvents.Count > 0) list.Children.Add(AllDayChips(item, allDayEvents));
        // Local blocks and external events are listed together by start time.
        var entries = new List<(int Start, FrameworkElement Row)>();
        foreach (var ev in external.Events.Where(e => !e.AllDay))
        {
            var (es, ee) = ev.MinutesOn(date);
            bool evCurrent = offset == 0 && nowMinutes >= es && nowMinutes < ee, evPast = offset < 0 || (offset == 0 && nowMinutes >= ee);
            entries.Add((es, ExternalRow(item, ev, date, evCurrent, evPast)));
        }
        foreach (var block in Planner.Occurrences(item.Blocks, date))
        {
            int s = Planner.Minutes(block.Start)!.Value, e = Planner.Minutes(block.End)!.Value;
            bool current = offset == 0 && nowMinutes >= s && nowMinutes < e, past = offset < 0 || (offset == 0 && nowMinutes >= e);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = Theme.Accent(block.Color), HorizontalAlignment = HorizontalAlignment.Left });
            var text = new StackPanel();
            var title = Theme.Label(block.Title, 13, Theme.Text);
            title.FontWeight = FontWeights.SemiBold;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            text.Children.Add(title);
            text.Children.Add(Theme.Label($"{Planner.Format(block.Start, false)} – {Planner.Format(block.End, false)}", 11.5, Theme.Text2));
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            if (current)
            {
                int left = e - nowMinutes;
                var badge = Pill(left >= 60 ? $"Now · {left / 60}h {left % 60}m left" : $"Now · {left}m left", block.Color);
                Grid.SetColumn(badge, 2);
                grid.Children.Add(badge);
            }
            var row = Row(grid);
            row.Margin = new Thickness(0, 0, 0, 4);
            row.Opacity = past ? 0.45 : 1;
            if (current) { row.Background = Theme.Tint(block.Color, 0.12); row.MouseLeave += (_, _) => row.Background = Theme.Tint(block.Color, 0.12); }
            row.Cursor = Cursors.Hand;
            row.ToolTip = block.Events.Count == 0 ? null : string.Join("\n", block.Events.OrderBy(x => Planner.Minutes(x.Start)).Select(x => $"{Planner.Format(x.Start, false)}  {x.Title}"));
            row.MouseLeftButtonUp += (_, ev) => { EditBlock(item, block); ev.Handled = true; };
            entries.Add((s, row));
        }
        foreach (var entry in entries.OrderBy(x => x.Start)) list.Children.Add(entry.Row);
        if (list.Children.Count == 0) list.Children.Add(EmptyNote("Nothing scheduled. Press + to add a block."));
        body.Children.Add(Scroll(list));
        tickers[KeyFor(item)] = (() => false, _ => RebuildContent(item));
        return card;
    }

    private void EditBlock(LauncherItem item, ScheduleBlock? block, ScheduleBlock? template = null)
    {
        var editor = new ScheduleBlockEditor(DialogOwner, block ?? template ?? new ScheduleBlock { Days = [Planner.DayName(Planner.Today())] }, block is null);
        if (Modal(editor.ShowDialog) != true) return;
        if (editor.Deleted && block is not null) item.Blocks.Remove(block);
        else if (editor.Result is { } result)
        {
            int index = block is null ? -1 : item.Blocks.IndexOf(block);
            if (index >= 0) item.Blocks[index] = result; else item.Blocks.Add(result);
        }
        Changed(item);
    }

    // ---------- Goals for this week ----------

    private DateOnly WeekShown(LauncherItem item) => Planner.Monday(Planner.Today()).AddDays(7 * goalOffsets.GetValueOrDefault(item.Id));

    private FrameworkElement BuildGoals(LauncherItem item, double w, double h)
    {
        var accent = Theme.Accent(item.Accent);
        int offset = goalOffsets.GetValueOrDefault(item.Id);
        var monday = WeekShown(item);
        var goals = Planner.GoalsFor(item, monday).ToList();
        int done = goals.Count(g => g.Done);
        var (card, body) = Frame(item, goals.Count == 0 ? null : Pill($"{done}/{goals.Count}", done == goals.Count ? "#9B9A5E" : null),
            NavButton(Theme.Glyphs.Left, "Previous week", () => { goalOffsets[item.Id] = offset - 1; RebuildContent(item); }),
            NavButton(Theme.Glyphs.Right, "Next week", () => { goalOffsets[item.Id] = offset + 1; RebuildContent(item); }),
            AddButton("Add a goal", () => AddGoal(item, monday)));

        var top = new StackPanel { Margin = new Thickness(0, -4, 0, 10) };
        DockPanel.SetDock(top, Dock.Top);
        var range = Theme.Label((offset == 0 ? "This week · " : offset == 1 ? "Next week · " : offset == -1 ? "Last week · " : "") + Planner.WeekRange(monday), 11.5, Theme.Text2);
        top.Children.Add(range);
        if (goals.Count > 0)
        {
            double progress = goals.Average(Planner.Progress);
            var bar = new Grid { Height = 6, Margin = new Thickness(0, 7, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, progress), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0.0001, 1 - progress), GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(3), Background = Theme.Surface3 };
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = accent });
            bar.ToolTip = $"{progress:P0} of this week's goals";
            top.Children.Add(bar);
        }
        int carry = offset == 0 ? Planner.GoalsFor(item, monday.AddDays(-7)).Count(g => !g.Done && goals.All(x => !string.Equals(x.Text, g.Text, StringComparison.CurrentCultureIgnoreCase))) : 0;
        if (carry > 0)
        {
            var link = Theme.Label($"↪ Carry over {carry} unfinished from last week", 11.5, accent);
            link.Cursor = Cursors.Hand;
            link.Margin = new Thickness(0, 8, 0, 0);
            link.MouseLeftButtonUp += (_, e) => { CarryOver(item); e.Handled = true; };
            top.Children.Add(link);
        }
        body.Children.Add(top);

        var list = new StackPanel();
        foreach (var goal in goals.OrderBy(g => g.Done))
        {
            bool expanded = expandedGoals.Contains(goal.Id);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var box = CheckBox(goal.Done, accent);
            box.MouseLeftButtonUp += (_, e) => { goal.Done = !goal.Done; Changed(item); e.Handled = true; };
            grid.Children.Add(box);
            var text = Theme.Label(goal.Text, 13, goal.Done ? Theme.Muted : Theme.Text);
            text.TextWrapping = TextWrapping.Wrap;
            if (goal.Done) text.TextDecorations = TextDecorations.Strikethrough;
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var meta = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (goal.Milestones.Count > 0) meta.Children.Add(Theme.Label($"{goal.Milestones.Count(m => m.Done)}/{goal.Milestones.Count}", 11, Theme.Muted));
            meta.Children.Add(Theme.Icon(expanded ? "" : "", 10, Theme.Muted));
            ((FrameworkElement)meta.Children[^1]).Margin = new Thickness(6, 0, 0, 0);
            Grid.SetColumn(meta, 2);
            grid.Children.Add(meta);
            var row = Row(grid);
            row.Cursor = Cursors.Hand;
            row.ToolTip = "Click to show steps";
            row.MouseLeftButtonUp += (_, e) => { if (!expandedGoals.Remove(goal.Id)) expandedGoals.Add(goal.Id); RebuildContent(item); e.Handled = true; };
            var menu = Menu();
            AddMenuItem(menu, "Rename…", () => { if (Ask("Rename goal", "Goal", goal.Text) is { } renamed) { goal.Text = renamed; Changed(item); } });
            AddMenuItem(menu, "Add step…", () => AddStep(item, goal));
            AddMenuItem(menu, "Move to next week", () => { goal.Week = Planner.Key(Planner.Monday(DateOnly.Parse(goal.Week, CultureInfo.InvariantCulture)).AddDays(7)); Changed(item); });
            AddMenuItem(menu, "Delete", () => { item.Goals.Remove(goal); Changed(item); });
            row.ContextMenu = menu;
            list.Children.Add(row);
            if (!expanded) continue;
            foreach (var step in goal.Milestones.ToList())
            {
                var stepGrid = new Grid { Margin = new Thickness(28, 0, 0, 0) };
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
                stepGrid.ColumnDefinitions.Add(new ColumnDefinition());
                stepGrid.Children.Add(CheckBox(step.Done, accent, 14));
                var stepText = Theme.Label(step.Text, 12, step.Done ? Theme.Muted : Theme.Text2);
                stepText.TextWrapping = TextWrapping.Wrap;
                Grid.SetColumn(stepText, 1);
                stepGrid.Children.Add(stepText);
                var stepRow = Row(stepGrid);
                stepRow.Padding = new Thickness(6, 3, 6, 3);
                stepRow.Cursor = Cursors.Hand;
                stepRow.MouseLeftButtonUp += (_, e) => { step.Done = !step.Done; if (goal.Milestones.All(m => m.Done)) goal.Done = true; else if (!step.Done) goal.Done = false; Changed(item); e.Handled = true; };
                var stepMenu = Menu();
                AddMenuItem(stepMenu, "Rename…", () => { if (Ask("Rename step", "Step", step.Text) is { } renamed) { step.Text = renamed; Changed(item); } });
                AddMenuItem(stepMenu, "Delete", () => { goal.Milestones.Remove(step); Changed(item); });
                stepRow.ContextMenu = stepMenu;
                list.Children.Add(stepRow);
            }
            var addStep = Theme.Label("+ Add step", 11.5, accent);
            addStep.Margin = new Thickness(40, 2, 0, 6);
            addStep.Cursor = Cursors.Hand;
            addStep.MouseLeftButtonUp += (_, e) => { AddStep(item, goal); e.Handled = true; };
            list.Children.Add(addStep);
        }
        if (goals.Count == 0) list.Children.Add(EmptyNote(offset == 0 ? "No goals for this week yet. Press + to set one." : "No goals for this week."));
        body.Children.Add(Scroll(list));
        return card;
    }

    private void AddGoal(LauncherItem item, DateOnly monday)
    {
        if (Ask("Add goal", "What do you want to get done this week?", "", "Add") is not { } text) return;
        item.Goals.Add(new WeeklyGoal { Text = text, Week = Planner.Key(monday) });
        Changed(item);
    }

    private void AddStep(LauncherItem item, WeeklyGoal goal)
    {
        if (Ask("Add step", "A smaller step toward this goal", "", "Add") is not { } text) return;
        goal.Milestones.Add(new Milestone { Text = text });
        goal.Done = false;
        expandedGoals.Add(goal.Id);
        Changed(item);
    }

    private void CarryOver(LauncherItem item)
    {
        int added = Planner.CarryOver(item, Planner.Monday(Planner.Today()));
        goalOffsets.Remove(item.Id);
        Changed(item);
        SetStatus(added > 0 ? $"Carried over {added} goal(s) from last week." : "Nothing left over from last week.");
    }
}
