using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Services;

namespace DesktopDeck;

/// <summary>What a toast shows. <paramref name="Id"/> is null for a note about several reminders (it only has "Open").</summary>
internal sealed record ReminderToastInfo(string? Id, string Title, string Text, string Detail, bool Missed);

/// <summary>
/// The reminder cards at the top-right of the main monitor: stacked, themed, about six seconds each (they stay while the pointer is on
/// them). Done, Snooze ▾ and Open act on the reminder. Dismissing a card does not answer the reminder: it stays under "Missed".
/// </summary>
internal sealed class ReminderToasts
{
    private const int MaxVisible = 4;
    private readonly DeckHub hub;
    private readonly List<ReminderToast> open = new();

    internal ReminderToasts(DeckHub hub)
    {
        this.hub = hub;
        hub.RemindersChanged += CloseAnswered;
    }

    internal int Count => open.Count;

    internal void Show(ReminderToastInfo info)
    {
        if (info.Id is not null) foreach (var old in open.Where(t => t.Info.Id == info.Id).ToList()) old.Dismiss(animate: false);
        var toast = new ReminderToast(hub, info, hub.ReminderOptions.ToastSeconds, Closed);
        open.Insert(0, toast);
        while (open.Count > MaxVisible) open[^1].Dismiss(animate: false);
        toast.Present();
        Layout();
    }

    /// <summary>--smoke-test --toast: builds a card for a reminder, a missed one and a several-reminders note (never shown) and renders each. Returns report lines; "FAIL " marks a problem.</summary>
    internal static string RenderForSmoke(DeckHub hub, string folder, Action<FrameworkElement, string> snap)
    {
        var lines = new List<string>();
        var cases = new[]
        {
            ("toast-reminder", new ReminderToastInfo("sample", "Reminder", "Call the dentist about Friday", "Today 15:00 · repeats weekly", false)),
            ("toast-missed", new ReminderToastInfo("sample", "Missed reminder", "Water the plants", "Yesterday 18:30", true)),
            ("toast-several", new ReminderToastInfo(null, "3 reminders missed", "Open the list to see them", "", true)),
        };
        foreach (var (name, info) in cases)
        {
            try
            {
                var toast = new ReminderToast(hub, info, 6, _ => { });
                var card = (FrameworkElement)toast.Content;
                toast.Content = null;
                card.Margin = new Thickness(0);
                card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                card.Arrange(new Rect(card.DesiredSize));
                card.UpdateLayout();
                snap(card, Path.Combine(folder, name + ".png"));
                lines.Add("ok   " + name + " (" + toast.PixelSize(1).Width + "x" + toast.PixelSize(1).Height + ")");
            }
            catch (Exception ex) { lines.Add("FAIL " + name + ": " + ex.Message); }
        }
        return string.Join(Environment.NewLine, lines);
    }

    internal void CloseAll() { foreach (var toast in open.ToList()) toast.Dismiss(animate: false); }

    private void Closed(ReminderToast toast)
    {
        open.Remove(toast);
        Layout();
    }

    /// <summary>A reminder answered somewhere else (the control center, the widget) takes its card away.</summary>
    private void CloseAnswered()
    {
        foreach (var toast in open.Where(t => t.Info.Id is not null && !hub.ReminderNeedsAnswer(t.Info.Id)).ToList()) toast.Dismiss(animate: true);
    }

    private void Layout()
    {
        if (hub.Displays.Count == 0) return;
        var display = hub.PrimaryDisplay;
        var work = display.WorkArea;
        int margin = (int)Math.Round(16 * display.Scale), gap = (int)Math.Round(10 * display.Scale);
        int y = work.Y + margin;
        foreach (var toast in open)
        {
            var size = toast.PixelSize(display.Scale);
            toast.MoveTo(new PixelRect(work.X + work.Width - size.Width - margin, y, size.Width, size.Height));
            y += size.Height + gap;
        }
    }
}

internal sealed class ReminderToast : Window
{
    private const double CardWidth = 344, Pad = 14;
    private readonly DeckHub hub;
    private readonly Action<ReminderToast> closedCallback;
    private readonly DispatcherTimer timer = new();
    private readonly Border card;
    private readonly Size cardSize;
    private nint handle;
    private PixelRect target;
    private bool menuOpen, ending;

    internal ReminderToastInfo Info { get; }

    internal ReminderToast(DeckHub hub, ReminderToastInfo info, int seconds, Action<ReminderToast> closed)
    {
        this.hub = hub;
        Info = info;
        closedCallback = closed;
        Title = "Desktop Deck reminder";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Opacity = 0;
        FontFamily = Theme.UiFont;
        UseLayoutRounding = true;
        System.Windows.Automation.AutomationProperties.SetName(this, "Reminder: " + info.Text);

        card = BuildCard();
        card.Margin = new Thickness(Pad);
        card.Effect = PanelKit.Shadow(18, 4, 0.5);
        Content = card;
        card.Measure(new Size(CardWidth, double.PositiveInfinity));
        cardSize = new Size(CardWidth + 2 * Pad, card.DesiredSize.Height + 2 * Pad);
        Width = cardSize.Width;
        Height = cardSize.Height;

        timer.Interval = TimeSpan.FromSeconds(Math.Max(2, seconds));
        timer.Tick += (_, _) =>
        {
            // Stay while the pointer is on the card or a menu is open.
            if (card.IsMouseOver || menuOpen) return;
            Dismiss(animate: true);
        };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            NativeWindow.AddExStyle(handle, NativeWindow.ExToolWindow | NativeWindow.ExNoActivate);
            Place();
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Place));
    }

    internal (int Width, int Height) PixelSize(double scale) => ((int)Math.Round(cardSize.Width * scale), (int)Math.Round(cardSize.Height * scale));

    internal void MoveTo(PixelRect rect)
    {
        target = rect;
        Place();
    }

    private void Place()
    {
        if (handle != 0 && !ending && target.Width > 0) NativeWindow.Place(handle, target, true);
    }

    internal void Present()
    {
        Show();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        timer.Start();
    }

    /// <summary>Takes the card away (it fades unless <paramref name="animate"/> is false). The reminder itself is untouched.</summary>
    internal void Dismiss(bool animate)
    {
        if (ending) return;
        ending = true;
        timer.Stop();
        closedCallback(this);
        if (!animate) { Close(); return; }
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120));
        fade.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    private Border BuildCard()
    {
        string accentHex = Info.Missed ? "#E07A6A" : hub.ReminderOptions.Color;
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition());
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tile = PanelKit.IconTile("", accentHex, 38);
        tile.VerticalAlignment = VerticalAlignment.Top;
        body.Children.Add(tile);

        var text = new StackPanel { Margin = new Thickness(12, 0, 8, 0) };
        var title = Theme.WidgetTitle(Info.Title, Theme.Accent(accentHex));
        text.Children.Add(title);
        var main = Theme.Label(Info.Text, 14.5);
        main.FontWeight = FontWeights.SemiBold;
        main.TextWrapping = TextWrapping.Wrap;
        main.TextTrimming = TextTrimming.CharacterEllipsis;
        main.MaxHeight = 40;
        main.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(main);
        if (Info.Detail.Length > 0)
        {
            var detail = Theme.Label(Info.Detail, 12, Theme.Muted);
            detail.TextWrapping = TextWrapping.Wrap;
            detail.TextTrimming = TextTrimming.CharacterEllipsis;
            detail.MaxHeight = 32;
            detail.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(detail);
        }
        Grid.SetColumn(text, 1);
        body.Children.Add(text);

        var close = Theme.IconButton("", "Dismiss (the reminder stays in the list)", 24);
        close.VerticalAlignment = VerticalAlignment.Top;
        close.Background = Brushes.Transparent;
        close.Click += (_, _) => Dismiss(animate: true);
        Grid.SetColumn(close, 2);
        body.Children.Add(close);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        if (Info.Id is { } id)
        {
            var done = Theme.Button("Done", prominent: true);
            done.MinHeight = 30;
            done.Padding = new Thickness(14, 4, 14, 4);
            done.Click += (_, _) => { Dismiss(animate: true); hub.CompleteReminder(id); };
            buttons.Children.Add(done);
            var snooze = PanelKit.Outline("Snooze ▾", () => { });
            snooze.Margin = new Thickness(8, 0, 0, 0);
            snooze.Click += (_, _) => ShowSnoozeMenu(snooze, id);
            buttons.Children.Add(snooze);
        }
        var open = PanelKit.Quiet("Open", () => { Dismiss(animate: true); hub.OpenReminders(); });
        open.Margin = new Thickness(4, 0, 0, 0);
        buttons.Children.Add(open);
        Grid.SetRow(buttons, 1);
        Grid.SetColumnSpan(buttons, 3);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        body.Children.Add(buttons);

        var frame = new Border
        {
            Background = Theme.Surface, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 10, 12), Width = CardWidth,
        };
        // A thin accent edge on the left.
        var edge = new Border { Width = 3, CornerRadius = new CornerRadius(2), Background = Theme.Accent(accentHex), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-8, 4, 0, 4) };
        var shell = new Grid();
        shell.Children.Add(body);
        shell.Children.Add(edge);
        frame.Child = shell;
        return frame;
    }

    /// <summary>The Snooze ▾ choices: 5, 10, 30, 60 minutes or tomorrow.</summary>
    internal static ContextMenu SnoozeMenu(Action<SnoozeChoice> choose)
    {
        var menu = MainWindow.Menu();
        foreach (var (choice, label) in new[] { (SnoozeChoice.Minutes5, "5 minutes"), (SnoozeChoice.Minutes10, "10 minutes"), (SnoozeChoice.Minutes30, "30 minutes"), (SnoozeChoice.Minutes60, "1 hour"), (SnoozeChoice.Tomorrow, "Tomorrow") })
        {
            var picked = choice;
            MainWindow.AddMenuItem(menu, label, () => choose(picked), "");
        }
        return menu;
    }

    private void ShowSnoozeMenu(Button anchor, string id)
    {
        var menu = SnoozeMenu(choice => { Dismiss(animate: true); hub.SnoozeReminder(id, choice); });
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.Opened += (_, _) => menuOpen = true;
        menu.Closed += (_, _) => { menuOpen = false; timer.Stop(); timer.Start(); };
        menu.IsOpen = true;
    }
}
