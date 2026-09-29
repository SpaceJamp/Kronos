# AI assistance disclosure

Kronos was developed with the help of an AI coding assistant. This document records exactly
what that covered, so nobody has to guess.

## Summary

- **The upstream code is not AI-generated.** This repository started as a copy of
  [beeradmoore/dlss-swapper](https://github.com/beeradmoore/dlss-swapper), obtained as a source
  archive. Everything that project contains was written by its author and contributors.
- **The changes made in Kronos were written with AI assistance**, under my direction and review.
  That covers the bug fixes, the asset-type refactor, the test suite, the cancellation work, the
  rename, and the packaging fixes.
- **I am responsible for all of it.** The AI is not an author and holds no rights. The commits in
  this repository are mine.

The assistant used was Claude, via the OpenCode harness.

## What the AI wrote

Everything in the "What changed in Kronos" section of the README. Concretely:

**New files it authored**

| File | Purpose |
| --- | --- |
| `src/Data/DLLAssetTypeInfo.cs` | The asset-type registry that replaced nine hand-maintained if/else chains |
| `tests/Kronos.Tests/*` | The entire xUnit test project, 91 tests |
| `CONTRIBUTING.md`, `SECURITY.md` | Project documentation |

**Existing files it edited**

- `src/Data/DLLManager.cs` — fixed a copy/paste bug in the v1.1.7 migration, fixed an inverted
  condition that prevented orphaned files being deleted, and replaced nine if/else chains with
  registry lookups (1401 lines to 1006)
- `src/Data/Game.cs` — reordered `ProcessGame` so database rows are no longer deleted before the
  install folder is scanned, converted an `async void` thread-pool work item to `Task.Run`, made
  game deletion stop walking the whole image cache, fixed the base64 title cache, replaced the DLL
  scan loop, and fixed the backup logic that could lose a game's original DLL
- `src/App.xaml.cs` — fixed `RunOnUIThread` / `RunOnUIThreadAsync` silently discarding their
  callback, and replaced an unreliable UI-thread test
- `src/Helpers/PathHelpers.cs` — fixed a drive root (`C:\`) being normalised to the drive-relative
  path `C:`
- `src/Logger.cs` — fixed `ChangeLoggingLevel` ignoring its own argument
- `src/Pages/LibraryPageModel.cs` — added real cancellation to the import flows, fixed a cancel
  check that was missing so cancelling still imported, cleared busy flags on failure, serialised
  concurrent dialogs, removed an unobserved thread-pool work item, and removed an O(n^2) re-hash
- `src/Pages/SettingsPageModel.cs` — cleared a busy flag on failure
- `package/*.cmd`, `package/Installer.iss`, `.github/workflows/*`, `.gitignore`, `*.sln`,
  `*.csproj`, `README.md` — project hygiene, rename, and packaging fixes

## What the AI did not write

- The upstream application: the XAML UI, the game library implementations for Steam/GOG/Epic/
  Battle.net/Ubisoft/Xbox/EA App, the download and manifest handling, the localisation system, and
  the settings storage. All of that is upstream's work.
- The protobuf-generated `src/Data/BattleNet/Proto/BattleNetProductDb.cs`.
- Any artwork. The icon and tile assets are still upstream's.

## What I verified rather than trusted

The assistant asserted that its changes worked. These are the checks I ran myself:

- **The build compiles.** All four configurations (`Debug`, `Release`, `Debug_Portable`,
  `Release_Portable`) build with zero errors and zero warnings.
- **The tests pass**, and I confirmed they are meaningful by deliberately reintroducing each bug
  they cover and checking that the relevant tests fail. Two of the bugs fixed here were caught by
  the assistant's own tests failing on a bad first attempt, which is recorded in the commit history.
- **The application actually runs.** Built and launched the `Debug_Portable` configuration, confirmed
  a window is created, memory is stable, and the log contains no errors or warnings. It scanned a
  real game library and correctly wrote 13 game asset rows across 7 asset types.
- **The release packaging works.** Installed NSIS 3.12 and PowerShell 7, ran the four packaging
  scripts, and confirmed both artifacts are produced and the installer's metadata is correct.
  This is also how two packaging bugs were found: the scripts could not run under the default
  execution policy, and every failure was reported as success because `cd` reset `%errorlevel%`.
- **Line-ending and encoding noise.** Compared the working tree against upstream history and found
  an accidental UTF-8 BOM change in a file the assistant had not meant to touch, and reverted it so
  the diff contains only deliberate changes.

## What remains unverified

Stated plainly, because the AI-assisted fixes above are not all equally proven:

- **Anything requiring UI interaction has not been clicked.** The new cancel buttons, the busy-flag
  recovery, the dialog serialisation, and the add/delete game flows are compile-verified and
  reasoned about, but not exercised by a person using the app.
- **The swap path was not tested against a real game.** The backup logic is unit-tested at the
  decision level, but actually overwriting a DLL inside a game install, and resetting it, has not
  been run.
- **Known bugs remain.** Kronos fixes a substantial number of defects but not all of them. See the
  conversation history and the code for the ones still outstanding, notably unmanaged memory leaks
  in `FileSystemHelper.cs`, and a `Game.Equals` implementation with no matching `GetHashCode`.

  One inherited bug was found while testing the `WinTrust` fix and is pinned by a test rather than
  fixed: signed `.exe` files such as `cmd.exe` are reported as `Valid` by Windows but rejected by
  `VerifyEmbeddedSignature`. Stashing the interop changes and re-running produced identical results,
  so it predates the work done here. The suspected cause is the object initializer in
  `VerifyEmbeddedSignature`, which overrides `dwUIContext` with the invalid value `0` and otherwise
  repeats what the constructor had already done, but that was not confirmed. It does not affect the
  import flow, which only handles `.dll` files.
- **Kronos is unsigned**, so Windows SmartScreen will warn.

## Licensing

Kronos remains under the [GNU General Public License v3.0](LICENSE), the same licence as
upstream, and upstream's copyright notices are preserved unmodified. AI-generated code has no
copyright of its own; it is contributed here under GPL-3.0 on the same terms as the rest of the
work, and the same licence applies to it as to any other contribution to this repository.
