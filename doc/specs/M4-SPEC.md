# EveLocalMon — M4 implementation spec: EVE-style UI refresh

**Status: implemented (2026-11-04).** 174 automated tests pass
(`dotnet test EveLocalMon.sln --filter "Category!=Perf"`; 102 before, 72 new), the whole solution
builds with no warnings, and the UI was run and inspected on a real display. §0 now records what
Avalonia 12.1.2 actually does, §14 lists where the build differs from this spec and the mockups, and
§13 says which manual checks were done here and which still need a person (EVE running, a second
monitor, a scaled display). Mockups approved by the user with every new feature kept: [EveLocalMon — EVE-style UI refresh](https://claude.ai/artifact/3LSJyuX7rp1mG7up14v7x5)
(private design canvas: Monitor / Alerts / Calibration tabs, popups, region picker, tokens).
Where this spec and the mockups disagree, this spec wins — §14 lists the deliberate differences.

Scope: restyle every window the app has so it looks like it belongs next to the EVE client, and
add the small features the mockups introduced (status header, session log, mute presets, popup
countdown, picker loupe and width check, calibration zoom and live draft preview). No change to
detection, `AlertState` debouncing, or alert routing rules. Tray icon, template cropper and
autostart move to **M5** (milestones renumbered — see `doc/specs/PLAN.md` §6).

Background: `doc/specs/M2-SPEC.md` §3/§7 (picker and main window being replaced),
`doc/specs/M3-SPEC.md` §3/§5 (wording invariant, popup), reference screenshot
`doc/samples/eve-online-client-sample.png` (full client, Photon UI).

Decided with the user before writing this:

| Question | Answer | Consequence |
| --- | --- | --- |
| Visual direction | Match the EVE client's Photon UI | Dark charcoal panels, 1px hairlines, square corners, sentence-case titles with `[count]`, underlined active tab |
| New features in the mockups | **Keep all of them** | Session log, mute presets, popup countdown, overflow popup variant, picker loupe + width check, calibration zoom |
| Spec placement | New milestone **M4**, ahead of the tray shell | Later milestones shift by one |

Unchanged hard rules: passive only; no notification/audio packages; UI built in C#, not XAML;
`EveLocalMon.Core` stays platform-neutral.

---

## 0. Prerequisites (verify before building, as M2 §0 did)

Avalonia 12 moved APIs; check each against **12.1.2** in a throwaway project, not from memory:

1. **Custom title bar.** How v12 extends the client area into the title bar while keeping OS
   resize borders and Aero Snap (v11 used `ExtendClientAreaToDecorationsHint` +
   `ExtendClientAreaChromeHints`; the popup already uses v12's `WindowDecorations`). Need: no
   system caption, OS resize edges kept, `BeginMoveDrag` from our own bar.
2. **Embedded fonts.** `AvaloniaResource` items plus a `FontFamily("avares://EveLocalMon/Assets/Fonts#Saira Semi Condensed")`
   URI resolves all three weights, and `TextBlock` falls back cleanly if a weight is missing.
3. **Theme layering.** `FluentTheme` with `RequestedThemeVariant = ThemeVariant.Dark`, then our
   own `Styles` added after it in C# (`new Style(x => x.OfType<Button>().Class("primary"))`)
   override template parts such as `/template/ ContentPresenter#PART_ContentPresenter`.
4. **Pixel-exact zoom.** An `Image` scaled by `LayoutTransformControl` (or explicit size) with
   `BitmapInterpolationMode.None` stays nearest-neighbour at 2× and 4× on a 125% monitor.
5. **Pointer over a non-activated window.** `PointerEntered`/`PointerExited` fire on the popup
   (`ShowActivated = false`) so hover can pause its countdown.

**Results (checked against 12.1.2, 2026-11-04):**

1. **Custom title bar — works.** `ExtendClientAreaChromeHints` no longer exists. `WindowDecorations =
   BorderOnly` draws no caption but keeps the native resize edges; our own bar calls `BeginMoveDrag`.
   Measured on Windows 11: dragging moved the window by exactly the mouse delta, an edge drag resized it by
   exactly the mouse delta, and a double-click maximised it. The §15 fallback was not needed.
2. **Embedded fonts — works, with a trap.** The folder URI plus `#family` resolves 400/500/600 and the
   mono face. But the Saira files in `assets/fonts/` are named one step off their real weight, and the
   family inside them is spelled "Saira SemiCondensed" (one word); see §2.2.
3. **Theme layering — partly.** Overriding Fluent's brush resources on the `Application` re-colours
   buttons, text boxes and scroll bars and keeps their hover/pressed/focus states. `Style.Resources`
   scoped to a style class did **not** reach Fluent's templates, so button variants are set per
   instance (`EveTheme.SetKind`). `TabControl` was not tried: tabs are a strip of `TabButton`s, which also
   keeps all three views alive.
4. **Pixel-exact zoom — works.** An `Image` sized to `pixels / RenderScaling * zoom` with
   `BitmapInterpolationMode.None` stays nearest-neighbour; the loupe uses the same.
5. **Hover on a non-activated popup — works** by construction (`PointerEntered`/`PointerExited` on the
   window); the countdown refill was not observed live.

Two more Avalonia 12 facts found while building: an item template can be asked to build for `null`
content while a list is replaced (it crashed the Monitor tab until the builders tolerated null), and
`KeyboardDevice.Instance` is gone, so modifier keys on a click come from the press event.

---

## 1. Projects and files

### `EveLocalMon.Core` (platform-neutral, unit-tested)

```text
Status/MonitorState.cs         enum Stopped | SetupNeeded | Checking | Clear | Hostile | Unreadable
Status/MonitorStatus.cs        record + pure Compute(...) over AlertState's stable view — the header's single source of truth (§4)
Alerts/AlertState.cs           + StableView Stable (debounced values for the header; events unchanged)
Alerts/SessionLog.cs           capped, newest-first, in-memory log + SessionEntry/SessionEntryKind (§6)
Alerts/DispatchOutcome.cs      what each channel actually did for one message (§6)
Alerts/AlertKind.cs            + Overflow (split from Unreadable for the popup/ntfy visuals, §9)
Alerts/AlertMessageFactory.cs  titles lose the "EVE: " prefix; Overflow kind (§9)
Alerts/AlertDispatcher.cs      DispatchAsync returns DispatchOutcome
Net/NtfyClient.cs              prefixes "EVE: " to the push title; Overflow → "warning" tag
Reading/RegionHeuristics.cs    MaxPlausibleListWidth = 400 (moved from MainWindow; used by picker + status)
Settings/UiSettings.cs         KeepOnTop, CalibrationZoom; AppSettings schema v3 (§12)
Reading/IconTemplate.cs        + Color (Bgr?), from optional templates.json "color" (§2.1)
Reading/TemplateStore.cs       parses "color"; malformed → null, never throws
```

### `EveLocalMon.App`

```text
Theme/Palette.cs        every colour in §2 as Color + immutable brush; nothing else hard-codes a colour
Theme/Fonts.cs          UI / Mono FontFamily constants (§2.2)
Theme/Icons.cs          StreamGeometry path data for the stroke icons (pin, minimise, close, kebab, mute, region, warning, eye-off, list-overflow, check, play, stop)
Theme/EveTheme.cs       Styles added after FluentTheme (§3)
Controls/TitleBar.cs    custom chrome (§5.1)
Controls/StatusHeader.cs  full (Monitor) and compact (other tabs) modes (§5.2)
Controls/SectionPanel.cs  titled panel: title, optional [count], optional header actions
Controls/Stepper.cs     − / value / + numeric field (Calibration)
Controls/StateDot.cs    8–10 px square with glow; outline-only when Stopped
Views/MainWindow.cs     shell + wiring only: title bar, header, tabs, footer
Views/MonitorTab.cs     capture, rows table, session panel (§7)
Views/AlertsTab.cs      replaces AlertsPanel.cs (§8)
Views/CalibrationTab.cs replaces CalibrationPanel.cs (§10)
Views/AlertPopupWindow.cs  restyled + countdown + hover pause (§9)
Views/RegionPickerWindow.cs  restyled + loupe + readout (§11)
Rendering/PreviewRenderer.cs  new overlay palette; guide colours per §10
Services/MonitorLoop.cs   + Ticked event (§4.2)
assets/fonts/           already present (§2.2); four TTFs embedded as AvaloniaResource
```

`MainWindow.cs` is 477 lines today mixing layout and wiring; splitting the tabs out is part of
this milestone, not a separate refactor.

---

## 2. Visual language

Values are final; they are the "Tokens & controls" board. `Palette.cs` is the only place they live.

### 2.1 Colour — taken from EVE, not invented

Every value is sampled from EVE itself: UI colours from `doc/samples/eve-online-client-sample.png`
(Photon UI), standing colours from the shipped icon templates in `assets/templates/`. Where an
EVE colour is too dark to read as text or a 1px line on our panels, the table says what it was
brightened from. This supersedes the cooler blue-grey palette in the mockups (§14).

**UI**

| Token | Hex | EVE source | Use |
| --- | --- | --- | --- |
| `Window` | `#121616` | between header and panel | window background |
| `Panel` | `#181c1c` | panel body (Local, Overview, chat) | panels, status header, footer |
| `TitleBar` | `#101414` | window header strip | title bar |
| `TabStrip` | `#141818` | Overview body, darker band | tab strip, panel footers |
| `Field` | `#0c1010` | — (one step below `TitleBar`) | text/number inputs, capture backdrop |
| `Hairline` | `#2a3030` | — | panel borders |
| `Divider` | `#222828` | — | lines inside panels |
| `ControlBorder` | `#3c4444` | — | buttons, inputs; hover `#586060` |
| `ControlFill` | `#1e2424` | — | buttons; hover `#283030` |
| `RowSelect` | `#203038` | selected Local row | row hover/selection |
| `TextStrong` | `#e8e8e8` | — | values, active tab, state words |
| `TextBody` | `#c0c0c0` | member names | body text |
| `TextData` | `#a4a4a4` | — | coordinates, template names |
| `TextLabel` | `#888888` | column headers, window titles | labels (4.9:1 on `Panel`) |
| `TextDisabled` | `#5c6060` | — | disabled controls, NoIcon/partial overlays (graphics only, never body text) |
| `Accent` | `#14b0d0` | Overview "PvE" tab cyan | Clear state, focus ring, checked state; light variant `#5cc8e0` for ticks/text on fills |
| `AccentFill` | `#10303a` / border `#1490ac` | darker cyan tab | primary buttons, checked boxes, selected segment |
| `Hostile` | `#fc4444` | Overview "PvP" tab red | Hostile state, Enemy row text, popup bar |
| `Warning` | `#fce400` | chat link / warning yellow | Unreadable state, Neutral/Unknown row text, notices |
| `Ok` | `#8cc068` | online-status dot | small "good" dots only (calibration, ntfy delivered) |
| `Idle` | `#3c4444` | — | state line when Stopped / Setup needed |

Threat row tint: `Hostile` at 13% alpha, `Warning` at 10%. Danger button: fill `#3a1414`,
border `#a84c4c` (EVE system-message red), text `#ffd0d0`.

**Standing colours (overlay outlines and category chips)**

| Template | EVE icon fill | Chip / overlay | Category |
| --- | --- | --- | --- |
| `blue-star`, `blue-plus` | `#002078` | `#306cbc` (EVE notification-badge blue) | Friendly |
| `purple-person` | `#7820b0` | `#9a40d8` | Friendly |
| `green-star` | `#187818` | `#3ca03c` | Friendly |
| `red-minus` | `#900000` | `#fc4444` | Enemy |
| `orange-minus` | `#c04000` | `#e05a10` | Enemy |
| `gray-equals` | `#888888` | `#888888` | Neutral |
| no template matched | — | `Warning` `#fce400` | Unknown |
| no icon | — | `TextDisabled` | NoIcon |

**Chip = what EVE shows; tint and text colour = what the app thinks.** A neutral row keeps EVE's
grey chip, but its row is tinted `Warning` and its category word is `Warning`, because the app
treats neutrals as threats. Unknown is yellow because EVE uses yellow for "look here", and purple
is already taken by corp.

Template colours live in data, not code: `templates.json` entries gain an optional `"color"`
(`"#9a40d8"`), carried on `IconTemplate.Color` (`Bgr?`). Missing → category default (Friendly
`#306cbc`, Enemy `#fc4444`, Neutral `#888888`). Users' already-seeded `templates.json` has no
colours (the seeder never overwrites), so the fallback is what they see until they reseed;
M5's template cropper will write the field.

### 2.2 Type

- **Saira Semi Condensed** 400/500/600 for all UI text — the closest open font to EVE's squarish
  face. **Share Tech Mono** for counts, coordinates, scores and times.
- Both are SIL OFL 1.1 and **already in the repo**: `assets/fonts/Saira_Semi_Condensed/` (all nine
  weights) and `assets/fonts/Share_Tech_Mono/`, each with its `OFL.txt`. Embed only
  `SairaSemiCondensed-{Regular,Medium,SemiBold}.ttf` and `ShareTechMono-Regular.ttf` as
  `AvaloniaResource` (the other weights are unused); copy both `OFL.txt` files to the output. Fonts are
  files, not packages — this does not break the no-packages rule — and they are never fetched at
  runtime (network stays ntfy-only).
- **The Saira files are mislabelled.** Read from each file's `OS/2` and `name` tables:
  `SairaSemiCondensed-Medium.ttf` is weight 400, `-SemiBold.ttf` is 500 and `-Bold.ttf` is 600
  (`-Regular.ttf` is 300), and the family name inside is `Saira SemiCondensed`. The project embeds them
  under their true weights (`Link="Assets\Fonts\SairaSemiCondensed-Regular.ttf"` for the 400 file, and so
  on) and `Fonts.Ui` uses the real family name. The files in `assets/` were not renamed.
- Scale: 34 (state word), 26 mono (header figures), 15 (popup/section emphasis), 13 (panel
  titles, tabs, title bar), 12 (body), 11 (labels).

### 2.3 Shape and layout

- Square corners everywhere (radius 0). 1px borders. No gradients, no drop shadows except the
  popup (to lift it off the game) and the state-dot glow.
- Panel = `SectionPanel`: 28 px header with sentence-case title (`Rows [18]`), hairline below,
  optional kebab/actions at the right.
- Buttons 28 px tall (24 px in dense spots), 12 px text. Segmented groups share borders.
- Spacing: 10 px between panels and around the body; 12–14 px panel padding.
- Default window 1120 × 740 DIP, minimum 900 × 600.

### 2.4 App icon

The title-bar radar glyph (two rings and a sweep line) in `Accent` on a `TitleBar` square, so the
taskbar button matches the window. `tools/make-icon.ps1` draws it and writes
`assets/icons/EveLocalMon.ico` (16–256 px, the exe icon via `<ApplicationIcon>`, used by Explorer and a
pinned taskbar button) and `EveLocalMon.png` (256 px, embedded as `Assets/EveLocalMon.png` and set as
`MainWindow.Icon`, used by the running window's taskbar button). The script holds the three hex values
because an icon cannot read `Palette.cs`; change both together and rerun it. Checked: the icon extracts
from the built exe, reads at 16 px, and the live window answers `WM_GETICON` for both sizes. The popup and
picker windows are not in the taskbar and have no icon.

### 2.5 Accessibility

- State is always **word + colour + glyph**, never colour alone. Row categories always print the
  category name next to the colour chip.
- All icon-only buttons set `AutomationProperties.Name`. Every focusable control shows a 1px
  `Accent` focus outline; tab order follows reading order.
- Text contrast ≥ 4.5:1 against its background, labels included (the palette above passes).

---

## 3. Theme implementation

`EveLocalMonApp.Initialize`: `FluentTheme` → `RequestedThemeVariant = Dark` → `new EveTheme()`.
`EveTheme : Styles` defines, in C#:

- `Window`: `Background = Window`, `FontFamily = Fonts.Ui`, `Foreground = TextBody`.
- `Button` base / `.primary` / `.danger` / `.segment` / `.selected` / `.icon` (28 × 28, no border).
- `TabItem` restyle (or a custom strip of `ToggleButton`s if TabControl's template fights back —
  decide in §0): 13 px text, `TextLabel` inactive, `TextStrong` + 2px `TextBody` underline active.
- `CheckBox`: 18 px square, `Field` fill, `ControlBorder`; checked = `AccentFill` + `Accent` light tick.
- `TextBox` / `NumericUpDown`: `Field` fill, `ControlBorder`, mono font for numeric content,
  `Accent` border on focus.
- `Slider`: 2px track `Hairline`, filled part `Accent`, 10 × 14 rectangular thumb `TextBody`.
- `ScrollBar`: thin (6 px), `ControlBorder` thumb, no arrows.
- Style classes `.label`, `.mono`, `.panel-title`, `.value` for `TextBlock`.

No per-control colour literals in views — they reference `Palette` or a style class.

---

## 4. Status model (Core)

### 4.1 `MonitorStatus`

One pure function decides what the header, title-bar subtitle and state line show, so the
"never call unreadable clear" rule is unit-tested in one place instead of re-derived in views.

**The header follows `AlertState`'s debounced (stable) view, not the latest frame** (user
decision, 2026-11-04). A blinking criminal/suspect flag can flip a row between Enemy, NoIcon and
Unknown frame to frame; the header must not flicker with it, and it must never disagree with the
alerts it sits above — same source, so they agree by construction. The capture preview and the
rows table stay **raw** (they show exactly what the reader saw this frame), so the header may lag
them by up to `StableFrames` × interval (≈ 1 s at the defaults). That lag is intended.

`AlertState` exposes its stable values (today they are private to `StableTracker`):

```csharp
public sealed record StableView(
    bool Settled,             // false until StableFrames snapshots have been fed since construction
    int Threats,
    ThreatBreakdown Breakdown,
    Readability Readability,
    int? MemberCount,
    int VisibleRows);

public StableView Stable { get; }   // on AlertState; updated by Update(...)
```

`Threats`, `Readability` and `MemberCount` are the trackers' `Stable` values. `Breakdown` and
`VisibleRows` come from the **most recent snapshot that agreed with the stable values** (raw
threat total == stable threats and raw readability == stable readability). A disagreeing frame
leaves them unchanged, so a one-frame flash never reaches the breakdown text either.

`Settled` exists because the trackers start at "0 threats, Full": without it the header would
say **Clear** for the first second of every run, before anything was confirmed. **As built, `Settled`
means the threat count and the readability have each had a run of `StableFrames` identical raw
readings** (the initial value does not count), not merely that `StableFrames` frames have been fed. The
looser reading would settle on a flickering first few frames and show Clear while a threat is visible.
Adding `StableView` does not change which events `AlertState` emits; its existing tests are untouched.

```csharp
public static MonitorStatus Compute(
    bool running, bool hasRegion, bool hasCalibration,
    StableView? stable, string? captureError);

public sealed record MonitorStatus(
    MonitorState State,
    string Word,            // "Clear", "Hostile", "Unreadable", "Checking", "Stopped", "Setup needed"
    string Reason,          // one line, shown under the header / in the compact strip tooltip
    int? InLocal,           // stable header count; null = unreadable
    string Readable,        // "18 / 18", "39 / 48", "0 / ?"
    int? Threats,           // null when NotVisible ("?")
    ThreatBreakdown? Breakdown);
```

`MonitorState` gains **`Checking`** (state line and dot `Idle`, word in `TextLabel`).

Precedence (first match wins):

| # | Condition | State | Word | Reason |
| --- | --- | --- | --- | --- |
| 1 | `!running` | Stopped | Stopped | "Not watching — alerts are off" |
| 2 | `!hasRegion` or `!hasCalibration` | SetupNeeded | Setup needed | "Select a region" / "Calibrate the region" |
| 3 | `captureError != null` | Unreadable | Unreadable | "Capture failed: {error}" |
| 4 | `stable` null or `!stable.Settled` | Checking | Checking | "Reading Local…" |
| 5 | `stable.Threats > 0` | Hostile | Hostile | breakdown wording from `AlertMessageFactory` (+ " · list overflowing" if Overflow) |
| 6 | Readability `NotVisible` | Unreadable | Unreadable | "Local not visible — covered, minimised or moved" |
| 7 | Readability `Overflow` | Unreadable | Unreadable | "{n} in Local, {v} readable" |
| 8 | otherwise | Clear | Clear | "All rows readable" |

Rule 3 is deliberately **not** debounced: a capture failure means there is no frame to debounce,
and it must show at once. Rule 5 above rule 7 on purpose: a visible hostile in an overflowing list
is the more urgent fact, and the overflow is still stated. Expose the breakdown wording helper
from `AlertMessageFactory` (`DescribeBreakdown` → `public`) rather than duplicating it.

`MainWindow` already creates a fresh `AlertState` on every Start, so each run begins in Checking.

### 4.2 "Last scan"

`MonitorLoop` skips unchanged frames, so "last update" would freeze on a quiet Local. Add
`event Action<DateTimeOffset>? Ticked`, raised (on the UI thread) after every capture, changed or
not. The header shows seconds since the last tick, refreshed by a 250 ms `DispatcherTimer`; if no
tick for 3 × interval, the figure turns `Warning` — the loop has stalled, which is not "clear".

---

## 5. Window shell

### 5.1 Title bar (30 px, `TitleBar`)

Left: radar glyph in `Accent`, "EveLocalMon", label "v{assembly version} · watching Local" /
"· stopped". Right: **pin** (toggles `Topmost`, persisted as `Ui.KeepOnTop`, shows filled when
on), minimise, close. Drag anywhere on empty bar area; double-click toggles maximised.

Directly under it, a **2px state line** in the state colour (`Accent` / `Hostile` / `Warning`,
`Idle` when Stopped or Setup needed). It is the one element visible at a glance from across the
desk, so it is never hidden.

### 5.2 Status header

- **Full (Monitor tab, 90 px):** left block — label "Local status", state dot, state word at 34 px
  in the state colour. Four figure cells: *In Local (header)*, *Readable rows*, *Threats* (value
  in `Hostile` when > 0; breakdown "1 hostile · 1 neutral · 0 unknown" below), *Last scan*
  (seconds since tick, sub-label is the **scan interval picker**: 250 / 500 / 1000 / 2000 ms).
  Right block — primary **Start watching / Stop watching**, and the **mute button**: "Mute 15 min"
  when unmuted; "Muted · 12:41 left" in `Warning` text when muted, click unmutes.
- **Compact (Alerts, Calibration, 44 px):** dot + state word (18 px), In Local, Readable,
  Threats inline, Start/Stop at the right.

### 5.3 Tab strip (36 px)

Tabs **Monitor**, **Alerts**, **Calibration**. Right side, per tab: Monitor and Calibration show
"Region 148 × 690 at 1712, 214" (mono values) + **Select region…**; Alerts shows "Changes apply
and save immediately".

### 5.4 Footer (26 px)

Left: the latest status message (settings saved, errors, "Region selection cancelled") — this
replaces today's multi-line status `TextBlock`. Right: calibration dot (`Ok` good, `Warning`
partial, `Hostile` failed, grey none) + "Calibration good (auto-detected)" / "(manual)" +
"{n} templates loaded".

**Removed:** the toolbar and the **Save settings** button. Every change saves when it is made
(region pick, interval change, alert settings, calibration Apply, pin), as the alerts panel
already does.

---

## 6. Session log (Core)

`SessionLog`: newest-first list, capped at 200 entries, in memory only (a restart starts clean;
persistent history stays in M5's "detection history").

```csharp
public enum SessionEntryKind { Info, Threat, Unreadable, Clear, Error }
public sealed record SessionEntry(DateTimeOffset At, SessionEntryKind Kind, string Title, string Detail);
```

Entries come from: watching started/stopped (with region + interval), region picked, calibration
detected (confidence + pitch) or applied manually, every dispatched alert, channel failures, test
sends, mute/unmute.

For alerts, `Detail` says what actually went out, which needs the dispatcher to report it:

```csharp
public sealed record DispatchOutcome(
    ChannelResult Sound, ChannelResult Popup, ChannelResult Ntfy);
public enum ChannelResult { Disabled, Sent, Muted, RateLimited, Failed }
```

`AlertDispatcher.DispatchAsync(AlertMessage)` returns `Task<DispatchOutcome>`; the event overload
returns one per message. Rendered as e.g. "1 red, 1 neutral — sound · popup · ntfy sent",
"popup sent · sound, ntfy muted", "ntfy rate-limited". Existing dispatcher tests keep passing:
awaiting a `Task<T>` is source-compatible.

---

## 7. Monitor tab

Grid: **Capture** (196 px) | **Rows** (fill) | **Session** (300 px).

- **Capture** — the region at 1:1 physical pixels, as now (interpolation `None`, sized
  `pixels / RenderScaling`), on `Field` with a 1px dashed `ControlBorder` outline; scrolls vertically
  when the region is taller than the panel. Each row's outline is its **template's EVE colour**
  (§2.1 standing table), so a corp member is outlined purple and a fleet member green, as in the
  client; Unknown `Warning`, NoIcon `TextDisabled`, partial rows dashed `TextDisabled`.
  `PreviewRenderer.Render` takes the `TemplateSet` to look colours up by `RowResult.TemplateName`.
  Legend below: Friendly (blue/purple/green chips) / Enemy (red/orange) / Neutral (grey) / Unknown
  (yellow).
- **Rows [n]** — replaces the monospace text table with a real list (virtualised `ItemsControl`).
  Columns: Row, Slot y, Kind (full/partial), Category (template-colour chip + name), Template,
  Best, 2nd. Empty full rows are omitted, as today. Enemy rows: `Hostile` tint and text;
  Neutral/Unknown/extra-NoIcon rows: `Warning` tint and text; friendly rows `TextBody`. **NotVisible** replaces the table with an empty state: warning glyph, "No
  member list found in the region", and "This is not 'all clear' — alerts stay armed."
- **Session** — `SessionLog` entries: mono `[HH:mm:ss]`, title coloured by kind (Info `TextBody`,
  Threat `Hostile`, Unreadable `Warning`, Clear `Accent`, Error `Hostile`), detail as label. Footer: "Channels for
  threats" chips (Sound / Popup / ntfy) showing which are enabled; when muted, Sound and ntfy
  chips render struck-through in `TextDisabled`.

---

## 8. Alerts tab

Two columns.

**Left**
- **When to alert** — a matrix: rows *Threat appears* (red chip; "Hostile, neutral or unknown
  icon, or the threat count rises"), *Local unreadable* (yellow; "Window covered, minimised,
  scrolled past, or header lost"), *All clear* (cyan; "Threats drop back to zero"); columns Sound /
  Popup / ntfy as 18 px checkboxes. Footer row: one **Test** button per column. Header note
  "rate-limited per channel". Maps 1:1 onto `AlertSettings.Threats/Unreadable/AllClear`.
- **Sound** — volume slider 0–100 with mono % readout; label "Built-in chime, synthesised at
  runtime · plays even when Windows is in Focus Assist". Releasing the slider plays the chime once
  at the new volume.
- **Mute** — segmented **15 min / 30 min / 1 hour / Unmute**; the active preset is `.selected`.
  Status line in `Warning`: "Muted until 19:21 — sound and ntfy are silent; popups still appear",
  or label "Not muted." Uses the existing `MuteFor`/`Unmute`; mute stays in-memory only (M3
  decision: a restart never leaves you silently muted).

**Right**
- **ntfy push** — header status dot + "last test delivered 19:02" / "last send failed 19:02" /
  "not tested yet". Server field, Topic field + **Generate**, yellow `Warning` notice "Anyone who knows the
  topic name can read your alerts. Keep the generated random name and don't post it in chat.",
  and a mono "Subscribe on your phone" line `{server-host}/{topic}` (selectable text, so it can be
  copied).
- **Popup** — one-line description of placement and focus behaviour, plus a live sample popup
  rendered with the real `AlertPopupWindow` content control (not a screenshot).

Every change applies and saves immediately (debounce text fields by 500 ms), replacing the
current **Apply** button.

---

## 9. Alert popups

`AlertPopupWindow` keeps its M3 behaviour (borderless, `Topmost`, `ShowActivated = false`, never
in the taskbar, bottom-right of the watched region's monitor, max 3 stacked, click dismisses).
New look, 360 × 96 DIP:

- Background `Window` at 96% opacity, 1px `ControlBorder` border, soft shadow; **2px top bar** in the
  kind colour.
- 52 px icon column: Threat → warning triangle `Hostile`; Unreadable (not visible) → crossed eye
  `Warning`; Overflow and pilots-joined-while-unreadable → list-overflow glyph `Warning`;
  LocalClear / Readable → check circle `Accent`.
- Title 15 px semibold in the kind's text colour; mono time right-aligned; body 12 px (one line,
  ellipsis); **meta line** 11 px label built by the app from the current `MonitorStatus` at
  presentation time, e.g. "20 in Local · all rows readable".
- **Countdown bar**: 2px `TextDisabled` along the bottom edge shrinking from full width to zero over
  `Lifetime` (8 s). Hovering the popup pauses the countdown and restores it to full; leaving
  resumes. Tick at ~30 Hz via `DispatcherTimer`, stopped on close.

Wording changes in `AlertMessageFactory` (the one place wording lives):

| Event | Kind | Title (was) | Title (now) |
| --- | --- | --- | --- |
| Threat, from 0 | ThreatIncreased | EVE: hostile in Local | Hostile in Local |
| Threat, increase | ThreatIncreased | EVE: more hostiles in Local | More hostiles in Local |
| Local clear | LocalClear | EVE: Local clear | All clear |
| Not visible | Unreadable | EVE: Local unreadable | Local unreadable |
| Overflow | **Overflow** (new) | EVE: Local unreadable | Local unreadable — too long |
| Readable again | Readable | EVE: Local readable again | Local readable again |
| Joined while unreadable | MemberCountWhileUnreadable | EVE: pilots joined, Local unreadable | Pilots joined, Local unreadable |

Bodies are unchanged. The "EVE: " prefix moves to `NtfyClient` (a phone notification needs to
say which app it is; an on-screen popup next to the game does not). `AlertKind.Overflow` routes
to the **Unreadable** channel set in `AlertSettings.ChannelsFor` and gets the `warning` ntfy tag —
routing does not change, only the visual. The §4.6 invariant test still holds: every unreadable
title contains "unreadable" and never "hostile".

---

## 10. Calibration tab

Two columns: **Guides** (380 px) | detection + **Manual override**.

- **Guides** — the captured region rendered by `PreviewRenderer` with guides only (no row
  classification), zoom segmented **1× / 2× / 4×** (persisted as `Ui.CalibrationZoom`, default 2),
  nearest-neighbour, scrollable. Guide colours: list rect dashed `TextData`; icon column left/right
  edges `Accent`; name x `TextStrong`; header count rect `Warning`; the first two row-slot
  boundaries dotted `TextDisabled` to show the pitch. Legend below. Guides are composited into the bitmap like
  today's overlays, so at 4× a guide is 4 px wide — intended: it is drawn on the exact pixel column
  the reader uses.
- **Detection banner** — icon + "Auto-detect: good / partial / failed" (cyan / yellow / red),
  "Detected 19:04:51 from region 148 × 690 · header reads 18, matching 18 occupied rows", the
  detector's `Notes` as bullet lines, and **Re-detect**.
- **Manual override** — four groups, each headed by its guide's line sample so fields and guides
  match visually: *Icon column* (Icon x, Icon size), *Name* (Name x, Name right), *Rows* (Row
  pitch, List top, List height — with "{n} slots" hint), *Header count* (x, y, w, h). Each field is
  a `Stepper` (−, mono value, +; arrow keys ±1, Shift ±10).
- **Draft model.** Fields edit a draft `Calibration`. Any change re-renders the guides from the
  most recent frame within 150 ms (the live frame while watching; otherwise one capture when the
  tab opens). Footer: "No unapplied changes" / "3 unapplied changes", **Revert to detected**, and
  primary **Apply**. Apply saves and restarts the loop if running (today's behaviour). Leaving the
  tab with unapplied changes keeps the draft; the tab label shows "Calibration •".

---

## 11. Region picker

Same mechanics as M2 §3 (one overlay per monitor, physical-pixel positions, Esc cancels and
changes nothing). New presentation:

- **Dimming outside the selection only**: four `rgba(0,0,0,0.55)` rects around the selection, so
  the selected pixels show undimmed — what you see inside is what will be captured. Before a drag
  starts the whole screen is dimmed.
- Selection: 1px `Accent` border with 8 px corner ticks in `Accent` light.
- **Hint card**, top-centre of each monitor: region glyph, "Drag around the Local member list",
  label "Just the names column — leave out the chat text", and an `Esc` keycap + "cancel".
- **Readout card**, beside the selection (flips side near a screen edge): mono "148 × 690 px",
  "at 1712, 214 · monitor 2 · 125%", and the **width check**: width ≤
  `RegionHeuristics.MaxPlausibleListWidth` → `Accent` tick "Width fits a member list"; wider →
  `Warning` "Wider than a member list — include just the names column". Same threshold as the
  existing post-pick warning, now shared from Core.
- **Loupe**, 112 × 112 DIP, beside the cursor (flips near edges): 14 × 14 physical pixels around
  the cursor at 8×, nearest-neighbour, `Accent` crosshair on the cursor pixel, "8×" label. Shown
  while hovering and while dragging. Source pixels come from **one capture of each monitor taken
  when the picker opens** (the app is already minimised by then); live capture per mouse move
  would cost a GDI blit per event for no benefit, since the screen is static while picking.

---

## 12. Settings

`AppSettings` schema **v3** adds:

```csharp
public sealed record UiSettings
{
    public bool KeepOnTop { get; init; }          // default false
    public int CalibrationZoom { get; init; } = 2; // 1, 2 or 4; anything else → 2
}
```

A v2 file loads with defaults for `Ui`; a v1 file still loads. `SettingsStore.Save` always writes the
current schema version, so an older file is upgraded the first time it is saved instead of carrying a `ui`
block under an old number.

`EVELOCALMON_DATA_DIR` (new, `SettingsPaths.DataDirectoryVariable`) moves the whole data folder
(settings and templates). It exists so a test run cannot touch the real settings, and doubles as a
portable-install switch.

---

## 13. Tests

Automated (`dotnet test EveLocalMon.sln --filter "Category!=Perf"` stays green; 174 pass):

| Area | Test |
| --- | --- |
| `MonitorStatus` | Every precedence row in §4.1, including: unsettled (or null) stable view is Checking, never Clear; overflow **with** a threat is Hostile and its reason mentions overflow; NotVisible is never Clear; capture error is Unreadable even before settling; no calibration is SetupNeeded |
| `AlertState.Stable` | Fresh state is not Settled; Settled after `StableFrames` updates; a single-frame threat flash changes neither `Threats` nor `Breakdown`; two agreeing frames change both; `Breakdown`/`VisibleRows` come from the last agreeing snapshot; events emitted for a recorded sequence are identical with and without reading `Stable` |
| `MonitorStatus` | `Readable` text: "18 / 18" full, "39 / 48" overflow with header, "39 / ?" overflow without header, "0 / ?" not visible; `Threats` null when NotVisible |
| `SessionLog` | Newest first; capped at 200 (oldest dropped); entries immutable |
| `DispatchOutcome` | Disabled channel → Disabled; muted → popup Sent, sound/ntfy Muted; second alert inside the window → RateLimited; throwing presenter → Failed and others still Sent; ntfy false → Failed |
| Wording | New titles per §9; Overflow event → `AlertKind.Overflow`; unreadable invariant test unchanged and passing |
| Routing | `ChannelsFor(Overflow)` returns the Unreadable set |
| ntfy | Title header is "EVE: " + message title; Overflow → priority high, tag warning |
| Settings | v3 round-trip; v2 file loads with `Ui` defaults; `CalibrationZoom` 3 → 2 |
| `RegionHeuristics` | 400 is plausible, 401 is not (pins the shared threshold) |
| Template colours | Shipped `templates.json` gives every template the colour in §2.1; an entry without `"color"` or with a malformed one loads with `Color == null` and does not throw |

The App project has no test project; UI is covered by the manual list below.

Manual acceptance (also re-run the still-outstanding M2 §9 and M3 §8 lists on the new UI — they
exercise the same screens). **Done here** means run against the real app on a 3440 × 1440 display at
100% with a live EVE window; **needs a person** means it could not be exercised from here:

- Done here: 1 (fonts render from the embedded files; whether this machine also has system copies of
  them was not checked), 2 (drag, resize, maximise, pin persisted), 5 (log shows start, alerts and
  the channels that fired), 6 (popup look and placement; countdown hover not observed), 7 (hint card,
  readout, width check, selection, loupe), 8 (draft count, tab marker, guides moving; Apply while
  watching not exercised), and the Unreadable and Hostile paths of 3.
- Needs a person: the 125%/150% monitor and second-monitor cases in 7, popup focus behaviour against a
  fullscreen EVE (6), 4 (a deliberately stalled loop), the keyboard-only pass (9), a real ntfy push, and
  the M2/M3 lists.

1. Fonts render as Saira / Share Tech Mono on a machine that has neither installed.
2. Custom title bar: drag moves, double-click maximises, edges resize, Aero Snap works, pin keeps
   the window above EVE and survives a restart.
3. Header follows the preview within about 1 s and never flickers: Start → "Checking" briefly,
   never Clear first; cover Local → Unreadable (yellow, never Clear); a hostile enters → Hostile
   with breakdown, at the same moment the alert fires; a flashing suspect/criminal flag does not
   make the header flicker; shrink Local until it scrolls with no threats → Unreadable "n in
   Local, v readable".
4. Last scan keeps counting up from 0 on a static Local (ticks continue), and turns yellow if the
   loop stalls.
5. Session log shows start, alerts with the channels that actually fired, mute, and failures; a
   muted alert reads "popup sent · sound, ntfy muted".
6. Popup: countdown shrinks over 8 s; hovering pauses and refills it; never takes focus from EVE;
   overflow and not-visible show different icons.
7. Picker: outside dimmed, inside undimmed; loupe tracks the cursor at 8× on a 125% monitor and on
   a second monitor; a 600 px drag shows the yellow width warning; Esc changes nothing.
8. Calibration: typing in a field moves the guide within ~150 ms; 4× zoom stays pixel-sharp;
   Revert restores detected values; Apply while watching restarts with the new numbers.
9. Keyboard only: every control reachable with Tab, focus ring visible, Space/Enter activate.

---

## 14. Differences from the mockups (deliberate)

- **Palette.** The mockups use a cooler blue-grey set and invented row colours; §2.1 replaces them
  with colours sampled from the EVE client and its standing icons (user decision, 2026-11-04).
  Layout, sizes and type in the mockups still apply.

- Rows table shows **"No icon"**, not "No icon (you)": the reader cannot know which no-icon row is
  the active client's pilot.
- Overflow popup title is **"Local unreadable — too long"**, not "Local too long to read", to keep
  the M3 invariant that every unreadable title says "unreadable".
- The **scan interval** control (missing from the mockups) is the sub-label of the Last scan cell.
- The **Save settings** button and toolbar are gone (§5.4).

Differences found while building (user not asked; each is the smaller change):

- **Popup body is up to two lines**, not one. A one-line ellipsis would cut "… Cannot confirm Local is
  clear." from an unreadable alert, which is the part that matters. The popup window is also 16 DIP larger
  than the 360 × 96 card so its shadow has room on a transparent window.
- **Session panel has a `Clear` button** where the mockup has a kebab: a menu with one item adds an
  un-themed Fluent flyout for nothing.
- **The Capture column grows** to fit a region wider than ~150 px (up to 340 px, then it scrolls)
  instead of clipping. The user's own region is 180 px.
- **Button colour variants are per-instance** (§0.3), not style classes.
- **The picker's window background is 1/255 black**, not transparent, so the interior still receives the
  drag (a fully transparent layered window can let clicks fall through).
- **"Checking" is shown in `TextLabel` grey with an outline dot**, like Stopped, as §4.1 says; it is not
  a fourth colour on the state line.

---

## 15. Risks

- **Custom chrome on Avalonia 12** is the most likely time sink (§0.1). Fallback: keep the native
  title bar with `RequestedThemeVariant = Dark` and put the pin button in the tab strip — the rest
  of the spec does not depend on custom chrome.
- **Fluent template overrides** can break on Avalonia upgrades. Keep overrides to template parts
  that exist in 12.1.2 and keep them in `EveTheme.cs` only.
- **Readability vs. look.** Small grey labels on charcoal are what make the EVE look, and also what
  makes UIs unreadable. The palette is chosen to pass 4.5:1; don't darken labels "to taste".
- **Loupe snapshot is stale** if something on screen moves while picking. Acceptable: it is an
  aiming aid, and the real capture happens after the pick.

---

## 16. Out of scope

Tray icon, start minimised, template cropper ("add as friendly"), persistent detection history
(**M5**); installer/autostart (M6); macOS (M7); light theme; user-selectable accent colour.
