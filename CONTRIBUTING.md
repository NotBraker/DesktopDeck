# Contributing to Desktop Deck

Thanks for your interest. Bug reports, fixes, new widgets and documentation improvements are all welcome. For an overview of how the code is organized, see [docs/design.md](docs/design.md).

## Build and test

You need Windows 10 (2004 or later) or Windows 11 and the .NET 10 SDK.

```powershell
dotnet build src/DesktopDeck/DesktopDeck.csproj -c Release

dotnet run --project tests/DesktopDeck.Tests/DesktopDeck.Tests.csproj -c Release
dotnet run --project tests/DesktopDeck.ServiceTests/DesktopDeck.ServiceTests.csproj -c Release
dotnet run --project tests/DesktopDeck.WidgetTests/DesktopDeck.WidgetTests.csproj -c Release

dotnet publish src/DesktopDeck/DesktopDeck.csproj -c Release -r win-x64 --self-contained false -o artifacts/app
```

The suites are console programs: they print `PASS`/`FAIL` lines and exit non-zero on failure. Please run all three before opening a pull request.

Useful switches for checking the UI (run the built `DesktopDeck.exe` with them):

- `--smoke-test` renders a preview to `verification/desktop-deck.png` and exits. Add `--sample` for made-up data. Neither it nor `--preview` touches your real settings.
- `--smoke-test --widgets` validates every plug-in widget and renders sample variants, defaults, minimum sizes and shared appearance settings to `verification/widgets/`. Add `--only <kind>` (for example `--only day-progress`) to render one widget.

The `verification/` and `artifacts/` folders are git-ignored.

## Writing a widget

Widgets are plug-ins that live in the application assembly. Implement `IWidget` and the widget is discovered automatically (by reflection), appears in the **+ Add** gallery, and gets a **Customize...** page generated from its option list. The Day progress widget is the reference example:

- `src/DesktopDeck/Widgets/Time/DayProgressWidget.cs`: the UI
- `src/DesktopDeck/Widgets/Time/DayProgressLogic.cs`: the pure logic
- `tests/DesktopDeck.WidgetTests/DayProgressTests.cs`: its tests

### 1. Put the files in the right place

Widgets are grouped in folders by gallery section under `src/DesktopDeck/Widgets/` (`Time`, `System`, `Productivity`, and so on). Each widget is a pair of files:

- `<Name>Widget.cs`: the class implementing `IWidget`. It may use WPF.
- `<Name>Logic.cs`: parsing, date math, conversions and other rules, with **no WPF**.

Keep this split. The widget test project compiles every `Widgets/**/*Logic.cs` file plus `Models/*.cs` (and nothing else from the app), so a logic file that touches WPF, the network or anything outside those folders will not compile in the tests. Network calls, live Windows samplers and COM wrappers belong in the widget or in a separate service file, not in `*Logic.cs`.

### 2. Write the logic

```csharp
using System;

namespace DesktopDeck.Widgets;

/// <summary>Days until the weekend. Pure logic (unit tested).</summary>
public static class WeekendLogic
{
    public static int DaysUntilSaturday(DateTime now) => ((int)DayOfWeek.Saturday - (int)now.DayOfWeek + 7) % 7;
}
```

### 3. Write the widget

The class is `internal sealed` with a public parameterless constructor (discovery looks for one). Here is a minimal widget in the shape of Day progress:

```csharp
using System.Globalization;
using System.Windows;
using DesktopDeck.Models;

namespace DesktopDeck.Widgets;

internal sealed class WeekendWidget : IWidget
{
    public string Kind => "weekend-countdown";
    public string Name => "Weekend countdown";
    public string Description => "Days until Saturday";
    public string Section => "Time";
    public string Glyph => "";
    public string Accent => "#14b8a6";
    public (double Width, double Height) DefaultSize => (180, 132);
    public (double Width, double Height) MinimumSize => (132, 84);

    public IReadOnlyList<OptionSpec> Options { get; } =
    [
        OptionSpec.Text("todayText", "Text on Saturday", "It's the weekend!", "Behavior"),
        OptionSpec.Toggle("big", "Large number", true, "Layout"),
    ];

    public IEnumerable<Action<LauncherItem>> Samples =>
    [
        _ => { },
        item => item.Options["big"] = "false",
    ];

    public FrameworkElement Build(WidgetContext ctx)
    {
        var (card, body) = WidgetKit.Frame(ctx);
        int days = WeekendLogic.DaysUntilSaturday(DateTime.Now);
        string text = days == 0 ? ctx.Get("todayText") : days.ToString(CultureInfo.CurrentCulture);
        body.Children.Add(WidgetKit.Text(ctx, text, ctx.GetBool("big") ? 36 : 18, bold: true));
        ctx.Tick(() => false, _ => ctx.Rebuild());   // redraw once a minute
        return card;
    }
}
```

What the interface asks for (see `src/DesktopDeck/Widgets/IWidget.cs`):

- `Kind`: unique and stable, 2-40 characters of `a-z`, `0-9` and `-`. It is saved in users' settings, so never change it once released. Invalid or duplicate kinds, or kinds that clash with a built-in one, are skipped and logged to `errors.log`.
- `Section`: one of `WidgetRegistry.Sections` (`Basics`, `Time`, `Planning`, `Study`, `Productivity`, `Connected`, `Info & media`, `Online`, `System`, `Health`, `Fun`, `Visual`, `Tools`). Within a section the gallery sorts by name.
- `DefaultSize` / `MinimumSize`: in DIP. Use `n x 24 - 12` (180, 276, 372, 468...) so widgets tile the 24 DIP grid.
- `Glyph`: a Segoe Fluent Icons code point.
- `Options`: everything the user can customize. `OptionSpec` has factory methods for `Toggle`, `Number`, `Text`, `Choice`, `Color` and `Lines`. For the other types (`LongText`, `Date`, `Time`, `Folder`, `File`) build the record directly, for example `new OptionSpec("due", "Due date", OptionType.Date, "") { Group = "Behavior" }`. Every widget also gets the shared appearance options in `WidgetRegistry.Common` (text size, roundness, card color and opacity, text and border color, spacing) for free.
- `Samples`: made-up data for smoke renders. Each entry is one variant. Samples must not touch the network or real user data.
- Optional members with defaults: `Actions(ctx)` adds right-click entries, and `Create(ctx)` runs when the widget is added from the gallery (return `false` to cancel).

### Where options appear

The Customize window shows a widget's own settings first. `src/DesktopDeck/Widgets/OptionTiers.cs` moves the rest behind collapsed sections, by option group:

- `Behavior` goes behind **More options**.
- `Layout` and `Colors` go behind **Look**.
- Any other group stays in the main part.

Use the group that fits what the setting does. `OptionTiers.AdvancedKeys` lists the exceptions: rarely changed settings that go to **More options** even though their group is in the main part. Nothing is removed or renamed by this, so the setting is still saved and read. `tests/DesktopDeck.WidgetTests/OptionTiersTests.cs` checks the list.

### 4. Use `WidgetContext` and `WidgetKit`

`Build` is called often, on every layer, so it must be quick and must never block.

- **Settings:** `ctx.Get`, `GetBool`, `GetNumber`, `GetInt`, `GetLines` and `GetColor` read options (falling back to the option's default). `ctx.Set(key, value)` saves one and redraws every copy. Values are stored as text in `LauncherItem.Options`.
- **Your own data:** store it in `ctx.Item.Options` (use keys like `data.items`) and call `ctx.Changed()` to save and redraw. Temporary view state that should not be saved goes in `ctx.State` / `ctx.SetState`.
- **Time:** `ctx.Tick(everySecond, tick)` registers one ticker per widget copy: every second while `everySecond()` returns true, otherwise every minute.
- **Slow work:** wrap network or disk work in `ctx.RunOnce(key, async () => ...)`. It runs once at a time per key across all copies, is skipped for snapshots, and the result is typically stored in `ctx.Item.Options` or `WidgetContext.Cache` followed by `ctx.Changed()`. Keep refresh rates bounded, cache the last good result, and back off after failures.
- **Snapshots:** when `ctx.IsSnapshot` is true (smoke renders) use sample or cached data only, start nothing and touch no network.
- **Secrets:** `ctx.GetSecret`, `SetSecret`, `HasSecret`, `RemoveSecret` and `AskSecret` store tokens and passwords encrypted for the Windows user (the name is prefixed with `widget:` automatically). Never put secrets in options.
- **Talking to the user:** `ctx.Ask`, `Confirm`, `Inform`, `Status`, `Notify` and `Open`. The deck cannot take typing directly, so text entry uses small dialogs.
- **UI building blocks:** prefer `WidgetKit` over raw WPF: `Frame` (card plus title header and a body), `Text`, `Muted`, `Pill`, `IconButton`, `Button`, `Empty`, `Row`, `Clickable`, `Scroll`, `Bar`, `Ring`, `Icon` and `Divider`. Colors come from `Theme` (`Theme.Text`, `Theme.Muted`, `Theme.Surface3`, `Theme.Accent(hex)`, ...), so widgets follow the active theme. Scale font sizes with `ctx.Font(size)` so the shared text-size setting works.
- Clicks are used for dragging in Edit layout (`ctx.IsEditing`), so do not rely on them there.

### 5. Add tests

Create `tests/DesktopDeck.WidgetTests/WeekendTests.cs`:

```csharp
using DesktopDeck.Widgets;

public static class WeekendTests
{
    public static void CountsDownToSaturday()
    {
        Expect.Equal(3, WeekendLogic.DaysUntilSaturday(new DateTime(2026, 10, 7)), "Wednesday is 3 days before Saturday");
        Expect.Equal(0, WeekendLogic.DaysUntilSaturday(new DateTime(2026, 10, 10)), "Saturday itself");
    }
}
```

The runner (`tests/DesktopDeck.WidgetTests/Program.cs`) executes every public static, parameterless, `void` method of every class whose name ends in `Tests`. A test fails by throwing; `Expect.True`, `Equal`, `Near` and `Throws<T>` are the assertion helpers. There is no registration step.

### 6. Check how it looks

Build the app and run `DesktopDeck.exe --smoke-test --widgets --only weekend-countdown`, then look at the images in `verification/widgets/`. Add the widget on a real deck and try it at its minimum size and with different appearance settings.

## Other changes

- Match the existing style: file-scoped namespaces, nullable reference types on, small focused files.
- Keep pure rules testable. Logic in `Layout/`, `Models/` and `Services/` is covered by the first two suites; add tests alongside any behavior change there.
- Never commit secrets, tokens, personal data or real account details, in code, tests or screenshots. Sample data must be made up.
- For security issues, do not open a public issue: see [SECURITY.md](SECURITY.md).

By contributing you agree that your work is released under the [MIT License](LICENSE).
