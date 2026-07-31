# DeskBoard → Uno Platform rebuild assessment

Assessed 2026-07-31 (Windows-only scope). POC validated the same day on
Uno.Sdk 6.6.42, `net10.0-desktop`, Skia renderer. POC repo:
`C:\Users\Platform006\DeskBoardUnoPoc`.

## Verdict

**YELLOW (~3.3/5), gated on overlay transparency — POC result: partial pass
(color-key tier).** Board mode is fully viable in Uno today. Ambient
ink-on-desktop mode degrades under color-key transparency. True per-pixel
alpha is not available on the Uno Win32 desktop host as of 6.6.42.

## POC findings (measured, not assumed)

Host discovery:

- The Windows desktop host is a **pure Win32 window**
  (`Uno.UI.NativeElementHosting.Win32NativeWindow`, window class
  `UnoPlatformRegularWindow`). The docs table still describing Skia+WPF is
  outdated — there is no WPF `Window` to reach, so `AllowsTransparency`
  tricks are off the table.

Strategy results:

| Strategy | Result |
|----------|--------|
| A. `WindowHelper.SetBackground(Transparent)` | Accepted without error, but composites **opaque** (transparent → black) |
| B. WPF-host `AllowsTransparency` via reflection | N/A — host is Win32, not WPF |
| C. Win32 layered color-key (`WS_EX_LAYERED` + `LWA_COLORKEY`) | **Works.** Desktop shows through keyed pixels; app content floats on top |

Verified working on the Win32 host via ex-style interop: borderless style
stripping, topmost fullscreen, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`,
`WS_EX_TRANSPARENT` click-through (readback 0x080800A8). Sizing must go
through `AppWindow.MoveAndResize` — a raw `SetWindowPos` resize leaves the
swapchain at its old size (dead black bands).

Color-key limitations, confirmed visually:

- **No per-pixel alpha**: a 50%-opacity element blends with the key color
  into an opaque non-key pixel — semi-transparent surfaces over the desktop
  are impossible.
- **Edge fringing**: anti-aliased edges blend toward the key color, leaving
  a visible fringe on strokes and text floating over the desktop.

## What this means per DeskBoard mode

- **Board mode**: no transparency needed (opaque paper surface covers the
  screen). Fully buildable in Uno now.
- **Ambient / ink-on-desktop**: works mechanically (click-through + color-key)
  but ink gets hard fringed edges and shadows/translucency are lost. Quality
  regression vs the WPF version's `AllowsTransparency`.
- **Hidden-mode today strip**: same color-key constraints; small opaque strip
  could simply avoid transparency.

## The unlock

First-class per-pixel transparent window support in the Uno Win32 desktop
host (premultiplied-alpha swapchain + DWM composition) would remove the only
real gate. Worth raising with the runtime team before committing to the
rebuild — everything else (ink engine on SKCanvasElement, VSM styles, shadow
rework, interop layer) is known, estimable work (~75–115 h, see session
notes 2026-07-31).

## Scores (Windows-only scope)

| Dimension | Score | Note |
|-----------|:-----:|------|
| Architecture | 3/5 | organized code-behind; core (~1k LOC) ports as-is |
| Dependencies | 5/5 | zero NuGet; WPF-only BCL types replaced by Skia equivalents |
| Controls | 2/5 | no InkCanvas — custom Skia ink engine + ISF migration |
| Platform coupling | 4/5 | all interop verified reachable on the Win32 host |
| XAML compatibility | 2/5 | trigger-based Tokens.xaml → VSM rewrite |
| Project health | 5/5 | SDK-style, .NET 10 |
