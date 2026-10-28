using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck;

// Linked widgets (see Models/WidgetLinks.cs): the "Link to…" question when a widget is added, "Link with" and "Unlink" in the right-click menu,
// and the small link mark shown in Edit layout.
public sealed partial class MainWindow
{
    private const string LinkGlyph = "";

    /// <summary>While a widget is being added from the gallery: the existing widget the user chose to link it to (see <see cref="AddNew"/>).</summary>
    private LauncherItem? pendingLink;

    /// <summary>The widget being added is linked to this one: skip the questions it would ask, its data comes from there.</summary>
    internal LauncherItem? PendingLink => pendingLink;

    /// <summary>
    /// Adding a widget of a kind that already exists: asks "Link to another …?". Returns false when the user cancelled (nothing is added).
    /// On true, <see cref="pendingLink"/> holds the chosen widget, or null for "Start from scratch".
    /// </summary>
    private bool AskLinkOnAdd(string kind, string kindName)
    {
        pendingLink = null;
        if (!hub.Advanced || !WidgetLinks.CanLink(kind)) return true; // the simple view always starts from scratch
        // One entry per group of linked widgets: they hold the same data.
        var shown = new List<LauncherItem>();
        foreach (var other in settings.Items.Where(i => i.Kind == kind && i.GroupId.Length == 0 && WidgetLinks.HasSharedData(i)))
            if (other.LinkId.Length == 0 || !shown.Any(s => s.LinkId == other.LinkId)) shown.Add(other);
        if (shown.Count == 0) return true;
        var choices = shown.Select(i => LinkChoice.Of(i, hub)).ToList();
        for (int n = 0; n < choices.Count; n++)
        {
            int others = WidgetLinks.Peers(choices[n].Item, settings.Items).Count;
            if (others > 0) choices[n] = choices[n] with { Where = choices[n].Where + $" · linked with {others} other{(others == 1 ? "" : "s")}" };
        }
        bool scratch = false;
        var pick = Modal(() => LinkDialog.Ask(DialogOwner, kindName, choices, out scratch));
        if (pick is null) return scratch;
        pendingLink = pick.Item;
        return true;
    }

    /// <summary>Smoke test: the right-click menu of a widget.</summary>
    internal ContextMenu MenuForSmoke(LauncherItem item) => BuildItemMenu(item);

    /// <summary>The right-click entries "Link with ›" and, for a linked widget, "Unlink".</summary>
    private void AddLinkMenu(ItemsControl menu, LauncherItem item)
    {
        if (!WidgetLinks.CanLink(item.Kind)) return;
        string kindName = ControlCenter.KindName(item);
        var sub = new MenuItem { Header = "Link with", Icon = Theme.Icon(LinkGlyph, 14, Theme.Text2) };
        sub.ToolTip = "Share this widget's information with another one. Each keeps its own size, place and look.";
        var candidates = WidgetLinks.Candidates(item, settings.Items);
        if (candidates.Count == 0)
            sub.Items.Add(new MenuItem { Header = $"No other {kindName} widgets", IsEnabled = false });
        foreach (var other in candidates)
        {
            var choice = LinkChoice.Of(other, hub);
            var target = other;
            AddMenuItem(sub, choice.Name + "  ·  " + choice.Where, () => LinkWith(item, target), "");
        }
        menu.Items.Add(sub);
        int peers = WidgetLinks.Peers(item, settings.Items).Count;
        if (peers > 0)
            AddMenuItem(menu, "Unlink", () => UnlinkItem(item), Theme.Glyphs.Remove).ToolTip = $"Stop sharing with {(peers == 1 ? "the other" : peers + " other")} {kindName} widget{(peers == 1 ? "" : "s")}. Each keeps what it has now.";
    }

    /// <summary>Links two widgets. When both hold different information, asks whose to keep.</summary>
    private void LinkWith(LauncherItem item, LauncherItem other)
    {
        var keep = item;
        var join = other;
        // Joining a widget that is already linked takes its whole group; the data to compare is the group's (all peers hold the same).
        bool differ = WidgetLinks.HasData(item) && WidgetLinks.HasData(other) && Differs(item, other);
        if (differ)
        {
            var kept = Modal(() => KeepDataDialog.Ask(DialogOwner, LinkChoice.Of(item, hub), LinkChoice.Of(other, hub)));
            if (kept is null) return;
            if (kept.Item == other) { keep = other; join = item; }
        }
        else if (!WidgetLinks.HasData(item) && WidgetLinks.HasData(other)) { keep = other; join = item; }
        if (!WidgetLinks.Link(keep, join, settings.Items)) return;
        hub.RenderAll();
        SaveNow();
        SetStatus($"Linked {ItemName(item)} with {ItemName(other)}. They now share their information.");
    }

    private static bool Differs(LauncherItem a, LauncherItem b)
    {
        var probe = b.Copy();
        return WidgetLinks.CopyData(a, probe);
    }

    private void UnlinkItem(LauncherItem item)
    {
        WidgetLinks.Unlink(item, settings.Items);
        hub.RenderAll();
        SaveNow();
        SetStatus($"{ItemName(item)} is no longer linked. It keeps what it has now.");
    }

    /// <summary>A small link mark for the top-left corner of a linked widget in Edit layout, or null when it isn't linked.</summary>
    private FrameworkElement? LinkBadge(LauncherItem item)
    {
        int peers = WidgetLinks.Peers(item, settings.Items).Count;
        if (peers == 0) return null;
        var badge = Theme.Icon(LinkGlyph, 11, Theme.Muted);
        badge.HorizontalAlignment = HorizontalAlignment.Left;
        badge.VerticalAlignment = VerticalAlignment.Top;
        badge.Margin = new Thickness(46, 3, 0, 0);
        badge.ToolTip = $"Linked: shares its information with {peers} other widget{(peers == 1 ? "" : "s")}. Right-click → Unlink to separate them.";
        return badge;
    }
}
