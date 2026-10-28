using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Models;
using DesktopDeck.Widgets;

namespace DesktopDeck;

// Plug-in widgets (Widgets/IWidget.cs): drawing, the services WidgetContext offers, and adding them.
public sealed partial class MainWindow
{
    /// <summary>Temporary per-widget state for plug-ins on this surface (see WidgetContext.State).</summary>
    internal Dictionary<string, object?> PluginState { get; } = new();

    /// <summary>The deck-wide card defaults (Settings → Appearance), for plug-in widgets.</summary>
    internal AppearanceSettings AppearanceDefaults => settings.Appearance;
    /// <summary>How many days each kind of history is kept (Settings → History), for plug-in widgets.</summary>
    internal HistorySettings HistorySettings => settings.History;

    private FrameworkElement BuildPlugin(IWidget widget, LauncherItem item, double w, double h, bool snapshot = false)
    {
        try { return widget.Build(new WidgetContext(this, item, widget, w, h, snapshot)); }
        catch (Exception ex)
        {
            App.Log(ex);
            var card = Card(item);
            var note = Theme.Label($"{widget.Name} couldn't draw: {ex.Message}", 12, Theme.Danger);
            note.TextWrapping = TextWrapping.Wrap;
            note.Margin = new Thickness(14);
            card.Child = note;
            return card;
        }
    }

    /// <summary>Smoke test: one plug-in widget drawn at a size, as it would look on the deck.</summary>
    internal FrameworkElement SnapshotPlugin(IWidget widget, LauncherItem item, double w, double h) => BuildPlugin(widget, item, w, h, snapshot: true);

    internal void ItemDataChanged(LauncherItem item, bool rebuild) => Changed(item, rebuild);
    internal void RebuildPlugin(LauncherItem item) { if (IsLoaded && !closingSurface) RebuildContent(item); }
    // A widget's ticker must not take the other widgets' tickers (or the app) down with it: errors are logged and ignored.
    internal void RegisterTicker(LauncherItem item, Func<bool> everySecond, Action<DateTimeOffset> tick) => tickers[KeyFor(item)] = (
        () => { try { return everySecond(); } catch (Exception ex) { App.Log(ex); return false; } },
        now => { try { tick(now); } catch (Exception ex) { App.Log(ex); } });
    internal DesktopDeck.Services.GoogleAuth GoogleAccount => hub.Google;
    internal DesktopDeck.Services.GmailService GmailApi => hub.Gmail;
    internal DesktopDeck.Services.ClassroomService ClassroomApi => hub.Classroom;
    internal List<LauncherItem> ItemsOfKind(string kind) => settings.Items.Where(i => i.Kind == kind).ToList();
    internal void OpenSettingsWindow() => hub.OpenSettings();
    internal DeckHub RemindersHub => hub;
    internal void NotifyUser(string title, string text) => hub.RaiseNotify(title, text);
    internal bool ConfirmUser(string text, string title) => Confirm(text, title);
    internal void CustomizeItem(LauncherItem item) => EditItem(item);
    internal string? AskSecret(string title, string label) => Modal(() => SecretDialog.Ask(DialogOwner, title, label));

    internal void RunOnce(string key, Func<Task> work)
    {
        if (!Fetching.Add("plugin:" + key)) return;
        _ = Run();
        async Task Run()
        {
            try { await work(); }
            catch (Exception ex) { App.Log(ex); }
            finally { Fetching.Remove("plugin:" + key); }
        }
    }

    private IEnumerable<WidgetAction> PluginActions(IWidget widget, LauncherItem item)
    {
        var (w, h) = rects.TryGetValue(item.Id, out var rect) ? (rect.Width, rect.Height) : SizeOf(item);
        try { return widget.Actions(new WidgetContext(this, item, widget, w, h, false)).ToList(); }
        catch (Exception ex) { App.Log(ex); return []; }
    }

    private void AddPlugin(IWidget widget)
    {
        var item = new LauncherItem { Kind = widget.Kind, Name = widget.Name, Accent = widget.Accent };
        var (w, h) = widget.DefaultSize;
        bool go;
        // Linking to another one: its information comes from there, so the widget's own setup question is skipped.
        try { go = pendingLink is not null || widget.Create(new WidgetContext(this, item, widget, w, h, false)); }
        catch (Exception ex) { App.Log(ex); ShowError($"Could not add {widget.Name}", ex); return; }
        if (go) AddNew(item);
    }
}
