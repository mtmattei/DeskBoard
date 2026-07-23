# DeskBoard

A full-screen whiteboard overlay for Windows 11. Toggle it on with a global hotkey and
the screen becomes a dry-erase board set into a soft neumorphic frame, with a
full-width brushed-steel marker rail along the bottom. Draw with chisel-nib markers, pin images and screenshots, stick notes, drop links
and files. Toggle it off and it becomes transparent and click-through — your ink and
pinned content stay visible over the desktop wallpaper, behind your app windows.

## Requirements

- Windows 10/11
- .NET 10 SDK (`net10.0-windows`)

No NuGet packages — WPF + WinForms `NotifyIcon` only; builds and runs fully offline.

## Build & run (PowerShell)

```powershell
cd DeskBoard
dotnet build -c Release
dotnet run -c Release
```

The app has no window chrome — it starts as an invisible click-through overlay plus a
tray icon. `DeskBoard.exe --board` starts directly in Board mode (useful for demos and
screenshots).

## Use

- **Global hotkeys** (work from anywhere, any mode):
  - **Ctrl+Alt+D** — toggle the board (left-clicking the tray icon does the same).
  - **Ctrl+Alt+A** — ink on desktop: board hidden, content superimposed on the
    wallpaper, click-through (default background state).
  - **Ctrl+Alt+H** — hide everything. Due/overdue reminders stay glanceable as a
    compact strip in the bottom-right corner of the wallpaper.
  - **Ctrl+Alt+N** — quick note: a floating sticky pops up; type, Enter, it's pinned
    to the board without opening it.
  - **Ctrl+Alt+S** — snip to board: drag a screen region and it lands as a taped
    image, opening the board to show it.
- **Reminders**: the reminder magnet (or **R**) adds a card with a calendar chip;
  click the chip to set a date. The day it comes due, DeskBoard raises a Windows
  notification (once per day; click it to open the board), and overdue chips turn
  red.
- **Marker rail** (full width along the bottom, Board mode):
  - Four markers — the active one lifts out of the tray, uncapped. Right-click a
    marker for a custom color.
  - Felt **eraser** — stroke erase.
  - **Select** (V) · **Text** (T) · **Note** (N) · **Image…** — tools and content.
  - **Undo / Redo / Clear / Hide**.
- **Content**: paste (Ctrl+V) or drag-drop images, screenshots, files, URLs, and text
  directly onto the board. Images pin with tape strips; links and files become cards
  (double-click opens them); pasted text becomes a sticky note.
- **Select tool**: click to select, Shift+click or drag a marquee for multi-select,
  drag to move, corner handles resize (images stay proportional; Shift frees them),
  top handle rotates (snaps to 15° steps). Right-click for pin, duplicate, paper
  color, z-order, delete. Double-click notes/text to edit.
- **Keyboard**: Ctrl+Z/Y undo/redo · Del delete · Ctrl+D duplicate · Ctrl+A select
  all · Ctrl+G / Ctrl+Shift+G group/ungroup · arrows nudge (Shift = 10px) ·
  V/B/E/T tools · Esc deselect, then hide.
- **Zoom**: Ctrl+scroll zooms into the board (up to 300%), middle-drag or Space+drag
  pans, Ctrl+0 resets.
- Everything autosaves and reloads: strokes in `%AppData%\DeskBoard\board.isf`,
  items in `board.json` (v2 schema; v1 files migrate automatically), pasted images
  in `%AppData%\DeskBoard\assets\`.

## Runtime verification

1. Launch, hit **Ctrl+Alt+D**, draw strokes, add a note, paste an image.
2. Hit **Ctrl+Alt+D** again — clicks pass through to the desktop; ink and pinned
   content stay visible on the wallpaper, behind app windows.
3. Relaunch — strokes, notes, and images reload where they were.
4. Undo/redo walks back strokes, adds, moves, resizes, deletes, and clears.

## Known limitations

- Primary monitor only.
- "Show Desktop" (Win+D) may hide the overlay until the next mode toggle.
- Text edits are not undoable (commit-on-blur); transforms and structure are.
- Board mode covers the full primary screen including the taskbar area.
