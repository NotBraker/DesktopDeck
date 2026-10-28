using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;
using DesktopDeck.Services;
using DesktopDeck.Widgets;
using Microsoft.Win32;

namespace DesktopDeck;

/// <summary>Edits one deck element on a copy; <see cref="Result"/> is set only when the user saves.</summary>
internal sealed partial class ItemEditor : Window
{
    internal const string PictureFilter = "Pictures|*.png;*.jpg;*.jpeg;*.jfif;*.bmp;*.gif;*.ico;*.tif;*.tiff;*.webp|All files (*.*)|*.*";
    private readonly LauncherItem draft;
    private readonly TextBox nameBox;
    private readonly TextBox? targetBox, argumentsBox, directoryBox, iconBox, imageBox, stepBox, valueBox, workBox, breakBox, longBox, everyBox, goalBox, startHourBox, endHourBox;
    private readonly TextBox? noteBox, dateBox, timeBox, folderBox, cityBox;
    private WeatherService.Place? pickedPlace;
    /// <summary>Plug-in widget settings: how to read each field back (null = invalid; the reader already showed why).</summary>
    private readonly List<(OptionSpec Spec, Func<string?> Read)> pluginFields = new();
    private readonly TextBox accentBox;
    private readonly TextBlock validation;
    internal LauncherItem? Result { get; private set; }
    private readonly string originalLocation, originalUnits;
    /// <summary>The deck-wide card defaults, so the Appearance fields can say what "empty" means.</summary>
    private readonly AppearanceSettings? deckAppearance;

    /// <param name="deck">The deck's settings, for the list of Google calendars a Schedule can pick from.</param>
    internal ItemEditor(Window owner, LauncherItem item, bool isNew = false, DeckSettings? deck = null, Func<CancellationToken, Task<List<GoogleTaskListChoice>>>? loadTaskLists = null, Action<LauncherItem>? preview = null)
    {
        draft = item.Copy();
        deckAppearance = deck?.Appearance;
        deckSettings = deck;
        originalLocation = item.Location;
        originalUnits = item.Units;
        Owner = owner;
        string noun = item.Kind switch
        {
            ItemKinds.Image => "picture",
            ItemKinds.Tasks => "task list",
            ItemKinds.Habits => "habit tracker",
            ItemKinds.Counter => "counter",
            ItemKinds.Clock => "time",
            ItemKinds.Timer => "timer",
            ItemKinds.Schedule => "schedule",
            ItemKinds.Goals => "weekly goals",
            ItemKinds.Note => "sticky note",
            ItemKinds.Countdown => "countdown",
            ItemKinds.Media => "media player",
            ItemKinds.Folder => "folder",
            ItemKinds.Weather => "weather",
            _ when WidgetRegistry.Find(item.Kind) is { } plugin => plugin.Name,
            _ => "launcher",
        };
        Title = (isNew ? "Add " : "Edit ") + noun;
        Width = 540;
        // As tall as its content (the groups are closed until opened), up to the screen; the rest scrolls.
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        Foreground = Theme.Text;
        FontFamily = Theme.UiFont;
        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(Theme.Label(Title, 25));
        var intro = Theme.Label(item.Kind switch
        {
            ItemKinds.Image => "A picture for your desktop. Desktop Deck keeps its own copy.",
            ItemKinds.Tasks => "A checklist you can tick off right on the desktop.",
            ItemKinds.Habits => "Daily habits with a seven-day view and streaks.",
            ItemKinds.Counter => "A number you can bump up or down with one click.",
            ItemKinds.Clock => "The current time and date.",
            ItemKinds.Timer => "A pomodoro timer: focus, then take a break. You'll get a notification when a session ends.",
            ItemKinds.Schedule => "Your week at a glance. Add blocks with + or by double-clicking a time.",
            ItemKinds.Goals => "A few goals for the week, with optional steps and progress.",
            ItemKinds.Note => "A sticky note on your desktop. Click it any time to edit the text.",
            ItemKinds.Countdown => "Counts the days until a date — a trip, a birthday, a deadline.",
            ItemKinds.Media => "Shows what's playing in Spotify, your browser or any media app, with play/pause and skip.",
            ItemKinds.Folder => "The newest files in a folder, newest first. Click one to open it.",
            ItemKinds.Weather => "Current weather and the week ahead, from Open-Meteo (free, no account). Only your city's coordinates are sent.",
            _ when WidgetRegistry.Find(item.Kind) is { } plugin => plugin.Description,
            _ => "A shortcut to something on your computer.",
        }, 13, Theme.Muted);
        intro.TextWrapping = TextWrapping.Wrap;
        intro.Margin = new Thickness(0, 6, 0, 20);
        root.Children.Add(intro);

        // "More options" (closed): settings most people never change. Built here and added below only when something ended up in it.
        var moreSection = new Disclosure("More options");
        moreSection.Toggled += KeepOnScreen;
        var more = moreSection.Body;
        nameBox = Field(root, item.Kind switch { ItemKinds.Image => "Caption (optional)", ItemKinds.Launcher => "Name", ItemKinds.Countdown => "What are you counting down to?", ItemKinds.Note => "Title (shown only when the title is on)", _ => "Title" }, item.Name);
        switch (item.Kind)
        {
            case ItemKinds.Launcher:
                targetBox = Field(root, "File or application", item.Target);
                root.Children.Add(Row(ActionButton("Browse…", BrowseTarget)));
                argumentsBox = Field(more, "Arguments (optional)", item.Arguments);
                directoryBox = Field(more, "Working folder (optional)", item.WorkingDirectory);
                iconBox = Field(more, "Custom icon (empty = the app's own icon)", item.IconPath);
                more.Children.Add(Row(ActionButton("Use app icon", () => iconBox.Text = ""), ActionButton("Choose image…", () => Pick(iconBox, "Choose an icon image"))));
                break;
            case ItemKinds.Image:
                imageBox = Field(root, "Picture", item.ImagePath);
                root.Children.Add(Row(ActionButton("Choose picture…", () => Pick(imageBox, "Choose a picture"))));
                var fitHeading = Theme.Label("Framing", 12, Theme.Muted);
                fitHeading.Margin = new Thickness(0, 0, 0, 5);
                root.Children.Add(fitHeading);
                var fill = Theme.Button("Fill the frame");
                var fit = Theme.Button("Show whole picture");
                void ShowFit() { (draft.ImageFit == "fit" ? fit : fill).BorderBrush = Theme.Primary; (draft.ImageFit == "fit" ? fill : fit).BorderBrush = Theme.Line; }
                fill.Click += (_, _) => { draft.ImageFit = "fill"; ShowFit(); };
                fit.Click += (_, _) => { draft.ImageFit = "fit"; ShowFit(); };
                ShowFit();
                var fitRow = Row(fill, fit);
                fitRow.HorizontalAlignment = HorizontalAlignment.Left;
                fitRow.Margin = new Thickness(0, 0, 0, 12);
                root.Children.Add(fitRow);
                break;
            case ItemKinds.Counter:
                valueBox = Field(root, "Current value", item.Count.ToString());
                stepBox = Field(root, "Step (how much each click adds or removes)", item.Step.ToString());
                goalBox = Field(root, "Goal (optional)", item.CountGoal > 0 ? item.CountGoal.ToString() : "");
                AddHint(root, "Shows \"5 / 8\", and the number takes your accent color when you get there.");
                break;
            case ItemKinds.Timer:
                workBox = Field(root, "Focus minutes", item.Pomodoro.WorkMinutes.ToString());
                breakBox = Field(root, "Short break minutes", item.Pomodoro.BreakMinutes.ToString());
                longBox = Field(more, "Long break minutes", item.Pomodoro.LongBreakMinutes.ToString());
                everyBox = Field(more, "Long break after this many focus sessions", item.Pomodoro.LongBreakEvery.ToString());
                root.Children.Add(SwitchRow("Play a sound when a session ends", draft.TimerSound, v => draft.TimerSound = v));
                root.Children.Add(SwitchRow("Start the next session automatically", draft.AutoNext, v => draft.AutoNext = v));
                break;
            case ItemKinds.Schedule:
                startHourBox = Field(more, "First hour shown (0–23, empty = fit your blocks)", item.StartHour >= 0 ? item.StartHour.ToString() : "");
                endHourBox = Field(more, "Last hour shown (1–24, empty = fit your blocks)", item.EndHour >= 0 ? item.EndHour.ToString() : "");
                root.Children.Add(SwitchRow("Weekdays only (hide Saturday and Sunday)", draft.HideWeekends, v => draft.HideWeekends = v));
                AddCalendarOptions(root, more, deck);
                break;
            case ItemKinds.Clock:
                root.Children.Add(SwitchRow("Show seconds", draft.ShowSeconds, v => draft.ShowSeconds = v));
                root.Children.Add(SwitchRow("24-hour time", draft.Use24Hour, v => draft.Use24Hour = v));
                root.Children.Add(SwitchRow("Show the date", draft.ShowDate, v => draft.ShowDate = v));
                break;
            case ItemKinds.Tasks:
                root.Children.Add(SwitchRow("Start fresh each day (finished tasks clear at midnight)", draft.DailyReset, v => draft.DailyReset = v));
                root.Children.Add(SwitchRow("Hide finished tasks", draft.HideCompleted, v => draft.HideCompleted = v));
                AddTasksSyncOptions(root, deck, loadTaskLists);
                break;
            case ItemKinds.Note:
                noteBox = Field(root, "Note", item.Text);
                noteBox.AcceptsReturn = true;
                noteBox.TextWrapping = TextWrapping.Wrap;
                noteBox.Height = 150;
                noteBox.VerticalContentAlignment = VerticalAlignment.Top;
                noteBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                break;
            case ItemKinds.Countdown:
                dateBox = Field(root, "Date (yyyy-mm-dd)", item.TargetDate);
                timeBox = Field(root, "Time (optional, e.g. 6:30 pm)", string.IsNullOrWhiteSpace(item.TargetTime) ? "" : Planner.Format(item.TargetTime, false));
                root.Children.Add(SwitchRow("Repeats every year (birthdays, anniversaries)", draft.RepeatYearly, v => draft.RepeatYearly = v));
                break;
            case ItemKinds.Folder:
                folderBox = Field(root, "Folder", string.IsNullOrWhiteSpace(item.Target) ? FolderListing.Downloads : item.Target);
                root.Children.Add(Row(ActionButton("Downloads", () => folderBox.Text = FolderListing.Downloads), ActionButton("Desktop", () => folderBox.Text = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)), ActionButton("Choose…", () => PickFolder(folderBox))));
                break;
            case ItemKinds.Weather:
                cityBox = Field(root, "City", item.Location);
                var results = new StackPanel { Margin = new Thickness(0, -4, 0, 10) };
                var find = ActionButton("Find", () => _ = FindPlaces(results));
                root.Children.Add(Row(find));
                root.Children.Add(results);
                var unitsHeading = Theme.Label("Units", 12, Theme.Muted);
                unitsHeading.Margin = new Thickness(0, 0, 0, 6);
                root.Children.Add(unitsHeading);
                if (draft.Units is not ("F" or "C")) draft.Units = WeatherService.DefaultUnits;
                Grid? units = null;
                void ShowUnits()
                {
                    if (units is not null) root.Children.Remove(units);
                    units = Theme.Segmented(["°F, mph", "°C, km/h"], draft.Units == "C" ? 1 : 0, i => { draft.Units = i == 1 ? "C" : "F"; ShowUnits(); }, 30);
                    units.Width = 240;
                    units.HorizontalAlignment = HorizontalAlignment.Left;
                    units.Margin = new Thickness(0, 0, 0, 14);
                    root.Children.Insert(root.Children.IndexOf(unitsHeading) + 1, units);
                }
                ShowUnits();
                break;
        }

        // The widget's own settings: the main ones stay here, "Behavior" and the rarely changed ones go to More options, "Layout" and "Colors" to Look.
        var widgetLook = new StackPanel();
        if (WidgetRegistry.Find(item.Kind) is { } widget) AddWidgetOptions(widget, root, more, widgetLook);
        if (draft.OnTop)
        {
            more.Children.Add(SwitchRow("Fade when it isn't being used (pinned)", draft.FloatIdleFade, v => draft.FloatIdleFade = v));
            more.Children.Add(SwitchRow("Let clicks pass through (pinned; hold Ctrl to use it)", draft.FloatClickThrough, v => draft.FloatClickThrough = v));
        }
        if (more.Children.Count > 0) root.Children.Add(moreSection);

        // Look (closed): card style, title, accent, corners and opacity; then the widget's own layout and colors; then a closed "More look options".
        // While the deck look applies to every widget, the card controls give way to a note and a switch ("Give this widget its own look");
        // they stay in the window, so what was saved there is kept.
        var look = Disclosure.Add(root, "Look");
        look.Toggled += KeepOnScreen;
        var ownCard = new StackPanel();
        var ownShape = new StackPanel();
        var ownMore = new StackPanel();
        AddDeckLookNotice(look.Body, ownCard, ownShape, ownMore);
        look.Body.Children.Add(ownCard);
        if (item.Kind != ItemKinds.Image)
        {
            var styleHeading = Theme.Label("Card style", 12, Theme.Muted);
            styleHeading.Margin = new Thickness(0, 4, 0, 6);
            ownCard.Children.Add(styleHeading);
            var (styleButtons, showCardStyle) = LookPanel.StyleButtons(draft.CardStyle, style => draft.CardStyle = style);
            showStyle = () => showCardStyle(draft.CardStyle);
            ownCard.Children.Add(styleButtons);
        }
        if (item.Kind is not (ItemKinds.Image or ItemKinds.Launcher or ItemKinds.Clock))
            look.Body.Children.Add(SwitchRow("Show the title", draft.ShowHeader, v => draft.ShowHeader = v));

        accentBox = Field(look.Body, "Accent color", string.IsNullOrWhiteSpace(item.Accent) ? Theme.PrimaryHex : item.Accent);
        accentBox.ToolTip = "Pick a swatch or type a color such as #C4684F.";
        look.Body.Children.Add(SwatchRow(accentBox));

        // The shared appearance settings for every kind. Empty = the deck-wide default (shown faintly in the box).
        var shared = WidgetRegistry.Common.Where(spec => spec.Key switch
        {
            "_padding" => item.Kind is not (ItemKinds.Launcher or ItemKinds.Image or ItemKinds.Clock or ItemKinds.Countdown or ItemKinds.Media),
            "_textScale" or "_textColor" => item.Kind != ItemKinds.Image,
            _ => true,
        }).ToList();
        look.Body.Children.Add(ownShape);
        AddOptionGroup(ownShape, "", shared.Where(spec => spec.Key is "_radius" or "_opacity"));
        look.Body.Children.Add(widgetLook);
        look.Body.Children.Add(ownMore);
        var moreLook = Disclosure.Add(ownMore, "More look options", nested: true);
        moreLook.Toggled += KeepOnScreen;
        AddOptionGroup(moreLook.Body, "", shared.Where(spec => spec.Key is not ("_radius" or "_opacity")));
        AddPresetRow(ownMore);
        void ShowLookSummary()
        {
            bool deckLook = deckAppearance is { ApplyToAll: true } && !draft.OwnLook;
            look.Summary = deckLook ? "Uses the deck look" : item.Kind == ItemKinds.Image ? "" : LookPanel.Styles.FirstOrDefault(s => s.Style == draft.CardStyle).Label ?? "";
        }
        look.Body.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => ShowLookSummary()));
        look.Body.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => ShowLookSummary()), true);
        ShowLookSummary();

        validation = Theme.Label("", 12, Theme.Danger);
        validation.TextWrapping = TextWrapping.Wrap;
        validation.MinHeight = 28;
        root.Children.Add(validation);
        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => DialogResult = false;
        var save = Theme.Button(isNew ? "Add" : "Save changes", true);
        save.IsDefault = true;
        save.Click += (_, _) => Save();
        root.Children.Add(Row(cancel, save));
        Loaded += (_, _) => { nameBox.Focus(); nameBox.SelectAll(); };
        if (preview is not null) StartPreview(root, preview);
    }

    /// <summary>A plug-in widget's settings, sorted by <see cref="OptionTiers"/>. Every setting gets a field whichever part it lands in, so Save reads them all.</summary>
    private void AddWidgetOptions(IWidget widget, Panel main, Panel more, Panel look)
    {
        var options = widget.Options;
        OptionTier Tier(OptionSpec o) => OptionTiers.Of(widget.Kind, o.Key, o.Group);
        foreach (var group in options.Where(o => Tier(o) == OptionTier.Main).GroupBy(o => o.Group)) AddOptionGroup(main, group.Key, group);
        // More options: loose settings first (no heading of their own), then whole groups that keep their heading.
        AddOptionGroup(more, "", options.Where(o => Tier(o) == OptionTier.More && !OptionTiers.IsAdvancedGroup(widget.Kind, o.Group)));
        foreach (var group in options.Where(o => OptionTiers.IsAdvancedGroup(widget.Kind, o.Group)).GroupBy(o => o.Group)) AddOptionGroup(more, group.Key, group);
        foreach (var group in options.Where(o => Tier(o) == OptionTier.Look).GroupBy(o => o.Group)) AddOptionGroup(look, group.Key, group);
    }

    private static void AddHint(Panel parent, string text)
    {
        var hint = Theme.Label(text, 11.5, Theme.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, -6, 0, 12);
        parent.Children.Add(hint);
    }

    /// <summary>The window is as tall as its content, so opening a group can push the bottom off the screen: move it up when that happens.</summary>
    private void KeepOnScreen()
    {
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            var area = SystemParameters.WorkArea;
            if (Top + ActualHeight > area.Bottom - 8) Top = Math.Max(area.Top + 8, area.Bottom - 8 - ActualHeight);
        }));
    }

    /// <summary>Faint text inside an empty box (such as the deck default it falls back to). The box is wrapped in a grid, in the same place.</summary>
    private static void AddPlaceholder(Panel parent, TextBox box, string text)
    {
        int at = parent.Children.IndexOf(box);
        if (at < 0) return;
        var host = new Grid { Margin = box.Margin };
        box.Margin = new Thickness(0);
        var faint = Theme.Label(text, 13, Theme.Muted);
        faint.Opacity = 0.85;
        faint.IsHitTestVisible = false;
        faint.Margin = new Thickness(12, 0, 12, 0);
        faint.TextTrimming = TextTrimming.CharacterEllipsis;
        void Sync() => faint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => Sync();
        Sync();
        parent.Children.RemoveAt(at);
        host.Children.Add(box);
        host.Children.Add(faint);
        parent.Children.Insert(at, host);
    }

    internal static TextBox Field(Panel parent, string label, string? value)
    {
        var heading = Theme.Label(label, 12, Theme.Muted);
        heading.Margin = new Thickness(0, 0, 0, 5);
        heading.TextWrapping = TextWrapping.Wrap;
        parent.Children.Add(heading);
        var box = Theme.TextField(value);
        box.Margin = new Thickness(0, 0, 0, 12);
        parent.Children.Add(box);
        return box;
    }

    internal static Grid SwitchRow(string label, bool value, Action<bool> changed)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 14) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Theme.Label(label, 13, Theme.Text);
        text.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(text);
        var toggle = Theme.Switch(value, changed);
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    internal static WrapPanel SwatchRow(TextBox target)
    {
        var swatches = new WrapPanel { Margin = new Thickness(0, -4, 0, 10) };
        foreach (var color in Theme.Swatches)
        {
            var swatch = new Border
            {
                Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 7, 6),
                Background = Theme.Accent(color), Cursor = Cursors.Hand, ToolTip = color,
            };
            swatch.MouseLeftButtonUp += (_, _) => target.Text = color;
            swatches.Children.Add(swatch);
        }
        return swatches;
    }

    internal static StackPanel Row(params Button[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, -4, 0, 10) };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (i < buttons.Length - 1) buttons[i].Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(buttons[i]);
        }
        return row;
    }

    private static Button ActionButton(string text, Action action)
    {
        var button = Theme.Button(text);
        button.Click += (_, _) => action();
        return button;
    }

    private void BrowseTarget()
    {
        var picker = new OpenFileDialog { Title = "Choose a file or application", CheckFileExists = true, Filter = "All files (*.*)|*.*" };
        if (picker.ShowDialog(this) != true) return;
        targetBox!.Text = picker.FileName;
        if (string.IsNullOrWhiteSpace(nameBox.Text)) nameBox.Text = Path.GetFileNameWithoutExtension(picker.FileName);
        if (string.IsNullOrWhiteSpace(directoryBox!.Text)) directoryBox.Text = Path.GetDirectoryName(picker.FileName) ?? "";
    }

    private string DeckDefaultColor(string key) => key switch
    {
        "_cardColor" => deckAppearance?.CardColor ?? "",
        "_textColor" => deckAppearance?.TextColor ?? "",
        "_borderColor" => deckAppearance?.BorderColor ?? "",
        _ => "",
    };

    /// <summary>Editor fields for a widget's settings, under an optional sub-heading.</summary>
    private void AddOptionGroup(Panel root, string heading, IEnumerable<OptionSpec> specs)
    {
        if (heading.Length > 0)
        {
            var title = Theme.WidgetTitle(heading, Theme.Primary);
            title.Margin = new Thickness(0, 10, 0, 10);
            root.Children.Add(title);
        }
        foreach (var spec in specs) AddOptionField(root, spec);
    }

    private void AddOptionField(Panel root, OptionSpec spec)
    {
        string current = draft.Options.TryGetValue(spec.Key, out var saved) ? saved : spec.Default;
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        switch (spec.Type)
        {
            case OptionType.Toggle:
            {
                bool value = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase);
                root.Children.Add(SwitchRow(spec.Label, value, v => value = v));
                pluginFields.Add((spec, () => value ? "true" : "false"));
                break;
            }
            case OptionType.Number:
            {
                bool bounded = spec.Min > double.MinValue / 2 && spec.Max < double.MaxValue / 2;
                // Shared appearance numbers may stay empty, which means the deck-wide default.
                bool shared = spec.Key.StartsWith('_');
                if (shared && !draft.Options.ContainsKey(spec.Key)) current = "";
                string range = bounded ? $"{spec.Min.ToString(culture)}–{spec.Max.ToString(culture)}" : "";
                // The shared appearance numbers stay plain: the range is in the tooltip and an empty box shows the deck default faintly.
                var box = Field(root, shared || range.Length == 0 ? spec.Label : $"{spec.Label} ({range})", current);
                if (shared)
                {
                    lookBoxes[spec.Key] = box;
                    double deckDefault = spec.Key switch
                    {
                        "_radius" => deckAppearance?.CornerRadius ?? AppearanceSettings.DefaultRadius,
                        "_opacity" => deckAppearance?.CardOpacity ?? 100,
                        "_textScale" => deckAppearance?.TextScale ?? 100,
                        "_padding" => deckAppearance?.Padding ?? 12,
                        _ => double.TryParse(spec.Default, System.Globalization.NumberStyles.Float, culture, out var d) ? d : 0,
                    };
                    box.ToolTip = (range.Length > 0 ? range + ". " : "") + "Leave empty to follow the deck.";
                    AddPlaceholder(root, box, deckDefault.ToString("0.##", culture));
                }
                pluginFields.Add((spec, () =>
                {
                    string text = box.Text.Trim().Replace(',', '.');
                    if (shared && text.Length == 0) return "";
                    if (double.TryParse(text, System.Globalization.NumberStyles.Float, culture, out var number) && double.IsFinite(number) && number >= spec.Min && number <= spec.Max)
                        return number.ToString(culture);
                    Fail(bounded ? $"{spec.Label}: enter a number from {spec.Min.ToString(culture)} to {spec.Max.ToString(culture)}." : $"{spec.Label}: enter a number.", box);
                    return null;
                }));
                break;
            }
            case OptionType.Choice:
            {
                var heading = Theme.Label(spec.Label, 12, Theme.Muted);
                heading.Margin = new Thickness(0, 0, 0, 6);
                root.Children.Add(heading);
                string value = spec.Choices.Contains(current) ? current : spec.Default;
                var choices = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
                var buttons = new List<(string Value, Button Button)>();
                void Show() { foreach (var (v, b) in buttons) { bool on = v == value; b.BorderBrush = on ? Theme.Primary : Theme.LineStrong; b.Background = on ? Theme.Tint(Theme.PrimaryHex, 0.18) : Theme.Surface; } }
                for (int i = 0; i < spec.Choices.Length; i++)
                {
                    string choice = spec.Choices[i];
                    var b = Theme.Button(spec.ChoiceLabels is { } labels && i < labels.Length ? labels[i] : choice);
                    b.Margin = new Thickness(0, 0, 6, 6);
                    b.Click += (_, _) => { value = choice; Show(); };
                    buttons.Add((choice, b));
                    choices.Children.Add(b);
                }
                Show();
                root.Children.Add(choices);
                pluginFields.Add((spec, () => value));
                break;
            }
            case OptionType.Color:
            {
                var box = Field(root, spec.Label, current);
                if (spec.Key.StartsWith('_')) lookBoxes[spec.Key] = box;
                root.Children.Add(SwatchRow(box));
                if (spec.Key.StartsWith('_'))
                {
                    // Empty follows the deck (or the theme): the box says which color that is, so no explanation line is needed.
                    box.ToolTip = "Pick a swatch or type a color such as #C4684F. Leave empty to follow the deck.";
                    AddPlaceholder(root, box, DeckDefaultColor(spec.Key) is { Length: > 0 } inherited ? inherited : "Theme color");
                    spec = spec with { Hint = "" };
                }
                pluginFields.Add((spec, () =>
                {
                    string text = box.Text.Trim();
                    if (text.Length == 0) return "";
                    try { Theme.Brush(text); return text; }
                    catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { Fail($"{spec.Label}: enter a color like #C4684F, or leave it empty.", box); return null; }
                }));
                break;
            }
            case OptionType.Date:
            {
                var box = Field(root, spec.Label + " (yyyy-mm-dd)", current);
                pluginFields.Add((spec, () =>
                {
                    string text = box.Text.Trim();
                    if (text.Length == 0 || DateOnly.TryParseExact(text, "yyyy-MM-dd", out _)) return text;
                    Fail($"{spec.Label}: write the date as yyyy-mm-dd, or leave it empty.", box);
                    return null;
                }));
                break;
            }
            case OptionType.Time:
            {
                var box = Field(root, spec.Label + " (e.g. 6:30 pm)", current.Length > 0 && Planner.ParseTime(current) is not null ? Planner.Format(current, false) : current);
                pluginFields.Add((spec, () =>
                {
                    string text = box.Text.Trim();
                    if (text.Length == 0) return "";
                    if (Planner.ParseTime(text) is { } time) return time;
                    Fail($"{spec.Label}: enter a time like 6:30 pm or 18:30.", box);
                    return null;
                }));
                break;
            }
            case OptionType.Folder:
            case OptionType.File:
            {
                var box = Field(root, spec.Label, current);
                root.Children.Add(Row(spec.Type == OptionType.Folder
                    ? ActionButton("Choose folder…", () => PickFolder(box))
                    : ActionButton("Choose file…", () => { var picker = new OpenFileDialog { Title = spec.Label, CheckFileExists = true }; if (picker.ShowDialog(this) == true) box.Text = picker.FileName; })));
                pluginFields.Add((spec, () => box.Text.Trim().Trim('"')));
                break;
            }
            case OptionType.LongText:
            case OptionType.Lines:
            {
                var box = Field(root, spec.Label + (spec.Type == OptionType.Lines ? " (one per line)" : ""), current);
                box.AcceptsReturn = true;
                box.TextWrapping = spec.Type == OptionType.Lines ? TextWrapping.NoWrap : TextWrapping.Wrap;
                box.Height = 110;
                box.VerticalContentAlignment = VerticalAlignment.Top;
                box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                box.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
                pluginFields.Add((spec, () => box.Text.Replace("\r\n", "\n").TrimEnd()));
                break;
            }
            default:
            {
                var box = Field(root, spec.Label, current);
                pluginFields.Add((spec, () => box.Text.Trim()));
                break;
            }
        }
        if (spec.Hint.Length > 0)
        {
            var hint = Theme.Label(spec.Hint, 11.5, Theme.Muted);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, -6, 0, 12);
            root.Children.Add(hint);
        }
    }

    /// <summary>Schedule: which Google and iCal events to show, and how they look.</summary>
    /// <summary>"Sync with Google Tasks" and the list picker. The lists come from the settings (filled the last time they were loaded); Reload asks Google.</summary>
    private void AddTasksSyncOptions(Panel root, DeckSettings? deck, Func<CancellationToken, Task<List<GoogleTaskListChoice>>>? load)
    {
        var title = Theme.WidgetTitle("Google Tasks", Theme.Primary);
        title.Margin = new Thickness(0, 10, 0, 6);
        root.Children.Add(title);
        var hint = Theme.Label("Keeps this list in step with a Google Tasks list, both ways. Changes on Google show up here within a couple of minutes. Tasks already here are added to the list you pick.", 11.5, Theme.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(hint);
        var host = new StackPanel();
        var lists = deck?.GoogleTaskLists.Select(l => new GoogleTaskListChoice { Id = l.Id, Title = l.Title }).ToList() ?? new List<GoogleTaskListChoice>();
        var status = Theme.Label("", 11.5, Theme.Muted);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 0, 0, 10);
        bool loading = false;

        void Show()
        {
            host.Children.Clear();
            if (!draft.GoogleTasksSync) return;
            var heading = Theme.Label("Which Google Tasks list", 12, Theme.Muted);
            heading.Margin = new Thickness(0, 0, 0, 6);
            host.Children.Add(heading);
            var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var options = new List<GoogleTaskListChoice> { new() { Id = "@default", Title = "My Tasks" } };
            options.AddRange(lists.Where(l => l.Id != "@default"));
            if (!options.Any(o => o.Id == draft.GoogleTaskListId)) options.Add(new GoogleTaskListChoice { Id = draft.GoogleTaskListId, Title = draft.GoogleTaskListName });
            foreach (var option in options)
            {
                var chosen = option;
                bool on = chosen.Id == draft.GoogleTaskListId;
                var b = Theme.Button(chosen.Id == "@default" ? "My Tasks (default)" : chosen.Title);
                b.Margin = new Thickness(0, 0, 6, 6);
                b.BorderBrush = on ? Theme.Primary : Theme.LineStrong;
                b.Background = on ? Theme.Tint(Theme.PrimaryHex, 0.18) : Theme.Surface;
                b.Click += (_, _) => { draft.GoogleTaskListId = chosen.Id; draft.GoogleTaskListName = chosen.Id == "@default" ? "My Tasks" : chosen.Title; Show(); };
                wrap.Children.Add(b);
            }
            host.Children.Add(wrap);
            if (load is not null)
            {
                var reload = Theme.Button(lists.Count == 0 ? "Load my lists" : "Reload lists");
                reload.HorizontalAlignment = HorizontalAlignment.Left;
                reload.Margin = new Thickness(0, 0, 0, 8);
                reload.IsEnabled = !loading;
                reload.Click += async (_, _) => await Reload();
                host.Children.Add(reload);
            }
            host.Children.Add(status);
        }

        async Task Reload()
        {
            if (load is null || loading) return;
            loading = true;
            status.Text = "Asking Google for your lists… (your browser opens if Tasks access has not been allowed yet)";
            status.Foreground = Theme.Text2;
            Show();
            try
            {
                lists = await load(CancellationToken.None);
                status.Text = lists.Count == 0 ? "No lists found." : $"{lists.Count} list{(lists.Count == 1 ? "" : "s")} found.";
                status.Foreground = Theme.Olive;
            }
            catch (CalendarException ex) { status.Text = ex.Message; status.Foreground = Theme.Danger; }
            finally { loading = false; if (IsLoaded) Show(); }
        }

        root.Children.Add(SwitchRow("Sync with Google Tasks", draft.GoogleTasksSync, v => { draft.GoogleTasksSync = v; Show(); if (v && lists.Count == 0 && load is not null) _ = Reload(); }));
        root.Children.Add(host);
        Show();
    }

    private void AddCalendarOptions(Panel root, Panel more, DeckSettings? deck)
    {
        var title = Theme.WidgetTitle("Google and iCal calendars", Theme.Primary);
        title.Margin = new Thickness(0, 10, 0, 6);
        root.Children.Add(title);
        var hint = Theme.Label("Connect an account or add calendar links in Settings → Connected accounts. Events from them appear next to your local blocks.", 11.5, Theme.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(hint);
        root.Children.Add(SwitchRow("Show Google Calendar events", draft.ShowGoogle, v => draft.ShowGoogle = v));

        var calendars = deck?.GoogleCalendars ?? new List<GoogleCalendarChoice>();
        if (calendars.Count > 0)
        {
            var heading = Theme.Label("Which Google calendars", 12, Theme.Muted);
            heading.Margin = new Thickness(0, 0, 0, 6);
            root.Children.Add(heading);
            var host = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var tabsHost = new StackPanel();
            bool picking = draft.GoogleCalendarIds.Count > 0;
            void ShowChips()
            {
                host.Children.Clear();
                if (!picking) return;
                var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (var calendar in calendars)
                {
                    var c = calendar;
                    wrap.Children.Add(CalendarVisuals.ColorChoice(c.Name, c.Color, draft.GoogleCalendarIds.Contains(c.Id), () =>
                    {
                        if (!draft.GoogleCalendarIds.Remove(c.Id)) draft.GoogleCalendarIds.Add(c.Id);
                        ShowChips();
                    }));
                }
                host.Children.Add(wrap);
                host.Children.Add(CalendarVisuals.Wrapped("Tick the calendars to show. With none ticked, every calendar switched on in Settings is shown.", 11.5, Theme.Muted, 0));
            }
            void ShowTabs()
            {
                tabsHost.Children.Clear();
                var tabs = Theme.Segmented(["All switched on in Settings", "Only these…"], picking ? 1 : 0, i => { picking = i == 1; if (!picking) draft.GoogleCalendarIds.Clear(); ShowTabs(); ShowChips(); }, 28);
                tabs.Width = 340;
                tabs.HorizontalAlignment = HorizontalAlignment.Left;
                tabsHost.Children.Add(tabs);
            }
            ShowTabs();
            ShowChips();
            root.Children.Add(tabsHost);
            root.Children.Add(host);
        }

        root.Children.Add(SwitchRow("Show iCal link calendars", draft.ShowIcs, v => draft.ShowIcs = v));
        var styleHeading = Theme.Label("Event style", 12, Theme.Muted);
        styleHeading.Margin = new Thickness(0, 0, 0, 6);
        more.Children.Add(styleHeading);
        var styleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var styleButtons = new List<(string Style, Button Button)>();
        void ShowStyle() { foreach (var (style, b) in styleButtons) b.BorderBrush = draft.ExternalStyle == style ? Theme.Primary : Theme.LineStrong; }
        foreach (var (style, label) in new[] { ("filled", "Filled"), ("outline", "Outline") })
        {
            var b = Theme.Button(label);
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Click += (_, _) => { draft.ExternalStyle = style; ShowStyle(); };
            styleButtons.Add((style, b));
            styleRow.Children.Add(b);
        }
        ShowStyle();
        more.Children.Add(styleRow);
        more.Children.Add(SwitchRow("All-day events strip under the day headers", draft.ShowAllDay, v => draft.ShowAllDay = v));
    }

    private void PickFolder(TextBox box)
    {
        var picker = new OpenFolderDialog { Title = "Choose a folder", InitialDirectory = Directory.Exists(box.Text.Trim()) ? box.Text.Trim() : "" };
        if (picker.ShowDialog(this) == true) box.Text = picker.FolderName;
    }

    private async Task FindPlaces(Panel results)
    {
        results.Children.Clear();
        string query = cityBox!.Text.Trim();
        if (query.Length < 2) { results.Children.Add(Theme.Label("Type a city name first.", 12, Theme.Danger)); return; }
        results.Children.Add(Theme.Label("Searching…", 12, Theme.Muted));
        try
        {
            var places = await WeatherService.FindAsync(query);
            results.Children.Clear();
            if (places.Count == 0) { results.Children.Add(Theme.Label("No places found. Try another spelling.", 12, Theme.Danger)); return; }
            foreach (var place in places)
            {
                var choice = Theme.Button(place.Label);
                choice.HorizontalAlignment = HorizontalAlignment.Left;
                choice.Margin = new Thickness(0, 0, 0, 6);
                choice.Click += (_, _) =>
                {
                    pickedPlace = place;
                    cityBox.Text = place.Label;
                    results.Children.Clear();
                    results.Children.Add(Theme.Label("✓ " + place.Label, 12, Theme.Olive));
                };
                results.Children.Add(choice);
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            results.Children.Clear();
            results.Children.Add(Theme.Label("Couldn't reach the place search. Check your connection.", 12, Theme.Danger));
        }
    }

    private void Pick(TextBox box, string title)
    {
        var picker = new OpenFileDialog { Title = title, CheckFileExists = true, Filter = PictureFilter };
        if (picker.ShowDialog(this) == true) box.Text = picker.FileName;
    }

    internal async void Save()
    {
        string Clean(TextBox? box) => box?.Text.Trim().Trim('"') ?? "";
        if (dateBox is not null && !DateOnly.TryParseExact(dateBox.Text.Trim(), "yyyy-MM-dd", out _)) { Fail("Write the date as yyyy-mm-dd, for example 2026-12-25.", dateBox); return; }
        if (timeBox is not null && timeBox.Text.Trim().Length > 0 && Planner.ParseTime(timeBox.Text) is null) { Fail("Enter a time like 6:30 pm or 18:30, or leave it empty.", timeBox); return; }
        if (folderBox is not null && !Directory.Exists(Clean(folderBox))) { Fail("That folder doesn't exist.", folderBox); return; }
        if (cityBox is not null)
        {
            string city = cityBox.Text.Trim();
            if (city.Length == 0) { draft.Location = ""; draft.Latitude = draft.Longitude = 0; }
            else if (pickedPlace is { } place && place.Label == city) { if (draft.Location != place.Label) { draft.Location = place.Label; draft.Latitude = place.Latitude; draft.Longitude = place.Longitude; } }
            else if (city != draft.Location || (draft.Latitude == 0 && draft.Longitude == 0))
            {
                validation.Text = "Finding " + city + "…";
                try
                {
                    var places = await WeatherService.FindAsync(city);
                    if (places.Count == 0) { Fail("No place called \"" + city + "\" was found. Try another spelling.", cityBox); return; }
                    draft.Location = places[0].Label; draft.Latitude = places[0].Latitude; draft.Longitude = places[0].Longitude;
                }
                catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
                { Fail("Couldn't reach the place search. Check your connection, or try again.", cityBox); return; }
            }
        }
        if (draft.Kind != ItemKinds.Image && string.IsNullOrWhiteSpace(nameBox.Text)) { Fail("Give this a name.", nameBox); return; }
        if (draft.Kind == ItemKinds.Launcher && string.IsNullOrWhiteSpace(targetBox!.Text)) { Fail("Choose the file or application to open.", targetBox); return; }
        if (iconBox is not null && Clean(iconBox) is { Length: > 0 } icon && !File.Exists(icon)) { Fail("The icon image could not be found.", iconBox); return; }
        if (imageBox is not null && !File.Exists(Clean(imageBox))) { Fail("Choose a picture file.", imageBox); return; }
        long value = draft.Count;
        int step = draft.Step;
        if (valueBox is not null && !long.TryParse(valueBox.Text.Trim(), out value)) { Fail("The current value must be a whole number.", valueBox); return; }
        if (stepBox is not null && (!int.TryParse(stepBox.Text.Trim(), out step) || step < 1)) { Fail("The step must be a whole number of 1 or more.", stepBox); return; }
        int work = draft.Pomodoro.WorkMinutes, rest = draft.Pomodoro.BreakMinutes, longRest = draft.Pomodoro.LongBreakMinutes, every = draft.Pomodoro.LongBreakEvery;
        foreach (var (box, assign, max) in new (TextBox?, Action<int>, int)[] { (workBox, v => work = v, 600), (breakBox, v => rest = v, 600), (longBox, v => longRest = v, 600), (everyBox, v => every = v, 20) })
        {
            if (box is null) continue;
            if (!int.TryParse(box.Text.Trim(), out int number) || number < 1 || number > max) { Fail($"Enter a whole number from 1 to {max}.", box); return; }
            assign(number);
        }
        try { Theme.Brush(accentBox.Text.Trim()); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { Fail("Enter a valid accent color, for example #A7DBC8.", accentBox); return; }
        var pluginValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (spec, read) in pluginFields)
        {
            if (read() is not { } setting) return;
            pluginValues[spec.Key] = setting;
        }
        foreach (var (key, setting) in pluginValues)
        {
            // Shared appearance settings left empty are removed, so the widget follows the deck-wide default.
            if (key.StartsWith('_') && setting.Length == 0) draft.Options.Remove(key);
            else draft.Options[key] = setting;
        }

        draft.Name = nameBox.Text.Trim();
        draft.Accent = accentBox.Text.Trim();
        if (targetBox is not null) draft.Target = Clean(targetBox);
        if (argumentsBox is not null) draft.Arguments = argumentsBox.Text;
        if (directoryBox is not null) draft.WorkingDirectory = Clean(directoryBox);
        if (iconBox is not null) draft.IconPath = Clean(iconBox);
        if (imageBox is not null) draft.ImagePath = Clean(imageBox);
        long goal = 0;
        if (goalBox is not null && goalBox.Text.Trim().Length > 0 && (!long.TryParse(goalBox.Text.Trim(), out goal) || goal < 0)) { Fail("The goal must be a whole number (or empty).", goalBox); return; }
        int startHour = -1, endHour = -1;
        if (startHourBox is not null && startHourBox.Text.Trim().Length > 0 && (!int.TryParse(startHourBox.Text.Trim(), out startHour) || startHour is < 0 or > 23)) { Fail("The first hour must be 0–23 (or empty).", startHourBox); return; }
        if (endHourBox is not null && endHourBox.Text.Trim().Length > 0 && (!int.TryParse(endHourBox.Text.Trim(), out endHour) || endHour is < 1 or > 24)) { Fail("The last hour must be 1–24 (or empty).", endHourBox); return; }
        if (startHour >= 0 && endHour >= 0 && endHour <= startHour) { Fail("The last hour must come after the first.", endHourBox!); return; }
        if (goalBox is not null) draft.CountGoal = goal;
        if (startHourBox is not null) { draft.StartHour = startHour; draft.EndHour = endHour; }
        if (noteBox is not null) draft.Text = noteBox.Text.TrimEnd();
        if (dateBox is not null) draft.TargetDate = dateBox.Text.Trim();
        if (timeBox is not null) draft.TargetTime = Planner.ParseTime(timeBox.Text) ?? "";
        if (folderBox is not null) draft.Target = Clean(folderBox);
        if (cityBox is not null && (draft.Location != originalLocation || draft.Units != originalUnits)) { draft.WeatherJson = ""; draft.WeatherFetched = null; }
        draft.Count = value;
        draft.Step = step;
        if (workBox is not null)
        {
            bool changedLengths = work != draft.Pomodoro.WorkMinutes || rest != draft.Pomodoro.BreakMinutes || longRest != draft.Pomodoro.LongBreakMinutes;
            (draft.Pomodoro.WorkMinutes, draft.Pomodoro.BreakMinutes, draft.Pomodoro.LongBreakMinutes, draft.Pomodoro.LongBreakEvery) = (work, rest, longRest, every);
            if (changedLengths && !Planner.IsRunning(draft.Pomodoro)) Planner.Reset(draft.Pomodoro);
        }
        Result = draft;
        DialogResult = true;
    }

    /// <summary>Shows what is wrong and goes to the field, opening the group it sits in if that is closed.</summary>
    private void Fail(string message, Control field)
    {
        if (quiet) return;
        validation.Text = message;
        Disclosure.Reveal(field);
        field.UpdateLayout();
        field.Focus();
        field.BringIntoView();
    }
}

/// <summary>A one-line text prompt. Text entry happens in a normal top-level window because
/// keyboard input to a window hosted inside Explorer's desktop layer is not dependable.</summary>
/// <summary>A masked one-line prompt for tokens and passwords.</summary>
internal sealed class SecretDialog : Window
{
    private readonly PasswordBox box = Theme.SecretField();
    internal string? Result { get; private set; }

    private SecretDialog(Window owner, string title, string label)
    {
        Owner = owner;
        Title = title;
        Width = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        FontFamily = Theme.UiFont;
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
        var heading = Theme.Label(title, 20);
        heading.Margin = new Thickness(0, 0, 0, 14);
        root.Children.Add(heading);
        var text = Theme.Label(label, 12, Theme.Muted);
        text.TextWrapping = TextWrapping.Wrap;
        text.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(text);
        box.Margin = new Thickness(0, 0, 0, 8);
        root.Children.Add(box);
        var note = Theme.Label("Saved encrypted for your Windows account only.", 11.5, Theme.Muted);
        note.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(note);
        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        var ok = Theme.Button("Save", true);
        ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Password)) { box.Focus(); return; }
            Result = box.Password.Trim();
            DialogResult = true;
        };
        root.Children.Add(ItemEditor.Row(cancel, ok));
        Content = root;
        Loaded += (_, _) => { Activate(); box.Focus(); };
    }

    internal static string? Ask(Window owner, string title, string label)
    {
        var dialog = new SecretDialog(owner, title, label);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}

internal sealed class InputDialog : Window
{
    private readonly TextBox box;
    internal string? Result { get; private set; }

    internal InputDialog(Window owner, string title, string label, string value = "", string action = "Save", bool multiline = false, bool allowEmpty = false)
    {
        Owner = owner;
        Title = title;
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        FontFamily = Theme.UiFont;
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
        var heading = Theme.Label(title, 20);
        heading.Margin = new Thickness(0, 0, 0, 14);
        root.Children.Add(heading);
        box = ItemEditor.Field(root, label, value);
        if (multiline)
        {
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.Height = 180;
            box.VerticalContentAlignment = VerticalAlignment.Top;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Width = 520;
        }
        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        var ok = Theme.Button(action, true);
        ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(box.Text) && !allowEmpty) { box.Focus(); return; }
            Result = multiline ? box.Text.TrimEnd() : box.Text.Trim();
            DialogResult = true;
        };
        var row = ItemEditor.Row(cancel, ok);
        row.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(row);
        Content = root;
        Loaded += (_, _) => { Activate(); box.Focus(); box.SelectAll(); };
    }

    internal static string? Ask(Window owner, string title, string label, string value = "", string action = "Save", bool multiline = false, bool allowEmpty = false)
    {
        var dialog = new InputDialog(owner, title, label, value, action, multiline, allowEmpty);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }
}

/// <summary>Adds or edits one schedule block: title, time, days (or a single date) and color.</summary>
internal sealed class ScheduleBlockEditor : Window
{
    private readonly ScheduleBlock draft;
    private readonly TextBox titleBox, startBox, endBox, dateBox, colorBox;
    private readonly TextBlock validation;
    internal ScheduleBlock? Result { get; private set; }
    internal bool Deleted { get; private set; }

    internal ScheduleBlockEditor(Window owner, ScheduleBlock block, bool isNew)
    {
        draft = block.Copy();
        Owner = owner;
        Title = isNew ? "Add block" : "Edit block";
        Width = 520;
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
        titleBox = ItemEditor.Field(root, "Title", draft.Title);
        var times = new Grid();
        times.ColumnDefinitions.Add(new ColumnDefinition());
        times.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        times.ColumnDefinitions.Add(new ColumnDefinition());
        var startPanel = new StackPanel();
        var endPanel = new StackPanel();
        startBox = ItemEditor.Field(startPanel, "Starts (e.g. 8:35 am)", Planner.Format(draft.Start, false));
        endBox = ItemEditor.Field(endPanel, "Ends (e.g. 3:10 pm)", Planner.Format(draft.End, false));
        Grid.SetColumn(endPanel, 2);
        times.Children.Add(startPanel);
        times.Children.Add(endPanel);
        root.Children.Add(times);

        var daysLabel = Theme.Label("Repeats on", 12, Theme.Muted);
        daysLabel.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(daysLabel);
        var days = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var day in Planner.DayNames)
        {
            var label = Theme.Label(day, 12);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var pill = new Border { Width = 50, Height = 30, CornerRadius = new CornerRadius(15), Margin = new Thickness(0, 0, 6, 0), Cursor = Cursors.Hand, Child = label };
            void Show() { bool on = draft.Days.Contains(day); pill.Background = on ? Theme.Primary : Theme.Surface3; label.Foreground = on ? Theme.OnPrimary : Theme.Text2; }
            pill.MouseLeftButtonUp += (_, _) => { if (!draft.Days.Remove(day)) draft.Days.Add(day); Show(); };
            Show();
            days.Children.Add(pill);
        }
        root.Children.Add(days);
        dateBox = ItemEditor.Field(root, "Or only on one date (yyyy-mm-dd, optional — overrides the days above)", draft.Date);
        colorBox = ItemEditor.Field(root, "Color", draft.Color);
        root.Children.Add(ItemEditor.SwatchRow(colorBox));
        if (draft.CancelledDates.Count > 0)
        {
            var skipped = Theme.Label($"Skipped on {draft.CancelledDates.Count} date(s). ", 12, Theme.Muted);
            var restore = Theme.Button("Restore skipped days");
            restore.Click += (_, _) => { draft.CancelledDates.Clear(); restore.IsEnabled = false; skipped.Text = "Skipped days will be restored. "; };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            row.Children.Add(skipped);
            row.Children.Add(restore);
            root.Children.Add(row);
        }
        validation = Theme.Label("", 12, Theme.Danger);
        validation.TextWrapping = TextWrapping.Wrap;
        validation.MinHeight = 24;
        root.Children.Add(validation);

        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!isNew)
        {
            var delete = Theme.Button("Delete");
            delete.Foreground = Theme.Danger;
            delete.HorizontalAlignment = HorizontalAlignment.Left;
            delete.Click += (_, _) => { Deleted = true; DialogResult = true; };
            actions.Children.Add(delete);
        }
        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        var save = Theme.Button(isNew ? "Add" : "Save", true);
        save.IsDefault = true;
        save.Click += (_, _) => Save();
        var right = ItemEditor.Row(cancel, save);
        right.Margin = new Thickness(0);
        Grid.SetColumn(right, 1);
        actions.Children.Add(right);
        root.Children.Add(actions);
        Content = root;
        Loaded += (_, _) => { Activate(); titleBox.Focus(); titleBox.SelectAll(); };
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(titleBox.Text)) { Fail("Give the block a title.", titleBox); return; }
        if (Planner.ParseTime(startBox.Text) is not { } start) { Fail("Enter a start time like 8:35 am or 17:30.", startBox); return; }
        if (Planner.ParseTime(endBox.Text) is not { } end) { Fail("Enter an end time like 3:10 pm or 18:00.", endBox); return; }
        if (Planner.Minutes(end) <= Planner.Minutes(start)) { Fail("The block must end after it starts.", endBox); return; }
        string date = dateBox.Text.Trim();
        if (date.Length > 0 && !DateOnly.TryParseExact(date, "yyyy-MM-dd", out _)) { Fail("Write the date as yyyy-mm-dd, or leave it empty.", dateBox); return; }
        if (date.Length == 0 && draft.Days.Count == 0) { Fail("Pick at least one day, or enter a date.", titleBox); return; }
        try { Theme.Brush(colorBox.Text.Trim()); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { Fail("Enter a color such as #C4684F.", colorBox); return; }
        draft.Title = titleBox.Text.Trim();
        draft.Start = start;
        draft.End = end;
        draft.Date = date;
        draft.Color = colorBox.Text.Trim();
        draft.Days = Planner.DayNames.Where(draft.Days.Contains).ToList();
        Result = draft;
        DialogResult = true;
    }

    private void Fail(string message, Control field) { validation.Text = message; field.Focus(); }
}
