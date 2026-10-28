using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DesktopDeck.Models;

namespace DesktopDeck;

// The Layouts tab's "Start from a template": ready-made arrangements added as new layouts.
internal sealed partial class ControlCenter
{
    /// <summary>The layout the last template made, so the note under the templates can offer to switch to it.</summary>
    private string? templateLayoutId;
    private string templateNote = "";

    private void AddTemplates(StackPanel stack)
    {
        var body = PanelKit.Section(stack, "Start from a template",
            "Ready-made arrangements for common days. Each is added as a new layout; nothing changes until you switch to it.");
        if (templateLayoutId is not null && LayoutEngine.Find(hub.Settings, templateLayoutId) is { } made && LayoutEngine.ActiveId(hub.Settings, made.Layer) != made.Id)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, PanelKit.S12) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var check = Theme.Icon(Theme.Glyphs.Check, 12, Theme.Olive);
            check.Margin = new Thickness(0, 0, PanelKit.S8, 0);
            row.Children.Add(check);
            var note = Theme.Label(templateNote, 12.5, Theme.Text2);
            note.TextWrapping = TextWrapping.Wrap;
            note.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(note, 1);
            row.Children.Add(note);
            var go = Theme.Button("Switch to it now", true);
            go.MinHeight = 30;
            go.Margin = new Thickness(PanelKit.S12, 0, 0, 0);
            go.Click += (_, _) =>
            {
                string layer = made.Layer, id = made.Id;
                templateLayoutId = null;
                Quietly(() => hub.SwitchLayout(layer, id));
                AfterLayoutChange();
            };
            Grid.SetColumn(go, 2);
            row.Children.Add(go);
            body.Children.Add(row);
        }
        else templateLayoutId = null;

        var grid = new UniformGrid { Columns = 2 };
        foreach (var template in WidgetTemplates.All) grid.Children.Add(TemplateCard(template));
        body.Children.Add(grid);
    }

    private FrameworkElement TemplateCard(WidgetTemplate template)
    {
        var stack = new StackPanel();
        var name = Theme.Label(template.Name, 14.5);
        name.FontWeight = FontWeights.SemiBold;
        stack.Children.Add(name);
        var description = Theme.Label(template.Description, 12, Theme.Muted);
        description.TextWrapping = TextWrapping.Wrap;
        description.Margin = new Thickness(0, 3, 0, PanelKit.S12);
        stack.Children.Add(description);
        var buttons = new WrapPanel { Margin = new Thickness(0, PanelKit.S4, 0, 0) };
        var background = PanelKit.Outline("+ Desktop", () => AddTemplate(template, LayoutLayers.Background), "A new desktop layout: the widgets sit on your desktop.");
        background.Margin = new Thickness(0, 0, PanelKit.S8, PanelKit.S4);
        buttons.Children.Add(background);
        var floating = PanelKit.Outline("+ Pinned", () => AddTemplate(template, LayoutLayers.Floating), "A new pinned layout: the widgets stay above your windows.");
        floating.Margin = new Thickness(0, 0, 0, PanelKit.S4);
        buttons.Children.Add(floating);
        stack.Children.Add(buttons);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var tile = PanelKit.IconTile(template.Icon, template.Accent, 44, emoji: true);
        tile.VerticalAlignment = VerticalAlignment.Top;
        tile.Margin = new Thickness(0, 0, PanelKit.S12, 0);
        grid.Children.Add(tile);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);
        var card = PanelKit.HoverCard(grid);
        card.Margin = new Thickness(0, 0, PanelKit.S8, PanelKit.S8);
        return card;
    }

    private void AddTemplate(WidgetTemplate template, string layer)
    {
        LayoutProfile? made = null;
        Quietly(() => made = hub.CreateTemplateLayout(template, layer));
        if (made is null) { templateLayoutId = null; return; }
        templateLayoutId = made.Id;
        int count = made.Entries.Count;
        templateNote = $"Added “{made.Name}” as a {(layer == LayoutLayers.Floating ? "pinned" : "desktop")} layout with {count} widget{(count == 1 ? "" : "s")}. It's in the list above.";
        AfterLayoutChange();
    }
}
