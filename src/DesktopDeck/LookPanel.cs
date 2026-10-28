using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>
/// Pieces of the "one look for every widget" settings that more than one window shows: the card-style buttons (a small picture of each style,
/// used by the Appearance tab and by Customize) and the switch that turns the deck look on.
/// </summary>
internal static class LookPanel
{
    /// <summary>The card styles in the order they are offered, with the words shown to the user.</summary>
    internal static readonly (string Style, string Label)[] Styles =
    [
        ("solid", "Solid"), ("glass", "See-through"), ("frosted", "Frosted glass"), ("outline", "Outline"), ("none", "No card"),
    ];

    private const double TileWidth = 84, PictureHeight = 52;

    /// <summary>
    /// Big buttons, one per card style, each with a small picture of the style over a colorful background. Returns the buttons and a function
    /// that marks one of them as chosen (null marks none, for several widgets that differ).
    /// </summary>
    internal static (WrapPanel Panel, Action<string?> Show) StyleButtons(string? current, Action<string> pick)
    {
        var panel = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var buttons = new List<(string Style, Button Button)>();
        string? shown = current;
        void Show(string? style)
        {
            shown = style;
            foreach (var (s, b) in buttons)
            {
                bool on = s == shown;
                b.BorderBrush = on ? Theme.Primary : Theme.LineStrong;
                b.BorderThickness = new Thickness(on ? 2 : 1);
                b.Background = on ? Theme.Tint(Theme.PrimaryHex, 0.14) : Theme.Surface;
            }
        }
        foreach (var (style, label) in Styles)
        {
            var stack = new StackPanel { Width = TileWidth };
            var picture = Picture(style);
            picture.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(picture);
            var name = Theme.Label(label, 12);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(name);
            var button = Theme.Button("");
            button.Content = stack;
            button.Padding = new Thickness(6, 6, 6, 7);
            button.Margin = new Thickness(0, 0, 8, 8);
            button.ToolTip = style switch
            {
                "glass" => "A see-through card.",
                "frosted" => "Frosted glass: the background behind the card is blurred, like on a Mac.",
                "outline" => "Just an outline, no fill.",
                "none" => "No card at all.",
                _ => "A plain card.",
            };
            button.Click += (_, _) => { Show(style); pick(style); };
            buttons.Add((style, button));
            panel.Children.Add(button);
        }
        Show(current);
        return (panel, Show);
    }

    /// <summary>A small stand-in for a card in one style, sitting on colorful shapes so see-through and blurred styles can be told apart.</summary>
    private static FrameworkElement Picture(string style)
    {
        FrameworkElement Scene(double blur)
        {
            var canvas = new Canvas { Width = TileWidth - 12, Height = PictureHeight };
            canvas.Children.Add(new Rectangle { Width = canvas.Width, Height = PictureHeight, Fill = new LinearGradientBrush(Color.FromRgb(0x2B, 0x3A, 0x67), Color.FromRgb(0x8E, 0x4A, 0x6B), 20) });
            var sun = new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x47)) };
            Canvas.SetLeft(sun, 46); Canvas.SetTop(sun, 6);
            var bar = new Rectangle { Width = 40, Height = 5, RadiusX = 2.5, RadiusY = 2.5, Fill = new SolidColorBrush(Color.FromRgb(0x7F, 0xE0, 0xC8)) };
            Canvas.SetLeft(bar, 14); Canvas.SetTop(bar, 32);
            canvas.Children.Add(sun);
            canvas.Children.Add(bar);
            if (blur > 0) canvas.Effect = new BlurEffect { Radius = blur, KernelType = KernelType.Gaussian };
            return canvas;
        }
        var frame = new Grid { Width = TileWidth - 12, Height = PictureHeight, ClipToBounds = true };
        frame.Children.Add(new Border { CornerRadius = new CornerRadius(5), Child = null });
        frame.Children.Add(Scene(0));
        var card = new Border { Width = 52, Height = 30, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 11, 0, 0), ClipToBounds = true };
        switch (style)
        {
            case "solid":
                card.Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x23, 0x1B));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)); card.BorderThickness = new Thickness(1);
                break;
            case "glass":
                card.Background = new SolidColorBrush(Color.FromArgb(120, 34, 28, 21));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)); card.BorderThickness = new Thickness(1);
                break;
            case "frosted":
            {
                // The same scene, blurred, in the card's own place, under a light tint.
                var grid = new Grid();
                var blurred = Scene(5);
                blurred.Margin = new Thickness(-8, -11, 0, 0);
                blurred.HorizontalAlignment = HorizontalAlignment.Left;
                blurred.VerticalAlignment = VerticalAlignment.Top;
                grid.Children.Add(blurred);
                grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) });
                card.Child = grid;
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)); card.BorderThickness = new Thickness(1);
                break;
            }
            case "outline":
                card.Background = Brushes.Transparent;
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(220, 230, 220, 205)); card.BorderThickness = new Thickness(1.5);
                break;
            default:
                card.Background = Brushes.Transparent;
                break;
        }
        var sample = new TextBlock { Text = "Aa", Foreground = Brushes.White, FontSize = 13, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 11, 0, 0) };
        // The text sits in the card's place (frosted cards hold a picture, so it goes beside them in the frame).
        sample.Width = 52; sample.Height = 30; sample.TextAlignment = TextAlignment.Center; sample.Padding = new Thickness(0, 6, 0, 0); sample.HorizontalAlignment = HorizontalAlignment.Left; sample.VerticalAlignment = VerticalAlignment.Top;
        frame.Children.Add(card);
        frame.Children.Add(sample);
        return new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = frame, HorizontalAlignment = HorizontalAlignment.Center };
    }

    /// <summary>
    /// The prominent switch for the deck look: a highlighted card with the title, a short note and the switch.
    /// </summary>
    internal static FrameworkElement DeckSwitch(bool value, Action<bool> changed)
    {
        var row = PanelKit.Option("Use one look for every widget", "Set the look once below and every widget follows it. A widget can keep its own look from its Customize window; turn this off to bring them all back.", value, changed);
        row.Margin = new Thickness(0);
        return new Border
        {
            Background = Theme.Tint(Theme.PrimaryHex, 0.10), BorderBrush = Theme.Primary, BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(0, 0, 0, 16), Child = row,
        };
    }
}
