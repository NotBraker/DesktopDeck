using System.Windows;
using System.Windows.Controls;
using DesktopDeck.Services;

namespace DesktopDeck.Widgets;

/// <summary>Shared plumbing for the Google widgets (Gmail, Classroom): turning API problems into widget messages, and incremental consent.</summary>
internal static class GoogleKit
{
    /// <summary>Runs a Google call for <see cref="ConnectedKit"/>: a Google problem becomes a message the widget can show (with the "fix it" styling when the user has to act).</summary>
    public static async Task<string> Guard(Func<Task<string>> work)
    {
        try { return await work(); }
        catch (GoogleApiException ex) { throw new ConnectedException(ex.Message, ex.Status, auth: ex.NeedsUserAction); }
        catch (GoogleAuthException ex) { throw new ConnectedException(ex.Message, 401, auth: true); }
        catch (CalendarException ex) { throw new ConnectedException(ex.Message); }
    }

    /// <summary>
    /// Asks Google for this feature's permission (the browser opens Google's consent page; nothing is asked again if it was granted),
    /// then redraws. Works while another sign-in is not already open.
    /// </summary>
    public static void Enable(WidgetContext ctx, GoogleFeature feature, Action? granted = null)
    {
        ctx.RunOnce("google-enable:" + feature.Key, async () =>
        {
            ctx.Status($"Allow {feature.Name} in your browser…");
            try
            {
                await ctx.Google.EnsureScopesAsync(feature.Scopes);
                if (!ctx.Google.HasScopes(feature.Scopes))
                {
                    ctx.Inform($"Google did not allow {feature.Name}. Press Enable again and leave every box on Google's page ticked.", ctx.Item.Name);
                    return;
                }
                ctx.Status($"{feature.Name} is enabled.");
                granted?.Invoke();
                ctx.Rebuild();
            }
            catch (CalendarException ex) { ctx.Inform(ex.Message, ctx.Item.Name); }
        });
    }

    /// <summary>The setup state for a Google widget that cannot show anything yet: connect an account, or enable this feature.</summary>
    public static FrameworkElement Setup(WidgetContext ctx, GoogleFeature feature, string glyph, string title, string what)
    {
        var (card, body) = WidgetKit.Frame(ctx);
        bool connected = !ctx.IsSnapshot && ctx.Google.IsConnected && !ctx.Google.SampleMode;
        StackPanel panel = connected
            ? ConnectedKit.SetupPanel(ctx, glyph, title, what + " Press Enable to allow it; Google's page opens in your browser.", "Enable " + feature.Name, () => Enable(ctx, feature))
            : ConnectedKit.SetupPanel(ctx, glyph, title, what + " First connect a Google account in Settings → Connected accounts.", "Open Settings", ctx.OpenSettings);
        body.Children.Add(WidgetKit.Scroll(panel));
        return card;
    }
}
