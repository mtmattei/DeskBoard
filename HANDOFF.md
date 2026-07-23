# HANDOFF — DeskBoard v2 ship-ready rebuild
Updated: 2026-07-23

## Where we are
Full v2 rebuild of the WPF desktop-whiteboard overlay is implemented per SPEC.md:
lit board surface with procedural noise + aluminum frame, full-width top-down marker
rail (markers lift/uncap, static contact shadows), board objects (notes / marker text /
taped images / link + file cards) with selection chrome, drag/resize/rotate/pin/group,
undo/redo, zoom (1–3x) + pan, paste + drag-drop import, v1→v2 persistence migration,
empty/error/disabled states. Mid-session user feedback added: full-width rail redesign
and three visibility modes (Board / Ink-on-desktop / Hide everything, via tray menu).
User live-tested during the session (notes, text, image pin, hide) — all worked.
Later user pivots, all implemented: acrylic frame (replaced neumorphic), the rail
REPLACED by MagnetDock — draggable 3D fridge-magnet tools with persisted positions
(magnets.json) — state hotkeys (Ctrl+Alt+D board / A ink-on-desktop / H hidden),
and a Reminder item kind: card + tear-off calendar chip, click chip or context menu
opens a Calendar popup, overdue chips flush red, orange calendar magnet / R key adds.

## Last verified state
- Build: pass, net10.0-windows Release, 0 warnings.
- Runtime: --board + screenshot verified magnet dock over the user's real board
  content. Reminder card + date-picker popup build-verified only (no input injection
  while the user's board was live).
- Git: main @ 67a578c pushed to https://github.com/mtmattei/DeskBoard (root commit).

## Next actions (in order)
1. User feel-pass: quick note (Ctrl+Alt+N), snip (Ctrl+Alt+S), reminder toast +
   Hidden-mode today strip (verified programmatically 2026-07-23: toast stamped
   LastNotified, mode round-trip clean; strip visuals unseen — desktop was covered).
2. Consider persisting the background-mode choice across launches (session-only).
3. Polish backlog: multi-monitor (snip + board), text-edit undo, run-at-startup
   toggle, first-run coach marks, board snapshots/PNG export, multiple boards.

## Open questions
- Should background-mode choice (Ambient vs Hidden) persist across restarts?
- Move project out of OneDrive (bin/obj sync churn) now that it's on GitHub?

## Relaunch
```powershell
cd "C:\Users\Platform006\OneDrive - Uno Platform\Desktop\MattOS\DeskBoard"
dotnet build -c Release
.\bin\Release\net10.0-windows\DeskBoard.exe          # ambient start
.\bin\Release\net10.0-windows\DeskBoard.exe --board  # straight to board (demos/screenshots)
```
App may already be running from this session (tray icon "DeskBoard"). Kill with
`Stop-Process -Name DeskBoard` before rebuilding (exe lock).
