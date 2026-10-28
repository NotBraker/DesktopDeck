using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDeck.Models;
using Microsoft.Win32;

namespace DesktopDeck;

/// <summary>
/// The look editor: the deck background per display and the default card style for every widget, with a live preview.
/// Changes apply to every deck at once and are saved. It returns plain content (no window), so the control center's Appearance tab hosts it.
/// </summary>
internal static class AppearancePanel
{
    private const double PreviewWidth = 340;
    private static readonly string[] Neutrals = ["#000000", "#1A1611", "#2E261D", "#18334A", "#4A3F33", "#EDE4D3", "#FFFFFF"];
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private static void ShowWarning(Window? owner, string text)
    {
        if (owner is null) MessageBox.Show(text, "Desktop Deck", MessageBoxButton.OK, MessageBoxImage.Warning);
        else MessageBox.Show(owner, text, "Desktop Deck", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <param name="shareChanged">Runs after "Use one look for every widget" is switched, so the tab around this panel can show or hide what depends on it (the saved looks). Without it the panel just redraws itself.</param>
    internal static FrameworkElement Build(DeckHub hub, Window? owner = null, Action? shareChanged = null)
    {
        var settings = hub.Settings;
        var host = new StackPanel();
        // "" is all displays; otherwise a display id.
        string selected = "";
        var preview = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(10), BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), ClipToBounds = true, Margin = new Thickness(0, 0, 0, 14) };
        var saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); hub.RenderAll(); hub.Save(); };

        AppearanceSettings Look() => settings.Appearance ??= new AppearanceSettings();
        BackgroundStyle? Effective() => Look().BackgroundFor(selected);
        bool HasOwn() => Look().Backgrounds.ContainsKey(selected);
        /// <summary>The style being edited: this selection's own, created from what currently shows when it has none.</summary>
        BackgroundStyle Own()
        {
            if (Look().Backgrounds.TryGetValue(selected, out var own)) return own;
            var created = (selected.Length > 0 ? Effective()?.Copy() : null) ?? new BackgroundStyle();
            Look().Backgrounds[selected] = created;
            return created;
        }

        void Changed()
        {
            UpdatePreview();
            saveTimer.Stop();
            saveTimer.Start();
        }

        void UpdatePreview()
        {
            var display = selected.Length > 0 ? hub.Displays.FirstOrDefault(d => string.Equals(d.Id, selected, StringComparison.OrdinalIgnoreCase)) : null;
            double aspect = display is { Bounds: { Width: > 0, Height: > 0 } b } ? (double)b.Width / b.Height : 16.0 / 9;
            double height = Math.Round(PreviewWidth / aspect);
            var size = new Size(PreviewWidth, height);
            var canvas = new Canvas { Width = size.Width, Height = size.Height, Background = Appearance.BuildBrush(Appearance.DeckStyle(settings, selected), size, 1) };
            var backdrop = Appearance.FrostBackdrop(Appearance.DeckStyle(settings, selected), size);
            void Mock(string title, string[] lines, double x, double y, double w, double h, string style = "solid")
            {
                var item = new LauncherItem { CardStyle = style };
                using var frost = Appearance.PushBackdrop(backdrop, new Rect(x, y, w, h));
                var card = Appearance.Card(Look(), item);
                card.Width = w; card.Height = h;
                var text = Appearance.TextColor(Look(), item);
                var stack = new StackPanel { Margin = new Thickness(8, 6, 6, 4) };
                var heading = Theme.WidgetTitle(title, Appearance.TitleBrush(Look(), item));
                heading.FontSize = 7.5;
                stack.Children.Add(heading);
                foreach (var line in lines)
                {
                    var label = Theme.Label(line, 9, text is { } t ? Appearance.Solid(t) : Theme.Text);
                    label.Margin = new Thickness(0, 4, 0, 0);
                    stack.Children.Add(label);
                }
                card.Child = stack;
                Canvas.SetLeft(card, x); Canvas.SetTop(card, y);
                canvas.Children.Add(card);
            }
            Mock("TIME", ["10:42 AM"], 16, 14, 104, 46);
            Mock("TODAY", ["Reply to email", "Gym"], size.Width - 128, 14, 112, 70);
            Mock("NOTE", ["Finish the slides"], 16, size.Height - 60, 128, 44, "glass");
            preview.Child = canvas;
        }

        Border Heading(string text)
        {
            var title = Theme.Label(text, 13.5);
            title.FontWeight = FontWeights.SemiBold;
            return new Border { Margin = new Thickness(0, 4, 0, 8), Child = title };
        }

        TextBlock Hint(string text)
        {
            var hint = Theme.Label(text, 12, Theme.Muted);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 0, 0, 12);
            return hint;
        }

        // A color box with swatches. Valid colors apply as you type; empty (when allowed) means the theme.
        void ColorField(Panel parent, string label, string value, Action<string> set, bool allowEmpty, string? emptyMeans = null)
        {
            var box = ItemEditor.Field(parent, label + (emptyMeans is null ? "" : $" (empty = {emptyMeans})"), value);
            box.ToolTip = "Pick a swatch or type a color such as #C4684F.";
            box.TextChanged += (_, _) =>
            {
                string text = box.Text.Trim();
                bool ok = text.Length == 0 ? allowEmpty : AppearanceSettings.CleanColor(text).Length > 0 && Appearance.ParseColor(text) is not null;
                box.BorderBrush = ok ? Theme.LineStrong : Theme.Danger;
                if (!ok) return;
                set(text);
                Changed();
            };
            // One row: the accent swatches, a divider, then a few neutrals.
            var swatches = new WrapPanel { Margin = new Thickness(0, -6, 0, 12) };
            void Chip(string color)
            {
                var chip = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 0, 6, 6), BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1),
                    Background = Theme.Accent(color), Cursor = Cursors.Hand, ToolTip = color,
                };
                chip.MouseLeftButtonUp += (_, _) => box.Text = color;
                swatches.Children.Add(chip);
            }
            foreach (var color in Theme.Swatches) Chip(color);
            swatches.Children.Add(new Border { Width = 1, Height = 20, Background = Theme.LineStrong, Margin = new Thickness(2, 0, 8, 6) });
            foreach (var color in Neutrals) Chip(color);
            parent.Children.Add(swatches);
        }

        // A labeled number box on one line; valid numbers apply as you type.
        void NumberRow(Panel parent, string label, double value, double min, double max, Action<double> set, string? note = null)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            text.Children.Add(Theme.Label(label, 13));
            if (note is not null)
            {
                var small = Theme.Label(note, 11.5, Theme.Muted);
                small.TextWrapping = TextWrapping.Wrap;
                text.Children.Add(small);
            }
            row.Children.Add(text);
            var box = Theme.TextField(value.ToString("0.##", Invariant));
            box.HorizontalContentAlignment = HorizontalAlignment.Right;
            box.ToolTip = $"{min.ToString(Invariant)} to {max.ToString(Invariant)}";
            box.TextChanged += (_, _) =>
            {
                bool ok = double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, Invariant, out var number) && double.IsFinite(number) && number >= min && number <= max;
                box.BorderBrush = ok ? Theme.LineStrong : Theme.Danger;
                if (!ok) return;
                set(number);
                Changed();
            };
            Grid.SetColumn(box, 1);
            row.Children.Add(box);
            parent.Children.Add(row);
        }

        void Rebuild()
        {
            host.Children.Clear();

            // ---------- Preview ----------
            host.Children.Add(preview);
            UpdatePreview();

            // ---------- One look for every widget ----------
            var deck = Look();
            host.Children.Add(Heading("Widget cards"));
            host.Children.Add(LookPanel.DeckSwitch(deck.ApplyToAll, on =>
            {
                deck.ApplyToAll = on;
                Changed();
                if (shareChanged is not null) shareChanged(); else Rebuild();
            }));
            host.Children.Add(Hint(deck.ApplyToAll
                ? "Every widget follows the look below. To keep one different, choose “Give this widget its own look” in its Customize window."
                : "The switch is off, so every widget keeps its own look. Card style, text size and inner spacing need the switch on."));
            var cards = new StackPanel();
            host.Children.Add(cards);
            var styleHost = new StackPanel { IsEnabled = deck.ApplyToAll, Opacity = deck.ApplyToAll ? 1 : 0.5 };
            var styleLabel = Theme.Label("Card style", 12, Theme.Muted);
            styleLabel.Margin = new Thickness(0, 0, 0, 6);
            styleHost.Children.Add(styleLabel);
            styleHost.Children.Add(LookPanel.StyleButtons(deck.CardStyle, s => { deck.CardStyle = s; Changed(); }).Panel);
            cards.Children.Add(styleHost);
            NumberRow(cards, "Corner roundness", deck.CornerRadius, 0, 40, v => deck.CornerRadius = v);
            NumberRow(cards, "Card opacity (%)", deck.CardOpacity, 0, 100, v => deck.CardOpacity = v);
            var sizeHost = new StackPanel { IsEnabled = deck.ApplyToAll, Opacity = deck.ApplyToAll ? 1 : 0.5 };
            NumberRow(sizeHost, "Text size (%)", deck.TextScale, 60, 250, v => deck.TextScale = v);
            NumberRow(sizeHost, "Inner spacing", deck.Padding, 0, 40, v => deck.Padding = v);
            cards.Children.Add(sizeHost);
            var colors = PanelKit.Collapsible(cards, "appearance.colors", "More colors", nested: true);
            ColorField(colors.Body, "Card color", deck.CardColor, c => deck.CardColor = c, true, "theme");
            ColorField(colors.Body, "Text color", deck.TextColor, c => deck.TextColor = c, true, "theme");
            ColorField(colors.Body, "Border color", deck.BorderColor, c => deck.BorderColor = c, true, "theme");
            var resetRow = new WrapPanel();
            var resetCards = Theme.Button("Reset cards to theme");
            resetCards.Margin = new Thickness(0, 0, 8, 6);
            resetCards.IsEnabled = deck.CardColor.Length > 0 || deck.TextColor.Length > 0 || deck.BorderColor.Length > 0 || deck.CardOpacity < 99.999 || Math.Abs(deck.CornerRadius - AppearanceSettings.DefaultRadius) > 0.001 || deck.CardStyle != "solid" || Math.Abs(deck.TextScale - 100) > 0.001 || Math.Abs(deck.Padding - 12) > 0.001;
            resetCards.Click += (_, _) =>
            {
                deck.CardColor = deck.TextColor = deck.BorderColor = "";
                deck.CardOpacity = 100;
                deck.CornerRadius = AppearanceSettings.DefaultRadius;
                deck.CardStyle = "solid";
                deck.TextScale = 100;
                deck.Padding = 12;
                Changed();
                Rebuild();
            };
            resetRow.Children.Add(resetCards);
            var resetWidgets = Theme.Button("Reset every widget to this look");
            resetWidgets.Margin = new Thickness(0, 0, 0, 6);
            resetWidgets.IsEnabled = deck.ApplyToAll;
            resetWidgets.ToolTip = "Gives every widget this card style and clears the colors, corners, text size and spacing they were set to one by one. You can undo it.";
            resetWidgets.Click += (_, _) =>
            {
                var answer = owner is null
                    ? MessageBox.Show("Make every widget use this look? Their own card style, colors, corners, text size and spacing are cleared. You can undo this.", "Desktop Deck", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    : MessageBox.Show(owner, "Make every widget use this look? Their own card style, colors, corners, text size and spacing are cleared. You can undo this.", "Desktop Deck", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
                saveTimer.Stop();
                hub.ResetWidgetLooks();
            };
            resetRow.Children.Add(resetWidgets);
            host.Children.Add(resetRow);
            host.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 22, 0, 16) });

            // ---------- Background ----------
            host.Children.Add(Heading("Deck background"));
            var active = LayoutEngine.Active(settings, LayoutLayers.Background);
            host.Children.Add(Hint($"A color, gradient or picture for the desktop deck, or your Windows wallpaper. It belongs to the active desktop layout{(active is null ? "" : " (" + active.Name + ")")}, and each display can have its own."));
            var displays = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            var choices = new List<(string Id, string Label)> { ("", "All displays") };
            choices.AddRange(hub.Displays.Select(d => (d.Id, d.Name)));
            if (hub.Displays.Count > 1 || Look().Backgrounds.Keys.Any(k => k.Length > 0))
            {
                foreach (var (id, label) in choices)
                {
                    var b = Theme.Button(label);
                    b.Margin = new Thickness(0, 0, 6, 6);
                    b.BorderBrush = string.Equals(id, selected, StringComparison.OrdinalIgnoreCase) ? Theme.Primary : Theme.LineStrong;
                    b.Click += (_, _) => { selected = id; Rebuild(); };
                    displays.Children.Add(b);
                }
                host.Children.Add(displays);
            }
            if (selected.Length > 0 && !HasOwn())
            {
                var inherit = Look().BackgroundFor("") is null ? "the theme background" : "the All displays background";
                host.Children.Add(Hint($"This display shows {inherit}. Change anything below to give it its own."));
            }

            // With desktop icons shown the deck always draws the Windows wallpaper, so it lines up with the real desktop around the widgets.
            // The saved choice is kept untouched and comes back when icons are turned off.
            bool locked = hub.ShowDesktopIcons;
            if (locked) host.Children.Add(Hint("Desktop icons are on, so the deck uses your Windows wallpaper. Turn icons off in Settings → Display to pick another background."));
            var style = locked ? new BackgroundStyle { Kind = "wallpaper" } : Effective() ?? new BackgroundStyle();
            string[] kinds = ["theme", "solid", "gradient", "image", "wallpaper"];
            var kindTabs = Theme.Segmented(["Theme", "Color", "Gradient", "Picture", "Wallpaper"], Math.Max(0, Array.IndexOf(kinds, style.Kind)), index =>
            {
                var own = Own();
                own.Kind = kinds[index];
                Changed();
                Rebuild();
            }, 30);
            kindTabs.IsEnabled = !locked;
            if (locked) kindTabs.Opacity = 0.55;
            host.Children.Add(kindTabs);
            var detail = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            host.Children.Add(detail);
            switch (style.Kind)
            {
                case "solid":
                    ColorField(detail, "Color", style.Color, c => Own().Color = c.Length > 0 ? c : "#1A1611", false);
                    break;
                case "gradient":
                    ColorField(detail, "From", style.Color, c => Own().Color = c.Length > 0 ? c : "#1A1611", false);
                    ColorField(detail, "To", style.Color2, c => Own().Color2 = c.Length > 0 ? c : "#3A2A22", false);
                    double[] angles = [0, 45, 90, 135, 180];
                    int preset = Array.FindIndex(angles, a => Math.Abs(a - style.Angle) < 0.5);
                    var directionLabel = Theme.Label("Direction", 12, Theme.Muted);
                    directionLabel.Margin = new Thickness(0, 0, 0, 6);
                    detail.Children.Add(directionLabel);
                    detail.Children.Add(Theme.Segmented(["↑", "↗", "→", "↘", "↓"], preset, index => { Own().Angle = angles[index]; Changed(); Rebuild(); }, 26));
                    var fine = PanelKit.Collapsible(detail, "appearance.angle", "Fine-tune the angle", nested: true, open: preset < 0);
                    NumberRow(fine.Body, "Direction in degrees", style.Angle, 0, 360, v => Own().Angle = v, "0 = up, 90 = right, 180 = down");
                    break;
                case "image":
                case "wallpaper":
                    if (style.Kind == "image")
                    {
                        var path = Theme.Label(style.ImagePath.Length > 0 ? Path.GetFileName(style.ImagePath) : "No picture chosen yet", 12.5, style.ImagePath.Length > 0 ? Theme.Text2 : Theme.Muted);
                        path.Margin = new Thickness(0, 0, 0, 8);
                        detail.Children.Add(path);
                        var choose = Theme.Button(style.ImagePath.Length > 0 ? "Choose another picture…" : "Choose a picture…", style.ImagePath.Length == 0);
                        choose.HorizontalAlignment = HorizontalAlignment.Left;
                        choose.Margin = new Thickness(0, 0, 0, 14);
                        choose.Click += (_, _) =>
                        {
                            var picker = new OpenFileDialog { Title = "Choose a background picture", Filter = ItemEditor.PictureFilter, CheckFileExists = true };
                            if ((owner is null ? picker.ShowDialog() : picker.ShowDialog(owner)) != true) return;
                            try
                            {
                                var own = Own();
                                string before = own.ImagePath;
                                own.ImagePath = Appearance.ImportPicture(hub.Store, picker.FileName);
                                own.Kind = "image";
                                if (!string.Equals(before, own.ImagePath, StringComparison.OrdinalIgnoreCase)) Appearance.ReleasePicture(settings, hub.Store, before);
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowWarning(owner, "Could not copy the picture: " + ex.Message); return; }
                            Changed();
                            Rebuild();
                        };
                        detail.Children.Add(choose);
                    }
                    else
                    {
                        string wallpaper = WindowsWallpaper.For(hub.Displays.FirstOrDefault(d => string.Equals(d.Id, selected, StringComparison.OrdinalIgnoreCase)) ?? hub.PrimaryDisplay).Path;
                        detail.Children.Add(Hint(wallpaper.Length > 0 ? "Using your current Windows wallpaper (" + Path.GetFileName(wallpaper) + "). It is only read, never changed, and follows the wallpaper when you change it." : "Windows has no wallpaper picture right now (a solid color desktop), so " + (locked ? "its color shows." : "the theme color shows.")));
                    }
                    if (locked) break;
                    var fitLabel = Theme.Label("Picture size", 12, Theme.Muted);
                    fitLabel.Margin = new Thickness(0, 0, 0, 6);
                    detail.Children.Add(fitLabel);
                    string[] fits = ["fill", "fit", "tile", "center"];
                    var fitTabs = Theme.Segmented(["Fill", "Fit", "Tile", "Center"], Math.Max(0, Array.IndexOf(fits, style.Fit)), index => { Own().Fit = fits[index]; Changed(); Rebuild(); }, 28);
                    fitTabs.Margin = new Thickness(0, 0, 0, 14);
                    detail.Children.Add(fitTabs);
                    NumberRow(detail, "Dim", style.Dim, 0, 80, v => Own().Dim = v, "Darkens the picture so widgets stay readable.");
                    NumberRow(detail, "Blur", style.Blur, 0, 20, v => Own().Blur = v, "Softens the picture.");
                    break;
            }
            var resetBackground = Theme.Button(selected.Length == 0 ? "Reset to theme" : "Same as all displays");
            resetBackground.HorizontalAlignment = HorizontalAlignment.Left;
            resetBackground.IsEnabled = HasOwn() && !locked;
            resetBackground.Margin = new Thickness(0, 4, 0, 0);
            resetBackground.Click += (_, _) =>
            {
                if (Look().Backgrounds.Remove(selected, out var removed)) Appearance.ReleasePicture(settings, hub.Store, removed.ImagePath);
                Changed();
                Rebuild();
            };
            host.Children.Add(resetBackground);

        }

        Rebuild();
        return host;
    }
}
