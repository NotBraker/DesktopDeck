using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>Adds or edits one automation rule: when (time of day, app in front, monitor setup), which layout to switch to, and whether to switch back.</summary>
internal sealed class AutomationRuleEditor : Window
{
    private static readonly (string Text, int Day)[] WeekDays = [("Mon", 1), ("Tue", 2), ("Wed", 3), ("Thu", 4), ("Fri", 5), ("Sat", 6), ("Sun", 0)];

    private readonly AutomationRule draft;
    private readonly DeckSettings settings;
    private readonly IReadOnlyList<DisplayInfo> displays;
    private readonly Border whenHost = new(), thenHost = new();
    private readonly TextBlock validation = Theme.Label("", 12, Theme.Danger);
    private TextBox? startBox, endBox, processBox, countBox;
    private bool exactSet;

    internal AutomationRule? Result { get; private set; }

    internal AutomationRuleEditor(Window owner, AutomationRule rule, bool isNew, DeckSettings settings, IReadOnlyList<DisplayInfo> displays)
    {
        draft = rule.Copy();
        this.settings = settings;
        this.displays = displays;
        exactSet = draft.DisplayIds.Count > 0;
        Owner = owner;
        Title = isNew ? "Add rule" : "Edit rule";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        FontFamily = Theme.UiFont;

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 18) };
        var heading = Theme.Label(Title, 22);
        heading.Margin = new Thickness(0, 0, 0, 16);
        root.Children.Add(heading);
        root.Children.Add(Caption("When"));
        root.Children.Add(whenHost);
        root.Children.Add(Caption("Then switch"));
        root.Children.Add(thenHost);
        root.Children.Add(ItemEditor.SwitchRow("Switch back to the previous layout when this no longer applies", draft.SwitchBack, on => draft.SwitchBack = on));
        validation.TextWrapping = TextWrapping.Wrap;
        validation.Margin = new Thickness(0, 0, 0, 10);
        root.Children.Add(validation);
        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => DialogResult = false;
        var save = Theme.Button(isNew ? "Add rule" : "Save changes", true);
        save.IsDefault = true;
        save.Click += (_, _) => Save();
        root.Children.Add(ItemEditor.Row(cancel, save));
        Content = root;
        ShowWhen();
        ShowThen();
        Loaded += (_, _) => Activate();
    }

    /// <summary>Smoke test: draws the editor's content without showing the window.</summary>
    internal void RenderForSmoke(Action<FrameworkElement, string> snap, string path)
    {
        var root = (StackPanel)Content;
        root.Background = Theme.Background;
        root.Measure(new Size(Width, double.PositiveInfinity));
        root.Arrange(new Rect(0, 0, Width, root.DesiredSize.Height));
        root.UpdateLayout();
        snap(root, path);
    }

    private static TextBlock Caption(string text)
    {
        var label = Theme.Label(text, 12, Theme.Muted);
        label.Margin = new Thickness(0, 0, 0, 6);
        return label;
    }

    private static Border Pill(string text, bool on, Action click, double minWidth = 0)
    {
        var label = Theme.Label(text, 12.5, on ? Theme.OnPrimary : Theme.Text2);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var pill = new Border
        {
            MinWidth = minWidth, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(14, 0, 14, 0),
            Cursor = Cursors.Hand, Background = on ? Theme.Primary : Theme.Surface3, Child = label,
        };
        PanelKit.MakeFocusable(pill, click, text);
        pill.BorderThickness = new Thickness(1);
        pill.BorderBrush = Brushes.Transparent;
        pill.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return pill;
    }

    private static Border Tabs(string[] labels, int selected, Action<int> select)
    {
        var tabs = Theme.Segmented(labels, selected, select, 28);
        return new Border { Background = Theme.Surface3, CornerRadius = new CornerRadius(16), Padding = new Thickness(2), Margin = new Thickness(0, 0, 0, 12), Child = tabs };
    }

    // ---------- When ----------

    private void ShowWhen()
    {
        var stack = new StackPanel();
        string[] kinds = [AutomationTriggers.Time, AutomationTriggers.App, AutomationTriggers.Displays];
        stack.Children.Add(Tabs(["Time of day", "App in front", "Monitors"], Math.Max(0, Array.IndexOf(kinds, draft.Trigger)), index => { ReadWhen(); draft.Trigger = kinds[index]; ShowWhen(); }));
        switch (draft.Trigger)
        {
            case AutomationTriggers.Time: BuildTime(stack); break;
            case AutomationTriggers.App: BuildApp(stack); break;
            default: BuildDisplays(stack); break;
        }
        whenHost.Child = stack;
        whenHost.Margin = new Thickness(0, 0, 0, 8);
    }

    private void BuildTime(StackPanel stack)
    {
        stack.Children.Add(Caption("On these days (none picked means every day)"));
        var days = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var (text, day) in WeekDays)
        {
            void Toggle() { if (!draft.Days.Remove(day)) draft.Days.Add(day); ReadWhen(); ShowWhen(); }
            days.Children.Add(Pill(text, draft.Days.Contains(day), Toggle, 54));
        }
        stack.Children.Add(days);
        var times = new Grid();
        times.ColumnDefinitions.Add(new ColumnDefinition());
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        times.ColumnDefinitions.Add(new ColumnDefinition());
        var startPanel = new StackPanel();
        var endPanel = new StackPanel();
        startBox = ItemEditor.Field(startPanel, "From (e.g. 09:00 or 9 am)", draft.Start);
        endBox = ItemEditor.Field(endPanel, "Until (e.g. 17:00 or 5 pm)", draft.End);
        Grid.SetColumn(endPanel, 2);
        times.Children.Add(startPanel);
        times.Children.Add(endPanel);
        stack.Children.Add(times);
        stack.Children.Add(PanelKit.Hint("Until earlier than From runs overnight (22:00 to 06:00 is Friday night and Saturday morning for a Friday rule). The same time in both means the whole day."));
    }

    private void BuildApp(StackPanel stack)
    {
        processBox = ItemEditor.Field(stack, "App (its process name, e.g. Code.exe)", draft.Process);
        Button? pick = null;
        pick = PanelKit.Outline("Pick a running app…", () => OpenRunningApps(pick!), "Choose from the apps that have a window open right now.");
        pick.HorizontalAlignment = HorizontalAlignment.Left;
        pick.Margin = new Thickness(0, -4, 0, 8);
        stack.Children.Add(pick);
        stack.Children.Add(PanelKit.Hint("Applies while a window of this app is in front. Desktop Deck's own windows, the taskbar and the Alt+Tab switcher never change it."));
    }

    private void OpenRunningApps(Button anchor)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = 4 };
        var list = new StackPanel();
        var apps = ForegroundWatcher.RunningApps();
        if (apps.Count == 0) list.Children.Add(Theme.Label("No open apps found.", 12, Theme.Muted));
        foreach (var app in apps)
        {
            var item = new Border { Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(7), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = Theme.Label(app, 12.5) };
            item.MouseEnter += (_, _) => item.Background = PanelKit.Hover;
            item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;
            string name = app;
            item.MouseLeftButtonUp += (_, e) => { popup.IsOpen = false; if (processBox is not null) processBox.Text = name; e.Handled = true; };
            list.Children.Add(item);
        }
        popup.Child = new Border
        {
            Margin = new Thickness(8, 4, 16, 18), Padding = new Thickness(6), CornerRadius = new CornerRadius(12), Background = Theme.Surface, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1),
            Width = 240, MaxHeight = 280, Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list }, Effect = PanelKit.Shadow(20, 6, 0.5),
        };
        popup.IsOpen = true;
    }

    private void BuildDisplays(StackPanel stack)
    {
        stack.Children.Add(Tabs(["Number of displays", "These displays"], exactSet ? 1 : 0, index => { ReadWhen(); exactSet = index == 1; if (exactSet && draft.DisplayIds.Count == 0) draft.DisplayIds = displays.Select(d => d.Id).ToList(); ShowWhen(); }));
        if (!exactSet)
        {
            countBox = ItemEditor.Field(stack, "Exactly this many displays are connected", draft.DisplayCount.ToString());
            countBox.Width = 120;
            countBox.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(PanelKit.Hint($"{displays.Count} {(displays.Count == 1 ? "display is" : "displays are")} connected now. For example 1 for laptop only, 2 or more for docked."));
            return;
        }
        stack.Children.Add(Caption("Applies when exactly these displays are connected"));
        var wrap = new WrapPanel();
        var known = displays.Select(d => d.Id).ToList();
        foreach (var display in displays)
        {
            string id = display.Id;
            string text = $"Display {display.Number}  {display.Bounds.Width}×{display.Bounds.Height}{(display.IsPrimary ? "  main" : "")}";
            wrap.Children.Add(Pill(text, draft.DisplayIds.Contains(id, StringComparer.OrdinalIgnoreCase), () => Toggle(id)));
        }
        // A display from an earlier setup that is not plugged in now stays in the rule until it is switched off here.
        foreach (var gone in draft.DisplayIds.Where(id => !known.Contains(id, StringComparer.OrdinalIgnoreCase)))
        {
            string id = gone;
            wrap.Children.Add(Pill("Not connected: " + AutomationText.DisplayLabel(id), true, () => Toggle(id)));
        }
        stack.Children.Add(wrap);
        var current = PanelKit.Outline("Use the current setup", () => { draft.DisplayIds = displays.Select(d => d.Id).ToList(); ShowWhen(); }, "Pick every display that is connected right now.");
        current.HorizontalAlignment = HorizontalAlignment.Left;
        current.Margin = new Thickness(0, 0, 0, 8);
        stack.Children.Add(current);
        stack.Children.Add(PanelKit.Hint("Docked and laptop-only setups are told apart by which monitors are plugged in. Plug in your dock, then press Use the current setup."));
    }

    private void Toggle(string id)
    {
        int at = draft.DisplayIds.FindIndex(i => string.Equals(i, id, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) draft.DisplayIds.RemoveAt(at); else draft.DisplayIds.Add(id);
        ShowWhen();
    }

    /// <summary>Copies what is typed in the visible fields into the draft (before the section is redrawn).</summary>
    private void ReadWhen()
    {
        if (startBox is not null && draft.Trigger == AutomationTriggers.Time) { draft.Start = startBox.Text; draft.End = endBox?.Text ?? draft.End; }
        if (processBox is not null && draft.Trigger == AutomationTriggers.App) draft.Process = processBox.Text;
        if (countBox is not null && draft.Trigger == AutomationTriggers.Displays && int.TryParse(countBox.Text.Trim(), out int count)) draft.DisplayCount = count;
        startBox = endBox = processBox = countBox = null;
    }

    // ---------- Then ----------

    private void ShowThen()
    {
        var stack = new StackPanel();
        stack.Children.Add(Tabs(["Pinned layout", "Desktop layout"], draft.Layer == LayoutLayers.Floating ? 0 : 1, index =>
        {
            string layer = index == 0 ? LayoutLayers.Floating : LayoutLayers.Background;
            if (layer == draft.Layer) return;
            draft.Layer = layer;
            draft.LayoutId = "";
            ShowThen();
        }));
        var wrap = new WrapPanel();
        foreach (var layout in LayoutEngine.Of(settings, draft.Layer))
        {
            string id = layout.Id;
            wrap.Children.Add(Pill((layout.Icon.Length > 0 ? layout.Icon + "  " : "") + layout.Name, draft.LayoutId == id, () => { draft.LayoutId = id; ShowThen(); }));
        }
        stack.Children.Add(wrap);
        thenHost.Child = stack;
        thenHost.Margin = new Thickness(0, 0, 0, 10);
    }

    // ---------- Save ----------

    private void Save()
    {
        string countText = countBox?.Text.Trim() ?? "";
        bool countTyped = countBox is not null && draft.Trigger == AutomationTriggers.Displays && !exactSet;
        ReadWhen();
        string? problem = null;
        if (draft.Trigger == AutomationTriggers.Time)
        {
            if (Planner.ParseTime(draft.Start) is not { } start) problem = "The start time isn't a time. Try 09:00 or 9 am.";
            else if (Planner.ParseTime(draft.End) is not { } end) problem = "The end time isn't a time. Try 17:00 or 5 pm.";
            else { draft.Start = start; draft.End = end; }
        }
        else if (draft.Trigger == AutomationTriggers.App)
        {
            draft.Process = draft.Process.Trim();
            if (draft.Process.Length == 0) problem = "Type the app's process name, or pick a running app.";
        }
        else if (exactSet)
        {
            if (draft.DisplayIds.Count == 0) problem = "Pick at least one display.";
        }
        else
        {
            draft.DisplayIds = new();
            if (countTyped && !(int.TryParse(countText, out int typed) && typed is >= 1 and <= 16)) problem = "Type a number of displays from 1 to 16.";
        }
        if (problem is null && draft.LayoutId.Length == 0) problem = "Choose the layout to switch to.";
        if (problem is not null) { validation.Text = problem; ShowWhen(); return; }
        if (draft.Trigger == AutomationTriggers.Displays && !exactSet) draft.DisplayIds = new();
        Result = draft;
        DialogResult = true;
    }
}
