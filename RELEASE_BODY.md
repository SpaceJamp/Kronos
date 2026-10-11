Kronos 1.52 — bug fixes and dependency security

A bug-fix release. No new features. The Games page now works, "Reset to default" now works, and two
libraries can no longer delete your game data on a recoverable error.

## Games page showed no games

The Games page was empty on **every** build and every configuration — while the database held your
games correctly. Steam, GOG and the rest were being discovered and stored the whole time; only the
display was broken.

A refactor for Linux support rebuilt the collection views with a list of `Game` objects while leaving
"IsSourceGrouped" enabled, so the UI tried to group individual games by a group-name property that
doesn't exist on them and produced nothing. Each library's view was also built over a throwaway
collection instead of the shared one, so even the ungrouped path lost everything.

Restored the original design, including library reordering in Settings, which had been silently dead
for the same period.

## "Reset to default" never worked

Swapping a DLL in worked fine. Putting it back did not — every reset returned *"Unable to reset to
default. Please repair your game manually."*

Backups had been changed from a single `.dlsss` file to a numbered chain (`.kronosbak1`, `.kronosbak2`,
…) so swaps can be undone more than once. The write paths were updated; the read paths were not. Reset
looked for the literal text `".dlsss"` in a path that doesn't contain it.

Worse: a rescan found no backups, dropped those records, and deleted them from the database — leaving
the files on disk untracked. That made reset permanently impossible and made each subsequent swap
append another chain entry without bound.

**If you have a game where reset fails, this is the fix.** No manual repair needed.

## One unreadable registry key could delete game data

**Ubisoft Connect:** the library used `break` where `continue` was meant. A single permission-denied
install subkey abandoned the rest of the enumeration — and the cleanup pass that follows deletes
cached games it didn't rediscover, taking their assets, history and notes with them.

**GOG:** `continue` on a cover-art failure skipped the calls that save and process the game, so a
missing or corrupt `webcache.zip` made an installed, working game never appear at all.

Cover handling is now separate from game handling, so a missing cover can no longer hide a game.

## Thread-safety holes in the game scanner

Five sites read the live game-asset list from a background thread while `ProcessGame` was running up
to four scans concurrently and clearing that list. Any interleaving threw `Collection was modified`,
which a catch-all turned into "this game has no DLLs" — permanently.

All now use the snapshot helper the class already documented as required.

## A single failed cover fetch broke cover art library-wide

The in-progress flag guarding cover loading was cleared by a trailing statement rather than a
`finally`. One network failure left the flag stuck and every later call returned immediately.

## Imports no longer claim to be verified when they weren't

`WinTrust.VerifyEmbeddedSignature` returned `true` on Linux — which reads as "verified" when nothing was
checked. Authenticode is a Microsoft PE certificate format with no Linux implementation.

It now returns `Valid` / `Invalid` / `Unavailable`, and only `Invalid` blocks. Where a file is
accepted without being checked, the import summary shows an orange warning banner and the dialog title
states how many files were not verified. Windows never returns `Unavailable`, so it never appears
there.

**To be precise about scope:** the download and swap path was already hash-verified on both platforms
— `ZipMD5Hash` on download, `MD5Hash` before the swap. That was never broken. This affects the
*import* paths, where the file isn't in the signed manifest and so has no expected hash and no
signature gate either.

## Dependencies — no known vulnerabilities

`dotnet list package --vulnerable` reports **no vulnerable packages** for either project on either
target framework.

| Package | Was | Now | Advisory |
|---|---|---|---|
| SixLabors.ImageSharp | 3.1.5 → 4.0.0 | **4.1.2** | CVE-2026-106115 — out-of-bounds write in the TIFF CCITT T6 encoder — plus six others |
| SQLitePCLRaw.bundle_e_sqlite3 | 2.1.8 | **3.0.5** | CVE-2025-6965 — memory corruption. Every 2.x release is affected with no patched 2.x, so 3.x was the only option |

ImageSharp 3.2+ requires a licence. Builds use a Community Licence supplied at build time via
`IMAGESHARP_LICENSE_KEY`; it is never committed. Contributors building from source need their own key.

## Build notes

- **`dotnet test` is green: 367 tests, both target frameworks.**
- The inherited GitHub Actions workflow was removed — it called `package\*.cmd` scripts deleted with
  the installer, so every step failed. **There is no CI.** Run the tests locally before pushing.
- `.gitignore` had three UTF-16 encoded rules that matched nothing, including the one meant to keep
  the licence file out of the repository. Fixed.
- `build.ps1` failed outright on Windows PowerShell 5.1 (three-argument `Join-Path`, unqualified
  `[RuntimeInformation]`). Fixed; both build scripts verified working.

## Known limitations

- **Linux support has been dropped for now.** The `net10.0` target compiles and `build.sh` works, but
  the CLI has only `update`, `version` and `self-update` — nothing that swaps a DLL — and no
  resulting build has ever been run on a real Linux machine. Treat the Linux target as unverified and
  unmaintained. Game discovery is registry-based and each store would need porting. The Avalonia GUI
  does not build. See the README's "Linux support is paused" section.
- **Builds are unsigned.** Expect SmartScreen warnings. The official signed build is upstream's, at
  [beeradmoore/dlss-swapper](https://github.com/beeradmoore/dlss-swapper).
- DLL swapping isn't guaranteed to improve performance or avoid crashes. Downgrading below a game's
  shipped version can disable Frame Generation; Kronos warns first. Originals are always restorable.

## Upgrading

No action required. The database schema is unchanged, so 1.52 reads your existing library as-is.

## Downloading

`Kronos-1.52.0-portable.zip` is attached to this release — 52 MB, self-contained, no console window.
Unzip it anywhere and run `Kronos.exe`.

Verify before running:

```
sha256  9e0836ba070f952ee89aa4be468f4b87f3af0403714f1de2b79da551e431c915
```

GitHub shows the same digest beside the asset, and Kronos verifies it on any in-app update.

**If `Kronos.exe` exits without showing a window**, the
[Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) is not
installed. This is an unpackaged WinUI application, so it loads `Microsoft.UI.Xaml.dll` from that
runtime rather than carrying a copy — bundling it was tried and makes the crash worse, not better.
Upstream's signed installer installs the runtime as part of setup.

The build is **unsigned**, so SmartScreen will warn. That is expected.

**Kronos will not offer to auto-update this build.** The in-app updater looks for an installer-style
executable and deliberately ignores a portable zip, because replacing a running portable copy with a
fresh one unasked is not something it should do. Take new versions from the Releases page.

Building from source produces the same binary and needs your own ImageSharp licence key — see the
README's Building section.
