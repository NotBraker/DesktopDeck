namespace DesktopDeck.Models;

/// <summary>
/// The rules for layouts, with no UI in them. A layout describes one layer: the floating widgets (items with <see cref="LauncherItem.OnTop"/>)
/// or the background (every other item, plus the deck background). The active layout of each layer is "live": <see cref="CaptureActive"/>
/// (called by <c>SettingsStore.Save</c>, so every save point is covered) writes the current state of the layer into it. Switching writes the
/// current state into the old layout, then applies the new one: an item of the layer that the layout has no entry for becomes hidden.
/// </summary>
public static class LayoutEngine
{
    public const int MaxNameLength = 40;

    /// <summary>True when the item belongs to the layer: floating widgets are the kept-on-top ones, everything else is on the background.</summary>
    public static bool InLayer(LauncherItem item, string layer) => layer == LayoutLayers.Floating ? item.OnTop : !item.OnTop;

    public static IEnumerable<LayoutProfile> Of(DeckSettings settings, string layer) => settings.Layouts.Where(l => l.Layer == layer);

    public static LayoutProfile? Find(DeckSettings settings, string? id) => id is null ? null : settings.Layouts.FirstOrDefault(l => l.Id == id);

    public static string ActiveId(DeckSettings settings, string layer) => layer == LayoutLayers.Floating ? settings.ActiveFloatingLayout : settings.ActiveBackgroundLayout;

    public static LayoutProfile? Active(DeckSettings settings, string layer) => Find(settings, ActiveId(settings, layer)) is { } found && found.Layer == layer ? found : null;

    private static void SetActive(DeckSettings settings, string layer, string id)
    {
        if (layer == LayoutLayers.Floating) settings.ActiveFloatingLayout = id; else settings.ActiveBackgroundLayout = id;
    }

    // ---------- Repair and first-load defaults ----------

    /// <summary>
    /// Repairs loaded layouts (a hand-edited or older file) and makes sure each layer has one and one is active. A layer with no layout gets
    /// a "Default" made from the current state, so upgrading changes nothing you can see.
    /// </summary>
    public static void EnsureDefaults(DeckSettings settings)
    {
        var cleaned = new List<LayoutProfile>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layout in settings.Layouts ?? new())
        {
            if (layout is null || !LayoutLayers.IsValid(layout.Layer)) continue;
            if (string.IsNullOrWhiteSpace(layout.Id) || !ids.Add(layout.Id)) { layout.Id = Guid.NewGuid().ToString("N"); ids.Add(layout.Id); }
            layout.Name = string.IsNullOrWhiteSpace(layout.Name) ? "Layout" : Trim(layout.Name);
            layout.Icon = layout.Icon is { Length: > 0 } icon && icon.Length <= 8 ? icon : "";
            var entries = new Dictionary<string, LayoutEntry>(StringComparer.Ordinal);
            foreach (var (key, entry) in layout.Entries ?? new())
            {
                if (entry is null || string.IsNullOrWhiteSpace(key)) continue;
                entry.Display ??= "";
                entry.X = double.IsFinite(entry.X) ? Math.Clamp(entry.X, 0, 1) : 0;
                entry.Y = double.IsFinite(entry.Y) ? Math.Clamp(entry.Y, 0, 1) : 0;
                entry.Width = double.IsFinite(entry.Width) && entry.Width > 0 ? entry.Width : 0;
                entry.Height = double.IsFinite(entry.Height) && entry.Height > 0 ? entry.Height : 0;
                entries[key] = entry;
            }
            layout.Entries = entries;
            var backgrounds = new Dictionary<string, BackgroundStyle>(StringComparer.OrdinalIgnoreCase);
            if (layout.Layer == LayoutLayers.Background)
                foreach (var (key, style) in layout.Backgrounds ?? new())
                {
                    if (style is null) continue;
                    style.Normalize();
                    backgrounds[(key ?? "").Trim()] = style;
                }
            layout.Backgrounds = backgrounds;
            cleaned.Add(layout);
        }
        settings.Layouts = cleaned;
        settings.ActiveFloatingLayout ??= "";
        settings.ActiveBackgroundLayout ??= "";
        foreach (var layer in new[] { LayoutLayers.Floating, LayoutLayers.Background })
        {
            if (!Of(settings, layer).Any())
            {
                var made = new LayoutProfile { Name = "Default", Layer = layer };
                settings.Layouts.Add(made);
                Capture(settings, made);
                SetActive(settings, layer, made.Id);
            }
            else if (Active(settings, layer) is null) SetActive(settings, layer, Of(settings, layer).First().Id);
        }
        Prune(settings);
    }

    private static string Trim(string name)
    {
        name = name.Trim();
        return name.Length > MaxNameLength ? name[..MaxNameLength].TrimEnd() : name;
    }

    /// <summary>Drops the entries of items that no longer exist, in every layout (removing a widget removes it from all layouts).</summary>
    public static void Prune(DeckSettings settings)
    {
        var ids = new HashSet<string>(settings.Items.Select(i => i.Id), StringComparer.Ordinal);
        foreach (var layout in settings.Layouts)
            foreach (var key in layout.Entries.Keys.Where(k => !ids.Contains(k)).ToList()) layout.Entries.Remove(key);
    }

    /// <summary>A widget was removed: forget it in every layout.</summary>
    public static void Forget(DeckSettings settings, string itemId)
    {
        foreach (var layout in settings.Layouts) layout.Entries.Remove(itemId);
    }

    // ---------- Live capture ----------

    /// <summary>Writes the current state of the layer into a layout: every item of the layer gets an entry (shown or hidden, with its place); a background layout also copies the deck background.</summary>
    public static void Capture(DeckSettings settings, LayoutProfile layout)
    {
        foreach (var item in settings.Items.Where(i => InLayer(i, layout.Layer)))
        {
            if (!layout.Entries.TryGetValue(item.Id, out var entry)) layout.Entries[item.Id] = entry = new LayoutEntry();
            entry.Visible = !item.Hidden;
            if (layout.Layer == LayoutLayers.Floating)
            {
                entry.Display = item.FloatDisplay; entry.X = item.FloatX; entry.Y = item.FloatY; entry.Width = item.FloatWidth; entry.Height = item.FloatHeight;
            }
            else
            {
                entry.Display = item.Display; entry.X = item.X; entry.Y = item.Y; entry.Width = item.Width; entry.Height = item.Height;
            }
        }
        if (layout.Layer == LayoutLayers.Background) layout.Backgrounds = CopyBackgrounds(settings.Appearance.Backgrounds);
    }

    /// <summary>Keeps both active layouts equal to what is on screen. Cheap; called before every save.</summary>
    public static void CaptureActive(DeckSettings settings)
    {
        Prune(settings);
        foreach (var layer in new[] { LayoutLayers.Floating, LayoutLayers.Background })
            if (Active(settings, layer) is { } active) Capture(settings, active);
    }

    private static Dictionary<string, BackgroundStyle> CopyBackgrounds(Dictionary<string, BackgroundStyle> source)
        => new(source.ToDictionary(b => b.Key, b => b.Value.Copy()), StringComparer.OrdinalIgnoreCase);

    // ---------- Applying and switching ----------

    /// <summary>Makes the layer look like the layout: items it lists get its place and visibility, items of the layer it doesn't list are hidden; a background layout also swaps the deck background.</summary>
    public static void Apply(DeckSettings settings, LayoutProfile layout)
    {
        foreach (var item in settings.Items.Where(i => InLayer(i, layout.Layer)))
        {
            if (!layout.Entries.TryGetValue(item.Id, out var entry)) { item.Hidden = true; continue; }
            item.Hidden = !entry.Visible;
            if (layout.Layer == LayoutLayers.Floating)
            {
                item.FloatDisplay = entry.Display; item.FloatX = entry.X; item.FloatY = entry.Y; item.FloatWidth = entry.Width; item.FloatHeight = entry.Height;
            }
            else
            {
                item.Display = entry.Display; item.X = entry.X; item.Y = entry.Y; item.Width = entry.Width; item.Height = entry.Height;
            }
        }
        if (layout.Layer == LayoutLayers.Background) settings.Appearance.Backgrounds = CopyBackgrounds(layout.Backgrounds);
    }

    /// <summary>Switches a layer to another layout: the old one is brought up to date first. False when it is already active or does not exist.</summary>
    public static bool Switch(DeckSettings settings, string layer, string id)
    {
        if (Find(settings, id) is not { } target || target.Layer != layer || ActiveId(settings, layer) == id) return false;
        CaptureActive(settings);
        Apply(settings, target);
        SetActive(settings, layer, id);
        return true;
    }

    // ---------- Managing layouts ----------

    /// <summary>"New layout from current": the layer as it is now, saved under a name, and made the active layout.</summary>
    public static LayoutProfile CreateFromCurrent(DeckSettings settings, string layer, string? name)
    {
        CaptureActive(settings);
        var made = new LayoutProfile { Layer = layer, Name = UniqueName(settings, layer, string.IsNullOrWhiteSpace(name) ? "New layout" : Trim(name), null) };
        Capture(settings, made);
        settings.Layouts.Add(made);
        SetActive(settings, layer, made.Id);
        return made;
    }

    public static LayoutProfile? Duplicate(DeckSettings settings, string id)
    {
        if (Find(settings, id) is not { } source) return null;
        CaptureActive(settings);
        var copy = source.Copy();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = UniqueName(settings, source.Layer, Trim(source.Name + " copy"), null);
        settings.Layouts.Insert(settings.Layouts.IndexOf(source) + 1, copy);
        return copy;
    }

    public static bool Rename(DeckSettings settings, string id, string name)
    {
        if (Find(settings, id) is not { } layout || string.IsNullOrWhiteSpace(name)) return false;
        layout.Name = UniqueName(settings, layout.Layer, Trim(name), layout.Id);
        return true;
    }

    public static bool SetIcon(DeckSettings settings, string id, string icon)
    {
        if (Find(settings, id) is not { } layout) return false;
        icon = (icon ?? "").Trim();
        layout.Icon = icon.Length <= 8 ? icon : "";
        return true;
    }

    /// <summary>Deletes a layout (never the last one of its layer). When it was the active one, the next layout is applied and becomes active. Returns the deleted layout, or null.</summary>
    public static LayoutProfile? Delete(DeckSettings settings, string id)
    {
        if (Find(settings, id) is not { } layout) return null;
        var siblings = Of(settings, layout.Layer).ToList();
        if (siblings.Count <= 1) return null;
        bool wasActive = ActiveId(settings, layout.Layer) == id;
        if (!wasActive) CaptureActive(settings);
        int at = siblings.IndexOf(layout);
        settings.Layouts.Remove(layout);
        if (wasActive)
        {
            var next = siblings[at + 1 < siblings.Count ? at + 1 : at - 1];
            Apply(settings, next);
            SetActive(settings, layout.Layer, next.Id);
        }
        return layout;
    }

    /// <summary>Moves a layout up (-1) or down (+1) among the layouts of its layer; the order is the quick-switch order.</summary>
    public static bool Move(DeckSettings settings, string id, int delta)
    {
        if (Find(settings, id) is not { } layout) return false;
        var siblings = Of(settings, layout.Layer).ToList();
        int at = siblings.IndexOf(layout), to = at + Math.Sign(delta);
        if (to < 0 || to >= siblings.Count) return false;
        int a = settings.Layouts.IndexOf(layout), b = settings.Layouts.IndexOf(siblings[to]);
        (settings.Layouts[a], settings.Layouts[b]) = (settings.Layouts[b], settings.Layouts[a]);
        return true;
    }

    private static string UniqueName(DeckSettings settings, string layer, string name, string? exceptId)
    {
        var taken = new HashSet<string>(Of(settings, layer).Where(l => l.Id != exceptId).Select(l => l.Name), StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(name)) return name;
        for (int n = 2; ; n++)
        {
            string candidate = $"{(name.Length > MaxNameLength - 4 ? name[..(MaxNameLength - 4)] : name)} {n}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    /// <summary>How many widgets a layout shows (entries that are visible, for widgets that exist and are in the layout's layer).</summary>
    public static int VisibleCount(DeckSettings settings, LayoutProfile layout)
    {
        // Widgets inside a group card are tabs of it, not widgets of their own.
        var inLayer = settings.Items.Where(i => InLayer(i, layout.Layer) && i.GroupId.Length == 0).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        if (ActiveId(settings, layout.Layer) == layout.Id) return settings.Items.Count(i => InLayer(i, layout.Layer) && i.GroupId.Length == 0 && !i.Hidden);
        return layout.Entries.Count(e => e.Value.Visible && inLayer.Contains(e.Key));
    }

    // ---------- Pictures ----------

    /// <summary>
    /// True when a background picture is still used by the current deck background or by a background layout other than the active one
    /// (the active layout only mirrors the current background, and lags it until the next save).
    /// </summary>
    public static bool PictureInUse(DeckSettings settings, string path)
    {
        bool Same(BackgroundStyle b) => string.Equals(b.ImagePath, path, StringComparison.OrdinalIgnoreCase);
        if (settings.Appearance.Backgrounds.Values.Any(Same)) return true;
        return settings.Layouts.Any(l => l.Layer == LayoutLayers.Background && l.Id != settings.ActiveBackgroundLayout && l.Backgrounds.Values.Any(Same));
    }
}
