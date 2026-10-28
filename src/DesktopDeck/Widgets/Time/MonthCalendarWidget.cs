using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DesktopDeck.Models;
using Shapes = System.Windows.Shapes;

namespace DesktopDeck.Widgets;

/// <summary>
/// A month at a glance: today highlighted, ‹ › to page through months, optional week numbers, weekends and marked
/// dates (dots with tooltips). Logic: MonthCalendarLogic.cs.
/// </summary>
internal sealed class MonthCalendarWidget : IWidget
{
    public string Kind => "month-calendar";
    public string Name => "Month calendar";
    public string Description => "The month at a glance, with marked dates";
    public string Section => "Time";
    public string Glyph => Theme.Glyphs.Calendar;
    public string Accent => "#C4684F";
    public (double Width, double Height) DefaultSize => (276, 276);
    public (double Width, double Height) MinimumSize => (180, 180);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Toggle("weekNumbers", "Week numbers", false, "What to show"),
        OptionSpec.Toggle("adjacent", "Days from the previous and next month", true, "What to show"),
        OptionSpec.Toggle("dayNames", "Weekday names", true, "What to show"),
        OptionSpec.Lines("marks", "Marked dates", "", "What to show",
            "One per line: 2026-12-24 | Christmas Eve. Use 12-25 | Christmas for every year, 2026-08-03..2026-08-14 | Holiday for a range, and add | #22c55e for a color."),
        OptionSpec.Number("upcoming", "Upcoming marked dates to list", 0, 0, 6, "What to show", "Shown when the widget is tall enough."),
        OptionSpec.Choice("firstDay", "Week starts on", "auto", ["auto", "Monday", "Sunday", "Saturday"], ["Automatic (region)", "Monday", "Sunday", "Saturday"], "Layout"),
        OptionSpec.Choice("textSize", "Day numbers", "normal", ["compact", "normal", "large"], ["Compact", "Normal", "Large"], "Layout"),
        OptionSpec.Choice("titleStyle", "Month title", "large", ["large", "caps"], ["Large", "Small capitals"], "Layout"),
        OptionSpec.Choice("todayStyle", "Today looks like", "fill", ["fill", "ring", "bold"], ["Filled circle", "Ring", "Bold text"], "Layout"),
        OptionSpec.Toggle("sixRows", "Always six rows", false, "Layout", "Keeps the height steady from month to month."),
        OptionSpec.Toggle("highlightWeekends", "Highlight weekends", true, "Behavior"),
        OptionSpec.Choice("weekend", "Weekend days", "sat-sun", ["sat-sun", "fri-sat", "sun", "fri"], ["Saturday & Sunday", "Friday & Saturday", "Sunday", "Friday"], "Behavior"),
        OptionSpec.Color("todayColor", "Today color", "", "Colors", "Empty = accent color."),
        OptionSpec.Color("weekendColor", "Weekend color", "#E07A6A", "Colors"),
        OptionSpec.Color("markColor", "Marked date color", "#f59e0b", "Colors"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        item => item.Options["marks"] = SampleMarks(),
        item =>
        {
            item.Options["marks"] = SampleMarks();
            item.Options["weekNumbers"] = "true";
            item.Options["firstDay"] = "Monday";
            item.Options["todayStyle"] = "ring";
            item.Options["titleStyle"] = "caps";
            item.Options["upcoming"] = "3";
            item.Options["textSize"] = "large";
            item.Accent = "#6c63ff";
            item.Width = 372; item.Height = 420;
        },
        item =>
        {
            item.Options["adjacent"] = "false";
            item.Options["firstDay"] = "Sunday";
            item.Options["textSize"] = "compact";
            item.Options["todayStyle"] = "bold";
            item.Options["weekend"] = "fri-sat";
            item.Accent = "#14b8a6";
            item.CardStyle = "outline";
            item.Width = 228; item.Height = 228;
        },
    ];

    /// <summary>Made-up dates around today so the sample always shows a few marks.</summary>
    private static string SampleMarks()
    {
        var today = DateTime.Today;
        string Day(int offset) => today.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return string.Join("\n",
            $"{Day(3)} | Dentist 10:30",
            $"{Day(9)} | Mia's birthday | #ec4899",
            $"{Day(-6)}..{Day(-4)} | Trip to the coast | #14b8a6",
            $"{Day(16)} | Project deadline");
    }

    public IEnumerable<WidgetAction> Actions(WidgetContext ctx)
    {
        if (ctx.State("offset", 0) != 0) yield return new WidgetAction("Back to this month", "", () => ctx.SetState("offset", 0));
        yield return new WidgetAction("Go to month…", Theme.Glyphs.Calendar, () => GoTo(ctx));
        yield return new WidgetAction("Mark a date…", Theme.Glyphs.Add, () => AddMark(ctx));
    }

    public FrameworkElement Build(WidgetContext ctx)
    {
        var today = DateTime.Today;
        int offset = ctx.State("offset", 0);
        var shown = new DateTime(today.Year, today.Month, 1).AddMonths(Math.Clamp(offset, -1200, 1200));
        var culture = CultureInfo.CurrentCulture;
        var firstDay = MonthCalendarLogic.FirstDay(ctx.Get("firstDay"), culture);
        bool weekNumbers = ctx.GetBool("weekNumbers"), adjacent = ctx.GetBool("adjacent"), highlightWeekends = ctx.GetBool("highlightWeekends");
        string weekend = ctx.Get("weekend"), todayStyle = ctx.Get("todayStyle");
        var weeks = MonthCalendarLogic.Weeks(shown.Year, shown.Month, firstDay, ctx.GetBool("sixRows"));
        var (marks, invalid) = MonthCalendarLogic.ParseMarks(ctx.GetLines("marks"), weeks[0][0], weeks[^1][6]);
        var todayBrush = ctx.GetColor("todayColor");
        var weekendBrush = ctx.GetColor("weekendColor", Theme.Danger);
        var markBrush = ctx.GetColor("markColor", Theme.Accent("#f59e0b"));

        var card = WidgetKit.Card(ctx);
        double pad = ctx.GetNumber("_padding");
        var dock = new DockPanel { Margin = new Thickness(pad + 2, pad - 1, pad + 1, pad), LastChildFill = true };
        card.Child = dock;
        double innerW = ctx.Width - 2 * pad - 3, innerH = ctx.Height - 2 * pad + 1;

        // Header: month title (click = back to today) and ‹ › buttons.
        if (ctx.Item.ShowHeader)
        {
            var head = new Grid { Margin = new Thickness(0, 0, 0, 6), MinHeight = 26 };
            head.ColumnDefinitions.Add(new ColumnDefinition());
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement title;
            string monthName = culture.DateTimeFormat.GetMonthName(shown.Month);
            if (ctx.Get("titleStyle") == "caps")
            {
                var caps = Theme.WidgetTitle($"{monthName} {shown.Year}", offset == 0 ? Theme.Muted : ctx.Accent);
                caps.FontSize = ctx.Font(10.5);
                title = caps;
            }
            else
            {
                var large = new TextBlock { FontFamily = Theme.UiFont, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                large.Inlines.Add(new System.Windows.Documents.Run(Capitalize(monthName, culture)) { FontSize = ctx.Font(innerW >= 220 ? 15 : 13.5), FontWeight = FontWeights.SemiBold, Foreground = ctx.TextBrush });
                large.Inlines.Add(new System.Windows.Documents.Run("  " + shown.Year.ToString(culture)) { FontSize = ctx.Font(innerW >= 220 ? 13 : 12), Foreground = Theme.Muted });
                title = large;
            }
            if (offset != 0) WidgetKit.Clickable(title, () => ctx.SetState("offset", 0), "Back to this month");
            head.Children.Add(title);
            double size = innerW >= 220 ? 24 : 22;
            var nav = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (offset != 0 && innerW >= 200)
            {
                var back = WidgetKit.IconButton("", "Back to this month", () => ctx.SetState("offset", 0), size);
                back.Margin = new Thickness(0, 0, 4, 0);
                nav.Children.Add(back);
            }
            nav.Children.Add(WidgetKit.IconButton(Theme.Glyphs.Left, "Previous month", () => ctx.SetState("offset", offset - 1), size));
            var next = WidgetKit.IconButton(Theme.Glyphs.Right, "Next month", () => ctx.SetState("offset", offset + 1), size);
            next.Margin = new Thickness(4, 0, 0, 0);
            nav.Children.Add(next);
            Grid.SetColumn(nav, 1);
            head.Children.Add(nav);
            if (invalid.Count > 0) head.ToolTip = $"Couldn't read {invalid.Count} marked-date line(s): {string.Join("; ", invalid.Take(3))}";
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            innerH -= 32;
        }

        // Upcoming marked dates (below the grid, only when there's room).
        int upcomingCount = ctx.GetInt("upcoming");
        if (upcomingCount > 0)
        {
            var upcoming = MonthCalendarLogic.Upcoming(ctx.GetLines("marks"), today, upcomingCount);
            double rowH = ctx.Font(12) * 1.35 + 4;
            int fit = (int)Math.Floor((innerH - 6 * 24 - 30) / rowH);
            if (upcoming.Count > 0 && fit > 0)
            {
                var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
                list.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 0, 0, 6) });
                foreach (var (day, mark) in upcoming.Take(fit))
                {
                    var row = new Grid { Margin = new Thickness(0, 1, 0, 3) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.Children.Add(new Shapes.Ellipse { Width = 7, Height = 7, Fill = MarkBrush(mark), Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
                    var label = WidgetKit.Text(ctx, $"{day.ToString("ddd d MMM", culture)}  ·  {(mark.Label.Length > 0 ? mark.Label : "Marked")}", 12);
                    Grid.SetColumn(label, 1);
                    row.Children.Add(label);
                    var when = WidgetKit.Muted(ctx, MonthCalendarLogic.Until(today, day), 11);
                    when.Margin = new Thickness(8, 0, 0, 0);
                    Grid.SetColumn(when, 2);
                    row.Children.Add(when);
                    list.Children.Add(row);
                }
                DockPanel.SetDock(list, Dock.Bottom);
                dock.Children.Add(list);
                innerH -= (list.Children.Count - 1) * rowH + 13;
            }
        }

        // Weekday names.
        int columns = weekNumbers ? 8 : 7;
        double cellW = innerW / columns;
        var names = new UniformGrid { Rows = 1, Columns = columns, Margin = new Thickness(0, 0, 0, 2) };
        if (ctx.GetBool("dayNames"))
        {
            string[] dayNames = cellW >= 34 ? culture.DateTimeFormat.AbbreviatedDayNames : culture.DateTimeFormat.ShortestDayNames;
            if (weekNumbers) names.Children.Add(new Border());
            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)(((int)firstDay + i) % 7);
                string name = dayNames[(int)dow];
                if (cellW < 24 && name.Length > 1) name = name[..1];
                var label = WidgetKit.Text(ctx, name, 10.5, highlightWeekends && MonthCalendarLogic.IsWeekend(dow, weekend) ? weekendBrush : Theme.Muted, bold: true);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.TextTrimming = TextTrimming.None;
                if (highlightWeekends && MonthCalendarLogic.IsWeekend(dow, weekend)) label.Opacity = 0.8;
                names.Children.Add(label);
            }
            DockPanel.SetDock(names, Dock.Top);
            dock.Children.Add(names);
            innerH -= ctx.Font(10.5) * 1.35 + 2;
        }

        // The days.
        double cellH = innerH / weeks.Count;
        double factor = ctx.Get("textSize") switch { "compact" => 0.85, "large" => 1.2, _ => 1.0 };
        double numberSize = Math.Clamp(Math.Min(cellH * 0.44, cellW * 0.42), 8.5, 26) * factor;
        double circle = Math.Max(14, Math.Min(Math.Min(cellH, cellW) - 3, numberSize * ctx.TextScale * 2.1));
        var grid = new UniformGrid { Rows = weeks.Count, Columns = columns };
        foreach (var week in weeks)
        {
            if (weekNumbers)
            {
                var wk = WidgetKit.Text(ctx, MonthCalendarLogic.WeekNumber(week[0], firstDay).ToString(culture), numberSize * 0.72, Theme.Dim);
                wk.HorizontalAlignment = HorizontalAlignment.Center;
                wk.ToolTip = "Week " + wk.Text;
                grid.Children.Add(wk);
            }
            foreach (var day in week)
            {
                var cell = new Grid { Background = Brushes.Transparent };
                bool inMonth = day.Month == shown.Month;
                if (!inMonth && !adjacent) { grid.Children.Add(cell); continue; }
                bool isToday = day == today;
                bool isWeekend = highlightWeekends && MonthCalendarLogic.IsWeekend(day.DayOfWeek, weekend);
                Brush color = !inMonth ? Theme.Dim : isWeekend ? weekendBrush : ctx.TextBrush;
                if (isToday && todayStyle == "fill")
                {
                    cell.Children.Add(new Shapes.Ellipse { Width = circle, Height = circle, Fill = todayBrush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    color = Theme.OnPrimary;
                }
                else if (isToday && todayStyle == "ring")
                {
                    cell.Children.Add(new Shapes.Ellipse { Width = circle, Height = circle, Stroke = todayBrush, StrokeThickness = 1.6, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    color = todayBrush;
                }
                else if (isToday) color = todayBrush;
                var number = WidgetKit.Text(ctx, day.Day.ToString(culture), numberSize, color, bold: isToday);
                number.HorizontalAlignment = HorizontalAlignment.Center;
                number.TextTrimming = TextTrimming.None;
                System.Windows.Documents.Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
                if (!inMonth || (isWeekend && !isToday)) number.Opacity = inMonth ? 0.9 : 1;
                cell.Children.Add(number);
                string tip = day.ToString("D", culture);
                if (marks.TryGetValue(day, out var dayMarks))
                {
                    var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                    double dot = Math.Clamp(numberSize * ctx.TextScale * 0.3, 3.5, 6);
                    dots.Margin = new Thickness(0, numberSize * ctx.TextScale * 1.25 + dot, 0, 0);
                    foreach (var mark in dayMarks.Take(3))
                        dots.Children.Add(new Shapes.Ellipse { Width = dot, Height = dot, Margin = new Thickness(1, 0, 1, 0), Fill = isToday && todayStyle == "fill" ? Theme.OnPrimary : MarkBrush(mark), Opacity = inMonth ? 1 : 0.5 });
                    cell.Children.Add(dots);
                    tip += "\n" + string.Join("\n", dayMarks.Select(m => "• " + (m.Label.Length > 0 ? m.Label : "Marked")));
                }
                if (isToday) tip += "\nToday";
                cell.ToolTip = tip;
                grid.Children.Add(cell);
            }
        }
        dock.Children.Add(grid);

        // Redraw when the day changes (this handler may run every second when another widget needs seconds).
        ctx.Tick(() => false, _ => { if (DateTime.Today != today) ctx.Rebuild(); });
        return card;

        Brush MarkBrush(MonthCalendarLogic.Mark mark) => mark.Color.Length > 0 ? Theme.Accent(mark.Color) : markBrush;
    }

    private static string Capitalize(string text, CultureInfo culture) => text.Length == 0 ? text : char.ToUpper(text[0], culture) + text[1..];

    private static void GoTo(WidgetContext ctx)
    {
        var today = DateTime.Today;
        var current = new DateTime(today.Year, today.Month, 1).AddMonths(ctx.State("offset", 0));
        string? text = ctx.Ask("Go to month", "Month (yyyy-MM, e.g. 2027-03)", current.ToString("yyyy-MM", CultureInfo.InvariantCulture));
        if (text is null) return;
        if (!DateTime.TryParseExact(text.Trim(), ["yyyy-MM", "yyyy-M", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var target))
        {
            ctx.Inform("Type a month like 2027-03.", "Go to month");
            return;
        }
        ctx.SetState("offset", (target.Year - today.Year) * 12 + target.Month - today.Month);
    }

    private static void AddMark(WidgetContext ctx)
    {
        string? text = ctx.Ask("Mark a date", "Date and label, e.g. 2026-12-24 | Christmas Eve", DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " | ");
        if (string.IsNullOrWhiteSpace(text)) return;
        var (_, invalid) = MonthCalendarLogic.ParseMarks([text], DateTime.Today, DateTime.Today);
        if (invalid.Count > 0)
        {
            ctx.Inform("Start with a date like 2026-12-24 (or 12-24 for every year), then | and a label.", "Mark a date");
            return;
        }
        var lines = ctx.GetLines("marks").Append(text.Trim());
        ctx.Set("marks", string.Join("\n", lines));
    }
}
