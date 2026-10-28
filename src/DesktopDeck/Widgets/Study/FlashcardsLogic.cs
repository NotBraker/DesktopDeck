using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopDeck.Widgets;

/// <summary>
/// Flashcards with Leitner-box spaced repetition: parsing decks written as lines "front | back", a stable per-card key,
/// box/due-date transitions for Again / Good / Easy, the daily new-card limit, and the order cards come up in.
/// Progress is plain JSON in Item.Options["data.progress"], keyed by a hash of the card text so it survives edits to other cards.
/// </summary>
public static class FlashcardsLogic
{
    public const string ProgressKey = "data.progress", DailyKey = "data.daily";
    public const int MaxProgress = 6000, MaxCardsPerDeck = 2000, Boxes = 5;
    public static readonly int[] DefaultIntervals = [1, 2, 4, 8, 16];

    public enum Grade { Again, Good, Easy }

    /// <summary>A card as written in a deck.</summary>
    public sealed record Card(string Front, string Back, int Deck);

    /// <summary>A card as it is asked: <see cref="Prompt"/> is shown first. Reversed cards ask the back and answer the front.</summary>
    public sealed record Entry(string Prompt, string Answer, string Key, int Deck, bool Reversed);

    public sealed class Progress
    {
        [JsonPropertyName("b")] public int Box { get; set; }
        /// <summary>yyyy-MM-dd the card is next due.</summary>
        [JsonPropertyName("d")] public string Due { get; set; } = "";
        [JsonPropertyName("r")] public int Reps { get; set; }
        [JsonPropertyName("l")] public int Lapses { get; set; }
        /// <summary>Order of the last review: cards failed a moment ago come back after the rest.</summary>
        [JsonPropertyName("s")] public long Stamp { get; set; }
    }

    public sealed class Daily
    {
        [JsonPropertyName("d")] public string Date { get; set; } = "";
        [JsonPropertyName("n")] public int New { get; set; }
        [JsonPropertyName("r")] public int Reviews { get; set; }
        [JsonPropertyName("a")] public int Again { get; set; }
    }

    /// <summary>What is waiting today.</summary>
    public sealed record Plan(IReadOnlyList<Entry> Queue, int DueCount, int NewLeftToday, int NewAvailable, IReadOnlyList<Entry> Extra, int[] BoxCounts)
    {
        public Entry? Next => Queue.Count > 0 ? Queue[0] : null;
    }

    private static readonly JsonSerializerOptions Json = new();

    // ---------- Keys ----------

    /// <summary>FNV-1a 64-bit over the UTF-8 bytes, as 16 lower-case hex digits. Stable across runs, machines and versions.</summary>
    public static string StableHash(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }
        return hash.ToString("x16", CultureInfo.InvariantCulture);
    }

    public static string KeyOf(string prompt, string answer) => StableHash(prompt.Trim() + "|" + answer.Trim());

    // ---------- Decks ----------

    /// <summary>
    /// Lines "front | back" (a tab works too). Only the first bar splits, so answers can contain "|". The two letters \n stand
    /// for a line break. Lines without an answer and lines starting with # are skipped.
    /// </summary>
    public static List<Card> ParseDeck(IEnumerable<string> lines, int deck = 0)
    {
        var cards = new List<Card>();
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            int split = line.IndexOf('|');
            if (split < 0) split = line.IndexOf('\t');
            if (split <= 0) continue;
            string front = Clean(line[..split]), back = Clean(line[(split + 1)..]);
            if (front.Length == 0 || back.Length == 0) continue;
            cards.Add(new Card(front, back, deck));
            if (cards.Count >= MaxCardsPerDeck) break;
        }
        return cards;
    }

    private static string Clean(string text)
    {
        text = text.Trim().Replace("\\n", "\n");
        return text.Length <= 400 ? text : text[..400];
    }

    /// <summary>Cards as they will be asked. direction: "front" (front first), "back" (reverse: back to front) or "both". Duplicates are dropped.</summary>
    public static List<Entry> Entries(IEnumerable<Card> cards, string direction)
    {
        var result = new List<Entry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var card in cards)
        {
            if (direction != "back")
                Add(new Entry(card.Front, card.Back, KeyOf(card.Front, card.Back), card.Deck, false));
            if (direction is "back" or "both")
                Add(new Entry(card.Back, card.Front, KeyOf(card.Back, card.Front), card.Deck, true));
        }
        return result;

        void Add(Entry entry) { if (seen.Add(entry.Key)) result.Add(entry); }
    }

    /// <summary>Five whole-day intervals, one per box. Missing ones double the previous; junk falls back to 1, 2, 4, 8, 16.</summary>
    public static int[] ParseIntervals(string? text)
    {
        var values = (text ?? "").Split([',', ' ', ';', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Select(t => int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var v) && v is >= 1 and <= 3650 ? v : -1).ToList();
        if (values.Count == 0 || values.Any(v => v < 0)) return (int[])DefaultIntervals.Clone();
        while (values.Count < Boxes) values.Add(Math.Min(3650, values[^1] * 2));
        return values.Take(Boxes).ToArray();
    }

    // ---------- Progress ----------

    public static Dictionary<string, Progress> ParseProgress(string? json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, Progress>>(string.IsNullOrWhiteSpace(json) ? "{}" : json, Json) ?? [];
            var clean = new Dictionary<string, Progress>(StringComparer.Ordinal);
            foreach (var (key, p) in raw)
            {
                if (p is null || key.Length == 0) continue;
                p.Box = Math.Clamp(p.Box, 0, Boxes);
                if (!DateTime.TryParseExact(p.Due, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) p.Due = "";
                clean[key] = p;
            }
            return clean;
        }
        catch (JsonException) { return []; }
    }

    public static string SerializeProgress(Dictionary<string, Progress> progress)
    {
        if (progress.Count > MaxProgress)
            progress = progress.OrderByDescending(kv => kv.Value.Stamp).Take(MaxProgress).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return JsonSerializer.Serialize(progress, Json);
    }

    public static Daily ParseDaily(string? json, DateTime today)
    {
        string stamp = Day(today);
        try
        {
            var daily = JsonSerializer.Deserialize<Daily>(string.IsNullOrWhiteSpace(json) ? "{}" : json, Json);
            if (daily is not null && daily.Date == stamp) { daily.New = Math.Max(0, daily.New); daily.Reviews = Math.Max(0, daily.Reviews); daily.Again = Math.Max(0, daily.Again); return daily; }
        }
        catch (JsonException) { }
        return new Daily { Date = stamp };
    }

    public static string SerializeDaily(Daily daily) => JsonSerializer.Serialize(daily, Json);

    public static string Day(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static long NextStamp(Dictionary<string, Progress> progress) => progress.Count == 0 ? 1 : progress.Values.Max(p => p.Stamp) + 1;

    public static bool IsNew(Dictionary<string, Progress> progress, string key) => !progress.TryGetValue(key, out var p) || p.Box == 0 || p.Reps == 0;

    // ---------- Reviewing ----------

    /// <summary>
    /// Again puts the card in box 1 (back today when <paramref name="retryToday"/>, otherwise after the first interval).
    /// Good moves it up one box, Easy two; the card is next due after its new box's interval.
    /// </summary>
    public static Progress Review(Progress? before, Grade grade, DateTime today, IReadOnlyList<int> intervals, bool retryToday, long stamp)
    {
        int oldBox = before?.Box ?? 0;
        var result = new Progress { Reps = (before?.Reps ?? 0) + 1, Lapses = before?.Lapses ?? 0, Stamp = stamp };
        switch (grade)
        {
            case Grade.Again:
                result.Box = 1;
                if (oldBox > 0 && before!.Reps > 0) result.Lapses++;
                result.Due = Day(retryToday ? today : today.AddDays(intervals[0]));
                break;
            default:
                result.Box = Math.Min(Boxes, oldBox + (grade == Grade.Easy ? 2 : 1));
                result.Due = Day(today.AddDays(intervals[result.Box - 1]));
                break;
        }
        return result;
    }

    /// <summary>Days until the card comes back after each answer (0 = again today); for the button labels.</summary>
    public static int PreviewDays(Progress? before, Grade grade, IReadOnlyList<int> intervals, bool retryToday) =>
        grade == Grade.Again ? (retryToday ? 0 : intervals[0]) : intervals[Review(before, grade, DateTime.Today, intervals, retryToday, 0).Box - 1];

    public static string IntervalText(int days) => days switch
    {
        <= 0 => "now",
        1 => "1 day",
        < 14 => $"{days} days",
        < 60 => $"{(int)Math.Round(days / 7.0)} wk",
        < 365 => $"{(int)Math.Round(days / 30.4)} mo",
        _ => $"{Math.Round(days / 365.0, 1).ToString("0.#", CultureInfo.InvariantCulture)} yr",
    };

    public static void RecordReview(Daily daily, bool wasNew, Grade grade)
    {
        daily.Reviews++;
        if (wasNew) daily.New++;
        if (grade == Grade.Again) daily.Again++;
    }

    // ---------- Today's queue ----------

    /// <summary>
    /// Due reviews (oldest due first, cards failed a moment ago last), then new cards up to the daily limit.
    /// <paramref name="order"/> "shuffle" mixes cards among equals using <paramref name="seed"/>; "deck" keeps the deck's order.
    /// <see cref="Plan.Extra"/> holds what you could study ahead of time: cards due later, then new cards beyond the limit.
    /// </summary>
    public static Plan MakePlan(IReadOnlyList<Entry> entries, Dictionary<string, Progress> progress, Daily daily, DateTime today, int newLimit, string order, string seed, bool reviewsFirst = true)
    {
        string todayText = Day(today);
        var due = new List<(Entry Entry, Progress P)>();
        var later = new List<(Entry Entry, Progress P)>();
        var fresh = new List<Entry>();
        var boxCounts = new int[Boxes + 1];
        foreach (var entry in entries)
        {
            if (!progress.TryGetValue(entry.Key, out var p) || p.Reps == 0 || p.Box == 0) { fresh.Add(entry); boxCounts[0]++; continue; }
            boxCounts[Math.Clamp(p.Box, 1, Boxes)]++;
            if (string.CompareOrdinal(p.Due, todayText) <= 0) due.Add((entry, p)); else later.Add((entry, p));
        }
        string Mix(Entry e) => order == "deck" ? "" : StableHash(seed + "|" + e.Key);
        var dueOrdered = due.OrderBy(x => x.P.Due, StringComparer.Ordinal).ThenBy(x => x.P.Stamp).ThenBy(x => Mix(x.Entry), StringComparer.Ordinal).Select(x => x.Entry).ToList();
        var freshOrdered = order == "deck" ? fresh : fresh.OrderBy(Mix, StringComparer.Ordinal).ToList();
        int newLeft = Math.Max(0, newLimit - daily.New);
        var newToday = freshOrdered.Take(newLeft).ToList();
        var queue = reviewsFirst ? dueOrdered.Concat(newToday).ToList() : newToday.Concat(dueOrdered).ToList();
        var extra = later.OrderBy(x => x.P.Due, StringComparer.Ordinal).ThenBy(x => Mix(x.Entry), StringComparer.Ordinal).Select(x => x.Entry).Concat(freshOrdered.Skip(newLeft)).ToList();
        return new Plan(queue, dueOrdered.Count, newToday.Count, fresh.Count, extra, boxCounts);
    }

    /// <summary>Share of today's work done: reviews so far over (reviews so far + what is still waiting).</summary>
    public static double TodayFraction(Daily daily, Plan plan)
    {
        int left = plan.Queue.Count;
        int total = daily.Reviews + left;
        return total == 0 ? 1 : Math.Clamp(daily.Reviews / (double)total, 0, 1);
    }
}
