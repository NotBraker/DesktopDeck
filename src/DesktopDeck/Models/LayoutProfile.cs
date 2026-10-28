namespace DesktopDeck.Models;

/// <summary>The two independent layers a layout can describe.</summary>
public static class LayoutLayers
{
    /// <summary>The kept-on-top widgets (<see cref="LauncherItem.OnTop"/>).</summary>
    public const string Floating = "floating";
    /// <summary>The widgets on the desktop decks, and the deck background.</summary>
    public const string Background = "background";

    public static bool IsValid(string? layer) => layer is Floating or Background;
}

/// <summary>Where one widget is, and whether it is shown, in one layout.</summary>
public sealed class LayoutEntry
{
    public bool Visible { get; set; } = true;
    /// <summary>The monitor (empty = the primary one).</summary>
    public string Display { get; set; } = "";
    /// <summary>Top-left corner as a fraction (0–1) of that monitor's work area (floating) or of its deck (background).</summary>
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Size in DIP; zero means the widget's default.</summary>
    public double Width { get; set; }
    public double Height { get; set; }

    public LayoutEntry Copy() => (LayoutEntry)MemberwiseClone();
}

/// <summary>
/// A named arrangement of one layer: which widgets are shown and where (<see cref="Entries"/>, keyed by item id), and for a background layout
/// the deck background too (<see cref="Backgrounds"/>). A widget with no entry is hidden in the layout. The active layout of each layer is
/// kept up to date automatically as you change things (see <see cref="LayoutEngine"/>).
/// </summary>
public sealed class LayoutProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Layout";
    /// <summary><see cref="LayoutLayers.Floating"/> or <see cref="LayoutLayers.Background"/>.</summary>
    public string Layer { get; set; } = LayoutLayers.Floating;
    /// <summary>An optional emoji shown on the layout's card.</summary>
    public string Icon { get; set; } = "";
    public Dictionary<string, LayoutEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Background layouts: the deck background per display (a copy of <see cref="AppearanceSettings.Backgrounds"/>).</summary>
    public Dictionary<string, BackgroundStyle> Backgrounds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public LayoutProfile Copy()
    {
        var copy = (LayoutProfile)MemberwiseClone();
        copy.Entries = new Dictionary<string, LayoutEntry>(Entries.ToDictionary(e => e.Key, e => e.Value.Copy()), StringComparer.Ordinal);
        copy.Backgrounds = new Dictionary<string, BackgroundStyle>(Backgrounds.ToDictionary(b => b.Key, b => b.Value.Copy()), StringComparer.OrdinalIgnoreCase);
        return copy;
    }
}
