using System.Net.Http;

namespace DesktopDeck.Services;

/// <summary>A calendar problem with a message that is safe to show (it never contains tokens or private calendar addresses).</summary>
public class CalendarException : Exception
{
    public CalendarException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Signing in to Google failed or was cancelled, or the saved connection no longer works.</summary>
public sealed class GoogleAuthException : CalendarException
{
    /// <summary>True when the user has to press Connect again.</summary>
    public bool NeedsReconnect { get; }
    public GoogleAuthException(string message, bool needsReconnect = false, Exception? inner = null) : base(message, inner) => NeedsReconnect = needsReconnect;

    public const string Cancelled = "Google sign-in was cancelled";
    public const string Expired = "Your Google connection expired — Connect again";
    public const string AdminBlocked = "Your school or organization blocks this app — ask your admin to allow it, or use the iCal link of your calendar instead";
}

/// <summary>One shared HttpClient for calendar traffic (Google's servers and the user's iCal hosts only).</summary>
public static class CalendarHttp
{
    public static readonly HttpClient Client = Create();

    private static HttpClient Create()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DesktopDeck");
        return client;
    }
}
