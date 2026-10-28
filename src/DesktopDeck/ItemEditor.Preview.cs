using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopDeck.Models;
using DesktopDeck.Widgets;

namespace DesktopDeck;

/// <summary>What "Customize together" changes on every selected widget. Null members were left alone.</summary>
internal sealed record GroupChanges(string? CardStyle, bool? ShowHeader, string? Accent, Dictionary<string, string?> Options)
{
    /// <summary>Applies the changes to a widget (an option set to null is removed, so the widget follows the deck default again).</summary>
    internal void ApplyTo(LauncherItem item)
    {
        if (CardStyle is not null) item.CardStyle = CardStyle;
        if (ShowHeader is { } show) item.ShowHeader = show;
        if (Accent is not null) item.Accent = Accent;
        foreach (var (key, value) in Options)
        {
            if (value is null) item.Options.Remove(key);
            else item.Options[key] = value;
        }
    }
}

// Live preview of Customize, and the "Customize together" window for several widgets.
internal sealed partial class ItemEditor
{
    /// <summary>True while the fields are read for the preview: invalid values are skipped without a message and without taking the focus.</summary>
    private bool quiet;
    private DispatcherTimer? previewTimer;
    private string lastPreview = "";
    private readonly Dictionary<string, string> lastGood = new(StringComparer.Ordinal);

    // ---------- Live preview ----------

    /// <summary>
    /// While this window is open, changes to the name, accent, card style and options show on the real widget about 150 ms after
    /// the last edit. The caller draws the copy it is given instead of the widget and puts the real one back when the window closes.
    /// </summary>
    private void StartPreview(Panel root, Action<LauncherItem> preview)
    {
        var timer = previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        lastPreview = Signature(draft);
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var copy = PreviewCopy();
            string signature = Signature(copy);
            if (signature == lastPreview) return;
            lastPreview = signature;
            preview(copy);
        };
        void Schedule() { timer.Stop(); timer.Start(); }
        root.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => Schedule()));
        root.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, _) => Schedule()));
        // The switches mark their click as handled, so listen for it even then.
        root.AddHandler(UIElement.MouseLeftButtonUpEvent, new MouseButtonEventHandler((_, _) => Schedule()), true);
        Closed += (_, _) => timer.Stop();
    }

    private static string Signature(LauncherItem item) =>
        string.Join("|", item.Name, item.Accent, item.CardStyle, item.ShowHeader, item.OwnLook, string.Join(";", item.Options.OrderBy(o => o.Key, StringComparer.Ordinal).Select(o => o.Key + "=" + o.Value)));

    /// <summary>The draft as the fields show it now (valid values only), without changing the draft.</summary>
    internal LauncherItem PreviewCopy()
    {
        quiet = true;
        try
        {
            var copy = draft.Copy();
            foreach (var (spec, read) in pluginFields)
            {
                // A value that is half typed keeps showing the last good one.
                string? value = read();
                if (value is null) { if (!lastGood.TryGetValue(spec.Key, out value)) continue; }
                else lastGood[spec.Key] = value;
                if (spec.Key.StartsWith('_') && value.Length == 0) copy.Options.Remove(spec.Key);
                else copy.Options[spec.Key] = value;
            }
            if (nameBox.Text.Trim() is { Length: > 0 } name) copy.Name = name;
            string accent = accentBox.Text.Trim();
            try { Theme.Brush(accent); copy.Accent = accent; }
            catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { }
            return copy;
        }
        finally { quiet = false; }
    }

    // ---------- Customize together ----------

    internal GroupChanges? GroupResult { get; private set; }
    private HashSet<string> mixedKeys = new(StringComparer.Ordinal);
    private bool styleTouched, headerTouched;
    private string groupInitialAccent = "";

    /// <summary>
    /// The appearance of several widgets at once. Every field starts with what the widgets share (blank where they differ), and only
    /// what you change is applied, so each widget keeps its own settings for everything else.
    /// </summary>
    internal ItemEditor(Window owner, IReadOnlyList<LauncherItem> items, DeckSettings deck)
    {
        draft = items[0].Copy();
        deckAppearance = deck.Appearance;
        originalLocation = originalUnits = "";
        nameBox = new TextBox();
        // Where the widgets differ, the field starts blank and anything typed there is applied.
        foreach (var spec in WidgetRegistry.Common)
            if (items.Select(i => i.Options.TryGetValue(spec.Key, out var v) ? v : "").Distinct(StringComparer.Ordinal).Count() > 1)
            {
                mixedKeys.Add(spec.Key);
                draft.Options.Remove(spec.Key);
            }
        bool sameStyle = items.All(i => i.CardStyle == items[0].CardStyle);
        bool sameHeader = items.All(i => i.ShowHeader == items[0].ShowHeader);
        bool sameAccent = items.All(i => string.Equals(i.Accent, items[0].Accent, StringComparison.OrdinalIgnoreCase));
        groupInitialAccent = sameAccent ? (string.IsNullOrWhiteSpace(items[0].Accent) ? Theme.PrimaryHex : items[0].Accent) : "";

        Owner = owner;
        Title = $"Customize {items.Count} widgets";
        Width = 540;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 24;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Theme.Background;
        Foreground = Theme.Text;
        FontFamily = Theme.UiFont;
        var root = new StackPanel { Margin = new Thickness(28, 24, 28, 20) };
        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(Theme.Label(Title, 25));
        var intro = Theme.Label("Change the look of all of them at once. Only what you change is applied: every widget keeps its own settings for the rest. A blank field means the widgets differ there.", 12.5, Theme.Muted);
        intro.TextWrapping = TextWrapping.Wrap;
        intro.Margin = new Thickness(0, 6, 0, 18);
        root.Children.Add(intro);
        if (deck.Appearance is { ApplyToAll: true })
        {
            var deckLook = Theme.Label("One look for every widget is on, so these changes only show on widgets that have their own look (their Customize window).", 12, Theme.Muted);
            deckLook.TextWrapping = TextWrapping.Wrap;
            deckLook.Margin = new Thickness(0, -8, 0, 14);
            root.Children.Add(deckLook);
        }

        var styleHeading = Theme.Label("Card style", 12, Theme.Muted);
        styleHeading.Margin = new Thickness(0, 4, 0, 6);
        root.Children.Add(styleHeading);
        string? shown = sameStyle ? items[0].CardStyle : null;
        root.Children.Add(LookPanel.StyleButtons(shown, style => { draft.CardStyle = style; styleTouched = true; }).Panel);
        root.Children.Add(SwitchRow(sameHeader ? "Show the titles" : "Show the titles (they differ now)", sameHeader && items[0].ShowHeader, v => { draft.ShowHeader = v; headerTouched = true; }));

        accentBox = Field(root, "Accent color" + (sameAccent ? "" : " (they differ now)"), groupInitialAccent);
        accentBox.ToolTip = "Pick a swatch or type a color such as #C4684F.";
        root.Children.Add(SwatchRow(accentBox));
        AddOptionGroup(root, "", WidgetRegistry.Common.Where(spec => spec.Key is "_radius" or "_opacity"));
        // Text size, colors and spacing stay out of the way until asked for (they are still read when you apply).
        var moreLook = Disclosure.Add(root, "More look options", nested: true);
        moreLook.Toggled += KeepOnScreen;
        AddOptionGroup(moreLook.Body, "", WidgetRegistry.Common.Where(spec => spec.Key is not ("_radius" or "_opacity")));

        validation = Theme.Label("", 12, Theme.Danger);
        validation.TextWrapping = TextWrapping.Wrap;
        validation.MinHeight = 28;
        root.Children.Add(validation);
        var cancel = Theme.Button("Cancel");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => DialogResult = false;
        var apply = Theme.Button("Apply to all", true);
        apply.IsDefault = true;
        apply.Click += (_, _) => SaveGroup();
        root.Children.Add(Row(cancel, apply));
    }

    private static bool SameOption(string a, string b) =>
        string.Equals(a, b, StringComparison.Ordinal) ||
        (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y) && x == y);

    private void SaveGroup()
    {
        try { Theme.Brush(accentBox.Text.Trim().Length == 0 ? Theme.PrimaryHex : accentBox.Text.Trim()); }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or ArgumentException) { Fail("Enter a valid accent color, for example #A7DBC8.", accentBox); return; }
        var options = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (spec, read) in pluginFields)
        {
            if (read() is not { } value) return;
            string initial = mixedKeys.Contains(spec.Key) ? "" : (draft.Options.TryGetValue(spec.Key, out var own) ? own : "");
            if (SameOption(value, initial)) continue;
            options[spec.Key] = value.Length == 0 ? null : value;
        }
        string accent = accentBox.Text.Trim();
        string? newAccent = accent.Length > 0 && !string.Equals(accent, groupInitialAccent, StringComparison.OrdinalIgnoreCase) ? accent : null;
        GroupResult = new GroupChanges(styleTouched ? draft.CardStyle : null, headerTouched ? draft.ShowHeader : null, newAccent, options);
        DialogResult = true;
    }
}
