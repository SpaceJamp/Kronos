v1.58.1 — Refresh button fix

A patch release for one user-facing bug: **clicking Refresh could start the same scan several times at once**, which looks like a refresh that never finishes.

## What was happening

**Refresh had no guard against being clicked twice.** The only thing stopping a second click was the button being greyed out while a refresh ran — and that was a UI convenience, not a lock.

The button disabled itself based on a "loading" flag, but that flag is shared with the initial page load, so whenever the two disagreed the button stayed clickable. Each click that got through started another full pass over your library, and each pass queues every game for a fresh DLL scan: a recursive walk of the install folder plus a cover-image fetch, for every game you have.

Two of those running at once looks, from the outside, like a refresh that keeps going.

**The per-game scan had the same flaw one level down.** Before starting a game's scan it checked "am I already scanning this?" and only set the flag two UI-thread hops later. Two callers arriving in that gap both passed the check, and both scanned the same game.

## What's fixed

- Refresh now claims a lock before doing anything. A second click while one is running does nothing rather than starting another pass.
- The lock is released in a `finally`, so if a scan throws, the button still comes back. Previously a single failure could have left Refresh permanently dead until you restarted the app.
- The button's enabled state now reads that same lock, so what you see is exactly what the command enforces.
- Each game claims its scan slot atomically, so one game can only ever be scanned by one caller.

## Testing

515 tests, up from 497. Six cover this fix.

They were checked against the unfixed code: with the guard removed, they fail. Three consecutive full runs green.

## Note

This release also carries `UpdateErrorLog`, a tested reader for the partial-update log introduced in 1.55. It is not shown in the UI yet — that wiring is still to come — so this release changes no update behaviour.

## Downloading

`Kronos-1.58.1.0-portable.zip` is attached. Unzip anywhere and run `Kronos.exe`.

**It needs the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).** If `Kronos.exe` exits without a window, install it.

The build is **unsigned**, so SmartScreen will warn. GitHub publishes a SHA256 beside every asset and Kronos verifies it before installing anything.