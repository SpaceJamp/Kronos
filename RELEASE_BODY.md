Kronos 1.53 — selection in grid view, and an update check that works

Three fixes, one of which means the update check has probably never worked before.

## "Update all" could not be used in grid view

You could not tick a game in grid view, so there was nothing for **Update all** to act on. The button
itself was fine — the selection underneath it was unreachable.

The cover art sits in the same tile as the selection checkbox, and it was declared *after* it. A Grid
draws later children on top, and that cover had no width, height or alignment, so it stretched over
the whole tile — over the checkbox, and over the version text. Every click on the tick landed on the
cover instead, which opened the game.

Nothing in the XAML says "this element is on top of that one", so nothing looked wrong. List view was
unaffected, because there the cover sits in its own column and never overlaps the tick.

The cover and the version bar are now transparent to input. The tile itself still opens the game on a
click, exactly as before — only the checkbox belongs to the checkbox now.

## The update check was pointed at a repository that does not exist

`SpaceJamp/kronos-dlss-swapper`. There is no such repository. Every check 404'd.

The updater has had two wrong repository names: it was `SpaceJamp/unofficial-dlss-swapper` first, left
over from when this was a fork. Neither showed up, because GitHub redirects the old path for `git`, so
pushing still worked and the stale name stayed invisible — right up until something read it without
credentials, which is exactly what the update check does. A 404 there is reported as *the check
failed*, so the symptom was never "wrong version offered", just "checking for updates" doing nothing.

It is `SpaceJamp/Kronos` now. The test that was supposed to prevent this **asserted the wrong name**,
which is how it stayed wrong; it now compares the constant against this repository's own `origin`
remote, so a rename breaks the build rather than the updater.

**If you are on 1.52 you will not be offered this update**, because 1.52 is looking in the wrong
place. Download it once from the Releases page, and 1.53 and later will find each other.

## Portable builds now update themselves

Previously they could not. The updater only knew how to launch an `-installer.exe`, so with a
portable zip it found nothing and offered nothing.

A portable build now downloads the new archive, checks it against the SHA256 GitHub publishes for it,
and replaces itself. The copy is done by a detached helper *after* the app exits, because Windows will
not let a running executable be overwritten. The helper keeps a rollback copy of `Kronos.exe` and puts
it back if the copy fails, and the app restarts afterwards.

A release with no published checksum is **refused**, not installed unverified. The helper is
replacing the running application with whatever it is handed, so there is no gate on an unverified
file, and there is nothing behind this gate but the checksum.

## Also fixed

- **The release title now has to start with the version.** `GetVersionNumber` parses the first word of
  the release *title* and needs a leading `v`. The 1.52 release was titled `Kronos 1.52 — …`, so it
  parsed to 0, and 0 compares below every real version — the app would have called itself up to date
  forever, with nothing logged. The tag is used as a fallback now, and releases are titled `v1.53`.
- **Downloads are verified against GitHub's digest.** The Linux updater read the checksum from a
  field GitHub does not send, so verification was silently skipped on every download.
- **No console window.** The portable build was a console-subsystem binary, so a command prompt opened
  behind the app and stayed there.

## Downloading

`Kronos-1.53.0-portable.zip` is attached. Unzip anywhere, run `Kronos.exe`. It is **unsigned**, so
SmartScreen will warn — that is expected.

**It needs the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).**
This is an unpackaged WinUI app and loads `Microsoft.UI.Xaml.dll` from that runtime rather than
carrying a copy. If `Kronos.exe` exits without showing a window, install it. (Bundling the runtime was
tried; it makes the crash worse, not better.)

GitHub shows a SHA256 beside every asset, and Kronos verifies it on update.

## Testing

430 tests, green on the Windows target framework. The build also **launches the binary it just
published** and fails if it exits immediately — the 1.52 archive was a binary that died on startup and
nothing in the build output mentioned it.
