# HANDOFF — DeskBoard v2 ship-ready rebuild
Updated: 2026-07-22 21:15

## Where we are
Full v2 rebuild of the WPF desktop-whiteboard overlay is implemented per SPEC.md:
lit board surface with procedural noise + aluminum frame, full-width top-down marker
rail (markers lift/uncap, static contact shadows), board objects (notes / marker text /
taped images / link + file cards) with selection chrome, drag/resize/rotate/pin/group,
undo/redo, zoom (1–3x) + pan, paste + drag-drop import, v1→v2 persistence migration,
empty/error/disabled states. Mid-session user feedback added: full-width rail redesign
and three visibility modes (Board / Ink-on-desktop / Hide everything, via tray menu).
User live-tested during the session (notes, text, image pin, hide) — all worked.
Third user request mid-session: neumorphic frame — implemented (soft matte band,
rounded r20 inner opening, TL inset shadow / BR light catch) and screenshot-verified.

## Last verified state
- Build: pass, net10.0-windows Release, 0 warnings.
- Runtime: verified via --board launch + screenshots (surface, rail, items, text
  auto-size). User exercised note/text/image creation + persistence live. Not yet
  re-verified after the three-mode change (build-verified only).
- Git: not a git repository (consider `git init` + first commit).

## Next actions (in order)
1. User feel-pass on the new full-width rail + Hidden mode (tray menu → Hide everything).
2. Verify Hidden mode round-trip: tray menu → Hide everything → Ctrl+Alt+D → board →
   Esc → confirm nothing renders; then back to Ink on desktop.
3. Consider persisting the chosen background mode across launches (session-only today).
4. Optional polish backlog: marker color reflections on the ledge, multi-monitor,
   text-edit undo.

## Open questions
- Should background-mode choice (Ambient vs Hidden) persist across restarts?
- git init + move project out of OneDrive (bin/obj sync churn) — worth doing before
  more sessions.

## Relaunch
```powershell
cd "C:\Users\Platform006\OneDrive - Uno Platform\Desktop\MattOS\DeskBoard"
dotnet build -c Release
.\bin\Release\net10.0-windows\DeskBoard.exe          # ambient start
.\bin\Release\net10.0-windows\DeskBoard.exe --board  # straight to board (demos/screenshots)
```
App may already be running from this session (tray icon "DeskBoard"). Kill with
`Stop-Process -Name DeskBoard` before rebuilding (exe lock).
