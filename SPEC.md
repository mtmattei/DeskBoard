# DeskBoard v2 — Ship-Ready Rebuild Spec

DeskBoard v1 is a working prototype: full-screen WPF overlay, InkCanvas, gradient tray,
ad-hoc sticky notes, no undo, no objects, no zoom. v2 rebuilds the visual language,
interaction model, and rendering quality to a polished Windows productivity tool that
convincingly turns the desktop background into an interactive whiteboard.

## Architecture Brief

**Stack (unchanged):** WPF, `net10.0-windows`, zero NuGet packages, WinForms `NotifyIcon`
tray. WPF stays because the entire app is InkCanvas + native overlay windowing — existing
project code wins for local conventions.

**Pattern:** code-behind + services, no MVVM framework. This app is 90% direct canvas
manipulation (hit-testing, mouse capture, transforms); a binding layer would add ceremony
without testability gains.
- Decision: code-behind shell + plain services. Reason: manipulation-heavy canvas app.
  Tradeoff: less unit-testable; mitigated by pulling pure logic (undo, storage, geometry)
  into service classes.

**Module structure:**

```
DeskBoard/
├─ App.xaml                     merges Themes/*
├─ Themes/Tokens.xaml           easing KeySplines, durations, radii, palette, shadows
├─ MainWindow.xaml              shell: board chrome, viewport, layers, tray, HUD
├─ MainWindow.xaml.cs           window/mode/hotkey/tray-icon lifecycle
├─ MainWindow.Input.cs          keyboard shortcuts, paste, drag-drop, zoom/pan
├─ MainWindow.Items.cs          item creation, selection ops, context actions
├─ MainWindow.Persistence.cs    load/save orchestration
├─ Controls/MarkerTray.xaml(.cs) tray UserControl: markers, tools, events out
├─ Board/BoardItemKind.cs       enums + item model
├─ Board/BoardItemView.cs       interactive item container (chrome, drag/resize/rotate)
├─ Board/BoardItemContent.cs    per-kind content builders (note/text/image/link/file)
├─ Board/UndoStack.cs           IBoardEdit + stack (strokes, items, transforms, clear)
├─ Services/BoardStorage.cs     JSON v2 + ISF + assets folder, v1 migration
├─ Rendering/TextureFactory.cs  procedural noise / brushed-metal tiles, frozen brushes
└─ Native/NativeMethods.cs      existing + monitor work-area
```

**Visual/interaction layer stack** (inside a zoom/pan-transformed `BoardContent` grid):

1. `ItemsCanvas` (Canvas) — board objects, z-ordered
2. `Ink` (InkCanvas, transparent bg) — strokes render **above** items (marker draws over
   pinned paper, like a real board)
3. `SelectionLayer` (Canvas) — marquee rect + selection chrome live on top

Hit-testing is **tool-modal**: Marker/Eraser → `Ink.IsHitTestVisible=true`, items false;
Select → inverse. Board chrome (surface, frame) sits below the viewport, tray + HUD above.

**State model:** `_mode` (Ambient | Board), `_tool` (Select | Marker | Eraser | Text),
marker color, selection set (`List<BoardItemView>`), `UndoStack`. Zoom/pan =
`ScaleTransform`+`TranslateTransform` on `BoardContent` (zoom 1.0–3.0 into the
screen-sized board — a physical board has fixed bounds, and this keeps v1 stroke
coordinates valid).

**Persistence:** `%AppData%\DeskBoard\` — `board.isf` (strokes, unchanged),
`board.json` v2 `{version:2, items:[{id,kind,x,y,w,h,rotation,z,pinned,groupId,color,
fontSize,text,asset,url,title}]}`, `assets\{guid}.png` for pasted/dropped images.
v1 `board.json` (bare array) migrates on load. Debounced save (1.5s) kept.

**Platform constraints:** primary monitor only (v1 limitation stands). Ambient mode stays
click-through with ink+items visible over the desktop wallpaper, behind app windows.

**Testing/validation:** `--board` CLI arg starts directly in Board mode so the build can
be launched and screen-captured (PowerShell CopyFromScreen) for visual verification.
Build after each phase; screenshot-verify after visual phases.

## Design Brief

**Direction:** restrained realism. The board must read instantly as a dry-erase surface
through light and material, never through cartoon skeuomorphism. One memorable thing:
the marker tray with real markers lying in it, the selected one lifted and uncapped.

**Board surface (Board mode):**
- Base `#FBFCFD` with a very subtle cool radial light (center-top brighter, corners
  ~3% darker) — a lit board, not flat white.
- Procedural fine-grain noise tile (WriteableBitmap, ~2% contrast, frozen ImageBrush,
  tiled) for porcelain tooth.
- Frame (amended per user direction, 2026-07-22): neumorphic — 18px soft matte band
  in the surface's own tone (`#E3E8ED`), lit top-left, board pressed into a rounded
  (r20) inner opening: dark inset ring top-left, light catch bottom-right. Replaces
  the original aluminum-edge concept.
- No smudge/ghosting decals — clean board, texture carries the realism.

**Marker tray:** centered floating shelf (~760×card), never full-width.
- Shelf: brushed aluminum via procedural horizontal-streak tile + vertical gradient,
  front lip (darker face below a hairline top highlight), two slot-head screws,
  layered shadow onto board (tight `#14000000` contact + wide `#0F000000` ambient).
- Markers: vector markers lying horizontally — body in ink color, darker cap to the
  left, gray nib collar. Selected marker translates up 8px & drops its cap dot,
  200ms EaseSmooth. Hover lifts 3px.
- Eraser: felt block with darker felt base.
- Tools right of a divider: Select / Text / Note / Image / Undo / Redo / Clear / Hide
  as icon glyph buttons (Segoe MDL2 Assets / Segoe Fluent Icons), 0.98 press scale.

**Board objects — attachment language:**
- Sticky notes: 4 paper colors (yellow `#FFEE8C`, blue `#BEE3F5`, pink `#F8C8CF`,
  green `#CDEBC5`), slight random rotation ±2.5° on placement, bottom curl shadow
  (gradient, 4–6% black), folded-corner hint.
- Images/screenshots: white 6px mat border, two translucent tape strips across the top
  corners (rotated ±40°, `#66FFFFFF` with hairline edges).
- Link cards: compact white card, globe glyph + title + domain, red push-pin dot at top.
- File cards: white card, file-type glyph + name, push-pin.
- All items: layered shadow (contact 8% + ambient 4%), never pure black.

**Typography:** handwriting = Ink Free (fallback Segoe Print) for board text + notes;
UI chrome = Segoe UI Variable (fallback Segoe UI) for cards/HUD/tooltips. No literal
font-size sprinkling — sizes come from Tokens.xaml doubles.

**Motion tokens (xaml-design-polish house values, in Tokens.xaml):**
EaseSmooth `0.22,1 0.36,1` (default) · EaseOut `0.17,1 0.32,1` · durations 150/200/280ms
· radii 6/12 · press scale 0.98 · shadow opacities 2–8%. Default WPF easing is banned.

**Mode transition:** board surface fades in 280ms EaseSmooth + tray rises 12px from
bottom with fade; hide reverses at 200ms. No system-default animation anywhere.

## Interaction Brief

**Tools:** Select (default after non-draw actions), Marker (4 colors + custom via tray
right-click… keep v1 tray-menu color picker), Eraser (stroke-erase), Text (click to
place handwriting text). Notes/images/links arrive via tray buttons, paste, or drop.

**Ink feel:**
- `FitToCurve=true` (Bezier smoothing), pressure kept for stylus (`IgnorePressure=false`).
- Marker nib: `StylusTip.Rectangle`, W≈2×H at a fixed nib ratio for chisel character;
  eraser unchanged.
- Latency: `Stylus.IsPressAndHoldEnabled=false`, `IsFlicksEnabled=false`,
  `IsTapFeedbackEnabled=false` on the window; no Effects on any ancestor of the
  InkCanvas; all static brushes frozen.
- Mouse strokes get a light Catmull-Rom resample on `StrokeCollected` when the point
  spacing is jittery; stylus strokes pass through untouched.

**Selection & manipulation (Select tool):**
- Click selects; Shift+click adds; click empty deselects; drag empty = marquee.
- Selected chrome: 1px hairline ring (accent `#2E6FE0` at 80%) + 4 corner resize
  handles (10px squares) + rotate handle 24px above top-center. Hover (unselected):
  hairline ring at 30%.
- Drag anywhere on item moves it (and its group). Resize: corners, proportional for
  images (Shift = free). Rotate: drag handle, snaps to multiples of 15° within 3°.
- Pinned items show a pin glyph and refuse move/resize/rotate until unpinned.
- Group: Ctrl+G / Ctrl+Shift+G on multi-selection → shared GroupId, moves/deletes as
  one. (Group resize/rotate out of scope v2.)
- Z-order: context menu Bring Forward / Send Backward / To Front / To Back.
- Double-click note/text → inline edit (TextBox focus); Esc or click-away commits.
- Double-click link/file card → open in browser/shell.

**Context menu (right-click item):** Edit, Pin/Unpin, Duplicate, note-color submenu,
z-order submenu, Delete. Right-click empty board: Paste, Add note, Add text, Select all,
Clear board.

**Keyboard:** Ctrl+Z / Ctrl+Y undo/redo · Delete removes selection · Ctrl+D duplicate ·
Ctrl+A select all · arrows nudge 1px (Shift 10px) · Ctrl+V paste (image → ImageItem,
file paths → image/file cards, URL text → link card, other text → note) · Ctrl+G group ·
Esc deselect, then hide board · Ctrl+0 reset zoom · Ctrl+scroll zoom at cursor ·
Space-drag or middle-drag pan · V/B/E/T select/marker/eraser/text.

**Undo model:** edits for stroke add/erase, item add/remove, transform (move/resize/
rotate as one edit per gesture), pin, recolor, z-order, group, clear (snapshot).
Text edits are not undoable (commit-on-blur), accepted scope cut.

**States:**
- Empty board (first Board entry, nothing saved): centered handwriting hint
  "draw with a marker below — paste or drop anything onto the board", fades 280ms on
  first stroke/item, never returns once board has content.
- Loading: strokes/items load synchronously (small local files); images decode async
  with a neutral placeholder card and swap-in fade 150ms.
- Errors: corrupt save → moved to `.bak` + tray balloon (v1 behavior kept); image
  decode failure → card shows "image unavailable" glyph state; hotkey conflict →
  balloon (kept).
- Disabled: Undo/Redo tray buttons at 40% opacity when stacks empty; paste menu item
  disabled when clipboard has nothing usable.

**Feedback:** every tray button presses to 0.98 · marker selection lifts the marker ·
drop target: board shows faint accent inset ring during drag-over · zoom HUD chip
(bottom-right, "125%") appears on zoom change, fades after 1.2s.

**Accessibility:** all tray controls keyboard-focusable with visible focus ring;
tooltips on every tool; respects `SystemParameters.ClientAreaAnimation` — when off,
transitions jump to end state.

**Runtime verification:** launch `--board`, screenshot → verify surface/tray/notes;
scripted checks: draw (mouse drag), paste image, select/move/resize, undo, toggle
ambient (Ctrl+Alt+D), relaunch → persistence.

## Implementation Plan

1. **Foundation** — Tokens.xaml, TextureFactory (noise + brushed metal), restructure
   MainWindow into partials. Build.
2. **Board surface + frame + mode transitions** — visual base, `--board` arg. Build +
   screenshot.
3. **Marker tray** — MarkerTray control, markers/eraser/tools, events, animations.
   Build + screenshot.
4. **Ink engine** — nib, smoothing, latency flags, stylus settings. Build.
5. **Object system** — BoardItemView (chrome, drag/resize/rotate/pin), content kinds,
   paste/drop, context menus. Build + screenshot.
6. **Undo/redo + keyboard + zoom/pan.** Build.
7. **Persistence v2 + migration + async images.** Build.
8. **States & polish pass** — empty state, HUD, disabled states, transitions,
   final screenshot verification + README update.

## Unresolved Questions

Resolved by session assumption (autonomous run):

- Ink above or below objects? → **Above** (marker writes over pinned paper). Accepted.
- Group resize/rotate? → **Out of scope v2** (move/delete/duplicate only). Accepted.
- Text-edit undo? → **Not undoable v2.** Accepted.
- Multi-monitor? → **Primary only**, unchanged v1 limitation. Accepted.
- Smudge/ghosting decals on surface? → **No** — clean board; noise texture carries
  realism. Accepted.
