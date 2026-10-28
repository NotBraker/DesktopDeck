using System.Reflection;
using System.Windows;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>
/// A plug-in widget. Implement this in its own file under Widgets/ (a public parameterless class) and it is
/// discovered automatically: it appears in the "+ Add" gallery, gets a settings section in Customize… built from
/// <see cref="Options"/>, can add right-click entries, and is drawn through <see cref="Build"/>.
/// Settings are stored as text in <see cref="LauncherItem.Options"/>; read them with <see cref="WidgetContext"/>.
/// </summary>
internal interface IWidget
{
    /// <summary>Unique, stable id saved in settings: 2–40 characters of a–z, 0–9 and "-". Never change it once shipped.</summary>
    string Kind { get; }
    string Name { get; }
    /// <summary>One short line for the gallery.</summary>
    string Description { get; }
    /// <summary>Gallery section: one of <see cref="WidgetRegistry.Sections"/>.</summary>
    string Section { get; }
    /// <summary>A Segoe Fluent Icons glyph, e.g. "".</summary>
    string Glyph { get; }
    /// <summary>Default accent color, e.g. "#6c63ff".</summary>
    string Accent { get; }
    /// <summary>Sizes in DIP; use n × 24 − 12 so widgets tile the grid (e.g. 180, 276, 372, 468).</summary>
    (double Width, double Height) DefaultSize { get; }
    (double Width, double Height) MinimumSize { get; }
    /// <summary>Everything the user can customize. Be generous: this app aims to be the most customizable desktop.</summary>
    IReadOnlyList<OptionSpec> Options { get; }

    /// <summary>
    /// Draws the widget at ctx.Width × ctx.Height. Called often (every rebuild, on every layer), so it must be quick
    /// and must never block: start slow work with <see cref="WidgetContext.RunOnce"/> and rebuild when it finishes.
    /// </summary>
    FrameworkElement Build(WidgetContext ctx);

    /// <summary>Extra right-click entries (shown above Customize…).</summary>
    IEnumerable<WidgetAction> Actions(WidgetContext ctx) => [];

    /// <summary>
    /// Made-up data for smoke renders (--smoke-test --widgets). Each entry is one variant; use several to show off
    /// different settings. Must not touch the network or real user data.
    /// </summary>
    IEnumerable<Action<LauncherItem>> Samples => [_ => { }];

    /// <summary>Runs when the user adds the widget from the gallery; may ask a question first. Return false to cancel.</summary>
    bool Create(WidgetContext ctx) => true;
}

/// <summary>A right-click menu entry for a plug-in widget.</summary>
/// <param name="Group">Optional: actions with the same group name share one submenu (for example the five ratings under "Log last night").</param>
internal sealed record WidgetAction(string Title, string? Glyph, Action Run, string? Group = null);

internal enum OptionType
{
    /// <summary>On/off switch. Stored "true"/"false".</summary>
    Toggle,
    /// <summary>A number between Min and Max. Stored with invariant culture.</summary>
    Number,
    Text,
    /// <summary>Multi-line text.</summary>
    LongText,
    /// <summary>One value per line (a list of cities, links, feeds…).</summary>
    Lines,
    /// <summary>One of <see cref="OptionSpec.Choices"/> (shown with <see cref="OptionSpec.ChoiceLabels"/> if given).</summary>
    Choice,
    /// <summary>A color like #RRGGBB or #AARRGGBB; empty means "use the accent".</summary>
    Color,
    /// <summary>yyyy-MM-dd (empty allowed).</summary>
    Date,
    /// <summary>HH:mm (stored 24-hour; the editor accepts "6:30 pm").</summary>
    Time,
    Folder,
    File,
}

/// <summary>One customizable setting of a plug-in widget.</summary>
internal sealed record OptionSpec(string Key, string Label, OptionType Type, string Default = "")
{
    public string[] Choices { get; init; } = [];
    public string[]? ChoiceLabels { get; init; }
    public double Min { get; init; } = double.MinValue;
    public double Max { get; init; } = double.MaxValue;
    /// <summary>Help text shown under the field.</summary>
    public string Hint { get; init; } = "";
    /// <summary>Sub-heading in the editor, e.g. "Layout", "Colors", "Behavior".</summary>
    public string Group { get; init; } = "";

    public static OptionSpec Toggle(string key, string label, bool value, string group = "", string hint = "") => new(key, label, OptionType.Toggle, value ? "true" : "false") { Group = group, Hint = hint };
    public static OptionSpec Number(string key, string label, double value, double min, double max, string group = "", string hint = "") => new(key, label, OptionType.Number, value.ToString(System.Globalization.CultureInfo.InvariantCulture)) { Min = min, Max = max, Group = group, Hint = hint };
    public static OptionSpec Text(string key, string label, string value = "", string group = "", string hint = "") => new(key, label, OptionType.Text, value) { Group = group, Hint = hint };
    public static OptionSpec Choice(string key, string label, string value, string[] choices, string[]? labels = null, string group = "", string hint = "") => new(key, label, OptionType.Choice, value) { Choices = choices, ChoiceLabels = labels, Group = group, Hint = hint };
    public static OptionSpec Color(string key, string label, string value = "", string group = "", string hint = "") => new(key, label, OptionType.Color, value) { Group = group, Hint = hint };
    public static OptionSpec Lines(string key, string label, string value = "", string group = "", string hint = "") => new(key, label, OptionType.Lines, value) { Group = group, Hint = hint };
}

/// <summary>Finds every <see cref="IWidget"/> in the app.</summary>
internal static class WidgetRegistry
{
    /// <summary>Gallery sections, in order. Built-in widgets use the first four.</summary>
    public static readonly string[] Sections = ["Basics", "Time", "Planning", "Study", "Productivity", "Connected", "Info & media", "Online", "System", "Health", "Fun", "Visual", "Tools"];

    /// <summary>
    /// Appearance settings every widget gets, built-in or plug-in (keys start with "_"). A value left empty means "use the
    /// deck-wide default" (Appearance panel in Settings), which in turn means the theme. The defaults listed here apply when neither is set.
    /// </summary>
    public static readonly OptionSpec[] Common =
    [
        OptionSpec.Number("_textScale", "Text size (%)", 100, 60, 250, "Appearance"),
        OptionSpec.Number("_radius", "Corner roundness", 14, 0, 40, "Appearance"),
        OptionSpec.Number("_opacity", "Card opacity (%)", 100, 0, 100, "Appearance"),
        OptionSpec.Color("_textColor", "Text color", "", "Appearance"),
        OptionSpec.Color("_cardColor", "Card color", "", "Appearance"),
        OptionSpec.Color("_borderColor", "Border color", "", "Appearance"),
        OptionSpec.Number("_padding", "Spacing inside the card", 12, 0, 40, "Appearance"),
    ];

    private static readonly Lazy<List<IWidget>> all = new(Discover);
    private static volatile Merged? merged;
    private sealed record Merged(IReadOnlyList<IWidget> All, Dictionary<string, IWidget> ByKind);
    public static IReadOnlyList<IWidget> All => Current.All;
    public static IWidget? Find(string? kind) => kind is not null && Current.ByKind.TryGetValue(kind, out var widget) ? widget : null;
    public static bool IsBuiltIn(string kind) => all.Value.Any(w => w.Kind == kind);

    private static Merged Current => merged ?? (merged = Combine());

    private static Merged Combine()
    {
        var list = all.Value.OrderBy(w => Array.IndexOf(Sections, w.Section) is var i && i < 0 ? 99 : i).ThenBy(w => w.Name, StringComparer.CurrentCulture).ToList();
        return new Merged(list, list.ToDictionary(w => w.Kind, StringComparer.Ordinal));
    }

    /// <summary>Tells the layout about plug-in sizes. Call once at startup.</summary>
    public static void Install()
    {
        ItemKinds.PluginDefaultSize = kind => Find(kind)?.DefaultSize;
        ItemKinds.PluginMinimumSize = kind => Find(kind)?.MinimumSize;
    }

    private static List<IWidget> Discover()
    {
        var found = new List<IWidget>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in typeof(IWidget).Assembly.GetTypes().Where(t => typeof(IWidget).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false } && t.GetConstructor(Type.EmptyTypes) is not null).OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            try
            {
                var widget = (IWidget)Activator.CreateInstance(type)!;
                if (!ItemKinds.IsPluginId(widget.Kind) || ItemKinds.All.Contains(widget.Kind) || !seen.Add(widget.Kind))
                {
                    App.Log(new InvalidOperationException($"Widget {type.Name} has an invalid or duplicate kind \"{widget.Kind}\"; skipped."));
                    continue;
                }
                found.Add(widget);
            }
            catch (Exception ex) when (ex is TargetInvocationException or MissingMethodException or InvalidCastException) { App.Log(ex); }
        }
        return found.OrderBy(w => Array.IndexOf(Sections, w.Section) is var i && i < 0 ? 99 : i).ThenBy(w => w.Name, StringComparer.CurrentCulture).ToList();
    }
}
