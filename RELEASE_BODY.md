v1.60 — an audit, and nine fixes

A full pass over the code found nine real defects. One is a security hole. Two were bugs I introduced while fixing other things, and I'd rather say that plainly than bury it.

## The serious one: the installer update ran unverified, as administrator

The Windows installer path downloaded an update and **never checked it**. The SHA256 GitHub publishes was sitting right there, unused — the only hash check decided whether a *cached* file needed re-downloading. After that, it went straight to launching the file, which is what raises the UAC prompt.

So anything that came back from the network rather than from GitHub — a truncating proxy, a substituted response — was **executed as administrator**.

It is now verified before launch, and an update with no checksum is refused rather than run unchecked.

It survived because **the portable path was already verified, and that was the path with tests.** The safe code was the covered code.

## Two bugs I introduced

**The Refresh button was completely dead in 1.58.1.** I rebound it to a new `CanRefresh` property so the button would match what the command enforces — but nothing notified that property when the loading flags changed. Both start `true`, so the button was disabled at startup and never re-enabled. My own tests passed, because they checked that the guard existed and the binding was correct. Neither was the question that mattered: *does the button ever become enabled?*

**A game could get permanently stuck.** The processing guard I added released itself in a `catch` — but a `return` from inside a `try` doesn't run its `catch`. So a game whose install folder had been moved or deleted stayed marked "processing" for the rest of the session and refused to open. That is the exact symptom the guard was added to prevent.

## Performance

**Games took far too long to process.** Kronos allows four game scans at once, and one of those four slots was being held for the whole cover-image *download*. Four games' scanning stalled for the length of every network round-trip.

Measured over 30 games, same work either way:

| | Total |
|---|---|
| Before | **8.3s** |
| After | **1.9s** |

The cover is still awaited before a game reports itself done — only the slot is freed sooner.

## Also fixed

- **Changing your proxy could fail an in-progress download.** The shared HTTP client was disposed while downloads were reading from it. It isn't disposed any more.
- **Every successful update left a full copy of Kronos in your temp folder.** The update script now tidies up after itself.
- **A checksum error could blame the wrong thing.** A `sha512` checksum was compared as if it were a hash, producing "checksum mismatch" for what was actually an unsupported algorithm.
- **The download progress bar could crash the app.** A race let the total read as zero, producing `NaN`, which throws when set on a progress bar.
- **Closing Kronos after a manifest error showed you the crash window.** A missing `return` after `Close()` let the rest of the startup routine run against a window that was already gone.
- **The theme watcher could not be stopped.** It unsubscribed a handler that had never been added.
- **Toggling a game library mid-load could leave your library empty**, and a routine rescan failure raised the crash dialog.
- **Cached release info was written in a way that could corrupt it**, and read outside any error handler on the startup path.

## Testing

571 tests, up from 470 when the audit began.

The two I introduced were found by review, not by tests — so the new tests target behaviour rather than code shape: a miniature demonstrating a button frozen at its startup value, and a control-flow test for the leaked processing slot.

The staging cleanup is verified by **running the real generated script**. That immediately caught the cleanup deleting the test's own fixture directory, because the script wasn't in a staging folder of its own the way the app writes it.

Three consecutive full runs, green.

## Downloading

`Kronos-1.60.0-portable.zip` is attached. Unzip anywhere and run `Kronos.exe`.

**It needs the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).** If `Kronos.exe` exits without a window, install it.

The build is **unsigned**, so SmartScreen will warn. GitHub publishes a SHA256 beside every asset and Kronos verifies it before installing anything.