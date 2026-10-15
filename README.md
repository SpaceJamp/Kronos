<p align="center">
  <h2 align="center">Kronos</h2>
  <p align="center">Download, manage and swap <strong>DLSS</strong>, <strong>FSR</strong> and <strong>XeSS</strong> DLLs, so you can change the version a game runs without waiting for the game to update.</p>
</p>

> [!IMPORTANT]
> **Kronos is based on [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
> [beeradmoore](https://github.com/beeradmoore), licensed under the [GNU GPL v3.0](LICENSE).** It is an
> independent modification, not affiliated with or endorsed by the original maintainer or NVIDIA.
> Upstream's copyright notices are preserved, and beeradmoore is credited under [Credits](#credits).
>
> Upstream warns that malicious sites impersonate DLSS Swapper. For the **official, signed** build, use
> **[beeradmoore/dlss-swapper](https://github.com/beeradmoore/dlss-swapper)**, not this.
>
> The changes made here are listed in [What changed from upstream](#what-changed-from-upstream). They
> were written with AI assistance, as disclosed in [AI_ASSISTED.md](AI_ASSISTED.md).

## Contents

- [Status](#status)
- [Requirements](#requirements)
- [Getting it](#getting-it)
- [The Uno Platform migration](#the-uno-platform-migration)
- [Linux](#linux)
- [Building](#building)
- [Testing](#testing)
- [Releasing](#releasing)
- [Where data is stored](#where-data-is-stored)
- [What changed from upstream](#what-changed-from-upstream)
- [Authorship of the Linux port and the Uno migration](#authorship-of-the-linux-port-and-the-uno-migration)
- [Credits, licence and contributing](#credits)

## Status

| Build | Target framework | Status |
|---|---|---|
| **Windows, released** (`main`) | `net10.0-windows10.0.26100.0` | **Working.** The WinUI 3 app: game detection, DLL swapping, self-update. Every release so far is this build. |
| **Windows, Uno** (this branch, `uno-pre-alpha`) | `net10.0-desktop` | **Pre-alpha, working.** Lists games with their covers, imports DLLs and swaps them end to end, and publishes a portable build that needs no Windows App Runtime. |
| **Linux** (this branch) | `net10.0-desktop` | **Does not compile yet.** The same Uno project; the data layer still has Windows-only code. See [Linux](#linux). |

`main` stays on WinUI 3 until the Uno build replaces it.

## Requirements

| | |
|---|---|
| **OS** | **Windows 10 version 22H2 (build 19045) or newer**, x64 |
| **CPU** | **64-bit** (x64 or ARM64) |
| **To run** | Nothing extra: the portable build is self-contained |
| **To build** | .NET 10 SDK, and an ImageSharp licence key for Release builds ([details](#the-imagesharp-licence-key)) |

Older Windows is refused at launch with an explanation, before anything touches the disk, database or
network. Build 19045 is the final Windows 10 release; everything below it has been out of support for
years. There is no setting or flag that overrides the check.

> [!WARNING]
> **There is no 32-bit build and never will be.** The DLLs Kronos swaps (`nvngx_dlss.dll`,
> `nvngx_dlssg.dll`, `amd_fidelityfx_dx12.dll`, …) only exist as 64-bit binaries, and a 32-bit CPU
> cannot run the app, the .NET runtime or the bundled SQLite at all. To check, run `msinfo32` and look
> at **Processor → Architecture**: `x64` or `ARM64` is supported, `x86` is not. (*System Type* describes
> the OS, not the CPU.) Kronos explains this on launch rather than failing obscurely.

## Getting it

**Official, signed build:** [upstream releases](https://github.com/beeradmoore/dlss-swapper/releases), or
`winget install --id=beeradmoore.dlss-swapper -e`. Those are the only signed builds of this software.

**Kronos:** download `Kronos-<version>.0-portable.zip` from the
[Releases page](https://github.com/SpaceJamp/Kronos/releases), unzip it anywhere and run `Kronos.exe`.
There is no installer and no .NET runtime to install.

- **Released builds need the Windows App Runtime.** They are the WinUI 3 app from `main`, which loads
  its UI framework from the runtime rather than carrying it. If `Kronos.exe` exits without showing a
  window, install the [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads).
  The Uno build on this branch carries its own UI framework and does not need it.
- **The builds are unsigned**, so SmartScreen will warn. GitHub publishes a SHA256 beside every asset.
- **Kronos updates itself.** It downloads the new archive, checks it against the SHA256 GitHub publishes,
  and a detached helper replaces the files once the app has exited, keeping a rollback copy of
  `Kronos.exe`. A release without a published checksum is refused rather than installed unverified.

**Supported game libraries:** Steam · GOG · Epic Games Store · Ubisoft Connect · Xbox App · Battle.net ·
EA App · games added by hand with **Add Game**.

Kronos does not add DLSS to games that do not support it, and swapping DLLs is not guaranteed to improve
performance, reduce artifacts or avoid crashes. Downgrading below the version a game shipped with can
disable Frame Generation, so Kronos warns before doing that. Originals can always be restored.

## The Uno Platform migration

This branch moves Kronos from WinUI 3 to [Uno Platform](https://platform.uno/), so that Windows and Linux
share one GUI codebase. Uno was chosen because it runs **WinUI XAML itself**: the app's ~2,980 lines of
XAML carried over unchanged. A previous attempt with Avalonia, which has its own XAML dialect, was
abandoned after 141 compile errors.

The plan is in [`spike/UNO-MIGRATION-PLAN.md`](spike/UNO-MIGRATION-PLAN.md), in phases that each end with
something runnable:

| Phase | Work | Status |
|---|---|---|
| 0 | Windowing: port `WindowManager` to Uno's windowing | **Done.** A port, not a redesign. |
| 1 | Move the project to `Uno.Sdk` 6.7.30 with a single `net10.0-desktop` target | **Done.** All 28 XAML files compile unchanged. |
| 2 | Replace the Windows-only UI packages | **Done.** |
| 3 | Dialogs, pickers and images | **Done.** A DLSS swap was run end to end through the UI and checked on disk. |
| 4 | Build and release scripts | **Done.** The portable zip is a self-contained Release build needing no Windows App Runtime. |
| 5 | Move `main` to Uno | **Next.** |

### Where Uno differed, and what replaced it

Uno implements most of the WinUI API but not all of it, and several gaps failed silently rather than
loudly. Each was found by running the app, not by reading code:

| What broke on Uno | Replacement |
|---|---|
| `WindowNative.GetWindowHandle` returns `0x1`, so every file and folder picker silently did nothing | `WindowHandleHelper` finds the real window handle |
| The grouped games grid misplaced tiles added after it was bound: one game's cover appeared several times and others vanished | `NotifyingCollectionView` re-announces each change as a coalesced reset, which Uno handles correctly |
| `ItemsWrapGrid` is not implemented, so the Library page was empty | `WrapPanel` |
| `AppWindow` sizes and positions use inconsistent units, so the window grew 1.5× per launch at 150% scaling and could reopen minimised | Win32 `GetWindowPlacement` / `SetWindowPlacement`, fitted to the nearest monitor |
| `PackageManager` is not implemented, so the Xbox library failed to load | The Win32 packaging API in kernel32 (`AppxPackages`) |
| `PasswordVault` is not implemented, so proxy settings could not be loaded or saved | Windows Credential Manager through the Win32 `Cred*` functions (`CredentialStore`) |
| A resource API used at startup is not implemented, so the window never opened | `ResourceHelper` reads the `.resw` files directly |
| Custom title-bar button colours are not supported | The window draws its own title bar |

### Still open

- **Rendering on Linux** has never been seen on a real Linux desktop: the Linux build does not compile
  yet, and the spike that did build had no compositor to draw to.
- **A machine that has never had the Windows App Runtime** has not run the Uno build. The portable build
  ships none of the runtime's files and a running copy loads none of its modules, which is strong
  evidence but not a test on a clean machine.
- **Proxy settings saved by a WinUI build are not carried over.** Credential Manager is a different
  store from `PasswordVault`'s, so a proxy has to be entered again once after moving to the Uno build.
- **The Uno Release build has not been published yet.** It needs the ImageSharp key, which only the
  maintainer holds; the same restore, publish and smoke test were run in Debug instead.

## Linux

The Linux build is the same Uno project, published for `linux-x64`. **On this branch it does not compile
yet:** the data layer has 49 errors from Windows-only code (registry access, P/Invoke, store discovery)
that the old `net10.0` Linux target used to leave out. The build scripts fail at the build step rather
than skipping it.

That is the real work for Linux, and it is not a UI task. The UI migration covers roughly 8% of `src/`;
store discovery and DLL management are the rest. Every store library except manually added games finds
games through the Windows registry today. Steam is the easiest to port: `libraryfolders.vdf` and
`appmanifest_*.acf` are plain files, parsers for both exist in
[`src/Data/Steam/Manifest/`](src/Data/Steam/Manifest/), and `DLLManager` is already shared.

Known about the Linux runtime already: Uno's text engine needs `libicu`, so
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` does **not** work for it.

On `main`, the Linux build is a command-line tool with `update`, `version` and `self-update`; `list`,
`swap` and `reset` were never wired up.

```bash
./build.sh                      # → Output/linux-x64-<timestamp>/Kronos
.\build.ps1 -Target Linux       # cross-compile from Windows
```

`build.sh` installs the .NET 10 SDK into `~/.dotnet` if it is missing, without `sudo`, and adds it to
`PATH` in `~/.profile` for new shells. Set `DOTNET_INSTALL_DIR` to install elsewhere.

## Building

```powershell
# Debug build
dotnet build ".\src\Kronos.csproj" -f net10.0-desktop -c Debug -r win-x64

# Self-contained portable build → Output/win-x64-portable-<timestamp>/
.\build.ps1 -Target Windows -LicenseKey <your ImageSharp key>
```

`build.ps1` restores, builds and publishes the portable build as `Release` with
`-p:KronosPortable=true`, which defines `PORTABLE`: data is kept beside the executable, the portable
self-update is enabled, and the installer update path is compiled out. It then checks that `Kronos.exe`,
`Uno.UI.dll`, `Assets` and `Translations` are present, and **launches the binary**, failing if it exits
within twelve seconds. A build that dies on startup is not visible from exit codes alone.

| Script | Option | Values |
|---|---|---|
| `build.ps1` | `-Target` | `Windows`, `Linux`, `All` (default: the host OS) |
| `build.ps1` | `-Configuration` | `Release`, `Debug` (Linux only; the Windows portable build is always `Release`) |
| `build.ps1` | `-LicenseKey` | ImageSharp key, if not already in the environment |
| `build.sh` | `$1` / `$2` | target (`Windows`, `Linux`, `All`) / configuration (`Release`, `Debug`) |

The configurations are `Debug`, `Release` and `Debug_Portable`. `Debug` keeps its data in a separate
`DEBUG` folder, and portable builds keep theirs inside the build output, so neither touches a real
installation. **x64 only**: do not add a 32-bit configuration.

### The ImageSharp licence key

ImageSharp, which processes game cover art, needs a licence from 3.2 onwards. Kronos builds against a
free **Community Licence** supplied at build time and never committed:

```powershell
$env:IMAGESHARP_LICENSE_KEY = "<your key>"
```

or pass `-LicenseKey` to `build.ps1`. ImageSharp refuses to compile into any configuration whose name
does not start with `Debug` without one, so Debug builds work without a key and Release builds do not.

> [!WARNING]
> **Do not edit this file with `Get-Content` / `Set-Content` under Windows PowerShell 5.1.** It reads
> UTF-8 without a byte-order mark as Windows-1252, and writes the damage back: every em dash (—) and
> arrow (→) becomes three characters of garbage. It happened once, from a one-line edit. `TextEncodingTests` now
> fails if it happens again.

## Testing

There is an xUnit suite in [`tests/`](tests/) of 668 tests, covering backup decisions, swap versioning,
mass-update scope, release parsing, the window placement maths, the Win32 replacements for the APIs Uno
lacks, the games page markup and the build scripts.

```powershell
dotnet test ".\tests\Kronos.Tests\Kronos.Tests.csproj" -r win-x64
```

There is no CI, so run it before pushing. In a git worktree, one test fails because `.git` is a file
there rather than a folder; that failure is expected.

## Releasing

There is no release automation; tagging a commit publishes nothing. From an authenticated checkout with
the GitHub CLI logged in:

```powershell
./publish-release.ps1                     # tag, release, build and upload the portable zip
./publish-release.ps1 -SkipBuild          # notes only
./publish-release.ps1 -ReplaceArtifactOnly  # rebuild and re-upload for an existing release
```

It stops before creating anything if the version disagrees with `src/Kronos.csproj`, the working tree is
dirty, the tag already exists, the ImageSharp key is missing, or the tests fail. After building, it
refuses to upload an archive with fewer files than the build output or missing `Kronos.exe`,
`Uno.UI.dll` or the icons. The release title must start with the version (`v1.61`), because the in-app
updater reads the version from the first word of the title.

## Where data is stored

| Build | Location | Contents |
|---|---|---|
| Installed | `%LOCALAPPDATA%\Kronos\` | `kronos.db`, `json\`, `dlls\`, `image_cache\` |
| Portable | `<folder with Kronos.exe>\StoredData\` | the same, self-contained |
| Debug | `%LOCALAPPDATA%\Kronos\DEBUG\` | the same, kept apart from a real installation |
| Logs | `%TEMP%\Kronos\logs\` | one file per day |

Proxy credentials are kept in Windows Credential Manager under `Kronos/proxy`.

## What changed from upstream

Kronos is a derivative of DLSS Swapper. As GPL-3.0 §5(a) requires, these are the substantive
modifications. Per-file detail is in the commit history.

### Identity and data

- Everything is named `Kronos` (`Kronos.exe`), so it cannot collide with an official installation, and
  its data lives in its own folder (`%LOCALAPPDATA%\Kronos`, database `kronos.db`) rather than inheriting
  DLSS Swapper's.
- The Inno Setup installer was removed. Releases are a self-contained portable archive.

### Features

- **Portable builds update themselves**, verifying the download against GitHub's SHA256 and rolling
  back `Kronos.exe` if the copy fails.
- **"Update all" acts on the whole library**, and its confirmation names every game it will write to,
  including the ones it will leave alone.
- **Downgrades are warned about** rather than done silently, since an older runtime can break Frame
  Generation.
- **Swaps can be undone more than once.** Backups are a numbered chain and are copied, not consumed, on
  reset. Before, a second reset had nothing to restore, and the next swap could overwrite the game's
  original DLL for good.
- **Scan progress**, **keyboard shortcuts** (Ctrl+R refresh, Ctrl+F search, Ctrl+, settings) and
  cancellable imports.

### Correctness

A full audit fixed a long list of defects. Among them:

- Game DLL records are no longer deleted before a rescan, where one unreadable folder used to wipe
  everything known about a game.
- A finished DLL import no longer cancels itself, leaving its dialog stuck and the import unsaved.
- The self-update's rollback copy is actually made, and it no longer fails when Kronos was started from
  PowerShell 7.
- The update check pointed at a repository that did not exist. A test now compares it with this
  repository's own remote.
- File dialogs leaked native memory on every use, and `Game` equality disagreed with its hash.
- Toggling a DLSS setting no longer freezes the window behind a UAC prompt, and failures no longer
  leave buttons permanently disabled.

### Signature verification says what it checked

Importing a DLL from disk checks its Authenticode signature, the only gate for a file that is not in the
signed manifest. The check now returns `Valid`, `Invalid` or `Unavailable` instead of a `bool` that could
not tell "passed" from "nothing was checked". Only `Invalid` blocks. `Unavailable`, which Windows never
returns, is shown as a warning rather than passed off as success. Downloads and swaps are verified by
hash, independently of this.

### Dependencies

ImageSharp moved to 4.1.2 under a Community Licence, because 3.2 and later are no longer permissively
licensed. SQLitePCLRaw moved to 3.0.5, because every 2.x release is affected by CVE-2025-6965. The project
reports **no known vulnerable packages**
(`dotnet list src\Kronos.csproj package --vulnerable --include-transitive`).

### Unchanged on purpose

The DLL manifest still comes from upstream's public `beeradmoore.github.io` endpoint, and the DLLs
themselves from NVIDIA. Kronos depends on both staying available.

## Authorship of the Linux port and the Uno migration

**The Linux port, and the move of Kronos's entire user interface from WinUI 3 to Uno Platform, are
authored, owned and maintained by this repository's maintainer, SpaceJamp. Neither is upstream's work,
and upstream DLSS Swapper holds no rights over either.** Upstream DLSS Swapper is a WinUI 3 application
for Windows only.

- **The Linux port:** the Linux CLI and its `Program.cs` entry point, the cross-platform core extracted
  to serve it (`UiDispatcher`, `Http`, the shared `Storage` and `Database` layers, the Linux `Updater`),
  and `build.sh`.
- **The Uno Platform migration:** the evaluation spike, the windowing port, the single `net10.0-desktop`
  target, the replacement of every Windows-only UI package, the picker, grid, dialog and image fixes,
  the Win32 replacements for the WinRT APIs Uno lacks, and the build and release pipeline that publishes
  a portable build with no Windows App Runtime. It is what lets Windows and Linux share one GUI
  codebase.

[NOTICE](NOTICE) records the copyright formally: *Kronos modifications copyright (c) 2024-2026
SpaceJamp*. Anything that traces back to DLSS Swapper remains beeradmoore's and its contributors', as
[Credits](#credits) says.

> [!NOTE]
> This is a statement of authorship, not a restriction. Everything here is licensed under
> [GPL-3.0](LICENSE). Copyright in a GPL project says who wrote a piece of work; it does not withdraw the
> licence. This section exists so that this work is not mistaken for something upstream shipped or could
> speak for.

## Credits

**Kronos would not exist without [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
[beeradmoore](https://github.com/beeradmoore) and its contributors.** It is released under the same
licence with the original copyright notices intact. Please support the upstream project
([GitHub](https://github.com/beeradmoore/dlss-swapper/), [Reddit](https://www.reddit.com/r/DLSS_Swapper/))
rather than treating this as a replacement for it.

Upstream receives free Windows code signing from [SignPath.io](https://signpath.io/) through the
[SignPath Foundation](https://www.signpath.com/solutions/for-open-source-community-foundation). That
covers the official project only; Kronos builds are unsigned.

### Licence

[GPL-3.0](LICENSE), the same as upstream. Kronos is a derivative work and cannot be relicensed. See
[NOTICE](NOTICE) for copyright ownership.

`DLSS`, `FSR`, `FidelityFX` and `XeSS` are trademarks of their respective owners. This project is not
affiliated with NVIDIA, AMD or Intel.

| Third-party component | Licence | Notes |
|---|---|---|
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 4.1.2 | SixLabors Community Licence | Cover art processing. Free for non-commercial open-source use; key supplied at build time. [Apply for a key](https://licensing.sixlabors.com/). |
| [Uno Platform](https://github.com/unoplatform/uno) | Apache-2.0 | The cross-platform UI framework on this branch. |
| SQLitePCLRaw 3.0.5 | MIT | Bundled SQLite, through `sqlite-net`. |

### Contributing

This is a personal project and is not accepting contributions. Issues with DLSS Swapper itself belong
[upstream](https://github.com/beeradmoore/dlss-swapper/issues); see [CONTRIBUTING.md](CONTRIBUTING.md).
