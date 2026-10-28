using System.Windows.Threading;
using DesktopDeck.Layout;
using DesktopDeck.Models;
using Microsoft.Win32;

namespace DesktopDeck;

// Automation: layout rules (time of day, app in front, monitor setup) and hiding the floating widgets while a fullscreen app is in front.
// Event-driven: a foreground hook, one timer aimed at the next time-rule edge, and display changes; nothing polls.
internal sealed partial class DeckHub
{
    private readonly AutomationRunner runner = new();
    private ForegroundWatcher? watcher;
    private AutomationToast? toast;
    private DispatcherTimer? ruleTimer, displayTimer, retryTimer;
    private bool fullscreenOn;

    /// <summary>True while a fullscreen app has the floating widgets hidden (never saved).</summary>
    public bool FloatsSuppressed { get; private set; }
    /// <summary>True while a fullscreen app has the control button hidden (never saved).</summary>
    public bool OrbSuppressed { get; private set; }
    /// <summary>The foreground app the rules last saw (null when unknown or automation is not running).</summary>
    public string? ForegroundProcess => watcher?.Process;
    public bool AutomationRunning => watcher is not null;
    public AutomationSettings Automation => Settings.Automation;

    // ---------- Start and stop ----------

    /// <summary>Starts watching (the real app only, never a preview or a smoke run) and applies the rules once.</summary>
    public void StartAutomation()
    {
        if (watcher is not null || closing || preview || offscreen) return;
        watcher = new ForegroundWatcher(() => Settings.Automation.FullscreenIgnore);
        watcher.ProcessChanged += _ => EvaluateAutomation();
        watcher.FullscreenChanged += on => { fullscreenOn = on; ApplyFullscreen(); };
        watcher.Start();
        fullscreenOn = watcher.Fullscreen;
        ruleTimer = new DispatcherTimer();
        ruleTimer.Tick += (_, _) => { ruleTimer.Stop(); EvaluateAutomation(); };
        displayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        displayTimer.Tick += (_, _) => { displayTimer.Stop(); EvaluateAutomation(); watcher?.Recheck(); };
        retryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        retryTimer.Tick += (_, _) => { retryTimer.Stop(); EvaluateAutomation(); };
        SystemEvents.TimeChanged += OnClockChanged;
        SystemEvents.PowerModeChanged += OnPowerChanged;
        ApplyFullscreen();
        EvaluateAutomation();
    }

    public void StopAutomation()
    {
        if (watcher is null) return;
        SystemEvents.TimeChanged -= OnClockChanged;
        SystemEvents.PowerModeChanged -= OnPowerChanged;
        watcher.Dispose();
        watcher = null;
        ruleTimer?.Stop(); displayTimer?.Stop(); retryTimer?.Stop();
        toast?.CloseToast();
        toast = null;
    }

    private void OnClockChanged(object? sender, EventArgs e) => uiDispatcher.BeginInvoke(new Action(EvaluateAutomation));

    private void OnPowerChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) uiDispatcher.BeginInvoke(new Action(() => { EvaluateAutomation(); watcher?.Recheck(); }));
    }

    /// <summary>Monitors came or went (called at the end of <see cref="SyncDisplays"/>). Docking sends a burst of changes, so the rules wait for it to settle.</summary>
    private void OnDisplaysSynced()
    {
        if (displayTimer is null) return;
        displayTimer.Stop();
        displayTimer.Start();
    }

    // ---------- The rules ----------

    private AutomationContext Context() => new(DateTime.Now, watcher?.Process, Displays.Select(d => d.Id).ToList());

    private bool LayoutExists(string layer, string id) => id.Length > 0 && LayoutEngine.Find(Settings, id) is { } layout && layout.Layer == layer;

    /// <summary>
    /// Applies the rules to the situation now. Switches a layer only when its winning rule changed since last time, so a layout you chose by
    /// hand stays until the next trigger change. While you are editing the decks it waits, then tries again.
    /// </summary>
    public void EvaluateAutomation()
    {
        if (closing || watcher is null) return;
        ScheduleRuleTimer();
        if (!Settings.Automation.Enabled || Displays.Count == 0) return;
        if (IsEditing) { retryTimer?.Stop(); retryTimer?.Start(); return; }
        var switches = runner.Step(Settings.Automation.Rules, Context(), layer => LayoutEngine.ActiveId(Settings, layer), LayoutExists);
        foreach (var change in switches) ApplySwitch(change);
    }

    private void ApplySwitch(AutomationSwitch change)
    {
        var layout = LayoutEngine.Find(Settings, change.LayoutId);
        if (layout is null || !SwitchLayout(change.Layer, change.LayoutId)) return;
        string message = change.Rule is null
            ? $"No longer applies, back to the {AutomationText.LayerName(change.Layer)} layout \"{layout.Name}\""
            : $"{Capitalize(AutomationText.When(change.Rule))}: {AutomationText.LayerName(change.Layer)} layout \"{layout.Name}\"";
        ShowAutomationNotice(message);
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    /// <summary>One timer, aimed at the next moment a time rule starts or stops applying.</summary>
    private void ScheduleRuleTimer()
    {
        if (ruleTimer is null) return;
        ruleTimer.Stop();
        if (!Settings.Automation.Enabled || AutomationEngine.NextChange(Settings.Automation.Rules, DateTime.Now) is not { } next) return;
        ruleTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, (next - DateTime.Now).TotalSeconds + 1));
        ruleTimer.Start();
    }

    /// <summary>The rules were edited in the panel: save them, and apply them (a rule that now wins takes effect at once).</summary>
    public void AutomationChanged()
    {
        Settings.Automation = AutomationSettings.Normalized(Settings.Automation);
        Save();
        EvaluateAutomation();
        ApplyFullscreen();
    }

    /// <summary>"Apply rules now": forgets what the rules did before and applies them as at startup, so every layer follows its winning rule.</summary>
    public void ApplyRulesNow()
    {
        runner.Reset();
        EvaluateAutomation();
    }

    /// <summary>"Test now" for one rule: says whether it applies right now, and when it is the winner of its layer switches to its layout.</summary>
    public string TestRule(AutomationRule rule)
    {
        var context = Context();
        if (!LayoutExists(rule.Layer, rule.LayoutId)) return "Its layout no longer exists. Choose another layout.";
        if (!AutomationEngine.Matches(rule, context)) return "Does not apply right now: " + Why(rule, context);
        var winner = AutomationEngine.Winner(Settings.Automation.Rules, rule.Layer, context, LayoutExists);
        string layoutName = LayoutEngine.Find(Settings, rule.LayoutId)?.Name ?? "";
        if (winner is not null && winner.Id != rule.Id) return $"Applies right now, but an earlier rule for the {AutomationText.LayerName(rule.Layer)} layer comes first, so it is not used.";
        if (LayoutEngine.ActiveId(Settings, rule.Layer) == rule.LayoutId) return $"Applies right now. \"{layoutName}\" is already in use.";
        SwitchLayout(rule.Layer, rule.LayoutId);
        return $"Applies right now. Switched to \"{layoutName}\".";
    }

    /// <summary>Whether a rule applies right now (for the panel's badge).</summary>
    public bool RuleMatchesNow(AutomationRule rule) => LayoutExists(rule.Layer, rule.LayoutId) && AutomationEngine.Matches(rule, Context());

    private string Why(AutomationRule rule, AutomationContext context) => rule.Trigger switch
    {
        AutomationTriggers.Time => $"it is {context.Now:ddd HH:mm}.",
        AutomationTriggers.App => watcher is null ? "the foreground app is only watched while Desktop Deck runs normally." : $"{(string.IsNullOrEmpty(context.Process) ? "no app" : context.Process)} is in front.",
        _ => $"{context.DisplayIds.Count} display{(context.DisplayIds.Count == 1 ? " is" : "s are")} connected.",
    };

    // ---------- The notice ----------

    private void ShowAutomationNotice(string message)
    {
        if (!Settings.Automation.Notify || preview || offscreen || closing || FloatsSuppressed || Displays.Count == 0) return;
        toast ??= new AutomationToast();
        toast.ShowText(message, DisplayMap.Resolve(Settings.OrbDisplay, Displays));
        KeepCenterOnTop();
    }

    // ---------- Fullscreen ----------

    /// <summary>
    /// Hides the floating widgets (and the control button, if asked) while a fullscreen app is in front, and brings them back afterwards.
    /// Never touches the saved "Hide floating widgets" switch.
    /// </summary>
    public void ApplyFullscreen()
    {
        var settings = Settings.Automation;
        bool hide = watcher is not null && fullscreenOn && settings.HideInFullscreen;
        bool hideOrb = hide && settings.HideOrbInFullscreen;
        if (hide == FloatsSuppressed && hideOrb == OrbSuppressed) return;
        bool floatsChanged = hide != FloatsSuppressed;
        FloatsSuppressed = hide;
        OrbSuppressed = hideOrb;
        if (hide) toast?.Hide();
        if (floatsChanged) Floats.Sync(false);
        SyncOrb();
    }
}
