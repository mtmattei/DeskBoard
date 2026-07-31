# HANDOFF — DeskBoard warm-studio chrome verified + Uno rebuild GO
Updated: 2026-07-31

> **Uno rebuild is approved and specced.** Spec:
> `C:\Users\Platform006\DESKBOARD-UNO-SPEC.md`. Feasibility + POC results:
> `docs/uno-rebuild-assessment.md` and `C:\Users\Platform006\DeskBoardUnoPoc`.
> Implementation happens in a fresh session in a NEW repo
> (`C:\Users\Platform006\DeskBoardUno`) — this WPF repo keeps shipping
> untouched until parity. Kickoff prompt at the bottom of this file.

## Where we are
Post-redesign checkpoint. Since the v2 handoff, three feature commits landed:
reminder toasts + Hidden-mode today strip + quick note (Ctrl+Alt+N) + snip
(Ctrl+Alt+S); doodle shape tools; and the warm-studio chrome redesign
(`249b75c`) — floating title pill, labeled right tool rail, pen/color/shape
bar with nib presets, zoom pill, minimap. Warm paper surface replaced the
acrylic frame and the MagnetDock was removed entirely (magnets.json gone).
Snap-into-bento-grid button added. BoardStorage now writes temp+rename
(atomic) after the 2026-07-29 board-wipe incident. This session pushed
`249b75c`, relaunched, and screenshot-verified the new chrome over the real
board content — board data intact.

## Last verified state
- Build: pass, net10.0-windows Release, 0 warnings (2026-07-30).
- Runtime: --board launch + PrintWindow screenshot verified: chrome renders,
  items/ink/reminder chip intact. Possible issue: minimap renders as an empty
  white panel despite board content — unconfirmed whether that's a bug or the
  100%-zoom viewport rect. App left running.
- Git: main @ 249b75c, pushed, working tree clean.
- Data backup: %APPDATA%\DeskBoard.bak-20260730 taken before this session's
  relaunch.

## Next actions (in order)
1. User feel-pass on the new chrome: tool rail, pen bar + nib presets, bento
   snap animation, minimap (check the empty-panel question above).
2. Decide: persist background-mode choice (Ambient vs Hidden) across launches.
3. Decide: move the repo out of OneDrive (bin/obj churn; it's on GitHub now).
4. Polish backlog: multi-monitor (snip + board), text-edit undo, run-at-startup
   toggle, first-run coach marks, board snapshots/PNG export, multiple boards.

## Open questions
- Minimap: empty white panel at 100% zoom — bug or expected viewport rect?
- Background-mode persistence across restarts?
- Move project out of OneDrive?

## Relaunch
```powershell
cd "C:\Users\Platform006\OneDrive - Uno Platform\Desktop\MattOS\DeskBoard"
dotnet build -c Release
.\bin\Release\net10.0-windows\DeskBoard.exe          # ambient start
.\bin\Release\net10.0-windows\DeskBoard.exe --board  # straight to board
```
Back up `%APPDATA%\DeskBoard` before killing a live instance
(`Stop-Process -Name DeskBoard` to release the exe lock before rebuilding).

## Kickoff prompt — Uno rebuild Phase 1 (paste into a fresh session)

> Session: DeskBoardUno Phase 1 — scaffold + shell. Orient in order:
> (1) `C:\Users\Platform006\DESKBOARD-UNO-SPEC.md` — the spec is source of
> truth; (2) `C:\Users\Platform006\DeskBoardUnoPoc\DeskBoardUnoPoc\Poc\`
> (`OverlayPoc.cs`, `LayeredInkWindow.cs`) — proven window techniques;
> (3) the WPF repo at `...\MattOS\DeskBoard` for feature reference only.
> Read them cold — written in prior sessions, don't assume. Implement
> Phase 1 of the spec's Implementation Plan (scaffold at
> `C:\Users\Platform006\DeskBoardUno`, blank preset pinned to Uno.Sdk
> 6.6.42, tokens port, borderless board window, tray, hotkeys,
> ModeController). Run the phase-1 time-boxed risk spike (hide/show
> swapchain survival) early. Verify with uno-verify after each step;
> back up `%APPDATA%\DeskBoard` before any runtime test. Run `/handoff`
> before stopping.
