using DesktopDeck.Models;
using DesktopDeck.Services;

/// <summary>Automation rules: matching (time, app, displays), which rule wins, the next time change, and when layouts are switched or switched back. No UI.</summary>
public static class AutomationTests
{
    // 2026-10-05 is a Monday.
    private static DateTime At(int day, int hour, int minute) => new(2026, 10, 4 + day, hour, minute, 0); // day 1 = Mon … 7 = Sun
    private static AutomationContext Ctx(DateTime now, string? process = null, params string[] displays) => new(now, process, displays.Length == 0 ? ["d1"] : displays);

    private static AutomationRule Time(string start, string end, string layout = "L1", params int[] days) =>
        new() { Trigger = AutomationTriggers.Time, Start = start, End = end, Days = [.. days], LayoutId = layout };

    private static AutomationRule App(string process, string layout = "L1", bool back = false, string layer = LayoutLayers.Floating) =>
        new() { Trigger = AutomationTriggers.App, Process = process, LayoutId = layout, SwitchBack = back, Layer = layer };

    public static void Run(Action<bool, string> check, string folder)
    {
        Matching(check);
        Winners(check);
        NextChange(check);
        Edges(check);
        Normalizing(check, folder);
    }

    private static void Matching(Action<bool, string> check)
    {
        var work = Time("09:00", "17:00", "L1", 1, 2, 3, 4, 5);
        check(AutomationEngine.Matches(work, Ctx(At(1, 9, 0))) && AutomationEngine.Matches(work, Ctx(At(1, 16, 59))), "Time rule: matches inside the window, start included");
        check(!AutomationEngine.Matches(work, Ctx(At(1, 17, 0))) && !AutomationEngine.Matches(work, Ctx(At(1, 8, 59))), "Time rule: end excluded, before start no");
        check(!AutomationEngine.Matches(work, Ctx(At(6, 12, 0))) && !AutomationEngine.Matches(work, Ctx(At(7, 12, 0))), "Time rule: only on its days");
        check(AutomationEngine.Matches(Time("09:00", "17:00"), Ctx(At(7, 12, 0))), "Time rule: no days means every day");

        var night = Time("22:00", "06:00", "L1", 5); // Friday night
        check(AutomationEngine.Matches(night, Ctx(At(5, 23, 0))), "Overnight: Friday 23:00 matches");
        check(AutomationEngine.Matches(night, Ctx(At(6, 5, 59))), "Overnight: Saturday 05:59 belongs to Friday's window");
        check(!AutomationEngine.Matches(night, Ctx(At(6, 6, 0))), "Overnight: ends at 06:00");
        check(!AutomationEngine.Matches(night, Ctx(At(5, 5, 0))), "Overnight: Friday 05:00 belongs to Thursday's window, not matched");
        check(!AutomationEngine.Matches(night, Ctx(At(6, 23, 0))), "Overnight: Saturday night is not Friday");
        check(AutomationEngine.Matches(Time("08:00", "08:00", "L1", 2), Ctx(At(2, 3, 0))) && !AutomationEngine.Matches(Time("08:00", "08:00", "L1", 2), Ctx(At(3, 3, 0))), "Equal start and end means the whole day (on its days)");
        check(!AutomationEngine.Matches(new AutomationRule { Trigger = AutomationTriggers.Time, Start = "bad", End = "17:00" }, Ctx(At(1, 12, 0))), "A rule with an unreadable time never matches");

        check(AutomationEngine.ProcessKey("Code.exe") == "code" && AutomationEngine.ProcessKey(@"C:\Apps\CODE.EXE") == "code" && AutomationEngine.ProcessKey(" code ") == "code", "Process names compare without case, path or .exe");
        check(AutomationEngine.Matches(App("Code.exe"), Ctx(At(1, 1, 0), "code")) && !AutomationEngine.Matches(App("Code.exe"), Ctx(At(1, 1, 0), "Code - Insiders.exe")) && !AutomationEngine.Matches(App(""), Ctx(At(1, 1, 0), "")), "App rule: same app only, an empty name never matches");
        check(AutomationEngine.IsIgnored("VLC.exe", ["vlc"]) && !AutomationEngine.IsIgnored("mpv.exe", ["vlc"]) && !AutomationEngine.IsIgnored(null, ["vlc"]), "Ignore list matches by process key");

        // A portrait second monitor with no taskbar (work area = whole monitor), as on a real two-monitor setup.
        var side = (-1080, -179, 0, 1741);
        check(!AutomationEngine.LooksFullscreen(0x14C70000, false, (-1088, -179, 8, 1749), side), "Fullscreen: a framed window filling a taskbar-less monitor (a chat app snapped full height) is not fullscreen");
        check(!AutomationEngine.LooksFullscreen(0x17CF0000, true, (-8, -8, 2568, 1448), (0, 0, 2560, 1440)), "Fullscreen: a maximized window is not fullscreen");
        check(AutomationEngine.LooksFullscreen(0x96000000, false, (0, 0, 2560, 1440), (0, 0, 2560, 1440)), "Fullscreen: a frameless popup covering the monitor (a game, F11 browser, video) is fullscreen");
        check(AutomationEngine.LooksFullscreen(0x16000000, false, side, side), "Fullscreen: a frameless window covering the second monitor is fullscreen");
        check(!AutomationEngine.LooksFullscreen(0x96000000, false, (0, 0, 1920, 1080), (0, 0, 2560, 1440)), "Fullscreen: a frameless window smaller than its monitor is not fullscreen");

        var count = new AutomationRule { Trigger = AutomationTriggers.Displays, DisplayCount = 2 };
        check(AutomationEngine.Matches(count, Ctx(At(1, 1, 0), null, "a", "b")) && !AutomationEngine.Matches(count, Ctx(At(1, 1, 0), null, "a")), "Display count: exactly N");
        var set = new AutomationRule { Trigger = AutomationTriggers.Displays, DisplayIds = ["A", "b"] };
        check(AutomationEngine.Matches(set, Ctx(At(1, 1, 0), null, "B", "a")), "Display set: order and case do not matter");
        check(!AutomationEngine.Matches(set, Ctx(At(1, 1, 0), null, "a")) && !AutomationEngine.Matches(set, Ctx(At(1, 1, 0), null, "a", "b", "c")), "Display set: exactly those displays (laptop-only and a third monitor differ)");
    }

    private static void Winners(Action<bool, string> check)
    {
        bool Exists(string layer, string id) => id != "gone";
        var disabled = App("Code.exe", "L1"); disabled.Enabled = false;
        var missing = App("Code.exe", "gone");
        var second = App("Code.exe", "L2");
        var third = App("Code.exe", "L3");
        var rules = new[] { disabled, missing, second, third };
        var winner = AutomationEngine.Winner(rules, LayoutLayers.Floating, Ctx(At(1, 1, 0), "code"), Exists);
        check(winner == second, "Winner: skips a disabled rule and a rule whose layout was deleted, then takes the first match");
        check(AutomationEngine.Winner(rules, LayoutLayers.Background, Ctx(At(1, 1, 0), "code"), Exists) is null, "Winner: only rules of that layer count");
    }

    private static void NextChange(Action<bool, string> check)
    {
        var rules = new List<AutomationRule> { Time("09:00", "17:00") };
        check(AutomationEngine.NextChange(rules, At(1, 8, 0)) == At(1, 9, 0), "Next change: the start");
        check(AutomationEngine.NextChange(rules, At(1, 12, 0)) == At(1, 17, 0), "Next change: the end");
        check(AutomationEngine.NextChange(rules, At(1, 18, 0)) == At(2, 0, 0), "Next change: midnight (the day may matter) before tomorrow's start");
        rules[0].Enabled = false;
        check(AutomationEngine.NextChange(rules, At(1, 8, 0)) is null && AutomationEngine.NextChange([App("x")], At(1, 8, 0)) is null, "Next change: none without enabled time rules");
    }

    private static void Edges(Action<bool, string> check)
    {
        string floatingActive = "base", backgroundActive = "bg";
        bool Exists(string layer, string id) => id is "base" or "bg" or "code" or "study" or "focus" or "other";
        string Active(string layer) => layer == LayoutLayers.Floating ? floatingActive : backgroundActive;
        List<AutomationSwitch> Step(AutomationRunner runner, AutomationRule[] rules, DateTime now, string? process = null)
        {
            var result = runner.Step(rules, Ctx(now, process), Active, Exists);
            foreach (var s in result) { if (s.Layer == LayoutLayers.Floating) floatingActive = s.LayoutId; else backgroundActive = s.LayoutId; }
            return result;
        }

        var coding = App("Code.exe", "code", back: true);
        var runner = new AutomationRunner();
        var rules = new[] { coding };
        check(Step(runner, rules, At(1, 9, 0), "chrome.exe").Count == 0 && floatingActive == "base", "Edge: nothing happens while no rule applies (also the first evaluation)");
        var first = Step(runner, rules, At(1, 9, 1), "Code.exe");
        check(first.Count == 1 && first[0].LayoutId == "code" && first[0].Rule == coding && floatingActive == "code" && runner.RestoreTarget(LayoutLayers.Floating) == "base", "Edge: the rule starts to apply, switches and remembers where to go back to");
        check(Step(runner, rules, At(1, 9, 2), "Code.exe").Count == 0 && Step(runner, rules, At(1, 9, 3), "code").Count == 0, "Edge: no repeat switch while the rule keeps applying");
        var back = Step(runner, rules, At(1, 9, 4), "chrome.exe");
        check(back.Count == 1 && back[0].LayoutId == "base" && back[0].Rule is null && floatingActive == "base", "Switch back: restores the previous layout when the rule stops applying");
        check(Step(runner, rules, At(1, 9, 5), "mail.exe").Count == 0, "Switch back: only once");

        // Manual switches are respected until the trigger changes.
        Step(runner, rules, At(1, 10, 0), "Code.exe");
        floatingActive = "other"; // switched by hand while the rule is on
        check(Step(runner, rules, At(1, 10, 1), "Code.exe").Count == 0 && floatingActive == "other", "Manual: a hand-picked layout is not undone while the trigger is unchanged");
        check(Step(runner, rules, At(1, 10, 2), "chrome.exe").Count == 0 && floatingActive == "other", "Manual: and not switched back either, when the rule ends");
        var again = Step(runner, rules, At(1, 10, 3), "Code.exe");
        check(again.Count == 1 && floatingActive == "code" && runner.RestoreTarget(LayoutLayers.Floating) == "other", "Manual: the next trigger change applies the rule again, going back to the hand-picked layout");
        Step(runner, rules, At(1, 10, 4), "chrome.exe");
        check(floatingActive == "other", "Manual: switch back returns to the hand-picked layout");

        // Without switch back, nothing is restored.
        floatingActive = "base";
        var plain = new[] { App("Code.exe", "code") };
        var r2 = new AutomationRunner();
        Step(r2, plain, At(1, 9, 0), "Code.exe");
        check(floatingActive == "code" && Step(r2, plain, At(1, 9, 1), "chrome.exe").Count == 0 && floatingActive == "code", "No switch back: the layout stays");

        // A chain of rules keeps the layout from before the first one.
        floatingActive = "base";
        var chain = new[] { App("Code.exe", "code", back: true), App("Figma.exe", "study", back: true) };
        var r3 = new AutomationRunner();
        Step(r3, chain, At(1, 9, 0), "Code.exe");
        Step(r3, chain, At(1, 9, 1), "Figma.exe");
        check(floatingActive == "study" && r3.RestoreTarget(LayoutLayers.Floating) == "base", "Chain: moving from one switch-back rule to another keeps the layout from before the first");
        Step(r3, chain, At(1, 9, 2), "chrome.exe");
        check(floatingActive == "base", "Chain: and returns to it at the end");

        // Layers are independent; time rules switch at their start and end.
        floatingActive = "base"; backgroundActive = "bg";
        var timed = new[] { new AutomationRule { Trigger = AutomationTriggers.Time, Start = "18:00", End = "21:00", Layer = LayoutLayers.Background, LayoutId = "focus", SwitchBack = true }, App("Code.exe", "code") };
        var r4 = new AutomationRunner();
        Step(r4, timed, At(1, 17, 0), "Code.exe");
        check(floatingActive == "code" && backgroundActive == "bg", "Layers: each layer follows its own rules");
        Step(r4, timed, At(1, 18, 0), "Code.exe");
        check(backgroundActive == "focus" && floatingActive == "code", "Time rule: switches at its start");
        Step(r4, timed, At(1, 21, 0), "Code.exe");
        check(backgroundActive == "bg", "Time rule: switches back at its end");

        // A deleted layout stops a rule; Reset applies the rules again as at startup.
        floatingActive = "base";
        var gone = new[] { App("Code.exe", "gone") };
        check(Step(new AutomationRunner(), gone, At(1, 9, 0), "Code.exe").Count == 0, "A rule whose layout is gone is skipped");
        floatingActive = "base";
        var r5 = new AutomationRunner();
        Step(r5, plain, At(1, 9, 0), "Code.exe");
        floatingActive = "base";
        check(Step(r5, plain, At(1, 9, 1), "Code.exe").Count == 0, "Apply now is needed to re-assert a rule that has not changed");
        r5.Reset();
        check(Step(r5, plain, At(1, 9, 2), "Code.exe").Count == 1 && floatingActive == "code", "Reset: the rules apply again as at startup");
    }

    private static void Normalizing(Action<bool, string> check, string folder)
    {
        var store = new SettingsStore(Path.Combine(folder, "automation"));
        var settings = new DeckSettings();
        settings.Automation.Rules =
        [
            new AutomationRule { Id = "same", Trigger = "weird", Layer = "nope", Days = [3, 3, 9, -1, 1], Start = "9am", End = "x", Process = "  Code.exe ", DisplayCount = 99, DisplayIds = ["a", "A", " "] },
            new AutomationRule { Id = "same", Trigger = AutomationTriggers.App, Process = "vlc.exe" },
        ];
        settings.Automation.FullscreenIgnore = ["VLC.exe", "vlc", "", "C:\\Games\\Game.exe"];
        store.Save(settings);
        var loaded = store.Load().Automation;
        var a = loaded.Rules[0];
        check(a.Trigger == AutomationTriggers.Time && a.Layer == LayoutLayers.Floating && a.Days.SequenceEqual([1, 3]) && a.Start == "09:00" && a.End == "17:00" && a.Process == "Code.exe" && a.DisplayCount == 16 && a.DisplayIds.Count == 1, "Normalize: repairs triggers, days, times, counts and display ids");
        check(loaded.Rules[0].Id != loaded.Rules[1].Id, "Normalize: duplicate rule ids are made unique");
        check(loaded.FullscreenIgnore.SequenceEqual(["vlc", "game"]), "Normalize: the ignore list is stored as process keys, without repeats");
        check(loaded.Enabled && loaded.Notify && loaded.HideInFullscreen && !loaded.HideOrbInFullscreen, "Automation defaults: rules on, notice on, hide in fullscreen on, control button stays");

        var round = new DeckSettings();
        round.Automation.Rules = [new AutomationRule { Trigger = AutomationTriggers.Displays, DisplayIds = ["MONITOR\\GSM5B09\\{x}\\0001"], LayoutId = "L9", Layer = LayoutLayers.Background, SwitchBack = true }];
        round.Automation.HideOrbInFullscreen = true;
        store.Save(round);
        var back = store.Load().Automation;
        check(back.Rules.Count == 1 && back.Rules[0].DisplayIds[0].Contains("GSM5B09") && back.Rules[0].SwitchBack && back.HideOrbInFullscreen, "Automation settings round-trip");
        check(AutomationText.DisplayLabel(back.Rules[0].DisplayIds[0]) == "GSM5B09" && AutomationText.DaysText([1, 2, 3, 4, 5]) == "Weekdays" && AutomationText.DaysText([]) == "Every day" && AutomationText.DaysText([6, 0]) == "Weekends", "Descriptions read well");
    }
}
