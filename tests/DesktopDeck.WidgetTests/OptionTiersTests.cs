using DesktopDeck.Widgets;

/// <summary>
/// The Customize window sorts every plug-in widget's settings into main, More options and Look. These tests cover the rule and the tables;
/// the check against the real widgets' settings (every key in exactly one tier, no name that matches nothing) runs in
/// <c>DesktopDeck.exe --smoke-test --widgets</c>, which has the widgets.
/// </summary>
public static class OptionTiersTests
{
    private static readonly (string Key, string Group)[] Sample =
    [
        ("show", "What to show"), ("size", "Layout"), ("color", "Colors"), ("refresh", "Behavior"), ("extra", "What to show"), ("ping", "Reminders"),
    ];

    public static void GroupRuleSortsBehaviorLayoutAndColors()
    {
        Expect.Equal(OptionTier.Main, OptionTiers.Of("unknown", "show", "What to show"), "a main group stays");
        Expect.Equal(OptionTier.More, OptionTiers.Of("unknown", "refresh", "Behavior"), "Behavior goes to More options");
        Expect.Equal(OptionTier.Look, OptionTiers.Of("unknown", "size", "Layout"), "Layout goes to Look");
        Expect.Equal(OptionTier.Look, OptionTiers.Of("unknown", "color", "Colors"), "Colors goes to Look");
    }

    public static void ListedKeysAndGroupsGoToMoreOptions()
    {
        Expect.Equal(OptionTier.More, OptionTiers.Of("flashcards", "deck2", "What to show"), "a listed key");
        Expect.Equal(OptionTier.Main, OptionTiers.Of("flashcards", "deck1", "What to show"), "an unlisted key in the same group");
        Expect.Equal(OptionTier.More, OptionTiers.Of("hydration", "remind", "Reminders"), "a listed group");
        Expect.Equal(OptionTier.Main, OptionTiers.Of("dice", "deck2", "What to show"), "the same key on another widget");
    }

    public static void EveryKeyIsInExactlyOneTier()
    {
        foreach (var kind in new[] { "flashcards", "hydration", "unknown" })
        {
            var counts = Sample.GroupBy(o => OptionTiers.Of(kind, o.Key, o.Group)).Sum(g => g.Count());
            Expect.Equal(Sample.Length, counts, kind + ": each setting has one tier");
        }
    }

    public static void ProblemsFindNamesThatMatchNothing()
    {
        Expect.Equal(0, OptionTiers.Problems("unknown", Sample).Count, "a widget with no entries is fine");
        Expect.True(OptionTiers.Problems("flashcards", Sample).Count >= 4, "flashcards entries do not match the sample settings");
        var listed = OptionTiers.AdvancedKeys["dice"][0];
        var fine = new[] { (listed, "What to show"), ("other", "What to show") };
        Expect.Equal(0, OptionTiers.Problems("dice", fine).Count - OptionTiers.AdvancedKeys["dice"].Length + 1, "only the other listed keys are missing");
        Expect.True(OptionTiers.Problems("dice", new[] { (listed, "Behavior") }).Any(p => p.Contains("already")), "a key the group rule already covers is flagged");
    }

    public static void TablesHaveNoDuplicatesAndKnownKindsAreReported()
    {
        foreach (var (kind, keys) in OptionTiers.AdvancedKeys)
            Expect.Equal(keys.Length, keys.Distinct().Count(), kind + ": no key listed twice");
        Expect.Equal(0, OptionTiers.UnknownKinds(OptionTiers.AdvancedKeys.Keys.Concat(OptionTiers.AdvancedGroups.Keys)).Count, "all listed kinds known");
        Expect.True(OptionTiers.UnknownKinds(["dice"]).Contains("flashcards"), "a kind that is not registered is reported");
    }
}
