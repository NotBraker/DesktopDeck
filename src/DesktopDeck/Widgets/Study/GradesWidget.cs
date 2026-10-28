using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Grades and GPA: a bar per class colored by grade, the weighted average, a 4.0 GPA (and an optional 5.0 weighted one),
/// and a "what do I need on the final" calculator. Classes are lines "Class | grade | credits | level" in the options;
/// click a class to change its grade. The math is in GradesLogic.cs.
/// </summary>
internal sealed class GradesWidget : IWidget
{
    public string Kind => "grades";
    public string Name => "Grades & GPA";
    public string Description => "Class grades as bars, GPA, and a final-exam calculator";
    public string Section => "Study";
    public string Glyph => "";
    public string Accent => "#9B9A5E";
    public (double Width, double Height) DefaultSize => (372, 324);
    public (double Width, double Height) MinimumSize => (276, 204);

    private const string DefaultClasses = "History | 94 | 1\nLatin | A- | 1\nChem | 88 | 1 | AP\nMath | 91 | 1\nEnglish | 90 | 1 | H";

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Lines("classes", "Classes", DefaultClasses, "What to show", "One per line: Class | grade | credits | level. The grade can be a percent (92), a fraction (18/20) or a letter (A-). Level is optional: H for honors, AP for AP/IB."),
        OptionSpec.Toggle("showGpa", "Show the GPA", true, "What to show"),
        OptionSpec.Toggle("showWeighted", "Show the weighted GPA (5.0 scale)", false, "What to show", "Adds the honors and AP bonuses below."),
        OptionSpec.Toggle("showAverage", "Show the weighted average", true, "What to show"),
        OptionSpec.Toggle("showLetter", "Show the letter next to each grade", true, "What to show"),
        OptionSpec.Toggle("showPercent", "Show the percent next to each grade", true, "What to show"),
        OptionSpec.Toggle("showCredits", "Show credits on each class", false, "What to show"),
        OptionSpec.Choice("sort", "Order of classes", "entered", ["entered", "high", "low", "name"], ["As written", "Highest first", "Lowest first", "A to Z"], "Layout"),
        OptionSpec.Number("barHeight", "Bar thickness", 7, 3, 16, "Layout"),
        OptionSpec.Toggle("compact", "Compact rows", false, "Layout", "Name, grade and bar on a single line."),
        OptionSpec.Number("decimals", "Decimal places in percents", 1, 0, 2, "Layout"),
        OptionSpec.Choice("colorMode", "Bar colors", "grade", ["grade", "accent"], ["By grade", "Accent color"], "Colors"),
        OptionSpec.Color("aColor", "A color", "#6FA37A", "Colors"),
        OptionSpec.Color("bColor", "B color", "#9B9A5E", "Colors"),
        OptionSpec.Color("cColor", "C color", "#C2A14D", "Colors"),
        OptionSpec.Color("dColor", "D color", "#D98A5B", "Colors"),
        OptionSpec.Color("fColor", "F color", "#E07A6A", "Colors"),
        OptionSpec.Lines("scale", "Letter scale", GradesLogic.DefaultScale, "Behavior", "One band per line: lowest percent | letter | GPA points. Change it to match your school."),
        OptionSpec.Number("honorsBonus", "Honors bonus (GPA points)", 0.5, 0, 2, "Behavior"),
        OptionSpec.Number("apBonus", "AP / IB bonus (GPA points)", 1, 0, 2, "Behavior"),
        OptionSpec.Number("finalWeight", "Final exam counts for (%)", 20, 1, 100, "Behavior", "Used as the suggestion in the final-exam calculator."),
        OptionSpec.Text("targetGrade", "Target grade", "90", "Behavior", "A percent (90) or a letter (A-): the final-exam calculator aims for it."),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => { },
        item =>
        {
            item.Options["classes"] = "Biology | 97.5 | 1\nEconomics | 82 | 0.5\nGeometry | 74 | 1\nSpanish | B+ | 1\nArt | 58 | 0.5\nPE | 100 | 0.5";
            item.Options["showWeighted"] = "true";
            item.Options["sort"] = "high";
            item.Options["compact"] = "true";
            item.Accent = "#5E9BA6";
            item.Width = 276; item.Height = 276;
        },
        item =>
        {
            item.Options["classes"] = "Physics | 91 | 1 | AP\nLiterature | 88 | 1 | H\nCalculus | 79 | 1 | AP\nChoir | A | 0.5\nGerman | 93 | 1\nStudy hall | | 0.5";
            item.Options["showWeighted"] = "true";
            item.Options["showCredits"] = "true";
            item.Options["colorMode"] = "accent";
            item.Width = 468; item.Height = 276;
        },
    ];

    // ---------- Editing ----------

    private static List<GradesLogic.Band> Scale(WidgetContext ctx) => GradesLogic.ParseScale(ctx.GetLines("scale"));

    private static void EditGrade(WidgetContext ctx, GradesLogic.Course course)
    {
        string? text = ctx.Ask("Update grade", $"{course.Name}: new grade (percent, fraction like 18/20, or a letter)", course.GradeText);
        if (text is null) return;
        if (text.Trim().Length > 0 && GradesLogic.ParseGrade(text, Scale(ctx)) is null) { ctx.Status("Couldn't read \"" + text.Trim() + "\" as a grade."); return; }
        ctx.Set("classes", GradesLogic.SetField(ctx.Get("classes"), course.Name, 1, text));
    }

    private static void AddClass(WidgetContext ctx)
    {
        string? text = ctx.Ask("Add a class", "Class | grade | credits   (for example: Biology | 92 | 1)");
        if (string.IsNullOrWhiteSpace(text)) return;
        var parsed = GradesLogic.ParseCourses([text], Scale(ctx));
        if (parsed.Count == 0 || !parsed[0].Understood) { ctx.Status("Couldn't read that class. Try: Biology | 92 | 1"); return; }
        ctx.Set("classes", GradesLogic.AddClass(ctx.Get("classes"), text));
    }

    private static void CalculateFinal(WidgetContext ctx, string? className, string currentText)
    {
        var scale = Scale(ctx);
        string title = "What do I need on the final?";
        string? current = ctx.Ask(title, className is null ? "Your grade in the class right now (percent or letter)" : $"Your grade in {className} right now (percent or letter)", currentText);
        if (current is null) return;
        if (GradesLogic.ParseGrade(current, scale) is not { } now) { ctx.Status("Couldn't read \"" + current.Trim() + "\" as a grade."); return; }
        string? weightText = ctx.Ask(title, "How much is the final worth? (percent of the class grade)", ctx.Get("finalWeight"));
        if (weightText is null) return;
        if (!double.TryParse(weightText.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double weight) || weight is <= 0 or > 100) { ctx.Status("The final's weight should be between 1 and 100."); return; }
        string? targetText = ctx.Ask(title, "The grade you want in the class (percent or letter)", ctx.Get("targetGrade"));
        if (targetText is null) return;
        if (GradesLogic.ParseTarget(targetText, scale) is not { } target) { ctx.Status("Couldn't read \"" + targetText.Trim() + "\" as a target."); return; }

        int decimals = ctx.GetInt("decimals");
        var result = GradesLogic.NeededOnFinal(now, weight, target);
        var text = new StringBuilder();
        text.AppendLine(GradesLogic.DescribeFinal(result, target, decimals));
        text.AppendLine();
        text.AppendLine($"With {GradesLogic.FormatPercent(now, decimals)} now and a final worth {GradesLogic.FormatPercent(weight, 0)}:");
        foreach (var (letter, min, r) in GradesLogic.FinalTable(now, weight, scale))
        {
            string need = r.Status switch
            {
                GradesLogic.FinalStatus.Safe => "already secured",
                GradesLogic.FinalStatus.OutOfReach => "out of reach (" + GradesLogic.FormatPercent(r.Needed, decimals) + ")",
                _ => GradesLogic.FormatPercent(r.Needed, decimals) + " on the final",
            };
            text.AppendLine($"   {letter} ({GradesLogic.FormatPercent(min, 0)}):  {need}");
        }
        ctx.Inform(text.ToString().TrimEnd(), className is null ? title : $"{title}  ({className})");
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("What do I need on the final…", "", () =>
        {
            var courses = GradesLogic.ParseCourses(ctx.GetLines("classes"), Scale(ctx));
            var summary = GradesLogic.Summarize(courses, Scale(ctx), ctx.GetNumber("honorsBonus"), ctx.GetNumber("apBonus"));
            CalculateFinal(ctx, null, summary.Average is { } a ? GradesLogic.FormatPercent(a, 1).TrimEnd('%') : "");
        });
        yield return new WidgetAction("Add a class…", Theme.Glyphs.Add, () => AddClass(ctx));
    }

    // ---------- Drawing ----------

    public FrameworkElement Build(WidgetContext ctx)
    {
        var scale = Scale(ctx);
        var all = GradesLogic.ParseCourses(ctx.GetLines("classes"), scale);
        var summary = GradesLogic.Summarize(all, scale, ctx.GetNumber("honorsBonus"), ctx.GetNumber("apBonus"));
        int decimals = ctx.GetInt("decimals");
        bool compact = ctx.GetBool("compact");
        double maxPoints = scale.Max(b => b.Points);

        var calc = WidgetKit.IconButton("", "What do I need on the final?", () => { }, 24);
        var add = WidgetKit.IconButton(Theme.Glyphs.Add, "Add a class", () => AddClass(ctx), 24);
        var (card, body) = WidgetKit.Frame(ctx, null, calc, add);
        calc.Click += (_, e) =>
        {
            e.Handled = true;
            var entries = new List<object> { StudyKit.Heading("Final exam for…") };
            foreach (var c in all.Where(c => c.Percent is not null))
                entries.Add(StudyKit.Item(c.Name, () => CalculateFinal(ctx, c.Name, string.IsNullOrEmpty(c.GradeText) ? GradesLogic.FormatPercent(c.Percent ?? 0, 1).TrimEnd('%') : c.GradeText)));
            entries.Add(new Separator());
            entries.Add(StudyKit.Item("Another grade…", () => CalculateFinal(ctx, null, "")));
            StudyKit.Popup(calc, entries);
        };

        // Summary tiles.
        var tiles = new List<(string Label, string Value, string Unit, Brush Color)>();
        if (ctx.GetBool("showGpa")) tiles.Add(("GPA", summary.Gpa is { } g ? GradesLogic.FormatGpa(g) : "–", "/ " + GradesLogic.FormatGpa(maxPoints).TrimEnd('0').TrimEnd('.') + (maxPoints % 1 == 0 ? ".0" : ""), ctx.TextBrush));
        if (ctx.GetBool("showWeighted")) tiles.Add(("Weighted", summary.WeightedGpa is { } w ? GradesLogic.FormatGpa(w) : "–", "/ " + (maxPoints + Math.Max(ctx.GetNumber("apBonus"), ctx.GetNumber("honorsBonus"))).ToString("0.0", CultureInfo.InvariantCulture), ctx.TextBrush));
        if (ctx.GetBool("showAverage")) tiles.Add(("Average", summary.Average is { } a ? a.ToString(decimals <= 0 ? "0" : "0." + new string('0', decimals), CultureInfo.InvariantCulture) : "–", "%", ctx.TextBrush));
        if (tiles.Count > 0)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            for (int i = 0; i < tiles.Count; i++)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition());
                var (label, value, unit, color) = tiles[i];
                var stack = new StackPanel { Orientation = Orientation.Vertical };
                var line = new StackPanel { Orientation = Orientation.Horizontal };
                line.Children.Add(Theme.Label(value, ctx.Font(tiles.Count == 3 && ctx.Width < 330 ? 17 : 20), color));
                var u = Theme.Label(" " + unit, ctx.Font(10.5), Theme.Muted);
                u.VerticalAlignment = VerticalAlignment.Bottom; u.Margin = new Thickness(0, 0, 0, 3);
                line.Children.Add(u);
                ((TextBlock)line.Children[0]).FontWeight = FontWeights.SemiBold;
                stack.Children.Add(line);
                stack.Children.Add(Theme.Label(label.ToUpperInvariant(), ctx.Font(9), Theme.Muted));
                var tile = new Border { CornerRadius = new CornerRadius(10), Background = Theme.Surface3, Padding = new Thickness(10, 6, 8, 6), Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0), Child = stack };
                Grid.SetColumn(tile, i);
                row.Children.Add(tile);
            }
            DockPanel.SetDock(row, Dock.Top);
            body.Children.Add(row);
        }

        // One bar per class.
        var list = new StackPanel();
        var courses = GradesLogic.Sort(all, ctx.Get("sort"));
        if (courses.Count == 0) list.Children.Add(WidgetKit.Empty(ctx, "No classes yet. Press + or open Customize and write one per line: Class | grade | credits."));
        double barHeight = ctx.GetNumber("barHeight");
        foreach (var course in courses) list.Children.Add(ClassRow(ctx, course, scale, decimals, compact, barHeight));
        int bad = courses.Count(c => !c.Understood);
        if (bad > 0)
        {
            var note = WidgetKit.Muted(ctx, bad == 1 ? "1 line couldn't be read." : $"{bad} lines couldn't be read.", 10.5);
            note.Margin = new Thickness(6, 4, 0, 0);
            list.Children.Add(note);
        }
        body.Children.Add(StudyKit.ThinScroll(list));
        return card;
    }

    private static Brush GradeBrush(WidgetContext ctx, double percent, IReadOnlyList<GradesLogic.Band> scale)
    {
        if (ctx.Get("colorMode") == "accent") return ctx.Accent;
        return GradesLogic.Bucket(percent, scale) switch
        {
            0 => StudyKit.Hex(ctx.Get("aColor"), Theme.Olive),
            1 => StudyKit.Hex(ctx.Get("bColor"), Theme.Olive),
            2 => StudyKit.Hex(ctx.Get("cColor"), Theme.Olive),
            3 => StudyKit.Hex(ctx.Get("dColor"), Theme.Danger),
            _ => StudyKit.Hex(ctx.Get("fColor"), Theme.Danger),
        };
    }

    private static FrameworkElement ClassRow(WidgetContext ctx, GradesLogic.Course course, IReadOnlyList<GradesLogic.Band> scale, int decimals, bool compact, double barHeight)
    {
        double? percent = course.Percent;
        Brush color = percent is { } p ? GradeBrush(ctx, p, scale) : Theme.Dim;
        string percentText = percent is { } v ? GradesLogic.FormatPercent(v, decimals) : course.Understood ? "–" : "?";
        string letter = percent is { } v2 ? GradesLogic.BandOf(v2, scale).Letter : "";

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (compact) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ctx.Width < 330 ? 60 : 96) });
        else { grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); }

        string nameText = course.Name + (ctx.GetBool("showCredits") ? $"  ·  {course.Credits.ToString("0.##", CultureInfo.InvariantCulture)} cr" : "") + (course.Level == "ap" ? "  AP" : course.Level == "honors" ? "  H" : "");
        var name = WidgetKit.Text(ctx, nameText, 12.5, course.Understood ? ctx.TextBrush : Theme.Muted);
        name.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(name);

        var gradeLine = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, compact ? 8 : 0, 0) };
        if (ctx.GetBool("showPercent") || percent is null) gradeLine.Children.Add(WidgetKit.Text(ctx, percentText, 12.5, percent is null ? Theme.Muted : ctx.TextBrush));
        if (ctx.GetBool("showLetter") && letter.Length > 0)
        {
            var l = WidgetKit.Text(ctx, letter, 12.5, color, true);
            l.Margin = new Thickness(gradeLine.Children.Count > 0 ? 7 : 0, 0, 0, 0);
            gradeLine.Children.Add(l);
        }
        Grid.SetColumn(gradeLine, 1);
        grid.Children.Add(gradeLine);

        var bar = WidgetKit.Bar((percent ?? 0) / 100.0, color, compact ? Math.Min(barHeight, 8) : barHeight);
        bar.VerticalAlignment = VerticalAlignment.Center;
        if (compact) { Grid.SetColumn(bar, 2); }
        else { Grid.SetRow(bar, 1); Grid.SetColumnSpan(bar, 2); bar.Margin = new Thickness(0, 4, 0, 1); }
        grid.Children.Add(bar);

        var row = WidgetKit.Row(grid);
        row.Padding = new Thickness(6, compact ? 4 : 5, 6, compact ? 4 : 5);
        row.ToolTip = course.Understood ? $"{course.Name}: click to change the grade, right-click for more" : "This line couldn't be read. Fix it in Customize.";
        WidgetKit.Clickable(row, () => EditGrade(ctx, course));
        StudyKit.AttachMenu(row, [
            StudyKit.Item("Change grade…", () => EditGrade(ctx, course)),
            StudyKit.Item("What do I need on the final…", () => CalculateFinal(ctx, course.Name, course.GradeText)),
            new Separator(),
            StudyKit.Item("Remove class", () => ctx.Set("classes", GradesLogic.RemoveClass(ctx.Get("classes"), course.Name))),
        ]);
        return row;
    }
}
