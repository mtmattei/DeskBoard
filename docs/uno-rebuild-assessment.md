# DeskBoard → Uno Platform rebuild assessment

Assessed 2026-07-31 (Windows-only scope). POC validated the same day on
Uno.Sdk 6.6.42, `net10.0-desktop`, Skia renderer. POC repo:
`C:\Users\Platform006\DeskBoardUnoPoc`.

## Verdict

**GREEN-leaning YELLOW — the gate is resolved by the hybrid architecture.**
Board mode is fully viable in Uno today. Ambient ink-on-desktop reaches full
WPF-equivalent quality through a native layered ink surface (see hybrid POC
below). True per-pixel alpha is still unavailable on the Uno Win32 desktop
host itself as of 6.6.42 — now an optimization to request, not a blocker.

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

## Hybrid POC (2026-07-31, same day): the workaround that closes the gap

`--hybrid` mode in the POC proves the two-window architecture:

- The Uno window stays a normal opaque window (chrome, Board mode).
- The ambient ink layer is a plain Win32 layered window (`CreateWindowEx` on
  the `STATIC` class, `WS_EX_LAYERED|TOOLWINDOW|NOACTIVATE|TRANSPARENT|TOPMOST`)
  painted by **SkiaSharp directly into a DIB section**
  (`SKSurface.Create(info, dibBits, stride)` — zero-copy) and presented with
  `UpdateLayeredWindow` (premultiplied BGRA, `AC_SRC_ALPHA`).

Confirmed visually over live desktop content: translucent highlighter blends
with the desktop, opaque ink strokes have clean anti-aliased edges, soft drop
shadows render, frosted semi-transparent card works. No fringing. SkiaSharp
resolves transitively from the Uno Skia renderer — no extra package. The whole
surface is ~230 lines (`Poc/LayeredInkWindow.cs` in the POC repo). Per-pixel
alpha hit-testing gives free click-through on empty areas; capture input while
inking with a 1/255-alpha fill.

Cost of the pattern: the ink surface lives outside the XAML tree (no XAML
elements can float in it), and each stroke change re-presents the bitmap from
the CPU — fine for ink (invalidate on input, not per frame), wrong for
animation-heavy content.

## What this means per DeskBoard mode

- **Board mode**: no transparency needed (opaque paper surface covers the
  screen). Fully buildable in Uno now.
- **Ambient / ink-on-desktop**: full quality via the hybrid layered ink
  surface — translucency, AA, shadows all equal to the WPF version. Color-key
  remains the fallback if a single-window build is ever mandatory.
- **Hidden-mode today strip**: opaque strip, or rendered into the same
  layered surface — either way, no constraint.

## The remaining ask (optimization, not blocker)

First-class per-pixel transparent window support in the Uno Win32 desktop
host (premultiplied-alpha swapchain + DWM composition) would collapse the
hybrid back into one window and let XAML elements float over the desktop.
Worth raising with the runtime team regardless. Everything else (ink engine
on SKCanvasElement, VSM styles, shadow rework, interop layer) is known,
estimable work (~75–115 h, see session notes 2026-07-31).

## Scores (Windows-only scope)

| Dimension | Score | Note |
|-----------|:-----:|------|
| Architecture | 3/5 | organized code-behind; core (~1k LOC) ports as-is |
| Dependencies | 5/5 | zero NuGet; WPF-only BCL types replaced by Skia equivalents |
| Controls | 2/5 | no InkCanvas — custom Skia ink engine + ISF migration |
| Platform coupling | 4/5 | all interop verified reachable on the Win32 host |
| XAML compatibility | 2/5 | trigger-based Tokens.xaml → VSM rewrite |
| Project health | 5/5 | SDK-style, .NET 10 |
