using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DesktopDeck.Widgets;

/// <summary>Shared pure helpers for the Online widgets: cache times, refresh policy, short ages, numbers, text cleanup.</summary>
public static class OnlineLogic
{
    public static string CacheForRequest(string json, string? cachedSignature, string expectedSignature)
        => string.Equals(cachedSignature, expectedSignature, StringComparison.Ordinal) ? json : "";
    /// <summary>Round-trip text for "cache.time".</summary>
    public static string FormatStamp(DateTimeOffset time) => time.ToString("o", CultureInfo.InvariantCulture);

    public static DateTimeOffset? ParseStamp(string? text)
        => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time) ? time : null;

    /// <summary>True when there's no data yet, it's older than <paramref name="interval"/>, or the clock went backwards a lot.</summary>
    public static bool IsStale(DateTimeOffset? fetched, TimeSpan interval, DateTimeOffset now)
        => fetched is not { } f || now - f >= interval || f - now > TimeSpan.FromHours(1);

    /// <summary>The refresh interval in minutes, never below <paramref name="minimumMinutes"/>.</summary>
    public static TimeSpan Interval(double minutes, double minimumMinutes)
        => TimeSpan.FromMinutes(double.IsFinite(minutes) ? Math.Max(minutes, minimumMinutes) : minimumMinutes);

    /// <summary>After a failed fetch, wait this long before trying again on our own (manual refresh still works).</summary>
    public static TimeSpan RetryDelay(TimeSpan interval) => interval < TimeSpan.FromMinutes(5) ? interval : TimeSpan.FromMinutes(5);

    /// <summary>"now", "5m", "3h", "2d", "3w", or a date for anything older than a year.</summary>
    public static string Age(DateTimeOffset then, DateTimeOffset now, CultureInfo? culture = null)
    {
        var span = now - then;
        if (span < TimeSpan.FromMinutes(1)) return "now";
        if (span < TimeSpan.FromHours(1)) return $"{(int)span.TotalMinutes}m";
        if (span < TimeSpan.FromDays(1)) return $"{(int)span.TotalHours}h";
        if (span < TimeSpan.FromDays(14)) return $"{(int)span.TotalDays}d";
        if (span < TimeSpan.FromDays(365)) return $"{(int)(span.TotalDays / 7)}w";
        return then.ToString("MMM yyyy", culture ?? CultureInfo.CurrentCulture);
    }

    /// <summary>"10:42" for data from today, "Oct 3, 10:42" for older data (in the local time of <paramref name="now"/>).</summary>
    public static string DataTime(DateTimeOffset fetched, DateTimeOffset now, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var local = fetched.ToOffset(now.Offset);
        string time = local.ToString("t", culture);
        return local.Date == now.Date ? time : local.ToString("MMM d", culture) + ", " + time;
    }

    /// <summary>The quiet note shown when the last refresh failed but older data is still on screen.</summary>
    public static string OfflineNote(DateTimeOffset fetched, DateTimeOffset now, CultureInfo? culture = null)
        => $"Offline — showing {DataTime(fetched, now, culture)} data";

    /// <summary>A short, friendly reason for a failed request.</summary>
    public static string Problem(int? statusCode, bool timedOut, bool badData)
    {
        if (badData) return "The service sent something unexpected.";
        if (timedOut) return "The service didn't answer in time.";
        return statusCode switch
        {
            404 => "Not found — check the settings.",
            429 => "Too many requests — slowing down.",
            >= 500 => "The service is having trouble.",
            null => "Couldn't connect. Check your connection.",
            _ => $"The service refused the request ({statusCode}).",
        };
    }

    /// <summary>Collapses runs of whitespace (including line breaks) into single spaces.</summary>
    public static string Squash(string? text) => string.IsNullOrEmpty(text) ? "" : Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>Shortens text to at most <paramref name="max"/> characters, ending in "…" at a word break when possible.</summary>
    public static string Clip(string? text, int max)
    {
        text = Squash(text);
        if (text.Length <= max || max < 2) return text;
        int cut = text.LastIndexOf(' ', max - 1);
        if (cut < max / 2) cut = max - 1;
        return text[..cut].TrimEnd(' ', ',', ';', ':', '.', '-') + "…";
    }

    /// <summary>Removes tags, scripts and styles from HTML and decodes entities.</summary>
    public static string StripHtml(string? html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        string text = Regex.Replace(html, @"<(script|style)\b[^>]*>.*?</\1\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"<!--.*?-->", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"<\s*(br|/p|/div|/li|/h\d)\b[^>]*>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]*>", "");
        // Some feeds escape twice ("&amp;amp;"); decoding twice is harmless for normal text.
        text = WebUtility.HtmlDecode(WebUtility.HtmlDecode(text));
        return Squash(text.Replace(' ', ' '));
    }

    /// <summary>The host of a web address without "www.", or "" if it isn't one.</summary>
    public static string Domain(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return "";
        string host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    /// <summary>Only http(s) addresses may be opened from downloaded data.</summary>
    public static bool IsWebLink(string? url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// A number with <paramref name="decimals"/> places. With <paramref name="smart"/>, small values get enough places to
    /// show three significant digits (0.000123) and large values drop decimals beyond what matters.
    /// </summary>
    public static string Number(double value, int decimals, bool smart, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (!double.IsFinite(value)) return "—";
        decimals = Math.Clamp(decimals, 0, 10);
        if (smart)
        {
            double abs = Math.Abs(value);
            if (abs > 0 && abs < 1)
            {
                int needed = (int)Math.Ceiling(-Math.Log10(abs)) + 2;
                decimals = Math.Clamp(Math.Max(decimals, needed), 0, 10);
            }
            else if (abs >= 100_000) decimals = 0;
        }
        return value.ToString("N" + decimals, culture);
    }

    /// <summary>1.2K, 3.4M, 5.6B, 7.8T.</summary>
    public static string Compact(double value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        double abs = Math.Abs(value);
        (double div, string suffix) = abs switch { >= 1e12 => (1e12, "T"), >= 1e9 => (1e9, "B"), >= 1e6 => (1e6, "M"), >= 1e3 => (1e3, "K"), _ => (1, "") };
        double scaled = value / div;
        return scaled.ToString(Math.Abs(scaled) >= 100 || suffix.Length == 0 ? "N0" : "0.#", culture) + suffix;
    }

    /// <summary>"+1.85%" / "−0.40%" with a real minus sign.</summary>
    public static string Percent(double value, int decimals = 2, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        if (!double.IsFinite(value)) return "—";
        string body = Math.Abs(value).ToString("N" + Math.Clamp(decimals, 0, 4), culture) + "%";
        return (Math.Round(value, Math.Clamp(decimals, 0, 4)) switch { > 0 => "+", < 0 => "−", _ => "" }) + body;
    }

    /// <summary>A short stable hash for remembering things (e.g. which articles were opened).</summary>
    public static string Hash(string text)
    {
        unchecked
        {
            ulong hash = 14695981039346656037;
            foreach (byte b in Encoding.UTF8.GetBytes(text)) { hash ^= b; hash *= 1099511628211; }
            return hash.ToString("x16", CultureInfo.InvariantCulture)[..12];
        }
    }

    /// <summary>A deterministic shuffle (same seed, same order), e.g. one order per day.</summary>
    public static T[] Shuffle<T>(System.Collections.Generic.IEnumerable<T> items, int seed)
    {
        var list = items.ToArray();
        var random = new Random(seed);
        for (int i = list.Length - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
        return list;
    }
}
