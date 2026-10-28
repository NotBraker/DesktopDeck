using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DesktopDeck.Widgets;

/// <summary>Small pure helpers shared by the Connected widgets (OBS, GitHub, Notion). Unit tested.</summary>
public static class ConnectedLogic
{
    /// <summary>"12:34" under an hour, "1:02:03" from an hour on (negative spans show as zero).</summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        long hours = (long)span.TotalHours;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes:00}:{span.Seconds:00}");
    }

    /// <summary>A count that stops at a cap: "7", "50+".</summary>
    public static string Count(int value, int cap) => value >= cap ? cap.ToString(CultureInfo.InvariantCulture) + "+" : Math.Max(0, value).ToString(CultureInfo.InvariantCulture);

    /// <summary>Avatars may only be downloaded over https from the services' own image hosts.</summary>
    public static bool IsAvatarUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        string host = uri.Host.ToLowerInvariant();
        return host == "avatars.githubusercontent.com"
            || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    /// <summary>One or two capital letters for a placeholder avatar ("octo cat" gives "OC", "monalisa" gives "M").</summary>
    public static string Initials(string? name)
    {
        var words = (name ?? "").Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "?";
        string first = FirstLetter(words[0]);
        return words.Length > 1 ? first + FirstLetter(words[1]) : first;
    }

    private static string FirstLetter(string word)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(word);
        return enumerator.MoveNext() ? ((string)enumerator.Current).ToUpperInvariant() : "";
    }

    /// <summary>A stable index in [0, count) for a name, so placeholders keep their color.</summary>
    public static int Bucket(string? text, int count)
    {
        if (count <= 0) return 0;
        unchecked
        {
            uint hash = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(text ?? "")) { hash ^= b; hash *= 16777619; }
            return (int)(hash % (uint)count);
        }
    }

    /// <summary>Allows only http(s) addresses on the given hosts (exact or subdomain); used before opening downloaded links.</summary>
    public static bool IsLinkOn(string? url, params string[] hosts)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return false;
        string host = uri.Host.ToLowerInvariant();
        return hosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal));
    }

    /// <summary>Text for a cache-less first paint or a failure that has no more specific wording.</summary>
    public static string GenericProblem(int? status, bool timedOut, bool badData)
    {
        if (badData) return "The service sent something unexpected.";
        if (timedOut) return "The service didn't answer in time.";
        return status switch
        {
            null => "Couldn't connect. Check your connection.",
            429 => "Too many requests — slowing down.",
            >= 500 => "The service is having trouble.",
            _ => $"The service refused the request ({status}).",
        };
    }

    /// <summary>True if the cached error text came from a rejected token, so the footer should say so rather than "Offline".</summary>
    public static bool IsAuthStatus(int? status) => status is 401 or 403;
}
