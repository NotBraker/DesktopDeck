using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class QuickLinksWidget : IWidget
{
    public string Kind => "quick-links";
    public string Name => "Quick links";
    public string Description => "Your favorite websites, folders and apps in one place";
    public string Section => "Productivity";
    public string Glyph => "";
    public string Accent => "#5E9BA6";
    public (double Width, double Height) DefaultSize => (276, 276);
    public (double Width, double Height) MinimumSize => (180, 180);
    public IReadOnlyList<OptionSpec> Options { get; } = [
        OptionSpec.Lines("links", "Links", "Notebook | https://example.com/notes\nResearch | https://example.com/research\nDesign | https://example.com/design\nCalendar | https://example.com/calendar\nProjects | C:\\Projects\nReading | https://example.com/reading", "What to show", "Title | website, file, folder or app"),
        OptionSpec.Toggle("labels", "Show labels", true, "What to show"),
        OptionSpec.Toggle("targets", "Show targets in list", false, "What to show"),
        OptionSpec.Choice("layout", "Layout", "grid", ["grid", "list"], group: "Layout"),
        OptionSpec.Number("columns", "Grid columns", 3, 1, 6, "Layout"),
        OptionSpec.Number("iconSize", "Icon size", 30, 16, 64, "Layout"),
        OptionSpec.Choice("labelPosition", "Label position", "below", ["below", "beside"], group: "Layout"),
        OptionSpec.Choice("tileStyle", "Tile style", "soft", ["soft", "outline", "plain"], group: "Layout"),
        OptionSpec.Color("tileColor", "Letter tile color", "", "Colors"),
        OptionSpec.Choice("openBehavior", "Opening", "immediate", ["immediate", "confirm"], ["Open directly", "Ask before opening"], "Behavior") ];
    public IEnumerable<Action<LauncherItem>> Samples => [ _ => { }, item => { item.Options["layout"] = "list"; item.Options["targets"] = "true"; item.Options["iconSize"] = "24"; item.Options["tileStyle"] = "outline"; item.Options["links"] = "Studio | https://example.com/studio\nPlanning | https://example.com/planning\nArchive | https://example.com/archive"; }, item => { item.Options["columns"] = "2"; item.Options["labelPosition"] = "beside"; item.Options["tileColor"] = "#C4684F"; } ];
    public FrameworkElement Build(WidgetContext ctx)
    {
        var (card, body) = WidgetKit.Frame(ctx, null, WidgetKit.IconButton("", "Edit links", ctx.Customize));
        bool list = ctx.Get("layout") == "list";
        int columns = Math.Min(ctx.GetInt("columns"), Math.Max(1, (int)((ctx.Width - 30) / 72)));
        Panel panel = list ? new StackPanel() : new UniformGrid { Columns = columns };
        foreach (var link in QuickLinksLogic.Parse(ctx.GetLines("links")))
        {
            double size = ctx.GetNumber("iconSize");
            var letter = WidgetKit.Text(ctx, link.Title[..1].ToUpperInvariant(), size * .5, ctx.GetColor("tileColor"), true);
            letter.HorizontalAlignment = HorizontalAlignment.Center; letter.VerticalAlignment = VerticalAlignment.Center;
            var icon = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * .25), Background = Theme.Tint(ctx.Item.Accent, .14), Child = letter, VerticalAlignment = VerticalAlignment.Center };
            if (!link.Web && !ctx.IsSnapshot) ctx.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!icon.IsLoaded) return; var source = ImageCache.ShellIcon(link.Target, (int)size * 2); if (source is not null) icon.Child = new Image { Source = source, Stretch = Stretch.Uniform }; }));
            bool beside = list || ctx.Get("labelPosition") == "beside";
            var content = new StackPanel { Orientation = beside ? Orientation.Horizontal : Orientation.Vertical, HorizontalAlignment = list ? HorizontalAlignment.Stretch : HorizontalAlignment.Center };
            content.Children.Add(icon);
            if (ctx.GetBool("labels"))
            {
                var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = beside ? new Thickness(9, 0, 0, 0) : new Thickness(0, 6, 0, 0), MaxWidth = list ? Math.Max(40, ctx.Width - size - 58) : Math.Max(30, (ctx.Width - 35) / columns - (beside ? size + 16 : 6)) };
                var label = WidgetKit.Text(ctx, link.Title, 11.5); label.TextTrimming = TextTrimming.CharacterEllipsis; labels.Children.Add(label);
                if (list && ctx.GetBool("targets")) labels.Children.Add(WidgetKit.Muted(ctx, link.Target, 10));
                content.Children.Add(labels);
            }
            var tile = WidgetKit.Row(content); tile.Margin = new Thickness(2); tile.Padding = new Thickness(6, 9, 6, 9);
            tile.Background = ctx.Get("tileStyle") == "soft" ? Theme.Surface3 : Brushes.Transparent;
            if (ctx.Get("tileStyle") == "outline") { tile.BorderBrush = Theme.Line; tile.BorderThickness = new Thickness(1); }
            WidgetKit.Clickable(tile, () => { if (ctx.Get("openBehavior") != "confirm" || ctx.Confirm($"Open {link.Title}?\n{link.Target}", "Open link")) ctx.Open(link.Target); }, link.Target);
            panel.Children.Add(tile);
        }
        if (panel.Children.Count == 0) panel.Children.Add(WidgetKit.Clickable(WidgetKit.Empty(ctx, "Add your favorite links"), ctx.Customize));
        body.Children.Add(WidgetKit.Scroll(panel)); return card;
    }
}
