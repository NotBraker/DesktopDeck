namespace DesktopDeck.Widgets;

/// <summary>Where a plug-in widget's setting sits in the Customize window.</summary>
internal enum OptionTier
{
    /// <summary>Always shown: what the widget shows and where it gets its data.</summary>
    Main,
    /// <summary>Behind the collapsed "More options".</summary>
    More,
    /// <summary>Behind the collapsed "Look", with the card style and colors.</summary>
    Look,
}

/// <summary>
/// Sorts a plug-in widget's settings into <see cref="OptionTier"/>s so the Customize window opens short. Nothing is removed or renamed:
/// every setting is still built, saved and read, only tucked away. No widget file has to change.
/// The rule, by the setting's group: "Behavior" goes to More options; "Layout" and "Colors" go to Look; any other group stays in
/// the main part. <see cref="AdvancedKeys"/> and <see cref="AdvancedGroups"/> list the exceptions that go to More options anyway.
/// </summary>
internal static class OptionTiers
{
    internal const string BehaviorGroup = "Behavior", LayoutGroup = "Layout", ColorsGroup = "Colors";

    /// <summary>Per widget kind: settings (by key) in a main group that are rarely changed, so they go to More options.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> AdvancedKeys = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["recent-files"] = ["extensions", "network"],
        ["month-calendar"] = ["marks"],
        ["flashcards"] = ["deck2Name", "deck2", "showBox", "showIntervals"],
        ["grades"] = ["showWeighted", "showAverage", "showCredits"],
        ["periodic-table"] = ["defaultElement", "showConfig", "showFBlock", "showMasses"],
        ["journal"] = ["labels"],
        ["focus"] = ["history"],
        ["agenda"] = ["showEmptyDays"],
        ["bluetooth"] = ["showAddress", "batteryDisconnected", "showHint"],
        ["github"] = ["types", "participating"],
        ["gmail"] = ["query", "badge"],
        ["notion"] = ["statusProperty", "dateProperty", "addStatus", "addDate", "showAdd"],
        ["obs"] = ["host", "port", "micInput", "hiddenScenes", "maxScenes", "showReplay"],
        ["air-quality"] = ["scale", "pm10", "ozone", "nitrogen_dioxide"],
        ["currency"] = ["inverse"],
        ["rss"] = ["perFeed", "maxAgeDays", "summaryLines"],
        ["on-this-day"] = ["language"],
        ["battery"] = ["plan", "saver", "health"],
        ["system-stats"] = ["showCommit", "showPeak"],
        ["disk-space"] = ["network", "hidden"],
        ["network-speed"] = ["totals", "ip", "name"],
        ["pc-info"] = ["build", "boot", "screens"],
        ["top-apps"] = ["exclude"],
        ["bedtime"] = ["weekendBedtime", "weekendWake"],
        ["break-reminder"] = ["stretchEvery", "stretchSeconds", "snooze"],
        ["hydration"] = ["sizes"],
        ["breathing"] = ["inhale", "holdIn", "exhale", "holdOut"],
        ["dice"] = ["min", "max", "headsLabel", "tailsLabel"],
        ["unit-converter"] = ["rows"],
        ["word-of-day"] = ["partStyle", "highlight", "difficultyBadge", "difficulty", "partFilter"],
        ["color-palette"] = ["rgb"],
        ["slideshow"] = ["subfolders", "counter"],
    };

    /// <summary>Per widget kind: whole groups that go to More options (they keep their heading there).</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> AdvancedGroups = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["bedtime"] = ["Reminders", "Sleep cycles"],
        ["break-reminder"] = ["Active hours", "Alerts"],
        ["hydration"] = ["Reminders"],
    };

    internal static bool IsAdvancedGroup(string kind, string group)
        => AdvancedGroups.TryGetValue(kind, out var groups) && groups.Contains(group, StringComparer.Ordinal);

    internal static OptionTier Of(string kind, string key, string group)
    {
        if (group == LayoutGroup || group == ColorsGroup) return OptionTier.Look;
        if (group == BehaviorGroup || IsAdvancedGroup(kind, group)) return OptionTier.More;
        return AdvancedKeys.TryGetValue(kind, out var keys) && keys.Contains(key, StringComparer.Ordinal) ? OptionTier.More : OptionTier.Main;
    }

    /// <summary>What is wrong with the tables for this widget (empty = fine): names that match no setting, and entries the group rule already covers.</summary>
    internal static IReadOnlyList<string> Problems(string kind, IReadOnlyCollection<(string Key, string Group)> options)
    {
        var problems = new List<string>();
        var groups = options.Select(o => o.Group).ToHashSet(StringComparer.Ordinal);
        if (AdvancedKeys.TryGetValue(kind, out var keys))
        {
            foreach (var dup in keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1)) problems.Add($"{kind}: \"{dup.Key}\" is listed twice in OptionTiers");
            foreach (var key in keys)
            {
                var found = options.Where(o => o.Key == key).ToList();
                if (found.Count == 0) problems.Add($"{kind}: OptionTiers names \"{key}\", which is not one of its settings");
                else if (Of(kind, key, found[0].Group) != OptionTier.More || found[0].Group is BehaviorGroup || IsAdvancedGroup(kind, found[0].Group))
                    problems.Add($"{kind}: \"{key}\" is already in More options or Look by its group ({found[0].Group}), so it does not belong in OptionTiers.AdvancedKeys");
            }
        }
        if (AdvancedGroups.TryGetValue(kind, out var advancedGroups))
            foreach (var group in advancedGroups)
                if (!groups.Contains(group)) problems.Add($"{kind}: OptionTiers names the group \"{group}\", which none of its settings use");
        var keyCount = options.Select(o => o.Key).Distinct(StringComparer.Ordinal).Count();
        var tiered = options.Select(o => Of(kind, o.Key, o.Group)).Count();
        if (keyCount != options.Count || tiered != options.Count) problems.Add($"{kind}: a setting key is used twice, so it cannot sit in exactly one tier");
        return problems;
    }

    /// <summary>The kinds in the tables that are not among <paramref name="registered"/> (a typo, or a widget that was removed).</summary>
    internal static IReadOnlyList<string> UnknownKinds(IEnumerable<string> registered)
    {
        var known = registered.ToHashSet(StringComparer.Ordinal);
        return AdvancedKeys.Keys.Concat(AdvancedGroups.Keys).Distinct(StringComparer.Ordinal).Where(k => !known.Contains(k)).ToList();
    }
}
