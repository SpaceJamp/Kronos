v1.54 — a minimum Windows, and an Update all that does what it says

Two behaviour changes and the removal of a project that could have cost you your settings.

## Kronos now requires Windows 10 22H2

Windows 10 version 22H2, build 19045, is the floor. Older Windows is refused at launch, before
anything touches your disk, database or network.

The README claimed 19041 and **nothing checked it**. The floor is 19045 rather than 19041 because the
Windows App SDK and the WebView2 runtime Kronos hosts are only serviced on current Windows — running
below it means running with neither patched, which is a better reason to refuse than "it looks nicer
on newer Windows".

You get an explanation naming both the requirement and what your machine reports, not a message
telling you to go and look up your own version. There is no setting, environment variable or command
line switch that waives it.

**This will exclude Windows 10 21H2 and anything older.** That is the intent, but it is the one change
here that can stop the app launching on a machine that used to work.

## "Update all" now updates all

Previously it needed games ticking one at a time, and the checkbox was unreachable in grid view — the
tile's cover art was drawn over it and swallowed every click, so the button did nothing and the
cover opened the game instead.

There is no checkbox now. The button acts on the whole library, so there is nothing to tick, nothing
to get stuck, and nothing to forget about. It is disabled only while a run is in progress.

It uses the **whole library, not what your search box has narrowed to**. That is deliberate: a button
called "Update all" that quietly updated only three visible games while two hundred others looked
untouched would be worse than either alternative. The confirmation dialog names every game it is about
to write to before anything happens.

## The Avalonia project has been removed

It was not in the solution, so nothing ever built it and nothing shipped it, and its swap and reset
were stubs that reported "not fully implemented in cross-platform layer yet".

It also carried a fault worth knowing about even though it never shipped:
**`AvaloniaSettingsService` wrote to `settings.json` — the same file Kronos keeps its real settings
in — using an incompatible schema.** Reading it discarded the real settings; saving overwrote them.
Anyone who had run it would have lost their configuration.

It was 141 compile errors from building, most of them reaching into internal members of the core and
assigning `init`-only properties after construction. Fixing it would have been new work rather than a
repair, so it is gone.

## Also

- **The README has been rewritten** for a Windows-first project, with the Linux port's ownership stated
  explicitly: it is this repository's work, not upstream's, and upstream holds no rights over it.
- **The published archive is verified before upload.** The 1.53 zip shipped 97 files short — all of
  `Assets`, `Translations` and `StoredData` — because `Copy-Item` without `-Recurse` copies
  directories but not their contents and the archiver then drops the empty ones. The only symptom was
  a missing logo. The staged file count is now compared against the build output and a mismatch stops
  the upload.

## Downloading

`Kronos-1.54.0-portable.zip` is attached. Unzip anywhere and run `Kronos.exe`. No installer, no
console window, no .NET runtime required.

**It needs the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).**
This is an unpackaged WinUI app and loads `Microsoft.UI.Xaml.dll` from that runtime rather than
carrying a copy. If `Kronos.exe` exits without showing a window, install it. Bundling the runtime was
tried; it makes the crash worse, not better.

The build is **unsigned**, so SmartScreen will warn. GitHub publishes a SHA256 beside every asset and
Kronos verifies it.

**If you are on 1.52**, you will not be offered this update. That build is looking for releases in a
repository that does not exist, so it cannot find anything. Download 1.53 and 1.54 by hand once, and
from 1.53 onwards the updater works.

## Testing

455 tests, green. The build also launches the binary it just published and fails if it exits during
the first twelve seconds — the first artifact ever published here was a binary that died on startup and
nothing in the build output said so.
