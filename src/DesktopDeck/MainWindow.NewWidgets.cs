using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DesktopDeck.Models;
using DesktopDeck.Services;
using Shapes = System.Windows.Shapes;

namespace DesktopDeck;

// The v0.5 widgets: sticky note, countdown, media player, folder, weather.
public sealed partial class MainWindow
{
    /// <summary>Folder widgets watch their folder; one watcher per item on this surface.</summary>
    private readonly Dictionary<string, (string Path, FileSystemWatcher Watcher, DispatcherTimer Settle)> watchers = new();
    /// <summary>Network fetches in flight (shared by every surface so each widget fetches once).</summary>
    private static readonly HashSet<string> Fetching = new();
    private static readonly Dictionary<string, string> WeatherProblems = new();
    private static readonly Dictionary<string, DateTimeOffset> WeatherAttempts = new();

    // ---------- Sticky note ----------

    private FrameworkElement BuildNote(LauncherItem item, double w, double h)
    {
        var edit = Theme.IconButton(Theme.Glyphs.Edit, "Edit note", 24);
        edit.Click += (_, _) => EditNote(item);
        var (card, body) = Frame(item, null, item.ShowHeader ? new FrameworkElement[] { edit } : Array.Empty<FrameworkElement>());
        // A paper-like card tinted with the note's color.
        // With one look for every widget the note follows the deck's card instead.
        if (!Appearance.UsesDeckLook(settings.Appearance, item))
        {
            if (item.CardStyle == "solid") card.Background = Theme.Mix(Theme.SurfaceHex, item.Accent, 0.2);
            else if (item.CardStyle == "glass") card.Background = Theme.Tint(item.Accent, 0.22);
            card.BorderBrush = Theme.Tint(item.Accent, 0.45);
        }
        string text = string.IsNullOrWhiteSpace(item.Text) ? "Click to write a note." : item.Text;
        double area = Math.Max(1, (w - 30) * (h - (item.ShowHeader ? 56 : 26)));
        double size = Math.Clamp(Math.Sqrt(area / Math.Max(24, text.Length)) * 0.62, 12, 30);
        var label = Theme.Label(text, size, string.IsNullOrWhiteSpace(item.Text) ? Theme.Muted : Theme.Text);
        label.TextWrapping = TextWrapping.Wrap;
        label.VerticalAlignment = VerticalAlignment.Top;
        label.LineHeight = size * 1.35;
        body.Children.Add(Scroll(label));
        body.Margin = new Thickness(16, item.ShowHeader ? 11 : 14, 14, 12);
        card.Cursor = Cursors.Hand;
        card.ToolTip = "Click to edit";
        card.MouseLeftButtonUp += (_, e) => { if (IsEditing) return; e.Handled = true; EditNote(item); };
        return card;
    }

    private void EditNote(LauncherItem item)
    {
        if (Modal(() => InputDialog.Ask(DialogOwner, "Sticky note", "Note", item.Text, "Save", multiline: true, allowEmpty: true)) is not { } text) return;
        item.Text = text;
        Changed(item);
    }

    // ---------- Countdown ----------

    private FrameworkElement BuildCountdown(LauncherItem item, double w, double h)
    {
        var card = Card(item);
        var accent = Theme.Accent(item.Accent);
        var view = Countdown.Describe(DateTime.Now, item.TargetDate, item.TargetTime, item.RepeatYearly);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(14, 8, 14, 8) };
        if (item.ShowHeader)
        {
            var title = Theme.Label(item.Name, Math.Clamp(h * 0.1, 12, 18), Theme.Text2);
            title.FontWeight = FontWeights.SemiBold;
            title.HorizontalAlignment = HorizontalAlignment.Center;
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(title);
        }
        if (view is null)
        {
            var note = Theme.Label("Pick a date: double-click in Edit layout, or right-click → Customize.", 12, Theme.Muted);
            note.TextWrapping = TextWrapping.Wrap;
            note.TextAlignment = TextAlignment.Center;
            stack.Children.Add(note);
            card.Child = stack;
            return card;
        }
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        double size = Math.Clamp(Math.Min(h * 0.36, (w - 40) / Math.Max(2.5, view.Number.Length * 0.62 + view.Unit.Length * 0.25)), 20, 96);
        var number = Theme.Label(view.Number, size, view.Past ? Theme.Muted : accent);
        number.FontWeight = FontWeights.SemiBold;
        System.Windows.Documents.Typography.SetNumeralAlignment(number, FontNumeralAlignment.Tabular);
        line.Children.Add(number);
        if (view.Unit.Length > 0)
        {
            var unit = Theme.Label(view.Unit, Math.Clamp(size * 0.3, 11, 22), Theme.Text2);
            unit.VerticalAlignment = VerticalAlignment.Bottom;
            unit.Margin = new Thickness(8, 0, 0, size * 0.16);
            line.Children.Add(unit);
        }
        stack.Children.Add(line);
        if (h >= 120)
        {
            var caption = Theme.Label(view.Caption + (item.RepeatYearly ? " · every year" : ""), 11.5, Theme.Muted);
            caption.HorizontalAlignment = HorizontalAlignment.Center;
            caption.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(caption);
        }
        card.Child = stack;
        card.ToolTip = view.Caption;
        tickers[KeyFor(item)] = (() => false, _ => RebuildContent(item));
        return card;
    }

    private void AddCountdown()
    {
        var draft = new LauncherItem { Kind = ItemKinds.Countdown, Name = "Countdown", Accent = "#ec4899", TargetDate = Planner.Key(Planner.Today().AddDays(30)) };
        if (pendingLink is not null) { AddNew(draft); return; }
        var editor = new ItemEditor(DialogOwner, draft, true);
        if (Modal(editor.ShowDialog) == true && editor.Result is { } result) AddNew(result);
    }

    // ---------- Media player ----------

    private void OnMediaChanged()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!IsLoaded || closingSurface) return;
            foreach (var item in MyItems.Where(i => i.Kind == ItemKinds.Media).ToList()) RebuildContent(item);
        }));
    }

    private FrameworkElement BuildMedia(LauncherItem item, double w, double h)
    {
        _ = MediaService.Instance.EnsureStartedAsync();
        var media = MediaService.Instance.Current;
        var accent = Theme.Accent(item.Accent);
        var card = Card(item);
        var grid = new Grid { Margin = new Thickness(12) };
        double artSize = Math.Clamp(h - 24, 40, Math.Min(160, w * 0.38));
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(artSize + 14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var art = new Border { Width = artSize, Height = artSize, CornerRadius = new CornerRadius(10), Background = Theme.Tint(item.Accent, 0.16), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        if (media.Art is { } bytes && Picture(bytes, artSize) is { } picture)
        {
            var brush = new ImageBrush(picture) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            art.Background = brush;
        }
        else art.Child = Theme.Icon(Theme.Glyphs.Music, artSize * 0.36, accent);
        grid.Children.Add(art);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);
        if (!media.HasSession)
        {
            info.Children.Add(Theme.Label(MediaService.Instance.Problem is null ? "Nothing playing" : "Media unavailable", 14, Theme.Text2));
            var hint = Theme.Label(MediaService.Instance.Problem ?? "Play something in Spotify, a browser or any media app.", 11.5, Theme.Muted);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(0, 3, 0, 0);
            info.Children.Add(hint);
            card.Child = grid;
            return card;
        }
        var title = Theme.Label(string.IsNullOrWhiteSpace(media.Title) ? "Unknown title" : media.Title, Math.Clamp(h * 0.11, 13, 18), Theme.Text);
        title.FontWeight = FontWeights.SemiBold;
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        title.ToolTip = media.Title;
        info.Children.Add(title);
        var artist = Theme.Label(string.Join(" · ", new[] { media.Artist, media.App }.Where(s => !string.IsNullOrWhiteSpace(s))), 12, Theme.Muted);
        artist.TextTrimming = TextTrimming.CharacterEllipsis;
        artist.Margin = new Thickness(0, 2, 0, 0);
        info.Children.Add(artist);

        Shapes.Rectangle? fill = null;
        TextBlock? elapsed = null;
        Grid? bar = null;
        if (media.Duration > TimeSpan.Zero && h >= 120)
        {
            bar = new Grid { Height = 4, Margin = new Thickness(0, 10, 0, 0) };
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = Theme.Surface3 });
            fill = new Shapes.Rectangle { RadiusX = 2, RadiusY = 2, Fill = accent, HorizontalAlignment = HorizontalAlignment.Left };
            bar.Children.Add(fill);
            info.Children.Add(bar);
            elapsed = Theme.Label("", 10.5, Theme.Muted);
            elapsed.Margin = new Thickness(0, 4, 0, 0);
            info.Children.Add(elapsed);
        }
        void ShowPosition(DateTimeOffset now)
        {
            if (fill is null || bar is null || elapsed is null) return;
            var position = media.PositionNow(now);
            double fraction = media.Duration.TotalSeconds > 0 ? Math.Clamp(position.TotalSeconds / media.Duration.TotalSeconds, 0, 1) : 0;
            fill.Width = Math.Max(0, (bar.ActualWidth > 0 ? bar.ActualWidth : Math.Max(40, w - artSize - 40)) * fraction);
            elapsed.Text = $"{Clock(position)} / {Clock(media.Duration)}";
            static string Clock(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture) : t.ToString(@"m\:ss", CultureInfo.InvariantCulture);
        }
        ShowPosition(DateTimeOffset.Now);
        if (bar is not null) bar.SizeChanged += (_, _) => ShowPosition(DateTimeOffset.Now);

        double button = Math.Clamp(h * 0.2, 26, 34);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-4, h >= 120 ? 6 : 8, 0, 0) };
        var previous = Theme.IconButton(Theme.Glyphs.Previous, "Previous", button);
        previous.Background = Brushes.Transparent;
        previous.Click += (_, _) => _ = MediaService.Instance.PreviousAsync();
        var play = new Border
        {
            Width = button + 6, Height = button + 6, CornerRadius = new CornerRadius((button + 6) / 2), Background = accent, Cursor = Cursors.Hand, Margin = new Thickness(6, 0, 6, 0),
            Child = Theme.Icon(media.Playing ? Theme.Glyphs.Pause : Theme.Glyphs.Play, button * 0.4, Theme.OnPrimary), ToolTip = media.Playing ? "Pause" : "Play",
        };
        play.MouseLeftButtonUp += (_, e) => { e.Handled = true; _ = MediaService.Instance.TogglePlayPauseAsync(); };
        var next = Theme.IconButton(Theme.Glyphs.NextTrack, "Next", button);
        next.Background = Brushes.Transparent;
        next.Click += (_, _) => _ = MediaService.Instance.NextAsync();
        controls.Children.Add(previous);
        controls.Children.Add(play);
        controls.Children.Add(next);
        info.Children.Add(controls);
        card.Child = grid;
        if (fill is not null) tickers[KeyFor(item)] = (() => media.Playing, ShowPosition);
        return card;
    }

    private static BitmapImage? Picture(byte[] bytes, double size)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = (int)Math.Clamp(size * 2, 64, 512);
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException or ArgumentException) { return null; }
    }

    // ---------- Folder / recent files ----------

    private FrameworkElement BuildFolder(LauncherItem item, double w, double h)
    {
        string folder = string.IsNullOrWhiteSpace(item.Target) ? FolderListing.Downloads : item.Target;
        var open = Theme.IconButton(Theme.Glyphs.OpenFolder, "Open the folder", 24);
        open.Click += (_, _) => OpenPath(folder);
        var (card, body) = Frame(item, null, open);
        var list = new StackPanel();
        int rows = Math.Max(1, (int)((h - (item.ShowHeader ? 58 : 30)) / 34));
        var entries = FolderListing.Recent(folder, rows);
        var now = DateTime.Now;
        foreach (var entry in entries)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Image { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
            var glyph = Theme.Icon(entry.IsFolder ? Theme.Glyphs.Folder : "", 14, Theme.Accent(item.Accent));
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            grid.Children.Add(glyph);
            grid.Children.Add(icon);
            string path = entry.Path;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (ImageCache.ShellIcon(path, 32) is { } source) { icon.Source = source; glyph.Visibility = Visibility.Collapsed; }
            }));
            var name = Theme.Label(entry.Name, 12.5, Theme.Text);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
            var age = Theme.Label(Ago(entry.Modified, now), 11, Theme.Muted);
            age.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(age, 2);
            grid.Children.Add(age);
            var row = Row(grid);
            row.Cursor = Cursors.Hand;
            row.ToolTip = entry.Path;
            row.MouseLeftButtonUp += (_, e) => { e.Handled = true; OpenPath(path); };
            var menu = Menu();
            AddMenuItem(menu, "Open", () => OpenPath(path), Theme.Glyphs.Open);
            AddMenuItem(menu, "Show in folder", () => ShowInFolder(path), Theme.Glyphs.OpenFolder);
            AddMenuItem(menu, "Copy path", () => { try { Clipboard.SetText(path); SetStatus("Path copied."); } catch (System.Runtime.InteropServices.COMException) { } });
            row.ContextMenu = menu;
            list.Children.Add(row);
        }
        if (entries.Count == 0) list.Children.Add(EmptyNote(Directory.Exists(folder) ? "This folder is empty." : "Folder not found. Right-click → Customize to choose one."));
        body.Children.Add(Scroll(list));
        card.ToolTip = folder;
        Watch(item, folder);
        // Ages ("5 min ago") move on even when nothing changes.
        tickers[KeyFor(item)] = (() => false, _ => RebuildContent(item));
        return card;
    }

    /// <summary>Refreshes a folder widget a moment after its folder changes.</summary>
    private void Watch(LauncherItem item, string folder)
    {
        if (watchers.TryGetValue(item.Id, out var existing))
        {
            if (string.Equals(existing.Path, folder, StringComparison.OrdinalIgnoreCase)) return;
            existing.Watcher.Dispose();
            existing.Settle.Stop();
            watchers.Remove(item.Id);
        }
        if (!Directory.Exists(folder)) return;
        try
        {
            var settle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            settle.Tick += (_, _) => { settle.Stop(); if (IsLoaded && !closingSurface) RebuildContent(item); };
            var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = false, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
            void Poke(object? _, FileSystemEventArgs __) => Dispatcher.BeginInvoke(new Action(() => { settle.Stop(); settle.Start(); }));
            watcher.Created += Poke; watcher.Deleted += Poke; watcher.Changed += Poke;
            watcher.Renamed += (s, e) => Poke(s, e);
            watcher.EnableRaisingEvents = true;
            watchers[item.Id] = (folder, watcher, settle);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { }
    }

    /// <summary>Stops watching folders whose widgets are gone from this surface.</summary>
    private void PruneWatchers()
    {
        foreach (var id in watchers.Keys.Where(id => (!rects.ContainsKey(id) && !hub.Floats.Has(id)) || settings.Items.All(i => i.Id != id || i.Kind != ItemKinds.Folder)).ToList())
        {
            watchers[id].Watcher.Dispose();
            watchers[id].Settle.Stop();
            watchers.Remove(id);
        }
    }

    private void DisposeWatchers()
    {
        foreach (var (_, watcher, settle) in watchers.Values) { watcher.Dispose(); settle.Stop(); }
        watchers.Clear();
    }

    internal void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException) { ShowError("Could not open it", ex); }
    }

    private static void ShowInFolder(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    // ---------- Weather ----------

    private FrameworkElement BuildWeather(LauncherItem item, double w, double h)
    {
        var refresh = Theme.IconButton(Theme.Glyphs.Refresh, "Refresh now", 24);
        refresh.Click += (_, _) => _ = RefreshWeather(item, force: true);
        var (card, body) = Frame(item, null, refresh);
        bool placed = !string.IsNullOrWhiteSpace(item.Location) && (item.Latitude != 0 || item.Longitude != 0);
        if (!placed)
        {
            var note = EmptyNote("Choose your city: right-click → Customize.");
            note.Cursor = Cursors.Hand;
            note.MouseLeftButtonUp += (_, e) => { e.Handled = true; EditItem(item); };
            body.Children.Add(note);
            return card;
        }
        WeatherService.Report? report = null;
        if (item.WeatherJson.Length > 0) { try { report = WeatherService.Parse(item.WeatherJson); } catch (FormatException) { } }
        bool stale = item.WeatherFetched is not { } fetched || DateTimeOffset.Now - fetched > TimeSpan.FromMinutes(30);
        if (stale) Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = RefreshWeather(item, force: false)));
        tickers[KeyFor(item)] = (() => false, now => { if (item.WeatherFetched is not { } f || DateTimeOffset.Now - f > TimeSpan.FromMinutes(30)) _ = RefreshWeather(item, force: false); });
        if (report is null)
        {
            body.Children.Add(EmptyNote(WeatherProblems.GetValueOrDefault(item.Id) ?? "Getting the weather…"));
            return card;
        }
        string unit = item.Units == "C" ? "C" : "F";
        string speed = unit == "C" ? "km/h" : "mph";
        var (condition, iconKind) = WeatherService.Describe(report.Code);
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition());
        double iconSize = Math.Clamp(Math.Min(h * 0.32, w * 0.24), 34, 96);
        var icon = WeatherIcon(iconKind, report.IsDay, iconSize);
        icon.Margin = new Thickness(0, 0, 14, 0);
        top.Children.Add(icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var temperature = Theme.Label($"{Math.Round(report.Temperature)}°", Math.Clamp(iconSize * 0.62, 24, 64), Theme.Text);
        temperature.FontWeight = FontWeights.SemiBold;
        text.Children.Add(temperature);
        var conditionLabel = Theme.Label(condition, 12.5, Theme.Text2);
        conditionLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(conditionLabel);
        Grid.SetColumn(text, 1);
        top.Children.Add(text);
        var stack = new StackPanel();
        stack.Children.Add(top);
        var today = report.Days.FirstOrDefault();
        var details = new List<string>();
        if (double.IsFinite(report.FeelsLike)) details.Add($"Feels {Math.Round(report.FeelsLike)}°");
        if (today is not null && double.IsFinite(today.High)) details.Add($"H {Math.Round(today.High)}°  L {Math.Round(today.Low)}°");
        if (double.IsFinite(report.Wind) && w >= 300) details.Add($"Wind {Math.Round(report.Wind)} {speed}");
        if (report.Humidity >= 0 && w >= 360) details.Add($"Humidity {report.Humidity}%");
        if (h >= 150)
        {
            var line = Theme.Label(string.Join("  ·  ", details), 11.5, Theme.Muted);
            line.Margin = new Thickness(0, 8, 0, 0);
            line.TextTrimming = TextTrimming.CharacterEllipsis;
            stack.Children.Add(line);
        }
        int days = Math.Min(report.Days.Count, (int)((w - 28) / 52));
        if (h >= 215 && days >= 3)
        {
            var strip = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = days, Margin = new Thickness(0, 14, 0, 0) };
            foreach (var day in report.Days.Take(days))
            {
                var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var name = Theme.Label(day.Date == DateOnly.FromDateTime(DateTime.Today) ? "Today" : day.Date.ToString("ddd", CultureInfo.CurrentCulture), 10.5, Theme.Muted);
                name.HorizontalAlignment = HorizontalAlignment.Center;
                cell.Children.Add(name);
                var small = WeatherIcon(WeatherService.Describe(day.Code).Icon, true, 22);
                small.Margin = new Thickness(0, 4, 0, 4);
                cell.Children.Add(small);
                var range = Theme.Label($"{Math.Round(day.High)}° {Math.Round(day.Low)}°", 10.5, Theme.Text2);
                range.HorizontalAlignment = HorizontalAlignment.Center;
                cell.Children.Add(range);
                cell.ToolTip = $"{day.Date:dddd}: {WeatherService.Describe(day.Code).Text}" + (day.RainChance is { } rain ? $", {rain}% chance of rain" : "");
                strip.Children.Add(cell);
            }
            stack.Children.Add(strip);
        }
        body.Children.Add(stack);
        string age = item.WeatherFetched is { } at ? at.LocalDateTime.ToString("h:mm tt", CultureInfo.CurrentCulture) : "";
        card.ToolTip = $"{item.Location}\nUpdated {age}" + (WeatherProblems.TryGetValue(item.Id, out var problem) ? "\n" + problem : "") + "\nWeather data: Open-Meteo.com";
        return card;
    }

    private async Task RefreshWeather(LauncherItem item, bool force)
    {
        if (!force && item.WeatherFetched is { } fetched && DateTimeOffset.Now - fetched < TimeSpan.FromMinutes(30)) return;
        // After a failure, wait five minutes before trying again on its own.
        if (!force && WeatherAttempts.TryGetValue(item.Id, out var attempted) && DateTimeOffset.Now - attempted < TimeSpan.FromMinutes(5)) return;
        if (item.Latitude == 0 && item.Longitude == 0) return;
        string key = "weather:" + item.Id;
        if (!Fetching.Add(key)) return;
        WeatherAttempts[item.Id] = DateTimeOffset.Now;
        try
        {
            string json = await WeatherService.FetchAsync(item.Latitude, item.Longitude, item.Units == "C" ? "C" : "F");
            WeatherService.Parse(json);
            item.WeatherJson = json;
            item.WeatherFetched = DateTimeOffset.Now;
            WeatherProblems.Remove(item.Id);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or FormatException)
        {
            // Keep the last forecast on screen.
            WeatherProblems[item.Id] = ex is FormatException ? "The weather service answered unexpectedly." : item.WeatherJson.Length > 0 ? "Offline — showing the last forecast." : "Couldn't reach the weather service. Check your connection.";
        }
        finally { Fetching.Remove(key); }
        if (IsLoaded && !closingSurface) Changed(item);
    }

    /// <summary>Simple drawn weather icons (clear, partly, cloudy, fog, rain, snow, storm) on a 24×24 grid.</summary>
    private static FrameworkElement WeatherIcon(string kind, bool day, double size)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var sunColor = Theme.Brush("#F2B33D");
        var cloudColor = Theme.Brush("#D9CDB8");
        var rainColor = Theme.Brush("#5CC8F0");
        void Add(string data, Brush? fill, Brush? stroke = null, double thickness = 0)
        {
            canvas.Children.Add(new Shapes.Path { Data = Geometry.Parse(data), Fill = fill, Stroke = stroke, StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        void Sun(double cx, double cy, double r)
        {
            canvas.Children.Add(new Shapes.Ellipse { Width = r * 2, Height = r * 2, Fill = sunColor, Margin = new Thickness(cx - r, cy - r, 0, 0) });
            for (int i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4, x1 = cx + Math.Cos(a) * (r + 1.6), y1 = cy + Math.Sin(a) * (r + 1.6), x2 = cx + Math.Cos(a) * (r + 3.4), y2 = cy + Math.Sin(a) * (r + 3.4);
                Add(FormattableString.Invariant($"M{x1:0.##},{y1:0.##} L{x2:0.##},{y2:0.##}"), null, sunColor, 1.6);
            }
        }
        void Moon(double dx, double dy) => Add(FormattableString.Invariant($"M{14 + dx},{4 + dy} A7,7 0 1 0 {20 + dx},{14 + dy} A5.5,5.5 0 0 1 {14 + dx},{4 + dy} Z"), Theme.Brush("#E6DCC8"));
        const string cloud = "M7,19 C4.2,19 2.5,17.2 2.5,15 C2.5,12.9 4.1,11.2 6.2,11 C6.9,8.1 9.3,6 12.2,6 C15.6,6 18.3,8.6 18.5,11.9 C20.5,12.2 22,13.9 22,15.9 C22,17.7 20.6,19 18.8,19 Z";
        const string lowCloud = "M7,16 C4.2,16 2.5,14.2 2.5,12 C2.5,9.9 4.1,8.2 6.2,8 C6.9,5.1 9.3,3 12.2,3 C15.6,3 18.3,5.6 18.5,8.9 C20.5,9.2 22,10.9 22,12.9 C22,14.7 20.6,16 18.8,16 Z";
        switch (kind)
        {
            case "clear":
                if (day) Sun(12, 12, 4.6); else Moon(-3, 1);
                break;
            case "partly":
                if (day) Sun(8.5, 8.5, 3.6); else Moon(-6, -2);
                Add("M9,20 C6.8,20 5.5,18.6 5.5,16.9 C5.5,15.3 6.7,14 8.3,13.8 C8.9,11.6 10.8,10 13,10 C15.7,10 17.8,12 18,14.6 C19.6,14.8 21,16.1 21,17.7 C21,19.1 19.9,20 18.5,20 Z", cloudColor);
                break;
            case "fog":
                foreach (var y in new[] { 9, 13, 17 }) Add($"M4,{y} L20,{y}", null, cloudColor, 2);
                break;
            case "rain":
                Add(lowCloud, cloudColor);
                foreach (var x in new[] { 8, 12.5, 17 }) Add(FormattableString.Invariant($"M{x},18 L{x - 1.5},22"), null, rainColor, 1.8);
                break;
            case "snow":
                Add(lowCloud, cloudColor);
                foreach (var (x, y) in new[] { (8.0, 19.5), (12.5, 21.5), (17.0, 19.5) }) canvas.Children.Add(new Shapes.Ellipse { Width = 2.6, Height = 2.6, Fill = Brushes.White, Margin = new Thickness(x - 1.3, y - 1.3, 0, 0) });
                break;
            case "storm":
                Add(lowCloud, cloudColor);
                Add("M12.5,15 L9.5,19.5 L12,19.5 L10.5,23.5 L15,18 L12.5,18 L14,15 Z", sunColor);
                break;
            default:
                Add(cloud, cloudColor);
                break;
        }
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
    }

    private void AddWeather()
    {
        var draft = new LauncherItem { Kind = ItemKinds.Weather, Name = "Weather", Accent = "#f59e0b", Units = WeatherService.DefaultUnits };
        if (pendingLink is not null) { AddNew(draft); return; }
        var editor = new ItemEditor(DialogOwner, draft, true);
        if (Modal(editor.ShowDialog) == true && editor.Result is { } result) AddNew(result);
    }

    /// <summary>"just now", "5 min ago", "3 h ago", "yesterday", "Oct 2".</summary>
    private static string Ago(DateTime when, DateTime now)
    {
        var span = now - when;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24 && when.Date == now.Date) return $"{(int)span.TotalHours} h ago";
        if (when.Date == now.Date.AddDays(-1)) return "yesterday";
        return when.ToString("MMM d", System.Globalization.CultureInfo.CurrentCulture);
    }
}
