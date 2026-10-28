using DesktopDeck.Models;
using DesktopDeck.Services;

string folder = Path.Combine(Path.GetTempPath(), "DesktopDeck-ServiceTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    var store = new SettingsStore(folder);
    Check(store.Load().Items.Count == 0, "Missing settings use defaults");
    var original = new DeckSettings { Items = [new LauncherItem { Name = "First", Target = @"C:\sample.cmd", X = 0.2, Y = 0.5 }] };
    store.Save(original);
    Check(store.Load().Items[0].Name == "First", "Settings round-trip");
    original.Items[0].Name = "Second";
    store.Save(original);
    File.WriteAllText(store.FilePath, "{bad json");
    Check(store.Load().Items[0].Name == "First" && store.LastLoadWarning is not null, "Corrupt primary recovers backup with warning");
    store.Save(original);
    Check(store.Load().Items[0].Name == "Second", "Recovered settings can be saved");
    Check(Directory.EnumerateFiles(folder, "*.corrupt-*").Any(), "Corrupt primary preserved separately");
    Check(File.ReadAllText(store.FilePath + ".bak").Contains("First"), "Recovery save preserves good backup");
    File.WriteAllText(store.FilePath, "bad");
    File.WriteAllText(store.FilePath + ".bak", "also bad");
    try { store.Load(); throw new Exception("Expected corrupt-file failure"); }
    catch (InvalidDataException) { Console.WriteLine("PASS: Both corrupt files fail visibly"); }

    // Version 1 files have no Kind, size, or tracker fields; they must load as launchers.
    string legacyFolder = Path.Combine(folder, "legacy");
    Directory.CreateDirectory(legacyFolder);
    var legacy = new SettingsStore(legacyFolder);
    File.WriteAllText(legacy.FilePath, """{ "Items": [ { "Id": "a1", "Name": "Old", "Target": "C:\\old.bat", "Arguments": "", "WorkingDirectory": "", "Accent": "#A7DBC8", "X": 0.04, "Y": 0.09 } ], "StartAtLogin": true }""");
    var upgraded = legacy.Load();
    Check(upgraded.Items[0].Kind == ItemKinds.Launcher && upgraded.Items[0].Width == 0 && upgraded.Items[0].Tasks.Count == 0 && upgraded.Items[0].Step == 1, "Version 1 settings load as launchers with defaults");
    Check(upgraded.SnapToGrid && !upgraded.KeepCenterClear && upgraded.ShowTitle, "Version 1 settings get new layout defaults");
    File.WriteAllText(legacy.FilePath, """{ "Items": [ { "Id": "b2", "Kind": "TASKS", "Name": "T", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0, "Tasks": null, "Habits": [ { "Name": "Run", "Days": null } ], "IconPath": null, "Width": -5, "Step": 0 } ] }""");
    var odd = legacy.Load().Items[0];
    Check(odd.Kind == ItemKinds.Tasks && odd.Tasks.Count == 0 && odd.Habits[0].Days.Count == 0 && odd.IconPath == "" && odd.Width == 0 && odd.Step == 1, "Null and out-of-range new fields are repaired on load");

    var widgets = new DeckSettings
    {
        SnapToGrid = false, KeepCenterClear = true,
        Items =
        [
            new LauncherItem { Kind = ItemKinds.Tasks, Name = "Today", Width = 276, Height = 300, Locked = true, Tasks = [new TaskEntry { Text = "Ship", Done = true }] },
            new LauncherItem { Kind = ItemKinds.Habits, Name = "Habits", Habits = [new HabitEntry { Name = "Read", Days = ["2026-10-03"] }] },
            new LauncherItem { Kind = ItemKinds.Counter, Name = "Cups", Count = 3, Step = 2 },
            new LauncherItem { Kind = ItemKinds.Image, ImagePath = @"C:\media\a.png", ImageFit = "fit" },
        ]
    };
    store.Save(widgets);
    var loaded = store.Load();
    Check(!loaded.SnapToGrid && loaded.KeepCenterClear && loaded.Items.Count == 4, "Layout settings round-trip");
    Check(loaded.Items[0].Tasks[0].Done && loaded.Items[0].Locked && loaded.Items[0].Height == 300, "Task lists, locks and sizes round-trip");
    Check(loaded.Items[1].Habits[0].Days.SequenceEqual(["2026-10-03"]) && loaded.Items[2].Count == 3 && loaded.Items[2].Step == 2, "Habits and counters round-trip");
    Check(loaded.Items[3].Kind == ItemKinds.Image && loaded.Items[3].ImageFit == "fit", "Pictures round-trip");
    var copy = loaded.Items[0].Copy();
    copy.Tasks[0].Text = "Changed";
    Check(loaded.Items[0].Tasks[0].Text == "Ship", "Copy is deep for tracker lists");

    // Plug-in configuration and content must survive a restart and cancelled Customize drafts.
    var pluginStore = new SettingsStore(Path.Combine(folder, "plugins"));
    var pluginItem = new LauncherItem
    {
        Kind = "kanban", Name = "Work", Display = "fixture-monitor", OnTop = true,
        Options = new()
        {
            ["columns"] = "To do\nDoing\nDone", ["_textScale"] = "150",
            ["data.cards"] = "[{\"title\":\"Keep my notes\"}]", ["cache.json"] = "{\"value\":42}",
        },
    };
    pluginStore.Save(new DeckSettings { Items = [pluginItem] });
    var pluginBack = pluginStore.Load().Items.Single();
    Check(pluginBack.Kind == "kanban" && pluginBack.Options.OrderBy(p => p.Key).SequenceEqual(pluginItem.Options.OrderBy(p => p.Key)), "Plug-in settings, content and caches survive restart");
    Check(pluginBack.OnTop && pluginBack.Display == "fixture-monitor", "Plug-ins retain monitor and Keep on top settings");
    var pluginDraft = pluginBack.Copy();
    pluginDraft.Options["data.cards"] = "[]";
    pluginDraft.Options["_textScale"] = "60";
    Check(pluginBack.Options["data.cards"] == pluginItem.Options["data.cards"] && pluginBack.Options["_textScale"] == "150", "Cancelled plug-in drafts cannot mutate saved data or appearance");

    // ---------- v0.5: displays, keep on top, new widgets ----------
    string v4Folder = Path.Combine(folder, "v4");
    Directory.CreateDirectory(v4Folder);
    var v4 = new SettingsStore(v4Folder);
    File.WriteAllText(v4.FilePath, """{ "SchemaVersion": 3, "Items": [ { "Id": "p1", "Kind": "clock", "Name": "Clock", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#C4684F", "X": 0.1, "Y": 0.1, "PopOut": { "X": 10, "Y": 10, "Width": 300, "Height": 200, "OnTop": true } }, { "Id": "p2", "Kind": "counter", "Name": "Count", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#C4684F", "X": 0.2, "Y": 0.2, "PopOut": { "X": 10, "Y": 10, "Width": 300, "Height": 200, "OnTop": false } } ] }""");
    var popMigrated = v4.Load();
    Check(popMigrated.Items[0].OnTop && popMigrated.Items[0].PopOut is null && !popMigrated.Items[1].OnTop && popMigrated.Items[1].PopOut is null, "v0.4 pop-outs become Keep on top (only when they were on top)");
    Check(popMigrated.Items.All(i => i.Display == "" && i.Text == "") && popMigrated.SchemaVersion == DeckSettings.CurrentSchema, "Older settings get empty display and note");

    var v5 = new DeckSettings
    {
        Items =
        [
            new LauncherItem { Kind = ItemKinds.Note, Text = "Line one\nLine two", Display = @"\\?\DISPLAY#X#1", OnTop = true },
            new LauncherItem { Kind = ItemKinds.Countdown, Name = "Trip", TargetDate = "2026-12-25", TargetTime = "18:30", RepeatYearly = true },
            new LauncherItem { Kind = ItemKinds.Weather, Location = "Austin, Texas", Latitude = 30.27, Longitude = -97.74, Units = "C", WeatherJson = "{}", WeatherFetched = DateTimeOffset.Parse("2026-10-04T12:00:00Z") },
            new LauncherItem { Kind = ItemKinds.Folder, Target = @"C:\Downloads" },
        ],
    };
    store.Save(v5);
    var back5 = store.Load();
    Check(back5.Items[0].Text == "Line one\nLine two" && back5.Items[0].Display == @"\\?\DISPLAY#X#1" && back5.Items[0].OnTop, "Notes, displays and Keep on top round-trip");
    var layers = new DeckSettings
    {
        FloatsHidden = true,
        Items =
        [
            new LauncherItem { Kind = ItemKinds.Timer, Name = "Focus", OnTop = true, Hidden = true, FloatDisplay = "mon-2", FloatX = 0.25, FloatY = 0.75, FloatWidth = 300.5, FloatHeight = 330 },
            new LauncherItem { Kind = ItemKinds.Counter, Name = "Bad", OnTop = true, FloatX = double.NaN, FloatY = 7, FloatWidth = -4, FloatHeight = double.PositiveInfinity },
            new LauncherItem { Kind = ItemKinds.Clock, Name = "Neg", FloatDisplay = null!, FloatX = -2, FloatY = 0.5 },
        ],
    };
    store.Save(layers);
    var backLayers = store.Load();
    Check(backLayers.Items[0].Hidden && backLayers.Items[0].OnTop && backLayers.Items[0].FloatDisplay == "mon-2" && backLayers.Items[0].FloatX == 0.25 && backLayers.Items[0].FloatY == 0.75 && backLayers.Items[0].FloatWidth == 300.5 && backLayers.Items[0].FloatHeight == 330, "Hidden and floating-layer position and size round-trip");
    Check(!backLayers.Items[1].Hidden && backLayers.Items[1].FloatX == 0 && backLayers.Items[1].FloatY == 1 && backLayers.Items[1].FloatWidth == 0 && backLayers.Items[1].FloatHeight == 0, "Bad floating-layer numbers are repaired (finite, 0 to 1, no negative sizes)");
    Check(backLayers.Items[2].FloatDisplay == "" && backLayers.Items[2].FloatX == 0 && backLayers.Items[2].FloatY == 0.5, "Missing float display and negative float position are repaired");
    Check(backLayers.FloatsHidden, "Hide-floats switch round-trips");
    store.Save(new DeckSettings());
    var oldLayers = store.Load();
    Check(!oldLayers.FloatsHidden, "Older files get the Hide-floats default");
    Check(layers.Items[0].Copy() is { Hidden: true, FloatX: 0.25, FloatDisplay: "mon-2" }, "Copy keeps the floating-layer fields");
    Check(back5.Items[1].TargetDate == "2026-12-25" && back5.Items[1].TargetTime == "18:30" && back5.Items[1].RepeatYearly, "Countdowns round-trip");
    Check(back5.Items[2].Latitude == 30.27 && back5.Items[2].Units == "C" && back5.Items[2].WeatherFetched is not null, "Weather round-trips");
    Check(back5.Items[3].Target == @"C:\Downloads", "Folders round-trip");
    File.WriteAllText(legacy.FilePath, """{ "Items": [ { "Id": "c3", "Kind": "weather", "Name": "W", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0, "Units": "K", "Latitude": 500, "Accounts": [ null, { "Provider": "gemini" } ], "Display": null } ] }""");
    var repairedWeather = legacy.Load().Items[0];
    Check(repairedWeather.Units == "" && repairedWeather.Latitude == 0 && repairedWeather.Display == "", "Bad weather values are repaired on load (and a leftover Accounts field is ignored)");

    // Countdown
    var noon = new DateTime(2026, 10, 4, 12, 0, 0);
    Check(Countdown.Describe(noon, "2026-10-27", "", false) is { Number: "23", Unit: "days to go", Past: false }, "Countdown counts days");
    Check(Countdown.Describe(noon, "2026-10-05", "", false) is { Number: "1", Unit: "day to go" }, "Countdown says 1 day");
    Check(Countdown.Describe(noon, "2026-10-04", "18:30", false) is { Number: "6h 30m", Unit: "to go" }, "Countdown shows hours on the last day");
    Check(Countdown.Describe(noon, "2026-10-04", "12:20", false) is { Number: "20", Unit: "minutes to go" }, "Countdown shows minutes in the last hour");
    Check(Countdown.Describe(noon, "2026-10-04", "", false) is { Number: "Today!" } && Countdown.Describe(noon, "2026-10-04", "9:00", false) is { Number: "Today!" }, "Countdown says Today! on the day");
    Check(Countdown.Describe(noon, "2026-09-30", "", false) is { Number: "4", Unit: "days ago", Past: true }, "Countdown counts days since");
    Check(Countdown.Describe(noon, "2001-10-10", "", true) is { Number: "6", Unit: "days to go" } view && view.Target.Year == 2026, "Yearly countdown aims at this year's date");
    Check(Countdown.Describe(noon, "2001-09-01", "", true)!.Target == new DateTime(2027, 9, 1), "Yearly countdown moves to next year once passed");
    Check(Countdown.Describe(new DateTime(2027, 3, 1), "2024-02-29", "", true)!.Target.Year == 2028, "Feb 29 yearly countdowns survive non-leap years");
    Check(Countdown.Describe(noon, "not a date", "", false) is null && Countdown.Describe(noon, "2026-13-40", "", false) is null, "Invalid countdown dates show nothing");

    // Folder listing
    string listing = Path.Combine(folder, "listing");
    Directory.CreateDirectory(listing);
    foreach (var (name, age) in new[] { ("old.txt", 300), ("new.pdf", 1), ("mid.docx", 60), ("half.crdownload", 0), ("~$lock.docx", 0), ("desktop.ini", 0) })
    {
        string path = Path.Combine(listing, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-age));
    }
    string hidden = Path.Combine(listing, "secret.txt");
    File.WriteAllText(hidden, "x");
    File.SetAttributes(hidden, FileAttributes.Hidden);
    Directory.CreateDirectory(Path.Combine(listing, "Project"));
    Directory.SetLastWriteTime(Path.Combine(listing, "Project"), DateTime.Now.AddMinutes(-30));
    var recentFiles = FolderListing.Recent(listing, 10);
    Check(recentFiles.Select(e => e.Name).SequenceEqual(["new.pdf", "Project", "mid.docx", "old.txt"]) && recentFiles[1].IsFolder, "Folder lists newest first, skipping partial, lock, hidden and system files");
    Check(FolderListing.Recent(listing, 2).Count == 2 && FolderListing.Recent(Path.Combine(listing, "missing"), 5).Count == 0, "Folder listing respects the limit and missing folders");
    Check(Directory.Exists(FolderListing.Downloads) || FolderListing.Downloads.EndsWith("Downloads", StringComparison.OrdinalIgnoreCase), "Downloads folder is found");

    // Weather
    var places = WeatherService.ParsePlaces("""{"results":[{"name":"Austin","latitude":30.26715,"longitude":-97.74306,"country":"United States","admin1":"Texas"},{"name":"Austin","latitude":43.66,"longitude":-92.97,"country":"United States","admin1":"Minnesota"}]}""");
    Check(places.Count == 2 && places[0].Label == "Austin, Texas, United States" && Math.Abs(places[1].Latitude - 43.66) < 1e-9, "Weather place search results are read");
    Check(WeatherService.ParsePlaces("""{"generationtime_ms":0.5}""").Count == 0, "No place results means an empty list");
    var report = WeatherService.Parse("""{"current":{"temperature_2m":78.4,"apparent_temperature":80.1,"relative_humidity_2m":48,"weather_code":2,"wind_speed_10m":9.2,"is_day":0},"daily":{"time":["2026-10-04","2026-10-05"],"weather_code":[2,95],"temperature_2m_max":[84,72],"temperature_2m_min":[66,60],"precipitation_probability_max":[10,null]}}""");
    Check(report.Temperature == 78.4 && report.Code == 2 && !report.IsDay && report.Humidity == 48 && report.Days.Count == 2 && report.Days[1].Code == 95 && report.Days[1].RainChance is null && report.Days[0].RainChance == 10, "Weather forecast is read");
    try { WeatherService.Parse("""{"error":true,"reason":"bad"}"""); throw new Exception("Expected weather format failure"); }
    catch (FormatException) { Console.WriteLine("PASS: Unexpected weather responses fail clearly"); }
    Check(WeatherService.Describe(0).Icon == "clear" && WeatherService.Describe(63).Text == "Rain" && WeatherService.Describe(99).Icon == "storm" && WeatherService.Describe(73).Icon == "snow" && WeatherService.Describe(1234).Text == "—", "Weather codes map to words and icons");



    var habit = new HabitEntry { Name = "Walk" };
    var day = new DateOnly(2026, 10, 4);
    Check(Trackers.Streak(habit, day) == 0, "Empty habit has no streak");
    Trackers.Toggle(habit, day.AddDays(-1));
    Trackers.Toggle(habit, day.AddDays(-2));
    Check(Trackers.Streak(habit, day) == 2, "Streak continues through yesterday while today is open");
    Check(Trackers.Toggle(habit, day) && Trackers.Streak(habit, day) == 3, "Completing today extends the streak");
    Check(!Trackers.Toggle(habit, day.AddDays(-1)) && Trackers.Streak(habit, day) == 1, "Unticking a day breaks the streak");
    Trackers.Toggle(habit, day.AddDays(-500));
    Trackers.Toggle(habit, day.AddDays(-3));
    Check(!habit.Days.Contains(Trackers.Key(day.AddDays(-500))), "History older than 400 days is trimmed");
    var counter = new LauncherItem { Kind = ItemKinds.Counter, Step = 5, Count = long.MaxValue - 2 };
    Check(Trackers.Bump(counter, 1) == long.MaxValue && Trackers.Bump(counter, -1) == long.MaxValue - 5, "Counter steps saturate instead of overflowing");

    // ---------- v0.3: migration, planner, secrets ----------
    File.WriteAllText(legacy.FilePath, """{ "SchemaVersion": 2, "Items": [ { "Id": "m1", "Name": "Mint", "Target": "C:\\a.bat", "Arguments": "", "WorkingDirectory": "", "Accent": "#A7DBC8", "X": 0, "Y": 0 }, { "Id": "m2", "Name": "Blue", "Target": "C:\\b.bat", "Arguments": "", "WorkingDirectory": "", "Accent": "#AFCBE9", "X": 0, "Y": 0 } ] }""");
    var migrated = legacy.Load();
    Check(migrated.Items[0].Accent == "#C4684F" && migrated.Items[1].Accent == "#AFCBE9" && migrated.SchemaVersion == DeckSettings.CurrentSchema, "Version 2 default accents move to the dusk palette; custom accents stay");
    migrated.Items[0].Accent = "#A7DBC8";
    legacy.Save(migrated);
    Check(legacy.Load().Items[0].Accent == "#A7DBC8", "Migration runs once; a deliberately chosen mint survives later saves");

    var sunday = new DateOnly(2026, 10, 4);
    Check(Planner.Monday(sunday) == new DateOnly(2026, 9, 28) && Planner.Monday(new DateOnly(2026, 9, 28)) == new DateOnly(2026, 9, 28), "Weeks start on Monday");
    Check(Planner.DayName(sunday) == "Sun", "Day names are short");
    Check(Planner.ParseTime("5:30 pm") == "17:30" && Planner.ParseTime("12am") == "00:00" && Planner.ParseTime("0835") == "08:35" && Planner.ParseTime("9") == "09:00" && Planner.ParseTime("13pm") is null && Planner.ParseTime("25:00") is null, "Time parsing");
    Check(Planner.Format("17:30", false) == "5:30 PM" && Planner.Format("00:05", false) == "12:05 AM" && Planner.Format("17:30", true) == "17:30", "Time formatting");
    var blocksForTest = new List<ScheduleBlock>
    {
        new() { Title = "School", Days = ["Mon", "Tue"], Start = "08:35", End = "15:10", CancelledDates = ["2026-09-29"] },
        new() { Title = "Run", Days = ["Mon"], Start = "06:00", End = "07:00" },
        new() { Title = "Trip", Date = "2026-09-30", Start = "10:00", End = "11:00" },
        new() { Title = "Broken", Days = ["Mon"], Start = "x", End = "y" },
    };
    Check(Planner.Occurrences(blocksForTest, new DateOnly(2026, 9, 28)).Select(b => b.Title).SequenceEqual(["Run", "School"]), "Occurrences are filtered by day and sorted by start");
    Check(Planner.Occurrences(blocksForTest, new DateOnly(2026, 9, 29)).Count == 0, "Cancelled dates are skipped");
    Check(Planner.Occurrences(blocksForTest, new DateOnly(2026, 9, 30)).Single().Title == "Trip", "One-off blocks appear only on their date");

    var todayList = new LauncherItem { Kind = ItemKinds.Tasks, DailyReset = true, Tasks = [new TaskEntry { Text = "a", Done = true }, new TaskEntry { Text = "b" }] };
    Check(Planner.RollOver(todayList, sunday) && todayList.Tasks.Count == 2, "First rollover only records the date");
    Check(!Planner.RollOver(todayList, sunday), "Same day does nothing");
    Check(Planner.RollOver(todayList, sunday.AddDays(1)) && todayList.Tasks.Single().Text == "b", "New day clears finished tasks and carries unfinished ones");
    var keepList = new LauncherItem { Kind = ItemKinds.Tasks, LastDate = "2026-10-01", Tasks = [new TaskEntry { Text = "a", Done = true }] };
    Planner.RollOver(keepList, sunday);
    Check(keepList.Tasks.Count == 1, "Lists without daily reset keep finished tasks");

    var pomo = new PomodoroState { WorkMinutes = 25, BreakMinutes = 5, LongBreakMinutes = 20, LongBreakEvery = 2 };
    var t0 = new DateTimeOffset(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);
    Check(Planner.Remaining(pomo, t0) == 1500 && !Planner.IsRunning(pomo), "Stopped timer shows the full session");
    Planner.Start(pomo, t0);
    Check(Planner.Remaining(pomo, t0.AddSeconds(61)) == 1439, "Remaining time comes from the end time");
    Planner.Pause(pomo, t0.AddSeconds(100));
    Check(Planner.Remaining(pomo, t0.AddHours(5)) == 1400 && !Planner.IsRunning(pomo), "Paused time does not move");
    Planner.Start(pomo, t0.AddHours(5));
    Check(Planner.IsFinished(pomo, t0.AddHours(5).AddSeconds(1400)), "Resumed session finishes after the paused remainder");
    Check(Planner.Advance(pomo, sunday, true) == "focus" && pomo.Mode == "break" && Planner.SessionsOn(pomo, sunday) == 1, "Finished focus logs a session and starts a break");
    Planner.Advance(pomo, sunday, true);
    Planner.Advance(pomo, sunday, true);
    Check(pomo.Mode == "long" && pomo.Cycle == 0 && Planner.SessionsOn(pomo, sunday) == 2, "Every Nth focus session earns a long break");
    Planner.Advance(pomo, sunday, false);
    Check(pomo.Mode == "focus" && pomo.EndsAt is null && pomo.PausedSeconds is null, "Skipping a break returns to a stopped focus session");

    var goalsItem = new LauncherItem { Kind = ItemKinds.Goals };
    var lastMonday = new DateOnly(2026, 9, 21);
    goalsItem.Goals.Add(new WeeklyGoal { Text = "Run 3x", Week = Planner.Key(lastMonday), Milestones = [new Milestone { Text = "1", Done = true }, new Milestone { Text = "2" }] });
    goalsItem.Goals.Add(new WeeklyGoal { Text = "Done one", Week = Planner.Key(lastMonday), Done = true });
    Check(Math.Abs(Planner.Progress(goalsItem.Goals[0]) - 0.5) < 1e-9 && Planner.Progress(goalsItem.Goals[1]) == 1, "Goal progress uses milestones");
    Check(Planner.CarryOver(goalsItem, lastMonday.AddDays(7)) == 1 && Planner.CarryOver(goalsItem, lastMonday.AddDays(7)) == 0, "Carry-over copies unfinished goals once");
    Check(Planner.GoalsFor(goalsItem, lastMonday.AddDays(7)).Single().Milestones.Count == 2, "Carried goals keep their steps");

    var secrets = new SecretStore(Path.Combine(folder, "secrets"));
    Check(secrets.Get("demo-a") is null && !secrets.Has("demo-a"), "No secret stored by default");
    const string fakeKey = "demo-TESTONLY-0123456789abcdef";
    secrets.Set("demo-a", "  " + fakeKey + " ");
    secrets.Set("demo-b", "TESTONLY987654321");
    Check(secrets.Get("demo-a") == fakeKey && secrets.Get("demo-b") == "TESTONLY987654321", "Secrets round-trip and are trimmed");
    string onDisk = File.ReadAllText(secrets.FilePath);
    Check(!onDisk.Contains("TESTONLY") && !onDisk.Contains("0123456789abcdef"), "Secret file contains no plaintext");
    Check(new SecretStore(Path.Combine(folder, "secrets")).Get("demo-a") == fakeKey, "Secrets survive a restart");
    secrets.Remove("demo-a");
    Check(secrets.Get("demo-a") is null && secrets.Get("demo-b") is not null, "Removing one secret keeps the other");
    Check(SecretStore.Hint(fakeKey) == "••••cdef", "Hint shows only the last four characters");

    // ---------- v0.4: appearance, per-widget options, pop-outs ----------
    var styled = new DeckSettings { Items = [new LauncherItem { Kind = ItemKinds.Clock, CardStyle = "glass", ShowHeader = false, ShowDate = false, PopOut = new PopOut { X = 10, Y = 20, Width = 300, Height = 160, OnTop = true } },
        new LauncherItem { Kind = ItemKinds.Schedule, StartHour = 7, EndHour = 22, HideWeekends = true }, new LauncherItem { Kind = ItemKinds.Counter, CountGoal = 8 }] };
    store.Save(styled);
    var styledBack = store.Load();
    Check(styledBack.Items[0].CardStyle == "glass" && !styledBack.Items[0].ShowHeader && !styledBack.Items[0].ShowDate && styledBack.Items[0].PopOut is null && styledBack.Items[0].OnTop, "Appearance round-trips; a saved pop-out becomes Keep on top");
    Check(styledBack.Items[1] is { StartHour: 7, EndHour: 22, HideWeekends: true } && styledBack.Items[2].CountGoal == 8, "Widget options round-trip");
    var copySource = new LauncherItem { PopOut = new PopOut { X = 10, Y = 20, Width = 300, Height = 160 } };
    var copyPop = copySource.Copy();
    copyPop.PopOut!.X = 999;
    Check(copySource.PopOut!.X == 10, "Copy does not share the pop-out position");
    File.WriteAllText(legacy.FilePath, """{ "SchemaVersion": 3, "Items": [ { "Id": "z", "Kind": "schedule", "Name": "S", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0, "CardStyle": "neon", "StartHour": 30, "PopOut": { "X": 0, "Y": 0, "Width": 5, "Height": 5 } } ] }""");
    var repaired = legacy.Load().Items[0];
    Check(repaired.CardStyle == "solid" && repaired.StartHour == -1 && repaired.PopOut is null, "Bad appearance values and tiny pop-outs are repaired");

    // ---------- Calendars: iCal, Google JSON, sign-in, caching ----------
    // Everything here is local: iCal text and Google JSON are fixtures, HTTP goes to a fake handler, and the sign-in
    // redirect only travels to a loopback listener on this PC. Google is never contacted.
    {
        // Folding, escapes, UTC, time zones, floating and all-day times.
        var wide = (From: new DateTime(2026, 9, 1), To: new DateTime(2027, 1, 1));
        var single = IcsCalendar.Parse(Ics(
            "BEGIN:VEVENT", "UID:a1", "SUMMARY:Team sy", " nc\\, weekly", "LOCATION:Room 4\\; North", "DESCRIPTION:Line one\\nLine two",
            "DTSTART:20261005T140000Z", "DTEND:20261005T150000Z", "END:VEVENT")).Expand(wide.From, wide.To, "c", "Cal", "#123456");
        Check(single.Count == 1 && single[0].Title == "Team sync, weekly" && single[0].Location == "Room 4; North" && single[0].Description == "Line one\nLine two", "iCal: folded lines and escaped text");
        Check(single[0].Start == FromUtc(2026, 10, 5, 14, 0) && single[0].End == FromUtc(2026, 10, 5, 15, 0) && !single[0].AllDay && single[0].Source == CalendarSource.Ics && !single[0].Editable && single[0].Color == "#123456", "iCal: UTC times become local; iCal events are read-only");

        var zoned = IcsCalendar.Parse(Ics("BEGIN:VEVENT", "UID:z1", "SUMMARY:NY", "DTSTART;TZID=America/New_York:20261005T090000", "DTEND;TZID=America/New_York:20261005T100000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:z2", "SUMMARY:Windows name", "DTSTART;TZID=\"Eastern Standard Time\":20261005T090000", "DTEND;TZID=\"Eastern Standard Time\":20261005T100000", "END:VEVENT")).Expand(wide.From, wide.To, "c", "Cal", "#123456");
        Check(zoned.Count == 2 && zoned.All(e => e.Start == FromUtc(2026, 10, 5, 13, 0) && e.End == FromUtc(2026, 10, 5, 14, 0)), "iCal: TZID times (IANA and Windows names) convert with daylight saving");
        Check(IcsCalendar.ResolveZone("Nonsense/Nowhere") is null && IcsCalendar.ResolveZone("UTC") == TimeZoneInfo.Utc, "iCal: unknown zone names are not guessed");

        var floating = IcsCalendar.Parse(Ics("BEGIN:VEVENT", "UID:f1", "SUMMARY:Floating", "DTSTART:20261005T093000", "DURATION:PT45M", "END:VEVENT")).Expand(wide.From, wide.To, "c", "Cal", "#123456");
        Check(floating.Count == 1 && floating[0].Start == Local(2026, 10, 5, 9, 30) && floating[0].End == Local(2026, 10, 5, 10, 15), "iCal: floating times stay on the wall clock; DURATION sets the end");

        var allDay = IcsCalendar.Parse(Ics("BEGIN:VEVENT", "UID:d1", "SUMMARY:Trip", "DTSTART;VALUE=DATE:20261007", "DTEND;VALUE=DATE:20261009", "END:VEVENT",
            "BEGIN:VEVENT", "UID:d2", "SUMMARY:Holiday", "DTSTART;VALUE=DATE:20261012", "END:VEVENT")).Expand(wide.From, wide.To, "c", "Cal", "#123456");
        Check(allDay.Count == 2 && allDay[0].AllDay && allDay[0].Start == Local(2026, 10, 7, 0, 0) && allDay[0].End == Local(2026, 10, 9, 0, 0) && allDay[0].LastDay == new DateOnly(2026, 10, 8), "iCal: all-day events (exclusive end)");
        Check(allDay[1].End == Local(2026, 10, 13, 0, 0), "iCal: an all-day event without DTEND lasts one day");
        Check(IcsCalendar.Parse(Ics("BEGIN:VEVENT", "UID:v", "SUMMARY:Alarm inside", "DTSTART:20261005T140000Z", "BEGIN:VALARM", "ACTION:DISPLAY", "SUMMARY:Reminder", "TRIGGER:-PT10M", "END:VALARM", "END:VEVENT")).Expand(wide.From, wide.To, "c", "Cal", "#123456") is [{ Title: "Alarm inside" }], "iCal: alarms inside an event are ignored");
        Check(IcsCalendar.Parse(Ics("BEGIN:VEVENT", "UID:x", "SUMMARY:Cancelled", "STATUS:CANCELLED", "DTSTART:20261005T140000Z", "END:VEVENT")).Expand(wide.From, wide.To, "c", "Cal", "#123456").Count == 0, "iCal: cancelled events are skipped");

        // Repeat rules.
        List<CalendarEvent> Run(string rule, string start, string extra = "", string? from = null, string? to = null, string zoneParam = "") =>
            IcsCalendar.Parse(Ics("BEGIN:VEVENT", "UID:r", "SUMMARY:R", $"DTSTART{zoneParam}:{start}", "DURATION:PT1H", "RRULE:" + rule, extra, "END:VEVENT").Replace("\r\n\r\n", "\r\n"))
                .Expand(from is null ? wide.From : DateTime.Parse(from, System.Globalization.CultureInfo.InvariantCulture), to is null ? wide.To : DateTime.Parse(to, System.Globalization.CultureInfo.InvariantCulture), "c", "Cal", "#123456");
        string Days(IEnumerable<CalendarEvent> list) => string.Join(",", list.Select(e => e.Start.ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture)));

        var weekly = Run("FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=5", "20261005T090000");
        Check(Days(weekly) == "10-05,10-07,10-09,10-12,10-14" && weekly.All(e => e.Start.TimeOfDay == TimeSpan.FromHours(9)), "iCal RRULE: weekly on several days with COUNT");
        Check(Days(Run("FREQ=WEEKLY;BYDAY=TU,TH;UNTIL=20261015", "20261006T100000")) == "10-06,10-08,10-13,10-15", "iCal RRULE: UNTIL as a date is inclusive");
        Check(Run("FREQ=WEEKLY;BYDAY=TU,TH;UNTIL=20261015T100000Z", "20261006T100000Z").Count == 4 && Run("FREQ=WEEKLY;BYDAY=TU,TH;UNTIL=20261015T095959Z", "20261006T100000Z").Count == 3, "iCal RRULE: UNTIL in UTC is inclusive to the second");
        var exdated = Run("FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=5", "20261005T090000", "EXDATE:20261007T090000");
        Check(Days(exdated) == "10-05,10-09,10-12,10-14", "iCal RRULE: EXDATE removes an occurrence without freeing a COUNT slot");
        Check(Days(Run("FREQ=WEEKLY;INTERVAL=2;BYDAY=TU", "20261006T100000", "", null, "2026-11-30")) == "10-06,10-20,11-03,11-17", "iCal RRULE: INTERVAL=2 skips weeks");
        Check(Days(Run("FREQ=DAILY;COUNT=3", "20261005T090000")) == "10-05,10-06,10-07", "iCal RRULE: daily with COUNT");
        Check(Days(Run("FREQ=MONTHLY;BYDAY=1MO", "20261005T100000", "", null, "2027-01-31")) == "10-05,11-02,12-07,01-04", "iCal RRULE: monthly on the first Monday");
        Check(Days(Run("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1", "20261030T170000", "", null, "2027-01-01")) == "10-30,11-30,12-31", "iCal RRULE: BYSETPOS picks the last weekday of the month");
        Check(Days(Run("FREQ=MONTHLY", "20260131T080000", "", "2026-01-01", "2026-06-01")) == "01-31,03-31,05-31", "iCal RRULE: monthly on the 31st skips shorter months");
        Check(Days(Run("FREQ=MONTHLY;BYMONTHDAY=-1", "20261031T080000", "", null, "2027-01-15")) == "10-31,11-30,12-31", "iCal RRULE: BYMONTHDAY=-1 is the last day of each month");
        var birthday = IcsCalendar.Parse(Ics("BEGIN:VEVENT", "UID:b", "SUMMARY:Birthday", "DTSTART;VALUE=DATE:20100315", "RRULE:FREQ=YEARLY", "END:VEVENT")).Expand(new DateTime(2027, 1, 1), new DateTime(2028, 1, 1), "c", "Cal", "#123456");
        Check(birthday.Count == 1 && birthday[0].AllDay && birthday[0].Start == Local(2027, 3, 15, 0, 0), "iCal RRULE: yearly all-day event");
        var longRunning = Run("FREQ=DAILY", "20150105T090000", "", "2026-10-05", "2026-10-12");
        Check(longRunning.Count == 7, "iCal RRULE: only the requested range is returned, however old the series is");

        // Overrides can come before their master, and replace exactly one occurrence.
        var overridden = IcsCalendar.Parse(Ics(
            "BEGIN:VEVENT", "UID:o1", "RECURRENCE-ID:20261006T090000", "SUMMARY:Moved", "DTSTART:20261006T130000", "DTEND:20261006T140000", "END:VEVENT",
            "BEGIN:VEVENT", "UID:o1", "SUMMARY:Standup", "DTSTART:20261005T090000", "DTEND:20261005T093000", "RRULE:FREQ=DAILY;COUNT=3", "END:VEVENT")).Expand(wide.From, wide.To, "c", "Cal", "#123456");
        Check(overridden.Count == 3 && overridden.Select(e => e.Title + "@" + e.Start.ToString("MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)).SequenceEqual(["Standup@10-05 09:00", "Moved@10-06 13:00", "Standup@10-07 09:00"]), "iCal RECURRENCE-ID: an override replaces one occurrence, even listed before its series");

        // Daylight saving: the wall clock stays at 9:00 while the UTC offset changes; a time in the spring gap doesn't crash.
        var dst = Run("FREQ=WEEKLY;COUNT=2", "20261026T090000", "", "2026-10-20", "2026-11-10", ";TZID=America/New_York");
        Check(dst.Count == 2 && dst[0].Start == FromUtc(2026, 10, 26, 13, 0) && dst[1].Start == FromUtc(2026, 11, 2, 14, 0), "iCal: a weekly zoned event keeps its wall-clock time across the end of daylight saving");
        var gap = Run("FREQ=DAILY;COUNT=3", "20260307T023000", "", "2026-03-01", "2026-03-20", ";TZID=America/New_York");
        Check(gap.Count == 3, "iCal: a time inside the spring-forward gap still produces its occurrence");
        Check(IcsCalendar.NormalizeAddress("webcal://example.test/a.ics") == "https://example.test/a.ics" && IcsCalendar.NormalizeAddress("not a url") is null && IcsCalendar.NormalizeAddress("ftp://x.test/a.ics") is null && IcsCalendar.NormalizeAddress("") is null, "iCal: webcal:// addresses are normalised; other text is rejected");

        // Lanes, clipping and colors.
        var lanes = CalendarLanes.Assign(new[] { (S: 600, E: 660, N: "b"), (S: 540, E: 630, N: "a"), (S: 700, E: 760, N: "c") }, x => x.S, x => x.E);
        Check(lanes.Count == 3 && lanes.Single(l => l.Item.N == "a").Lane == 0 && lanes.Single(l => l.Item.N == "b").Lane == 1 && lanes.Single(l => l.Item.N == "a").Lanes == 2 && lanes.Single(l => l.Item.N == "c").Lanes == 1, "Lanes: unsorted overlapping items share the width; a later item gets the whole width");
        var night = new CalendarEvent { Start = new DateTime(2026, 10, 5, 22, 0, 0), End = new DateTime(2026, 10, 6, 2, 0, 0) };
        Check(night.MinutesOn(new DateOnly(2026, 10, 5)) == (1320, 1440) && night.MinutesOn(new DateOnly(2026, 10, 6)) == (0, 120) && night.OnDay(new DateOnly(2026, 10, 6)) && !night.OnDay(new DateOnly(2026, 10, 7)), "Events that run past midnight are clipped to each day");
        Check(CalendarColors.IsLight("#fbd75b") && !CalendarColors.IsLight("#1a73e8") && CalendarColors.Normalize("#ABC") == "#aabbcc" && CalendarColors.Normalize("nope", "#111111") == "#111111" && CalendarColors.ForEventColorId("11") == "#dc2127", "Calendar colors: contrast, shorthand and Google's event palette");

        // Google JSON.
        var primary = new GoogleCalendarChoice { Id = "me@example.com", Name = "Me", Color = "#4285f4", Primary = true, AccessRole = "owner" };
        var holidays = new GoogleCalendarChoice { Id = "holidays#x@group.v.calendar.google.com", Name = "Holidays", Color = "#0b8043", AccessRole = "reader" };
        string eventsJson = """
        { "items": [
          { "id": "t1", "etag": "\"e1\"", "status": "confirmed", "summary": "Dentist", "location": "Main St", "description": "Bring <b>forms</b><br>and ID", "htmlLink": "https://www.google.com/calendar/event?eid=t1",
            "start": { "dateTime": "2026-10-05T09:00:00-05:00" }, "end": { "dateTime": "2026-10-05T10:30:00-05:00" } },
          { "id": "u1", "summary": "UTC call", "start": { "dateTime": "2026-10-05T14:00:00Z" }, "end": { "dateTime": "2026-10-05T15:00:00Z" }, "colorId": "11" },
          { "id": "d1", "summary": "Conference", "start": { "date": "2026-10-07" }, "end": { "date": "2026-10-10" } },
          { "id": "r1_20261006T140000Z", "recurringEventId": "r1", "summary": "Weekly sync", "start": { "dateTime": "2026-10-06T14:00:00Z" }, "end": { "dateTime": "2026-10-06T14:30:00Z" } },
          { "id": "x1", "status": "cancelled", "summary": "Gone", "start": { "dateTime": "2026-10-05T14:00:00Z" }, "end": { "dateTime": "2026-10-05T15:00:00Z" } },
          { "id": "n1", "summary": "Declined", "attendees": [ { "self": true, "responseStatus": "declined" } ], "start": { "dateTime": "2026-10-05T14:00:00Z" }, "end": { "dateTime": "2026-10-05T15:00:00Z" } },
          { "id": "bd", "eventType": "birthday", "summary": "Sam's birthday", "start": { "date": "2026-10-08" }, "end": { "date": "2026-10-09" } },
          { "id": "nt", "start": { "dateTime": "2026-10-09T08:00:00Z" } },
          { "id": "bad", "summary": "No start" }
        ], "nextPageToken": "page2" }
        """;
        var parsedEvents = GoogleCalendarService.ParseEvents(eventsJson, primary, out var nextPage);
        Check(parsedEvents.Count == 6 && nextPage == "page2", "Google JSON: cancelled, declined and start-less events are skipped; paging token is read");
        var dentist = parsedEvents[0];
        Check(dentist.Start == FromUtc(2026, 10, 5, 14, 0) && dentist.End == FromUtc(2026, 10, 5, 15, 30) && !dentist.AllDay && dentist.Editable && dentist.GoogleId == "t1" && dentist.ETag == "\"e1\"" && dentist.HtmlLink.StartsWith("https://www.google.com/") && dentist.Description == "Bring forms\nand ID" && dentist.Color == "#4285f4", "Google JSON: a timed event with an offset converts to local time, and HTML descriptions become text");
        Check(parsedEvents[1].Start == FromUtc(2026, 10, 5, 14, 0) && parsedEvents[1].Color == "#dc2127", "Google JSON: UTC times, and an event colorId overrides the calendar color");
        Check(parsedEvents[2].AllDay && parsedEvents[2].Start == Local(2026, 10, 7, 0, 0) && parsedEvents[2].End == Local(2026, 10, 10, 0, 0) && parsedEvents[2].LastDay == new DateOnly(2026, 10, 9), "Google JSON: all-day events use date fields with an exclusive end");
        Check(parsedEvents[3].IsRecurringInstance && parsedEvents[3].RecurringEventId == "r1" && !parsedEvents[0].IsRecurringInstance, "Google JSON: instances of repeating events are recognised");
        Check(!parsedEvents[4].Editable && parsedEvents[4].Title == "Sam's birthday" && parsedEvents[5].Title == "(No title)" && parsedEvents[5].End == parsedEvents[5].Start, "Google JSON: birthdays can't be edited; a missing title and end are handled");
        Check(!GoogleCalendarService.ParseEvents(eventsJson, holidays)[0].Editable, "Google JSON: events on a read-only calendar are not editable");

        string calendarsJson = """
        { "items": [
          { "id": "holidays#x@group.v.calendar.google.com", "summary": "Holidays in US", "backgroundColor": "#0B8043", "accessRole": "reader", "selected": false },
          { "id": "me@example.com", "summary": "me@example.com", "summaryOverride": "Personal", "backgroundColor": "#9fe1e7", "accessRole": "owner", "primary": true },
          { "id": "team@example.com", "summary": "Team", "backgroundColor": "#f83a22", "accessRole": "writer", "selected": true },
          { "id": "old", "deleted": true } ] }
        """;
        var calendars = GoogleCalendarService.ParseCalendarList(calendarsJson);
        Check(calendars.Count == 3 && calendars[1].Name == "Personal" && calendars[1].Primary && calendars[1].Visible && !calendars[0].Visible && calendars[2].Visible && calendars[2].CanEdit && !calendars[0].CanEdit && calendars[0].Color == "#0b8043", "Google JSON: calendar list names, colors, access and default visibility");
        var saved = new List<GoogleCalendarChoice> { new() { Id = "team@example.com", Name = "Team", Visible = false }, new() { Id = "gone@example.com", Name = "Gone" } };
        var merged = GoogleCalendarService.MergeCalendars(saved, calendars);
        Check(merged.Count == 3 && merged[0].Primary && !merged.Single(c => c.Id == "team@example.com").Visible && merged.All(c => c.Id != "gone@example.com"), "Calendar list refresh keeps show/hide choices, adds new calendars and drops removed ones");

        // Event JSON for create and PATCH (a fixed UTC-5 zone with no IANA name, then the real Eastern zone).
        var minusFive = TimeZoneInfo.CreateCustomTimeZone("Test", TimeSpan.FromHours(-5), "Test", "Test");
        using (var created = System.Text.Json.JsonDocument.Parse(GoogleCalendarService.BuildEventJson(new EventDraft("Lunch", new DateTime(2026, 10, 5, 12, 0, 0), new DateTime(2026, 10, 5, 13, 0, 0), false, "Cafe", ""), null, minusFive)))
        {
            var r = created.RootElement;
            Check(r.GetProperty("summary").GetString() == "Lunch" && r.GetProperty("location").GetString() == "Cafe" && !r.TryGetProperty("description", out _)
                  && r.GetProperty("start").GetProperty("dateTime").GetString() == "2026-10-05T12:00:00-05:00" && r.GetProperty("end").GetProperty("dateTime").GetString() == "2026-10-05T13:00:00-05:00" && !r.GetProperty("start").TryGetProperty("date", out _), "Event JSON: create a timed event with a UTC offset");
        }
        using (var createdAllDay = System.Text.Json.JsonDocument.Parse(GoogleCalendarService.BuildEventJson(new EventDraft("Trip", new DateTime(2026, 10, 7), new DateTime(2026, 10, 10), true, "", "Notes"), null, minusFive)))
            Check(createdAllDay.RootElement.GetProperty("start").GetProperty("date").GetString() == "2026-10-07" && createdAllDay.RootElement.GetProperty("end").GetProperty("date").GetString() == "2026-10-10" && !createdAllDay.RootElement.GetProperty("start").TryGetProperty("dateTime", out _) && createdAllDay.RootElement.GetProperty("description").GetString() == "Notes", "Event JSON: create an all-day event (end date is exclusive)");
        var eastern = ZoneOrNull("Eastern Standard Time");
        if (eastern is not null)
        {
            using var east = System.Text.Json.JsonDocument.Parse(GoogleCalendarService.BuildEventJson(new EventDraft("Call", new DateTime(2026, 10, 5, 9, 0, 0), new DateTime(2026, 10, 5, 9, 30, 0), false, "", ""), null, eastern));
            Check(east.RootElement.GetProperty("start").GetProperty("dateTime").GetString() == "2026-10-05T09:00:00-04:00" && east.RootElement.GetProperty("start").GetProperty("timeZone").GetString() == "America/New_York", "Event JSON: a real zone adds its IANA name and the daylight-saving offset");
        }
        var existingEvent = dentist with { Title = "Dentist", Location = "Main St", Description = "Bring forms\nand ID" };
        Check(GoogleCalendarService.BuildEventJson(new EventDraft("Dentist", existingEvent.Start, existingEvent.End, false, "Main St", "Bring forms\nand ID"), existingEvent, minusFive) == "{}", "Event JSON: PATCH with no changes sends nothing");
        using (var titleOnly = System.Text.Json.JsonDocument.Parse(GoogleCalendarService.BuildEventJson(new EventDraft("Dentist (moved)", existingEvent.Start, existingEvent.End, false, "Main St", "Bring forms\nand ID"), existingEvent, minusFive)))
            Check(titleOnly.RootElement.EnumerateObject().Select(p => p.Name).SequenceEqual(["summary"]), "Event JSON: PATCH sends only the title when only the title changed (rich descriptions are never overwritten)");
        using (var toAllDay = System.Text.Json.JsonDocument.Parse(GoogleCalendarService.BuildEventJson(new EventDraft("Dentist", new DateTime(2026, 10, 5), new DateTime(2026, 10, 6), true, "Main St", "Bring forms\nand ID"), existingEvent, minusFive)))
            Check(toAllDay.RootElement.GetProperty("start").GetProperty("date").GetString() == "2026-10-05" && toAllDay.RootElement.GetProperty("start").GetProperty("dateTime").ValueKind == System.Text.Json.JsonValueKind.Null, "Event JSON: PATCH to all-day clears the dateTime");
        using (var cleared = System.Text.Json.JsonDocument.Parse(GoogleCalendarService.BuildEventJson(new EventDraft("Dentist", existingEvent.Start, existingEvent.End, false, "", "Bring forms\nand ID"), existingEvent, minusFive)))
            Check(cleared.RootElement.GetProperty("location").GetString() == "", "Event JSON: PATCH can clear the location");
        string url = GoogleCalendarService.ListEventsUrl("me@example.com", new DateTime(2026, 10, 5), new DateTime(2026, 10, 12), "tok/en");
        Check(url.Contains("/calendars/me%40example.com/events?") && url.Contains("singleEvents=true") && url.Contains("orderBy=startTime") && url.Contains("maxResults=250") && url.Contains("timeMin=") && url.Contains("pageToken=tok%2Fen"), "Events request: single events, ordered by start, 250 per page, escaped ids");
        Check(GoogleCalendarService.DescribeError(System.Net.HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"Insufficient Permission","errors":[{"reason":"insufficientPermissions"}]}}""").Message.Contains("view-only")
              && GoogleCalendarService.DescribeError(System.Net.HttpStatusCode.Unauthorized, "").Message == GoogleAuthException.Expired
              && GoogleCalendarService.DescribeError(System.Net.HttpStatusCode.NotFound, "{}").Message.StartsWith("That event no longer exists"), "Google errors become plain messages");

        // PKCE, authorization address, tokens.
        Check(GoogleAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk") == "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", "PKCE: S256 challenge matches the RFC 7636 example");
        var verifier = GoogleAuth.NewVerifier();
        Check(verifier.Length == 64 && verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~') && verifier != GoogleAuth.NewVerifier(), "PKCE: verifiers are random and use only allowed characters");
        string authUrl = GoogleAuth.BuildAuthUrl("id.apps.googleusercontent.com", "http://127.0.0.1:5123/", GoogleScopes.Calendar, "st", "chal", "me@example.com", includeGranted: true);
        var authQuery = System.Web.HttpUtility.ParseQueryString(new Uri(authUrl).Query);
        Check(authUrl.StartsWith("https://accounts.google.com/") && authQuery["redirect_uri"] == "http://127.0.0.1:5123/" && authQuery["code_challenge_method"] == "S256" && authQuery["code_challenge"] == "chal" && authQuery["access_type"] == "offline" && authQuery["prompt"] == "consent" && authQuery["include_granted_scopes"] == "true"
              && authQuery["scope"]!.Split(' ').SequenceEqual(GoogleScopes.Calendar) && authQuery["login_hint"] == "me@example.com" && authQuery["response_type"] == "code", "Authorization address: scopes, PKCE, loopback redirect, offline access");
        string idToken = "h." + GoogleAuth.Base64Url(System.Text.Encoding.UTF8.GetBytes("""{"email":"me@example.com","sub":"1"}""")) + ".s";
        Check(GoogleAuth.EmailFromIdToken(idToken) == "me@example.com" && GoogleAuth.EmailFromIdToken("garbage") is null && GoogleAuth.EmailFromIdToken(null) is null, "Sign-in: the account email is read from the ID token");
        var tokens = GoogleAuth.ParseTokenResponse("""{"access_token":"at","expires_in":3599,"refresh_token":"rt","scope":"openid email","id_token":"x"}""");
        Check(tokens.AccessToken == "at" && tokens.ExpiresInSeconds == 3599 && tokens.RefreshToken == "rt" && tokens.Scope == "openid email", "Sign-in: token answers are read");
        try { GoogleAuth.ParseTokenResponse("""{"error":"invalid_grant"}""", refreshing: true); throw new Exception("expected"); }
        catch (GoogleAuthException ex) when (ex.NeedsReconnect && ex.Message == "Your Google connection expired — Connect again") { Console.WriteLine("PASS: An expired refresh token asks to Connect again"); }
        try { GoogleAuth.ParseTokenResponse("""{"error":"access_denied"}"""); throw new Exception("expected"); }
        catch (GoogleAuthException ex) when (ex.Message == "Google sign-in was cancelled") { Console.WriteLine("PASS: Declining consent reads as cancelled"); }

        // GoogleAuth with fake servers: one refresh at a time, invalid_grant, the full loopback flow, incremental consent.
        string authFolder = Path.Combine(folder, "google-auth");
        var authSecrets = new SecretStore(authFolder);
        authSecrets.Set(GoogleAuth.ClientIdEntry, "client-id");
        authSecrets.Set(GoogleAuth.ClientSecretEntry, "client-secret-value");
        authSecrets.Set(GoogleAuth.RefreshEntry, "refresh-token-value");
        authSecrets.Set(GoogleAuth.ScopesEntry, string.Join(" ", GoogleScopes.Calendar));
        var clock = DateTimeOffset.UtcNow;
        var refreshServer = new FakeHandler((_, _) => Json("""{"access_token":"access-1","expires_in":3600}"""));
        var auth = new GoogleAuth(authSecrets, new HttpClient(refreshServer), _ => throw new Exception("browser must not open"), () => clock);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => auth.GetAccessTokenAsync()));
        Check(concurrent.All(t => t == "access-1") && refreshServer.Calls == 1, "Access token: six simultaneous callers cause one refresh");
        var form = System.Web.HttpUtility.ParseQueryString(refreshServer.Bodies[0]);
        Check(form["grant_type"] == "refresh_token" && form["refresh_token"] == "refresh-token-value" && form["client_id"] == "client-id", "Access token: the refresh request carries the saved credentials");
        await auth.GetAccessTokenAsync();
        Check(refreshServer.Calls == 1, "Access token: a fresh token is reused");
        clock = clock.AddMinutes(59);
        await auth.GetAccessTokenAsync();
        Check(refreshServer.Calls == 2, "Access token: refreshed again when it is about to expire");
        Check(auth.HasScopes(GoogleScopes.CalendarEvents) && !auth.HasScopes(GoogleScopes.Tasks), "Granted scopes are remembered");
        await auth.EnsureScopesAsync(GoogleScopes.CalendarEvents, GoogleScopes.Email);
        Console.WriteLine("PASS: EnsureScopesAsync does nothing (and opens no browser) when the scopes are already granted");

        var expiredServer = new FakeHandler((_, _) => Json("""{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""", System.Net.HttpStatusCode.BadRequest));
        var expiredAuth = new GoogleAuth(authSecrets, new HttpClient(expiredServer), _ => Task.CompletedTask, () => clock);
        try { await expiredAuth.GetAccessTokenAsync(); throw new Exception("expected"); }
        catch (GoogleAuthException ex) when (ex.NeedsReconnect && ex.Message == GoogleAuthException.Expired) { Console.WriteLine("PASS: A revoked refresh token reports the connection expired"); }
        Check(!authSecrets.Has(GoogleAuth.RefreshEntry) && authSecrets.Get(GoogleAuth.ClientIdEntry) == "client-id" && !expiredAuth.IsConnected, "After it expired only the refresh token is dropped, so Connect needs no new client");

        // Full loopback flow: the "browser" opens the consent address, then the redirect hits the local listener.
        string loopFolder = Path.Combine(folder, "google-loop");
        var loopSecrets = new SecretStore(loopFolder);
        string? seenChallenge = null;
        var tokenServer = new FakeHandler((_, body) =>
        {
            var f = System.Web.HttpUtility.ParseQueryString(body);
            bool good = f["code"] == "auth-code-1" && f["redirect_uri"]!.StartsWith("http://127.0.0.1:") && f["redirect_uri"]!.EndsWith("/") && GoogleAuth.Challenge(f["code_verifier"]!) == seenChallenge && f["client_secret"] == "secret-xyz";
            return good ? Json("{\"access_token\":\"at\",\"expires_in\":3600,\"refresh_token\":\"rt-loop\",\"scope\":\"" + string.Join(" ", GoogleScopes.Calendar) + "\",\"id_token\":\"" + idToken + "\"}") : Json("""{"error":"invalid_grant"}""", System.Net.HttpStatusCode.BadRequest);
        });
        using var browserClient = new HttpClient();
        // Opening the browser returns at once; the "user" then lands on the redirect address a moment later.
        var flow = new GoogleAuth(loopSecrets, new HttpClient(tokenServer), address =>
        {
            var q = System.Web.HttpUtility.ParseQueryString(new Uri(address).Query);
            seenChallenge = q["code_challenge"];
            _ = Task.Run(async () =>
            {
                _ = await browserClient.GetAsync(q["redirect_uri"] + "favicon.ico?x=1"); // a stray request is ignored
                _ = await browserClient.GetAsync(q["redirect_uri"] + "?code=auth-code-1&state=" + q["state"]);
            });
            return Task.CompletedTask;
        });
        string connectedEmail = await flow.ConnectAsync("  id-xyz ", "secret-xyz");
        Check(connectedEmail == "me@example.com" && flow.IsConnected && loopSecrets.Get(GoogleAuth.RefreshEntry) == "rt-loop" && loopSecrets.Get(GoogleAuth.ClientIdEntry) == "id-xyz" && flow.HasScopes(GoogleScopes.CalendarEvents), "Sign-in: loopback code flow with PKCE stores the client, refresh token, scopes and email");
        Check(!File.ReadAllText(loopSecrets.FilePath).Contains("rt-loop") && !File.ReadAllText(loopSecrets.FilePath).Contains("secret-xyz"), "Sign-in: tokens and the client secret are encrypted on disk");
        var deniedFlow = new GoogleAuth(new SecretStore(Path.Combine(folder, "google-denied")), new HttpClient(tokenServer), address =>
        {
            var q = System.Web.HttpUtility.ParseQueryString(new Uri(address).Query);
            _ = Task.Run(() => browserClient.GetAsync(q["redirect_uri"] + "?error=access_denied&state=" + q["state"]));
            return Task.CompletedTask;
        });
        try { await deniedFlow.ConnectAsync("a", "b"); throw new Exception("expected"); }
        catch (GoogleAuthException ex) when (ex.Message == "Google sign-in was cancelled") { Console.WriteLine("PASS: Clicking Cancel at Google reads as cancelled"); }
        using var stop = new CancellationTokenSource();
        var waiting = new GoogleAuth(new SecretStore(Path.Combine(folder, "google-wait")), new HttpClient(tokenServer), _ => { stop.CancelAfter(150); return Task.CompletedTask; });
        try { await waiting.ConnectAsync("a", "b", stop.Token); throw new Exception("expected"); }
        catch (GoogleAuthException ex) when (ex.Message == "Google sign-in was cancelled") { Console.WriteLine("PASS: Closing the waiting sign-in stops listening and reads as cancelled"); }
        string? incrementalUrl = null;
        var incremental = new GoogleAuth(loopSecrets, new HttpClient(tokenServer), address => { incrementalUrl = address; throw new GoogleAuthException(GoogleAuthException.Cancelled); });
        try { await incremental.EnsureScopesAsync(GoogleScopes.Tasks); throw new Exception("expected"); } catch (GoogleAuthException) { }
        var incrementalQuery = System.Web.HttpUtility.ParseQueryString(new Uri(incrementalUrl!).Query);
        Check(incrementalQuery["include_granted_scopes"] == "true" && incrementalQuery["scope"]!.Split(' ').Contains(GoogleScopes.Tasks) && incrementalQuery["scope"]!.Split(' ').Contains(GoogleScopes.CalendarEvents) && incrementalQuery["login_hint"] == "me@example.com", "Incremental consent asks for the new scope plus the ones already granted");
        var revokeServer = new FakeHandler((request, _) => Json("{}"));
        var disconnecting = new GoogleAuth(loopSecrets, new HttpClient(revokeServer));
        Check(await disconnecting.DisconnectAsync() && revokeServer.Calls == 1 && revokeServer.Bodies[0].StartsWith("token=rt-loop") && !loopSecrets.Has(GoogleAuth.RefreshEntry) && !loopSecrets.Has(GoogleAuth.ClientSecretEntry) && !disconnecting.IsConnected, "Disconnect revokes the grant and removes every saved Google secret");

        // Caching: iCal feed through the shared cache, with a fake host.
        string feedFolder = Path.Combine(folder, "feed");
        var feedSecrets = new SecretStore(feedFolder);
        var link = new IcsCalendarInfo { Label = "School", Color = "#14b8a6" };
        feedSecrets.Set(link.Entry, "webcal://school.example.test/private/abcdef/basic.ics");
        var feedSettings = new DeckSettings { IcsCalendars = [link] };
        string icsBody = Ics("BEGIN:VEVENT", "UID:s1", "SUMMARY:Math", "DTSTART:20261005T150000Z", "DTEND:20261005T160000Z", "END:VEVENT");
        bool schoolUp = true;
        var school = new FakeHandler((request, _) => schoolUp ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(icsBody) } : new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        var feedAuth = new GoogleAuth(feedSecrets, new HttpClient(school));
        var feed = new CalendarFeed(feedAuth, new GoogleCalendarService(feedAuth, new HttpClient(school)), feedSecrets, feedSettings, new HttpClient(school));
        var updated = new SemaphoreSlim(0);
        feed.Updated += () => updated.Release();
        var weekFrom = new DateOnly(2026, 10, 5);
        var feedFirst = feed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All);
        Check(feedFirst.Events.Count == 0 && feedFirst.Loading, "Feed: the first query answers at once (empty) and starts a background fetch");
        Check(await updated.WaitAsync(TimeSpan.FromSeconds(10)), "Feed: widgets are told when data arrives");
        var feedSecond = feed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All);
        Check(feedSecond.Events.Count == 1 && feedSecond.Events[0].Title == "Math" && feedSecond.Events[0].CalendarLabel == "School" && feedSecond.Note is null && school.Calls == 1 && school.Urls[0] == "https://school.example.test/private/abcdef/basic.ics", "Feed: cached events come back, webcal:// is fetched over https");
        feed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All);
        feed.Query(weekFrom.AddDays(7), weekFrom.AddDays(14), CalendarQuery.All);
        await Task.Delay(100);
        Check(school.Calls == 1, "Feed: rebuilding or navigating within 5 minutes does not refetch");
        Check(feed.Query(weekFrom, weekFrom.AddDays(7), new CalendarQuery(true, [], false)).Events.Count == 0 && feed.Query(weekFrom, weekFrom.AddDays(7), new CalendarQuery(false, [], true)).Events.Count == 1, "Feed: the iCal switch decides whether link events show");
        schoolUp = false;
        feed.Refresh();
        Check(await updated.WaitAsync(TimeSpan.FromSeconds(10)), "Feed: Refresh redraws widgets");
        feed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All);
        Check(await updated.WaitAsync(TimeSpan.FromSeconds(10)), "Feed: a failed refresh is reported");
        var offline = feed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All);
        Check(offline.Events.Count == 1 && offline.Note is { } note && note.StartsWith("Offline — showing ") && note.EndsWith(" data") && !note.Contains("abcdef"), "Feed: on failure the last events stay and a quiet offline note is shown (without the private address)");
        int callsAfterFailure = school.Calls;
        for (int i = 0; i < 5; i++) feed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All);
        await Task.Delay(100);
        Check(school.Calls == callsAfterFailure, "Feed: a failed fetch waits before trying again (no retry storm)");
        var notConnected = new CalendarFeed(new GoogleAuth(new SecretStore(Path.Combine(folder, "feed-none")), new HttpClient(school)), new GoogleCalendarService(feedAuth), new SecretStore(Path.Combine(folder, "feed-none")), new DeckSettings { GoogleCalendars = [new() { Id = "x", Name = "X" }] });
        Check(notConnected.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All) is { Events.Count: 0, Loading: false } && !notConnected.GoogleReady, "Feed: with no Google account nothing is fetched");
        var sampleFeed = new CalendarFeed(new GoogleAuth(new SecretStore(Path.Combine(folder, "feed-sample"))), new GoogleCalendarService(feedAuth), new SecretStore(Path.Combine(folder, "feed-sample")), new DeckSettings());
        sampleFeed.UseSample([new CalendarEvent { Source = CalendarSource.Google, Title = "Fake", Start = new DateTime(2026, 10, 6, 9, 0, 0), End = new DateTime(2026, 10, 6, 10, 0, 0) }]);
        Check(sampleFeed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All).Events.Count == 1 && sampleFeed.Auth.IsConnected && sampleFeed.Query(weekFrom.AddDays(7), weekFrom.AddDays(14), CalendarQuery.All).Events.Count == 0, "Feed: sample mode serves made-up events and never touches the network");

        // A sign-in that stopped working is reported in the widget (not silently dropped), and keeps the saved client.
        var expiredSecrets = new SecretStore(Path.Combine(folder, "feed-expired"));
        expiredSecrets.Set(GoogleAuth.ClientIdEntry, "id");
        expiredSecrets.Set(GoogleAuth.ClientSecretEntry, "secret");
        expiredSecrets.Set(GoogleAuth.RefreshEntry, "refresh");
        expiredSecrets.Set(GoogleAuth.ScopesEntry, string.Join(" ", GoogleScopes.Calendar));
        var revokedServer = new FakeHandler((_, _) => Json("""{"error":"invalid_grant"}""", System.Net.HttpStatusCode.BadRequest));
        var expiredFeedAuth = new GoogleAuth(expiredSecrets, new HttpClient(revokedServer));
        var expiredFeed = new CalendarFeed(expiredFeedAuth, new GoogleCalendarService(expiredFeedAuth, new HttpClient(revokedServer)), expiredSecrets, new DeckSettings { GoogleCalendars = [new() { Id = "me@example.com", Name = "Me", Primary = true, AccessRole = "owner" }] });
        var expiredSignal = new SemaphoreSlim(0);
        expiredFeed.Updated += () => expiredSignal.Release();
        expiredFeed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All);
        Check(await expiredSignal.WaitAsync(TimeSpan.FromSeconds(10)), "Feed: a revoked sign-in is noticed");
        var expiredSnapshot = expiredFeed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All);
        Check(expiredSnapshot.Note == GoogleAuthException.Expired && expiredSnapshot.NeedsReconnect && !expiredFeedAuth.IsConnected && expiredFeedAuth.HasClient, "Feed: widgets show \"Your Google connection expired — Connect again\" and the client stays saved");
        // Clearing the cache while a fetch is running (Settings does this) must not break the fetch.
        var slowSchool = new FakeHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(icsBody) });
        var clearedFeed = new CalendarFeed(feedAuth, new GoogleCalendarService(feedAuth), feedSecrets, feedSettings, new HttpClient(slowSchool));
        clearedFeed.Query(weekFrom, weekFrom.AddDays(7), CalendarQuery.All);
        clearedFeed.Clear(raise: false);
        await Task.Delay(250);
        Check(slowSchool.Calls == 1 && clearedFeed.Query(weekFrom, weekFrom.AddDays(7), new CalendarQuery(false, [], true)).Events.Count == 0, "Feed: clearing the cache during a fetch drops the result quietly");

        // Settings: new fields round-trip, defaults for old files, repair.
        var calendarStore = new SettingsStore(Path.Combine(folder, "calendar-settings"));
        var calendarDeck = new DeckSettings
        {
            GoogleEmail = "me@example.com",
            GoogleCalendars = [new GoogleCalendarChoice { Id = "me@example.com", Name = "Personal", Color = "#9fe1e7", Primary = true, AccessRole = "owner" }, new GoogleCalendarChoice { Id = "h", Name = "Holidays", Color = "#0b8043", Visible = false }],
            IcsCalendars = [new IcsCalendarInfo { Id = "ics1", Label = "School", Color = "#ec4899", Enabled = false }],
            Items = [new LauncherItem { Kind = ItemKinds.Schedule, Name = "Week", ShowGoogle = false, GoogleCalendarIds = ["me@example.com", "h"], ShowIcs = false, ExternalStyle = "outline", ShowAllDay = false }],
        };
        calendarStore.Save(calendarDeck);
        string settingsText = File.ReadAllText(calendarStore.FilePath);
        var reloaded = calendarStore.Load();
        var sched = reloaded.Items[0];
        Check(!sched.ShowGoogle && !sched.ShowIcs && !sched.ShowAllDay && sched.ExternalStyle == "outline" && sched.GoogleCalendarIds.SequenceEqual(["me@example.com", "h"]), "Settings: schedule options for Google and iCal round-trip");
        Check(reloaded.GoogleEmail == "me@example.com" && reloaded.GoogleCalendars.Count == 2 && !reloaded.GoogleCalendars[1].Visible && reloaded.GoogleCalendars[0].Primary && reloaded.GoogleCalendars[0].CanEdit && reloaded.IcsCalendars[0] is { Id: "ics1", Label: "School", Color: "#ec4899", Enabled: false }, "Settings: Google calendars and iCal link labels round-trip");
        Check(!settingsText.Contains("refresh") && !settingsText.Contains("secret") && !settingsText.Contains("http"), "Settings: no tokens or addresses are written to settings.json");
        var scheduleCopy = sched.Copy();
        scheduleCopy.GoogleCalendarIds.Add("zzz");
        Check(sched.GoogleCalendarIds.Count == 2, "Settings: copying a schedule copies its calendar list");
        var oldItem = new LauncherItem { Kind = ItemKinds.Schedule };
        Check(oldItem.ShowGoogle && oldItem.ShowIcs && oldItem.ShowAllDay && oldItem.ExternalStyle == "filled" && oldItem.GoogleCalendarIds.Count == 0, "Settings: new schedule options default to everything on, filled");
        File.WriteAllText(calendarStore.FilePath, """{ "Items": [ { "Id": "s", "Kind": "schedule", "Name": "S", "Target": "", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0, "ExternalStyle": "weird", "GoogleCalendarIds": [ "a", "a", "", null ] } ], "GoogleCalendars": [ { "Id": "c1", "Name": "", "Color": "red-ish", "AccessRole": "boss" }, { "Id": "c1" }, null, { "Id": "" } ], "IcsCalendars": [ { "Id": "dup", "Label": "A", "Color": "zz" }, { "Id": "dup", "Label": null } ], "GoogleEmail": null }""");
        var repairedSettings = calendarStore.Load();
        Check(repairedSettings.Items[0].ExternalStyle == "filled" && repairedSettings.Items[0].GoogleCalendarIds.SequenceEqual(["a"]) && repairedSettings.Items[0].ShowGoogle, "Settings: odd schedule values are repairedSettings on load");
        Check(repairedSettings.GoogleCalendars.Count == 1 && repairedSettings.GoogleCalendars[0].Name == "c1" && repairedSettings.GoogleCalendars[0].Color == "#4285f4" && repairedSettings.GoogleCalendars[0].AccessRole == "reader" && repairedSettings.GoogleEmail == ""
              && repairedSettings.IcsCalendars.Count == 2 && repairedSettings.IcsCalendars[0].Id != repairedSettings.IcsCalendars[1].Id && repairedSettings.IcsCalendars.All(c => c.Label is not null) && repairedSettings.IcsCalendars[0].Color == "#14b8a6", "Settings: calendar lists drop duplicates and blanks, and repair names, colors and ids");
        File.WriteAllText(calendarStore.FilePath, """{ "Items": [ { "Id": "o", "Name": "Old", "Target": "x", "Arguments": "", "WorkingDirectory": "", "Accent": "#fff", "X": 0, "Y": 0 } ] }""");
        var oldSettings = calendarStore.Load();
        Check(oldSettings.GoogleCalendars.Count == 0 && oldSettings.IcsCalendars.Count == 0 && oldSettings.GoogleEmail == "" && oldSettings.Items[0].ShowGoogle, "Settings: files from before calendars load with defaults");

        // Appearance: deck-wide card defaults and the background per display.
        Check(oldSettings.Appearance.IsDefault && oldSettings.Appearance.CardOpacity == 100 && oldSettings.Appearance.CornerRadius == 14 && oldSettings.Appearance.BackgroundFor("any") is null, "Appearance: files from before it load with the theme");
        var look = new DeckSettings
        {
            Items = [new LauncherItem { Id = "p", Name = "P", Options = { ["_cardColor"] = "#18334A", ["_borderColor"] = "#8b5cf6" } }],
            Appearance = new AppearanceSettings
            {
                CardColor = "#2B2450", CardOpacity = 80, CornerRadius = 22, TextColor = "#EDE4D3", BorderColor = "#33EDE4D3",
                Backgrounds =
                {
                    [""] = new BackgroundStyle { Kind = "gradient", Color = "#112233", Color2 = "#445566", Angle = 45 },
                    ["DISPLAY\\ONE"] = new BackgroundStyle { Kind = "image", ImagePath = @"C:\media\wall.jpg", Fit = "tile", Dim = 35, Blur = 6 },
                },
            },
        };
        calendarStore.Save(look);
        var looked = calendarStore.Load();
        Check(looked.Appearance.CardColor == "#2B2450" && looked.Appearance.CardOpacity == 80 && looked.Appearance.CornerRadius == 22 && looked.Appearance.TextColor == "#EDE4D3" && looked.Appearance.BorderColor == "#33EDE4D3", "Appearance: card defaults round-trip");
        Check(looked.Appearance.Backgrounds[""].Kind == "gradient" && looked.Appearance.Backgrounds[""].Angle == 45 && looked.Appearance.Backgrounds["display\\one"].Fit == "tile" && looked.Appearance.Backgrounds["DISPLAY\\ONE"].Blur == 6 && looked.Appearance.Backgrounds["DISPLAY\\ONE"].Dim == 35 && looked.Appearance.Backgrounds["DISPLAY\\ONE"].ImagePath == @"C:\media\wall.jpg", "Appearance: backgrounds per display round-trip");
        Check(looked.Appearance.BackgroundFor("DISPLAY\\ONE")?.Kind == "image" && looked.Appearance.BackgroundFor("OTHER")?.Kind == "gradient" && looked.Appearance.BackgroundFor(null)?.Kind == "gradient", "Appearance: a display falls back to the all-displays background");
        Check(looked.Items[0].Options["_cardColor"] == "#18334A" && looked.Items[0].Options["_borderColor"] == "#8b5cf6", "Appearance: a widget's own card and border colors round-trip");
        File.WriteAllText(calendarStore.FilePath, """{ "Items": [], "Appearance": { "CardColor": "not a color!", "CardOpacity": 400, "CornerRadius": -9, "TextColor": null, "BorderColor": "#12", "Backgrounds": { "": { "Kind": "NEON", "Color": "zz zz", "Angle": -90, "Fit": "stretch", "Dim": 500, "Blur": -3, "ImagePath": null }, "d2": null, "d3": { "Kind": "Solid", "Color": "#abc" } } } }""");
        var odd2 = calendarStore.Load().Appearance;
        Check(odd2.CardColor == "" && odd2.CardOpacity == 100 && odd2.CornerRadius == 0 && odd2.TextColor == "" && odd2.BorderColor == "", "Appearance: out-of-range or invalid defaults are repaired");
        Check(odd2.Backgrounds[""].Kind == "theme" && odd2.Backgrounds[""].Color == "#1A1611" && odd2.Backgrounds[""].Angle == 270 && odd2.Backgrounds[""].Fit == "fill" && odd2.Backgrounds[""].Dim == 80 && odd2.Backgrounds[""].Blur == 0 && odd2.Backgrounds[""].ImagePath == "" && !odd2.Backgrounds.ContainsKey("d2") && odd2.Backgrounds["d3"].Kind == "solid" && odd2.Backgrounds["d3"].Color == "#abc", "Appearance: odd backgrounds are repaired and null ones dropped");
        File.WriteAllText(calendarStore.FilePath, """{ "Items": [], "Appearance": null }""");
        Check(calendarStore.Load().Appearance.IsDefault, "Appearance: a null block loads as the theme");
    }

    string script = Path.Combine(folder, "child process test.cmd");
    File.WriteAllText(script, "@echo off\r\nstart \"\" /b \"%SystemRoot%\\System32\\cmd.exe\" /d /c \"ping -n 25 127.0.0.1 > nul\"\r\nexit /b 0\r\n");
    using var launcher = new LauncherService();
    var tree = new LauncherItem { Name = "Owned test tree", Target = script };
    var isolated = new LauncherItem { Name = "Separate owned process", Target = Path.Combine(Environment.SystemDirectory, "cmd.exe"), Arguments = "/d /c ping -n 25 127.0.0.1 > nul", WorkingDirectory = folder };
    try
    {
        launcher.Launch(tree);
        launcher.Launch(isolated);
        await Task.Delay(1000);
        Check(launcher.GetStatus(tree.Id) == "Running", "Batch descendants remain tracked after root exits");
        try { launcher.Launch(tree); throw new Exception("Expected duplicate launch failure"); }
        catch (InvalidOperationException) { Console.WriteLine("PASS: Duplicate running launch rejected"); }
        Task treeStopped = WaitForStatus(launcher, tree.Id, "Stopped");
        launcher.Stop(tree.Id, force: true);
        await treeStopped.WaitAsync(TimeSpan.FromSeconds(10));
        Check(launcher.GetStatus(isolated.Id) == "Running", "Force stop leaves another job running");
        Task isolatedStopped = WaitForStatus(launcher, isolated.Id, "Stopped");
        launcher.Stop(isolated.Id, force: true);
        await isolatedStopped.WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine("PASS: Force stop completion is event-driven");

        var fast = new LauncherItem { Target = Path.Combine(Environment.SystemDirectory, "cmd.exe"), Arguments = "/d /c exit 7", WorkingDirectory = folder };
        Task fastExited = WaitForStatus(launcher, fast.Id, "Exited (code 7)");
        launcher.Launch(fast);
        await fastExited.WaitAsync(TimeSpan.FromSeconds(10));
        Console.WriteLine("PASS: Natural process exit reports exit code");
        Check(launcher.GetStatus("unknown") == "Ready", "Unlaunched items report Ready");
    }
    finally
    {
        foreach (var item in new[] { tree, isolated })
            try { launcher.Stop(item.Id, force: true); } catch (InvalidOperationException) { }
    }
    LayoutTests.Run(Check, folder);
    ReminderTests.Run(Check, folder);
    AutomationTests.Run(Check, folder);
    GridSettingsTests.Run(Check, folder);
    ThemeTests.Run(Check, folder);
    LookTests.Run(Check, folder);
    OrganizeTests.Run(Check, folder);
    SimpleModeTests.Run(Check, folder);
    DesktopIconsTests.Run(Check, folder);
    LinkTests.Run(Check, folder);
    FloatExtrasTests.Run(Check, folder);
    HistoryTests.Run(Check, folder);
    await GoogleExtensionsTests.RunAsync(Check, folder);
    Console.WriteLine("All service tests passed. No startup registry values were changed.");
}
finally
{
    string resolved = Path.GetFullPath(folder);
    string expected = Path.GetFullPath(Path.GetTempPath());
    if (!resolved.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("DesktopDeck-ServiceTests-", StringComparison.Ordinal)) throw new InvalidOperationException("Refusing to remove an unexpected test directory.");
    Directory.Delete(resolved, recursive: true);
}

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
}

static Task WaitForStatus(LauncherService launcher, string id, string expected)
{
    var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    EventHandler? changed = null;
    changed = (_, _) =>
    {
        if (launcher.GetStatus(id) == expected)
        {
            launcher.StatusChanged -= changed;
            completion.TrySetResult();
        }
    };
    launcher.StatusChanged += changed;
    changed(null, EventArgs.Empty);
    return completion.Task;
}

static DateTime Local(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
static DateTime FromUtc(int year, int month, int day, int hour, int minute) => DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc), TimeZoneInfo.Local), DateTimeKind.Unspecified);
static string Ics(params string[] lines) { const string nl = "\r\n"; return "BEGIN:VCALENDAR" + nl + "VERSION:2.0" + nl + string.Join(nl, lines) + nl + "END:VCALENDAR" + nl; }
static TimeZoneInfo? ZoneOrNull(string id) => TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null;
static HttpResponseMessage Json(string body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

/// <summary>A stand-in for Google's or a calendar host's servers: answers from a function and records what was asked.</summary>
sealed class FakeHandler(Func<HttpRequestMessage, string, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int calls;
    public int Calls => Volatile.Read(ref calls);
    public List<string> Bodies { get; } = [];
    public List<string> Urls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Bodies) { Bodies.Add(body); Urls.Add(request.RequestUri?.ToString() ?? ""); }
        await Task.Delay(30, cancellationToken); // long enough for simultaneous callers to overlap
        return respond(request, body);
    }
}
