using System;
using System.Collections.Generic;
using System.Linq;
using DesktopDeck.Layout;
using DesktopDeck.Models;

namespace DesktopDeck;

// Editing helpers shared by every surface: the grid, hiding several widgets at once, and the live preview of Customize.
internal sealed partial class DeckHub
{
    // ---------- The grid (Control center → Settings) ----------

    /// <summary>
    /// Sets the grid cell size and the gap between widgets. Everything is redrawn at once; <paramref name="commit"/> also saves
    /// (a slider being dragged applies live and saves only when it is let go).
    /// </summary>
    public void SetGrid(int size, int gap, bool commit = true)
    {
        Settings.GridSize = DeckLayout.ClampGridSize(size);
        Settings.GridGap = DeckLayout.ClampGap(gap);
        DeckLayout.Configure(Settings.GridSize, Settings.GridGap);
        RenderAll();
        if (commit) Save();
    }

    // ---------- Several widgets at once ----------

    /// <summary>Hides several widgets everywhere with one redraw and one save.</summary>
    public void HideItems(IReadOnlyCollection<LauncherItem> items)
    {
        var changed = items.Where(i => !i.Hidden).ToList();
        if (changed.Count == 0) return;
        foreach (var item in changed) item.Hidden = true;
        RenderAll();
        Save();
        SetStatus(changed.Count == 1 ? "The widget is hidden. Show it again from the control center → Widgets." : $"{changed.Count} widgets are hidden. Show them again from the control center → Widgets.");
    }

    // ---------- Live preview while Customize is open ----------

    /// <summary>The unsaved copy of a widget that Customize is editing; drawn instead of the real one until the dialog closes. Null when none.</summary>
    internal LauncherItem? PreviewDraft { get; private set; }

    /// <summary>The item to draw: the draft while Customize previews it, otherwise the item itself.</summary>
    internal LauncherItem Previewed(LauncherItem item) => PreviewDraft is { } draft && draft.Id == item.Id ? draft : item;

    /// <summary>Draws <paramref name="draft"/> in place of the real widget (same id) on every surface and float. Nothing is saved.</summary>
    public void PreviewItem(LauncherItem draft)
    {
        PreviewDraft = draft;
        RebuildPreviewTarget(draft.Id);
    }

    /// <summary>Customize closed (saved or cancelled): the real widget is drawn again.</summary>
    public void ClearPreview()
    {
        if (PreviewDraft is not { } draft) return;
        PreviewDraft = null;
        RebuildPreviewTarget(draft.Id);
    }

    private void RebuildPreviewTarget(string id)
    {
        if (Settings.Items.FirstOrDefault(i => i.Id == id) is not { } real) return;
        foreach (var surface in Surfaces) surface.RebuildItem(real);
    }
}
