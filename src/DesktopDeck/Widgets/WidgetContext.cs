using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// Everything a plug-in widget needs while it draws: its item and size, its settings, a ticker, saving, dialogs,
/// and a way to run slow work once. One context is made per build; don't keep it after the build returns except in
/// event handlers and tickers of the element you returned.
/// </summary>
internal sealed class WidgetContext
{
    private readonly MainWindow surface;

    internal WidgetContext(MainWindow surface, LauncherItem item, IWidget widget, double width, double height, bool snapshot)
    {
        this.surface = surface;
        Item = item;
        Widget = widget;
        Width = width;
        Height = height;
        IsSnapshot = snapshot;
    }

    public LauncherItem Item { get; }
    public IWidget Widget { get; }
    /// <summary>The widget's size in DIP.</summary>
    public double Width { get; }
    public double Height { get; }
    /// <summary>The deck is in Edit layout: clicks go to dragging, so the widget shouldn't rely on them.</summary>
    public bool IsEditing => surface.IsEditing;
    /// <summary>Rendering for a smoke-test image: use sample/cached data only, start nothing, touch no network.</summary>
    public bool IsSnapshot { get; }
    public Dispatcher Dispatcher => surface.Dispatcher;

    // ---------- Settings ----------

    private OptionSpec? Spec(string key) => Widget.Options.FirstOrDefault(o => o.Key == key) ?? WidgetRegistry.Common.FirstOrDefault(o => o.Key == key);

    /// <summary>The saved value, or the option's default.</summary>
    public string Get(string key) => Item.Options.TryGetValue(key, out var value) ? value : Spec(key)?.Default ?? "";
    public bool GetBool(string key) => string.Equals(Get(key), "true", StringComparison.OrdinalIgnoreCase);
    public double GetNumber(string key)
    {
        // With one look for every widget, the shared appearance numbers come from the deck.
        if (Appearance.DeckNumber(Defaults, Item, key) is { } deckValue) return deckValue;
        var spec = Spec(key);
        double fallback = double.TryParse(spec?.Default, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        // Shared appearance settings the widget left empty follow the deck-wide defaults (Settings → Appearance).
        if (key is "_radius" or "_opacity" && Appearance.OptionNumber(Item, key) is null)
            fallback = key == "_radius" ? Defaults.CornerRadius : Defaults.CardOpacity;
        double value = double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : fallback;
        return spec is null ? value : Math.Clamp(value, spec.Min, spec.Max);
    }
    public int GetInt(string key) => (int)Math.Round(GetNumber(key));
    /// <summary>A Lines option: non-empty trimmed lines.</summary>
    public string[] GetLines(string key) => Get(key).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    /// <summary>A Color option as a brush; empty or invalid falls back to <paramref name="fallback"/> (default: the accent).</summary>
    public Brush GetColor(string key, Brush? fallback = null)
    {
        string value = Get(key).Trim();
        if (value.Length == 0) return fallback ?? Accent;
        try { return Theme.Brush(value); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { return fallback ?? Accent; }
    }

    /// <summary>Saves one setting (soon, batched) and redraws every copy of the widget unless <paramref name="rebuild"/> is false.</summary>
    public void Set(string key, string value, bool rebuild = true)
    {
        Item.Options[key] = value;
        Changed(rebuild);
    }
    public void Set(string key, bool value, bool rebuild = true) => Set(key, value ? "true" : "false", rebuild);
    public void Set(string key, double value, bool rebuild = true) => Set(key, value.ToString(CultureInfo.InvariantCulture), rebuild);

    // ---------- Appearance ----------

    public Brush Accent => Theme.Accent(Item.Accent);
    /// <summary>The user's text size setting as a factor (1 = 100%).</summary>
    public double TextScale => GetNumber("_textScale") / 100.0;
    /// <summary>A font size scaled by the user's text size setting.</summary>
    public double Font(double size) => Math.Max(6, size * TextScale);
    public Brush TextBrush => Appearance.UsesDeckLook(Defaults, Item)
        ? (Appearance.ParseColor(Defaults.TextColor) is { } deckText ? Appearance.Solid(deckText) : Theme.Text)
        : GetColor("_textColor", Appearance.ParseColor(Defaults.TextColor) is { } c ? Appearance.Solid(c) : Theme.Text);
    /// <summary>The deck-wide card defaults (Settings → Appearance).</summary>
    internal DesktopDeck.Models.AppearanceSettings Defaults => surface.AppearanceDefaults;

    /// <summary>How many days the deck keeps one kind of history (Settings → History; 0 = forever). Keys are <see cref="HistoryKeys"/>.</summary>
    public int HistoryDays(string key) => surface.HistorySettings.DaysFor(key);

    // ---------- State, saving, redrawing ----------

    /// <summary>Temporary per-widget state on this surface (e.g. which month is shown). Not saved.</summary>
    public T State<T>(string key, T fallback) => surface.PluginState.TryGetValue(Item.Id + "/" + key, out var value) && value is T typed ? typed : fallback;
    public void SetState<T>(string key, T value, bool rebuild = true)
    {
        surface.PluginState[Item.Id + "/" + key] = value;
        if (rebuild) Rebuild();
    }

    /// <summary>Data changed: save soon and redraw every copy (other monitor layer, floating window).</summary>
    public void Changed(bool rebuild = true) => surface.ItemDataChanged(Item, rebuild);
    /// <summary>Redraw this copy now.</summary>
    public void Rebuild() => surface.RebuildPlugin(Item);

    /// <summary>
    /// Calls <paramref name="tick"/> every second (when <paramref name="everySecond"/> returns true) or every minute
    /// while the widget is on screen. One ticker per widget copy; registering again replaces it.
    /// </summary>
    public void Tick(Func<bool> everySecond, Action<DateTimeOffset> tick) { if (!IsSnapshot) surface.RegisterTicker(Item, everySecond, tick); }

    /// <summary>
    /// Runs slow work (network, disk) once at a time per <paramref name="key"/> across every copy of every widget.
    /// Typical use: fetch, store the result in Item.Options or <see cref="Cache"/>, then call Changed().
    /// Skipped for snapshots.
    /// </summary>
    public void RunOnce(string key, Func<Task> work)
    {
        if (IsSnapshot) return;
        surface.RunOnce(key, work);
    }

    /// <summary>Shared in-memory cache for all widgets (not saved).</summary>
    public static ConcurrentDictionary<string, object> Cache { get; } = new();

    // ---------- Secrets (tokens, passwords, API keys) ----------

    /// <summary>
    /// Reads a secret saved by a widget, encrypted with Windows DPAPI for this user in secrets.json (never in
    /// settings.json or logs). Names are shared by all widgets: use a clear prefix such as "github.token".
    /// Returns null in snapshot mode.
    /// </summary>
    public string? GetSecret(string name) => IsSnapshot ? null : surface.Secrets.Get("widget:" + name);
    public bool HasSecret(string name) => !IsSnapshot && surface.Secrets.Has("widget:" + name);
    public void SetSecret(string name, string value) { if (!IsSnapshot) surface.Secrets.Set("widget:" + name, value); }
    public void RemoveSecret(string name) { if (!IsSnapshot) surface.Secrets.Remove("widget:" + name); }
    /// <summary>Asks for a token or password in a masked box. Null if cancelled.</summary>
    public string? AskSecret(string title, string label) => surface.AskSecret(title, label);

    // ---------- Calendars (Google, iCal links, Schedule blocks) ----------

    /// <summary>The shared calendar cache. Reading it never blocks; it fetches in the background and redraws widgets when data arrives.</summary>
    public DesktopDeck.Services.CalendarFeed Calendar => surface.CalendarData;
    /// <summary>Every Schedule widget's local blocks as events between two days (start inclusive, end exclusive).</summary>
    public List<CalendarEvent> LocalScheduleEvents(DateOnly from, DateOnly toExclusive) => surface.LocalScheduleEvents(from, toExclusive);
    /// <summary>Opens an event: the Google editor, the read-only details, or the schedule block editor.</summary>
    public void OpenEvent(CalendarEvent ev) => surface.OpenCalendarEvent(ev);
    /// <summary>Opens the editor for a new Google event.</summary>
    public void AddGoogleEvent(DateOnly date) => surface.AddGoogleEvent(date);

    // ---------- Google (Gmail, Classroom) ----------

    /// <summary>The Google sign-in (Settings → Connected accounts).</summary>
    public DesktopDeck.Services.GoogleAuth Google => surface.GoogleAccount;
    /// <summary>Read-only Gmail (metadata only).</summary>
    public DesktopDeck.Services.GmailService Gmail => surface.GmailApi;
    public DesktopDeck.Services.ClassroomService Classroom => surface.ClassroomApi;
    /// <summary>True when a real account with this feature's permission is connected. Always false in snapshots and smoke renders, so nothing contacts Google.</summary>
    public bool GoogleReady(DesktopDeck.Services.GoogleFeature feature) => !IsSnapshot && !Google.SampleMode && Google.IsConnected && Google.HasScopes(feature.Scopes);
    /// <summary>The app-wide reminders (Reminders tab): lists, add, done, snooze. Not for snapshots: they use ReminderSamples.</summary>
    internal DeckHub Reminders => surface.RemindersHub;
    /// <summary>Opens Settings (Connected accounts live there).</summary>
    public void OpenSettings() => surface.OpenSettingsWindow();

    // ---------- Other widgets on the deck ----------

    /// <summary>Every widget of a kind on the deck (all surfaces), e.g. "homework".</summary>
    public List<LauncherItem> ItemsOfKind(string kind) => surface.ItemsOfKind(kind);
    /// <summary>Another widget's data changed: save soon and redraw it.</summary>
    public void Changed(LauncherItem other) => surface.ItemDataChanged(other, true);

    // ---------- Talking to the user ----------

    public void Status(string text) => surface.SetStatus(text);
    public void Notify(string title, string text) => surface.NotifyUser(title, text);
    /// <summary>A small text prompt (the deck can't take typing directly). Null if cancelled.</summary>
    public string? Ask(string title, string label, string value = "", bool multiline = false) => surface.Ask(title, label, value, multiline: multiline);
    public bool Confirm(string text, string title) => surface.ConfirmUser(text, title);
    public void Inform(string text, string title) => surface.Inform(text, title);
    /// <summary>Opens a file, folder or web address with its default app.</summary>
    public void Open(string pathOrUrl) => surface.OpenPath(pathOrUrl);
    /// <summary>Opens the widget's Customize… window.</summary>
    public void Customize() => surface.CustomizeItem(Item);
}
