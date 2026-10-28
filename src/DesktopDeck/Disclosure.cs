using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopDeck;

/// <summary>
/// A collapsible section: a header row (chevron, title and an optional one-line summary) above a body that opens and closes.
/// Collapsed by default. The body is always built and stays in the tree while it is closed (just not shown), so the fields in it
/// are still read when the window is saved. The header can be reached with Tab and opened with Enter or Space.
/// </summary>
internal sealed class Disclosure : StackPanel
{
    private const string ChevronClosed = "", ChevronOpen = "";
    private readonly Border header;
    private readonly TextBlock chevron, summaryText;
    private readonly string title;
    private readonly bool nested;
    private bool open;

    /// <summary>Where the content goes.</summary>
    internal StackPanel Body { get; } = new();

    /// <summary>Raised after the section opened or closed (a window can use it to make room).</summary>
    internal event Action? Toggled;

    /// <param name="nested">A lighter header (no box) for a section inside another one.</param>
    internal Disclosure(string title, bool open = false, string? summary = null, bool nested = false)
    {
        this.title = title;
        this.nested = nested;
        chevron = Theme.Icon(ChevronClosed, 10, Theme.Muted);
        chevron.Width = 16;
        chevron.Margin = new Thickness(0, 0, 8, 0);
        var name = Theme.Label(title, nested ? 12.5 : 13.5, Theme.Text);
        name.FontWeight = FontWeights.SemiBold;
        summaryText = Theme.Label("", 12, Theme.Muted);
        summaryText.TextTrimming = TextTrimming.CharacterEllipsis;
        summaryText.Margin = new Thickness(12, 0, 0, 0);
        summaryText.Visibility = Visibility.Collapsed;

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(chevron, Dock.Left);
        DockPanel.SetDock(name, Dock.Left);
        row.Children.Add(chevron);
        row.Children.Add(name);
        row.Children.Add(summaryText);
        header = new Border
        {
            Child = row, Padding = nested ? new Thickness(2, 6, 8, 6) : new Thickness(14, 10, 14, 10), CornerRadius = new CornerRadius(10),
            Background = nested ? Brushes.Transparent : Theme.Surface, BorderThickness = new Thickness(1),
            BorderBrush = nested ? Brushes.Transparent : Theme.Line,
        };
        Brush rest() => nested ? Brushes.Transparent : Theme.Surface;
        header.MouseEnter += (_, _) => header.Background = Theme.Wash;
        header.MouseLeave += (_, _) => header.Background = rest();
        header.MouseLeftButtonUp += (_, e) => { Toggle(); e.Handled = true; };
        PanelKit.MakeFocusable(header, Toggle, title);
        header.LostKeyboardFocus += (_, _) => header.BorderBrush = nested ? Brushes.Transparent : Theme.Line;

        Body.Margin = new Thickness(nested ? 14 : 0, 12, 0, 0);
        Margin = new Thickness(0, nested ? 2 : 6, 0, nested ? 6 : 12);
        Children.Add(header);
        Children.Add(Body);
        Summary = summary ?? "";
        this.open = !open;
        SetOpen(open, false);
    }

    /// <summary>Adds a new section to <paramref name="parent"/> and returns it; put its content in <see cref="Body"/>.</summary>
    internal static Disclosure Add(Panel parent, string title, bool open = false, string? summary = null, bool nested = false)
    {
        var section = new Disclosure(title, open, summary, nested);
        parent.Children.Add(section);
        return section;
    }

    internal bool IsOpen { get => open; set => SetOpen(value, true); }

    /// <summary>One quiet line after the title, e.g. what is set inside. Empty hides it.</summary>
    internal string Summary
    {
        set { summaryText.Text = value; summaryText.Visibility = value.Length > 0 ? Visibility.Visible : Visibility.Collapsed; }
    }

    internal void Toggle() => SetOpen(!open, true);

    private void SetOpen(bool value, bool raise)
    {
        if (value == open) return;
        open = value;
        Body.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        chevron.Text = value ? ChevronOpen : ChevronClosed;
        AutomationProperties.SetName(header, title);
        AutomationProperties.SetHelpText(header, value ? "Expanded" : "Collapsed");
        if (raise) Toggled?.Invoke();
    }

    /// <summary>Opens every section that contains <paramref name="element"/>, so something inside a closed one (such as a field with a mistake) can be seen.</summary>
    internal static void Reveal(DependencyObject? element)
    {
        for (var node = element; node is not null; node = LogicalTreeHelper.GetParent(node) ?? (node is Visual ? VisualTreeHelper.GetParent(node) : null))
            if (node is Disclosure { IsOpen: false } section) section.IsOpen = true;
    }
}
