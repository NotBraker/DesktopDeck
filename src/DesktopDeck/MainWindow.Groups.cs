using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck;

// Group cards: several widgets as the tabs of one card (see WidgetOrganizer). Each tab's widget is drawn by its own builder at the card's size minus the tab strip.
public sealed partial class MainWindow
{
    private const double TabHeight = WidgetOrganizer.TabStripHeight;

    private FrameworkElement BuildGroup(LauncherItem group, double w, double h)
    {
        var card = Card(group);
        var children = WidgetOrganizer.Children(settings, group.Id);
        var selected = WidgetOrganizer.Selected(settings, group);
        // Only the showing tab is drawn: forget the clocks and status lines of the others.
        foreach (var child in children) { tickers.Remove(KeyFor(child)); statusUpdaters.Remove(KeyFor(child)); }

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TabHeight) });
        layout.RowDefinitions.Add(new RowDefinition());
        card.Child = layout;
        layout.Children.Add(BuildTabStrip(group, children, selected, w));

        var body = new Border { ClipToBounds = true };
        Grid.SetRow(body, 1);
        if (selected is null)
        {
            var hint = Theme.Label("An empty group. Right-click a widget on your desktop and choose Add to group › " + group.Name + " to put it here.", 12.5, Theme.Muted);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.TextAlignment = TextAlignment.Center;
            hint.Margin = new Thickness(20, 12, 20, 12);
            hint.VerticalAlignment = VerticalAlignment.Center;
            body.Child = hint;
        }
        else body.Child = BuildChildContent(selected, Math.Max(40, w - 2), Math.Max(40, h - TabHeight - 1));
        layout.Children.Add(body);
        return card;
    }

    /// <summary>The selected widget drawn without its own card and title (the group's card and tab hold them).</summary>
    private FrameworkElement BuildChildContent(LauncherItem child, double w, double h)
    {
        string style = child.CardStyle;
        bool header = child.ShowHeader;
        child.CardStyle = "none";
        child.ShowHeader = false;
        try { return Appearance.WithoutCard(child, () => BuildContent(child, w, h)); }
        finally { child.CardStyle = style; child.ShowHeader = header; }
    }

    private FrameworkElement BuildTabStrip(LauncherItem group, List<LauncherItem> children, LauncherItem? selected, double width)
    {
        var strip = new Border { Height = TabHeight, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent };
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 8, 0) };
        // When every name won't fit, only the showing tab keeps its name; the others show their icon (and the name as a tooltip).
        double needed = children.Sum(c => 34 + 7.2 * ControlCenter.DisplayName(c).Length + 8);
        bool compact = needed > width - 24;
        foreach (var child in children)
        {
            bool on = child == selected;
            var accent = string.IsNullOrWhiteSpace(child.Accent) ? Theme.PrimaryHex : child.Accent;
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Theme.Icon(ControlCenter.KindGlyph(child), 12, on ? Theme.Accent(accent) : Theme.Muted));
            if (on || !compact)
            {
                var label = Theme.Label(ControlCenter.DisplayName(child), 12, on ? Theme.Text : Theme.Text2);
                label.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                label.Margin = new Thickness(7, 0, 0, 0);
                label.TextTrimming = TextTrimming.CharacterEllipsis;
                label.MaxWidth = 150;
                content.Children.Add(label);
            }
            var tab = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 4, 10, 5), Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand,
                Background = on ? Theme.Tint(accent, 0.2) : Brushes.Transparent, Child = content, ToolTip = ControlCenter.DisplayName(child),
            };
            if (!on)
            {
                tab.MouseEnter += (_, _) => tab.Background = PanelKit.Hover;
                tab.MouseLeave += (_, _) => tab.Background = Brushes.Transparent;
            }
            var target = child;
            tab.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (WidgetOrganizer.Selected(settings, group) == target) return;
                WidgetOrganizer.Select(group, target.Id);
                Changed(group);
            };
            tab.ContextMenu = BuildTabMenu(group, child);
            System.Windows.Automation.AutomationProperties.SetName(tab, ControlCenter.DisplayName(child) + " tab");
            row.Children.Add(tab);
        }
        if (children.Count == 0) row.Children.Add(Theme.Label(group.Name, 12.5, Theme.Text2));
        strip.Child = new ScrollViewer { Content = row, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        return strip;
    }

    /// <summary>Right-click on a tab: that widget's own entries, Customize…, and taking it out of the group.</summary>
    private ContextMenu BuildTabMenu(LauncherItem group, LauncherItem child)
    {
        var menu = Menu();
        AddKindEntries(menu, child);
        if (menu.Items.Count > 0) menu.Items.Add(new Separator());
        AddMenuItem(menu, "Customize…", () => EditItem(child), Theme.Glyphs.Edit);
        AddMenuItem(menu, "Remove from group", () => hub.RemoveFromGroup(child), Theme.Glyphs.Back);
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "Rename group…", () => RenameGroup(group), Theme.Glyphs.Edit);
        return menu;
    }

    /// <summary>The group card's right-click menu: the showing tab's entries, then group actions.</summary>
    private void AddGroupEntries(ContextMenu menu, LauncherItem group)
    {
        if (WidgetOrganizer.Selected(settings, group) is { } shown)
        {
            AddKindEntries(menu, shown);
            if (menu.Items.Count > 0) menu.Items.Add(new Separator());
            string name = ControlCenter.DisplayName(shown);
            AddMenuItem(menu, $"Customize {name}…", () => EditItem(shown), Theme.Glyphs.Edit);
            AddMenuItem(menu, $"Take {name} out of the group", () => hub.RemoveFromGroup(shown), Theme.Glyphs.Back);
        }
    }

    /// <summary>"Ungroup all", next to "Rename group…" so the entries about the card itself sit together.</summary>
    private void AddUngroupEntry(ContextMenu menu, LauncherItem group)
    {
        if (WidgetOrganizer.Children(settings, group.Id).Count > 0)
            AddMenuItem(menu, "Ungroup all", () => hub.DissolveGroup(group), Theme.Glyphs.Remove).ToolTip = "Remove the group card and put its widgets back on the desktop.";
    }

    /// <summary>"Add to group ›" for a widget on its own: the existing groups and a new one.</summary>
    private void AddGroupMenu(ItemsControl menu, LauncherItem item)
    {
        var sub = new MenuItem { Header = "Add to group", Icon = Theme.Icon("", 14, Theme.Text2) };
        var groups = WidgetOrganizer.Groups(settings).Where(g => WidgetOrganizer.CanJoin(item, g)).ToList();
        foreach (var group in groups)
        {
            var target = group;
            AddMenuItem(sub, string.IsNullOrWhiteSpace(group.Name) ? "Group" : group.Name, () => hub.AddToGroup(item, target), "");
        }
        if (groups.Count > 0) sub.Items.Add(new Separator());
        AddMenuItem(sub, "New group…", () => NewGroupFor(item), Theme.Glyphs.Add);
        menu.Items.Add(sub);
    }

    private void NewGroupFor(LauncherItem item)
    {
        if (Ask("New group", "Group name", "Group", "Create") is { } name) hub.NewGroupWith(item, name);
    }

    private void RenameGroup(LauncherItem group)
    {
        if (Ask("Rename group", "Group name", group.Name) is { } name) hub.RenameGroup(group, name);
    }
}
