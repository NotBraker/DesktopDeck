using System.Windows;
using DesktopDeck.Models;
using DesktopDeck.Widgets;

namespace DesktopDeck;

// Linked widgets (see Models/WidgetLinks.cs): keeping the data of widgets that share a LinkId in step, on every deck and in every floating window.
internal sealed partial class DeckHub
{
    static DeckHub()
    {
        // Which options a plug-in declares are its settings; the rest is its data.
        // The appearance settings every widget has (WidgetRegistry.Common) are settings too.
        WidgetLinks.DeclaredKeys = kind => WidgetRegistry.Find(kind) is { } widget ? widget.Options.Concat(WidgetRegistry.Common).Select(o => o.Key).ToArray() : null;
    }

    /// <summary>
    /// <paramref name="source"/>'s data changed: copies it to its linked peers and redraws them wherever they are shown (any monitor's deck, the front
    /// layer, a floating window). The copy runs one way and redraws without saving through <c>Changed</c> again, so peers never trigger each other.
    /// The caller saves (every path that changes data already does). Does nothing for a widget that is not linked.
    /// </summary>
    public void SyncLinked(LauncherItem source)
    {
        if (closing || source.LinkId.Length == 0 || syncingLinks) return;
        // Drawing a peer may itself save something (a plug-in noting the time…): that must not start another round.
        syncingLinks = true;
        try
        {
            foreach (var peer in WidgetLinks.Propagate(source, Settings.Items))
                foreach (var surface in Surfaces) surface.RebuildItem(peer);
        }
        finally { syncingLinks = false; }
    }

    private bool syncingLinks;

    /// <summary>
    /// Smoke test (--smoke-test --sample --link-test): links a pair of timers and a pair of Homework widgets in the throwaway sample deck and checks that
    /// changes reach the peer, that a timer ending announces itself only once, and that removing a widget unlinks it.
    /// </summary>
    internal string VerifyLinks(Action pump, Action<FrameworkElement, string>? snap = null)
    {
        var lines = new List<string>();
        void Check(bool ok, string what) { lines.Add($"{(ok ? "OK  " : "FAIL")} {what}"); }
        if (Primary is not { } deck) return "FAIL no deck";
        try { return VerifyLinksCore(deck, pump, snap, lines, Check); }
        catch (Exception ex) { lines.Add("FAIL " + ex); return string.Join(Environment.NewLine, lines); }
    }

    private string VerifyLinksCore(MainWindow deck, Action pump, Action<FrameworkElement, string>? snap, List<string> lines, Action<bool, string> Check)
    {
        var timerA = new LauncherItem { Kind = ItemKinds.Timer, Name = "Focus timer" };
        var timerB = new LauncherItem { Kind = ItemKinds.Timer, Name = "Focus timer" };
        var homeA = new LauncherItem { Kind = "homework", Name = "Homework", Accent = "#C2A14D" };
        var homeB = new LauncherItem { Kind = "homework", Name = "Homework (floating)", Accent = "#C2A14D", OnTop = true };
        // The sample deck is full, so the new widgets float (the deck has no room for them).
        timerA.OnTop = timerB.OnTop = homeA.OnTop = homeB.OnTop = true;
        timerA.FloatX = 0.05; timerB.FloatX = 0.3; homeA.FloatX = 0.55; homeB.FloatX = 0.8;
        timerA.FloatY = timerB.FloatY = homeA.FloatY = homeB.FloatY = 0.05;
        foreach (var item in new[] { timerA, timerB, homeA, homeB }) Settings.Items.Add(item);
        RenderAll();
        Check(Floats.Has(timerA.Id) && Floats.Has(homeB.Id), "the new widgets float");
        Check(WidgetLinks.Link(timerA, timerB, Settings.Items) && WidgetLinks.Link(homeA, homeB, Settings.Items), "linked the pairs");
        homeA.Options["data.items"] = "[{\"id\":\"1\",\"title\":\"Essay\"}]";
        deck.ItemDataChanged(homeA, true);
        Check(homeB.Options.GetValueOrDefault("data.items") == homeA.Options["data.items"], "a Homework change reaches the floating peer");
        homeB.Options["_textScale"] = "150";
        deck.ItemDataChanged(homeB, true);
        Check(!homeA.Options.ContainsKey("_textScale"), "look settings stay with their own widget");
        int toasts = 0;
        Action<string, string> count = (_, _) => toasts++;
        Notify += count;
        timerA.Pomodoro.WorkMinutes = 1;
        timerA.TimerSound = false;
        timerB.TimerSound = false;
        deck.ItemDataChanged(timerA, true);
        Check(timerB.Pomodoro.WorkMinutes == 1, "timer settings are shared");
        timerA.Pomodoro.EndsAt = DateTimeOffset.Now.AddSeconds(1.2);
        deck.ItemDataChanged(timerA, true);
        Check(timerB.Pomodoro.EndsAt == timerA.Pomodoro.EndsAt, "a started timer runs on both");
        var until = DateTime.Now.AddSeconds(4);
        while (DateTime.Now < until) { pump(); Thread.Sleep(50); }
        Notify -= count;
        Check(toasts == 1, $"the end of the session was announced once ({toasts})");
        Check(timerA.Pomodoro.Mode == "break" && timerB.Pomodoro.Mode == "break" && timerB.Pomodoro.EndsAt is null, "both timers moved on to the break");
        if (snap is not null)
        {
            var menu = deck.MenuForSmoke(homeA);
            menu.ApplyTemplate();
            snap(menu, "menu-linked.png");
            var other = new LauncherItem { Kind = "homework", Name = "Homework (school)" };
            Settings.Items.Add(other);
            var otherMenu = deck.MenuForSmoke(other);
            otherMenu.ApplyTemplate();
            snap(otherMenu, "menu-unlinked.png");
            Settings.Items.Remove(other);
            // A deck widget linked to a floating copy shows its link mark in Edit layout.
            if (Settings.Items.FirstOrDefault(i => !i.OnTop && !i.Hidden && i.Kind == ItemKinds.Tasks) is { } onDeck)
            {
                var floating = onDeck.Copy();
                floating.Id = Guid.NewGuid().ToString("N");
                floating.LinkId = "";
                floating.OnTop = true; floating.FloatX = 0.6; floating.FloatY = 0.6;
                Settings.Items.Add(floating);
                WidgetLinks.Link(onDeck, floating, Settings.Items);
                RenderAll();
                snap(deck, "links-edit.png");
                TrashItem(floating);
                RenderAll();
            }
        }
        int before = Settings.Items.Count;
        TrashItem(homeB);
        Check(Settings.Items.Count == before - 1 && homeA.LinkId.Length == 0 && homeB.LinkId.Length == 0, "removing a widget leaves no lone link behind");
        RenderAll();
        return string.Join(Environment.NewLine, lines);
    }
}
