using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DesktopDeck.Models;
namespace DesktopDeck.Widgets;
internal sealed class JournalWidget : IWidget
{
    public string Kind => "journal";
    public string Name => "Daily journal";
    public string Description => "A moment to reflect, with a mood, note and daily streak";
    public string Section => "Productivity";
    public string Glyph => "";
    public string Accent => "#C2A14D";
    public (double Width, double Height) DefaultSize => (324, 276);
    public (double Width, double Height) MinimumSize => (228, 180);
    public IReadOnlyList<OptionSpec> Options { get; } = [
        OptionSpec.Text("labels", "Mood labels", "Awful, Bad, Okay, Good, Great", "What to show"),
        OptionSpec.Number("days", "Recent days", 7, 3, 21, "What to show"),
        OptionSpec.Toggle("notes", "Show today's note", true, "What to show"),
        OptionSpec.Toggle("streak", "Show streak", true, "What to show"),
        OptionSpec.Toggle("date", "Show date", true, "What to show"),
        OptionSpec.Toggle("dayLabels", "Show day names in history", true, "Layout"),
        OptionSpec.Choice("moodStyle", "Mood style", "faces", ["faces", "numbers"], group: "Layout"),
        OptionSpec.Choice("palette", "Mood colors", "earth", ["earth", "classic", "ocean", "sunset", "accent", "custom"], group: "Colors"),
        OptionSpec.Text("colors", "Custom colors", "#ef4444,#f97316,#eab308,#84cc16,#22c55e", "Colors"),
        OptionSpec.Text("prompt", "Reflection prompt", "What stood out today?", "Behavior") ];
    private static void Save(WidgetContext ctx, Action<Dictionary<string, JournalLogic.JournalDay>> change) { var entries = JournalLogic.Parse(ctx.Get("data.entries")); ctx.SetState("journal.undo", ctx.Get("data.entries"), false); change(entries); ctx.Set("data.entries", JournalLogic.Serialize(entries, DateTime.Today, ctx.HistoryDays(HistoryKeys.Journal))); }
    private static void Note(WidgetContext ctx) { string? value = ctx.Ask("Daily journal", ctx.Get("prompt"), JournalLogic.Get(JournalLogic.Parse(ctx.Get("data.entries")), DateTime.Today)?.Note ?? ""); if (value is not null) Save(ctx, e => JournalLogic.SetNote(e, DateTime.Today, value[..Math.Min(value.Length, 500)])); }
    public IEnumerable<WidgetAction> Actions(WidgetContext ctx) { if (ctx.State<string?>("journal.undo", null) is { } previous) yield return new WidgetAction("Undo last journal change", "", () => { ctx.SetState<string?>("journal.undo", null, false); ctx.Set("data.entries", previous); }); yield return new WidgetAction("Edit today's note…", "", () => Note(ctx)); }
    public IEnumerable<Action<LauncherItem>> Samples => [ item => Sample(item, false), item => { Sample(item, true); item.Options["palette"] = "ocean"; item.Options["moodStyle"] = "numbers"; } ];
    private static void Sample(LauncherItem item, bool alternate) { var entries = new Dictionary<string, JournalLogic.JournalDay>(); for (int i = 6; i >= 0; i--) entries[JournalLogic.Key(DateTime.Today.AddDays(-i))] = new() { Mood = (i % 3) + 3, Note = i == 0 ? alternate ? "A quiet walk and a fresh start." : "Finished the draft. Grateful for a good team." : "" }; item.Options["data.entries"] = JournalLogic.Serialize(entries, DateTime.Today); }
    public FrameworkElement Build(WidgetContext ctx)
    {
        var today = DateTime.Today; var entries = JournalLogic.Parse(ctx.Get("data.entries")); var entry = JournalLogic.Get(entries, today);
        var (card, body) = WidgetKit.Frame(ctx, ctx.GetBool("streak") ? WidgetKit.Pill(ctx, $"{JournalLogic.Streak(entries, today)}d streak") : null, WidgetKit.IconButton("", "Write today's note", () => Note(ctx)));
        var stack = new StackPanel();
        if (ctx.GetBool("date")) stack.Children.Add(WidgetKit.Muted(ctx, today.ToString("dddd, MMM d")));
        string[] labels = JournalLogic.Labels(ctx.Get("labels")); string[] faces = ["☹", "◔", "○", "◕", "☺"];
        var palette = JournalLogic.Palette(ctx.Get("palette"), ctx.Get("colors"));
        Brush Color(int mood) => mood == 0 ? Theme.Line : palette is null ? ctx.Accent : WidgetKit.Color(palette[mood - 1]);
        var moods = new UniformGrid { Columns = 5, Margin = new Thickness(0, 10, 0, 9) };
        for (int i = 1; i <= 5; i++) { int mood = i; var text = WidgetKit.Text(ctx, ctx.Get("moodStyle") == "numbers" ? i.ToString() : faces[i - 1], 23, Color(i), true); text.HorizontalAlignment = HorizontalAlignment.Center; var tile = new Border { Child = text, Padding = new Thickness(0, 4, 0, 4), CornerRadius = new CornerRadius(8), Background = entry?.Mood == i ? Theme.Surface3 : Brushes.Transparent, BorderThickness = new Thickness(entry?.Mood == i ? 1 : 0), BorderBrush = Color(i), Margin = new Thickness(2) }; WidgetKit.Clickable(tile, () => Save(ctx, e => JournalLogic.SetMood(e, DateTime.Today, mood)), labels[i - 1]); moods.Children.Add(tile); }
        stack.Children.Add(moods);
        if (ctx.GetBool("notes") && ctx.Height > 200) { var note = WidgetKit.Text(ctx, entry?.Note is { Length: > 0 } n ? n : ctx.Get("prompt"), 12, entry?.Note.Length > 0 ? ctx.TextBrush : Theme.Muted); note.TextWrapping = TextWrapping.Wrap; note.MaxHeight = 42; WidgetKit.Clickable(note, () => Note(ctx)); stack.Children.Add(note); }
        var history = new UniformGrid { Columns = ctx.GetInt("days"), Margin = new Thickness(0, 12, 0, 0) };
        foreach (var day in JournalLogic.LastDays(entries, today, ctx.GetInt("days"))) { var cell = new StackPanel { Margin = new Thickness(2), ToolTip = $"{day.Day:d}: {(day.Entry is { Mood: > 0 } d ? labels[d.Mood - 1] : "No mood")}\n{day.Entry?.Note}" }; cell.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = Color(day.Entry?.Mood ?? 0) }); if (ctx.GetBool("dayLabels") && ctx.GetInt("days") <= 10) { var label = WidgetKit.Muted(ctx, day.Day.ToString("ddd")[..1], 9); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 4, 0, 0); cell.Children.Add(label); } history.Children.Add(cell); }
        stack.Children.Add(history); body.Children.Add(WidgetKit.Scroll(stack)); ctx.Tick(() => false, now => { if (now.LocalDateTime.Date != today) ctx.Rebuild(); }); return card;
    }
}
