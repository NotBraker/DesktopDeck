using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DesktopDeck.Widgets;

/// <summary>Live tokens and letter case for the text banner. Pure logic (unit tested).</summary>
public static class TextBannerLogic
{
    /// <summary>The tokens a banner can contain; they are replaced with the current date and time.</summary>
    public static readonly string[] Tokens = ["{date}", "{shortdate}", "{time}", "{weekday}", "{day}", "{month}", "{year}", "{greeting}"];

    private static readonly Regex TokenPattern = new(@"\{(date|shortdate|time|weekday|day|month|year|greeting)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether the text has any live token (so it needs redrawing every minute).</summary>
    public static bool HasTokens(string text) => TokenPattern.IsMatch(text);

    /// <summary>Replaces {date} {shortdate} {time} {weekday} {day} {month} {year} {greeting} (any letter case).</summary>
    public static string ReplaceTokens(string text, DateTime now, CultureInfo culture, bool clock24 = false) =>
        TokenPattern.Replace(text, match => match.Groups[1].Value.ToLowerInvariant() switch
        {
            "date" => now.ToString("D", culture),
            "shortdate" => now.ToString("d", culture),
            "time" => now.ToString(clock24 ? "HH:mm" : "h:mm tt", culture).Trim(),
            "weekday" => now.ToString("dddd", culture),
            "day" => now.Day.ToString(culture),
            "month" => now.ToString("MMMM", culture),
            "year" => now.Year.ToString(culture),
            _ => Greeting(now),
        });

    /// <summary>"Good morning" (5–12), "Good afternoon" (12–17), "Good evening" (17–22), else "Good night".</summary>
    public static string Greeting(DateTime now) => now.Hour switch
    {
        >= 5 and < 12 => "Good morning",
        >= 12 and < 17 => "Good afternoon",
        >= 17 and < 22 => "Good evening",
        _ => "Good night",
    };

    /// <summary>Letter case: "upper", "lower", "title" (Every Word Capitalized) or anything else for as typed.</summary>
    public static string ApplyCase(string text, string mode, CultureInfo culture) => mode switch
    {
        "upper" => text.ToUpper(culture),
        "lower" => text.ToLower(culture),
        // ToTitleCase leaves ALL-CAPS words alone, so lower everything first.
        "title" => culture.TextInfo.ToTitleCase(text.ToLower(culture)),
        _ => text,
    };

    /// <summary>The text exactly as the banner shows it: tokens first, then letter case. Line breaks normalized to \n.</summary>
    public static string Render(string text, string letterCase, DateTime now, CultureInfo culture, bool clock24 = false)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');
        return ApplyCase(ReplaceTokens(normalized, now, culture, clock24), letterCase, culture);
    }
}
