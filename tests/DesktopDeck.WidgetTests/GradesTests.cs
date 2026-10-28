using DesktopDeck.Widgets;

public static class GradesTests
{
    private static readonly List<GradesLogic.Band> Scale = GradesLogic.ParseScale(GradesLogic.DefaultScale.Split('\n'));

    public static void DefaultScaleHasThirteenBandsHighestFirst()
    {
        Expect.Equal(13, Scale.Count, "A+ down to F");
        Expect.Equal("A+", Scale[0].Letter, "top");
        Expect.Equal("F", Scale[^1].Letter, "bottom");
        Expect.True(Scale.Zip(Scale.Skip(1), (a, b) => a.Min > b.Min).All(x => x), "descending");
    }

    public static void PercentsMapToLettersAndPoints()
    {
        Expect.Equal("A", GradesLogic.BandOf(93, Scale).Letter, "93 is an A");
        Expect.Equal("A-", GradesLogic.BandOf(92.99, Scale).Letter, "92.99 is an A-");
        Expect.Equal("B", GradesLogic.BandOf(85, Scale).Letter, "85");
        Expect.Equal(3.7, GradesLogic.BandOf(91, Scale).Points, "A- is 3.7");
        Expect.Equal("F", GradesLogic.BandOf(12, Scale).Letter, "low");
        Expect.Equal("F", GradesLogic.BandOf(-5, Scale).Letter, "below zero still lands somewhere");
        Expect.Equal("A+", GradesLogic.BandOf(120, Scale).Letter, "extra credit");
    }

    public static void ParsesPercentFractionAndLetterGrades()
    {
        Expect.Equal(92.0, GradesLogic.ParseGrade("92", Scale), "plain");
        Expect.Equal(92.5, GradesLogic.ParseGrade("92.5%", Scale), "percent sign");
        Expect.Equal(92.5, GradesLogic.ParseGrade("92,5", Scale), "decimal comma");
        Expect.Equal(90.0, GradesLogic.ParseGrade("18/20", Scale), "fraction");
        Expect.Equal(91.5, GradesLogic.ParseGrade("A-", Scale), "letter is the middle of its band");
        Expect.Equal(95.0, GradesLogic.ParseGrade("a", Scale), "lower case letter");
        Expect.Equal(98.5, GradesLogic.ParseGrade("A+", Scale), "top band");
        Expect.Equal(50.0, GradesLogic.ParseGrade("F", Scale), "bottom band");
        Expect.True(GradesLogic.ParseGrade("hello", Scale) is null, "nonsense");
        Expect.True(GradesLogic.ParseGrade("-4", Scale) is null, "negative");
        Expect.True(GradesLogic.ParseGrade("5000", Scale) is null, "absurd");
        Expect.True(GradesLogic.ParseGrade("", Scale) is null, "empty");
        Expect.True(GradesLogic.ParseGrade("3/0", Scale) is null, "divide by zero");
    }

    public static void CoursesParseWithDefaults()
    {
        var courses = GradesLogic.ParseCourses(["Math | 92 | 3", "Chem | 85 | 1 | AP", "History | 78", "Latin | A", "Art", "# comment", "Bad | wibble | 1", "Gym | 90 | 0"], Scale);
        Expect.Equal(7, courses.Count, "comment skipped");
        Expect.Equal(3.0, courses[0].Credits, "credits");
        Expect.Equal("ap", courses[1].Level, "AP level");
        Expect.Equal(1.0, courses[2].Credits, "credits default to 1");
        Expect.Equal(95.0, courses[3].Percent, "letter grade");
        Expect.True(courses[4].Percent is null && courses[4].Understood, "no grade yet is fine");
        Expect.True(!courses[5].Understood, "bad grade is flagged");
        Expect.True(!courses[6].Understood, "zero credits is flagged");
        Expect.Equal("honors", GradesLogic.LevelOf("Honors"), "honors");
    }

    public static void AveragesAreCreditWeighted()
    {
        var courses = GradesLogic.ParseCourses(["Math | 92 | 3", "Chem | 85 | 1 | AP", "History | 78 | 1", "Latin | A | 1", "Art"], Scale);
        var s = GradesLogic.Summarize(courses, Scale);
        Expect.Near(89.0, s.Average!.Value, 1e-9, "weighted average");
        Expect.Near(3.4, s.Gpa!.Value, 1e-9, "4.0 GPA");
        Expect.Near(21.4 / 6, s.WeightedGpa!.Value, 1e-9, "AP adds a point to the weighted GPA");
        Expect.Equal(6.0, s.Credits, "credits counted");
        Expect.Equal(4, s.Graded, "ungraded classes are left out");
    }

    public static void HonorsAndApBonusesAreConfigurable()
    {
        var courses = GradesLogic.ParseCourses(["A | 95 | 1 | AP", "B | 95 | 1 | H", "C | 95 | 1"], Scale);
        var s = GradesLogic.Summarize(courses, Scale, honorsBonus: 0.5, apBonus: 1.0);
        Expect.Near(4.0, s.Gpa!.Value, 1e-9, "unweighted");
        Expect.Near(4.5, s.WeightedGpa!.Value, 1e-9, "(5 + 4.5 + 4) / 3");
        var none = GradesLogic.Summarize(courses, Scale, 0, 0);
        Expect.Near(4.0, none.WeightedGpa!.Value, 1e-9, "no bonus");
        var failing = GradesLogic.Summarize(GradesLogic.ParseCourses(["X | 20 | 1 | AP"], Scale), Scale);
        Expect.Near(0.0, failing.WeightedGpa!.Value, 1e-9, "an F gets no bonus");
    }

    public static void NoGradesMeansNoNumbers()
    {
        var s = GradesLogic.Summarize(GradesLogic.ParseCourses(["Art", "Gym | - "], Scale), Scale);
        Expect.True(s.Average is null && s.Gpa is null && s.WeightedGpa is null, "nothing to average");
        Expect.True(GradesLogic.Summarize([], Scale).Gpa is null, "empty");
    }

    public static void CustomScalesWork()
    {
        var pass = GradesLogic.ParseScale(["50 | P | 4", "junk", "x | y | z"]);
        Expect.Equal(2, pass.Count, "a zero band is added");
        Expect.Equal("F", pass[^1].Letter, "F added");
        Expect.Equal("P", GradesLogic.BandOf(70, pass).Letter, "pass");
        Expect.Equal(13, GradesLogic.ParseScale([]).Count, "an empty scale falls back to the default");
        Expect.Equal(0, GradesLogic.Bucket(95, Scale), "A bucket");
        Expect.Equal(1, GradesLogic.Bucket(81, Scale), "B bucket");
        Expect.Equal(2, GradesLogic.Bucket(75, Scale), "C bucket");
        Expect.Equal(3, GradesLogic.Bucket(61, Scale), "D bucket");
        Expect.Equal(4, GradesLogic.Bucket(10, Scale), "F bucket");
    }

    public static void FinalExamFormula()
    {
        var easy = GradesLogic.NeededOnFinal(95, 20, 80);
        Expect.Equal(GradesLogic.FinalStatus.Needs, easy.Status, "reachable");
        Expect.Near(20, easy.Needed, 1e-9, "(80 - 76) / 0.2");
        var even = GradesLogic.NeededOnFinal(90, 25, 90);
        Expect.Near(90, even.Needed, 1e-9, "(90 - 67.5) / 0.25");
        var hard = GradesLogic.NeededOnFinal(85, 20, 90);
        Expect.Equal(GradesLogic.FinalStatus.OutOfReach, hard.Status, "more than 100");
        Expect.Near(110, hard.Needed, 1e-9, "(90 - 68) / 0.2");
        var safe = GradesLogic.NeededOnFinal(100, 10, 60);
        Expect.Equal(GradesLogic.FinalStatus.Safe, safe.Status, "already safe");
        Expect.Equal(GradesLogic.FinalStatus.NoFinal, GradesLogic.NeededOnFinal(90, 0, 90).Status, "no final");
        Expect.Equal(GradesLogic.FinalStatus.NoFinal, GradesLogic.NeededOnFinal(90, 150, 90).Status, "more than all of it");
        var everything = GradesLogic.NeededOnFinal(50, 100, 93);
        Expect.Near(93, everything.Needed, 1e-9, "a final worth everything is just the target");
        Expect.Equal(GradesLogic.FinalStatus.Needs, GradesLogic.NeededOnFinal(80, 25, 85).Status, "needing exactly 100 is reachable");
        Expect.Equal(GradesLogic.FinalStatus.OutOfReach, GradesLogic.NeededOnFinal(80, 20, 90).Status, "needing 130 is not");
        Expect.Equal(GradesLogic.FinalStatus.Needs, GradesLogic.NeededOnFinal(80, 20, 90, 150).Status, "extra credit raises the ceiling");
        Expect.Near(88, GradesLogic.ProjectedGrade(85, 20, 100), 1e-9, "projected");
    }

    public static void TargetsAcceptLettersAndNumbers()
    {
        Expect.Equal(90.0, GradesLogic.ParseTarget("90", Scale), "number");
        Expect.Equal(90.0, GradesLogic.ParseTarget("A-", Scale), "A- means reaching 90");
        Expect.Equal(93.0, GradesLogic.ParseTarget("a", Scale), "A means reaching 93");
        Expect.Equal(85.0, GradesLogic.ParseTarget("85%", Scale), "percent");
        Expect.True(GradesLogic.ParseTarget("Z", Scale) is null, "unknown");
    }

    public static void FinalTableListsWholeLetters()
    {
        var table = GradesLogic.FinalTable(88, 20, Scale);
        Expect.Equal("A,B,C,D", string.Join(',', table.Select(t => t.Letter)), "whole letters only");
        Expect.Near((93 - 88 * 0.8) / 0.2, table[0].Result.Needed, 1e-9, "A");
        Expect.Equal(GradesLogic.FinalStatus.Safe, table[3].Result.Status, "D is already safe");
    }

    public static void TextIsPlain()
    {
        Expect.Equal("91.5%", GradesLogic.FormatPercent(91.5), "one decimal");
        Expect.Equal("92%", GradesLogic.FormatPercent(91.97, 0), "rounded");
        Expect.Equal("90%", GradesLogic.FormatPercent(90.0), "no trailing zero");
        Expect.Equal("3.40", GradesLogic.FormatGpa(3.4), "gpa");
        Expect.True(GradesLogic.DescribeFinal(GradesLogic.NeededOnFinal(85, 20, 90), 90).Contains("more than a perfect"), "out of reach text");
        Expect.True(GradesLogic.DescribeFinal(GradesLogic.NeededOnFinal(95, 20, 80), 80).Contains("20%"), "needs text");
    }

    public static void SortModes()
    {
        var courses = GradesLogic.ParseCourses(["B | 80", "A | 95", "C | 70", "D"], Scale);
        Expect.Equal("A,B,C,D", string.Join(',', GradesLogic.Sort(courses, "high").Select(c => c.Name)), "high first, ungraded last");
        Expect.Equal("C,B,A,D", string.Join(',', GradesLogic.Sort(courses, "low").Select(c => c.Name)), "low first, ungraded last");
        Expect.Equal("B,A,C,D", string.Join(',', GradesLogic.Sort(courses, "entered").Select(c => c.Name)), "as written");
    }

    public static void EditingTheClassText()
    {
        string text = "Math | 92 | 3\nChem | 85\n# note\nLatin | A";
        Expect.Equal("Math | 95 | 3\nChem | 85\n# note\nLatin | A", GradesLogic.SetField(text, "math", 1, "95"), "grade changed, rest untouched");
        Expect.Equal("Math | 92 | 3\nChem | 85 | 2\n# note\nLatin | A", GradesLogic.SetField(text, "Chem", 2, "2"), "credits added");
        Expect.Equal("Math | 92 | 3\nChem | 85 | 1 | AP\n# note\nLatin | A", GradesLogic.SetField(text, "Chem", 3, "AP"), "level pads credits");
        Expect.Equal(text, GradesLogic.SetField(text, "Art", 1, "90"), "unknown class changes nothing");
        Expect.Equal("Math | 92 | 3\n# note\nLatin | A", GradesLogic.RemoveClass(text, "Chem"), "removed");
        Expect.Equal(text + "\nArt | 90", GradesLogic.AddClass(text, " Art | 90 "), "added");
        Expect.Equal("Art | 90", GradesLogic.AddClass("", "Art | 90"), "added to nothing");
        var courses = GradesLogic.ParseCourses(["Latin | A- | 1"], Scale);
        Expect.Equal("A-", courses[0].GradeText, "the typed grade is kept for editing");
    }
}
