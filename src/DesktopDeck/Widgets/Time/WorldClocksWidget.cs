using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

/// <summary>The time in other places, as a list or a grid, with day/night, offset and date. Logic: WorldClocksLogic.cs.</summary>
internal sealed class WorldClocksWidget : IWidget
{
    public string Kind => "world-clocks";
    public string Name => "World clocks";
    public string Description => "The time in other cities, with day and night";
    public string Section => "Time";
    public string Glyph => "";
    public string Accent => "#00d4ff";
    public (double Width, double Height) DefaultSize => (276, 228);
    public (double Width, double Height) MinimumSize => (180, 84);

    private const string DefaultPlaces = "New York | Eastern Standard Time\nLondon | GMT Standard Time\nTokyo | Tokyo Standard Time\nSydney | AUS Eastern Standard Time";

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Lines("places", "Places", DefaultPlaces, "What to show",
            "One per line: Label | time zone. The zone can be a Windows id (Tokyo Standard Time), an IANA id (Asia/Tokyo), a city from Windows' list (Osaka), UTC or Local."),
        OptionSpec.Toggle("showLocal", "Include your own time first", false, "What to show"),
        OptionSpec.Toggle("showOffset", "Show the time difference", true, "What to show"),
        OptionSpec.Choice("offsetStyle", "Show time difference as", "relative", ["relative", "utc"], ["Hours from here (+7 h)", "UTC offset (UTC+9)"], "What to show"),
        OptionSpec.Toggle("showDayNight", "Day / night icon", true, "What to show"),
        OptionSpec.Choice("showDate", "Show the date", "different", ["different", "always", "never"], ["When it's another day", "Always", "Never"], "What to show"),
        OptionSpec.Toggle("seconds", "Show seconds", false, "What to show"),
        OptionSpec.Choice("clock", "Clock", "auto", ["auto", "12", "24"], ["Automatic (region)", "12-hour", "24-hour"], "Layout"),
        OptionSpec.Choice("layout", "Layout", "auto", ["auto", "list", "grid"], ["Automatic", "List", "Tiles"], "Layout", "Automatic uses tiles when the widget is wide and short."),
        OptionSpec.Choice("sort", "Order", "listed", ["listed", "offset", "name"], ["As listed", "West to east", "By name"], "Layout"),
        OptionSpec.Number("dayStart", "Day starts at (hour)", 7, 0, 23, "Behavior", "Used for the day / night icon."),
        OptionSpec.Number("dayEnd", "Night starts at (hour)", 19, 0, 23, "Behavior"),
        OptionSpec.Color("timeColor", "Time color", "", "Colors", "Empty = text color."),
        OptionSpec.Color("dayColor", "Day icon color", "#f59e0b", "Colors"),
        OptionSpec.Color("nightColor", "Night icon color", "#8b9cf6", "Colors"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        item =>
        {
            item.Options["places"] = "San Francisco | Pacific Standard Time\nNew York | America/New_York\nLondon | GMT Standard Time\nKathmandu | Nepal Standard Time\nAtlantis | Atlantis Standard Time";
            item.Options["offsetStyle"] = "utc";
            item.Options["showDate"] = "always";
            item.Options["clock"] = "24";
            item.Options["seconds"] = "true";
            item.Width = 324; item.Height = 276;
        },
        item =>
        {
            item.Options["places"] = "Berlin | W. Europe Standard Time\nDubai | Arabian Standard Time\nSingapore | Singapore Standard Time\nHonolulu | Hawaiian Standard Time";
            item.Options["layout"] = "grid";
            item.Options["showLocal"] = "true";
            item.Options["clock"] = "12";
            item.Accent = "#ec4899";
            item.Width = 564; item.Height = 156;
        },
    ];

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        yield return new WidgetAction("Add a place…", Theme.Glyphs.Add, () => AddPlace(ctx));
    }

    private sealed record Row(WorldClocksLogic.Place Place, TextBlock? Time, Run? Suffix);

    public FrameworkElement Build(WidgetContext ctx)
    {
        var culture = CultureInfo.CurrentCulture;
        var utcNow = DateTimeOffset.UtcNow;
        var local = TimeZoneInfo.Local;
        var localNow = TimeZoneInfo.ConvertTime(utcNow, local).DateTime;
        bool use24 = WorldClocksLogic.Use24Hour(ctx.Get("clock"), culture), seconds = ctx.GetBool("seconds");
        bool showOffset = ctx.GetBool("showOffset"), dayNight = ctx.GetBool("showDayNight"), utcStyle = ctx.Get("offsetStyle") == "utc";
        string showDate = ctx.Get("showDate");
        double dayStart = ctx.GetNumber("dayStart"), dayEnd = ctx.GetNumber("dayEnd");
        var timeBrush = ctx.GetColor("timeColor", ctx.TextBrush);

        var places = ctx.GetLines("places").Select(WorldClocksLogic.Parse).ToList();
        if (ctx.GetBool("showLocal")) places.Insert(0, new WorldClocksLogic.Place("Here", local, "Local"));
        places = ctx.Get("sort") switch
        {
            "offset" => places.OrderBy(p => p.Zone is null ? TimeSpan.MaxValue : p.Zone.GetUtcOffset(utcNow)).ToList(),
            "name" => places.OrderBy(p => p.Label, StringComparer.CurrentCultureIgnoreCase).ToList(),
            _ => places,
        };

        var add = ctx.Width >= 228 ? WidgetKit.IconButton(Theme.Glyphs.Add, "Add a place", () => AddPlace(ctx), 24, accent: true) : null;
        var (card, body) = add is null ? WidgetKit.Frame(ctx) : WidgetKit.Frame(ctx, null, add);
        if (places.Count == 0)
        {
            body.Children.Add(WidgetKit.Empty(ctx, "Add places with + or in Customize… (e.g. Tokyo | Tokyo Standard Time)."));
            return card;
        }
        double pad = ctx.GetNumber("_padding");
        double innerW = ctx.Width - 2 * pad - 2;
        double innerH = ctx.Height - 2 * pad - (ctx.Item.ShowHeader || add is not null ? 34 : 0);
        if (innerH < 65) places = places.Take(Math.Max(1, (int)(innerH / 24))).ToList();
        string layout = ctx.Get("layout");
        bool grid = layout == "grid" || (layout == "auto" && ctx.Width >= 372 && ctx.Width >= ctx.Height * 2.2);

        var rows = new List<Row>();
        string Detail(WorldClocksLogic.Place place, DateTime there)
        {
            var parts = new List<string>();
            if (showOffset && place.Zone is not null)
                parts.Add(utcStyle ? WorldClocksLogic.Utc(place.Zone.GetUtcOffset(utcNow)) : WorldClocksLogic.Relative(WorldClocksLogic.Difference(place.Zone, utcNow, local)));
            string word = WorldClocksLogic.DayWord(localNow, there);
            if (showDate == "always") parts.Add(there.ToString("ddd d MMM", culture));
            else if (showDate == "different" && word != "Today") parts.Add(word);
            return string.Join("  ·  ", parts);
        }
        FrameworkElement DayIcon(DateTime there, double size)
        {
            bool day = WorldClocksLogic.IsDaytime(there, dayStart, dayEnd);
            var icon = WidgetKit.Icon(day ? Theme.Glyphs.Sun : "", size, ctx.GetColor(day ? "dayColor" : "nightColor"));
            icon.ToolTip = day ? "Daytime" : "Night";
            return icon;
        }
        TextBlock TimeText(DateTime there, double size, out Run suffix)
        {
            var (t, s) = WorldClocksLogic.Format(there, use24, seconds);
            var text = new TextBlock { FontFamily = Theme.UiFont, Foreground = timeBrush, FontWeight = FontWeights.SemiBold, FontSize = ctx.Font(size), VerticalAlignment = VerticalAlignment.Center };
            Typography.SetNumeralAlignment(text, FontNumeralAlignment.Tabular);
            text.Inlines.Add(new Run(t));
            suffix = new Run(s.Length > 0 ? " " + s : "") { FontSize = ctx.Font(size * 0.5), Foreground = Theme.Muted, FontWeight = FontWeights.Normal };
            text.Inlines.Add(suffix);
            return text;
        }

        if (grid)
        {
            int columns = Math.Clamp((int)(innerW / 124), 1, places.Count);
            int gridRows = (int)Math.Ceiling(places.Count / (double)columns);
            double tileH = innerH / gridRows;
            double tileW = innerW / columns;
            double size = Math.Clamp(Math.Min(tileH * 0.36, tileW / (seconds ? 5.2 : 3.6)), 13, 34);
            var tiles = new UniformGrid { Columns = columns, Rows = gridRows };
            foreach (var place in places)
            {
                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                var top = new DockPanel { LastChildFill = true };
                var name = WidgetKit.Muted(ctx, place.Label, 11.5);
                if (place.Zone is null) { top.Children.Add(name); stack.Children.Add(top); stack.Children.Add(WidgetKit.Text(ctx, "unknown time zone", 12, Theme.Danger)); tiles.Children.Add(stack); continue; }
                var there = TimeZoneInfo.ConvertTime(utcNow, place.Zone).DateTime;
                if (dayNight)
                {
                    var icon = DayIcon(there, ctx.Font(11.5));
                    icon.Margin = new Thickness(0, 0, 5, 0);
                    DockPanel.SetDock(icon, Dock.Left);
                    top.Children.Add(icon);
                }
                top.Children.Add(name);
                stack.Children.Add(top);
                var time = TimeText(there, size, out var suffix);
                time.Margin = new Thickness(0, 1, 0, 1);
                stack.Children.Add(time);
                rows.Add(new Row(place, time, suffix));
                string detail = Detail(place, there);
                if (detail.Length > 0 && tileH >= 64) stack.Children.Add(WidgetKit.Muted(ctx, detail, 10.5));
                stack.ToolTip = place.Zone.DisplayName;
                tiles.Children.Add(stack);
            }
            body.Children.Add(tiles);
        }
        else
        {
            double rowH = Math.Min(54, (innerH - Math.Max(0, places.Count - 1) * 7) / places.Count);
            bool roomy = rowH >= 38;
            double size = Math.Clamp(rowH * (roomy ? 0.42 : 0.55), 12, 24);
            var list = new StackPanel { VerticalAlignment = places.Count * 54 < innerH ? VerticalAlignment.Center : VerticalAlignment.Top };
            for (int i = 0; i < places.Count; i++)
            {
                var place = places[i];
                var row = new Grid { MinHeight = Math.Min(rowH, 54) - 2 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                names.Children.Add(WidgetKit.Text(ctx, place.Label, roomy ? 13 : 12.5));
                Grid.SetColumn(names, 1);
                row.Children.Add(names);
                if (place.Zone is null)
                {
                    var bad = WidgetKit.Text(ctx, "unknown time zone", 11.5, Theme.Danger);
                    bad.ToolTip = $"\"{place.ZoneText}\" isn't a time zone on this PC. Try a Windows id like \"Tokyo Standard Time\".";
                    Grid.SetColumn(bad, 2);
                    row.Children.Add(bad);
                }
                else
                {
                    var there = TimeZoneInfo.ConvertTime(utcNow, place.Zone).DateTime;
                    if (dayNight)
                    {
                        var icon = DayIcon(there, ctx.Font(roomy ? 15 : 12.5));
                        icon.Margin = new Thickness(0, 0, 10, 0);
                        icon.Width = ctx.Font(roomy ? 16 : 13);
                        row.Children.Add(icon);
                    }
                    string detail = Detail(place, there);
                    if (detail.Length > 0 && roomy) names.Children.Add(WidgetKit.Muted(ctx, detail, 11));
                    var time = TimeText(there, size, out var suffix);
                    time.Margin = new Thickness(8, 0, 0, 0);
                    Grid.SetColumn(time, 2);
                    row.Children.Add(time);
                    rows.Add(new Row(place, time, suffix));
                    row.ToolTip = place.Zone.DisplayName + (detail.Length > 0 ? "\n" + detail : "");
                }
                list.Children.Add(row);
                if (i < places.Count - 1 && roomy) list.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 3, 0, 3) });
            }
            body.Children.Add(places.Count * rowH > innerH + 1 ? WidgetKit.Scroll(list) : list);
        }

        // Seconds update in place; everything else (day/night, dates) is redrawn when the minute changes.
        int builtMinute = localNow.Minute;
        ctx.Tick(() => seconds, at =>
        {
            var nowLocal = at.LocalDateTime;
            if (nowLocal.Minute != builtMinute) { ctx.Rebuild(); return; }
            foreach (var row in rows)
            {
                if (row.Time is null || row.Place.Zone is null) continue;
                var (t, _) = WorldClocksLogic.Format(TimeZoneInfo.ConvertTime(at, row.Place.Zone).DateTime, use24, seconds);
                if (row.Time.Inlines.FirstInline is Run run) run.Text = t;
            }
        });
        return card;
    }

    private static void AddPlace(WidgetContext ctx)
    {
        string? text = ctx.Ask("Add a place", "Label | time zone (e.g. Tokyo | Tokyo Standard Time, or just a city like Osaka)");
        if (string.IsNullOrWhiteSpace(text)) return;
        var place = WorldClocksLogic.Parse(text.Trim());
        if (place.Zone is null)
        {
            ctx.Inform($"\"{place.ZoneText}\" isn't a time zone this PC knows. Try a Windows id like \"Tokyo Standard Time\", an IANA id like \"Asia/Tokyo\", or a city named in Windows' time zone list.", "Add a place");
            return;
        }
        string line = text.Contains('|') ? text.Trim() : $"{place.Label} | {place.Zone.Id}";
        ctx.Set("places", string.Join("\n", ctx.GetLines("places").Append(line)));
    }
}


