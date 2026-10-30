<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://shieldcn.dev/header/graph.svg?title=TunerFest+Set+Builder&subtitle=Perk+rolling+automation+for+The+Crew+Motorfest&logo=windows&mode=dark&align=center" />
    <img alt="TunerFest Set Builder" src="https://shieldcn.dev/header/graph.svg?title=TunerFest+Set+Builder&subtitle=Perk+rolling+automation+for+The+Crew+Motorfest&logo=windows&mode=light&align=center" />
  </picture>
</p>

<p align="center">
  <img alt="Version 1.6.11" src="https://shieldcn.dev/badge/version-1.6.11-16A34A.svg?variant=secondary&logo=github" />
  <img alt="Platform Windows 10 and 11" src="https://shieldcn.dev/badge/platform-Windows%2010%2F11-0078D4.svg?variant=secondary&logo=windows" />
  <img alt=".NET 8" src="https://shieldcn.dev/badge/.NET-8.0-512BD4.svg?variant=secondary&logo=dotnet" />
  <img alt="UI WPF" src="https://shieldcn.dev/badge/UI-WPF%20%2B%20WPF--UI-7C3AED.svg?variant=secondary" />
  <img alt="Runs fully offline" src="https://shieldcn.dev/badge/data-100%25%20local-F59E0B.svg?variant=secondary&logo=shield" />
</p>

<p align="center">
  A Windows desktop app that re-rolls <b>Legendary part perks</b> in <i>The Crew Motorfest</i> for you.<br/>
  It reads the game screen with OCR and pixel templates, presses the keys, picks the better offer, and stops when both perks are maxed.
</p>

---

## What it does

You tell it which two perks you want on a set of parts. It opens each gold part, rolls the perk slots until the wanted perks appear, then keeps rolling the "same perk" offer until each value hits its maximum. It moves through all seven part tiles on its own, skips gray and pink parts, and ends with both perks on every gold part at 100%.

Everything happens on your machine. No account, no network calls, no uploads. The app only looks at the game window and sends keystrokes or virtual gamepad input to it.

## Highlights

- **Reads the screen, not memory.** Windows OCR plus trained pixel templates for all 31 perk names. Template matches take about 3 ms; OCR is the fallback.
- **Knows the game's math.** Perk values come in ten tiers between a known min and max. Every reading is validated against that grid, so a misread never becomes a wrong click.
- **Survives bad OCR.** When the game wraps the gray `[min%-max%]` bracket to a second line and OCR drops it, the catalog range steps in and the yellow value is isolated by colour.
- **Handles the real seven-parts screen.** Gold, gray and pink tiles are told apart by the coloured bar under the level number. The cursor jumps the game makes over unselectable tiles are expected, not errors.
- **Auto-calibrates zones.** One screen read measures the black bars, finds the 16:9 game area and places every OCR zone. Tested on 3440x1440 ultrawide and standard 16:9.
- **Keyboard or gamepad.** Keystrokes by default, or a virtual Xbox controller through ViGEmBus.
- **Full flight recorder.** Every run writes a session folder: readable log, ordered JSON events, roll ledger with spend in Bucks and spare parts, and optional screenshots with a size budget.
- **Built-in analytics.** A Perk Frequency tab reads every logged run: which perks the game offers, how often, how many rolls and how much currency a part cost on average.
- **Windows 11 look.** WPF with WPF-UI, Mica, system accent, dark and light themes, tray icon, toast notifications, sounds, and an optional overlay panel over the game.

## Quick start

1. Install the **English OCR language** in Windows (Settings, Time & Language, Language, add English and the "Basic typing" and "Optical character recognition" features).
2. Run the game in **English** and leave it in windowed or borderless mode.
3. Download or build the app (see below) and start `Start.cmd`. It launches the newest build listed in `latest-build.txt`.
4. In the game, open a car in **Upgrade**, pick the first Legendary part and stay on the seven-parts screen, the part list from Crafting, the perk screen, or an open roll.
5. In the app, choose the set, the two perks you want, how many parts to process, and press **F8**. Return focus to the game before the five-second countdown ends.

Try it on one part first.

### Hotkeys

| Key | Action |
|---|---|
| **F7** | Read the current screen without pressing anything |
| **F8** | Start or stop the run |
| **F10** | Stop |
| **F9** | Left to the game (Crafting) |

Close older versions of the app before starting: they hold the same hotkeys.

## How a run works

```
seven parts  ─Enter→F9→Enter─►  perk screen  ─roll─►  popup: new / same / keep
     ▲                                │                         │
     └────────── Esc ×3 ──────────────┴── both perks maxed ◄────┘
```

1. **Search.** Roll the free slot until one of the wanted perks is offered. A slot that already holds a wanted perk is locked immediately. An optional second perk is never rolled away.
2. **Maximize.** Keep taking the "same perk" offer while its value is higher than the current one. Stop at the maximum.
3. **Verify.** Re-read the part and confirm both perks are at their maximum before moving on.
4. **Next part.** Back out to the seven tiles, step to the next gold tile, open it. The quick route through the Crafting list is used when it is enabled and confirmed by the category header.

The ledger counts a roll only when the paid popup is confirmed on screen, so the spend shown at the end matches what the game actually took.

## The app pages

| Page | What you set |
|---|---|
| **Build** | Set, two perks (required, optional, or "either of"), number of parts, and the auto-detected set of the current part |
| **Zones** | OCR zones over a reference screenshot, one-click auto calibration, examples of every screen |
| **Control** | Keyboard or gamepad, press timing, extra pause after Esc, quick navigation, fast recognition, screenshot budget |
| **Logs** | Opens the shared `Logs` folder, zips the last run |
| **Notifications** | Toasts, sounds, tray behavior, theme, overlay over the game |
| **Analytics** | Perk frequency across all runs, text log search, completed parts with average cost |

## Build from source

Requirements: Windows 10 19041 or newer, .NET 8 SDK.

```powershell
.\Build.ps1          # runs every offline check, then publishes a self-contained win-x64 build into dist-v<version>
.\Build.ps1 -SkipTests
.\Test.ps1           # offline checks only; never touches a game window
```

The version comes from `SetBuilder.csproj` alone. `Start.cmd` reads `latest-build.txt`, so the launcher never needs editing.

### Offline checks

`Test.ps1` drives the app through fifteen self-test modes with no game present: workflow simulation, focus recovery, grid navigation with skipped tiles, analytics formats, UI smoke run with page screenshots, zone auto-calibration on saved frames, and value crops that previously broke OCR (`Extra Impact 2%`, `Cluster 1.2%`, `Cluster 2%` maxed). Around 290 checks must pass before a build is published.

### Retrain the perk templates

```powershell
& "dist-v1.6.11\TunerFest Set Builder.exe" --train-vision "Logs" "new-model.json"
```

The newest session is held out for validation. The model is saved only when it makes zero wrong name matches and zero wrong numeric readings on that session. Copy the result over `Assets/recognition-templates.json` and rebuild.

## Logs and data

All builds write to one shared `Logs` folder in the project root. Each run gets a timestamped folder:

| File | Content |
|---|---|
| `session.log` | Human-readable messages, flushed live |
| `events.jsonl` | Every frame, OCR result, decision, key press and screenshot link, in order |
| `rolls.csv` / `rolls.jsonl` | One row per confirmed roll with all three offers and the choice |
| `spending.json` / `summary.json` | Cost, outcome, errors, screenshot totals |
| `settings.json` | The exact profile and zones used |
| `screenshots/` | Optional PNGs within the configured budget |

Self-tests go to `Logs/Tests`, calibration evidence to `Logs/Calibration`, analytics exports to `Logs/Analytics`. `Prune-Screenshots.ps1 -Apply` deletes PNGs from old sessions and keeps the text.

## Project layout

```
Automation.cs        Vision (OCR, templates, focus, grid) and Runner (the game driver)
Workflow.cs          Build workflow: search, maximize, verify, advance
GridNavigator.cs     Seven-parts cursor movement with skipped-tile tolerance
Calibration.cs       Zone auto-calibration from one frame
Analytics.cs         Reads every session folder into the Perk Frequency tab
SessionLog.cs        Flight recorder
VisionTemplates.cs   Pixel template matcher; VisionTraining.cs trains it from logs
Ui/                  WPF shell, pages, view models
Assets/              Perk icons, reference screenshots, recognition-templates.json
docs/CHANGELOG-uk.md Detailed version history and design notes (Ukrainian)
```

## Requirements and limits

- Windows 10 build 19041 or newer. The published build bundles .NET.
- The game must run in English; perk names and descriptions are matched against an English catalog.
- The game UI must be visible on screen. Captures are of the game window only.
- Tested with keyboard input and with a ViGEm virtual pad. For the pad, install [ViGEmBus](https://github.com/nefarius/ViGEmBus).

## Disclaimer

This is a personal tool, not affiliated with Ubisoft or Ivory Tower. *The Crew Motorfest* is a trademark of its owner. Automating input may be against the game's terms of use; use it at your own risk and on your own account only.
