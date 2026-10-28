# Architecture overview

A short map of how Desktop Deck is put together, for contributors. For building, testing and writing a widget, see [CONTRIBUTING.md](../CONTRIBUTING.md).

## What it is

A native Windows WPF app (.NET 10) that places widget surfaces on the desktop layer behind your windows. It has no browser runtime and no telemetry; widgets that talk to online services only fetch once you add and configure them. The app is single-instance, starts at sign-in if you ask it to, and exits from the tray.

## Source layout

| Folder | What lives there |
| --- | --- |
| `src/DesktopDeck/` | The app: hosting and lifecycle (`App.cs`, `DeckHub*.cs`, `NativeDesktopHost.cs`), the deck surface (`MainWindow*.cs`), the control center (`ControlCenter*.cs`), Settings, the control button and toasts |
| `src/DesktopDeck/Layout/` | Portable geometry with no UI dependencies: collision-free placement (`DeckLayout`), display matching (`DisplayMap`) and template placement (`TemplatePlacer`) |
| `src/DesktopDeck/Models/` | Plain data: the saved settings, items, layouts, themes, reminders, automation rules, calendar events |
| `src/DesktopDeck/Services/` | Persistence, secrets, Google and calendar clients, reminders, history and snapshots, launching |
| `src/DesktopDeck/Widgets/` | Plug-in widgets, one `*Widget.cs` (UI) plus `*Logic.cs` (pure logic) per widget, grouped by gallery section |
| `tests/` | Three console test suites (see below) |

## Surfaces

`DeckHub` owns every surface:

- **Background decks:** one per monitor, attached to Explorer's desktop layer and sized to that monitor's work area. When the desktop's DPI differs from the monitor's, the content is scaled to match.
- **Front surfaces:** transparent top-level windows per monitor that exist only while widgets are brought forward (the Ctrl+Alt+D peek, Window mode and Always on top mode). Empty pixels are click-through.
- **Pinned layer:** widgets with **Pin on top** live in their own tool-style, topmost windows that do not take focus and are kept out of Alt+Tab. They have their own saved position, size and monitor.
- **Control button and control center:** the always-visible round button and the tabbed panel that manage everything else.

Items remember a stable monitor id and fall back to the main monitor while theirs is unplugged. Positions are normalized and revalidated after monitor changes.

## Layouts

A layout is a saved arrangement for one layer, pinned or desktop: which widgets are shown, where, and (for desktop layouts) the deck background. While a layout is active, changes to its layer are saved into it. Automation rules, the tray, the control button's menu and optional hotkeys switch layouts.

## Persistence

- Settings are written atomically to `%LOCALAPPDATA%\DesktopDeck\settings.json` with a recovery backup; older files load unchanged, with defaults for missing fields.
- Secrets are kept separately, encrypted with Windows DPAPI (see [SECURITY.md](../SECURITY.md)).
- Pictures and icons you pick are copied into a media folder beside the settings.
- Daily snapshots of `settings.json` back the History tab.

## Widgets

`IWidget` implementations are discovered by reflection inside the application assembly and added to the gallery automatically. This is an internal extension contract, not arbitrary external DLL loading. Pure parsing, date math, rollover, conversion and calculator code lives in `*Logic.cs` files that are compiled into the package-free widget test harness; anything that touches WPF, the network or live Windows samplers stays in the widget class or a separate service.

## Launching programs

Programs and batch files started from a launcher are tracked in a Windows job object so that Stop closes only what the deck started, including descendants that remain in the job. Force stop terminates the owned job. Shortcuts and documents opened through the shell are explicitly untracked.

## Testing

| Suite | Covers |
| --- | --- |
| `tests/DesktopDeck.Tests` | Layout geometry and display matching, including property tests |
| `tests/DesktopDeck.ServiceTests` | Settings, layouts, themes, reminders, automation, history, Google helpers |
| `tests/DesktopDeck.WidgetTests` | The pure logic of every widget |

The app itself can render images of its UI for visual checks (`--smoke-test`, with `--sample` for made-up data) and verify desktop hosting on a real machine (`--host-test`).
