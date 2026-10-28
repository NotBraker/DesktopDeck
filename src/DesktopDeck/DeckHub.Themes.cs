using System.Windows;
using System.Windows.Threading;
using DesktopDeck.Models;

namespace DesktopDeck;

/// <summary>Themes (palette, accent, font) and saved looks ("style presets"): the hub APIs the control center and Customize use.</summary>
internal sealed partial class DeckHub
{
    /// <summary>The look copied with a widget's "Copy look" menu item. Kept in memory only (it is gone when the app closes).</summary>
    internal StylePreset? CopiedLook { get; set; }

    /// <summary>The theme settings being edited. Change them, then call <see cref="ApplyTheme"/>.</summary>
    internal ThemeSettings ThemeOptions => Settings.Theme ??= new ThemeSettings();

    /// <summary>
    /// Makes the saved theme the one on screen: palette, accent and font, the menu and tooltip styles, then every surface, floating widget,
    /// the control button and the control center are redrawn with it. Call on the UI thread, and not from inside one of the control
    /// center's own click handlers (it rebuilds the control center; use <see cref="ApplyThemeSoon"/> there).
    /// </summary>
    public void ApplyTheme()
    {
        Settings.Theme = ThemeSettings.Normalized(Settings.Theme);
        Theme.Apply(Settings.Theme, Application.Current);
        Appearance.ThemeChanged();
        foreach (var surface in Surfaces) surface.RestyleChrome();
        // Floating windows keep their grips and content from when they were made: make them again.
        Floats.CloseAll();
        RenderAll();
        if (orb is not null)
        {
            orb.CloseOrb();
            orb = null;
            SyncOrb();
        }
        center?.ApplyTheme();
        Save();
    }

    /// <summary>Applies the theme a moment later, once the click that asked for it has finished.</summary>
    public void ApplyThemeSoon() => uiDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!closing) ApplyTheme(); }));

    public void SetTheme(string name)
    {
        ThemeOptions.Name = name;
        ApplyThemeSoon();
    }

    /// <summary>An accent that replaces the theme's own; empty goes back to the theme's accent.</summary>
    public void SetThemeAccent(string hex)
    {
        ThemeOptions.Accent = HexColor.Clean(hex);
        ApplyThemeSoon();
    }

    public void SetThemeFont(string font)
    {
        ThemeOptions.Font = font;
        ApplyThemeSoon();
    }

    /// <summary>Saves the custom colors and switches to the custom theme.</summary>
    public void SetCustomTheme(CustomPalette colors)
    {
        var theme = ThemeOptions;
        theme.Custom = CustomPalette.Normalized(colors.Copy());
        theme.Name = ThemeSettings.IdCustom;
        // The custom accent is the theme's own accent, so it replaces a separate accent override.
        theme.Accent = "";
        ApplyThemeSoon();
    }

    // ---------- Style presets ----------

    public StylePresetSettings Presets => Settings.StylePresets ??= new StylePresetSettings();

    /// <summary>Saves the look of <paramref name="source"/> as a new preset (null when the list is full).</summary>
    public StylePreset? SavePreset(LauncherItem source, string name)
    {
        var made = Presets.Add(source, name);
        if (made is null) { SetStatus($"You can keep up to {StylePresetSettings.MaxPresets} looks. Delete one in the control center → Appearance first."); return null; }
        Save();
        StateChanged?.Invoke(StateChange.Items);
        SetStatus($"Saved the look \"{made.Name}\".");
        return made;
    }

    public void RenamePreset(string id, string name)
    {
        if (!Presets.Rename(id, name)) return;
        Save();
        StateChanged?.Invoke(StateChange.Items);
    }

    public void DeletePreset(string id)
    {
        if (!Presets.Delete(id)) return;
        Save();
        StateChanged?.Invoke(StateChange.Items);
    }
}
