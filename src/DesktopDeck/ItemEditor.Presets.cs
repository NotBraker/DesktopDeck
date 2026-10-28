using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>Customize → Looks: apply a saved look to the fields of the editor, or save the fields as a new look.</summary>
internal sealed partial class ItemEditor
{
    private readonly DeckSettings? deckSettings;
    /// <summary>The boxes of the "_" appearance options by key, so a preset can fill them (Save reads the boxes, not the draft).</summary>
    private readonly Dictionary<string, TextBox> lookBoxes = new(StringComparer.Ordinal);
    private Action showStyle = () => { };
    private WrapPanel? presetChips;
    private TextBlock? presetHeading;

    /// <summary>Called after a look was saved, so the owner can write the settings to disk.</summary>
    internal Action? PresetsChanged { get; init; }

    private void AddPresetRow(Panel root)
    {
        if (deckSettings is null || draft.Kind == ItemKinds.Image) return;
        presetHeading = Theme.WidgetTitle("Saved looks", Theme.Primary);
        presetHeading.Margin = new Thickness(0, 10, 0, 8);
        root.Children.Add(presetHeading);
        presetChips = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        root.Children.Add(presetChips);
        var save = Theme.Button("Save this look as a preset…");
        save.ToolTip = "Keep the card style, colors, corners and accent as a look you can click on any widget. Clicking a look only fills in the fields; Save keeps it.";
        save.HorizontalAlignment = HorizontalAlignment.Left;
        save.Margin = new Thickness(0, 4, 0, 14);
        save.Click += (_, _) => SaveLook();
        root.Children.Add(save);
        ShowPresetChips();
    }

    private void ShowPresetChips()
    {
        if (presetChips is null || deckSettings is null) return;
        presetChips.Children.Clear();
        var presets = deckSettings.StylePresets.Items;
        // Nothing to pick yet: just the button, no heading and no instructions.
        presetChips.Visibility = presets.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (presetHeading is not null) presetHeading.Visibility = presetChips.Visibility;
        if (presets.Count == 0) return;
        foreach (var preset in presets.ToList())
        {
            var chip = Theme.Button(preset.Name);
            chip.Margin = new Thickness(0, 0, 6, 6);
            chip.ToolTip = $"{preset.CardStyle} card" + (preset.Accent.Length > 0 ? ", accent " + preset.Accent : "");
            chip.Click += (_, _) => ApplyLook(preset);
            presetChips.Children.Add(chip);
        }
    }

    /// <summary>Fills the editor with a look. Options the look does not have are cleared (they follow the deck default again).</summary>
    private void ApplyLook(StylePreset preset)
    {
        foreach (var (key, box) in lookBoxes) box.Text = preset.Options.GetValueOrDefault(key, "");
        draft.CardStyle = ItemKinds.CardStyles.Contains(preset.CardStyle) ? preset.CardStyle : "solid";
        showStyle();
        if (preset.Accent.Length > 0) accentBox.Text = preset.Accent;
        validation.Text = "";
    }

    private void SaveLook()
    {
        if (deckSettings is null) return;
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (spec, read) in pluginFields.Where(f => StylePreset.IsLookKey(f.Spec.Key)).ToList())
        {
            // A field with a mistake in it has already said so.
            if (read() is not { } value) return;
            if (value.Length > 0) options[spec.Key] = value;
        }
        string accent = accentBox.Text.Trim();
        try { Theme.Brush(accent); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { Fail("Enter a valid accent color before saving the look.", accentBox); return; }
        if (InputDialog.Ask(this, "Save this look", "Name for this look", "", "Save") is not { } name) return;
        var preset = deckSettings.StylePresets.Add(new StylePreset { Name = name, CardStyle = draft.CardStyle, Accent = accent, Options = options });
        if (preset is null) { validation.Text = $"You can keep up to {StylePresetSettings.MaxPresets} looks. Delete one in the control center → Appearance."; return; }
        PresetsChanged?.Invoke();
        ShowPresetChips();
        validation.Text = $"Saved the look \"{preset.Name}\".";
    }
}
