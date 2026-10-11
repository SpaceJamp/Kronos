# Phase 0 findings: windowing

**Verdict: phase 0 passes, with one concrete gap and one environmental caveat.**

`WindowManager` (243 lines) and `WindowPositionRect` (92 lines) were mirrored one-for-one against
Uno's real API — no placeholders, compiled, published for Linux, and run. These are results, not
inferences.

Branch: `uno-phase0-windowing`. Probe: `spike/Kronos.UnoSpike/Kronos.UnoSpike/WindowingProbe.cs`.

---

## The capability map

| Capability | Result |
|---|---|
| `Window.Closed` / `Activate` / `Close` | **YES** — the registry and cascade-close need no porting |
| `window.AppWindow` | **YES** — returns `Microsoft.UI.Windowing.AppWindow` |
| `AppWindow.Size` / `.Position` read | **YES** |
| `AppWindow.Changed` | **YES** — needed to persist position on move |
| `FrameworkElement.RequestedTheme` | **YES** — theme propagation works |
| `OverlappedPresenter.Maximize` | **YES** |
| **`AppWindowTitleBar` colours** | **NO — `NotImplementedException`** |
| **`AppWindow.Resize` / `Move` / `MoveAndResize`** | **No effect** — see the caveat |
| `AppWindowTitleBar.IsCustomizationSupported()` | **No such API** |

---

## The gap: title bar theming

Every one of the eight `AppWindowTitleBar.Button*Color` properties throws
`NotImplementedException`. That is the bulk of `WindowManager` — roughly 80 of its 243 lines are those
two near-identical `UpdateColorsLight` / `UpdateColorsDark` blocks setting them from the theme
dictionary.

There is also no `IsCustomizationSupported()`, which is what `WindowManager` asks before choosing
between the custom title bar and the legacy caption path. So the branch that falls back to poking
`WindowCaption*` resources and forcing a repaint with Win32 `SendMessage` has no equivalent to fall
back *to*.

**Consequence:** the custom title bar is where Kronos puts its own controls — the app name, the drag
region — and `RepaintWindow` is a Win32-only hack. On Linux this becomes a genuine redesign, not a
port: either accept the platform caption bar, or draw the caption in-app and drop the colour calls.

It is bounded, though. It touches one class, and the theme *values* already live in a resource
dictionary that is platform-neutral — only the API that applies them is missing.

## The caveat: Move and Resize did nothing

`AppWindow.Resize`, `.Move` and `.MoveAndResize` all returned without throwing and left the window at
`1024x640 at 0,0`. `Minimize` and `Restore` likewise reported `state=Maximized` immediately after being
called.

**This is probably the environment, not Uno.** There is no window manager here: WSLg starts weston when
a GUI app launches, the window associates, and then there is no RDP client to present it to. A window
with no compositor underneath it has nothing to move within, so a no-op is the expected result and
says nothing about Uno's implementation.

This is the same gap that left the original spike's rendering unproven, and it is the reason phase 0
still needs a run on a real Linux desktop before phase 1 is safe to start. The `NotImplementedException`
above needs no such confirmation — that is Uno declining to implement, stated directly.

---

## Three differences worth knowing now

| | WinAppSDK | Uno |
|---|---|---|
| Getting an `AppWindow` | `WindowNative.GetWindowHandle` → `Win32Interop` → `AppWindow.GetFromWindowId` | **`window.AppWindow`** |
| Geometry types | `new SizeInt32(w, h)`, `new RectInt32(x, y, w, h)` | No multi-arg constructors — `new SizeInt32 { Width = …, Height = … }` |
| Page load | overridable `OnLoaded` | No `OnLoaded`; use the `Loaded` event |
| Window registry | `Application.Windows` | Not present — hold the reference |
| App singleton | `Application.Current` | Not present as used here |

The first row is a genuine improvement: `WindowManager.GetAppWindowForWindow` is 4 lines of HWND
interop that exist only because WinAppSDK's `Window` has no `AppWindow`. On Uno that whole method
disappears.

The second row is the only one that will touch real code — `WindowPositionRect.GetRectInt32` returns
`new RectInt32(X, Y, Width, Height)`, which does not compile on Uno.

---

## What this means for the plan

Phase 0 was designed to fail loudly if the migration were a redesign. **It did not.**

- Windowing is a port, not a redesign.
- One bounded gap: title bar theming.
- One line to change: `RectInt32` construction in `WindowPositionRect`.
- Everything else in `WindowManager` — the registry, cascade-close, theme propagation — needs no work.

Phase 1 can start. The title bar question should be settled during it rather than before it, because
the answer depends on what Uno's `Window` chrome looks like on a real desktop.

> **Still unproven:** anything that requires a window manager. Position restore, maximise behaviour and
> rendering all remain unverified until this runs on a Linux desktop with a compositor. That is one run
> on a real machine, and it is the last thing standing between phase 0 and phase 1.
