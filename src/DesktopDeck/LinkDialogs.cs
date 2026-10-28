using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>One widget in a "Link to…" list: its name, where it is and a short peek at what it holds.</summary>
internal sealed record LinkChoice(LauncherItem Item, string Name, string Where, string Preview)
{
    /// <summary>"Pinned", "Desktop · Display 2", with "hidden" added for a widget that is switched off.</summary>
    internal static string PlaceOf(LauncherItem item, DeckHub hub)
    {
        string place = item.OnTop ? "Pinned" : "Desktop";
        if (hub.Displays.Count > 1) place += " · " + hub.CurrentDisplayOf(item).Name;
        if (item.Hidden) place += " · hidden";
        return place;
    }

    internal static LinkChoice Of(LauncherItem item, DeckHub hub) =>
        new(item, ControlCenter.DisplayName(item), PlaceOf(item, hub), WidgetLinks.Preview(item));
}

/// <summary>
/// "Link to another Homework?": a list of the existing widgets of the same kind. Link shares the chosen one's data with the new widget;
/// Start from scratch makes an unlinked one. Closing the window (Esc) cancels adding.
/// </summary>
internal sealed class LinkDialog : Window
{
    private readonly List<(Border Row, Border Dot, LinkChoice Choice)> rows = new();
    private LinkChoice? selected;
    /// <summary>The widget to link with; null when the user chose Start from scratch or cancelled.</summary>
    internal LinkChoice? Result { get; private set; }
    /// <summary>True when the user chose Start from scratch.</summary>
    internal bool Scratch { get; private set; }

    internal LinkDialog(Window? owner, string kindName, IReadOnlyList<LinkChoice> choices)
    {
        if (owner is not null) Owner = owner;
        Title = $"Link to another {kindName}?";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        FontFamily = Theme.UiFont;
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
        var heading = Theme.Label(Title, 20);
        heading.TextWrapping = TextWrapping.Wrap;
        heading.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(heading);
        var explain = Theme.Label("Linked widgets share the same information. Each one keeps its own size, place and look.", 12, Theme.Muted);
        explain.TextWrapping = TextWrapping.Wrap;
        explain.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(explain);

        var list = new StackPanel();
        foreach (var choice in choices) list.Children.Add(MakeRow(choice));
        root.Children.Add(new ScrollViewer { Content = list, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 14) });
        Select(choices.FirstOrDefault());

        var scratch = Theme.Button("Start from scratch");
        scratch.Click += (_, _) => { Scratch = true; DialogResult = false; };
        var link = Theme.Button("Link", true);
        link.IsDefault = true;
        link.Click += (_, _) => { Result = selected; DialogResult = true; };
        root.Children.Add(ItemEditor.Row(scratch, link));
        Content = root;
        Loaded += (_, _) => Activate();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }

    private Border MakeRow(LinkChoice choice)
    {
        var dot = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(8), BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1.5), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var name = Theme.Label(choice.Name, 13.5);
        name.FontWeight = FontWeights.SemiBold;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var detail = Theme.Label(choice.Preview.Length > 0 ? choice.Where + "  ·  " + choice.Preview : choice.Where, 11.5, Theme.Muted);
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);
        text.Children.Add(detail);
        var inner = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left);
        inner.Children.Add(dot);
        inner.Children.Add(text);
        var row = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = inner };
        row.MouseLeftButtonUp += (_, e) => { Select(choice); e.Handled = true; };
        row.MouseLeftButtonDown += (_, e) => e.Handled = true;
        rows.Add((row, dot, choice));
        return row;
    }

    private void Select(LinkChoice? choice)
    {
        selected = choice;
        foreach (var (row, dot, c) in rows)
        {
            bool on = c == choice;
            row.Background = on ? Theme.Tint(Theme.PrimaryHex, 0.18) : Theme.Surface3;
            row.BorderBrush = on ? Theme.Primary : Theme.Line;
            dot.Background = on ? Theme.Primary : Brushes.Transparent;
            dot.BorderBrush = on ? Theme.Primary : Theme.LineStrong;
        }
    }

    /// <summary>Shows the dialog. Returns the chosen widget; sets <paramref name="scratch"/> for Start from scratch; null with it false means cancelled.</summary>
    internal static LinkChoice? Ask(Window owner, string kindName, IReadOnlyList<LinkChoice> choices, out bool scratch)
    {
        var dialog = new LinkDialog(owner, kindName, choices);
        dialog.ShowDialog();
        scratch = dialog.Scratch;
        return dialog.Result;
    }
}

/// <summary>Two widgets with their own data are being linked: which one's data do both keep?</summary>
internal sealed class KeepDataDialog : Window
{
    /// <summary>The widget whose data is kept; null when cancelled.</summary>
    internal LinkChoice? Kept { get; private set; }

    internal KeepDataDialog(Window? owner, LinkChoice first, LinkChoice second)
    {
        if (owner is not null) Owner = owner;
        Title = "Which one do you want to keep?";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        FontFamily = Theme.UiFont;
        var root = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
        var heading = Theme.Label(Title, 20);
        heading.Margin = new Thickness(0, 0, 0, 6);
        root.Children.Add(heading);
        var explain = Theme.Label("Both widgets already hold information. Once linked they share one copy: the other widget's information is replaced.", 12, Theme.Muted);
        explain.TextWrapping = TextWrapping.Wrap;
        explain.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(explain);
        root.Children.Add(Option(first));
        root.Children.Add(Option(second));
        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        var row = ItemEditor.Row(cancel);
        row.Margin = new Thickness(0, 6, 0, 0);
        root.Children.Add(row);
        Content = root;
        Loaded += (_, _) => Activate();
    }

    private Border Option(LinkChoice choice)
    {
        var name = Theme.Label("Keep " + choice.Name, 13.5);
        name.FontWeight = FontWeights.SemiBold;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        var detail = Theme.Label(choice.Preview.Length > 0 ? choice.Where + "  ·  " + choice.Preview : choice.Where, 11.5, Theme.Muted);
        detail.TextTrimming = TextTrimming.CharacterEllipsis;
        var text = new StackPanel();
        text.Children.Add(name);
        text.Children.Add(detail);
        var row = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = text, Background = Theme.Surface3, BorderBrush = Theme.Line };
        row.MouseEnter += (_, _) => row.Background = Theme.SurfaceHover;
        row.MouseLeave += (_, _) => row.Background = Theme.Surface3;
        row.MouseLeftButtonUp += (_, e) => { Kept = choice; DialogResult = true; e.Handled = true; };
        return row;
    }

    internal static LinkChoice? Ask(Window owner, LinkChoice first, LinkChoice second)
    {
        var dialog = new KeepDataDialog(owner, first, second);
        dialog.ShowDialog();
        return dialog.Kept;
    }
}
