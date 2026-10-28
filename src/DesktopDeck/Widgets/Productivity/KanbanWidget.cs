using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class KanbanWidget : IWidget
{
    public string Kind => "kanban";
    public string Name => "Mini kanban";
    public string Description => "Move ideas from to do to done, one small card at a time";
    public string Section => "Productivity";
    public string Glyph => "";
    public string Accent => "#8E9B6B";
    public (double Width, double Height) DefaultSize => (468, 324);
    public (double Width, double Height) MinimumSize => (276, 180);
    public IReadOnlyList<OptionSpec> Options { get; } = [
        OptionSpec.Lines("columns", "Columns", "To do\nDoing\nDone", "What to show", "Up to six columns."),
        OptionSpec.Toggle("counts", "Show counts", true, "What to show"),
        OptionSpec.Toggle("compact", "Compact cards", false, "Layout"),
        OptionSpec.Toggle("wrap", "Wrap card text", true, "Layout"),
        OptionSpec.Number("textSize", "Card text size", 12, 10, 18, "Layout"),
        OptionSpec.Lines("columnColors", "Column colors", "#5E9BA6\n#C2A14D\n#9B9A5E", "Colors"),
        OptionSpec.Color("cardColor", "Card background", "", "Colors"),
        OptionSpec.Number("wip", "Card limit per column (0 = off)", 4, 0, 50, "Behavior"),
        OptionSpec.Number("hideDone", "Hide done after days (0 = never)", 0, 0, 365, "Behavior"),
        OptionSpec.Toggle("cycle", "Move done cards back to start", false, "Behavior") ];
    public IEnumerable<Action<LauncherItem>> Samples => [ item => Sample(item, false), item => { Sample(item, true); item.Options["compact"] = "true"; item.Options["wip"] = "2"; item.Options["columns"] = "Ideas\nMaking\nShipped"; } ];
    private static void Sample(LauncherItem item, bool busy) { var cards = new List<KanbanLogic.Card>(); string[] titles = ["Explore onboarding", "Write the brief", "Review the prototype", "Send weekly update", "Ship the landing page", "Fix the empty state"]; for (int i = 0; i < titles.Length; i++) cards.Add(new() { Id = i.ToString(), Text = titles[i], Column = busy ? i < 4 ? 1 : i % 3 : i % 3, Tag = i == 0 ? "#C4684F" : i == 2 ? "#5E9BA6" : "", Completed = i % 3 == 2 ? DateTime.Now : null }); item.Options["data.cards"] = KanbanLogic.Serialize(cards); }
    private static void Update(WidgetContext ctx, Action<List<KanbanLogic.Card>> change) { var cards = KanbanLogic.Parse(ctx.Get("data.cards"), KanbanLogic.Columns(ctx.GetLines("columns")).Length); ctx.SetState("kanban.undo", ctx.Get("data.cards"), false); change(cards); ctx.Set("data.cards", KanbanLogic.Serialize(cards)); }
    private static void Add(WidgetContext ctx, int column) { string? value = ctx.Ask("Add card", "What needs doing?"); if (!string.IsNullOrWhiteSpace(value)) Update(ctx, cards => { if (cards.Count >= 300) { ctx.Status("Board is full (300 cards). Delete a card first."); return; } cards.Add(new() { Id = Guid.NewGuid().ToString("N"), Text = value.Trim()[..Math.Min(300, value.Trim().Length)], Column = column }); }); }
    public IEnumerable<WidgetAction> Actions(WidgetContext ctx) { yield return new WidgetAction("Add card…", "", () => Add(ctx, 0)); if (ctx.State<string?>("kanban.undo", null) is { } previous) yield return new WidgetAction("Undo last board change", "", () => { ctx.SetState<string?>("kanban.undo", null, false); ctx.Set("data.cards", previous); }); }
    public FrameworkElement Build(WidgetContext ctx)
    {
        var columns = KanbanLogic.Columns(ctx.GetLines("columns")); var cards = KanbanLogic.Parse(ctx.Get("data.cards"), columns.Length); var now = DateTime.Now;
        var (card, body) = WidgetKit.Frame(ctx, null, WidgetKit.IconButton("", "Add card", () => Add(ctx, 0)));
        var board = new Grid(); var colors = ctx.GetLines("columnColors");
        for (int i = 0; i < columns.Length; i++)
        {
            int column = i; board.ColumnDefinitions.Add(new ColumnDefinition());
            Brush color; try { color = WidgetKit.Color(colors.ElementAtOrDefault(i) ?? ctx.Item.Accent); } catch (Exception ex) when (ex is ArgumentException or FormatException or NotSupportedException) { color = ctx.Accent; }
            var lane = new DockPanel { Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0) }; Grid.SetColumn(lane, i); board.Children.Add(lane);
            int count = cards.Count(c => c.Column == column); bool exceeded = ctx.GetInt("wip") > 0 && count > ctx.GetInt("wip");
            var heading = WidgetKit.Text(ctx, columns[i] + (ctx.GetBool("counts") ? $" · {count}" : ""), ctx.Width < 350 ? 10 : 11, exceeded ? Theme.Danger : color, true); heading.ToolTip = exceeded ? $"WIP limit {ctx.GetInt("wip")} exceeded ({count})" : columns[i]; heading.Margin = new Thickness(3, 0, 0, 9); DockPanel.SetDock(heading, Dock.Top); lane.Children.Add(heading);
            var add = WidgetKit.Button("+", () => Add(ctx, column)); add.FontSize = ctx.Font(14); add.Margin = new Thickness(0, 5, 0, 0); DockPanel.SetDock(add, Dock.Bottom); lane.Children.Add(add);
            var list = new StackPanel();
            foreach (var entry in cards.Where(c => c.Column == column && KanbanLogic.Visible(c, columns.Length - 1, ctx.GetInt("hideDone"), now)))
            {
                var text = WidgetKit.Text(ctx, entry.Text, ctx.GetNumber("textSize")); text.TextWrapping = ctx.GetBool("wrap") ? TextWrapping.Wrap : TextWrapping.NoWrap; text.TextTrimming = TextTrimming.CharacterEllipsis; text.MaxHeight = ctx.GetBool("compact") ? ctx.Font(34) : ctx.Font(65);
                var tile = WidgetKit.Row(text); tile.Background = ctx.GetColor("cardColor", Theme.Surface3); tile.Padding = new Thickness(8, ctx.GetBool("compact") ? 6 : 10, 8, ctx.GetBool("compact") ? 6 : 10); tile.Margin = new Thickness(0, 0, 0, 6); tile.ToolTip = entry.Text;
                if (JournalLogic.IsHex(entry.Tag)) { tile.BorderBrush = WidgetKit.Color(entry.Tag); tile.BorderThickness = new Thickness(3, 0, 0, 0); }
                void Change(Action<KanbanLogic.Card> edit) => Update(ctx, all => { var target = all.FirstOrDefault(c => c.Id == entry.Id); if (target is not null) edit(target); });
                WidgetKit.Clickable(tile, () => Change(c => KanbanLogic.Move(c, c.Column == columns.Length - 1 && ctx.GetBool("cycle") ? 0 : c.Column + 1, columns.Length, DateTime.Now)));
                var menu = new ContextMenu(); void Menu(string title, Action run) { var item = new MenuItem { Header = title }; item.Click += (_, e) => { e.Handled = true; run(); }; menu.Items.Add(item); }
                if (column > 0) Menu("Move left", () => Change(c => KanbanLogic.Move(c, c.Column - 1, columns.Length, DateTime.Now)));
                if (column < columns.Length - 1) Menu("Move right", () => Change(c => KanbanLogic.Move(c, c.Column + 1, columns.Length, DateTime.Now)));
                Menu("Rename…", () => { string? value = ctx.Ask("Rename card", "Card text", entry.Text); if (!string.IsNullOrWhiteSpace(value)) Change(c => c.Text = value.Trim()[..Math.Min(300, value.Trim().Length)]); });
                Menu("Color tag…", () => { string? value = ctx.Ask("Color tag", "Hex color (#RRGGBB), or blank to clear", entry.Tag); if (value is not null && (value.Trim().Length == 0 || JournalLogic.IsHex(value.Trim()))) Change(c => c.Tag = value.Trim()); });
                Menu("Delete (undo available)", () => Update(ctx, all => all.RemoveAll(c => c.Id == entry.Id))); tile.ContextMenu = menu; list.Children.Add(tile);
            }
            var scroll = WidgetKit.Scroll(list); scroll.Margin = new Thickness(0); lane.Children.Add(scroll);
        }
        body.Children.Add(board); ctx.Tick(() => false, _ => ctx.Rebuild()); return card;
    }
}

