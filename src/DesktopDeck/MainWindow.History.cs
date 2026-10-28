using System.Windows;
using System.Windows.Controls;

namespace DesktopDeck;

// The toast at the bottom-left, with an optional button ("Removed Note. Undo") for undoing destructive changes.
public sealed partial class MainWindow
{
    private readonly StackPanel toastRow = new() { Orientation = Orientation.Horizontal };
    private Button? toastButton;

    /// <summary>The toast's content: the message, and (while there is one) an action button after it.</summary>
    private FrameworkElement BuildToastContent()
    {
        toastRow.Children.Add(toastText);
        return toastRow;
    }

    /// <summary>
    /// A short message that fades away by itself. With an <paramref name="actionLabel"/> it also has a button that runs <paramref name="action"/> and
    /// stays a little longer (clicking the toast is otherwise impossible: it never takes the pointer).
    /// </summary>
    internal void SetStatus(string text, string? actionLabel, Action? action)
    {
        toastText.Text = text;
        if (toastButton is not null) { toastRow.Children.Remove(toastButton); toastButton = null; }
        bool withAction = actionLabel is not null && action is not null;
        if (withAction)
        {
            toastButton = PanelKit.Quiet(actionLabel!, () => { toast.Visibility = Visibility.Collapsed; toastTimer.Stop(); action!(); });
            toastButton.Foreground = Theme.Primary;
            toastButton.Margin = new Thickness(PanelKit.S12, -6, -8, -6);
            toastRow.Children.Add(toastButton);
        }
        toast.IsHitTestVisible = withAction;
        toastTimer.Interval = TimeSpan.FromSeconds(withAction ? 8 : 3.2);
        toast.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Start();
    }
}
