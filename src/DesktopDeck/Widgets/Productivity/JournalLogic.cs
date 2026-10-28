using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace DesktopDeck.Widgets;

/// <summary>Daily journal and mood: entries by day, the streak, the recent-days strip, labels and palettes. Pure logic (unit tested).</summary>
public static class JournalLogic
{
    /// <summary>One day: a mood from 1 (worst) to <see cref="Scale"/> (best), 0 = none, and an optional one-line note.</summary>
    public sealed class JournalDay
    {
        public int Mood { get; set; }
        public string Note { get; set; } = "";
    }

    public const int Scale = 5;
    /// <summary>Entries older than this many days are dropped when saving.</summary>
    public const int KeepDays = 3 * 366;
    public static readonly string[] DefaultLabels = ["Awful", "Bad", "Okay", "Good", "Great"];
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static Dictionary<string, JournalDay> Parse(string? json)
    {
        var result = new Dictionary<string, JournalDay>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, JournalDay>>(json, Json);
            if (raw is null) return result;
            foreach (var (key, day) in raw)
            {
                if (day is null || !DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) continue;
                day.Mood = Math.Clamp(day.Mood, 0, Scale);
                day.Note = (day.Note ?? "").Trim();
                if (!IsEmpty(day)) result[key] = day;
            }
        }
        catch (JsonException) { }
        return result;
    }

    /// <summary>Saves the entries, oldest first, dropping empty days and days older than <paramref name="keepDays"/> before <paramref name="today"/> (0 = keep all).</summary>
    public static string Serialize(Dictionary<string, JournalDay> entries, DateTime today, int keepDays = KeepDays)
    {
        string oldest = Key(today.Date.AddDays(-keepDays));
        var kept = entries.Where(e => !IsEmpty(e.Value) && (keepDays <= 0 || string.CompareOrdinal(e.Key, oldest) >= 0)).OrderBy(e => e.Key, StringComparer.Ordinal).ToDictionary(e => e.Key, e => e.Value);
        return JsonSerializer.Serialize(kept, Json);
    }

    public static bool IsEmpty(JournalDay day) => day.Mood <= 0 && string.IsNullOrWhiteSpace(day.Note);

    public static JournalDay? Get(Dictionary<string, JournalDay> entries, DateTime day) => entries.TryGetValue(Key(day), out var entry) ? entry : null;

    /// <summary>Picks a mood for the day; picking the same mood again clears it.</summary>
    public static void SetMood(Dictionary<string, JournalDay> entries, DateTime day, int mood)
    {
        mood = Math.Clamp(mood, 0, Scale);
        string key = Key(day);
        if (!entries.TryGetValue(key, out var entry)) entries[key] = entry = new JournalDay();
        entry.Mood = entry.Mood == mood ? 0 : mood;
        if (IsEmpty(entry)) entries.Remove(key);
    }

    /// <summary>Sets (or with blank text clears) the day's note.</summary>
    public static void SetNote(Dictionary<string, JournalDay> entries, DateTime day, string note)
    {
        string key = Key(day);
        if (!entries.TryGetValue(key, out var entry)) entries[key] = entry = new JournalDay();
        entry.Note = note.Trim(); if (entry.Note.Length > 500) entry.Note = entry.Note[..500];
        if (IsEmpty(entry)) entries.Remove(key);
    }

    /// <summary>Days in a row with a mood or a note, ending today (or yesterday while today is still blank).</summary>
    public static int Streak(Dictionary<string, JournalDay> entries, DateTime today)
    {
        var day = today.Date;
        if (Get(entries, day) is null) day = day.AddDays(-1);
        int streak = 0;
        while (Get(entries, day) is { } entry && !IsEmpty(entry)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    /// <summary>The last <paramref name="count"/> days, oldest first, ending today.</summary>
    public static List<(DateTime Day, JournalDay? Entry)> LastDays(Dictionary<string, JournalDay> entries, DateTime today, int count)
    {
        var list = new List<(DateTime, JournalDay?)>();
        for (int i = Math.Max(1, count) - 1; i >= 0; i--)
        {
            var day = today.Date.AddDays(-i);
            list.Add((day, Get(entries, day)));
        }
        return list;
    }

    /// <summary>The average mood over the last <paramref name="count"/> days, or null when none was logged.</summary>
    public static double? Average(Dictionary<string, JournalDay> entries, DateTime today, int count)
    {
        var moods = LastDays(entries, today, count).Where(d => d.Entry is { Mood: > 0 }).Select(d => (double)d.Entry!.Mood).ToList();
        return moods.Count == 0 ? null : moods.Average();
    }

    /// <summary>Five labels from "Awful, Bad, Okay, Good, Great"; missing ones fall back to the defaults.</summary>
    public static string[] Labels(string? text)
    {
        var parts = (text ?? "").Split(',').Select(p => p.Trim()).ToArray();
        return Enumerable.Range(0, Scale).Select(i => i < parts.Length && parts[i].Length > 0 ? parts[i] : DefaultLabels[i]).ToArray();
    }

    /// <summary>Five colors, worst mood first. "custom" reads up to five comma-separated colors from <paramref name="custom"/>; null for "accent" (shades of the accent).</summary>
    public static string[]? Palette(string name, string? custom = null)
    {
        string[] classic = ["#ef4444", "#f97316", "#eab308", "#84cc16", "#22c55e"];
        return name switch
        {
            "sunset" => ["#6d28d9", "#c026d3", "#f43f5e", "#fb923c", "#facc15"],
            "ocean" => ["#475569", "#3b82f6", "#06b6d4", "#2dd4bf", "#34d399"],
            "earth" => ["#8E8270", "#B47A4E", "#C4684F", "#c2a14d", "#9B9A5E"],
            "accent" => null,
            "custom" => Enumerable.Range(0, Scale).Select(i => (custom ?? "").Split(',').Select(c => c.Trim()).ElementAtOrDefault(i) is { } c && IsHex(c) ? c : classic[i]).ToArray(),
            _ => classic,
        };
    }

    /// <summary>"#rgb", "#rrggbb" or "#aarrggbb".</summary>
    public static bool IsHex(string text) =>
        text.Length is 4 or 7 or 9 && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);
}

