using DesktopDeck.Models;
using DesktopDeck.Services;

namespace DesktopDeck;

/// <summary>
/// Smoke test (--smoke-test --sample): replaces the isolated preview settings with one of every widget, filled
/// with made-up data. Nothing here touches a real account or login.
/// </summary>
internal static class SampleDeck
{
    public static void Write(SettingsStore store)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "DesktopDeck-sample");
        string files = Path.Combine(scratch, "Downloads");
        Directory.CreateDirectory(files);
        foreach (var (name, minutesAgo) in new[] { ("Trip itinerary.pdf", 3), ("Budget 2026.xlsx", 50), ("photo_0412.jpg", 300), ("notes.txt", 2000) })
        {
            string path = Path.Combine(files, name);
            if (!File.Exists(path)) File.WriteAllText(path, "sample");
            File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-minutesAgo));
        }
        var today = Planner.Today();
        var settings = new DeckSettings
        {
            SnapToGrid = true,
            // A non-default look, so the smoke render proves backgrounds and card colors work: a gradient behind everything, and some custom cards.
            Appearance = new AppearanceSettings { Backgrounds = { [""] = new BackgroundStyle { Kind = "gradient", Color = "#1B1530", Color2 = "#5B2A3A", Angle = 135 } } },
            Items =
            [
                Launcher("Notepad", @"C:\Windows\System32\notepad.exe", 0.02, 0.08),
                Launcher("Calculator", @"C:\Windows\System32\calc.exe", 0.02, 0.17, new() { ["_cardColor"] = "#2B2450", ["_borderColor"] = "#8b5cf6" }),
                new LauncherItem { Kind = ItemKinds.Clock, Name = "Time", X = 0.15, Y = 0.08, ShowSeconds = false, Options = { ["_cardColor"] = "#18334A", ["_radius"] = "26", ["_textColor"] = "#CFE8FF" } },
                new LauncherItem { Kind = ItemKinds.Countdown, Name = "Trip to Japan", Accent = "#ec4899", X = 0.62, Y = 0.08, TargetDate = Planner.Key(today.AddDays(23)), Options = { ["_cardColor"] = "#6B1F44", ["_opacity"] = "72", ["_radius"] = "4" } },
                new LauncherItem { Kind = ItemKinds.Media, Name = "Now playing", Accent = "#22c55e", X = 0.15, Y = 0.3 },
                new LauncherItem
                {
                    Kind = ItemKinds.Weather, Name = "Weather", Accent = "#f59e0b", X = 0.15, Y = 0.5, Location = "Austin, Texas, United States", Latitude = 30.27, Longitude = -97.74, Units = "F",
                    WeatherJson = SampleWeather(today), WeatherFetched = DateTimeOffset.Now,
                },
                new LauncherItem { Kind = ItemKinds.Tasks, Name = "Today's Tasks", X = 0.82, Y = 0.08, DailyReset = true, LastDate = Planner.Key(today), GoogleTasksSync = true, GoogleTaskListName = "My Tasks",
                    Tasks =
                    [
                        new TaskEntry { Text = "Reply to email", GoogleId = "sample-1", GoogleListId = "@default", GoogleUpdated = DateTimeOffset.UtcNow },
                        new TaskEntry { Text = "Pick up the library book", GoogleId = "sample-2", GoogleListId = "@default", GoogleUpdated = DateTimeOffset.UtcNow },
                        new TaskEntry { Text = "Gym", Done = true, GoogleId = "sample-3", GoogleListId = "@default", GoogleUpdated = DateTimeOffset.UtcNow },
                        new TaskEntry { Text = "Buy a birthday card", Modified = DateTimeOffset.UtcNow },
                    ] },
                new LauncherItem
                {
                    Kind = ItemKinds.Schedule, Name = "This week", Accent = "#6c63ff", X = 0.42, Y = 0.36, Width = 540, Height = 468, StartHour = 8, EndHour = 21,
                    Blocks =
                    [
                        new ScheduleBlock { Title = "Math", Color = "#6c63ff", Days = ["Mon", "Wed", "Fri"], Start = "09:00", End = "10:00" },
                        new ScheduleBlock { Title = "Chemistry", Color = "#f59e0b", Days = ["Tue", "Thu"], Start = "10:00", End = "11:30" },
                        new ScheduleBlock { Title = "Study hall", Color = "#9B9A5E", Days = ["Mon", "Tue", "Wed", "Thu"], Start = "13:00", End = "14:00" },
                        new ScheduleBlock { Title = "Gym", Color = "#C4684F", Days = ["Mon", "Wed", "Fri"], Start = "17:00", End = "18:00" },
                    ],
                },
                new LauncherItem { Kind = "agenda", Name = "Agenda", Accent = "#4285f4", X = 0.8, Y = 0.6, Width = 276, Height = 276 },
                // A group card: three widgets as tabs of one card.
                new LauncherItem { Id = "sample-group", Kind = ItemKinds.Group, Name = "Daily bits", Accent = "#22c55e", X = 0.42, Y = 0.08, Width = 276, Height = 264, Options = { ["data.tab"] = "sample-habits" } },
                new LauncherItem { Id = "sample-counter", Kind = ItemKinds.Counter, Name = "Water", Accent = "#00d4ff", GroupId = "sample-group", Count = 3, CountGoal = 8 },
                new LauncherItem { Id = "sample-habits", Kind = ItemKinds.Habits, Name = "Habits", Accent = "#22c55e", GroupId = "sample-group", View = "today", Habits = [new HabitEntry { Name = "Read 20 minutes", Days = [Planner.Key(today.AddDays(-1)), Planner.Key(today.AddDays(-2))] }, new HabitEntry { Name = "Stretch", Days = [Planner.Key(today)] }] },
                new LauncherItem { Id = "sample-note", Kind = ItemKinds.Note, Name = "Note", Accent = "#E2B657", GroupId = "sample-group", Text = "Call the dentist\nPick up groceries: eggs, rice, coffee\nFinish the slides by Friday" },
                // A kept-on-top widget: floats in its own window, apart from the deck.
                new LauncherItem { Kind = ItemKinds.Timer, Name = "Focus timer", OnTop = true, FloatX = 0.64, FloatY = 0.5 },
            ],
            // Made-up Google account and calendar links (no secrets exist for them; the sample deck never contacts anyone).
            Reminders = new ReminderSettings { Items = ReminderSamples.Make(DateTimeOffset.Now, TimeZoneInfo.Local) },
            GoogleEmail = "alex.sample@example.com",
            GoogleTaskLists = [new GoogleTaskListChoice { Id = "@default", Title = "My Tasks" }, new GoogleTaskListChoice { Id = "sample-list", Title = "Groceries" }],
            GoogleCalendars =
            [
                new GoogleCalendarChoice { Id = "alex.sample@example.com", Name = "Personal", Color = "#4285f4", Primary = true, AccessRole = "owner" },
                new GoogleCalendarChoice { Id = "family@example.com", Name = "Family", Color = "#f59e0b", AccessRole = "writer" },
                new GoogleCalendarChoice { Id = "holidays@example.com", Name = "Holidays in United States", Color = "#0b8043", AccessRole = "reader", Visible = false },
            ],
            IcsCalendars =
            [
                new IcsCalendarInfo { Id = "sample-school", Label = "School", Color = "#14b8a6" },
                new IcsCalendarInfo { Id = "sample-soccer", Label = "Soccer league", Color = "#ec4899" },
            ],
        };
        // Fake link addresses so Settings shows them as saved (the sample deck never fetches them).
        var sampleSecrets = new SecretStore(Path.GetDirectoryName(store.FilePath)!);
        foreach (var link in settings.IcsCalendars) sampleSecrets.Set(link.Entry, "https://calendar.example.test/private/" + link.Id + "/basic.ics");
        if (File.Exists(store.FilePath)) File.Delete(store.FilePath);
        if (File.Exists(store.FilePath + ".bak")) File.Delete(store.FilePath + ".bak");
        AutomationSamples.Add(settings);
        store.Save(settings);
    }

    /// <summary>
    /// Made-up Google and iCal events around this week, injected into the calendar feed for snapshots only
    /// (nothing is ever fetched in a smoke test).
    /// </summary>
    public static List<CalendarEvent> Events(DateOnly today)
    {
        var monday = Planner.Monday(today).ToDateTime(TimeOnly.MinValue);
        DateTime At(int day, int hour, int minute = 0) => monday.AddDays(day).AddHours(hour).AddMinutes(minute);
        CalendarEvent G(string cal, string color, string title, DateTime from, DateTime to, string location = "", bool allDay = false) =>
            new() { Source = CalendarSource.Google, CalendarId = cal, CalendarLabel = cal, Color = color, Title = title, Start = from, End = to, Location = location, AllDay = allDay, Editable = true, GoogleId = title + from.Ticks };
        CalendarEvent I(string cal, string color, string title, DateTime from, DateTime to, string location = "", bool allDay = false) =>
            new() { Source = CalendarSource.Ics, CalendarId = cal, CalendarLabel = cal, Color = color, Title = title, Start = from, End = to, Location = location, AllDay = allDay, LocalBlockId = title };
        var now = DateTime.Now;
        return
        [
            G("Personal", "#4285f4", "Dentist", At(1, 9, 30), At(1, 10, 15), "Main Street Dental"),
            G("Personal", "#4285f4", "Call with Mom", At(0, 12), At(0, 12, 30)),
            G("Personal", "#4285f4", "Study group", At(2, 9, 30), At(2, 10, 45), "Library"),
            G("Family", "#f59e0b", "Piano lesson", At(4, 15), At(4, 16)),
            G("Family", "#f59e0b", "Grandma's birthday", At(3, 0), At(4, 0), "", true),
            G("Personal", "#4285f4", "Study session: chemistry", now.AddMinutes(-20), now.AddMinutes(40), "Library, room 2"),
            I("School", "#14b8a6", "Chemistry lab", At(0, 14, 30), At(0, 16), "Science building"),
            I("School", "#14b8a6", "Essay due", At(2, 0), At(3, 0), "", true),
            I("School", "#14b8a6", "Field trip", At(3, 0), At(5, 0), "", true),
            I("Soccer league", "#ec4899", "Match vs Eagles", At(3, 16), At(3, 17, 30), "North field"),
            G("Personal", "#4285f4", "Dinner with Alex", today.AddDays(1).ToDateTime(new TimeOnly(18, 30)), today.AddDays(1).ToDateTime(new TimeOnly(20, 0)), "Luigi's"),
        ];
    }

    /// <summary>A made-up track with generated cover art for the Media widget in snapshots (the PC's real media session is never read in a smoke test).</summary>
    public static MediaService.Snapshot Track()
    {
        var visual = new System.Windows.Media.DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var sky = new System.Windows.Media.LinearGradientBrush(System.Windows.Media.Color.FromRgb(0xF9, 0x73, 0x16), System.Windows.Media.Color.FromRgb(0x7C, 0x3A, 0xED), 90);
            dc.DrawRectangle(sky, null, new System.Windows.Rect(0, 0, 240, 240));
            dc.DrawEllipse(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFD, 0xE6, 0x8A)), null, new System.Windows.Point(120, 140), 62, 62);
            dc.DrawRectangle(new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x1B, 0x4B)), null, new System.Windows.Rect(0, 160, 240, 80));
        }
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(240, 240, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return new MediaService.Snapshot(true, "Golden Hour Drive", "Sample Band", "Music", true, TimeSpan.FromSeconds(84), TimeSpan.FromSeconds(230), DateTimeOffset.Now, stream.ToArray());
    }

    private static LauncherItem Launcher(string name, string target, double x, double y, Dictionary<string, string>? options = null)
        => new() { Name = name, Target = target, WorkingDirectory = Path.GetDirectoryName(target) ?? "", X = x, Y = y, Options = options ?? new(StringComparer.Ordinal) };

    private static string SampleWeather(DateOnly today)
    {
        var days = Enumerable.Range(0, 7).Select(i => today.AddDays(i)).ToList();
        string dates = string.Join(",", days.Select(d => $"\"{d:yyyy-MM-dd}\""));
        return "{\"current\":{\"temperature_2m\":78.4,\"apparent_temperature\":80.1,\"relative_humidity_2m\":48,\"weather_code\":2,\"wind_speed_10m\":9.2,\"is_day\":1}," +
               "\"daily\":{\"time\":[" + dates + "],\"weather_code\":[2,0,3,61,95,1,71],\"temperature_2m_max\":[84,86,80,74,72,79,60],\"temperature_2m_min\":[66,67,65,62,60,63,41],\"precipitation_probability_max\":[10,0,20,70,80,5,40]}}";
    }
}
