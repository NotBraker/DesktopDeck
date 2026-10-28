using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck;

// The Layouts tab: cards for the pinned layouts and the desktop layouts, with switch, rename, duplicate, delete, order and an icon.
internal sealed partial class ControlCenter
{
    private static readonly string[] IconChoices = ["💻", "🎯", "📚", "🎧", "☕", "🌙", "⚡", "🎮", "✏️", "🧠", "🏠", "📝", "🔥", "🌿", "🎨", "🧘"];

    private FrameworkElement BuildLayoutsTab()
    {
        var stack = new StackPanel();
        AddLayer(stack, LayoutLayers.Floating, "Pinned layouts", "Which pinned widgets are shown, and where.");
        AddLayer(stack, LayoutLayers.Background, "Desktop layouts", "Which widgets are on your desktop, and the desktop background itself.");

        AddTemplates(stack);

        // Rules, fullscreen apps and shortcuts: the rest of "which layout is on, and when".
        AddAutomation(stack);

        var quick = TopSection(stack, "layouts.shortcuts", "Shortcuts", "Switch layouts without opening this window");
        var quickCard = CardBody(quick);
        quickCard.Children.Add(PanelKit.Hint("Right-click the control button, or use the tray icon's Pinned layout and Desktop layout menus."));
        quickCard.Children.Add(PanelKit.Option("Switch pinned layouts with Ctrl+Alt+1 … 9",
            "Number keys pick the pinned layouts in the order above. Off by default: where Alt Gr types characters (such as @ { } [ ] on Swedish and German keyboards) these shortcuts would take those keys away everywhere.",
            hub.Settings.LayoutHotkeys, on => Quietly(() => hub.SetLayoutHotkeys(on))));
        if (hub.Settings.LayoutHotkeys)
        {
            int count = Math.Min(9, LayoutEngine.Of(hub.Settings, LayoutLayers.Floating).Count());
            int taken = Enumerable.Range(1, count).Count(n => !hub.LayoutHotkeyWorks(n));
            quickCard.Children.Add(PanelKit.Hint(taken == 0 ? $"Ctrl+Alt+1 … {count} are ready." : $"{taken} of the {count} shortcuts are already used by another app and are not available."));
        }
        return stack;
    }

    /// <summary>A rounded card inside a section's body, like the ones <see cref="PanelKit.Section"/> draws. Returns its content.</summary>
    private static StackPanel CardBody(Disclosure section)
    {
        var inner = new StackPanel();
        section.Body.Children.Add(new Border
        {
            Background = Theme.Surface, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
            Padding = new Thickness(PanelKit.S16, PanelKit.S16, PanelKit.S16, PanelKit.S8), Child = inner,
        });
        return inner;
    }

    /// <summary>A collapsed section between the tab's cards: a header row that opens the content, which is not boxed (the content brings its own cards).</summary>
    private static Disclosure TopSection(StackPanel stack, string key, string title, string? summary = null)
    {
        bool afterOther = stack.Children.Count > 0 && stack.Children[^1] is Disclosure;
        var section = PanelKit.Collapsible(stack, key, title, summary);
        section.Margin = new Thickness(0, afterOther ? PanelKit.S8 : PanelKit.S24, 0, 0);
        return section;
    }

    private void AddLayer(StackPanel stack, string layer, string heading, string note)
    {
        var header = new Grid { Margin = new Thickness(2, stack.Children.Count == 0 ? 0 : PanelKit.S24, 0, PanelKit.S8) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel();
        var title = Theme.Label(heading, 14.5);
        title.FontWeight = FontWeights.SemiBold;
        text.Children.Add(title);
        var sub = Theme.Label(note, 12, Theme.Muted);
        sub.TextWrapping = TextWrapping.Wrap;
        sub.Margin = new Thickness(0, 2, PanelKit.S16, 0);
        text.Children.Add(sub);
        header.Children.Add(text);
        var add = PanelKit.Outline("+ New layout from current", () => NewLayout(layer), "Save the " + (layer == LayoutLayers.Floating ? "pinned widgets" : "desktop") + " exactly as they are now as a new layout.");
        add.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(add, 1);
        header.Children.Add(add);
        stack.Children.Add(header);

        var layouts = LayoutEngine.Of(hub.Settings, layer).ToList();
        string activeId = LayoutEngine.ActiveId(hub.Settings, layer);
        for (int i = 0; i < layouts.Count; i++)
            stack.Children.Add(LayoutCard(layouts[i], i, layouts.Count, layouts[i].Id == activeId));
    }

    private Border LayoutCard(LayoutProfile layout, int index, int count, bool active)
    {
        bool floating = layout.Layer == LayoutLayers.Floating;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // The icon (an emoji of your choice, or a default); click it to change.
        var tile = layout.Icon.Length > 0 ? PanelKit.IconTile(layout.Icon, Theme.PrimaryHex, 44, emoji: true) : PanelKit.IconTile(floating ? "" : "", active ? Theme.PrimaryHex : "#8E8270", 44);
        tile.Cursor = Cursors.Hand;
        tile.ToolTip = "Choose an icon";
        tile.Margin = new Thickness(0, 0, PanelKit.S12, 0);
        tile.MouseLeftButtonUp += (_, e) => { OpenIconPicker(layout, tile); e.Handled = true; };
        grid.Children.Add(tile);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var name = Theme.Label(layout.Name, 14.5);
        name.FontWeight = FontWeights.SemiBold;
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        name.MaxWidth = 230;
        line.Children.Add(name);
        if (active)
        {
            var badge = PanelKit.Badge("Active", Theme.PrimaryHex, filled: true);
            badge.Margin = new Thickness(PanelKit.S8, 0, 0, 0);
            line.Children.Add(badge);
        }
        info.Children.Add(line);
        int shown = LayoutEngine.VisibleCount(hub.Settings, layout);
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        if (!floating) meta.Children.Add(BackgroundChip(layout));
        string detail = $"{shown} {(shown == 1 ? "widget" : "widgets")}";
        if (!floating) detail += " · " + BackgroundName(layout);
        if (floating && hub.Settings.LayoutHotkeys && index < 9) detail += hub.LayoutHotkeyWorks(index + 1) ? $" · Ctrl+Alt+{index + 1}" : $" · Ctrl+Alt+{index + 1} (in use by another app)";
        meta.Children.Add(Theme.Label(detail, 12, Theme.Muted));
        info.Children.Add(meta);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        FrameworkElement switchTo;
        if (active)
        {
            var inUse = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(PanelKit.S8, 0, PanelKit.S8, 0), VerticalAlignment = VerticalAlignment.Center };
            inUse.Children.Add(Theme.Icon(Theme.Glyphs.Check, 11, Theme.Olive));
            var inUseText = Theme.Label("In use", 12, Theme.Olive);
            inUseText.Margin = new Thickness(6, 0, 0, 0);
            inUse.Children.Add(inUseText);
            switchTo = inUse;
        }
        else
        {
            var button = Theme.Button("Switch", true);
            button.MinHeight = 30;
            button.Padding = new Thickness(14, 4, 14, 4);
            button.ToolTip = "Apply this layout now";
            button.Click += (_, _) => { Quietly(() => hub.SwitchLayout(layout.Layer, layout.Id)); AfterLayoutChange(); };
            button.Margin = new Thickness(0, 0, PanelKit.S4, 0);
            switchTo = button;
        }
        actions.Children.Add(switchTo);
        actions.Children.Add(PanelKit.More("More: rename, duplicate, change the order", menu =>
        {
            PanelKit.MenuEntry(menu, "Rename\u2026", () => RenameLayout(layout));
            PanelKit.MenuEntry(menu, "Duplicate", () => { Quietly(() => hub.DuplicateLayout(layout.Id)); AfterLayoutChange(); });
            menu.Items.Add(new Separator());
            PanelKit.MenuEntry(menu, "Earlier in the list", () => { Quietly(() => hub.MoveLayout(layout.Id, -1)); AfterLayoutChange(); }, index > 0, tip: "The list order is the quick-switch order (Ctrl+Alt+1 \u2026 9)");
            PanelKit.MenuEntry(menu, "Later in the list", () => { Quietly(() => hub.MoveLayout(layout.Id, 1)); AfterLayoutChange(); }, index < count - 1, tip: "The list order is the quick-switch order (Ctrl+Alt+1 \u2026 9)");
        }));
        var delete = PanelKit.Quiet("Delete", () => DeleteLayout(layout), danger: true);
        delete.IsEnabled = count > 1;
        delete.ToolTip = count > 1 ? "Delete this layout" : "Every layer keeps at least one layout";
        delete.Margin = new Thickness(PanelKit.S4, 0, 0, 0);
        actions.Children.Add(delete);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        var card = PanelKit.HoverCard(grid, active);
        card.Margin = new Thickness(0, 0, 0, PanelKit.S8);
        card.Padding = new Thickness(PanelKit.S12, PanelKit.S12, PanelKit.S12, PanelKit.S12);
        card.MouseLeftButtonDown += (_, e) =>
        {
            // Double-click anywhere on the card (not on a button) switches to it.
            if (e.ClickCount == 2 && !active && !IsInsideButton(e.OriginalSource as DependencyObject)) { Quietly(() => hub.SwitchLayout(layout.Layer, layout.Id)); AfterLayoutChange(); }
        };
        return card;
    }

    private static bool IsInsideButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ButtonBase) return true;
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    private static string BackgroundName(LayoutProfile layout)
    {
        var style = layout.Backgrounds.TryGetValue("", out var all) ? all : layout.Backgrounds.Values.FirstOrDefault();
        return style?.Kind switch { "solid" => "Color background", "gradient" => "Gradient background", "image" => "Picture background", "wallpaper" => "Windows wallpaper", _ => "Theme background" };
    }

    /// <summary>A tiny preview of the layout's background (its look on all displays, or the first display that has one).</summary>
    private static FrameworkElement BackgroundChip(LayoutProfile layout)
    {
        var style = layout.Backgrounds.TryGetValue("", out var all) ? all : layout.Backgrounds.Values.FirstOrDefault();
        var chip = new Border { Width = 28, Height = 18, CornerRadius = new CornerRadius(5), BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, PanelKit.S8, 0), VerticalAlignment = VerticalAlignment.Center };
        if (style is { Kind: "image" or "wallpaper" }) { chip.Background = Theme.Surface3; chip.Child = Theme.Icon(Theme.Glyphs.Picture, 9, Theme.Muted); }
        else chip.Background = Appearance.BuildBrush(style, new Size(28, 18), 1);
        return chip;
    }

    // ---------- Actions ----------

    private void NewLayout(string layer)
    {
        string suggestion = layer == LayoutLayers.Floating ? "My pinned layout" : "My desktop layout";
        if (InputDialog.Ask(this, "New layout from current", "Name", suggestion, "Create") is not { } name) return;
        Quietly(() => hub.NewLayout(layer, name));
        AfterLayoutChange();
        scroll.ScrollToTop();
    }

    private void RenameLayout(LayoutProfile layout)
    {
        if (InputDialog.Ask(this, "Rename layout", "Name", layout.Name) is not { } name) return;
        Quietly(() => hub.RenameLayout(layout.Id, name));
        AfterLayoutChange();
    }

    private void DeleteLayout(LayoutProfile layout)
    {
        if (MessageBox.Show(this, $"Delete the layout \"{layout.Name}\"? Your widgets are not deleted, only this arrangement." + (layout.Id == LayoutEngine.ActiveId(hub.Settings, layout.Layer) ? " Another layout will be switched on instead." : ""), "Delete layout", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Quietly(() => hub.DeleteLayout(layout.Id));
        AfterLayoutChange();
    }

    private void OpenIconPicker(LayoutProfile layout, FrameworkElement anchor)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade, VerticalOffset = 4 };
        var wrap = new WrapPanel { Width = 8 * 40 };
        void Pick(string icon) { popup.IsOpen = false; Quietly(() => hub.SetLayoutIcon(layout.Id, icon)); AfterLayoutChange(); }
        foreach (var emoji in IconChoices)
        {
            var label = new TextBlock { Text = emoji, FontFamily = PanelKit.EmojiFont, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { Width = 36, Height = 36, Margin = new Thickness(2), CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = label };
            cell.MouseEnter += (_, _) => cell.Background = PanelKit.Hover;
            cell.MouseLeave += (_, _) => cell.Background = Brushes.Transparent;
            string chosen = emoji;
            cell.MouseLeftButtonUp += (_, e) => { Pick(chosen); e.Handled = true; };
            wrap.Children.Add(cell);
        }
        var none = PanelKit.Quiet("Use the default icon", () => Pick(""));
        none.Margin = new Thickness(2, 4, 0, 0);
        var stack = new StackPanel { Children = { wrap, none } };
        popup.Child = new Border
        {
            Margin = new Thickness(8, 4, 16, 18), Padding = new Thickness(8), CornerRadius = new CornerRadius(12), Background = Theme.Surface, BorderBrush = Theme.LineStrong, BorderThickness = new Thickness(1),
            Child = stack, Effect = PanelKit.Shadow(20, 6, 0.5),
        };
        popup.IsOpen = true;
    }
}
