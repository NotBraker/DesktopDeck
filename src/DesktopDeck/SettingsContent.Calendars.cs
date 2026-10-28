using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

// Settings → Connected accounts: a Google account (view and edit) and read-only iCal links.
internal sealed partial class SettingsContent
{
    private CancellationTokenSource? connecting;

    private void BuildConnectedAccounts(StackPanel root)
    {
        var section = Section(root, "Connected accounts");

        // ---------- Google ----------
        var googleTitle = Theme.Label("Google account (view and edit)", 13.5);
        googleTitle.FontWeight = FontWeights.SemiBold;
        section.Children.Add(googleTitle);
        var intro = Hint("Show your Google Calendar on Schedule and Agenda, and add or change events from here. You use your own Google Cloud client, so nothing is shared.");
        intro.Margin = new Thickness(0, 4, 0, 10);
        section.Children.Add(intro);
        var googleHost = new StackPanel();
        section.Children.Add(googleHost);

        var divider = new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 8, 0, 14) };
        section.Children.Add(divider);

        // ---------- iCal links ----------
        var linksTitle = Theme.Label("Calendar links (read-only)", 13.5);
        linksTitle.FontWeight = FontWeights.SemiBold;
        section.Children.Add(linksTitle);
        var linksIntro = Hint("Paste a calendar's private iCal address (Google Calendar settings → Secret address in iCal format). It is saved encrypted on this PC.");
        linksIntro.Margin = new Thickness(0, 4, 0, 10);
        section.Children.Add(linksIntro);
        var linksHost = new StackPanel();
        section.Children.Add(linksHost);

        // ---------- Google state ----------
        var status = Theme.Label("", 12, Theme.Text2);
        status.TextWrapping = TextWrapping.Wrap;
        status.Margin = new Thickness(0, 4, 0, 10);
        bool busy = false;
        void Say(string text, Brush? color = null) { status.Text = text; status.Foreground = color ?? Theme.Text2; }

        void ShowGoogle()
        {
            googleHost.Children.Clear();
            var google = deck.Google;
            if (google.IsConnected)
            {
                string email = google.SampleMode ? settings.GoogleEmail : google.Email is { Length: > 0 } e ? e : settings.GoogleEmail;
                var who = Theme.Label("Connected" + (email.Length > 0 ? " as " + email : ""), 13, Theme.Olive);
                who.FontWeight = FontWeights.SemiBold;
                who.Margin = new Thickness(0, 0, 0, 4);
                googleHost.Children.Add(who);
                bool canEdit = google.HasScopes(GoogleScopes.CalendarEvents);
                googleHost.Children.Add(Hint(canEdit ? "You can view and edit events. Each Schedule can also pick its own calendars in Customize." : "This connection is view-only. Press Disconnect, then Connect again to allow editing."));
                var calendars = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
                if (settings.GoogleCalendars.Count == 0) calendars.Children.Add(Theme.Label("No calendars loaded yet. Press Refresh calendars.", 12, Theme.Muted));
                foreach (var calendar in settings.GoogleCalendars)
                {
                    var c = calendar;
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                    row.ColumnDefinitions.Add(new ColumnDefinition());
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var dot = CalendarVisuals.Dot(c.Color, 12);
                    dot.HorizontalAlignment = HorizontalAlignment.Left;
                    dot.ToolTip = "Color from Google: " + c.Color;
                    row.Children.Add(dot);
                    var name = new StackPanel { Orientation = Orientation.Horizontal };
                    name.Children.Add(Theme.Label(c.Name, 13));
                    if (!c.CanEdit) name.Children.Add(Theme.Label("  view only", 11.5, Theme.Muted));
                    if (c.Primary) name.Children.Add(Theme.Label("  main", 11.5, Theme.Muted));
                    Grid.SetColumn(name, 1);
                    row.Children.Add(name);
                    var toggle = Theme.Switch(c.Visible, v => { c.Visible = v; deck.CalendarSettingsChanged(); });
                    Grid.SetColumn(toggle, 2);
                    row.Children.Add(toggle);
                    calendars.Children.Add(row);
                }
                googleHost.Children.Add(calendars);
                var buttons = new WrapPanel();
                var refresh = Theme.Button("Refresh calendars");
                refresh.IsEnabled = !google.SampleMode && !busy;
                refresh.Click += async (_, _) => await RefreshCalendarsAsync();
                var disconnect = Theme.Button("Disconnect");
                disconnect.Foreground = Theme.Danger;
                disconnect.IsEnabled = !google.SampleMode && !busy;
                disconnect.Click += async (_, _) => await DisconnectAsync();
                foreach (var b in new[] { refresh, disconnect }) { b.Margin = new Thickness(0, 0, 8, 8); buttons.Children.Add(b); }
                googleHost.Children.Add(buttons);
                AddFeatures(googleHost, google);
            }
            else
            {
                if (google.HasClient) googleHost.Children.Add(CalendarVisuals.Wrapped(GoogleAuthException.Expired + ". Your saved client is still here, so just press Connect.", 12.5, Theme.Danger, 10));
                var setup = PanelKit.Collapsible(googleHost, "settings.google.setup", "Setup help", "How to get a client ID and secret", nested: true);
                var steps = new[]
                {
                    "Open the Google Cloud console (console.cloud.google.com) and create a new project.",
                    "APIs & Services → Library → enable \"Google Calendar API\". For the other features you want, also enable the Tasks, Gmail and Classroom APIs the same way (each is one click and free).",
                    "OAuth consent screen: choose External and add your own Google address as a test user. In Testing mode Google signs you out after 7 days; press Publish app (\"In production\") to avoid that. Google warns that the app is unverified; that is fine, it is your own app (Advanced → continue).",
                    "Credentials → Create credentials → OAuth client ID → Application type: Desktop app. (If Google says a permission is not allowed for the app, add it under OAuth consent screen → Data access.)",
                    "Paste the client ID and client secret below and press Connect. Your browser opens Google's sign-in page.",
                };
                for (int i = 0; i < steps.Length; i++)
                {
                    var step = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                    step.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                    step.ColumnDefinitions.Add(new ColumnDefinition());
                    var number = Theme.Label((i + 1) + ".", 12, Theme.Primary);
                    number.FontWeight = FontWeights.SemiBold;
                    number.VerticalAlignment = VerticalAlignment.Top;
                    step.Children.Add(number);
                    var text = Theme.Label(steps[i], 12, Theme.Text2);
                    text.TextWrapping = TextWrapping.Wrap;
                    Grid.SetColumn(text, 1);
                    step.Children.Add(text);
                    setup.Body.Children.Add(step);
                }
                var console = Theme.Button("Open Google Cloud console");
                console.HorizontalAlignment = HorizontalAlignment.Left;
                console.Margin = new Thickness(0, 4, 0, 12);
                console.Click += (_, _) => CalendarVisuals.OpenLink("https://console.cloud.google.com/");
                setup.Body.Children.Add(console);

                var idBox = ItemEditor.Field(googleHost, "OAuth client ID", "");
                idBox.ToolTip = "Ends with .apps.googleusercontent.com";
                var secretLabel = Theme.Label("OAuth client secret", 12, Theme.Muted);
                secretLabel.Margin = new Thickness(0, 0, 0, 5);
                googleHost.Children.Add(secretLabel);
                var secretBox = Theme.SecretField();
                secretBox.Margin = new Thickness(0, 0, 0, 12);
                googleHost.Children.Add(secretBox);
                var connect = Theme.Button("Connect", true);
                connect.HorizontalAlignment = HorizontalAlignment.Left;
                connect.IsEnabled = !google.SampleMode;
                connect.Click += async (_, _) =>
                {
                    if (connecting is not null) { connecting.Cancel(); return; }
                    await ConnectAsync(idBox.Text, secretBox.Password, connect);
                };
                googleHost.Children.Add(connect);
                googleHost.Children.Add(Hint("The client ID and secret are saved encrypted on this PC. Google's sign-in opens in your browser and answers back to this PC only."));
            }
            googleHost.Children.Add(status);
        }

        void AddFeatures(StackPanel host, GoogleAuth google)
        {
            host.Children.Add(new Border { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 4, 0, 12) });
            var heading = Theme.Label("Google features", 13.5);
            heading.FontWeight = FontWeights.SemiBold;
            host.Children.Add(heading);
            var hint = Hint("Each feature asks Google for its own permission the first time you press Enable. Disconnect here to turn them all off.");
            hint.Margin = new Thickness(0, 4, 0, 10);
            host.Children.Add(hint);
            foreach (var feature in GoogleFeatures.All)
            {
                var f = feature;
                bool on = google.HasScopes(f.Scopes);
                var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var text = new StackPanel();
                text.Children.Add(Theme.Label(f.Name, 13));
                var description = Theme.Label(f.Description, 11.5, Theme.Muted);
                description.TextWrapping = TextWrapping.Wrap;
                text.Children.Add(description);
                row.Children.Add(text);
                FrameworkElement state;
                if (on)
                {
                    var label = Theme.Label("On", 12.5, Theme.Olive);
                    label.FontWeight = FontWeights.SemiBold;
                    label.VerticalAlignment = VerticalAlignment.Center;
                    state = label;
                }
                else
                {
                    var enable = Theme.Button("Enable", true);
                    enable.MinHeight = 28;
                    enable.Padding = new Thickness(12, 3, 12, 3);
                    enable.VerticalAlignment = VerticalAlignment.Center;
                    enable.IsEnabled = !google.SampleMode && !busy;
                    enable.ToolTip = "Needs the " + f.ApiName + " switched on in your Google Cloud project";
                    enable.Click += async (_, _) => await EnableAsync(f);
                    state = enable;
                }
                state.Margin = new Thickness(12, 0, 0, 0);
                Grid.SetColumn(state, 1);
                row.Children.Add(state);
                host.Children.Add(row);
            }
            var help = PanelKit.Collapsible(host, "settings.google.permissions", "Setup help", "About the Gmail permission", nested: true);
            var restricted = Hint("Gmail is one of Google's \"restricted\" permissions. For a personal client Google shows an \"unverified app\" warning on the consent page (Advanced → Go to … (unsafe)); that is expected for your own app. Desktop Deck only reads message metadata, can never send, change or delete mail, and keeps what it downloads on this PC. You can also remove its access at myaccount.google.com/permissions.");
            restricted.Margin = new Thickness(0, 0, 0, 6);
            help.Body.Children.Add(restricted);
        }

        async Task EnableAsync(GoogleFeature feature)
        {
            if (connecting is not null) return;
            connecting = new CancellationTokenSource();
            var cancel = connecting.Token;
            busy = true;
            Say($"Waiting for you to allow {feature.Name} in your browser…");
            ShowGoogle();
            Brush color = Theme.Text2;
            string message;
            try
            {
                await deck.Google.EnsureScopesAsync(feature.Scopes, cancel);
                if (!deck.Google.HasScopes(feature.Scopes)) { message = $"Google did not allow {feature.Name}. Press Enable again and leave every box on Google's page ticked."; color = Theme.Danger; }
                else
                {
                    message = $"{feature.Name} is enabled.";
                    color = Theme.Olive;
                    if (feature == GoogleFeatures.Tasks)
                    {
                        try { await deck.LoadTaskListsAsync(cancel); }
                        catch (CalendarException ex) { message += " (Your task lists could not be loaded yet: " + ex.Message + ")"; color = Theme.Text2; }
                    }
                    deck.RenderAll();
                }
            }
            catch (CalendarException ex) { message = ex.Message; color = Theme.Danger; }
            catch (OperationCanceledException) { message = GoogleAuthException.Cancelled + "."; color = Theme.Danger; }
            finally { connecting?.Dispose(); connecting = null; busy = false; }
            if (Alive) { ShowGoogle(); Say(message, color); }
        }

        async Task ConnectAsync(string clientId, string clientSecret, Button connect)
        {
            connecting = new CancellationTokenSource();
            var cancel = connecting.Token;
            busy = true;
            connect.Content = "Cancel";
            Say("Waiting for you to finish in your browser…");
            try
            {
                string email = await deck.Google.ConnectAsync(clientId, clientSecret, cancel);
                settings.GoogleEmail = email;
                Say("Loading your calendars…");
                await LoadCalendarsAsync(cancel);
                deck.CalendarSettingsChanged();
                Say("Connected.", Theme.Olive);
            }
            catch (CalendarException ex) { Say(ex.Message, Theme.Danger); deck.CalendarSettingsChanged(); }
            catch (OperationCanceledException) { Say(GoogleAuthException.Cancelled + ".", Theme.Danger); }
            finally
            {
                connecting?.Dispose();
                connecting = null;
                busy = false;
                if (Alive) { string keep = status.Text; Brush color = status.Foreground; ShowGoogle(); Say(keep, color); }
            }
        }

        async Task LoadCalendarsAsync(CancellationToken cancel)
        {
            var fresh = await deck.Calendar.Google.ListCalendarsAsync(cancel);
            settings.GoogleCalendars = GoogleCalendarService.MergeCalendars(settings.GoogleCalendars, fresh);
        }

        async Task RefreshCalendarsAsync()
        {
            busy = true;
            Say("Loading your calendars…");
            string message = "Calendars updated.";
            Brush color = Theme.Olive;
            try { await LoadCalendarsAsync(CancellationToken.None); deck.CalendarSettingsChanged(); }
            catch (CalendarException ex) { message = ex.Message; color = Theme.Danger; }
            finally { busy = false; if (Alive) { ShowGoogle(); Say(message, color); } }
        }

        async Task DisconnectAsync()
        {
            if (MessageBox.Show(owner, "Disconnect this Google account? Desktop Deck revokes its access at Google and forgets the sign-in and your client ID and secret. Your Google Calendar itself is not changed.", "Disconnect Google", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            busy = true;
            Say("Disconnecting…");
            bool revoked = await deck.Google.DisconnectAsync();
            settings.GoogleEmail = "";
            settings.GoogleCalendars = new List<GoogleCalendarChoice>();
            deck.GoogleDisconnected();
            busy = false;
            deck.CalendarSettingsChanged();
            ShowGoogle();
            Say(revoked ? "Disconnected, and access was revoked at Google." : "Disconnected here. Google could not be reached to revoke access; you can remove it at myaccount.google.com/permissions.", revoked ? Theme.Olive : Theme.Text2);
        }

        // ---------- Link state ----------
        var linkStatus = Theme.Label("", 12, Theme.Text2);
        linkStatus.TextWrapping = TextWrapping.Wrap;
        linkStatus.Margin = new Thickness(0, 4, 0, 6);
        string chosenColor = Theme.Swatches[5];
        bool adding = false;

        void ShowLinks()
        {
            linksHost.Children.Clear();
            if (settings.IcsCalendars.Count == 0) linksHost.Children.Add(Theme.Label("No links yet.", 12, Theme.Muted));
            foreach (var link in settings.IcsCalendars.ToList())
            {
                var l = link;
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var dot = CalendarVisuals.Dot(l.Color, 12);
                dot.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(dot);
                var name = new StackPanel { Orientation = Orientation.Horizontal };
                name.Children.Add(Theme.Label(l.Label, 13));
                name.Children.Add(Theme.Label(deck.Secrets.Has(l.Entry) ? "  address saved" : "  address missing", 11.5, deck.Secrets.Has(l.Entry) ? Theme.Muted : Theme.Danger));
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var toggle = Theme.Switch(l.Enabled, v => { l.Enabled = v; deck.CalendarSettingsChanged(); });
                toggle.Margin = new Thickness(0, 0, 10, 0);
                Grid.SetColumn(toggle, 2);
                row.Children.Add(toggle);
                var remove = Theme.Button("Remove");
                remove.Foreground = Theme.Danger;
                remove.MinHeight = 28;
                remove.Padding = new Thickness(10, 3, 10, 3);
                remove.Click += (_, _) =>
                {
                    if (MessageBox.Show(owner, $"Remove the calendar link \"{l.Label}\"? Its saved address is deleted from this PC.", "Remove calendar link", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    try { deck.Secrets.Remove(l.Entry); }
                    catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { linkStatus.Text = "Could not remove the address: " + ex.Message; return; }
                    settings.IcsCalendars.Remove(l);
                    deck.CalendarSettingsChanged();
                    ShowLinks();
                };
                Grid.SetColumn(remove, 3);
                row.Children.Add(remove);
                linksHost.Children.Add(row);
            }

            var form = PanelKit.Collapsible(linksHost, "settings.addlink", "+ Add a calendar link", nested: true);
            var labelBox = ItemEditor.Field(form.Body, "Name (for example School)", "");
            var addressLabel = Theme.Label("Private iCal address", 12, Theme.Muted);
            addressLabel.Margin = new Thickness(0, 0, 0, 5);
            form.Body.Children.Add(addressLabel);
            var addressBox = Theme.SecretField();
            addressBox.ToolTip = "Paste the secret address (https:// or webcal://)";
            addressBox.Margin = new Thickness(0, 0, 0, 10);
            form.Body.Children.Add(addressBox);
            var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            var swatchBorders = new List<(string Color, Border Border)>();
            void ShowChosen() { foreach (var (color, border) in swatchBorders) border.BorderBrush = color == chosenColor ? Theme.Text : Brushes.Transparent; }
            foreach (var color in Theme.Swatches)
            {
                var swatch = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Margin = new Thickness(0, 0, 7, 0), Background = Theme.Accent(color), Cursor = System.Windows.Input.Cursors.Hand, ToolTip = color, BorderThickness = new Thickness(2), BorderBrush = Brushes.Transparent };
                string picked = color;
                swatch.MouseLeftButtonUp += (_, _) => { chosenColor = picked; ShowChosen(); };
                swatchBorders.Add((color, swatch));
                swatches.Children.Add(swatch);
            }
            ShowChosen();
            form.Body.Children.Add(swatches);
            var add = Theme.Button("Add link", true);
            add.HorizontalAlignment = HorizontalAlignment.Left;
            add.Click += async (_, _) =>
            {
                if (adding) return;
                string? address = IcsCalendar.NormalizeAddress(addressBox.Password);
                if (address is null) { linkStatus.Text = "Paste the calendar's private address (it starts with https:// or webcal://)."; linkStatus.Foreground = Theme.Danger; return; }
                string label = labelBox.Text.Trim();
                adding = true;
                add.IsEnabled = false;
                linkStatus.Text = "Checking the link…";
                linkStatus.Foreground = Theme.Text2;
                try
                {
                    var (count, feedName) = await deck.Calendar.TestLinkAsync(address, label);
                    if (label.Length == 0) label = feedName.Length > 0 ? feedName : "Calendar " + (settings.IcsCalendars.Count + 1);
                    var info = new IcsCalendarInfo { Label = label, Color = chosenColor };
                    deck.Secrets.Set(info.Entry, address);
                    settings.IcsCalendars.Add(info);
                    deck.CalendarSettingsChanged();
                    ShowLinks();
                    linkStatus.Text = $"Added \"{label}\" ({count} event{(count == 1 ? "" : "s")} found).";
                    linkStatus.Foreground = Theme.Olive;
                }
                catch (CalendarException ex) { linkStatus.Text = ex.Message; linkStatus.Foreground = Theme.Danger; }
                catch (Exception ex) when (ex is System.IO.IOException or SecretStore.CryptographicFailure) { linkStatus.Text = "Could not save the address: " + ex.Message; linkStatus.Foreground = Theme.Danger; }
                finally { adding = false; add.IsEnabled = true; }
            };
            form.Body.Children.Add(add);
            linksHost.Children.Add(linkStatus);
        }

        // Building the window never contacts any server: it only shows what is already saved.
        ShowGoogle();
        ShowLinks();
    }
}
