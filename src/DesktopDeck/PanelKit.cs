using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopDeck;

/// <summary>
/// The small building blocks of the control center, so every tab shares one look: a spacing scale (4 / 8 / 12 / 16 / 24), section
/// headers, rows with a switch, cards with a hover state, badges, icon tiles and quiet buttons.
/// </summary>
internal static class PanelKit
{
    /// <summary>The spacing scale (DIP).</summary>
    internal const double S4 = 4, S8 = 8, S12 = 12, S16 = 16, S24 = 24;
    internal static Brush Hover => Theme.Wash;
    internal static Brush Pressed => Theme.WashStrong;
    internal static readonly FontFamily EmojiFont = new("Segoe UI Emoji");

    /// <summary>A section: a header (and an optional one-line note) above a rounded card. Returns the card's content panel.</summary>
    internal static StackPanel Section(Panel parent, string title, string? note = null)
    {
        var heading = Theme.Label(title, 14.5);
        heading.FontWeight = FontWeights.SemiBold;
        heading.Margin = new Thickness(2, parent.Children.Count == 0 ? 0 : S24, 0, note is null ? S8 : 2);
        parent.Children.Add(heading);
        if (note is not null)
        {
            var sub = Theme.Label(note, 12, Theme.Muted);
            sub.TextWrapping = TextWrapping.Wrap;
            sub.Margin = new Thickness(2, 0, 0, S8);
            parent.Children.Add(sub);
        }
        var card = new Border { Background = Theme.Surface, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(S16, S16, S16, S8) };
        var body = new StackPanel();
        card.Child = body;
        parent.Children.Add(card);
        return body;
    }

    internal static TextBlock Hint(string text)
    {
        var hint = Theme.Label(text, 12, Theme.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 0, 0, S12);
        return hint;
    }

    /// <summary>A label with a description on the left and a switch on the right.</summary>
    internal static Grid Option(string label, string description, bool value, Action<bool> changed)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, S16) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, S16, 0) };
        var name = Theme.Label(label, 13.5);
        name.FontWeight = FontWeights.SemiBold;
        text.Children.Add(name);
        var desc = Theme.Label(description, 12, Theme.Muted);
        desc.TextWrapping = TextWrapping.Wrap;
        desc.Margin = new Thickness(0, 3, 0, 0);
        text.Children.Add(desc);
        row.Children.Add(text);
        var toggle = Theme.Switch(value, changed);
        toggle.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(toggle, 1);
        row.Children.Add(toggle);
        return row;
    }

    /// <summary>A small rounded label, e.g. "Active".</summary>
    internal static Border Badge(string text, string color, bool filled = false)
    {
        var fg = filled ? Theme.OnPrimary : Theme.Accent(color);
        return new Border
        {
            CornerRadius = new CornerRadius(9), Background = filled ? Theme.Accent(color) : Theme.Tint(color, 0.18), Padding = new Thickness(S8, 1, S8, 2),
            VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text, FontFamily = Theme.UiFont, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = fg },
        };
    }

    /// <summary>A rounded square tile with an icon glyph (or an emoji) in the accent color.</summary>
    internal static Border IconTile(string glyph, string accent, double size = 36, bool emoji = false)
    {
        var icon = emoji
            ? new TextBlock { Text = glyph, FontFamily = EmojiFont, FontSize = size * 0.5, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = Theme.Text }
            : Theme.Icon(glyph, size * 0.44, Theme.Accent(accent));
        return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.3), Background = Theme.Tint(accent, 0.18), Child = icon };
    }

    /// <summary>A flat button for row actions: text only, a soft background on hover.</summary>
    internal static Button Quiet(string text, Action click, bool danger = false, string? tip = null, string? glyph = null)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (glyph is not null) content.Children.Add(new TextBlock { Text = glyph, FontFamily = Theme.IconFont, FontSize = 12, Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        var button = Theme.Button("");
        button.Content = content;
        button.FontWeight = FontWeights.Medium;
        button.MinHeight = 30;
        button.Padding = new Thickness(10, 4, 10, 4);
        button.Background = Brushes.Transparent;
        button.BorderBrush = Brushes.Transparent;
        button.Foreground = danger ? Theme.Danger : Theme.Text2;
        if (tip is not null) button.ToolTip = tip;
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A bordered button (secondary action).</summary>
    internal static Button Outline(string text, Action click, string? tip = null)
    {
        var button = Theme.Button(text);
        button.MinHeight = 30;
        button.Padding = new Thickness(12, 4, 12, 4);
        if (tip is not null) button.ToolTip = tip;
        button.Click += (_, _) => click();
        return button;
    }

    /// <summary>A rounded card that lights up under the pointer. <paramref name="selected"/> draws an accent border.</summary>
    internal static Border HoverCard(UIElement content, bool selected = false, double radius = 12)
    {
        var card = new Border
        {
            Background = Theme.Surface, BorderBrush = selected ? Theme.Primary : Theme.Line, BorderThickness = new Thickness(selected ? 1.5 : 1), CornerRadius = new CornerRadius(radius),
            Padding = new Thickness(S12), Child = content,
        };
        card.MouseEnter += (_, _) => { card.Background = Theme.SurfaceHover; if (!selected) card.BorderBrush = Theme.LineStrong; };
        card.MouseLeave += (_, _) => { card.Background = Theme.Surface; if (!selected) card.BorderBrush = Theme.Line; };
        return card;
    }

    /// <summary>A search box with a magnifier and a hint that disappears while typing.</summary>
    internal static (Border Host, TextBox Box) SearchBox(string hint, double width = 260)
    {
        var box = new TextBox
        {
            Background = Brushes.Transparent, Foreground = Theme.Text, BorderThickness = new Thickness(0), FontSize = 13, FontFamily = Theme.UiFont, CaretBrush = Theme.Primary,
            SelectionBrush = Theme.Primary, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0, 6, 0, 6),
        };
        var placeholder = Theme.Label(hint, 13, Theme.Dim);
        placeholder.IsHitTestVisible = false;
        box.TextChanged += (_, _) => placeholder.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var glass = Theme.Icon(Theme.Glyphs.Search, 13, Theme.Muted);
        glass.Margin = new Thickness(0, 0, S8, 0);
        grid.Children.Add(glass);
        var field = new Grid();
        field.Children.Add(box);
        field.Children.Add(placeholder);
        Grid.SetColumn(field, 1);
        grid.Children.Add(field);
        var host = new Border { Width = width, Background = Theme.Surface3, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9), Padding = new Thickness(S12, 0, S12, 0), Child = grid };
        box.GotKeyboardFocus += (_, _) => host.BorderBrush = Theme.Primary;
        box.LostKeyboardFocus += (_, _) => host.BorderBrush = Theme.LineStrong;
        return (host, box);
    }

    /// <summary>A soft shadow for floating surfaces (the panel, the orb).</summary>
    internal static DropShadowEffect Shadow(double blur = 36, double depth = 10, double opacity = 0.55) =>
        new() { BlurRadius = blur, ShadowDepth = depth, Direction = 270, Opacity = Theme.IsDark ? opacity : opacity * 0.5, Color = Colors.Black };

    /// <summary>A line of muted text for an empty list.</summary>
    internal static FrameworkElement Empty(string text)
    {
        var label = Theme.Label(text, 13, Theme.Muted);
        label.TextWrapping = TextWrapping.Wrap;
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.TextAlignment = TextAlignment.Center;
        label.Margin = new Thickness(S16, S24, S16, S24);
        return label;
    }

    /// <summary>Makes a plain element act as a keyboard-focusable button (Enter or Space), with a visible focus ring.</summary>
    internal static void MakeFocusable(Border element, Action click, string name)
    {
        element.Focusable = true;
        element.FocusVisualStyle = null;
        element.Cursor = Cursors.Hand;
        System.Windows.Automation.AutomationProperties.SetName(element, name);
        element.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { click(); e.Handled = true; } };
        element.GotKeyboardFocus += (_, _) => element.BorderBrush = Theme.Text;
    }

    // ---------- Collapsible sections and the "…" menu ----------

    private static readonly Dictionary<string, bool> openSections = new();

    /// <summary>
    /// A <see cref="Disclosure"/> that keeps its open or closed state under <paramref name="key"/> for as long as the app runs, so a tab that is
    /// redrawn after a click (most of them are) does not shut what the user just opened. Collapsed unless <paramref name="open"/> says otherwise.
    /// </summary>
    internal static Disclosure Collapsible(Panel parent, string key, string title, string? summary = null, bool nested = false, bool open = false)
    {
        var section = Disclosure.Add(parent, title, open, summary, nested);
        if (openSections.TryGetValue(key, out bool remembered)) section.IsOpen = remembered;
        section.Toggled += () => openSections[key] = section.IsOpen;
        return section;
    }

    /// <summary>Smoke test: forgets which sections were open.</summary>
    internal static void ForgetOpenSections() => openSections.Clear();

    /// <summary>A small "…" button that opens a menu of the less common actions for a row. <paramref name="fill"/> adds the entries each time it opens.</summary>
    internal static Button More(string tip, Action<ContextMenu> fill)
    {
        var button = Theme.IconButton("", tip, 30);
        button.Focusable = true;
        button.Click += (_, _) =>
        {
            var menu = MainWindow.Menu();
            fill(menu);
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        };
        return button;
    }

    /// <summary>One entry of a "…" menu. A greyed-out entry still explains itself in its tooltip.</summary>
    internal static MenuItem MenuEntry(ContextMenu menu, string title, Action run, bool enabled = true, bool danger = false, string? tip = null, bool? check = null)
    {
        var item = new MenuItem { Header = title, IsEnabled = enabled };
        if (check is { } on) { item.IsCheckable = true; item.IsChecked = on; }
        if (danger) item.Foreground = Theme.Danger;
        if (tip is not null) { item.ToolTip = tip; ToolTipService.SetShowOnDisabled(item, true); }
        item.Click += (_, _) => run();
        menu.Items.Add(item);
        return item;
    }

    /// <summary>A simple "coming soon" view for a tab that isn't built yet.</summary>
    internal static FrameworkElement Placeholder(string title, string text)
    {
        var stack = new StackPanel { Margin = new Thickness(24) };
        var heading = Theme.Label(title, 20);
        heading.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(heading);
        var note = Theme.Label(text, 13, Theme.Muted);
        note.Margin = new Thickness(0, 8, 0, 0);
        stack.Children.Add(note);
        return stack;
    }
}
