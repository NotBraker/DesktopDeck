namespace DesktopDeck.Models;

/// <summary>What makes an automation rule apply.</summary>
public static class AutomationTriggers
{
    /// <summary>Certain days of the week between two times of day.</summary>
    public const string Time = "time";
    /// <summary>A given app (process name) has the foreground window.</summary>
    public const string App = "app";
    /// <summary>The monitor setup: a number of displays, or exactly this set of displays.</summary>
    public const string Displays = "displays";

    public static bool IsValid(string? trigger) => trigger is Time or App or Displays;
}

/// <summary>"When &lt;trigger&gt; → switch the &lt;layer&gt; layout to &lt;layout&gt;". Ordered: the first matching rule of a layer wins.</summary>
public sealed class AutomationRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;
    /// <summary>One of <see cref="AutomationTriggers"/>.</summary>
    public string Trigger { get; set; } = AutomationTriggers.Time;
    /// <summary>The layer to switch (<see cref="LayoutLayers"/>) and the layout in it. The id, not the name, so renaming a layout keeps the rule working.</summary>
    public string Layer { get; set; } = LayoutLayers.Floating;
    public string LayoutId { get; set; } = "";
    /// <summary>Go back to the layout that was in use before when the rule stops applying.</summary>
    public bool SwitchBack { get; set; }

    // Time of day.
    /// <summary>Days of the week (0 = Sunday … 6 = Saturday); empty means every day. An overnight window belongs to the day it starts on.</summary>
    public List<int> Days { get; set; } = new();
    public string Start { get; set; } = "09:00";
    /// <summary>The end of the window; equal to <see cref="Start"/> means the whole day.</summary>
    public string End { get; set; } = "17:00";

    // App in the foreground.
    /// <summary>The process name, e.g. "Code.exe" (case does not matter, ".exe" is optional).</summary>
    public string Process { get; set; } = "";

    // Monitor setup.
    /// <summary>Exactly this many displays are connected (used when <see cref="DisplayIds"/> is empty).</summary>
    public int DisplayCount { get; set; } = 1;
    /// <summary>Exactly these displays are connected (their stable ids), for "docked" versus "laptop only".</summary>
    public List<string> DisplayIds { get; set; } = new();

    public AutomationRule Copy()
    {
        var copy = (AutomationRule)MemberwiseClone();
        copy.Days = [.. Days];
        copy.DisplayIds = [.. DisplayIds];
        return copy;
    }
}

/// <summary>Everything the automation rules (in the Layouts tab) and the fullscreen option keep.</summary>
public sealed class AutomationSettings
{
    public const int MaxRules = 100;

    /// <summary>Master switch for the layout rules.</summary>
    public bool Enabled { get; set; } = true;
    public List<AutomationRule> Rules { get; set; } = new();
    /// <summary>Show a small notice when a rule switches a layout.</summary>
    public bool Notify { get; set; } = true;

    /// <summary>Hide the floating widgets while a fullscreen app, game or video is in front.</summary>
    public bool HideInFullscreen { get; set; } = true;
    /// <summary>Hide the control button too.</summary>
    public bool HideOrbInFullscreen { get; set; }
    /// <summary>Apps that never count as fullscreen (process names).</summary>
    public List<string> FullscreenIgnore { get; set; } = new();

    public static AutomationSettings Normalized(AutomationSettings? value)
    {
        var settings = value ?? new AutomationSettings();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var rules = new List<AutomationRule>();
        foreach (var rule in settings.Rules ?? new())
        {
            if (rule is null || rules.Count >= MaxRules) continue;
            if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id)) { rule.Id = Guid.NewGuid().ToString("N"); ids.Add(rule.Id); }
            if (!AutomationTriggers.IsValid(rule.Trigger)) rule.Trigger = AutomationTriggers.Time;
            if (!LayoutLayers.IsValid(rule.Layer)) rule.Layer = LayoutLayers.Floating;
            rule.LayoutId ??= "";
            rule.Days = (rule.Days ?? new()).Where(d => d is >= 0 and <= 6).Distinct().OrderBy(d => d).ToList();
            rule.Start = Planner.ParseTime(rule.Start) ?? "09:00";
            rule.End = Planner.ParseTime(rule.End) ?? "17:00";
            rule.Process = (rule.Process ?? "").Trim();
            rule.DisplayCount = Math.Clamp(rule.DisplayCount, 1, 16);
            rule.DisplayIds = (rule.DisplayIds ?? new()).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            rules.Add(rule);
        }
        settings.Rules = rules;
        settings.FullscreenIgnore = (settings.FullscreenIgnore ?? new()).Select(AutomationEngine.ProcessKey).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Take(100).ToList();
        return settings;
    }
}

/// <summary>What the rules look at: the time, the app in front and the connected displays. Plain values, so it can be tested without a desktop.</summary>
public sealed record AutomationContext(DateTime Now, string? Process, IReadOnlyCollection<string> DisplayIds);

/// <summary>A layout the rules want switched on.</summary>
/// <param name="Rule">The rule that caused it, or null when it is a switch back.</param>
public sealed record AutomationSwitch(string Layer, string LayoutId, AutomationRule? Rule);

/// <summary>Pure rule evaluation: whether a rule matches, which rule wins, and when the time next changes. No UI and no clock of its own.</summary>
public static class AutomationEngine
{
    /// <summary>"Code.exe", "code" and "C:\Apps\CODE.EXE" are the same app.</summary>
    public static string ProcessKey(string? name)
    {
        string text = (name ?? "").Trim().Trim('"');
        int slash = Math.Max(text.LastIndexOf('\\'), text.LastIndexOf('/'));
        if (slash >= 0) text = text[(slash + 1)..];
        if (text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) text = text[..^4];
        return text.Trim().ToLowerInvariant();
    }

    public static bool SameProcess(string? a, string? b)
    {
        string key = ProcessKey(a);
        return key.Length > 0 && key == ProcessKey(b);
    }

    /// <summary>True when the app should never count as fullscreen.</summary>
    public static bool IsIgnored(string? process, IEnumerable<string> ignore) => ignore.Any(i => SameProcess(i, process));

    private const uint WsCaption = 0x00C00000, WsThickFrame = 0x00040000;

    /// <summary>
    /// A window is fullscreen when it covers its whole monitor (not just the work area), is not maximized, and has no title bar or resize frame.
    /// Games, F11 browsers and video players drop their frame; a normal window snapped or stretched over a monitor without a taskbar keeps it.
    /// </summary>
    public static bool LooksFullscreen(uint style, bool zoomed, (int Left, int Top, int Right, int Bottom) window, (int Left, int Top, int Right, int Bottom) monitor)
    {
        if (zoomed || (style & WsCaption) == WsCaption || (style & WsThickFrame) != 0) return false;
        return window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }

    /// <summary>The display ids as one comparable set (case and order do not matter).</summary>
    private static HashSet<string> IdSet(IEnumerable<string> ids) => new(ids.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()), StringComparer.OrdinalIgnoreCase);

    public static bool Matches(AutomationRule rule, AutomationContext context) => rule.Trigger switch
    {
        AutomationTriggers.Time => TimeMatches(rule, context.Now),
        AutomationTriggers.App => SameProcess(rule.Process, context.Process),
        AutomationTriggers.Displays => DisplaysMatch(rule, context.DisplayIds),
        _ => false,
    };

    public static bool DisplaysMatch(AutomationRule rule, IReadOnlyCollection<string> connected)
    {
        if (rule.DisplayIds.Count == 0) return connected.Count == rule.DisplayCount;
        return IdSet(rule.DisplayIds).SetEquals(IdSet(connected));
    }

    private static bool DayOn(AutomationRule rule, DayOfWeek day) => rule.Days.Count == 0 || rule.Days.Contains((int)day);

    /// <summary>
    /// Days and a window of the day. Overnight windows (start after end) run into the next morning and belong to the day they start on:
    /// "Fri 22:00–06:00" is on Friday night and Saturday until 06:00. Start equal to end means the whole day.
    /// </summary>
    public static bool TimeMatches(AutomationRule rule, DateTime now)
    {
        if (Planner.Minutes(rule.Start) is not { } start || Planner.Minutes(rule.End) is not { } end) return false;
        int minute = now.Hour * 60 + now.Minute;
        if (start == end) return DayOn(rule, now.DayOfWeek);
        if (start < end) return DayOn(rule, now.DayOfWeek) && minute >= start && minute < end;
        if (minute >= start) return DayOn(rule, now.DayOfWeek);
        return minute < end && DayOn(rule, now.AddDays(-1).DayOfWeek);
    }

    /// <summary>The first enabled rule of the layer that applies right now and whose layout still exists, or null.</summary>
    public static AutomationRule? Winner(IEnumerable<AutomationRule> rules, string layer, AutomationContext context, Func<string, string, bool> layoutExists) =>
        rules.FirstOrDefault(r => r.Enabled && r.Layer == layer && layoutExists(layer, r.LayoutId) && Matches(r, context));

    /// <summary>
    /// The next moment a time rule can start or stop applying (a window edge, or midnight when the day matters), so one timer can wake the rules
    /// instead of polling. Null when no enabled rule looks at the time.
    /// </summary>
    public static DateTime? NextChange(IEnumerable<AutomationRule> rules, DateTime now)
    {
        DateTime? best = null;
        bool any = false;
        foreach (var rule in rules.Where(r => r.Enabled && r.Trigger == AutomationTriggers.Time))
        {
            any = true;
            foreach (var minutes in new[] { Planner.Minutes(rule.Start), Planner.Minutes(rule.End) })
            {
                if (minutes is not { } m) continue;
                foreach (int add in new[] { 0, 1 })
                {
                    var at = now.Date.AddDays(add).AddMinutes(m);
                    if (at > now && (best is null || at < best)) best = at;
                }
            }
        }
        if (!any) return null;
        var midnight = now.Date.AddDays(1);
        return best is null || midnight < best ? midnight : best;
    }
}

/// <summary>
/// Decides which layouts the rules switch, and remembers just enough to do it politely. It acts only when the winning rule of a layer
/// <em>changes</em> (an edge, not a level): so a layout you switch by hand stays until the next trigger change, and calls that change nothing
/// (a taskbar moving, a timer firing early) do nothing. Per layer it also keeps the layout to go back to for rules with "switch back".
/// </summary>
public sealed class AutomationRunner
{
    private sealed class LayerState
    {
        /// <summary>The rule that won last time (empty = none); null before the first evaluation.</summary>
        public string? WinnerId;
        /// <summary>The layout the rules last switched this layer to; when the layer is on something else, you switched by hand.</summary>
        public string Applied = "";
        /// <summary>Where "switch back" returns to: the layout in use before the rules took over.</summary>
        public string Restore = "";
    }

    private readonly Dictionary<string, LayerState> layers = new() { [LayoutLayers.Floating] = new(), [LayoutLayers.Background] = new() };

    /// <summary>Forgets everything, so the next step acts as at startup (used by "Apply rules now").</summary>
    public void Reset() { foreach (var state in layers.Values) { state.WinnerId = null; state.Applied = ""; state.Restore = ""; } }

    /// <summary>The layout that "switch back" would return a layer to (empty = none), for tests and the panel.</summary>
    public string RestoreTarget(string layer) => layers[layer].Restore;

    /// <param name="active">The id of the layout in use in a layer.</param>
    /// <param name="layoutExists">Whether a layout (layer, id) exists.</param>
    /// <returns>The switches to make now, in no particular order; the runner assumes they all succeed.</returns>
    public List<AutomationSwitch> Step(IReadOnlyList<AutomationRule> rules, AutomationContext context, Func<string, string> active, Func<string, string, bool> layoutExists)
    {
        var result = new List<AutomationSwitch>();
        foreach (var (layer, state) in layers)
        {
            var winner = AutomationEngine.Winner(rules, layer, context, layoutExists);
            string winnerId = winner?.Id ?? "";
            if (state.WinnerId == winnerId) continue;
            bool first = state.WinnerId is null;
            state.WinnerId = winnerId;
            string current = active(layer) ?? "";
            // You switched by hand since the rules last did: that choice is respected, so there is nothing of ours to switch back.
            bool manual = state.Applied.Length > 0 && current != state.Applied;
            if (winner is null)
            {
                if (!first && !manual && state.Restore.Length > 0 && state.Restore != current && layoutExists(layer, state.Restore))
                {
                    result.Add(new AutomationSwitch(layer, state.Restore, null));
                }
                state.Applied = ""; state.Restore = "";
                continue;
            }
            if (!winner.SwitchBack) state.Restore = "";
            else if (manual || state.Restore.Length == 0) state.Restore = current == winner.LayoutId ? "" : current;
            state.Applied = winner.LayoutId;
            if (current != winner.LayoutId) result.Add(new AutomationSwitch(layer, winner.LayoutId, winner));
        }
        return result;
    }
}

/// <summary>Plain-language descriptions of rules, for the panel and the notice.</summary>
public static class AutomationText
{
    private static readonly string[] DayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    public static string DaysText(IReadOnlyCollection<int> days)
    {
        if (days.Count == 0 || days.Count == 7) return "Every day";
        var set = days.ToHashSet();
        if (set.SetEquals([1, 2, 3, 4, 5])) return "Weekdays";
        if (set.SetEquals([0, 6])) return "Weekends";
        return string.Join(", ", new[] { 1, 2, 3, 4, 5, 6, 0 }.Where(set.Contains).Select(d => DayNames[d]));
    }

    /// <summary>"every day", "on weekdays", "on Mon, Wed".</summary>
    public static string DaysPhrase(IReadOnlyCollection<int> days)
    {
        string text = DaysText(days);
        return text == "Every day" ? "every day" : "on " + (text is "Weekdays" or "Weekends" ? text.ToLowerInvariant() : text);
    }

    /// <summary>"it's 09:00–17:00 on weekdays", "Code.exe is in front", "2 displays connected".</summary>
    public static string When(AutomationRule rule) => rule.Trigger switch
    {
        AutomationTriggers.Time => rule.Start == rule.End ? $"it's {DaysPhrase(rule.Days)}, all day" : $"it's {rule.Start}–{rule.End} {DaysPhrase(rule.Days)}",
        AutomationTriggers.App => string.IsNullOrWhiteSpace(rule.Process) ? "an app is in front" : $"{rule.Process} is in front",
        _ => rule.DisplayIds.Count > 0 ? $"this exact set of {rule.DisplayIds.Count} display{(rule.DisplayIds.Count == 1 ? "" : "s")} is connected" : $"{rule.DisplayCount} display{(rule.DisplayCount == 1 ? "" : "s")} connected",
    };

    public static string LayerName(string layer) => layer == LayoutLayers.Floating ? "pinned" : "desktop";

    /// <summary>A short name for a display from its stable id (the monitor model part of the device path), else the id.</summary>
    public static string DisplayLabel(string id)
    {
        var parts = id.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && parts[0].Equals("MONITOR", StringComparison.OrdinalIgnoreCase) ? parts[1] : id;
    }
}
