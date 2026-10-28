namespace DesktopDeck.Models;

/// <summary>A saved widget look: the "_"-prefixed appearance options (card color, opacity, corners, border, text, spacing), the card style and the accent.</summary>
public sealed class StylePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>"solid", "glass", "frosted", "outline" or "none".</summary>
    public string CardStyle { get; set; } = "solid";
    /// <summary>The accent color (hex); empty = leave the widget's accent alone.</summary>
    public string Accent { get; set; } = "";
    /// <summary>Appearance options by key (every key starts with "_"). A key that is missing means "the deck default" and clears it when applied.</summary>
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.Ordinal);

    public StylePreset Copy() => new() { Id = Id, Name = Name, CardStyle = CardStyle, Accent = Accent, Options = new Dictionary<string, string>(Options, StringComparer.Ordinal) };

    /// <summary>Takes the look of an item: its appearance options, card style and accent.</summary>
    public static StylePreset From(LauncherItem item, string name) => new()
    {
        Name = name,
        CardStyle = item.CardStyle,
        Accent = item.Accent,
        Options = item.Options.Where(o => IsLookKey(o.Key) && !string.IsNullOrWhiteSpace(o.Value)).ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal),
    };

    public static bool IsLookKey(string? key) => key is { Length: > 1 } && key[0] == '_';

    /// <summary>Gives an item this look: its own appearance options are replaced by the preset's.</summary>
    public void ApplyTo(LauncherItem item)
    {
        foreach (var key in item.Options.Keys.Where(IsLookKey).ToList()) item.Options.Remove(key);
        foreach (var (key, value) in Options) item.Options[key] = value;
        item.CardStyle = ItemKinds.CardStyles.Contains(CardStyle) ? CardStyle : "solid";
        if (Accent.Length > 0) item.Accent = Accent;
    }
}

/// <summary>The saved looks (Customize → "Save this look as a preset…"). Managed from the control center's Appearance tab.</summary>
public sealed class StylePresetSettings
{
    public const int MaxPresets = 40, MaxNameLength = 40, MaxOptionLength = 64;

    public List<StylePreset> Items { get; set; } = new();

    public StylePreset? Find(string? id) => Items.FirstOrDefault(p => p.Id == id);

    /// <summary>A name that no other preset uses ("Warm", "Warm 2", …).</summary>
    public string UniqueName(string wanted, string? exceptId = null)
    {
        string baseName = CleanName(wanted);
        if (baseName.Length == 0) baseName = "My look";
        string name = baseName;
        for (int n = 2; Items.Any(p => p.Id != exceptId && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)); n++) name = $"{baseName} {n}";
        return name;
    }

    public static string CleanName(string? name)
    {
        string text = string.Join(" ", (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > MaxNameLength ? text[..MaxNameLength].TrimEnd() : text;
    }

    public StylePreset? Add(LauncherItem from, string name) => Add(StylePreset.From(from, name));

    /// <summary>Adds a preset under a name no other one has; null when the list is full.</summary>
    public StylePreset? Add(StylePreset preset)
    {
        if (Items.Count >= MaxPresets) return null;
        preset.Name = UniqueName(preset.Name);
        preset.Accent = HexColor.Clean(preset.Accent);
        if (!ItemKinds.CardStyles.Contains(preset.CardStyle)) preset.CardStyle = "solid";
        if (Items.Any(p => p.Id == preset.Id)) preset.Id = Guid.NewGuid().ToString("N");
        Items.Add(preset);
        return preset;
    }

    public bool Rename(string id, string name)
    {
        if (Find(id) is not { } preset || CleanName(name).Length == 0) return false;
        preset.Name = UniqueName(name, id);
        return true;
    }

    public bool Delete(string id) => Items.RemoveAll(p => p.Id == id) > 0;

    /// <summary>Repairs a loaded (possibly hand-edited or null) block: no nulls, unique ids and names, valid colors and styles, bounded sizes.</summary>
    public static StylePresetSettings Normalized(StylePresetSettings? value)
    {
        var s = value ?? new StylePresetSettings();
        var cleaned = new List<StylePreset>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in (s.Items ?? new()).Where(p => p is not null))
        {
            if (cleaned.Count >= MaxPresets) break;
            if (string.IsNullOrWhiteSpace(preset.Id) || !ids.Add(preset.Id)) { preset.Id = Guid.NewGuid().ToString("N"); ids.Add(preset.Id); }
            preset.Name = CleanName(preset.Name);
            preset.CardStyle = (preset.CardStyle ?? "").Trim().ToLowerInvariant();
            if (!ItemKinds.CardStyles.Contains(preset.CardStyle)) preset.CardStyle = "solid";
            preset.Accent = HexColor.Clean(preset.Accent);
            var options = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, text) in preset.Options ?? new())
            {
                if (!StylePreset.IsLookKey(key) || string.IsNullOrWhiteSpace(text) || key.Length > MaxOptionLength) continue;
                options[key] = text.Trim().Length > MaxOptionLength ? text.Trim()[..MaxOptionLength] : text.Trim();
            }
            preset.Options = options;
            cleaned.Add(preset);
        }
        // Names are what people see: give blank ones a name and make the rest unique.
        s.Items = new List<StylePreset>();
        foreach (var preset in cleaned)
        {
            preset.Name = s.UniqueName(preset.Name.Length == 0 ? "My look" : preset.Name);
            s.Items.Add(preset);
        }
        return s;
    }
}
