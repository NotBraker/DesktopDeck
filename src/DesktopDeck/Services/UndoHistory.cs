namespace DesktopDeck.Services;

/// <summary>One undoable (or redoable) change: what it was and the history state to go back to.</summary>
public sealed record UndoStep(string Label, string State, DateTime At);

/// <summary>What <see cref="UndoHistory.Observe"/> made of a saved state.</summary>
public enum Observed
{
    /// <summary>Nothing the user would call a change (or only background noise); the baseline moved on silently.</summary>
    Unchanged,
    /// <summary>A new step was added.</summary>
    Recorded,
    /// <summary>The change was folded into the previous step (same kind of change, a moment later).</summary>
    Coalesced,
}

/// <summary>
/// Undo and redo as a history of settings states, in memory. Every time the settings are saved the new state is offered to <see cref="Observe"/>; if it
/// differs from the last one the last one becomes an undo step labelled by <see cref="HistoryState.Describe"/>. Changes of the same kind within
/// <see cref="CoalesceWindow"/> of each other (tracker clicks, a drag saving along the way) count as one step. At most <see cref="Cap"/> steps are
/// kept. Time is always passed in, so nothing here sleeps or reads a clock. No UI.
/// </summary>
public sealed class UndoHistory
{
    public const int DefaultCap = 50;
    public static readonly TimeSpan DefaultCoalesceWindow = TimeSpan.FromMilliseconds(1500);

    private readonly List<UndoStep> undo = new();   // oldest first; the last one is what Undo takes
    private readonly List<UndoStep> redo = new();   // the last one is what Redo takes
    private string baseline = "";
    private string baselineKey = "";
    private DateTime lastChange = DateTime.MinValue;
    private string lastLabel = "";

    public UndoHistory(int cap = DefaultCap, TimeSpan? coalesceWindow = null)
    {
        Cap = Math.Max(1, cap);
        CoalesceWindow = coalesceWindow ?? DefaultCoalesceWindow;
    }

    public int Cap { get; }
    public TimeSpan CoalesceWindow { get; }
    /// <summary>Raised whenever the steps change (a step was added, undone or redone, or the history was reset).</summary>
    public event Action? Changed;
    /// <summary>Raised when steps fell off the old end (past <see cref="Cap"/>), so media only they needed can be let go.</summary>
    public event Action? Evicted;

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public string? UndoLabel => undo.Count > 0 ? undo[^1].Label : null;
    public string? RedoLabel => redo.Count > 0 ? redo[^1].Label : null;
    /// <summary>The steps that can be undone, newest first.</summary>
    public IReadOnlyList<UndoStep> UndoSteps => Enumerable.Reverse(undo).ToList();
    /// <summary>The steps that can be redone, next first.</summary>
    public IReadOnlyList<UndoStep> RedoSteps => Enumerable.Reverse(redo).ToList();
    /// <summary>The current state (the last one observed).</summary>
    public string Baseline => baseline;

    /// <summary>Every state still reachable (the current one and all steps): media these refer to must not be deleted.</summary>
    public IEnumerable<string> States => undo.Select(s => s.State).Concat(redo.Select(s => s.State)).Append(baseline);

    /// <summary>Starts over from the given settings JSON: no steps, this is the current state.</summary>
    public void Reset(string settingsJson)
    {
        undo.Clear();
        redo.Clear();
        SetBaseline(HistoryState.Capture(settingsJson));
        lastChange = DateTime.MinValue;
        lastLabel = "";
        Changed?.Invoke();
    }

    /// <summary>Makes the given settings the current state without recording a step (after an undo/redo, a restore, or housekeeping).</summary>
    public void Rebase(string settingsJson)
    {
        SetBaseline(HistoryState.Capture(settingsJson));
        lastChange = DateTime.MinValue;
    }

    private void SetBaseline(string state)
    {
        baseline = state;
        baselineKey = HistoryState.Key(state);
    }

    /// <summary>
    /// Offers the settings that were just saved. A real change records a step (labelled <paramref name="label"/> or, if none, by comparing with the
    /// previous state) and clears redo.
    /// </summary>
    public Observed Observe(string settingsJson, DateTime now, string? label = null)
    {
        string state = HistoryState.Capture(settingsJson);
        string key = HistoryState.Key(state);
        if (baseline.Length == 0) { SetBaseline(state); return Observed.Unchanged; }
        if (key == baselineKey)
        {
            // Only background data moved (a weather refresh, a task sync): keep it, record nothing.
            baseline = state;
            return Observed.Unchanged;
        }
        label = string.IsNullOrWhiteSpace(label) ? HistoryState.Describe(baseline, state) : label.Trim();
        bool same = undo.Count > 0 && redo.Count == 0 && lastChange != DateTime.MinValue && now - lastChange <= CoalesceWindow && label == lastLabel;
        lastChange = now;
        if (same)
        {
            SetBaseline(state);
            // Changed and changed back within the window (a toggle clicked twice): there is nothing left to undo.
            if (HistoryState.Key(undo[^1].State) == key) { undo.RemoveAt(undo.Count - 1); lastChange = DateTime.MinValue; Changed?.Invoke(); }
            return Observed.Coalesced;
        }
        undo.Add(new UndoStep(label, baseline, now));
        redo.Clear();
        lastLabel = label;
        SetBaseline(state);
        bool evicted = false;
        while (undo.Count > Cap) { undo.RemoveAt(0); evicted = true; }
        Changed?.Invoke();
        if (evicted) Evicted?.Invoke();
        return Observed.Recorded;
    }

    /// <summary>Takes the newest undo step. Returns it: <see cref="UndoStep.State"/> is the state to put back. Null if there is nothing to undo.</summary>
    public UndoStep? Undo(DateTime now)
    {
        if (undo.Count == 0) return null;
        var step = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        redo.Add(new UndoStep(step.Label, baseline, now));
        SetBaseline(step.State);
        lastChange = DateTime.MinValue;
        Changed?.Invoke();
        return step;
    }

    /// <summary>Takes the next redo step. Returns it: <see cref="UndoStep.State"/> is the state to put back. Null if there is nothing to redo.</summary>
    public UndoStep? Redo(DateTime now)
    {
        if (redo.Count == 0) return null;
        var step = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        undo.Add(new UndoStep(step.Label, baseline, now));
        SetBaseline(step.State);
        lastChange = DateTime.MinValue;
        Changed?.Invoke();
        return step;
    }

    /// <summary>True if any reachable state mentions the text (e.g. a media file name).</summary>
    public bool Holds(string text) => States.Any(s => s.Contains(text, StringComparison.OrdinalIgnoreCase));
}
