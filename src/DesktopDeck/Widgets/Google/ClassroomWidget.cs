using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck.Widgets;

/// <summary>
/// Google Classroom, read-only, for students: upcoming assignments from your active courses with due date and time, a course color,
/// and the overdue, missing and turned-in state. Click one to open it in Classroom. "Import into Homework" copies them into a Homework
/// widget (never twice). School Google Workspace admins often block third-party apps: a refusal says so and points to the iCal alternative.
/// Logic and tests: ClassroomLogic.cs, ClassroomService.cs.
/// </summary>
internal sealed class ClassroomWidget : IWidget
{
    private const string ClassroomHome = "https://classroom.google.com/";

    public string Kind => "classroom";
    public string Name => "Google Classroom";
    public string Description => "Upcoming assignments and what is missing, from your courses";
    public string Section => "Study";
    public string Glyph => "";
    public string Accent => "#34a853";
    public (double Width, double Height) DefaultSize => (324, 372);
    public (double Width, double Height) MinimumSize => (228, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Lines("courses", "Courses to include", "", "What to show", "One per line: part of a course name. Empty = all your active courses."),
        OptionSpec.Number("days", "Days ahead", 14, 1, 120, "What to show"),
        OptionSpec.Number("overdueDays", "Show overdue and missing work from the last (days, 0 = hide)", 7, 0, 60, "What to show"),
        OptionSpec.Toggle("hideTurnedIn", "Hide work you have turned in", true, "What to show"),
        OptionSpec.Toggle("showNoDue", "Show assignments without a due date", false, "What to show"),
        OptionSpec.Number("maxItems", "Most assignments to show", 10, 1, 60, "What to show"),
        OptionSpec.Choice("groupBy", "Group by", "date", ["date", "course", "none"], ["Due date", "Course", "No groups"], "Layout"),
        OptionSpec.Toggle("showCourse", "Show the course name", true, "Layout"),
        OptionSpec.Toggle("showDescription", "Show the assignment's description", false, "Layout"),
        OptionSpec.Toggle("showPoints", "Show the points", false, "Layout"),
        OptionSpec.Toggle("compact", "Compact (one line per assignment)", false, "Layout"),
        OptionSpec.Toggle("clock24", "24-hour times", StudyKit.Use24Hour(), "Layout"),
        OptionSpec.Toggle("showColor", "Show a color bar for each course", true, "Colors"),
        OptionSpec.Lines("courseColors", "Course colors", "", "Colors", "One per line as Course name | #color. Courses without one get a color automatically."),
        OptionSpec.Color("overdueColor", "Missing color", "#E07A6A", "Colors"),
        OptionSpec.Number("soonDays", "Count as \"soon\" within (days)", 2, 0, 14, "Behavior"),
        OptionSpec.Number("refresh", "Refresh every (minutes)", 15, 5, 240, "Behavior", "Only while the widget is on screen."),
        OptionSpec.Toggle("showUpdated", "Show when it last updated", false, "Behavior"),
    ];

    // ---------- Samples ----------

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => Fill(item),
        item => { Fill(item); item.Options["groupBy"] = "course"; item.Options["compact"] = "true"; item.Options["showDescription"] = "false"; item.Options["hideTurnedIn"] = "false"; item.Options["courseColors"] = "Chem | #ec4899"; item.Width = 372; item.Height = 372; },
        item => { Fill(item); item.Options["showDescription"] = "true"; item.Options["showPoints"] = "true"; item.Options["clock24"] = "true"; item.Options["groupBy"] = "none"; item.Options["days"] = "5"; item.Width = 324; item.Height = 420; },
        item => { Fill(item, empty: true); },
        item => { Fill(item, age: TimeSpan.FromHours(3), error: ClassroomService.BlockedMessage, kind: "auth"); item.Width = 276; item.Height = 324; },
    ];

    private static ClassroomWork W(string id, ClassroomCourse c, string title, DateTime? due, bool time, SubmissionState state = SubmissionState.Open, bool late = false, string description = "", double? points = null) =>
        new() { Id = id, CourseId = c.Id, CourseName = c.Name, Title = title, Due = due, HasDueTime = time && due is not null, State = state, Late = late, Description = description, MaxPoints = points, WorkType = "ASSIGNMENT", Link = "https://classroom.google.com/c/" + c.Id + "/a/" + id };

    private static void Fill(LauncherItem item, bool empty = false, TimeSpan? age = null, string? error = null, string kind = "net")
    {
        var today = DateTime.Today;
        var algebra = new ClassroomCourse { Id = "c1", Name = "Algebra II", Section = "Period 3" };
        var chem = new ClassroomCourse { Id = "c2", Name = "Chemistry", Section = "Period 5" };
        var history = new ClassroomCourse { Id = "c3", Name = "AP World History" };
        var english = new ClassroomCourse { Id = "c4", Name = "English Literature" };
        var courses = new List<ClassroomCourse> { algebra, chem, history, english };
        var work = empty ? new List<ClassroomWork>() :
        [
            W("w1", algebra, "Problem set 4.2: quadratic functions", today.AddDays(-1).AddHours(23).AddMinutes(59), true, description: "Odd problems only. Show your work.", points: 20),
            W("w2", chem, "Lab report: titration", today.AddHours(15).AddMinutes(30), true, description: "Include the data table, the graph and two sources of error.", points: 50),
            W("w3", history, "Read chapter 7 and answer the questions", today.AddDays(1), false, description: "Questions 1 to 8 at the end of the chapter.", points: 10),
            W("w4", english, "Essay outline: The Great Gatsby", today.AddDays(2).AddHours(23).AddMinutes(59), true, description: "One page. Thesis plus three supporting points.", points: 25),
            W("w5", algebra, "Quiz 4 review", today.AddDays(3), false, SubmissionState.TurnedIn, description: "Practice quiz, not graded."),
            W("w6", chem, "Pre-lab questions", today.AddDays(4).AddHours(8), true, points: 10),
            W("w7", history, "Document analysis: Silk Road", today.AddDays(8), false, description: "Choose two primary sources.", points: 30),
            W("w8", english, "Poetry journal entry", today.AddDays(10).AddHours(23).AddMinutes(59), true, points: 5),
            W("w9", algebra, "Extra credit: tessellations", null, false),
        ];
        ConnectedKit.Fill(item, ClassroomLogic.ToCache(courses, work, null), "", age ?? TimeSpan.FromMinutes(4), error);
        if (error is not null) item.Options[ConnectedKit.CacheErrorKind] = kind;
    }

    // ---------- Options and refreshing ----------

    private static ClassroomOptions ReadOptions(WidgetContext ctx) =>
        new(ctx.GetInt("days"), ctx.GetInt("overdueDays"), ctx.GetBool("hideTurnedIn"), ctx.GetBool("showNoDue"), ctx.GetInt("maxItems"), ctx.Get("groupBy"), ctx.GetLines("courses"));

    private static ConnectedSource Source(WidgetContext ctx)
    {
        var lines = ctx.GetLines("courses");
        string signature = string.Join("|", lines);
        return new ConnectedSource(signature, OnlineLogic.Interval(ctx.GetNumber("refresh"), 5), () => GoogleKit.Guard(async () =>
        {
            var fetch = await ctx.Classroom.FetchAsync(course => ClassroomService.CourseMatches(course, lines));
            return ClassroomLogic.ToCache(fetch.Courses, fetch.Work, fetch.SubmissionsProblem);
        }), Strict: true);
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        bool ready = ctx.GoogleReady(GoogleFeatures.Classroom);
        if (ready) yield return ConnectedKit.RefreshAction(ctx, Source(ctx));
        if (ready) yield return new WidgetAction("Import into Homework…", Theme.Glyphs.Next, () => ImportIntoHomework(ctx));
        yield return new WidgetAction("Open Google Classroom", "", () => ctx.Open(ClassroomHome));
    }

    // ---------- Import into Homework ----------

    /// <summary>Copies the open assignments in the widget's window into a Homework widget. Assignments already imported (by their Classroom id) are left alone.</summary>
    internal static void ImportIntoHomework(WidgetContext ctx)
    {
        var data = ClassroomLogic.FromCache(ConnectedKit.Cached(ctx));
        if (data.Work.Count == 0) { ctx.Inform("There are no assignments to import yet. Wait for the widget to load, then try again.", ctx.Item.Name); return; }
        var targets = ctx.ItemsOfKind("homework");
        if (targets.Count == 0) { ctx.Inform("Add a Homework widget first (+ Add → Study → Homework), then import again.", ctx.Item.Name); return; }
        var options = ReadOptions(ctx);
        void Run(LauncherItem target)
        {
            var list = HomeworkLogic.Parse(target.Options.GetValueOrDefault(HomeworkLogic.DataKey));
            var candidates = ClassroomLogic.ImportCandidates(data.Work, DateTime.Now, options);
            var plan = ClassroomService.PlanImport(candidates, ClassroomLogic.KnownIds(list), HomeworkLogic.MaxItems - list.Count);
            var (added, _, _) = ClassroomLogic.AddTo(list, plan.ToAdd);
            if (added > 0)
            {
                target.Options[HomeworkLogic.DataKey] = HomeworkLogic.Serialize(list);
                ctx.Changed(target);
            }
            string text = added == 0 ? "Nothing new to import" : $"Added {added} assignment{(added == 1 ? "" : "s")} to {target.Name}";
            if (plan.Duplicates > 0) text += $" ({plan.Duplicates} already there)";
            if (plan.NoRoom > 0) text += $", {plan.NoRoom} did not fit";
            ctx.Status(text + ".");
        }
        if (targets.Count == 1) { Run(targets[0]); return; }
        var entries = new List<object> { StudyKit.Heading("Import into") };
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            entries.Add(StudyKit.Item(string.IsNullOrWhiteSpace(target.Name) ? "Homework " + (i + 1) : target.Name, () => Run(target)));
        }
        StudyKit.Popup(null, entries);
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var source = Source(ctx);
        string json = ConnectedKit.Cached(ctx);
        bool ready = ctx.IsSnapshot ? json.Length > 0 : ctx.GoogleReady(GoogleFeatures.Classroom);
        if (!ready)
        {
            ConnectedKit.Watch(ctx, source);
            return GoogleKit.Setup(ctx, GoogleFeatures.Classroom, Glyph, "Connect Google Classroom", "See what is due and what you have not turned in. Read-only. Many schools block outside apps; if yours does, add your Classroom calendar's iCal link in Settings instead.");
        }

        var data = ClassroomLogic.FromCache(json);
        var options = ReadOptions(ctx);
        var now = DateTime.Now;
        int open = ClassroomLogic.OpenCount(data.Work, now, options);
        int missing = ClassroomLogic.Candidates(data.Work, now, options, false).Count(w => ClassroomLogic.IsMissing(w, now));
        string overdueHex = StudyKit.ValidHex(ctx.Get("overdueColor"), "#E07A6A");
        FrameworkElement? badge = missing > 0 ? StudyKit.PillBox(ctx, $"{missing} missing", overdueHex) : open > 0 ? WidgetKit.Pill(ctx, $"{open} open") : null;
        var refresh = WidgetKit.IconButton(Theme.Glyphs.Refresh, "Refresh now", () => { ctx.Status("Checking Classroom…"); ConnectedKit.Refresh(ctx, source, force: true); }, 24);
        var (card, body) = WidgetKit.Frame(ctx, badge, refresh);
        ConnectedKit.Keep(ctx, source, tick: _ => ctx.Rebuild());
        ConnectedKit.AddFooter(ctx, body, ctx.GetBool("showUpdated"));
        if (data.SubmissionsProblem.Length > 0)
        {
            var note = WidgetKit.Muted(ctx, data.SubmissionsProblem, 10.5);
            note.TextWrapping = TextWrapping.Wrap;
            note.TextTrimming = TextTrimming.None;
            note.Margin = new Thickness(0, 6, 0, 0);
            DockPanel.SetDock(note, Dock.Bottom);
            body.Children.Add(note);
        }

        if (ConnectedKit.Fetched(ctx) is null)
        {
            body.Children.Add(ConnectedKit.Error(ctx) is { } problem
                ? ConnectedKit.Setup(ctx, "", "Can't reach Classroom", problem, ConnectedKit.ErrorIsAuth(ctx) ? "Open Settings" : "Try again", ConnectedKit.ErrorIsAuth(ctx) ? ctx.OpenSettings : () => ConnectedKit.Refresh(ctx, source, force: true), Theme.Danger)
                : ConnectedKit.Loading(ctx, "Checking Classroom…"));
            return card;
        }

        var layout = ClassroomLogic.Arrange(data.Work, now, options);
        if (layout.Sections.Count == 0)
        {
            var done = WidgetKit.Empty(ctx, data.Courses.Count == 0 ? "No active courses found." : $"Nothing due in the next {options.Days} day{(options.Days == 1 ? "" : "s")}. Enjoy it.");
            body.Children.Add(done);
            return card;
        }

        var overrides = ClassroomLogic.ParseCourseColors(ctx.GetLines("courseColors"));
        var list = new StackPanel();
        foreach (var section in layout.Sections)
        {
            if (section.Title.Length > 0)
            {
                Brush color = section.Key == "overdue" ? StudyKit.Hex(overdueHex, Theme.Danger) : section.Key == "d" + now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) || section.Title == "Today" ? ctx.Accent : Theme.Muted;
                if (section.Key.StartsWith("course:", StringComparison.Ordinal) && ctx.GetBool("showColor") && section.Items.Count > 0)
                    color = Theme.Accent(ClassroomLogic.ColorFor(section.Items[0].CourseId, section.Items[0].CourseName, overrides));
                var heading = StudyKit.SectionLabel(ctx, section.Title + "  " + section.Items.Count.ToString(CultureInfo.CurrentCulture), color);
                if (list.Children.Count == 0) heading.Margin = new Thickness(4, 0, 0, 3);
                list.Children.Add(heading);
            }
            foreach (var work in section.Items) list.Children.Add(Row(ctx, work, now, overrides, overdueHex, section.Key.StartsWith("course:", StringComparison.Ordinal)));
        }
        if (layout.More > 0)
        {
            var more = WidgetKit.Muted(ctx, $"+{layout.More} more", 11);
            more.Margin = new Thickness(6, 6, 0, 0);
            list.Children.Add(more);
        }
        body.Children.Add(StudyKit.ThinScroll(list));
        return card;
    }

    private static FrameworkElement Row(WidgetContext ctx, ClassroomWork w, DateTime now, IReadOnlyList<(string Match, string Color)> overrides, string overdueHex, bool underCourseHeading)
    {
        bool compact = ctx.GetBool("compact"), use24 = ctx.GetBool("clock24");
        string courseColor = ClassroomLogic.ColorFor(w.CourseId, w.CourseName, overrides);
        var status = ClassroomLogic.StatusOf(w, now, ctx.GetInt("soonDays"));
        bool done = status.Kind is ClassroomLogic.StatusKind.TurnedIn or ClassroomLogic.StatusKind.TurnedInLate or ClassroomLogic.StatusKind.Returned;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ctx.GetBool("showColor") ? 11 : 0) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (ctx.GetBool("showColor")) grid.Children.Add(new Border { Width = 4, CornerRadius = new CornerRadius(2), Background = Theme.Accent(courseColor), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1) });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var title = WidgetKit.Text(ctx, w.Title, compact ? 12 : 13, done ? Theme.Muted : ctx.TextBrush, bold: !done && !compact);
        if (done) title.TextDecorations = TextDecorations.Strikethrough;
        if (!compact) { title.TextWrapping = TextWrapping.Wrap; title.MaxHeight = ctx.Font(13) * 2.7; }
        text.Children.Add(title);
        var parts = new List<string> { ClassroomLogic.DueText(w, now, use24, CultureInfo.CurrentCulture) };
        if (ctx.GetBool("showCourse") && !underCourseHeading) parts.Insert(0, w.CourseName);
        if (ctx.GetBool("showPoints") && w.MaxPoints is { } points) parts.Add(points.ToString("0.##", CultureInfo.CurrentCulture) + " pts");
        if (compact) { if (ctx.Width >= 300) title.ToolTip = w.CourseName + " · " + string.Join(" · ", parts.Skip(ctx.GetBool("showCourse") && !underCourseHeading ? 1 : 0)); }
        else text.Children.Add(WidgetKit.Muted(ctx, string.Join(" · ", parts), 11));
        if (!compact && ctx.GetBool("showDescription") && w.Description.Length > 0)
        {
            var description = WidgetKit.Muted(ctx, w.Description, 10.5);
            description.TextWrapping = TextWrapping.Wrap;
            description.MaxHeight = ctx.Font(10.5) * 1.45 * 2;
            description.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(description);
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        FrameworkElement? pill = status.Kind switch
        {
            ClassroomLogic.StatusKind.Missing => StudyKit.PillBox(ctx, "Missing", overdueHex),
            ClassroomLogic.StatusKind.DueToday => StudyKit.PillBox(ctx, "Today", "#E0A24F"),
            ClassroomLogic.StatusKind.Soon => compact ? null : StudyKit.PillBox(ctx, char.ToUpperInvariant(status.Text.Replace("Due ", "")[0]) + status.Text.Replace("Due ", "")[1..], "#C2A14D"),
            ClassroomLogic.StatusKind.TurnedInLate => StudyKit.PillBox(ctx, "Late", "#E0A24F"),
            ClassroomLogic.StatusKind.TurnedIn => StudyKit.PillBox(ctx, "Turned in", "#9B9A5E"),
            ClassroomLogic.StatusKind.Returned => StudyKit.PillBox(ctx, "Returned", "#5E9BA6"),
            _ => null,
        };
        if (pill is not null)
        {
            pill.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(pill, 2);
            grid.Children.Add(pill);
        }
        var row = WidgetKit.Row(grid);
        if (compact) row.Padding = new Thickness(6, 3, 6, 3);
        if (done) row.Opacity = 0.7;
        string url = ConnectedLogic.IsLinkOn(w.Link, "google.com") ? w.Link : "https://classroom.google.com/";
        WidgetKit.Clickable(row, () => ctx.Open(url), w.CourseName + "\nClick: open in Google Classroom");
        return row;
    }
}
