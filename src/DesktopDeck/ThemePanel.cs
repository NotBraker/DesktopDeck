using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>
/// The control center's Appearance tab, top sections: theme cards with small previews, the accent color, the font, the custom palette
/// editor, and the saved widget looks (presets). Everything applies through <see cref="DeckHub"/>, which redraws the whole app.
/// </summary>
internal static class ThemePanel
{
    private static readonly string[] AccentChoices = ["#C4684F", "#B5523A", "#D98A5B", "#C2A14D", "#9B9A5E", "#5E9BA6", "#5B8DEF", "#6C63FF", "#8B5CF6", "#EC4899", "#14B8A6", "#22C55E", "#FFD400"];

    // ---------- Theme, accent, font, custom colors ----------

    internal static void FillThemes(Panel body, DeckHub hub)
    {
        var theme = hub.ThemeOptions;

        // Theme cards.
        var grid = new UniformGrid { Columns = ThemeSettings.Names.Length, Margin = new Thickness(0, 0, -PanelKit.S8, PanelKit.S16) };
        foreach (var id in ThemeSettings.Names)
        {
            var preview = ThemePalette.For(new ThemeSettings { Name = id, Accent = theme.Accent, Custom = theme.Custom });
            grid.Children.Add(ThemeCard(preview, id, theme.Name == id, () => hub.SetTheme(id)));
        }
        body.Children.Add(grid);

        // Accent.
        body.Children.Add(Caption("Accent color", theme.Accent.Length == 0 ? "Using the theme's own accent." : "Replaces the theme's accent everywhere."));
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, PanelKit.S8) };
        chips.Children.Add(AccentChip(null, ThemePalette.For(new ThemeSettings { Name = theme.Name, Custom = theme.Custom }).Primary, theme.Accent.Length == 0, () => hub.SetThemeAccent("")));
        foreach (var color in AccentChoices)
            chips.Children.Add(AccentChip(color, color, string.Equals(theme.Accent, color, StringComparison.OrdinalIgnoreCase), () => hub.SetThemeAccent(color)));
        body.Children.Add(chips);
        var moreColors = PanelKit.Collapsible(body, "appearance.accent", "More colors", nested: true, open: theme.Accent.Length > 0 && !AccentChoices.Contains(theme.Accent, StringComparer.OrdinalIgnoreCase));
        var accentBox = Theme.TextField(theme.Accent);
        accentBox.Width = 150;
        accentBox.HorizontalAlignment = HorizontalAlignment.Left;
        accentBox.ToolTip = "A color such as #5B8DEF. Press Enter to apply, or empty to use the theme's accent.";
        void ApplyAccent()
        {
            string text = accentBox.Text.Trim();
            string clean = HexColor.Clean(text);
            bool ok = text.Length == 0 || clean.Length > 0;
            accentBox.BorderBrush = ok ? Theme.LineStrong : Theme.Danger;
            if (ok && !string.Equals(clean, theme.Accent, StringComparison.OrdinalIgnoreCase)) hub.SetThemeAccent(clean);
        }
        accentBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyAccent(); e.Handled = true; } };
        accentBox.LostKeyboardFocus += (_, _) => ApplyAccent();
        var accentRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, PanelKit.S8) };
        accentRow.Children.Add(accentBox);
        var accentHint = Theme.Label("  or type a hex color", 12, Theme.Muted);
        accentRow.Children.Add(accentHint);
        moreColors.Body.Children.Add(accentRow);

        // Font.
        body.Children.Add(Caption("Font", "Used for the whole deck, menus and the control center."));
        var fonts = new WrapPanel { Margin = new Thickness(0, 0, 0, PanelKit.S8) };
        foreach (var (id, label, family) in ThemeSettings.Fonts)
        {
            bool on = theme.Font == id;
            var button = Theme.Button(label);
            button.FontFamily = new FontFamily(family);
            button.FontSize = 13;
            button.Margin = new Thickness(0, 0, 8, 8);
            button.BorderBrush = on ? Theme.Primary : Theme.LineStrong;
            button.Background = on ? Theme.Tint(Theme.PrimaryHex, 0.18) : Theme.Surface;
            button.Click += (_, _) => hub.SetThemeFont(id);
            fonts.Children.Add(button);
        }
        body.Children.Add(fonts);

        if (theme.Name == ThemeSettings.IdCustom) FillCustom(body, hub);
    }

    private static void FillCustom(Panel body, DeckHub hub)
    {
        body.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, PanelKit.S8, 0, PanelKit.S16) });
        body.Children.Add(Caption("Custom colors", "Type hex colors, then press Apply. The rest (hover, lines, secondary text) is worked out from these."));
        var draft = hub.ThemeOptions.Custom.Copy();
        var boxes = new Dictionary<string, TextBox>();
        var warning = Theme.Label("", 12, Theme.Danger);
        warning.TextWrapping = TextWrapping.Wrap;
        warning.Margin = new Thickness(0, 0, 0, PanelKit.S8);

        CustomPalette Read(out bool ok)
        {
            var read = draft.Copy();
            ok = true;
            foreach (var (name, box) in boxes)
            {
                string clean = HexColor.Clean(box.Text);
                bool valid = clean.Length > 0;
                box.BorderBrush = valid ? Theme.LineStrong : Theme.Danger;
                if (!valid) { ok = false; continue; }
                switch (name)
                {
                    case "Background": read.Background = clean; break;
                    case "Surface": read.Surface = clean; break;
                    case "Text": read.Text = clean; break;
                    case "Muted": read.Muted = clean; break;
                    case "Accent": read.Accent = clean; break;
                }
            }
            return read;
        }

        void Check()
        {
            var read = Read(out bool ok);
            if (!ok) { warning.Text = "Use colors like #1A1611."; return; }
            double text = HexColor.Contrast(read.Text, read.Background), card = HexColor.Contrast(read.Text, read.Surface);
            warning.Text = Math.Min(text, card) < 4.5 ? "The text may be hard to read on these backgrounds. Make the text lighter or darker." : "";
        }

        void Row(string name, string key, string value)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, PanelKit.S8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            row.Children.Add(Theme.Label(name, 13));
            var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), Background = Theme.Brush(value), Margin = new Thickness(0, 0, 10, 0) };
            Grid.SetColumn(swatch, 1);
            row.Children.Add(swatch);
            var box = Theme.TextField(value);
            box.TextChanged += (_, _) =>
            {
                if (HexColor.Clean(box.Text) is { Length: > 0 } clean) swatch.Background = Theme.Brush(clean);
                Check();
            };
            Grid.SetColumn(box, 2);
            row.Children.Add(box);
            boxes[key] = box;
            body.Children.Add(row);
        }
        Row("Background", "Background", draft.Background);
        Row("Surface", "Surface", draft.Surface);
        Row("Text", "Text", draft.Text);
        Row("Muted text", "Muted", draft.Muted);
        Row("Accent", "Accent", draft.Accent);
        body.Children.Add(warning);

        var buttons = new WrapPanel();
        var apply = Theme.Button("Apply", true);
        apply.Margin = new Thickness(0, 0, 8, 8);
        apply.Click += (_, _) =>
        {
            var read = Read(out bool ok);
            if (!ok) { warning.Text = "Use colors like #1A1611."; return; }
            hub.SetCustomTheme(read);
        };
        buttons.Children.Add(apply);
        foreach (var from in new[] { ThemePalette.Light, ThemePalette.Dusk, ThemePalette.Midnight })
        {
            var start = ThemePalette.FromCustom(new CustomPalette { Background = from.Background, Surface = from.Surface, Text = from.Text, Muted = from.Muted, Accent = from.Primary });
            var button = Theme.Button("Start from " + from.Label);
            button.Margin = new Thickness(0, 0, 8, 8);
            button.Click += (_, _) =>
            {
                boxes["Background"].Text = start.Background; boxes["Surface"].Text = start.Surface; boxes["Text"].Text = start.Text;
                boxes["Muted"].Text = start.Muted; boxes["Accent"].Text = start.Primary;
            };
            buttons.Children.Add(button);
        }
        body.Children.Add(buttons);
    }

    private static UIElement Caption(string title, string note)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, PanelKit.S8) };
        var heading = Theme.Label(title, 13.5);
        heading.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(heading);
        var hint = Theme.Label(note, 12, Theme.Muted);
        hint.TextWrapping = TextWrapping.Wrap;
        hint.Margin = new Thickness(0, 2, 0, 0);
        stack.Children.Add(hint);
        return stack;
    }

    /// <summary>A round accent swatch; <paramref name="hex"/> is what it applies (null = the theme's own, drawn with a small slash).</summary>
    private static Border AccentChip(string? hex, string shown, bool selected, Action choose)
    {
        var chip = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 0, 8, 8), Background = Theme.Brush(shown),
            BorderBrush = selected ? Theme.Text : Theme.LineStrong, BorderThickness = new Thickness(selected ? 2.5 : 1), Cursor = Cursors.Hand,
            ToolTip = hex ?? "The theme's own accent",
        };
        if (hex is null) chip.Child = Theme.Icon("", 12, Brushes.White);
        else if (selected) chip.Child = Theme.Icon("", 12, Brushes.White);
        PanelKit.MakeFocusable(chip, choose, hex is null ? "Theme accent" : "Accent " + hex);
        chip.LostKeyboardFocus += (_, _) => chip.BorderBrush = selected ? Theme.Text : Theme.LineStrong;
        chip.MouseLeftButtonUp += (_, e) => { choose(); e.Handled = true; };
        return chip;
    }

    /// <summary>A theme card: a small drawing of the palette (cards, text lines and an accent button on its background) and its name.</summary>
    private static Border ThemeCard(ThemePalette palette, string id, bool selected, Action choose)
    {
        Brush B(string hex) => Theme.Brush(hex);
        var line = B(palette.LineStrong);
        var mini = new Border { Background = B(palette.Background), CornerRadius = new CornerRadius(8), Height = 74, Padding = new Thickness(8), BorderBrush = line, BorderThickness = new Thickness(1) };
        var content = new StackPanel();
        var card = new Border { Background = B(palette.Surface), BorderBrush = B(palette.Line), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(6, 5, 6, 5) };
        var lines = new StackPanel();
        lines.Children.Add(new Border { Height = 4, Width = 38, CornerRadius = new CornerRadius(2), Background = B(palette.Text), HorizontalAlignment = HorizontalAlignment.Left });
        lines.Children.Add(new Border { Height = 3, Width = 26, CornerRadius = new CornerRadius(2), Background = B(palette.Muted), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) });
        card.Child = lines;
        content.Children.Add(card);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        row.Children.Add(new Border { Height = 10, Width = 30, CornerRadius = new CornerRadius(5), Background = B(palette.Primary), Margin = new Thickness(0, 0, 5, 0) });
        row.Children.Add(new Border { Height = 10, Width = 18, CornerRadius = new CornerRadius(5), Background = B(palette.Surface3) });
        content.Children.Add(row);
        mini.Child = content;

        var name = Theme.Label(palette.Label, 12.5);
        name.FontWeight = FontWeights.SemiBold;
        name.Margin = new Thickness(2, 8, 0, 0);
        var stack = new StackPanel();
        stack.Children.Add(mini);
        stack.Children.Add(name);
        var outer = PanelKit.HoverCard(stack, selected, 12);
        outer.Margin = new Thickness(0, 0, PanelKit.S8, 0);
        outer.Padding = new Thickness(6, 6, 6, 8);
        outer.Cursor = Cursors.Hand;
        PanelKit.MakeFocusable(outer, choose, palette.Label + " theme");
        outer.LostKeyboardFocus += (_, _) => outer.BorderBrush = selected ? Theme.Primary : Theme.Line;
        outer.MouseLeftButtonUp += (_, e) => { choose(); e.Handled = true; };
        return outer;
    }

    // ---------- Saved looks ----------

    /// <summary>The list of saved looks with Rename and Delete. <paramref name="rebuild"/> redraws the tab afterwards.</summary>
    internal static void FillPresets(Panel body, DeckHub hub, Window owner, Action rebuild)
    {
        var presets = hub.Presets.Items;
        if (presets.Count == 0)
        {
            body.Children.Add(PanelKit.Hint("No saved looks yet. Press \"Save this look as a preset…\" in a widget's Customize window to keep one."));
            return;
        }
        foreach (var preset in presets.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, PanelKit.S8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var swatch = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 12, 0), BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1) };
            swatch.Background = preset.Options.TryGetValue("_cardColor", out var card) && Appearance.ParseColor(card) is { } c ? Appearance.Solid(c) : Theme.Surface3;
            swatch.Child = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Theme.Accent(preset.Accent.Length > 0 ? preset.Accent : null), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(swatch);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = Theme.Label(preset.Name, 13.5);
            name.FontWeight = FontWeights.SemiBold;
            text.Children.Add(name);
            string style = preset.CardStyle switch { "glass" => "See-through card", "frosted" => "Frosted glass card", "outline" => "Outline card", "none" => "No card", _ => "Solid card" };
            int extras = preset.Options.Count;
            text.Children.Add(Theme.Label(style + (extras > 0 ? $" · {extras} custom setting{(extras == 1 ? "" : "s")}" : ""), 12, Theme.Muted));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string id = preset.Id;
            actions.Children.Add(PanelKit.Quiet("Rename", () =>
            {
                if (InputDialog.Ask(owner, "Rename look", "Name", preset.Name, "Rename") is not { } renamed) return;
                hub.RenamePreset(id, renamed);
                rebuild();
            }, glyph: Theme.Glyphs.Edit));
            actions.Children.Add(PanelKit.Quiet("Delete", () =>
            {
                if (MessageBox.Show(owner, $"Delete the look \"{preset.Name}\"? Widgets that already use it keep their settings.", "Delete look", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                hub.DeletePreset(id);
                rebuild();
            }, danger: true, glyph: Theme.Glyphs.Delete));
            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            body.Children.Add(row);
        }
    }
}
