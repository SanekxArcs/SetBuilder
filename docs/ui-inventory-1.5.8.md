# TunerFest Set Builder: WinForms UI inventory (v1.5.8) for the WPF rewrite

Repo root: `C:\Users\Sanekx Arcs\Documents\Repos\personal\crew screenshot maker\SetBuilder\`. Every path below is relative to it, and `MF` means `MainForm.cs`.

UI files: `MainForm.cs` (2193 lines: `ZonePicker` at 3–109, `MainForm` at 111–2193), `AnalyticsPanel.cs`, `Shell.cs`, `Fluent.cs`, `Controls.cs`, `Program.cs`.

There is **no separate Settings class**. Every persisted setting is a property of `Profile` (Models.cs:73–179). It is saved as JSON to `%LOCALAPPDATA%\TunerFestSetBuilder\profile.json` (`Profile.Folder`/`PathName`, Models.cs:160–162). `Profile.Load()` fills in any missing default zones. In preview/smoke mode the profile is `new()` and `Save()` is never called (every save is guarded by `if (!smoke)`).

---

## 1. Window shell

### Window and title
- **Title:** `Shell.WindowTitlePrefix + " " + AppVersion`, which gives `"TunerFest · Set Builder 1.5.8"`. The prefix is the const `"TunerFest · Set Builder"` (Shell.cs:10). `AppVersion` is the assembly version formatted with `ToString(3)` (MF:114). The version comes only from `<Version>` in SetBuilder.csproj (currently 1.5.8).
- **Size and placement:** client size 1180×940, minimum 960×700, `CenterScreen`.
- **Look:** background `Bg`, foreground white. Font is `Shell.Family("Segoe UI Variable Text","Segoe UI")` at 10 pt. `AutoScaleMode.Dpi` (MF:265–276).
- **Icon:** `Shell.AppIcon`, loaded from `Assets\app.ico` (Shell.cs:39–52).
- **Frame:** on Shown, `Fluent.Window(this)` (Fluent.cs:34–50) sets these DWM attributes: dark title bar (20=1), rounded corners (33=2), caption colour (35)=Background, border colour (34)=Border, caption text colour (36)=White.
- **Scrollbars:** `Shell.DarkScrollbars(this)` (MF:622, Shell.cs:55–82) applies `SetWindowTheme("DarkMode_Explorer")` to every scrollable control, recursively, and also to controls added later.
- **Root layout:** `TableLayoutPanel` with padding 24 and 4 rows: header (86 px), tabs (100%), action row (60 px), status line (30 px) (MF:277–288).

### Header (MF:289–313)
- Label `"Set Builder"`: font `Shell.Family("Segoe UI Variable Display","Segoe UI")`, 27 pt bold, white, at (0,0).
- Label `"TUNERFEST     /     Два перки · сім деталей · один готовий сет"` (five spaces on each side of the `/`): muted colour, at (2,49).
- `StatusCard runStatus`: docked right, width 560, uses the form font.

### StatusCard (Shell.cs:138–291)
- **Modes** (enum `Mode`): `Idle, Countdown, Running, Paused, Done, Stopped, Failed`.
- **Initial text:** title `"Очікування"`, detail `"Вибери сет, два перки й вікно гри · F8 — запуск"`.
- **`Set(Mode, heading, text)` (186–214):**
  - Countdown, Running or Paused when not yet started: starts the clock (`started = now`).
  - Entering Paused records when the pause began; leaving Paused adds that time to `pausedTotal`.
  - Done, Stopped, Failed and Idle freeze the elapsed time (Idle resets it to 0) and stop the timer.
  - All other modes keep the 500 ms `System.Windows.Forms.Timer` running, which repaints the card.
  - `AccessibleName = heading + ". " + text`.
- **`Current`:** the current mode. **`Elapsed`:** time since start minus paused time (172–184).
- **Painting:**
  - Rounded card (radius 10 px, DPI-scaled), fill `Fluent.Surface`, border `Fluent.Border`.
  - 10 px status dot. Dot colour: Countdown/Running = Accent; Paused = (255,167,38); Done = (134,218,164); Failed = (255,107,107); Stopped = Muted; Idle = Border.
  - Bold white title next to the dot. Muted, word-wrapped detail text below it.
  - Clock on the right, font size +5 bold. Format `mm:ss`, or `h:mm:ss` once past 1 hour. Hidden in Idle. The clock is drawn in the dot colour while Paused, white otherwise.

### Tabs
`TabHost` (Controls.cs:282–341). Pages in this order (MF:316–321):
0. `"Збірка"`
1. `"Зони екрана"`
2. `"Керування"`
3. `"Журнали"`
4. `"Сповіщення"`
5. `"Аналітика"` (contains `new AnalyticsPanel(catalog)`)

### Bottom action row (MF:564–582)
A `FlowLayoutPanel`, left to right, top padding 10.

| # | Label | Action | Disabled during run? |
|---|---|---|---|
| 1 | `"Почати · F8"` (primary: BackColor=Accent, ForeColor=Bg, width 180) | `Arm(true)` | **Yes**. It is in `settingsControls`. While busy, F8 calls `Arm`, which cancels the run. |
| 2 | `"Зупинити · F10"` (width 180) | `Cancel` | Inverse: `Enabled=false` at idle and `true` only during a run or inspect. `Button()` skips adding this exact text to `settingsControls` (MF:809). |
| 3 | `"Відкрити Logs"` | `OpenLogs` (MF:1470) | No (removed from list) |
| 4 | `"ZIP останнього запуску"` | `ExportLogs` (MF:1485) | **Yes** |
| 5 | `"tunerfest.app/sets ↗"` | `OpenSite(SetsUrl)` | No (removed) |
| 6 | `"Підтримка ↗"` | `OpenSite(SupportUrl)` | No (removed) |

- `SetsUrl = "https://tunerfest.app/sets"`, `SupportUrl = "https://tunerfest.app/support"` (MF:1453–1454).
- `OpenSite` (MF:1456) calls `Process.Start(UseShellExecute)`. On error it logs `"Не вдалося відкрити {url}: {msg}"`.

**How `settingsControls` works (MF:195):**
- The helpers `Button()`, `Combo()` and `Numeric()` add their control automatically. Toggles and key fields are added explicitly.
- Controls that should stay clickable during a run are removed with `settingsControls.Remove(...)`.
- At run start, `Arm`/`InspectFile` set `Enabled=false` on every listed control.
- In `finally` they set `Enabled=true` on all of them, then call `SyncTimingControls()`, `SyncTargetControls()` and `SyncAudio()` to re-apply the dependent disables.
- Controls that are **not** in the list stay enabled: the site/support buttons (both rows), `"Відкрити Logs"`, `"Відкрити папку Logs"`, `"Копіювати"`, `"Очистити екран"`, `A−`/`A+`, the `followLog` toggle, the events box, and everything in AnalyticsPanel.

### Status line (MF:175–179, 583–584)
- A `Label` with muted colour. Initial text: `"Готовий до налаштування · F8 запуск · F7 зчитати · F10 стоп"`.
- Every `Log(text)` call (MF:1898) also sets `status.Text`. Text longer than 125 chars is cut to the first 122 chars plus `"…"`.

### Tray icon
**`CreateTray` (MF:692–712)** runs on Shown, but not in smoke mode.
- `NotifyIcon`: icon `Shell.AppIcon ?? Icon`, text `"TunerFest · Set Builder"`, visible.
- Context menu: dark (`Fluent.Surface`/white), no image margin. Items:
  - `"Показати вікно"` → `Shell.Activate(Handle)`
  - `"Зупинити · F10"` → `Cancel()`
  - separator
  - `"Вийти"` → `Close()`. This goes through `FormClosing`, so it is refused while busy.
- Double-click → `Shell.Activate(Handle)`.
- There is no minimize-to-tray or hide-on-close behaviour.
- `tray?.Dispose()` runs in `Dispose`.

**`SetTrayText(text)` (MF:714–720):** sets the tray text to `"Set Builder · " + text`, cut to 62 chars plus `"…"` when longer than 63. Values used:
- `"старт"` (Arm)
- `"пауза, гра без фокусу"`
- `"деталь {p}/{n}, кидків {r}"`
- `"готово"`, `"зупинено"`, `"помилка"`

### Toasts (Shell.cs:105–135)
- `Toasts.Show(title, text, tag)` uses `Microsoft.Toolkit.Uwp.Notifications` `ToastContentBuilder` (two text lines, group `"setbuilder"`). It is skipped when `Toasts.Enabled == false`.
- `Toasts.Remove(tag)` removes the toast from `ToastNotificationManagerCompat.History`.
- All toast calls swallow exceptions.

When toasts fire:
- `OnProgress` with state `"paused"`: `Show("Set Builder на паузі", "Гра втратила фокус. Поверни фокус у гру, щоб продовжити.", "state")` (MF:734).
- `OnProgress` with `"running"` right after a pause: `Remove("state")` (MF:737–738).
- `ShowOutcome`:
  - Always `Remove("state")` first.
  - `"completed"`: `Show("Сет готовий", $"{profile.Set}: {profile.First} + {profile.Second}. {spent}.", "result")`.
  - Error: `Show("Set Builder зупинено", reason, "result")`.
  - `"cancelled"`: no toast.

`Toasts.Enabled` is set to `false` in the constructor when smoke. It is set to `profile.ToastEnabled && !smoke` at the start of `Arm` (MF:1934) and when the toggle changes (MF:1372). At startup it is not synced from the profile; it defaults to `true`.

Program.cs:68 accepts `-ToastActivated` arguments, so clicking a toast launches the app normally, and the single-instance logic then activates the existing window.

### Window bounds persistence
- **`RestoreWindowBounds` (MF:658–676)**, on Shown, not in smoke:
  - Reads `profile.WindowBounds` (`int[]? [x,y,w,h]`, default null).
  - Applies them only if they overlap the `WorkingArea` of some screen by more than 200×120 and are at least the minimum window size.
  - If `profile.WindowMaximized` (bool, default false), sets `WindowState=Maximized`.
- **`SaveWindowBounds` (MF:678–690)**, from `FormClosing` when not busy:
  - Saves `Bounds`, or `RestoreBounds` when not in the Normal state, plus `WindowMaximized`.
  - Calls `profile.Save()` and swallows IO/UnauthorizedAccess errors.
- Storage: the profile.json file described above.

### FormClosing (MF:645–655)
If busy: calls `Cancel()`, sets `e.Cancel = true` and logs `"Зупиняю дію. Закрийте вікно ще раз після зупинки."`. Otherwise it calls `SaveWindowBounds()`.

### Single instance (Program.cs:128–133, Shell.cs:84–101)
- Uses `Mutex(true, @"Local\TunerFestSetBuilder")`.
- If the mutex is already held, `Shell.ActivateExisting()` finds the first top-level window whose title starts with `"TunerFest · Set Builder"` and calls `Shell.Activate`. `Activate` uses `ShowWindow(9)` if the window is iconic, then `SetForegroundWindow`.
- The WPF window title must keep this prefix for activation to work.

### Global hotkeys
**Registration (MF:632–642, on Shown, not smoke):**
- `Desktop.RegisterHotKey(Handle, id, 0x4000 /*MOD_NOREPEAT*/, vk)`.
- id 8 = F8, id 9 = F7, id 10 = F10.
- `stopAvailable` = whether F10 registered. `hotkeys` = whether all three registered.
- If any registration failed, it logs `"Частина гарячих клавіш зайнята іншою програмою. Кнопки працюють; запуск має 5 секунд відліку."`.

**`WndProc` (MF:2160–2178)** handles `WM_HOTKEY` (0x312):
- wParam 8 → `Arm(true)` (start, or cancel if busy)
- wParam 9 → `Arm(false)` (read-only screen read, or cancel if busy)
- wParam 10 → `Cancel()`

Unregistered in `Dispose` (MF:2180–2192).

After registration, Shown also logs `"OCR Windows: " + string.Join(", ", Vision.Languages())` (MF:643).

### DPI
- `app.manifest` is in the parent folder (`..\app.manifest`): `PerMonitorV2`, `longPathAware`.
- `Shell.Px(Control c, int logical) = round(logical * c.DeviceDpi / 96)` (Shell.cs:25). Custom-painted controls use it for track and knob sizes, number-box button zones, dropdown item height and icon sizes (22 px items).
- The form uses `AutoScaleMode.Dpi`.
- `Shell.Family(preferred, fallback)` checks whether the font family is installed.

---

## 2. Tabs

### Shared building helpers
| Helper | Line | What it does |
|---|---|---|
| `Stack()` | MF:743 | `FlowLayoutPanel` TopDown, no wrap, AutoScroll, padding 5, BackColor=Card |
| `AddLabel(c, text, muted=false)` | MF:756 | `FluentText`, width 405, margin (3,8,3,3); white, or Muted if `muted` |
| `Combo(c, DarkSelect, data)` | MF:769 | width 340, items added, `SelectedIndex=0` if any; **adds to `settingsControls`** |
| `Numeric(c, FluentNumber, value)` | MF:782 | width 150, value clamped; **adds to `settingsControls`** |
| `Button(c, text, action)` | MF:792 | `FluentButton`, AutoSize, height 34, min (120,34), margin (3,6,8,3); **added to `settingsControls` unless text is `"Зупинити · F10"`** |
| `AddInfo(c, FluentText)` | MF:814 | width 400, left margin 45, muted, initially hidden |

### Tab 0 "Збірка" (build) — MF:324–498

**Layout:**
- The tab holds a horizontal `SplitContainer mainSplit`: SplitterWidth 10, Panel1MinSize 130, Panel2MinSize 130, initial SplitterDistance 390, `FixedPanel=Panel2`.
- Panel1 holds a 2-column table (56% / 44%, padding 12): `left` Stack (also stored as `settingsStack`) and `right` Stack.
- Panel2 holds the log panel (see "Log panel" below).
- `mainSplit.SplitterMoved` (MF:346) sets `profile.LogPanelHeight = Panel2.Height` and saves, except while restoring the layout or when the form is not visible.
- `mainSplit.Paint` (MF:354) draws a 44 px muted grip line in the middle of the splitter.

**Left column, in order:**
1. AddLabel `"ПРОФІЛЬ ІЗ TUNERFEST"` (muted).
2. GoalRow `builds` (MF:850). The row is a 32×32 icon `iconBuild` followed by a `DarkSelect`, width 340.
   - Items: `new Build{Title="Власна збірка"}` (Set = ""), then `catalog.Builds`.
   - Not persisted. At startup it selects the first catalog build where `b.Set==profile.Set && b.Affixes == [profile.First, profile.Second]`, otherwise item 0 (MF:610–613).
3. AddInfo `buildInfo`.
4. `siteRow` (horizontal) with two buttons, both removed from `settingsControls` so they stay enabled during a run:
   - `"Сети та інструкції на сайті ↗"` → `OpenSite(SetsUrl)`
   - `"Підтримка ↗"` → `OpenSite(SupportUrl)`
5. FluentCheck `autoSet`: `"Автоматично визначати сет і пару перків"`.
   - Bound to `profile.AutoSet` (bool, default false).
   - On change: `SyncTargetControls()` only, no save. Persisted at start by `SaveSelection`.
   - In `settingsControls`.
6. AddLabel `"Сет деталі"`.
7. GoalRow `sets` with `iconSet`.
   - Items: `catalog.Sets` (`SetBonus`).
   - Initial value from `profile.Set` (string, default `"NITRO CHEMIST"`). Persisted at start.
8. AddInfo `setInfo`.
9. AddLabel `"Перший перк"`.
10. GoalRow `first` with `iconFirst`, combo width 300, plus FluentCheck `firstOptional`.
    - `first` items: `catalog.Affixes`. Initial value from `profile.First` (default `"Pure"`). Persisted at start.
    - `firstOptional`: text `"Необов'язковий"`, AccessibleName `"Необов'язковий перк"`. Bound to `profile.FirstOptional` (bool, default false). **Saves on change** (MF:416).
11. AddInfo `firstInfo`.
12. AlternativeRow `firstAlt` with `iconFirstAlt` (MF:823).
    - Muted label `"або"`, then a 32 px icon, then a `DarkSelect` (width 300, height 32).
    - Items: `NoAlternative` (an Affix with Title `"— без альтернативи —"`, MF:174), then `catalog.Affixes`.
    - Initial value: the affix matching `profile.FirstAlternative` (string, default ""), otherwise `NoAlternative`. Persisted at start (empty when index 0).
13. AddInfo `firstAltInfo`.
14. AddLabel `"Другий перк"`.
15. GoalRow `second` with `iconSecond` and FluentCheck `secondOptional`. Same as `first`, bound to `profile.Second` (default `"Extra Pump"`) and `profile.SecondOptional` (saves on change, MF:422).
16. AddInfo `secondInfo`.
17. AlternativeRow `secondAlt` with `iconSecondAlt`, bound to `profile.SecondAlternative`.
18. AddInfo `secondAltInfo`.
19. AddLabel (muted): `"Будь-який порядок слотів. «або» — беремо той із двох, що випав першим, і доводимо до максимуму. Необов'язковий не шукається й не покращується."`

**Right column, in order:**
1. AddLabel `"ВІКНО ГРИ"` (muted).
2. `windowRow`:
   - `DarkSelect windows`, width 285. Items are `GameWindow` records (`ToString` = Title). Not persisted.
   - Button `"↻"` (38 px wide, min 35×30) → `RefreshWindows` (MF:981). It reloads `Desktop.Windows()`, keeping only windows that are not this form and pass `Desktop.IsGame(h)` (process name contains "Motorfest"). It keeps the previous handle if still present, otherwise picks the first window whose title contains "Motorfest" (case-insensitive). It is also called once at startup (MF:621).
3. AddLabel `"Деталей за запуск"`.
4. `partRow` with 7 `FluentButton`s, texts `"1"`..`"7"`:
   - 45×36, AccessibleName `"Обробити {n} деталей"`, in `settingsControls`.
   - Click: sets `selectedPartCount`, sets `profile.Parts` (int, default 7, clamped 1..7 at load), calls `SyncPartCount()` and **saves**.
   - **`SyncPartCount` (MF:1568):** the selected button gets BackColor=Accent (FluentButton then draws it as primary with dark text); the others get `Fluent.Input`. AccessibleDescription is `"Обрано"` on the selected button, "" on the others.
5. AddLabel `"ПЕРЕВІРКА ПЕРЕД СТАРТОМ"` (muted).
6. AddLabel: `"Відкрий екран семи деталей або перків.\nПеревір зони й поверни фокус у гру.\nF8 — запуск · F7 — зчитати · F10 — стоп."`
7. Button `"Зчитати екран без натискань"` → `Arm(false)`. This is the same as F7: a read-only capture of the game window followed by `Inspect`.
8. Button `"Перевірити PNG зі скріншота"` → `OpenFileDialog{Filter="PNG screenshots|*.png"}`, then `await InspectFile(path)` (MF:2047).

**Icon and description behaviour:**
- **Wiring:** `SelectedIndexChanged` on `first`, `second`, `sets`, `firstAlt` and `secondAlt` calls `ShowGoal(icon, info, SelectedItem)` (MF:585–589). All six combos get `ItemImage = item => IconFor(IconTitle(item), Shell.Px(this, 22))`, so the open list shows icons (MF:590–591).
- **`ShowGoal` (MF:882):** `box.Image = IconFor(IconTitle(item), 32)`; `info.Text = Describe(item)`; `info.Visible = text != ""`.
- **`IconTitle` (MF:890):**
  - Build with Set "" → null.
  - Build → its `Set` (a build shows its set's icon).
  - `NoAlternative` or null → null.
  - Otherwise `item.ToString()`.
- **`Describe` (internal static, MF:901–932):**
  - Formatting rules: numbers use format `"0.##"` with invariant culture. Description newlines are joined with `" · "`. Tags are appended as `" · tag1, tag2"`.
  - Build with Set "" → `"Сет і перки обираються нижче вручну."`
  - Build → cleaned Description, or `"{Set}: {A + B}"` if empty, plus tags.
  - SetBonus → `"{desc} · 3 деталі: +{three}% · повний сет: +{full}%"` plus tags.
  - Affix that is `NoAlternative` or has an empty description → "".
  - Affix → `"{desc} · {from}–{to}%"` plus tags. Example: `"Increase GEARSHIFTING BOOST EFFECT in DRAG RACE events · 1.2–2% · speed"`.
- **`IconFor(title, size)` (MF:935–974):**
  - Cache key `title@size`.
  - File name: the title with non-alphanumeric chars removed, lowercased, e.g. `Assets\nitrochemist.svg`.
  - SVG rendering uses the `Svg` NuGet package at the given size. Every pixel is recoloured to Accent, keeping its alpha.
  - Fallback is `.png` (only `lootdigger.png` exists), used without recolouring.
  - Errors produce a null icon.

**Profile → set/perk auto-fill (`builds.SelectedIndexChanged`, MF:592–606):**
- Always calls `ShowGoal(iconBuild, buildInfo, ...)`.
- If the item is a `Build` with a non-empty Set: selects the matching set by Title.
- If the build has exactly 2 affixes: also selects `first`/`second` by Title and resets both alternatives to `NoAlternative`.
- This only changes the combo selections; nothing is saved until start.

**`SyncTargetControls` (MF:1587):** `sets, first, second, builds, firstAlt, secondAlt` get `Enabled = !busy && !autoSet.Checked`. The optional toggles are not affected.

**`AutoSetApplied(Build choice)` (MF:1593):**
- Callback from `Runner` (and from `Inspect`) when auto mode detects the set.
- Selects the set (matching with `Recognition.Normalize`), `first`/`second` from `choice.Affixes[0..1]`, both alternatives = `NoAlternative`, `profile.FirstAlternative = SecondAlternative = ""`, `builds.SelectedItem = choice`.
- Calls `SyncTargetControls()` and **saves**.

**`SaveSelection` (MF:1817–1859).** Called at the start of `Arm` and `InspectFile`. It copies every UI value into the profile, validates, then saves.
- Fields copied: AutoSet, Set, First, Second, FirstOptional, SecondOptional, FirstAlternative, SecondAlternative (`""` when index 0), Parts, FastRecognition, FastRolls, Delay, ExitPauseMs, RandomPause, PauseMinMs, PauseMaxMs, MaxRolls, FocusReadAttempts, FocusRetryMs, FocusLostWaitSeconds, `Xbox = backend.SelectedIndex==1`, `XboxRecalibrate = PadChoice.Code ?? "KeyboardF9"`, DiagnosticsEnabled, DiagnosticScreenshots, DiagnosticMaxMb.
- Validation throws `InvalidOperationException` with these messages:
  - Both optional: `"Принаймні один перк має бути обов'язковим."`
  - Not auto and any duplicate among first/firstAlt/second/secondAlt: `"Перки та альтернативи мають бути різними."`
  - `PressTiming.Validate(profile)`, which can throw:
    - `"Додаткова пауза Esc / B має бути від 0 до 5000 мс."`
    - `"Пауза: мінімум від 50 мс, максимум до 5000 мс; мінімум не може перевищувати максимум."`
    - `"Фіксована пауза має бути від 50 до 5000 мс."`
  - Not auto and (empty set or perk, or first == second): `"Оберіть сет і два різні перки."`

### Log panel at the bottom of "Збірка" (MF:500–559)
**Layout:** a `TableLayoutPanel` with padding 8 and BackColor=Bg. Row 0 (48 px) is the tool row; row 1 is `events`.

**Tool row, left to right:**
1. Muted label `"ЖУРНАЛ ЗАПУСКУ"`.
2. FluentCheck `followLog`: `"Стежити"`, checked by default, not persisted, not in `settingsControls`.
3. Button `"Копіювати"`: if the log has text, `Clipboard.SetText(events.Text)`. Removed from `settingsControls`.
4. Button `"Очистити екран"`: `events.Clear()`. Removed.
5. Buttons `"A−"` and `"A+"` (42×32): `profile.LogFontSize = clamp(LogFontSize ± 1, 9, 18)`; `events.Font = new Font("Segoe UI Variable Text", size)`; **save**. Removed.
   - `profile.LogFontSize`: int, default 11.
   - The `"A−"` label uses U+2212 (minus sign), not a hyphen.

**`events`:**
- `RichTextBox`: ReadOnly, vertical scrollbar, WordWrap, `DetectUrls=false`.
- BackColor=Bg, ForeColor=Muted, no border.
- Initial font: `"Segoe UI Variable Text"` at `clamp(LogFontSize, 9, 18)`.

**`RestoreLogLayout` (MF:1553–1566):**
- Runs on Shown, including in smoke mode. Sets `restoringLogLayout` so `SplitterMoved` does not save.
- `available = splitHeight - splitterWidth`.
- `needed = settingsStack.GetPreferredSize().Height + 40`.
- If `available >= min1 + min2`: `SplitterDistance = clamp(max(available - profile.LogPanelHeight, min(needed, available - 130)), 130, available - 130)`.
- `profile.LogPanelHeight`: int, default 220.
- Net effect: the settings column gets at least the height it needs, and the log gets the rest.

**`AppendLog(text)` (MF:1861–1896)** writes to `events` only.
- It saves the user's current selection first.
- Category (`kind`), checked in this order:
  1. Starts with `"✓"` or `"Готово"` → `ГОТОВО`, colour (134,218,164)
  2. Contains `"Зупинено"` or `"Помилка"` → `ЗУПИНКА`, colour (255,159,139)
  3. Starts with `"Кидок"` → `КИДОК`, colour Accent
  4. Starts with `"Збережено:"` → `РЕЗУЛЬТАТ`, colour (203,208,218)
  5. Contains `"знайдено у слоті"` → `ПЕРК`, colour Accent
  6. Otherwise → `ПОДІЯ`, colour (203,208,218)
- Each line is written as:
  1. `"HH:mm:ss   "` (3 spaces after) in colour (124,137,156)
  2. `kind + "  ·  "` in bold, in the category colour
  3. `text + NewLine` in regular font, in the category colour
- If `followLog` is checked and the user had no selection → `ScrollToCaret()`. Otherwise the previous selection is restored.

**`Log(text)` (MF:1898):** `recording?.Message(text)` (SessionLog), then `AppendLog(text)`, then updates the status line (truncated as described in section 1).

### Tab 1 "Зони екрана" (calibration) — `BuildCalibration` MF:995–1055
Controls in order:
1. AddLabel (white): `"Базові зони відповідають твоїм скрінам 3440×1440. Для іншого HUD виділи їх вручну."`
2. AddLabel (muted): `"Основний екран: 2 перки, назва сету та заголовок деталі. Меню кидка: верхній і середній блоки для кожного слота. Нижній збігається з основним перком."`
3. 11 rows, each 850×44. Each row has a Button with the label (→ `Pick(key, label)`) and a muted Label `zoneLabels[key]` (margin 15,14).

| key | Button label | Default zone (X, Y, W, H as fractions of the game client area) |
|---|---|---|
| `left` | `"Лівий перк"` | .190, .738, .216, .145 |
| `right` | `"Правий перк"` | .405, .738, .216, .145 |
| `set` | `"Назва сету"` | .690, .751, .145, .044 |
| `part` | `"Заголовок деталі"` | .460, .202, .105, .040 |
| `topLeft` | `"Лівий: новий перк зверху"` | .190, .455, .216, .143 |
| `middleLeft` | `"Лівий: повторний кидок посередині"` | .190, .598, .216, .140 |
| `topRight` | `"Правий: новий перк зверху"` | .405, .455, .216, .143 |
| `middleRight` | `"Правий: повторний кидок посередині"` | .405, .598, .216, .140 |
| `parts` | `"Ряд усіх семи деталей"` | .1855, .707, .672, .175 |
| `breadcrumb` | `"Шлях меню вгорі"` | .184, .036, .33, .05 |
| `inventoryHeader` | `"Заголовок списку, напр. BRAKES"` | .185, .663, .22, .044 |

Defaults come from `Profile.Defaults()` (Models.cs:144–158). Data is stored in `profile.Zones` (`Dictionary<string, Zone>`), where `Zone(double X, Y, W, H)` are fractions of the game client area.

4. Button `"Відновити зони зі скріншотів"`: `profile.Zones = Profile.Defaults()`, `UpdateZoneLabels()`, **save**.
5. AddLabel (muted): `"Виділяй блок разом із верхньою рамкою, іконкою, назвою та описом із [мін%–макс%]. Ціну знизу не включай. Зони зберігаються відносно вікна гри."`

**`UpdateZoneLabels` (MF:1057):** each label shows `"{X:P1} / {Y:P1} · {W:P1} × {H:P1}"`, or `"Не задано"` if the zone is missing.

**`Pick(key, label)` (async void, MF:1066–1096):**
1. Returns if busy.
2. If no game window is selected, logs `"Вибери вікно гри."` and returns.
3. `Hide()` the main form and wait 300 ms.
4. `new ZonePicker(Desktop.Bounds(game.Handle), label).ShowDialog()`.
5. On OK: `profile.Zones[key] = Selection`, **save**, `UpdateZoneLabels`.
6. Exceptions are logged. `finally` calls `Show()`.

**ZonePicker form (MF:3–109):**
- Borderless, TopMost, manual position exactly over the game client rectangle, cross cursor.
- Takes a screen capture of that rectangle as its background.
- Draws a black shade with alpha 115 over everything.
- While dragging with the left mouse button, the selected rectangle shows the unshaded background with a 3 px border in (255,244,35).
- Top-left black box (16,16, width up to 1000, height 80) with text in Segoe UI 14 bold, white: `label + "\nВиділіть увесь блок разом із верхньою жовтою межею · Esc — скасувати"`.
- Mouse-up with a selection of at least 16×16 → `Selection = new Zone(x/W, y/H, w/W, h/H)` and closes with OK. Smaller selections are ignored.
- Esc closes with Cancel.

### Tab 2 "Керування" (controls) — `BuildControls` MF:1098–1317
Two 50/50 columns (padding 16), each a Stack.

**Left column (`stack`):**
1. AddLabel `"НАТИСКАННЯ ТА ШВИДКІСТЬ"` (muted).
2. AddLabel `"Спосіб натискань"`.
3. Combo `backend`: `["Клавіатура", "Віртуальний Xbox · ViGEmBus"]` (plain strings).
   - Index = `profile.Xbox ? 1 : 0` (`profile.Xbox`: bool, default false). Saved at start.
4. AddLabel (muted): `"Xbox надсилає A / B / D-pad. Потрібен ViGEmBus; драйвер автоматично не встановлюється. Фізичний геймпад можна використовувати в обох режимах."`
5. AddLabel (muted): `"Кожен кидок: 1 500 Bucks + 150 запчастин. Підсумок і всі пропозиції зберігаються у Logs."`
6. FluentCheck `fastRecognition`: `"Швидкі назви з навчених шаблонів і кеш OCR"`.
   - `profile.FastRecognition`: bool, default true. Saved at start.
7. AddLabel `"Додаткова пауза після кожного Esc / B, мс"`.
8. FluentNumber `exitPause` (0..5000, step 50).
   - `profile.ExitPauseMs`: int, default 450. **Saves on ValueChanged** (MF:1141).
9. AddLabel (muted): `"Для виходу Esc ×2 або ×3. Додається до звичайної паузи; 0 — без додаткового очікування."`
10. AddLabel `"Паузи між натисканнями"`.
11. FluentCheck `randomPause`: `"Випадкова пауза в діапазоні"`.
    - `profile.RandomPause`: bool, default true. On change: `SyncTimingControls()`. Saved at start.
12. `timingRow`:
    - Label `"Від, мс"` (width 70), then FluentNumber `pauseMin` (50..5000, step 10, width 100). Bound to `profile.PauseMinMs` (default 50).
    - Label `"До, мс"`, then FluentNumber `pauseMax` (50..5000, step 10, width 100). Bound to `profile.PauseMaxMs` (default 500).
    - Both saved at start.
13. AddLabel `"Фіксована пауза, мс (якщо випадкову вимкнено)"`.
14. FluentNumber `delay` (50..5000, step 50). `profile.Delay`: int, default 450. Saved at start.
15. AddLabel (muted): `"Діапазон 50–500 мс увімкнений спочатку. Для кожного натискання вибирається нова пауза; тривалість утримання кнопки — 65 мс. Перевірки екрана можуть додати час очікування."`

**`SyncTimingControls` (MF:1811):** `pauseMin` and `pauseMax` are enabled when `!busy && randomPause`; `delay` is enabled when `!busy && !randomPause`.

**Right column (`navigation`):**
1. AddLabel `"НАДІЙНІСТЬ ТА НАВІГАЦІЯ"` (muted).
2. AddLabel `"Маршрут між деталями"`.
3. Combo `partRoute`: `PadChoice("classic","Класичний · через сім деталей")`, `PadChoice("quick","Швидкий · через список деталей")`.
   - Index = `profile.FastPartNavigation ? 1 : 0` (bool, default false). **Saves on change** (MF:1204).
4. AddLabel (muted): `"Швидкий: Esc ×2 → вниз → Crafting → Enter. Якщо перехід не підтвердиться, застосунок повернеться до класичного маршруту."`
5. FluentCheck `fastRolls`: `"Швидкі кидки: відповідь на меню за позицією"`.
   - `profile.FastRolls`: bool, default true. **Saves on change** (MF:1222).
6. AddLabel (muted): `"Пошук: Enter → Up одразу під час появи меню → Enter, щойно рамка на верхньому рядку. Покращення: Enter → Enter на середньому рядку. Меню не читається; результат береться з картки слота. Якщо рамка не підтвердиться, меню читається повністю, як раніше."`
7. AddLabel `"Ліміт кидків на весь запуск"`, then FluentNumber `limit` (1..10000, step 50).
   - `profile.MaxRolls`: default 500. Saved at start.
8. AddLabel `"Спроби зчитування рамки (без нових кидків)"`, then FluentNumber `focusAttempts` (1..100, step 1).
   - `profile.FocusReadAttempts`: default 12. Saved at start.
9. AddLabel `"Пауза між повторними зчитуваннями, мс"`, then FluentNumber `focusRetry` (10..1000, step 10).
   - `profile.FocusRetryMs`: default 75. Saved at start.
10. AddLabel `"Очікування повернення фокусу гри, с"`, then FluentNumber `focusWait` (0..600, step 10).
    - `profile.FocusLostWaitSeconds`: default 120. Saved at start.
11. AddLabel (muted): `"Якщо гра втратила фокус, натискання зупиняються, а запуск чекає повернення й продовжує з перевіркою екрана. 0 — зупиняти одразу, як раніше."`
12. AddLabel `"Xbox: відкриття CRAFTING"`.
13. Combo `xboxCrafting` with `PadChoice`s:
    - `("KeyboardF9","F9 клавіатури (разом із Xbox)")`
    - `("X","X")`, `("Y","Y")`
    - `("Start","Menu / Start")`, `("Back","View / Back")`
    - `("LeftShoulder","LB")`, `("RightShoulder","RB")`
    - `("LeftThumb","LS")`, `("RightThumb","RS")`
    - Initial value matches `profile.XboxRecalibrate` (string, default `"KeyboardF9"`), otherwise item 0. Saved at start.
14. AddLabel (muted): `"Вхід: Enter → F9 → Enter. Xbox: A → обрана дія Crafting → A. Класичний вихід: Esc/B ×3 → вправо. Швидкий: Esc/B ×2 → вниз → Crafting → A/Enter."`
15. AddLabel `"Клавіші клавіатури"`.
16. Seven rows (445×43). Each has a Label (width 180) and a `FluentKeyField` (width 150) bound by reflection to a Profile string property:

| Property | Label | Default |
|---|---|---|
| `Confirm` | `"Підтвердити (A)"` | `"Return"` |
| `Recalibrate` | `"Crafting (F9)"` | `"F9"` |
| `Back` | `"Назад (B)"` | `"Escape"` |
| `Left` | `"Ліворуч"` | `"Left"` |
| `Right` | `"Праворуч"` | `"Right"` |
| `Up` | `"Угору"` | `"Up"` |
| `Down` | `"Униз"` | `"Down"` |

    - KeyDown (MF:1301): F8, F7 and F10 are ignored. Any other key sets `box.Text = e.KeyCode.ToString()` (a **WinForms `Keys` enum name**), sets the profile property, and suppresses the key.
    - Changes are **not saved immediately**. They are written by the next `profile.Save()` (any save-on-change, or start).
    - All key fields are in `settingsControls`.
17. AddLabel (muted): `"Пошук: завжди верхній новий перк; приймається перший із двох потрібних. Другий слот шукає решту. Потім середні кидки: максимум другого, потім першого. F7 — зчитати; F9 належить грі."`

### Tab 3 "Журнали" (diagnostics) — `BuildDiagnostics` MF:1398–1450
All `FluentText` labels in this tab are widened to 920.
1. AddLabel (white): `"Журнали, статистика кидків і витрати зберігаються поруч із програмою. Кожен кидок записується один раз; записи кидків не обмежені лімітом скрінів."`
2. FluentCheck `logEnabled`: `"Зберігати журнал кожного запуску"`.
   - `profile.DiagnosticsEnabled`: bool, default true. Saved at start.
3. FluentCheck `logScreenshots`: `"Зберігати скріни гри та проблемних зон"`.
   - `profile.DiagnosticScreenshots`: bool, default true. Saved at start.
4. AddLabel `"Ліміт скрінів за один запуск, МБ"`, then FluentNumber `logMb` (1..2048, step 64).
   - `profile.DiagnosticMaxMb`: default 256. Saved at start.
   - `profile.DiagnosticMaxImages` (default 400) has no UI.
5. AddLabel (muted): `"Звичайні скріни: до 400 кадрів у межах ліміту. Останній безпечний кадр при помилці зберігається додатково. Текстовий журнал продовжується після вичерпання ліміту."`
6. AddLabel `"Папка журналів"`, then a read-only, borderless `TextBox` (width 900) showing `SessionLog.Root`, which is `AppContext.BaseDirectory\Logs`.
7. Button `"Відкрити папку Logs"` → `OpenLogs` (removed from `settingsControls`).
   - **`OpenLogs` (MF:1470):** creates the directory, then shell-opens `SessionLog.Root`. On error logs `"Не вдалося відкрити Logs: ..."`.
8. Button `"Створити ZIP останнього запуску"` → `ExportLogs` (disabled during a run).
   - **`ExportLogs` (async void, MF:1485):**
     1. Returns if busy.
     2. `folder = lastSessionFolder ?? SessionLog.Latest()`. If null, logs `"Журналів ще немає. Запусти F8, F7 або перевірку PNG."`.
     3. `await Task.Run(() => SessionLog.Export(folder))`, which creates `Logs\Archives\<session>_<HHmmssfff>.zip`.
     4. Logs `"ZIP готовий: " + path` and opens the Archives folder in Explorer.
     5. On error logs `"Не вдалося створити ZIP: ..."`.
9. AddLabel (muted): `"Записуються: версія і налаштування, кадри з часовими мітками, сирий OCR і повторні спроби, розпізнані відсотки, вибір варіанта, натискання та причина зупинки. Текст зберігається одразу; PNG — у фоні."`

### Tab 4 "Сповіщення" (notifications) — `BuildNotifications` MF:1319–1390
The Stack has padding 24.
1. AddLabel `"СПОВІЩЕННЯ"` (muted).
2. AddLabel `"Дізнайся, коли сет готовий, робота зупинилась або чекає на фокус гри."`
3. FluentCheck `toastEnabled`: `"Сповіщення Windows у кутку екрана: готово, зупинка, пауза"`.
   - `profile.ToastEnabled`: bool, default true.
   - On change: `Toasts.Enabled = value && !smoke`, then **save**.
4. `SoundRow(completionAudio, completionTone, "Коли всі деталі готові", ...)`:
   - FluentCheck `"Коли всі деталі готові"`, bound to `profile.CompletionSoundEnabled` (default true).
   - Combo `completionTone` with `NotificationAudio.Tones`, bound to `profile.CompletionSound` (default `"Asterisk"`).
   - Button `"Прослухати"`.
5. `SoundRow(stopAudio, stopTone, "При зупинці або помилці", ...)`:
   - Bound to `profile.StopSoundEnabled` (default true) and `profile.StopSound` (default `"Exclamation"`).
6. AddLabel (muted): `"Власні мелодії працюють незалежно від системної звукової схеми. Гучність Set Builder можна змінити у мікшері Windows. PNG та зчитування екрана — без сигналу."`

**SoundRow details (MF:1326–1361):**
- Toggle change and tone change both call the local `Save()`. It writes the Enabled flag and the tone code (fallback "Asterisk" / "Exclamation"), sets `tone.Enabled = toggle.Checked && !busy`, and **saves**.
- `"Прослухати"` → `PlayNotification(true, selectedTone.Code ?? "Asterisk", preview: true)`. It always plays, even when the toggle is off.
- **Tones** (Fluent.cs:502):
  - `Asterisk` = `"Мелодія завершення"`
  - `Exclamation` = `"Подвійний сигнал"`
  - `Beep` = `"Короткий дзвінок"`
  - `Hand` = `"Низький сигнал"`

**`SyncAudio` (MF:1392):** `completionTone.Enabled = !busy && completionAudio.Checked`; same for `stopTone`.

**`PlayNotification(enabled, code, preview=false)` (MF:1578):**
- Calls `NotificationAudio.Play(enabled, code)`.
- On error logs `"Не вдалося відтворити звук: " + err`.
- On success with `preview` logs `"Відтворюю сигнал. Гучність регулюється для Set Builder у мікшері Windows."`.

**`NotificationAudio` (Fluent.cs:495–577):**
- `Wave(code)` synthesises a 22050 Hz, 16-bit mono WAV in memory.
  - Asterisk: notes 523.25, 659.25, 783.99 Hz.
  - Exclamation: 660, 660.
  - Beep: 880 (0.3 s per note).
  - Hand: 440, 330, 220.
  - Other tones use 0.24 s per note, with a 12 ms attack envelope.
- `Play` caches one `System.Media.SoundPlayer` per code and calls `.Play()`. It returns the error message or null.

### Tab 5 "Аналітика"
Hosts `AnalyticsPanel` (see section 4). When a run finishes, `FinishDiagnostics` calls `analytics?.MarkStale()`.

---

## 3. Run lifecycle

**State fields:**
- `busy` (bool)
- `active` (`CancellationTokenSource?`): non-null exactly while an Arm or InspectFile is in progress. Disposed and nulled in `finally`.
- `recording` (`SessionLog?`) and `lastSessionFolder` (string?)
- `lastProgress` (`RunProgress?`)
- `stopAvailable` (bool)

**`Cancel()` (MF:1905):** `active?.Cancel()`. Called from F10, the "Зупинити" button, the tray menu, and `Arm` while busy.

### `Arm(bool run)` (async void, MF:1907–2021)
`run=true` means a full run (F8 or "Почати"). `run=false` means a read-only screen read (F7 or "Зчитати екран без натискань").

1. If `busy`: `Cancel()` and return. Pressing F8 or F7 again stops the current action.
2. If `run && !stopAvailable && !smoke`: log `"F10 зайнята іншою програмою. Закрий старе вікно Set Builder перед запуском нової версії."` and return.
3. If no `GameWindow` is selected: log `"Вибери вікно гри."` and return.
4. Set `busy = true` and `active = new CTS()`. Disable all `settingsControls`. Set `stop.Enabled = true`. Start with `outcome = "completed"`.
5. Inside `try`:
   1. `SaveSelection()`. It can throw, which leads to the error outcome.
   2. `Toasts.Enabled = profile.ToastEnabled && !smoke`.
   3. `StartDiagnostics(run ? "automation" : "read-only", game)`.
   4. If `run`: `lastProgress = null`; `runStatus.Set(Countdown, "Старт", "Поверни фокус у гру · F10 — скасувати")`; `SetTrayText("старт")`.
   5. **Countdown, only if the game is not already the foreground window:** for i = 5..1:
      - Log `"Поверни фокус у гру. Старт за {i} с · F10 — скасувати"`.
      - If `run`: status card `Countdown`, title `"Старт за {i} с"`, detail `"Поверни фокус у гру · F10 — скасувати"`.
      - `await Task.Delay(1000, token)`.
   6. If `!Desktop.IsGame(handle)`: throw `"Обране вікно не належить The Crew Motorfest."`.
   7. **Focus wait loop, while the game is still not foreground:**
      - If `FocusLostWaitSeconds <= 0` or the timeout has elapsed: throw `FocusLostException("Зупинено: гра втратила фокус.")`.
      - On the first iteration: log `"Гра ще не у фокусі. Чекаю до {N} с · F10 — скасувати"`, and if `run`, status card `Paused`, `"Чекаю фокус гри"`, `"Поверни фокус у гру · F10 — скасувати"`.
      - `Delay(250)`.
   8. If `run`: `await new Runner(catalog, profile, game.Handle, Log, recording, AutoSetApplied, OnProgress).Run(active.Token)`. The callbacks are: status/log `Action<string>` = `Log`; `SessionLog?` = `recording`; `Action<Build>? onAutoSet` = `AutoSetApplied`; `Action<RunProgress>? onProgress` = `OnProgress`.
   9. Otherwise: `using var f = Desktop.Capture(game.Handle); await Inspect(f);`
6. `catch OperationCanceledException`: `outcome = "cancelled"`; log `"Зупинено. Поточні зміни залишилися в грі."`.
7. `catch Exception`: `outcome = "error"`; `recording?.Write("error", new { error = e.ToString() })`; log `"Зупинено: " + e.Message`.
8. `finally`:
   1. `await FinishDiagnostics(outcome, failure)`.
   2. If `run`: `PlayNotification(completed ? CompletionSoundEnabled : StopSoundEnabled, completed ? CompletionSound : StopSound)`, then `ShowOutcome(outcome, failure)`.
   3. Dispose `active` and set it to null. Set `busy = false`. Re-enable all `settingsControls`. Set `stop.Enabled = false`.
   4. Call `SyncTimingControls()`, `SyncTargetControls()`, `SyncAudio()`.

There is **no UI-level pause/resume key**. Pausing happens automatically inside `Runner.EnsureFocus` (Automation.cs:980–1014) when the game loses focus:
- It logs `"Гра втратила фокус. Натискання зупинено; чекаю повернення до {N} с · F10 — скасувати"` and reports `"paused"`.
- It waits until focus returns or the timeout passes. A timeout throws `FocusLostException("Зупинено: гра не повернула фокус за {N} с.")`.
- When focus returns, it waits 400 ms, logs `"Фокус повернувся. Перевіряю екран і продовжую."` and reports `"running"`.

**`RunProgress`** (Workflow.cs:57):

```csharp
public sealed record RunProgress(string State, int Part, int Parts, int Rolls, long Bucks, long SpareParts);
```

- `State` is `"running"` or `"paused"`.
- Values come from the ledger: Rolls = `ledger.Confirmed`, Bucks = rolls × `RunLedger.BucksPerRoll`, SpareParts = rolls × `SparePartsPerRoll`.
- Reported when the run starts, on each part change (via the `BuildWorkflow` `onPart` callback), and on pause/resume (Automation.cs:904–914, 1128–1137).

**`ProgressText(r)` (MF:722):**
```
$"Деталь {max(1,Part)}/{Parts} · Кидків {Rolls} · {Bucks:N0} Bucks · {SpareParts:N0} запчастин"
```

**`OnProgress(r)` (MF:725–741):**
- Remembers whether the previous state was paused, then stores `lastProgress = r`.
- If `r.State == "paused"`:
  - Status card `Paused`, title `"Пауза: гра без фокусу"`, detail `ProgressText + " · поверни фокус у гру"`.
  - Tray `"пауза, гра без фокусу"`.
  - Pause toast (section 1).
- Otherwise:
  - If it was paused before: `Toasts.Remove("state")`.
  - Status card `Running`, title `"Виконується"`, detail `ProgressText`.
  - Tray `"деталь {p}/{n}, кидків {r}"`.

**`ShowOutcome(outcome, failure)` (MF:2023–2045).** Only called when `run=true`. `spent = lastProgress == null ? "Кидків 0" : ProgressText(lastProgress)`.

| outcome | Card mode | Title | Detail | Tray | Toast | Sound (from Arm) |
|---|---|---|---|---|---|---|
| `completed` | Done | `"Готово: обидва перки на максимумі"` | spent | `"готово"` | `"Сет готовий"` / `"{Set}: {First} + {Second}. {spent}."` | completion tone if enabled |
| `cancelled` | Stopped | `"Зупинено вручну"` | spent | `"зупинено"` | none | stop tone if enabled |
| other (error) | Failed | `"Зупинено з помилкою"` | `reason + " · " + spent` | `"помилка"` | `"Set Builder зупинено"` / reason | stop tone if enabled |

`reason = failure?.Message ?? "невідома помилка"`.

The read-only path (F7) and the PNG check do not change the status card, play sounds or show toasts.

**`StartDiagnostics(mode, game?)` (MF:1513):**
- Skipped when smoke or `!profile.DiagnosticsEnabled`.
- `recording = new SessionLog(profile, mode, game == null ? null : new { game.Title, game.ProcessName, handle = game.Handle.ToInt64() })`.
- `lastSessionFolder = recording.Folder`.
- Logs `"Запис діагностики: " + folder`.
- Mode values: `"automation"`, `"read-only"`, `"offline-png"`.

**`FinishDiagnostics(outcome, error)` (MF:1533):**
- Takes and clears `recording`, then `await session.Finish(outcome, error)`.
- Calls `analytics?.MarkStale()`.
- `AppendLog("Журнал збережено: " + folder)`. This uses AppendLog, not Log.
- If `session.WriteError != null`: log `"Не всі дані діагностики вдалося записати: " + err`.
- On exception: log `"Помилка збереження діагностики: " + msg`.

**`InspectFile(path)` (MF:2047–2091):**
- Same busy/disable pattern as `Arm`.
- Steps: `SaveSelection()`, `StartDiagnostics("offline-png")`, `recording?.Write("source_image", new { file = name })`, `using var f = new Bitmap(path)`, `await Inspect(f)`.
- Cancelled: log `"Зчитування скасовано."`. Error: `Log(e.Message)`.
- `finally`: `FinishDiagnostics`, re-enable controls, the three Sync calls. No sound, no toast, no status card change.

**`Inspect(Bitmap f)` (MF:2093–2158):**
1. `recording?.Observe(f, "inspect")`; `var v = new Vision(catalog, profile, recording)`.
2. Log `"Тільки зчитування · {W}×{H} · жодних натискань"`.
3. For each zone in `part, set, left, right, topLeft, middleLeft, topRight, middleRight`:
   - Check for cancellation.
   - `raw = await v.Text(f, name)`, then log `"{name}: {raw}"`.
   - For the perk zones (all except `part` and `set`), also log `"  → " + Recognition.Parse(raw, catalog)`. Parse errors are swallowed.
4. If `profile.AutoSet`:
   - `detected = await v.SetName(f)` and `choice = AutoSetResolver.Resolve(catalog, detected)`.
   - Set `profile.Set`, `First` and `Second`.
   - `recording?.SaveEffectiveProfile(profile)` and `recording?.Write("auto_set_preview", {set, preset, first, second})`.
   - `AutoSetApplied(choice)`, then log `"Автосет: {detected} → {First} + {Second} ({choice.Title})."`.
   - On `InvalidOperationException`: log `"Автосет: " + msg`.
5. `popup = await v.PopupSide(f)`:
   - If `< 0`: log `"Основний екран · вибраний слот: " + (v.Focus(f, ["left","right"]) + 1)`.
   - Otherwise: log `"Меню кидка ліворуч|праворуч · варіант: " + (v.Focus(f, side zones [topX, middleX, X]) + 1) + ". Натисни F8, щоб продовжити відкритий кидок."`.

---

## 4. AnalyticsPanel (AnalyticsPanel.cs)

A `UserControl` with `Dock=Fill`, BackColor `Fluent.Surface`. Constructor `AnalyticsPanel(Catalog)` (48–178).

**Root layout:** a `TableLayoutPanel` (padding 10) with 6 rows: 38 / 76 / 50 / 85 / 65 / 100%.

**Row 0, toolbar.** Local `Button()` helper (180): FluentButton, height 28, min 36×28, padding (7,0,7,0).
- `"Оновили"` is not a label; the buttons are:
  - `"Оновити"` → `Reload()`
  - `"Зберегти TXT + CSV + JSON"` → `Export()`
  - `"Додати папку Logs"` → `AddFolder()`

**Row 1, filters.** `DarkSelect`s in a wrapping flow panel, height 31:

| Filter | Width | Items |
|---|---|---|
| `sets` | 190 | `"Усі сети"` + distinct session sets, ordered |
| `phases` | 155 | Fixed `Choice`s: `""`→`"Усі фази"`, `"Search"`→`"Пошук"`, `"Maximize"`→`"Прокачування"`, `"Unknown"`→`"Фаза невідома"` |
| `versions` | 140 | `"Усі версії"` + distinct versions |
| `others` | 210 | `"Інший перк: усі"`, `("__unknown","Інший перк: не записано")`, then `"Інший: {title}"` per catalog affix |
| `sessions` | 580 | `"Усі запуски"` + one entry per session (format below) |

- Session entry format: `"{Started local dd.MM HH:mm:ss} · {Set} · {Version} · {Status} · {CompletedParts} детал. · {paid rolls} кидків"`.
- Status mapping: completed → `"успіх"`, cancelled → `"скасовано"`, error → `"помилка"`, running → `"у роботі"`.
- Any filter change calls `Render()`.

**Row 2, `totals`:** Label, Accent colour, Segoe UI 10 bold.

**Row 3, `itemTotals`:** Label, white, bold.

**Row 4, `note`:** muted Label, max width 980. Initial text: `"Відкрий вкладку або натисни «Оновити», щоб прочитати журнали всіх версій."`

**Row 5:** a nested `TabHost` with pages in this order:
0. `"Рейтинг перків"`: DataGridView `grid`.
   - Read-only, full-row select, sortable columns.
   - Columns: `Rank` "№", `Perk` "Перк", `Count` "Появ", `Chance` "Частка, %" (format 0.00), `Numeric` "З % зчитано", `Max` "На максимумі", `MaxChance` "Макс., %" (format 0.00, null shows "—").
   - Dark styling: header back=Surface, fore=Muted, Segoe UI 9 bold. Cells back=Bg. Selection (65,63,30). Alternating rows (33,33,33). Grid lines (55,55,55). Row height 26.
1. `"Усі текстові журнали"`:
   - Search TextBox with placeholder `"Пошук у всіх текстових журналах…"`, debounced by a 250 ms Timer, then `RenderText()`.
   - Read-only multiline `logs` TextBox: Cascadia Mono 9, Gainsboro text, both scrollbars, no wrap.
   - Paging row: buttons `"←"` and `"→"` (500 lines per page), plus a muted `pageLabel`: `"Сторінка {p}/{n} · рядків {count:N0} · усі рядки доступні через сторінки або TXT"`.
2. `"Завершені деталі"`: DataGridView `itemGrid`.
   - Columns: `Time` "Завершено", `Part` "Деталь", `Set` "Сет", `Duration` "Час¹", `Rolls` "Кидків¹", `Bucks` "Bucks¹", `Spares` "Запчастин¹", `Session` "Запуск".
   - Rolls, Bucks and Spares use format N0. Nulls show "—". Row height 28.
   - `Duration` is formatted with `ItemAnalytics.Duration(seconds)` in `CellFormatting` (324).

**Automatic loading:** on `VisibleChanged`, if the panel is visible, the form is visible, the data is stale and nothing is loading → `Reload()` (173–177). `MarkStale()` (333) sets stale and reloads immediately if the panel is visible.

**`Reload()` (async void, 340–407):**
- Disables Refresh and Export. Note shows `"Збираю журнали…"`.
- `data = await Task.Run(() => AnalyticsStore.Load(catalog, null, progressCallback))`. Progress strings such as `"Читаю журнали {n}/{N}"` go to `note` via `Progress<string>`.
- Fills the filters (`Fill()` keeps the prior selection by Code), then `Render()`.
- On error: note `"Не вдалося прочитати аналітику: " + msg`.

**`Render()` (419–481):**
- Filters sessions by set, version and session id, then rolls by phase and other perk. `paid` = rolls with `Paid`.
- `ranking = AnalyticsData.Leaderboard(catalog, paid)` (returns `List<PerkFrequency>` with `Rank, Name, Count, Percentage, NumericSamples, MaxCount, MaxPercentage`).
- `totals.Text`:
  ```
  "Запусків: {n:N0} · Кидків: {paid:N0} · Bucks: {sum:N0} · Запчастин: {sum:N0}\nНових пропозицій розпізнано: {known:N0} · Без назви/пропозиції: {unknown:N0} · Непідтверджених натискань: {Requested&&!Paid&&!NotSent:N0}"
  ```
- `note.Text`:
  ```
  "Частоти: нові верхні пропозиції; вибірка {known:N0}. Це спостереження, а не точні шанси гри."
  ```
  plus `" Проблемних журналів: {Warnings.Count}."` when there are warnings, plus:
  ```
  "\nСередні: усі витрати й час / готові деталі, включно з перерваними спробами; за фільтрами запусків.\n¹ Деталь — лише в межах її запуску. Загальний час без проміжків між запусками. — немає даних."
  ```
- `ItemAnalytics.Summary(selectedSessions)` returns `ItemAverages`. `itemTotals.Text`:
  ```
  "Завершено деталей: {n:N0}\nСереднє на готову деталь: {avgRolls:N1} кидків · {avgBucks} Bucks · {avgSpares} запчастин\nЧас запусків: {Duration(total)} · середнє на деталь: {Duration(avg)}"
  ```
  plus `" · без часу: {n} запусків"` when some sessions have no timing. Missing averages show "—".
- Item rows come from `selectedSessions.SelectMany(ItemAnalytics.Completed)`, using `CompletedItem` fields Time, Part, Set, DurationSeconds, Rolls, Bucks, SpareParts, Session.
- Finally calls `RenderText()`, which sets `textLines = AnalyticsStore.CombinedText(selectedSessions, search.Text).Split('\n')`, resets to page 0, and calls `ShowPage()`.

**`Export()` (500):** `await Task.Run(() => AnalyticsStore.Export(data, catalog, sessions, rolls))`, which writes into `AnalyticsStore.Output = Logs\Analytics`. Note shows `"Збережено: " + path`, or the error message.

**`AddFolder()` (526):**
- `FolderBrowserDialog`, Description `"Вибери папку Logs або папку старої версії програми"`.
- `AnalyticsStore.AddRoot(path)` uses the `Logs` subfolder if present and persists the list to `Logs\Analytics\sources.json`. Then sets stale and reloads.
- `AnalyticsStore.Roots()` also auto-discovers `dist*\Logs` folders and the repo `Logs` folder.

**Preview hooks:**
- `LoadPreview()` (546): loads synchronously via `Task.Run(AnalyticsStore.Load(catalog))`, fills the filters with only their "Усі…" entries, then `Render()`.
- `ShowTextPreview()` (558) opens nested page 1. `ShowItemsPreview()` (561) opens nested page 2.

---

## 5. Smoke test and previews

**`Program.cs --ui-smoke` (78–126):**
1. `new MainForm(true)`. Preview mode means: default `Profile`, `Toasts.Enabled = false`, no saves, no tray, no hotkeys, no bounds restore, no diagnostics recording, and `Arm` skips the F10 check.
2. `form.Shown += async: await form.LoadAnalyticsPreview(); timer.Start();`
3. A 1000 ms timer takes a `DrawToBitmap` capture of the whole form on each tick, saved in turn as `ui-preview.png`, `ui-zones.png`, `ui-controls.png`, `ui-diagnostics.png`, `ui-notifications.png`, `ui-analytics.png`. All files go to `AppContext.BaseDirectory`.
4. After the page-0 capture it also calls `SaveAutoPreview("ui-auto-set.png")` and `SaveSelectorPreview("ui-selector.png")`.
5. After each capture it calls `ShowPreviewTab(page)` to move to the next tab.
6. After page 6: `SaveAnalyticsTextPreview("ui-analytics-logs.png")`, `SaveAnalyticsItemsPreview("ui-analytics-items.png")`, `SaveExpandedLogPreview("ui-expanded-log.png")`, then stop the timer, `Close()` and return 0.
7. Failures are thrown `InvalidOperationException`s. They crash the process: `AppDomain.UnhandledException` → `WriteCrash` → `Logs\application-error-*.txt`, and the exit code is non-zero.
8. Unknown arguments write `error.txt` and return 2.

**`LoadAnalyticsPreview()` (MF:1654–1807).** When smoke, it runs these checks in order:
- `partRoute` index 1 → `FastPartNavigation` true; index 0 → false.
- `partCountButtons[0].PerformClick()` → Parts 1; `[6]` → Parts 7.
- Each tone's WAV loads in a `SoundPlayer`. The `"Asterisk"` WAV is at least 1000 bytes with RIFF/WAVE headers.
- A temporary `FluentNumber` (0..5000): typing "725" in the inner TextBox gives Value 725; setting 9000 clamps to 5000; disabling it disables the editor.
- Toggling `completionAudio` off disables `completionTone` and clears `profile.CompletionSoundEnabled`. Same for `stopAudio`.
- `completionTone = Tones[2]` gives `profile.CompletionSound == "Beep"`. It is then reset to Tones[0], and a JSON round-trip of the profile is checked.
- It then **writes `ui-controls-test.txt`** (`File.WriteAllText`, MF:1723–1726) with:
  ```
  PASS navigation route selector updates settings
  PASS seven count buttons update selection and saved setting
  PASS all four WAV signals load in Windows SoundPlayer
  PASS custom numeric typing, bounds and disabled state
  PASS completion and stop sound toggles
  PASS sound selection and profile round-trip
  ```
- More checks:
  - The `firstOptional` toggle leads to `RequiredNames() == [Second]`.
  - `secondAlt = "Rich"` plus `SaveSelection` gives `Goals()[1].Names == ["Extra Pump","Rich"]` and `GoalIndex("Rich") == 1`.
  - `secondAlt = "Pure"` (same as First) makes `SaveSelection` throw.
  - `NoAlternative` clears the alternative.
  - Build `"Nitro Chemist"` gives an icon, a visible `buildInfo` containing "NITRO", `setInfo` containing `"повний сет: +50%"`, `firstInfo` containing `"3–5%"`, `secondInfo` containing `"speed"`.
  - `Describe(Cluster)` must equal exactly `"Increase GEARSHIFTING BOOST EFFECT in DRAG RACE events · 1.2–2% · speed"`.
  - An alternative shows an icon and description; dropdown `ItemImage` is non-null; clearing hides them.
  - `OnProgress`/`ShowOutcome` drive the status card through Running → Paused → Running → Done → Failed → Stopped. It is then reset to Idle (`"Очікування"`, `"Вибери сет, два перки й вікно гри · F8 — запуск"`) and left in a running state for the screenshot.
- It then **appends** (MF:1798–1801):
  ```
  PASS status card: running, paused, resumed, done, failed, cancelled
  PASS app icon, dark scrollbars and toast toggle constructed
  PASS optional toggle and alternative perk goals save, duplicates rejected
  PASS build, set and perk icons and descriptions follow the selection, also in the open list
  ```
- It seeds the log with three sample lines through `AppendLog`:
  - `"Шукаю пару: Rich + Legend. Порядок слотів довільний."`
  - `"Знайдено Rich у слоті 2. У слоті 1 шукаю тільки Legend."`
  - `"✓ Деталь 1: Legend 4% / 4% + Rich 3% / 3%"`
- Finally it returns `analytics?.LoadPreview()`.

That gives 10 PASS lines in `ui-controls-test.txt`.

**Other preview methods:**
| Method | Line | What it does |
|---|---|---|
| `SaveAutoPreview(path)` | MF:1609 | Sets `autoSet` true, asserts sets/first/second/builds are disabled, renders the form to a PNG, restores the toggle |
| `SaveAnalyticsTextPreview` | MF:1621 | `analytics.ShowTextPreview()`, layout, render |
| `SaveExpandedLogPreview` | MF:1630 | Shows tab 0, moves the splitter 150 px up (asserts `Panel2.Height > LogPanelHeight`), renders, restores |
| `SaveAnalyticsItemsPreview` | MF:1645 | `analytics.ShowItemsPreview()`, render |
| `SaveSelectorPreview(path)` | MF:1809 | `first.SavePopupPreview(path)` (Controls.cs:260): opens the popup, types "Pure" in the search box and asserts exactly one "Pure" result, clears the search, renders the popup, closes it |
| `ShowPreviewTab(index)` | MF:976 | Finds the root table, then its TabHost, then `ShowPage(index)` |

**Test.ps1:**
- Builds the project (unless `-SkipBuild`) and finds `bin\<cfg>\**\TunerFest Set Builder.exe`.
- Runs each mode with `Start-Process -Wait`. Modes: `--self-test`, `--verify-workflow`, `--verify-focus`, `--verify-recovery`, `--verify-analytics`, `--ui-smoke`, `--verify-stuck ...`, and three `--verify-value-crop ...`.
- A mode passes when its exit code is 0.
- `Checks passed:` is the sum, over every `*-test.txt` in the exe folder, of lines matching `'PASS *'`.
- Other report files: `self-test.txt`, `workflow-test.txt`, `focus-test.txt`, `recovery-test.txt`, `analytics-test.txt`, `diagnostics-test.txt`, `value-crop-test.txt`, `recognition-cache-test.txt`, `ui-controls-test.txt`.

**Build.ps1:** runs Test.ps1 (unless `-SkipTests`), then `dotnet publish -c Release -r win-x64 --self-contained` into `dist-v<Version>`, and writes `latest-build.txt`, which `Start.cmd` reads.

---

## 6. Core boundary (what MainForm and AnalyticsPanel call)

**Models.cs:**
- `Catalog.Load()`, reading `catalog.json` from the base directory. Properties: `List<Affix> Affixes`, `List<SetBonus> Sets`, `List<Build> Builds`.
- `Affix`: Title, Description, BonusPercentageRangeFrom/To, Icon, `Tags[]?`.
- `SetBonus`: Title, Description, BonusPercentageThreeParts, BonusPercentageFull, `Tags[]?`.
- `Build`: Title, Description, `Tags[]?`, Set, `Affixes[]`.
- `Profile.Load()` and `profile.Save()`.
- `Profile.Defaults()`, `Profile.Folder`, `Profile.PathName`.
- `Goals()`, `RequiredNames()`, `GoalIndex(name)`.
- `Zone(X, Y, W, H)`.
- `Recognition.Normalize(string)` and `Recognition.Parse(string, Catalog) → Reading`.

**Automation.cs:**
- `new Vision(Catalog, Profile, SessionLog? = null, VisionTemplates? = null)`.
- `static string[] Vision.Languages()`.
- `Task<string> Text(Bitmap frame, string zone)`.
- `Task<string> SetName(Bitmap)`.
- `Task<int> PopupSide(Bitmap)`.
- `int Focus(Bitmap, IReadOnlyList<string>)`.
- Runner constructor:
  ```csharp
  new Runner(Catalog c, Profile profile, IntPtr w, Action<string> status,
             SessionLog? diagnostics = null, Action<Build>? onAutoSet = null,
             Action<RunProgress>? onProgress = null)   // Automation.cs:916
  ```
- `Task Runner.Run(CancellationToken)` (1122). It internally creates `BuildWorkflow(this, p, log, onPart)` from Workflow.cs:59.

**Workflow.cs:** `record RunProgress(...)` (57) and `record PadChoice(string Code, string Label)` (168, `ToString` = Label).

**Desktop.cs:**
- `record GameWindow(IntPtr Handle, string Title, string ProcessName)`.
- `FocusLostException`.
- `Desktop.GetForegroundWindow()`.
- `RegisterHotKey(IntPtr, int, uint, uint)` / `UnregisterHotKey(IntPtr, int)`.
- `IsGame(IntPtr)`.
- `List<GameWindow> Windows()`.
- `Rectangle Bounds(IntPtr)` (client rectangle in screen coordinates).
- `Bitmap Capture(IntPtr, Rectangle? = null)`.

**SessionLog.cs:**
- `static string Root` (= `BaseDirectory\Logs`).
- `new SessionLog(Profile, string mode, object? window = null, string? root = null)`.
- Properties: `Folder`, `WriteError`, `Ledger`.
- Methods: `Write(kind, data)`, `Message(string)`, `Observe(Bitmap, stage, ...)`, `SaveEffectiveProfile(Profile)`, `Task Finish(string outcome, Exception? = null)`.
- `static string? Latest()` and `static string Export(string session)` (zip into `Logs\Archives`).

**Other core APIs:**
- `AutoSetResolver.Resolve(Catalog, string set) → Build`. It throws `"Для сету {set} немає повного профілю TunerFest. Обери перки вручну."` when no build matches.
- `PressTiming.Validate(Profile)`.
- `AnalyticsStore` (Analytics.cs):
  - `Load(Catalog, IEnumerable<string>? roots = null, Action<string>? progress = null) → AnalyticsData`
  - `CombinedText(IEnumerable<AnalyticsSession>, string query = "")`
  - `Export(AnalyticsData, Catalog, IEnumerable<AnalyticsSession>, IEnumerable<AnalyticsRoll>) → string path`
  - `AddRoot(string)`, `Roots()`, `Output`
- `AnalyticsData.Leaderboard(Catalog, IEnumerable<AnalyticsRoll>)`.
- `ItemAnalytics.Summary(...)`, `Completed(AnalyticsSession)`, `Duration(double?)`.

**UI-side helpers that the core or Program also need:**
- `Toasts` and `Shell.ActivateExisting`/`Activate` (Shell.cs) are plain static Win32/UWP-toast code with no Control dependency. `Shell.Px` and `DarkScrollbars` do depend on `Control`.
- `NotificationAudio` lives in Fluent.cs but only uses `System.Media.SoundPlayer`.
- `MainForm.Describe` (internal static) and `MainForm.AppVersion` are used only inside MainForm. SessionLog takes the version from `typeof(SessionLog).Assembly`.

### Things that block or affect a WPF host
1. **Desktop.cs:263–266 (`Input.Press`) uses the `System.Windows.Forms.Keys` enum.** It runs `Enum.TryParse<Keys>(key, true, ...)` and compares against `Keys.Back`, `Keys.OemClear`, `Keys.Left`, `Keys.Right`, `Keys.Up`, `Keys.Down`.
   - So the core needs a WinForms reference.
   - Key names stored in the profile must be **WinForms `Keys` names** (e.g. `"Return"`, `"Escape"`, `"F9"`). A WPF key-capture field must convert `System.Windows.Input.Key` to `Keys`, for example via `KeyInterop.VirtualKeyFromKey`, then `((Keys)vk).ToString()`.
2. **The core depends on `System.Drawing` and relies on implicit global usings.** `UseWindowsForms` + `ImplicitUsings` supply the global usings `System.Drawing` and `System.Windows.Forms`. Core types used without explicit `using`:
   - `Bitmap`, `Rectangle`, `Graphics`, `Point`, `Size`, `Font` in Automation.cs, Desktop.cs, SessionLog.cs, VisionTemplates.cs and the Verification files.
   - `Zone.Pixels(Size) → Rectangle` in Models.cs.
   - A WPF project must keep `<UseWindowsForms>true</UseWindowsForms>` alongside `<UseWPF>true</UseWPF>`, or add explicit global usings. Expect name clashes in UI code: `Point`/`Size`/`Color`/`Brush`/`Image`/`Application`/`MessageBox`/`Clipboard` exist in both namespaces.
3. **No core file references `MainForm`, `Form`, `Control`, or `Control.Invoke`/`BeginInvoke`.** The only `.Invoke(` calls in the core are delegate invocations.
   - Runner callbacks (`log`, `onAutoSet`, `onProgress`) run on whatever context awaited `Run`. The core has no `ConfigureAwait(false)`, so they arrive on the UI thread when started from the UI thread.
   - `AnalyticsStore.Load`'s progress callback runs on the `Task.Run` thread. The panel marshals it with `Progress<string>`.
   - SessionLog writes images on its own `Task.Run` worker.
4. **Program.cs** references `MainForm`, `ApplicationConfiguration`, `Application.Run`/`ThreadException`/`SetUnhandledExceptionMode`, `MessageBox`, and a WinForms `Timer`/`DrawToBitmap` for the smoke test.
   - Crash dialog text: `"Сталася неочікувана помилка. Звіт збережено у папці Logs поруч із програмою.\n\n" + msg`, caption `"TunerFest Set Builder"`.
   - All verification CLI modes are dispatched here before any window is created.
5. **The Verification files** (`Verification.cs`, `WorkflowVerification.cs`, `AnalyticsVerification.cs`, `FocusVerification.cs`, `RecoveryVerification.cs`) have no WinForms or MainForm references.
6. **Project settings:** `SetBuilder.csproj` targets `net8.0-windows10.0.19041.0` (needed for WinRT OCR in Vision) and references `..\app.manifest`, which is **outside the repo**.
   - Packages: `Microsoft.Toolkit.Uwp.Notifications` 7.1.3, `Svg` 3.4.8 (icon rendering, UI only), `Nefarius.ViGEm.Client`.
   - `catalog.json` and `Assets/**` are copied to the output folder.

---

## 7. Custom controls and colours

**`Fluent` colour constants** (Fluent.cs:8–13):
| Name | RGB |
|---|---|
| `Background` | (23,24,27) |
| `Surface` | (32,34,38) |
| `Input` | (42,44,49) |
| `Border` | (66,69,76) |
| `Muted` | (164,170,182) |
| `Accent` | (255,235,59) (yellow) |

MainForm aliases them (MF:116–119): `Bg = Background`, `Card = Surface`, `Accent = Accent`, `Muted = Muted`.

Other literal colours:
- Log: timestamp (124,137,156); ГОТОВО (134,218,164); ЗУПИНКА (255,159,139); default (203,208,218).
- StatusCard: paused (255,167,38); failed (255,107,107).
- FluentButton hover: primary (255,244,125), normal (55,58,64). Disabled text (100,105,115).
- DarkSelect: selected list item (70,68,33); popup border (85,85,65).
- ZonePicker: selection rectangle (255,244,35).
- Button flat border (70,70,70).

**Controls:**
- **`Fluent.Shape(rect, radius)`:** builds a rounded-rectangle `GraphicsPath`.
- **`FluentButton`** (Fluent.cs:53): owner-drawn rounded button (radius 6), hand cursor.
  - Treated as primary when `BackColor == Accent`: Accent fill, lighter yellow on hover, dark text.
  - Otherwise: Input fill, (55,58,64) on hover, ForeColor text.
  - Disabled: Surface fill with grey text. Border is Accent when focused, Border otherwise.
- **`FluentCheck`** (108): a `CheckBox` drawn as a toggle switch.
  - Track 34×20 px (DPI-scaled), Accent when checked and enabled, Input otherwise. Knob 14 px: Background colour when on, Muted when off.
  - Label starts 45 px from the left and turns muted when disabled. Minimum height 28. Preferred width = text width + 48.
- **`FluentNumber`** (157): rounded input containing an inner borderless TextBox.
  - Properties `Minimum`, `Maximum`, `Increment`, `Value` (clamped, decimal, displayed as "0"). `ValueChanged` event.
  - Typing a value within range updates `Value` live. Enter or losing focus commits. Up/Down arrow keys step by `Increment`.
  - Width ≥ 110 px: `−` and `+` hit zones on the right (29 px each). Narrower: stacked `+` over `−`.
  - Border is Accent when focused. Disabling it disables the inner editor.
- **`FluentText`** (354): non-selectable label with fixed width and word wrap. Wraps on whole words only (works around GDI breaking Cyrillic words mid-word). Height follows the number of lines.
- **`FluentKeyField`** (432): focusable rounded box showing `Text`. Accepts every key (`IsInputKey` returns true). Border is Accent when focused. MainForm handles KeyDown.
- **`DarkSelect`** (Controls.cs:6): custom combo box.
  - API: `Items` (`List<object>`), `SelectedIndex`/`SelectedItem`, `SelectedIndexChanged`, `ItemImage: Func<object, Image?>`.
  - Closed state: rounded box (Input fill, Surface when disabled) showing `SelectedItem.ToString()` or `"Оберіть…"`, with a chevron.
  - Keyboard: Up/Down change the selection without opening; Space/Enter/click open the popup.
  - Popup: `ToolStripDropDown` with an owner-drawn ListBox. Items are 30 px high; the list is at least 300 px wide and at most 300 px tall. An optional 22 px icon is drawn per item. Click or Enter commits, Esc closes.
  - **When there are 8 or more items**, a search TextBox (placeholder `"Знайти…"`) sits above the list and filters by case-insensitive `Contains`. Down moves focus to the list; Enter commits.
  - `SavePopupPreview(path)` is used for the smoke test.
- **`TabHost`** (282): a top row of `FluentButton`s (height 46, buttons 36 high, padding 16) with content panels below.
  - Active tab: Accent background, dark text. Inactive: Background with Muted text. Pages use the Surface colour.
  - API: `AddPage(title)` returns a Panel; the first page is selected automatically. `ShowPage(index)`.
- **`StatusCard`:** see section 1.
- **`Toasts`:** see section 1.
- **`ZonePicker`:** see section 2, tab 1.
