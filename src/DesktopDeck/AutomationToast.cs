using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopDeck.Layout;

namespace DesktopDeck;

/// <summary>
/// A small notice above the taskbar, centered on the main monitor, that fades out by itself. It never takes focus, so it can appear over
/// whatever app you are using (the desktop decks, where the status line lives, are behind your windows).
/// </summary>
internal sealed class AutomationToast : Window
{
    private readonly TextBlock text = Theme.Label("", 13, Theme.Text);
    private readonly DispatcherTimer hide = new() { Interval = TimeSpan.FromSeconds(4) };
    private nint handle;
    private bool closed;

    internal AutomationToast()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        FontFamily = Theme.UiFont;
        Title = "Desktop Deck notice";
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Theme.Icon(Theme.Glyphs.Settings, 13, Theme.Primary));
        text.Margin = new Thickness(10, 0, 0, 0);
        text.MaxWidth = 520;
        text.TextWrapping = TextWrapping.Wrap;
        row.Children.Add(text);
        Content = new Border
        {
            Margin = new Thickness(14), Padding = new Thickness(16, 10, 18, 10), CornerRadius = new CornerRadius(12), Background = Theme.Surface3,
            BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), Effect = PanelKit.Shadow(18, 4, 0.5), Child = row,
        };
        hide.Tick += (_, _) => { hide.Stop(); FadeOut(); };
        SourceInitialized += (_, _) =>
        {
            handle = new WindowInteropHelper(this).Handle;
            NativeWindow.AddExStyle(handle, NativeWindow.ExToolWindow | NativeWindow.ExNoActivate);
        };
    }

    /// <summary>Shows (or replaces) the notice on a monitor.</summary>
    internal void ShowText(string message, DisplayInfo display)
    {
        if (closed) return;
        text.Text = message;
        BeginAnimation(OpacityProperty, null);
        Opacity = 1;
        UpdateLayout();
        if (!IsVisible) Show();
        int w = (int)Math.Round(ActualWidth * display.Scale), h = (int)Math.Round(ActualHeight * display.Scale);
        var work = display.WorkArea;
        NativeWindow.Place(handle, new PixelRect(work.X + (work.Width - w) / 2, work.Bottom - h - (int)(28 * display.Scale), w, h), true);
        hide.Stop();
        hide.Start();
    }

    private void FadeOut()
    {
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
        fade.Completed += (_, _) => { if (!closed && Opacity <= 0.01) Hide(); };
        BeginAnimation(OpacityProperty, fade);
    }

    internal void CloseToast()
    {
        closed = true;
        hide.Stop();
        try { Close(); } catch (InvalidOperationException) { }
    }
}
