using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck;

// The Layouts tab, rules part: rules that switch layouts by themselves (time of day, app in front, monitor setup), and hiding the floating widgets
// while a fullscreen app is in front.
internal sealed partial class ControlCenter
{
    /// <summary>What "Test" last said, kept across redraws of the tab.</summary>
    private string automationTestResult = "";

    /// <summary>Adds the rules and the fullscreen-apps options to the Layouts tab, each in a collapsed section.</summary>
    private void AddAutomation(StackPanel stack)
    {
        var automation = hub.Settings.Automation;

        // ---------- Layout rules ----------
        string summary = automation.Rules.Count == 0 ? "No rules yet" : $"{automation.Rules.Count} {(automation.Rules.Count == 1 ? "rule" : "rules")}, {(automation.Enabled ? "on" : "off")}";
        var rules = TopSection(stack, "layouts.rules", "Switch layouts automatically", summary);
        var intro = PanelKit.Hint("By the time of day, the app in front or the monitors connected. The first matching rule wins for each layer.");
        intro.Margin = new Thickness(2, 0, 0, PanelKit.S8);
        rules.Body.Children.Add(intro);
        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, PanelKit.S12) };
        var applyNow = PanelKit.Outline("Apply rules now", () => { Quietly(hub.ApplyRulesNow); automationTestResult = ""; AfterLayoutChange(); }, "Check every rule against what is happening right now and switch layouts to match.");
        applyNow.Margin = new Thickness(0, 0, PanelKit.S8, 0);
        buttons.Children.Add(applyNow);
        buttons.Children.Add(PanelKit.Outline("+ Add rule", AddRule, "Add a rule that switches a layout by itself."));
        rules.Body.Children.Add(buttons);

        var master = CardBody(rules);
        master.Children.Add(PanelKit.Option("Use automatic switching", "Turns all the rules off or on. Switching a layout yourself always works.", automation.Enabled, on =>
        {
            automation.Enabled = on;
            Quietly(() => { hub.AutomationChanged(); if (on) hub.ApplyRulesNow(); });
            AfterLayoutChange();
        }));
        master.Children.Add(PanelKit.Option("Show a small notice when a rule switches a layout", "A short message above the taskbar, so a layout never changes without you knowing why.", automation.Notify, on => { automation.Notify = on; Quietly(hub.AutomationChanged); }));
        if (automationTestResult.Length > 0)
        {
            var result = Theme.Label(automationTestResult, 12.5, Theme.Text2);
            result.TextWrapping = TextWrapping.Wrap;
            result.Margin = new Thickness(0, 0, 0, PanelKit.S12);
            master.Children.Add(result);
        }

        if (automation.Rules.Count == 0) rules.Body.Children.Add(PanelKit.Empty("No rules yet. Add one, for example: when Code.exe is in front, switch the pinned layout to Vibe coding."));
        else
        {
            var list = new StackPanel { Margin = new Thickness(0, PanelKit.S12, 0, 0) };
            for (int i = 0; i < automation.Rules.Count; i++) list.Children.Add(RuleCard(automation.Rules[i], i, automation.Rules.Count));
            rules.Body.Children.Add(list);
        }
        if (!hub.AutomationRunning)
            rules.Body.Children.Add(PanelKit.Hint("Rules that depend on the app in front are only followed while Desktop Deck is running normally."));

        // ---------- Fullscreen ----------
        var fullscreen = TopSection(stack, "layouts.fullscreen", "Fullscreen apps", automation.HideInFullscreen ? "Pinned widgets step aside" : "Off");
        var fullCard = CardBody(fullscreen);
        fullCard.Children.Add(PanelKit.Option("Hide pinned widgets while an app is fullscreen", "For fullscreen games, video and presentations. They come back when you leave. Separate from the Hide pinned widgets switch in Settings.", automation.HideInFullscreen, on =>
        {
            automation.HideInFullscreen = on;
            Quietly(hub.AutomationChanged);
            Rebuild("layouts");
        }));
        var orbOption = PanelKit.Option("Hide the control button too", "The round button is hidden as well while an app is fullscreen.", automation.HideOrbInFullscreen, on => { automation.HideOrbInFullscreen = on; Quietly(hub.AutomationChanged); });
        orbOption.IsEnabled = automation.HideInFullscreen;
        orbOption.Opacity = automation.HideInFullscreen ? 1 : 0.5;
        fullCard.Children.Add(orbOption);
        var ignore = PanelKit.Collapsible(fullCard, "layouts.ignore", "Apps to ignore", automation.FullscreenIgnore.Count == 0 ? "None" : $"{automation.FullscreenIgnore.Count}", nested: true);
        ignore.Body.Children.Add(IgnoreList(automation));
    }

    // ---------- Rule cards ----------

    private Border RuleCard(AutomationRule rule, int index, int count)
    {
        var layout = LayoutEngine.Find(hub.Settings, rule.LayoutId);
        bool missing = layout is null || layout.Layer != rule.Layer;
        bool floating = rule.Layer == LayoutLayers.Floating;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var toggle = Theme.Switch(rule.Enabled, on => { rule.Enabled = on; Quietly(hub.AutomationChanged); Rebuild("layouts"); });
        toggle.Margin = new Thickness(0, 0, PanelKit.S12, 0);
        toggle.ToolTip = rule.Enabled ? "This rule is on" : "This rule is off";
        grid.Children.Add(toggle);

        string glyph = rule.Trigger switch { AutomationTriggers.Time => Theme.Glyphs.Time, AutomationTriggers.App => Theme.Glyphs.App, _ => Theme.Glyphs.Monitor };
        var tile = PanelKit.IconTile(glyph, rule.Enabled ? Theme.PrimaryHex : "#8E8270", 40);
        tile.Margin = new Thickness(0, 0, PanelKit.S12, 0);
        Grid.SetColumn(tile, 1);
        grid.Children.Add(tile);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var when = Theme.Label("When " + AutomationText.When(rule), 14);
        when.TextWrapping = TextWrapping.NoWrap;
        when.FontWeight = FontWeights.SemiBold;
        when.TextTrimming = TextTrimming.CharacterEllipsis;
        when.MaxWidth = 300;
        when.Opacity = rule.Enabled ? 1 : 0.55;
        line.Children.Add(when);
        if (rule.Enabled && !missing && hub.RuleMatchesNow(rule))
        {
            var badge = PanelKit.Badge("Applies now", Theme.PrimaryHex, filled: true);
            badge.Margin = new Thickness(PanelKit.S8, 0, 0, 0);
            line.Children.Add(badge);
        }
        info.Children.Add(line);
        var then = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        then.Children.Add(Theme.Label($"Switch {AutomationText.LayerName(rule.Layer)} layout to ", 12, Theme.Muted));
        if (missing)
        {
            var gone = PanelKit.Badge("Layout missing", "#E07A6A");
            gone.ToolTip = "That layout was deleted. Edit the rule and choose another one; until then it is skipped.";
            then.Children.Add(gone);
        }
        else
        {
            if (layout!.Icon.Length > 0) then.Children.Add(new TextBlock { Text = layout.Icon + " ", FontFamily = PanelKit.EmojiFont, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var name = Theme.Label(layout.Name, 12, Theme.Text2);
            name.FontWeight = FontWeights.SemiBold;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.MaxWidth = 200;
            then.Children.Add(name);
        }
        if (rule.SwitchBack)
        {
            var back = Theme.Label("  ·  switches back after", 12, Theme.Muted);
            then.Children.Add(back);
        }
        info.Children.Add(then);
        Grid.SetColumn(info, 2);
        grid.Children.Add(info);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(PanelKit.Quiet("Test", () => TestRule(rule), tip: "Does this rule apply right now? If so, switch to its layout now."));
        actions.Children.Add(PanelKit.More("More: edit, change the order", menu =>
        {
            PanelKit.MenuEntry(menu, "Edit\u2026", () => EditRule(rule));
            menu.Items.Add(new Separator());
            PanelKit.MenuEntry(menu, "Earlier", () => MoveRule(rule, -1), index > 0, tip: "Rules are checked in this order");
            PanelKit.MenuEntry(menu, "Later", () => MoveRule(rule, 1), index < count - 1, tip: "Rules are checked in this order");
            menu.Items.Add(new Separator());
            PanelKit.MenuEntry(menu, "Delete", () => DeleteRule(rule), danger: true);
        }));
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        var card = PanelKit.HoverCard(grid);
        card.Margin = new Thickness(0, 0, 0, PanelKit.S8);
        card.Padding = new Thickness(PanelKit.S12);
        return card;
    }

    // ---------- Rule actions ----------

    private void AddRule()
    {
        var starter = new AutomationRule { Trigger = AutomationTriggers.App, Layer = LayoutLayers.Floating, LayoutId = LayoutEngine.Active(hub.Settings, LayoutLayers.Floating)?.Id ?? "" };
        if (new AutomationRuleEditor(this, starter, true, hub.Settings, hub.Displays) is { } editor && editor.ShowDialog() == true && editor.Result is { } made)
        {
            hub.Settings.Automation.Rules.Add(made);
            Changed();
        }
    }

    private void EditRule(AutomationRule rule)
    {
        var editor = new AutomationRuleEditor(this, rule, false, hub.Settings, hub.Displays);
        if (editor.ShowDialog() != true || editor.Result is not { } edited) return;
        var rules = hub.Settings.Automation.Rules;
        int at = rules.IndexOf(rule);
        if (at < 0) return;
        edited.Id = rule.Id;
        edited.Enabled = rule.Enabled;
        rules[at] = edited;
        Changed();
    }

    private void DeleteRule(AutomationRule rule)
    {
        if (MessageBox.Show(this, $"Delete the rule \"When {AutomationText.When(rule)}\"?", "Delete rule", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        hub.Settings.Automation.Rules.Remove(rule);
        Changed();
    }

    private void MoveRule(AutomationRule rule, int delta)
    {
        var rules = hub.Settings.Automation.Rules;
        int from = rules.IndexOf(rule), to = from + delta;
        if (from < 0 || to < 0 || to >= rules.Count) return;
        (rules[from], rules[to]) = (rules[to], rules[from]);
        Changed();
    }

    private void TestRule(AutomationRule rule)
    {
        automationTestResult = "";
        Quietly(() => automationTestResult = hub.TestRule(rule));
        AfterLayoutChange();
    }

    /// <summary>The rules changed from this tab: save and apply them, then redraw this tab and the ones that show layouts.</summary>
    private void Changed()
    {
        automationTestResult = "";
        Quietly(hub.AutomationChanged);
        AfterLayoutChange();
    }

    /// <summary>Smoke test: draws the rule editor (one image per kind of rule) next to the panel, without showing it.</summary>
    internal void RenderAutomationForSmoke(string folder, Action<FrameworkElement, string> snap)
    {
        foreach (var rule in hub.Settings.Automation.Rules.GroupBy(r => r.Trigger).Select(g => g.First()))
        {
            var editor = new AutomationRuleEditor(this, rule, false, hub.Settings, hub.Displays);
            editor.RenderForSmoke(snap, System.IO.Path.Combine(folder, $"panel-automation-editor-{rule.Trigger}.png"));
        }
    }

    // ---------- The ignore list ----------

    private FrameworkElement IgnoreList(AutomationSettings automation)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 0, PanelKit.S8) };
        var desc = Theme.Label("These apps never count as fullscreen, for example a video player you want your widgets over. Use the process name, such as vlc.exe.", 12, Theme.Muted);
        desc.TextWrapping = TextWrapping.Wrap;
        desc.Margin = new Thickness(0, 0, 0, PanelKit.S8);
        stack.Children.Add(desc);
        var chips = new WrapPanel();
        foreach (var app in automation.FullscreenIgnore.ToList())
        {
            string key = app;
            var label = Theme.Label(app, 12.5, Theme.Text2);
            var remove = new TextBlock { Text = "✕", FontSize = 11, Foreground = Theme.Muted, Margin = new Thickness(8, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = "Stop ignoring " + app };
            remove.MouseLeftButtonUp += (_, e) => { automation.FullscreenIgnore.Remove(key); Quietly(hub.AutomationChanged); Rebuild("layouts"); e.Handled = true; };
            var chip = new Border
            {
                CornerRadius = new CornerRadius(15), Background = Theme.Surface3, Padding = new Thickness(12, 5, 10, 5), Margin = new Thickness(0, 0, 6, 6),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, remove } },
            };
            chips.Children.Add(chip);
        }
        stack.Children.Add(chips);
        var add = new Grid();
        add.ColumnDefinitions.Add(new ColumnDefinition());
        add.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var box = Theme.TextField("");
        box.MaxLength = 80;
        box.ToolTip = "A process name, e.g. vlc.exe";
        void Add()
        {
            string key = AutomationEngine.ProcessKey(box.Text);
            if (key.Length == 0) { box.Focus(); return; }
            if (!automation.FullscreenIgnore.Contains(key)) automation.FullscreenIgnore.Add(key);
            Quietly(hub.AutomationChanged);
            Rebuild("layouts");
        }
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Add(); e.Handled = true; } };
        add.Children.Add(box);
        var button = PanelKit.Outline("Add", Add);
        button.Margin = new Thickness(PanelKit.S8, 0, 0, 0);
        Grid.SetColumn(button, 1);
        add.Children.Add(button);
        stack.Children.Add(add);
        stack.IsEnabled = automation.HideInFullscreen;
        stack.Opacity = automation.HideInFullscreen ? 1 : 0.5;
        return stack;
    }
}
