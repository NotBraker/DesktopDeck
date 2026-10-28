using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Homework tracker: assignments grouped Overdue / Today / Tomorrow / This week / Later with colored due pills.
/// Click a row to complete it, right-click to edit, move or delete it, + to add one (title, subject, due date).
/// Data: JSON in Item.Options["data.items"]; logic and tests in HomeworkLogic.cs.
/// </summary>
internal sealed class HomeworkWidget : IWidget
{
    public string Kind => "homework";
    public string Name => "Homework";
    public string Description => "Assignments by due date, with subjects and urgency colors";
    public string Section => "Study";
    public string Glyph => "";
    public string Accent => "#C2A14D";
    public (double Width, double Height) DefaultSize => (372, 324);
    public (double Width, double Height) MinimumSize => (276, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Lines("subjects", "Subjects", StudySubjectsLogic.DefaultSubjects, "What to show", "One per line as Name | #color. Used for the quick subject pick and the colors."),
        OptionSpec.Toggle("showSubject", "Show the subject under each title", true, "What to show"),
        OptionSpec.Toggle("showDone", "Show completed assignments", true, "What to show"),
        OptionSpec.Number("hideDoneDays", "Hide completed after (days, 0 = never)", 2, 0, 365, "What to show"),
        OptionSpec.Number("maxItems", "Most open assignments shown (0 = all)", 0, 0, 200, "What to show"),
        OptionSpec.Choice("groupMode", "Group by", "due", ["due", "subject", "priority", "none"], ["Due date", "Subject", "Priority", "No groups"], "Layout"),
        OptionSpec.Toggle("compact", "Compact rows", false, "Layout", "One line per assignment."),
        OptionSpec.Toggle("showCounts", "Show a count on each group", true, "Layout"),
        OptionSpec.Toggle("clock24", "24-hour times", StudyKit.Use24Hour(), "Layout"),
        OptionSpec.Toggle("subjectColors", "Color-code subjects", true, "Colors"),
        OptionSpec.Color("overdueColor", "Overdue color", "#E07A6A", "Colors"),
        OptionSpec.Color("todayColor", "Due today color", "#E0A24F", "Colors"),
        OptionSpec.Color("soonColor", "Due soon color", "#C2A14D", "Colors"),
        OptionSpec.Number("warningDays", "Count as \"soon\" within (days)", 2, 0, 14, "Behavior"),
        OptionSpec.Toggle("askSubject", "Ask for the subject when adding", true, "Behavior"),
        OptionSpec.Toggle("askDue", "Ask for the due date when adding", true, "Behavior"),
        OptionSpec.Toggle("confirmDelete", "Ask before deleting", false, "Behavior"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { Seed(item); },
        item => { Seed(item); item.Options["compact"] = "true"; item.Options["groupMode"] = "subject"; item.Width = 276; item.Height = 276; },
        item => { Seed(item); item.Options["groupMode"] = "priority"; item.Options["showSubject"] = "false"; item.Options["clock24"] = "false"; item.Accent = "#5E9BA6"; item.Width = 468; item.Height = 228; },
    ];

    private static void Seed(LauncherItem item)
    {
        var today = DateTime.Today;
        var list = new List<HomeworkLogic.Assignment>
        {
            HomeworkLogic.Create("Finish worksheet 4.2", "Math", today.AddDays(-1), "", 2),
            HomeworkLogic.Create("Latin vocab quiz prep", "Latin", today, "15:30", 2),
            HomeworkLogic.Create("Read chapter 7 and take notes", "History", today.AddDays(1)),
            HomeworkLogic.Create("Lab report: titration", "Chem", today.AddDays(3), "", 2),
            HomeworkLogic.Create("Essay outline", "History", today.AddDays(4)),
            HomeworkLogic.Create("Practice problems 12-30", "Math", today.AddDays(9), "", 0),
            HomeworkLogic.Create("Bring permission slip", "", null),
            HomeworkLogic.Create("Chapter 6 questions", "History", today.AddDays(-2)),
        };
        HomeworkLogic.Complete(list[^1], true, DateTime.Now);
        item.Options[HomeworkLogic.DataKey] = HomeworkLogic.Serialize(list);
    }

    // ---------- Changing the list ----------

    private static void Save(WidgetContext ctx, Action<List<HomeworkLogic.Assignment>> change)
    {
        var list = HomeworkLogic.Parse(ctx.Get(HomeworkLogic.DataKey));
        change(list);
        HomeworkLogic.PruneDone(list, DateTime.Now, ctx.HistoryDays(HistoryKeys.Homework));
        ctx.Set(HomeworkLogic.DataKey, HomeworkLogic.Serialize(list));
    }

    private static void Edit(WidgetContext ctx, string id, Action<HomeworkLogic.Assignment> change) =>
        Save(ctx, list => { if (list.FirstOrDefault(a => a.Id == id) is { } a) change(a); });

    private static List<StudySubjectsLogic.Subject> Subjects(WidgetContext ctx) => StudySubjectsLogic.Parse(ctx.GetLines("subjects"));

    /// <summary>Title, then (as the options say) a quick subject pick and a due-date pick; each pick is a small menu on the + button.</summary>
    private static void Add(WidgetContext ctx, FrameworkElement? anchor)
    {
        string? title = ctx.Ask("New assignment", "What needs doing?");
        if (string.IsNullOrWhiteSpace(title)) return;
        var subjects = Subjects(ctx);
        if (ctx.GetBool("askSubject") && subjects.Count > 0)
        {
            var entries = new List<object> { StudyKit.Heading("Subject") };
            entries.AddRange(subjects.Select(s => (object)StudyKit.Item(s.Name, () => AskDue(ctx, anchor, title, s.Name), s.Color)));
            entries.Add(new Separator());
            entries.Add(StudyKit.Item("No subject", () => AskDue(ctx, anchor, title, "")));
            StudyKit.Popup(anchor, entries);
        }
        else AskDue(ctx, anchor, title, "");
    }

    private static void AskDue(WidgetContext ctx, FrameworkElement? anchor, string title, string subject)
    {
        void Finish(DateTime? due, string time) => Save(ctx, list =>
        {
            if (list.Count >= HomeworkLogic.MaxItems) { ctx.Status("The list is full. Delete some completed assignments first."); return; }
            list.Add(HomeworkLogic.Create(title, subject, due, time));
        });
        if (!ctx.GetBool("askDue")) { Finish(null, ""); return; }
        var now = DateTime.Now;
        var entries = new List<object> { StudyKit.Heading("Due") };
        foreach (var (label, date) in HomeworkLogic.QuickDates(now))
            entries.Add(StudyKit.Item(date == now.Date || date == now.Date.AddDays(1) ? label : label + "   " + date.ToString("MMM d", CultureInfo.CurrentCulture), () => Finish(date, "")));
        entries.Add(new Separator());
        entries.Add(StudyKit.Item("Type a date or time…", () => { if (AskDate(ctx, "") is { } input) Finish(input.Date, input.Time); }));
        entries.Add(StudyKit.Item("No due date", () => Finish(null, "")));
        StudyKit.Popup(anchor, entries);
    }

    /// <summary>Asks until the text makes sense (or is cancelled).</summary>
    private static HomeworkLogic.DueInput? AskDate(WidgetContext ctx, string value)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string? text = ctx.Ask("Due date", "Date and time, e.g. fri 3pm, tomorrow, +3, 10/12, oct 12 14:30 (blank = no date)", value);
            if (text is null) return null;
            var parsed = HomeworkLogic.ParseDue(text, DateTime.Today, StudyKit.DayFirst());
            if (parsed.Ok) return parsed;
            ctx.Status("Couldn't read \"" + text.Trim() + "\" as a date.");
            value = text;
        }
        return null;
    }

    private static string DueText(HomeworkLogic.Assignment a) => (a.Due + " " + a.Time).Trim();

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("Add assignment…", Theme.Glyphs.Add, () => Add(ctx, null));
        var items = HomeworkLogic.Parse(ctx.Get(HomeworkLogic.DataKey));
        var (_, overdue, _, done) = HomeworkLogic.Summary(items, DateTime.Now);
        if (overdue > 0) yield return new WidgetAction("Move overdue to today", Theme.Glyphs.Next, () => Save(ctx, list => { foreach (var a in list.Where(a => HomeworkLogic.GroupOf(a, DateTime.Now) == HomeworkLogic.Group.Overdue)) a.Due = HomeworkLogic.Format(DateTime.Today); }));
        if (done > 0) yield return new WidgetAction("Clear completed", Theme.Glyphs.Delete, () => Save(ctx, list => list.RemoveAll(a => a.Done)));
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var now = DateTime.Now;
        var items = HomeworkLogic.Parse(ctx.Get(HomeworkLogic.DataKey));
        var subjects = Subjects(ctx);
        var (open, overdue, dueToday, _) = HomeworkLogic.Summary(items, now);
        bool narrow = ctx.Width < 330;

        FrameworkElement? badge = null;
        if (open > 0)
        {
            string text = overdue > 0 ? $"{overdue} overdue" : dueToday > 0 ? $"{dueToday} today" : $"{open} open";
            badge = overdue > 0 ? StudyKit.PillBox(ctx, text, StudyKit.ValidHex(ctx.Get("overdueColor"), "#E07A6A")) : dueToday > 0 ? StudyKit.PillBox(ctx, text, StudyKit.ValidHex(ctx.Get("todayColor"), "#E0A24F")) : WidgetKit.Pill(ctx, text);
        }
        Button? add = null;
        add = WidgetKit.IconButton(Theme.Glyphs.Add, "Add assignment", () => Add(ctx, add), 24, true);
        var (card, body) = WidgetKit.Frame(ctx, badge, add);

        var sections = HomeworkLogic.Layout(items, ctx.Get("groupMode"), now, ctx.GetBool("showDone"), ctx.GetInt("hideDoneDays"), ctx.GetInt("maxItems"), subjects.Select(s => s.Name).ToList());
        var list = new StackPanel();
        if (sections.Count == 0)
        {
            list.Children.Add(WidgetKit.Empty(ctx, items.Count == 0 ? "No homework yet. Press + to add an assignment." : "All caught up. Nothing left to hand in."));
        }
        bool first = true;
        foreach (var section in sections)
        {
            if (section.Title.Length > 0)
            {
                Brush color = section.Key == "Overdue" ? StudyKit.Hex(ctx.Get("overdueColor"), Theme.Danger) : section.Key == "Today" ? StudyKit.Hex(ctx.Get("todayColor"), Theme.Primary) : section.Key.StartsWith("subject:", StringComparison.Ordinal) && ctx.GetBool("subjectColors") ? StudyKit.Hex(StudySubjectsLogic.ColorOf(subjects, section.Key[8..]), Theme.Muted) : Theme.Muted;
                var label = StudyKit.SectionLabel(ctx, section.Title + (ctx.GetBool("showCounts") ? "  " + section.Items.Count : ""), color);
                if (first) label.Margin = new Thickness(4, 0, 0, 3);
                list.Children.Add(label);
            }
            first = false;
            foreach (var a in section.Items) list.Children.Add(Row(ctx, a, subjects, now, narrow));
        }
        body.Children.Add(StudyKit.ThinScroll(list));
        ctx.Tick(() => false, _ => ctx.Rebuild());
        return card;
    }

    private static FrameworkElement Row(WidgetContext ctx, HomeworkLogic.Assignment a, List<StudySubjectsLogic.Subject> subjects, DateTime now, bool narrow)
    {
        bool compact = ctx.GetBool("compact");
        string subjectHex = ctx.GetBool("subjectColors") ? StudySubjectsLogic.ColorOf(subjects, a.Subject) : "#8E8270";
        var subjectBrush = Theme.Brush(subjectHex);
        var pill = HomeworkLogic.PillFor(a, now, ctx.GetInt("warningDays"), ctx.GetBool("clock24"));

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // The round check box.
        double size = compact ? 15 : 18;
        var check = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), BorderThickness = new Thickness(1.8), BorderBrush = a.Done ? subjectBrush : subjectBrush,
            Background = a.Done ? subjectBrush : Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 9, 0),
            Child = a.Done ? WidgetKit.Icon(Theme.Glyphs.Check, size * 0.55, Theme.Background) : null,
        };
        grid.Children.Add(check);

        // Title (and subject).
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleLine = new StackPanel { Orientation = Orientation.Horizontal };
        if (a.Priority == 2 && !a.Done) titleLine.Children.Add(new TextBlock { Text = "! ", FontFamily = Theme.UiFont, FontWeight = FontWeights.Bold, FontSize = ctx.Font(13), Foreground = Theme.Danger, VerticalAlignment = VerticalAlignment.Center });
        var title = WidgetKit.Text(ctx, a.Title, compact ? 12 : 13, a.Done ? Theme.Muted : ctx.TextBrush);
        if (a.Done) title.TextDecorations = TextDecorations.Strikethrough;
        title.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        title.MaxHeight = ctx.Font(13) * 2.7;
        title.ToolTip = a.Title;
        if (compact || a.Priority != 2 || a.Done) { text.Children.Add(title); }
        else { titleLine.Children.Add(title); text.Children.Add(titleLine); }
        if (!compact && ctx.GetBool("showSubject") && a.Subject.Length > 0)
            text.Children.Add(WidgetKit.Text(ctx, a.Subject, 10.5, ctx.GetBool("subjectColors") ? subjectBrush : Theme.Muted));
        else if (compact && ctx.GetBool("showSubject") && a.Subject.Length > 0 && !narrow)
            title.ToolTip = a.Subject + " · " + a.Title;
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        // The due pill.
        if (pill.Kind is not (HomeworkLogic.PillKind.Done or HomeworkLogic.PillKind.None))
        {
            FrameworkElement due = pill.Kind switch
            {
                HomeworkLogic.PillKind.Overdue => StudyKit.PillBox(ctx, pill.Text, StudyKit.ValidHex(ctx.Get("overdueColor"), "#E07A6A")),
                HomeworkLogic.PillKind.Today => StudyKit.PillBox(ctx, pill.Text, StudyKit.ValidHex(ctx.Get("todayColor"), "#E0A24F")),
                HomeworkLogic.PillKind.Soon => StudyKit.PillBox(ctx, pill.Text, StudyKit.ValidHex(ctx.Get("soonColor"), "#C2A14D")),
                _ => WidgetKit.Pill(ctx, pill.Text),
            };
            due.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(due, 2);
            grid.Children.Add(due);
        }

        var row = WidgetKit.Row(grid);
        row.Padding = new Thickness(6, compact ? 3 : 5, 6, compact ? 3 : 5);
        row.Margin = new Thickness(0, 0, 0, compact ? 0 : 1);
        if (a.Done) row.Opacity = 0.7;
        WidgetKit.Clickable(row, () => Edit(ctx, a.Id, x => HomeworkLogic.Complete(x, !x.Done, DateTime.Now)), a.Done ? "Click to mark as not done" : "Click to mark as done");
        StudyKit.AttachMenu(row, RowMenu(ctx, a, subjects));
        return row;
    }

    private static IEnumerable<object> RowMenu(WidgetContext ctx, HomeworkLogic.Assignment a, List<StudySubjectsLogic.Subject> subjects)
    {
        yield return StudyKit.Item(a.Done ? "Mark as not done" : "Mark as done", () => Edit(ctx, a.Id, x => HomeworkLogic.Complete(x, !x.Done, DateTime.Now)));
        yield return new Separator();
        yield return StudyKit.Item("Rename…", () => { if (ctx.Ask("Rename assignment", "What needs doing?", a.Title) is { } t && !string.IsNullOrWhiteSpace(t)) Edit(ctx, a.Id, x => x.Title = t.Trim()); });
        yield return StudyKit.Item("Change due date…", () =>
        {
            if (AskDate(ctx, DueText(a)) is { } input) Edit(ctx, a.Id, x => { x.Due = input.Date is { } d ? HomeworkLogic.Format(d) : ""; x.Time = input.Date is null ? "" : input.Time; });
        });
        yield return StudyKit.Item("Postpone a day", () => Edit(ctx, a.Id, x => HomeworkLogic.Postpone(x, DateTime.Now, 1)));
        yield return StudyKit.Sub("Postpone more", [
            StudyKit.Item("2 days", () => Edit(ctx, a.Id, x => HomeworkLogic.Postpone(x, DateTime.Now, 2))),
            StudyKit.Item("A week", () => Edit(ctx, a.Id, x => HomeworkLogic.Postpone(x, DateTime.Now, 7))),
        ]);
        if (subjects.Count > 0)
            yield return StudyKit.Sub("Subject", subjects.Select(s => StudyKit.Item(s.Name, () => Edit(ctx, a.Id, x => x.Subject = s.Name), s.Color, string.Equals(s.Name, a.Subject, StringComparison.OrdinalIgnoreCase)))
                .Append(StudyKit.Item("None", () => Edit(ctx, a.Id, x => x.Subject = ""), null, a.Subject.Length == 0)));
        yield return StudyKit.Sub("Priority", [
            StudyKit.Item("High", () => Edit(ctx, a.Id, x => x.Priority = 2), null, a.Priority == 2),
            StudyKit.Item("Normal", () => Edit(ctx, a.Id, x => x.Priority = 1), null, a.Priority == 1),
            StudyKit.Item("Low", () => Edit(ctx, a.Id, x => x.Priority = 0), null, a.Priority == 0),
        ]);
        yield return new Separator();
        yield return StudyKit.Item("Delete", () =>
        {
            if (ctx.GetBool("confirmDelete") && !ctx.Confirm($"Delete \"{a.Title}\"?", ctx.Item.Name)) return;
            Save(ctx, list => list.RemoveAll(x => x.Id == a.Id));
        });
    }
}
