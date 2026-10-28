using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Shapes = System.Windows.Shapes;

namespace DesktopDeck.Widgets;

/// <summary>Small UI helpers shared by the Study widgets: pop-up choosers, color dots, pills and chips.</summary>
internal static class StudyKit
{
    /// <summary>A color option or "#rrggbb" text as a brush; anything invalid becomes <paramref name="fallback"/>.</summary>
    public static Brush Hex(string? hex, Brush fallback) => StudySubjectsLogic.IsHex(hex?.Trim()) ? Theme.Brush(hex!.Trim()) : fallback;

    public static string ValidHex(string? hex, string fallback) => StudySubjectsLogic.IsHex(hex?.Trim()) ? hex!.Trim() : fallback;

    public static Shapes.Ellipse Dot(Brush fill, double size = 9) => new() { Width = size, Height = size, Fill = fill, VerticalAlignment = VerticalAlignment.Center };

    public static MenuItem Item(string header, Action run, string? colorHex = null, bool isChecked = false)
    {
        var item = new MenuItem { Header = header, IsChecked = isChecked };
        if (colorHex is not null && StudySubjectsLogic.IsHex(colorHex)) item.Icon = Dot(Theme.Brush(colorHex), 10);
        item.Click += (_, e) => { e.Handled = true; run(); };
        return item;
    }

    public static MenuItem Sub(string header, IEnumerable<MenuItem> items)
    {
        var parent = new MenuItem { Header = header };
        foreach (var child in items) parent.Items.Add(child);
        return parent;
    }

    /// <summary>Opens a menu right under <paramref name="anchor"/> (or at the mouse when there is none): the deck's quick chooser.</summary>
    public static void Popup(FrameworkElement? anchor, IEnumerable<object> items)
    {
        var menu = new ContextMenu { Placement = anchor is null ? System.Windows.Controls.Primitives.PlacementMode.MousePoint : System.Windows.Controls.Primitives.PlacementMode.Bottom, PlacementTarget = anchor };
        foreach (var item in items) menu.Items.Add(item);
        menu.IsOpen = true;
    }

    public static MenuItem Heading(string text) => new() { Header = text, IsEnabled = false };

    /// <summary>A small rounded label tinted with <paramref name="hex"/>.</summary>
    public static Border PillBox(WidgetContext ctx, string text, string hex, double size = 10.5)
    {
        var label = Theme.Label(text, ctx.Font(size), Theme.Brush(hex));
        label.FontWeight = FontWeights.SemiBold;
        return new Border { CornerRadius = new CornerRadius(9), Background = Theme.Tint(hex, 0.16), Padding = new Thickness(7, 1, 7, 2), Child = label, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>A clickable chip: filled with its color when selected, outlined otherwise.</summary>
    public static Border Chip(WidgetContext ctx, string text, string hex, bool selected, Action click, string? tooltip = null, double size = 11)
    {
        var label = Theme.Label(text, ctx.Font(size), selected ? Theme.OnPrimary : Theme.Text2);
        label.FontWeight = FontWeights.SemiBold;
        label.TextTrimming = TextTrimming.CharacterEllipsis;
        var chip = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(9, 2, 9, 3),
            Background = selected ? Theme.Brush(hex) : Theme.Tint(hex, 0.14),
            Child = label,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        return WidgetKit.Clickable(chip, click, tooltip);
    }

    /// <summary>Right-click opens <paramref name="items"/>; the click is handled so the deck's own menu stays out of the way.</summary>
    public static void AttachMenu(FrameworkElement element, IEnumerable<object> items)
    {
        var menu = new ContextMenu();
        foreach (var item in items) menu.Items.Add(item);
        element.ContextMenu = menu;
    }

    public static ScrollViewer ThinScroll(UIElement content)
    {
        var scroll = WidgetKit.Scroll(content);
        scroll.Margin = new Thickness(0);
        return scroll;
    }

    /// <summary>Muted uppercase section label.</summary>
    public static TextBlock SectionLabel(WidgetContext ctx, string text, Brush color, double size = 10)
    {
        var label = Theme.Label(text.ToUpperInvariant(), ctx.Font(size), color);
        label.FontWeight = FontWeights.SemiBold;
        label.Margin = new Thickness(4, 8, 0, 3);
        return label;
    }

    /// <summary>The system's preference for writing the day before the month.</summary>
    public static bool DayFirst()
    {
        string pattern = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;
        int d = pattern.IndexOf('d'), m = pattern.IndexOf('M');
        return d >= 0 && m >= 0 && d < m;
    }

    public static bool Use24Hour() => !System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains('t');
}
