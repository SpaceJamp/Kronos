<p align="center">
  <h2 align="center">Kronos</h2>
  <p align="center">Download, manage and swap <strong>DLSS</strong>, <strong>FSR</strong> and <strong>XeSS</strong> DLLs, so you can change the version a game runs without the game itself updating.</p>
</p>

> [!IMPORTANT]
> **Kronos is based on [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
> [beeradmoore](https://github.com/beeradmoore), licensed under the
> [GNU GPL v3.0](LICENSE).** It is an independent modification, not affiliated with or endorsed by
> the original maintainer or NVIDIA. Original copyright notices are preserved and beeradmoore is
> credited in [LICENSE](LICENSE) and under [Credits](#credits).
>
> Upstream warns that malicious sites impersonate DLSS Swapper. If you want the **official, signed**
> build, use **[beeradmoore/dlss-swapper](https://github.com/beeradmoore/dlss-swapper)**, not this.
>
> Changes made here are listed in [What changed](#what-changed) and were written with AI assistance,
> disclosed in [AI_ASSISTED.md](AI_ASSISTED.md).

> [!WARNING]
> **The build requires a 64-bit processor. There is no 32-bit build and never will be.** A 32-bit
> CPU cannot execute the `x86_64` instructions in the app, the .NET runtime and the bundled SQLite —
> it is a hardware limit.
>
> | `msinfo32` field (`Win`+`R` → `msinfo32`) | Value | Meaning |
> |---|---|---|
> | **Processor → Architecture** | `x64` or `ARM64` | 64-bit **CPU**. Supported. |
> | **Processor → Architecture** | `x86` | 32-bit **CPU**. No version of Windows can fix this. |
>
> `System Type` describes your OS, not your CPU — an `x86-based PC` there does not necessarily mean
> a 32-bit processor, which is why *Processor → Architecture* is the field that settles it.
>
> Kronos checks `Environment.Is64BitOperatingSystem` on launch and shows an explanation rather than
> failing obscurely. The underlying reason it is x64-only: the DLLs it swaps
> (`nvngx_dlss.dll`, `nvngx_dlssg.dll`, `amd_fidelityfx_dx12.dll`, …) only exist as 64-bit binaries.

## Platform support

| Platform | Target framework | Status |
|---|---|---|
| **Windows x64 (released)** | `net10.0-windows10.0.26100.0` | **Working.** The WinUI 3 app on `main`, with game detection, DLL swapping and self-update. This is what releases ship. |
| **Windows x64 (Uno, this branch)** | `net10.0-desktop` | **Pre-alpha.** Opens, lists games with their covers, imports DLLs and swaps them end to end. See [Uno Platform migration](#uno-platform-migration) for what is not done yet. |
| **Linux x64** | `net10.0-desktop` | **Does not build yet on this branch.** The data layer has 49 compile errors (registry, P/Invoke and other Windows-only code). Store detection and DLL swapping on Linux are separate work beyond that. On `main`, the Linux CLI has `update`, `version` and `self-update`. |

The Windows GUI is the finished product. The Linux port is real work in progress by this
repository's maintainer — see [Ownership of the Linux work](#ownership-of-the-linux-work).

## Uno Platform migration

> [!IMPORTANT]
> **This branch (`uno-pre-alpha`) is moving Kronos from WinUI 3 to Uno Platform: one GUI codebase for
> Windows and Linux.** `main` still carries the WinUI 3 app, and it still builds, tests and releases
> exactly as before. Nothing here ships until the cut-over in phase 5.

Uno Platform was chosen because it runs **WinUI XAML** on Linux — meaning Kronos's ~2,980 lines of XAML
survive the port rather than being rewritten. A previous Avalonia attempt was abandoned after 141
compile errors. The plan, in phases that each end with something runnable, is in
[`spike/UNO-MIGRATION-PLAN.md`](spike/UNO-MIGRATION-PLAN.md).

| Phase | Work | Status |
|---|---|---|
| 0 | Windowing probe: `WindowManager` against Uno's windowing | **Done.** A port, not a redesign. Title-bar button colours are not supported. |
| 1 | Project onto `Uno.Sdk` 6.7.30, single `net10.0-desktop` target | **Done.** All 28 XAML files compile unchanged. |
| 2 | Replace the remaining Windows-only packages | **Done.** |
| 3 | Dialogs, pickers and images | **Done.** A DLSS swap was run end to end through the UI and verified on disk. |
| 4 | `build.ps1`, `build.sh`, `publish-release.ps1` | **Done.** The scripts build `net10.0-desktop`, and the portable zip is a self-contained Release build with `PORTABLE` defined, needing no Windows App Runtime. |
| 5 | Cut `main` over to Uno | **Next.** |

Known gaps on Windows, found while testing phase 3:

- **Xbox library:** Uno does not implement `PackageManager.FindPackagesForUser`, so the Xbox library
  fails to load. Games already in the database still show, but new installs are not found.
- **Proxy settings:** Uno does not implement the API `ProxySettings.LoadIfNeeded` uses either, so it
  logs an error on startup. Downloads still work.
- **Window state:** the window can reopen minimised after a forced close, and `RepaintWindow` still
  uses an invalid window handle.
- **Windows App Runtime:** the portable build ships none of its files and loads none of its modules
  (checked on a running build). It has not yet been run on a machine that has never had the runtime
  installed.
- **Not yet verified:** whether the app renders on a real Linux desktop. The Linux build does not
  compile yet.

### The original evaluation

Before the migration started, a spike was built on the `uno-spike` branch: one page exercising every
construct this app actually uses, in the shape it actually uses them, on Uno.Sdk 6.7.30.

| Question | Answer |
|---|---|
| Does the XAML dialect carry over? | **Yes.** Every construct this app uses compiles unchanged — including `CommunityToolkit.WinUI.Converters` (declared in every page's resources), `x:Bind` two levels deep through the generated `TranslationProperties` object (how every string in the app is bound), and the WinUI system brushes and styles behind `ThemeResource` and `StaticResource`. |
| Does it build for Linux? | **Yes**, from this Windows machine — it publishes a genuine ELF binary, so no Linux build runner is needed. |
| Does it run? | **Partly.** It starts and loads ICU, Skia, HarfBuzz, X11, fontconfig and Mesa GL. **Rendering is unproven** — it did not open a window in the environment available, which had no Wayland compositor running. |

The one construct that failed is `Hyperlink` wrapping a `TextBlock`: Uno's derives from `Inline` and
takes `Inline` children, where WinUI 3 also accepts a `UIElement`. All four `Hyperlink`s in this repo
already wrap a `Run`, which works — so it cost nothing.

Full detail, including what it does *not* solve, is in
[`spike/SPIKE-FINDINGS.md`](spike/SPIKE-FINDINGS.md).

> [!WARNING]
> **Uno migrates the UI, which is roughly 8% of `src/`.** It does nothing for the other 92% — 88 registry
> references, 53 P/Invoke sites, and eight store-discovery libraries. A completed Uno migration would
> leave a Linux GUI attached to a product that still cannot swap a DLL.
>
> **The blocking work is the data layer, not the UI.** Store discovery and DLL management on Linux is
> the next piece, and it is a different kind of job entirely.

One deployment consequence is already known: Uno's text engine loads ICU directly, so
`DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` does **not** work — unlike in a plain .NET app. Any Linux
distro targeted needs `libicu`. Conversely, Uno drops the Windows App Runtime, which removes the
"install the App Runtime or Kronos exits silently" problem described under [Getting it](#getting-it):
the Uno build on this branch carries its own UI framework instead.

## Ownership of the Linux work

**The Linux port, and the move of the whole user interface from WinUI 3 to Uno Platform, are
authored, owned and maintained by this repository's maintainer. Neither is upstream's work, and
upstream DLSS Swapper holds no rights over either.**

This matters because the two projects are easy to confuse: they share a name, a licence and a large
amount of shared code. Concretely:

- The Linux CLI, its `Program.cs` entry point, the cross-platform core that was extracted to serve it
  (`UiDispatcher`, `Http`, the shared `Storage` and `Database` layers, the Linux `Updater`), and the
  `build.sh` script were **written for this repository**.
- The **Uno Platform migration** was done for this repository too: the evaluation spike, the
  windowing port, the move to a single `net10.0-desktop` target, the replacement of every
  Windows-only UI package, the dialog, picker and image fixes, and the build and release pipeline
  that now publishes a portable build with no Windows App Runtime. It is what lets Windows and
  Linux share one GUI codebase. Upstream DLSS Swapper is still a WinUI 3 application.
- `NOTICE` records the same thing formally: *Kronos modifications copyright (c) 2024-2026 SpaceJamp*,
  including Linux CLI support, with upstream's own copyright notices preserved untouched.
- Anything here that traces back to DLSS Swapper is still beeradmoore's, and is credited as such under
  [Credits](#credits).

> [!NOTE]
> This is a statement of **authorship and ownership**, not a restriction. Everything in this repository
> is licensed under [GPL-3.0](LICENSE), which is what makes those contributions possible in the first
> place. Copyright in a GPL project identifies who wrote and may relicense a given piece of work; it
> does not withdraw the licence, and GPL-3.0 §5(a) requires only that the modifications be stated —
> which [What changed](#what-changed) and `NOTICE` both do. This section exists so the Linux work and
> the Uno migration are not mistaken for something upstream shipped or could speak for.

## Requirements

| | |
|---|---|
| **OS** | **Windows 10 version 22H2 (build 19045) or newer**, x64 |
| **CPU** | **64-bit** (x64 or ARM64) — 32-bit is not supported |
| **GPU** | Any |
| **To run** | [.NET Desktop Runtime 10](https://dotnet.microsoft.com/download/dotnet/10.0), or the self-contained build |
| **To build** | .NET 10 SDK, Windows SDK 10.0.26100 |

Older Windows is refused at launch with an explanation, before anything touches the disk, database or
network. The floor is not arbitrary: 19045 is the final Windows 10 release, and everything below it has
been out of support for years, so running there means security fixes for nothing — not even for Kronos.
There is no setting to override it, and no flag that turns it off.

### Linux (building only)

| | |
|---|---|
| **OS** | Any modern Linux (glibc 2.31+) |
| **CPU** | x64 |
| **To build** | .NET 10 SDK — `build.sh` installs it if missing |
| **Runtime** | Self-contained (no system .NET required) |

The Linux target is a work in progress. See
[Ownership of the Linux work](#ownership-of-the-linux-work) and
[Platform support](#platform-support) for what it does and does not do today.

## Getting it

**Official, signed build:** [upstream releases](https://github.com/beeradmoore/dlss-swapper/releases)
or `winget install --id=beeradmoore.dlss-swapper -e`. Those are the only signed builds of this
software.

**Kronos:** download `Kronos-<version>.0-portable.zip` from the
[Releases page](https://github.com/SpaceJamp/Kronos/releases). Unzip anywhere and run `Kronos.exe`.
No installer, no console window, and no .NET runtime required.

> **Released builds need the Windows App Runtime.** The releases so far are the unpackaged WinUI
> application from `main`, which resolves `Microsoft.UI.Xaml.dll` from the Windows App Runtime rather
> than carrying a copy. If `Kronos.exe` exits immediately without showing a window, install the
> [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) —
> upstream's signed installer installs it as part of setup. The Uno build on this branch does not
> need it: it carries its own UI framework.

> **These builds are unsigned.** Expect SmartScreen to warn; that is inherent to distributing
> binaries without signing credentials, not a sign of tampering. GitHub publishes a SHA256 for
> every asset, shown beside its download link, and Kronos verifies it on any in-app update.

> **Kronos updates itself.** It downloads the new archive from the Releases page, checks it against
> the SHA256 GitHub publishes for it, and replaces itself. The copy is done by a detached helper
> after the app exits, because Windows will not let a running executable be overwritten; the helper
> keeps a rollback copy of `Kronos.exe` and restores it if the copy fails. A release with no
> published checksum is refused rather than installed unverified.

**Building from source** produces the same binary and is covered in [Building](#building). It needs
your own ImageSharp licence key.

Kronos does not add DLSS to games that do not support it, and swapping DLLs is not guaranteed to
improve performance, reduce artifacts, or avoid crashes. Downgrading a runtime below the version a
game shipped with can disable Frame Generation, so Kronos warns before it does that. Originals are
always restorable.

## Supported game libraries (Windows)

Steam · GOG · Epic Games Store · Ubisoft Connect · Xbox App · Battle.net · EA App · games added
manually via **Add Game**

The Linux port has no store detection yet — see [Platform support](#platform-support).

## Building

```powershell
# Build
dotnet build ".\src\Kronos.csproj" -f net10.0-desktop -c Debug -r win-x64

# Test (658 tests)
dotnet test ".\tests\Kronos.Tests\Kronos.Tests.csproj" -r win-x64

# Self-contained portable build → Output/win-x64-portable-<timestamp>/
.\build.ps1 -Target Windows -LicenseKey <your ImageSharp key>
```

**Release builds need your ImageSharp licence key**, Debug builds do not: ImageSharp refuses to
compile into any configuration whose name does not start with `Debug` without one. See
[The ImageSharp licence key](#the-imagesharp-licence-key).

The configurations are `Debug`, `Release` and `Debug_Portable`. `Debug` writes to a separate `DEBUG`
folder under `%LOCALAPPDATA%\Kronos`, and the portable builds keep all data inside the build output,
so neither touches a real installation. The portable release build is `Release` with
`-p:KronosPortable=true`, which defines `PORTABLE`; it replaces the old `Release_Portable`
configuration, which existed only because Windows App SDK could not publish unpackaged otherwise.

**x64 only, for the project as well as the output.** `<Platforms>` is `x64` and the runtime is
`win-x64`. Do not add a 32-bit configuration; see the warning at the top.

`build.ps1` restores, builds and publishes that portable Release build, always in the same
configuration. A Debug restore followed by a Release build fails on Uno with `UNOB0019`. It then checks
that `Kronos.exe`, `Uno.UI.dll`, `Assets` and `Translations` are all there, and **launches the binary
it just published**, failing if it exits during the first twelve seconds. A build that produces
something which dies on startup is not a build worth shipping, and that failure is not visible from
the exit codes alone.

### Linux

```bash
./build.sh
# → Output/linux-x64-<timestamp>/Kronos

# Cross-compile from Windows
.\build.ps1 -Target Linux
```

> [!WARNING]
> **On this branch the Linux build does not compile yet.** Both scripts now build the same Uno
> project for `linux-x64`, and the data layer still has 49 errors from Windows-only code that the old
> `net10.0` Linux CLI target used to exclude. The scripts fail at the build step rather than skipping
> it. The CLI commands below describe `main`.

Output is self-contained — no .NET runtime needed on the target machine.

**The .NET 10 SDK is installed for you if it is missing.** The project targets `net10.0-desktop`, which
needs the .NET 10 SDK specifically — an installed .NET 8 or 9 will not build it. `build.sh` checks
for the right major version and, if it is not there, downloads Microsoft's official
[`dotnet-install.sh`](https://dot.net/v1/dotnet-install.sh) and installs into `~/.dotnet`. That is a
per-user install, so it does not need `sudo` and does not touch a system-wide installation.

It also appends `~/.dotnet` to `PATH` in your `~/.profile`, so later shells find it too. That
applies to **new** shells — run `source ~/.profile`, or open a new terminal, before building again by
hand.

To install somewhere else, or to skip the auto-install entirely:

```bash
DOTNET_INSTALL_DIR=/opt/dotnet ./build.sh     # different location
```

If the automatic install fails, it prints the manual instructions and exits rather than carrying on
with a missing SDK. You can also install it yourself from
[dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0).

#### CLI commands

```bash
./Kronos version                               # Show version information
./Kronos update --check                        # Check for updates, do not apply
./Kronos update                                # Apply an available update
./Kronos update --force                        # Re-check the feed even if already current
./Kronos self-update --path ./update.tar.gz     # Internal: apply a downloaded update
```

`list`, `swap`, `reset` and `import` are **not implemented yet**. The code they would call is
compiled and reachable in the Linux build — `QueryGames`, `UpdateDllAsync`, `ResetDllAsync` and the
planner — but nothing wires them to command-line arguments or output formatting. Game detection is
the other gap: Steam, GOG, Epic, Ubisoft, Xbox, Battle.net and EA App are all discovered through the
Windows registry, so `IGameLibrary.GetGameLibrary` returns `null` for those stores on Linux and the
load loop skips them.

Steam is the easiest to port, and more so than when this was first written: `libraryfolders.vdf` and
`appmanifest_*.acf` are plain file parsing rather than a registry key, parsers for both already exist
in [`src/Data/Steam/Manifest/`](src/Data/Steam/Manifest/), and `DLLManager` is already shared across
platforms. What is missing is the Linux library roots those paths are resolved against, and the
`IGameLibrary` implementation that walks them.

This is the blocking work for Linux, and it is not a UI task — see
[Uno Platform migration](#uno-platform-migration).

### Prerequisites at a glance

| Building | Needs |
|---|---|
| Windows | .NET 10 SDK, Windows SDK 10.0.26100 |
| Linux | .NET 10 SDK — or just run `build.sh`, which installs it |

### Build script options

| Script | Option | Values |
|---|---|---|
| `build.ps1` | `-Target` | `Windows`, `Linux`, `All` (default: detected from the host OS) |
| `build.ps1` | `-Configuration` | `Release`, `Debug` |
| `build.ps1` | `-LicenseKey` | SixLabors ImageSharp key, if not already in the environment |
| `build.sh` | `$1` | `Windows`, `Linux`, `All` |
| `build.sh` | `$2` | `Release`, `Debug` |

### The ImageSharp licence key

ImageSharp 3.2 and later require a licence. Kronos builds against a **Community Licence**, supplied at
build time — never committed. Set it as an environment variable:

```powershell
$env:IMAGESHARP_LICENSE_KEY = "<your key>"
```

or pass `-LicenseKey` to `build.ps1`. Without it the build fails with a clear error from the
ImageSharp MSBuild target. See [Third-party licences](#third-party-licences).

### Releasing

There is **no CI and no release automation.** Tagging a commit publishes nothing. To cut a release,
run the publishing script from an authenticated checkout with the GitHub CLI logged in:

```powershell
./publish-release.ps1                 # tag, release, build and upload the portable artifact
./publish-release.ps1 -SkipBuild      # notes only
```

It refuses to run rather than doing the wrong thing quietly:

- **version mismatch** — aborts if the requested version disagrees with `src/Kronos.csproj`, because a
  tag that disagrees makes the updater compare against the wrong number
- **dirty working tree** — aborts
- **tag already exists** — aborts
- **short archive** — aborts if the staged copy has fewer files than the build output. `Copy-Item`
  without `-Recurse` copies directories but not their contents, `Compress-Archive` then omits the
  empty ones, and the result is a valid zip that is quietly missing everything that lived in them.
  The archive is counted, not trusted.
- **release exists but the upload failed** — says so, because the retry will otherwise fail on
  "tag already exists" and leave you guessing

Two things the script gets right that are easy to get wrong:

- **The release title begins with the version.** `GitHubUpdater.GetVersionNumber` parses the first
  space-delimited token of the release *title* and requires a leading `v`. A title like
  "Kronos 1.53" parses to 0, which compares below every real version, so the app reports itself as
  up to date forever with nothing logged. The tag is a fallback now, but the title should still be
  right — it is also what the update dialog displays.
- **The updater's repository is `SpaceJamp/Kronos`.** It was `SpaceJamp/kronos-dlss-swapper`, which
  does not exist, so every check 404'd and surfaced as a failed check rather than as a wrong name. A
  test now compares the constant against this repository's own `origin` remote, so a rename breaks
  the build instead of the updater.

> [!NOTE]
> The workflow inherited from upstream was removed. It called `package\*.cmd` scripts that were
> deleted along with the installer, so every one of its steps failed and the checks were permanently
> red — which is worse than no checks, because a red badge trains people to ignore it. In its place,
> **run the tests locally before you push:**
>
> ```powershell
> dotnet test ".\Kronos.sln" -c Release
> ```
>
> If you want automation back, a `build.yml` that restores, builds and runs the tests is about
> twenty lines — the hard parts already work.

## What changed

Kronos is a derivative of upstream DLSS Swapper. The substantive differences:

### Renamed, and moved off upstream's data

- Everything is named `Kronos` (`Kronos.exe`), so it cannot collide with an official installation.
  `RootNamespace` matches `AssemblyName` deliberately: embedded resource names derive from the root
  namespace, and both the DLL manifest and the acknowledgements page look resources up by name.
- **Data lives in `%LOCALAPPDATA%\Kronos` with database `kronos.db`**, not in DLSS Swapper's folder,
  so a Kronos build starts empty rather than inheriting an existing installation.
- The Inno Setup installer and its `package\*.cmd` scripts were removed. Releases now carry a
  self-contained portable archive instead.

**The update check read a repository that does not exist.** `DefaultRepository` was
`SpaceJamp/kronos-dlss-swapper`, and GitHub answers 404 for a name that is not there — which the
check reported as *failed*, so the symptom was never a wrong version offered, just a check that did
nothing, silently, forever. The test written to prevent a stale name had itself locked in the wrong
one. It now compares the constant against this repository's own `origin` remote.

**Assorted correctness fixes** — the Xbox library dereferenced a possibly-null `WindowsIdentity` and
replaced rather than merged artwork when a title was installed in two locations; Steam's
`appmanifest` regex was pinned to a literal backslash and matched nothing on a forward-slash path;
the EA App library resolved its titles file against the working directory and stored install paths
unnormalised, so its games could not be matched by the ignore list; `kronos update --force`
dereferenced a null when already up to date.

### Features

**Portable builds update themselves.** Previously only an installer-style executable was understood,
so a portable release was never offered. A portable build now downloads the archive, verifies it
against the SHA256 GitHub publishes, and hands the replacement to a detached helper that waits for
the process to exit. The helper rolls back `Kronos.exe` if the copy fails. An asset with no
published checksum is refused rather than installed unverified.

**"Update all" acts on the whole library.** There is no checkbox to tick per tile and no selection to
get stuck. The confirmation dialog names every game it is about to write to, which is what makes the
whole-library scope safe.

**Downgrades are warned about.** Swapping in an older runtime than the game shipped with can break
Frame Generation. This was silent: the swap verified the file existed, matched its hash and was
signed, but never compared versions. Kronos asks rather than blocks, because downgrading deliberately
is a valid reason to be in that dialog.

**Swaps are undoable more than once.** DLL backups are a numbered chain rather than one file, and are
*copied* rather than moved on reset — the old code consumed the backup, so a second reset had nothing
to restore and the next swap saved the swapped file as the new "original", permanently losing what the
game shipped with. Legacy `.dlsss` files are adopted, not discarded.

**Game DLL records survive a failed scan.** They are no longer deleted from the database before the
install folder is walked; one unreadable subdirectory used to wipe everything known about a game's
DLLs.

**Responsiveness.** Import dialogs are cancellable, and cancelling the NVIDIA driver import actually
stops it. Toggling a DLSS setting no longer freezes the window — writing the NGXCore registry key used
to shell out to an elevated `reg.exe` and block on the UAC prompt on the UI thread. Busy flags are
cleared on failure instead of leaving buttons permanently disabled.

**Maintainability.** Per-DLL-type logic is driven from one table
([`src/Data/DLLAssetTypeInfo.cs`](src/Data/DLLAssetTypeInfo.cs)) instead of nine parallel if/else
chains. There is an xUnit suite in [`tests/`](tests/) — 658 tests covering backup decisions, swap
versioning, mass-update scope, release parsing, the games page markup and the build scripts. There is
no CI, so run it yourself before pushing: `dotnet test ".\Kronos.sln" -c Release`.

> [!WARNING]
> **Do not edit this file with `Get-Content` / `Set-Content` under Windows PowerShell.**
>
> Windows PowerShell's `Get-Content` defaults to the ANSI codepage for a file with no byte-order mark, so
> it decodes UTF-8 as Windows-1252, and `Set-Content -Encoding utf8` then writes the mangled result back
> out. Every em dash becomes three characters of garbage, every arrow becomes another three, and a BOM is
> added to the front.
>
> It happened here. A one-line change to a number corrupted 58 lines, and it passed the build, because
> nothing compiles a README. The reader saw three characters of Latin-1 garbage where an em dash should
> be — which is easy to miss in a paragraph and impossible to miss once you know to look.
>
> Use an editor that preserves encoding, or pass the encoding explicitly
> (`Get-Content -Encoding utf8`). `TextEncodingTests` fails the build if it happens again, and
> `BuildScriptTests` fails if the test count below goes stale.

### The Linux port

The Linux work in this repository is the maintainer's own — see
[Ownership of the Linux work](#ownership-of-the-linux-work). It is described here because it changed
the shape of the Windows app too.

A [Uno Platform spike](#the-original-evaluation) was also run, to establish whether the
Windows UI could reach Linux without a rewrite. It can — but that is the small half of the problem, and
it is not the half currently blocking anything.

The `net10.0` target had stopped compiling the game model: a change during the original port removed
`Game.cs`, `GameManager.cs`, `DLLManager.cs`, `DLLRecord.cs`, `Manifest.cs`, `GameHistory.cs`,
`GameAsset.cs` and the manually-added library from its compile list without touching their
cross-platform branches, so those branches were never compiled. Those files are back in the list, and
getting them to compile surfaced three pieces of coupling that were worth removing regardless:

- **`App.CurrentApp.RunOnUIThread` was called from the game model.** Marshalling bound-property
  updates is a concern of the UI layer, so calling into a WinUI `Application` from `Game.cs` put the
  whole file out of reach of the CLI. It now goes through `UiDispatcher.Invoke`, which forwards to
  the WinUI dispatcher on Windows and runs inline elsewhere. **Windows behaviour is unchanged.**
- **The shared `HttpClient` lived on `App`.** It was reachable only as `App.CurrentApp.HttpClient`,
  so the file downloader and the Steam cover URL resolver could not compile outside the UI. It moved to
  a standalone `Http` holder that `App` delegates to.
- **`ResourceHelper` was built entirely on WinUI's `ResourceManager`.** It now parses the `.resw` XML
  directly when that is what is available, which is the same data WinUI would have compiled into a
  PRI, so the CLI is genuinely localised rather than printing resource keys. A missing string falls
  back to en-US and then to the key, because a missing label is not a reason to abort whatever wanted
  it.

Three things were also needlessly Windows-only and are now shared: `DLLAssetTypes`, a pure data table,
was behind `#if WINDOWS` despite having no Windows types; `IsInKnownGameAsset` existed twice behind
`#if WINDOWS` with **different signatures** and otherwise identical bodies, so its only two call sites
could not compile elsewhere; and `Game.ResizeCoverAsync` and `AddCustomCover` are plain ImageSharp
work.

The genuinely Windows-only parts stayed Windows-only: `IsInstalled`, the registry-backed store
libraries, and the `PromptTo*` methods that drive a `ContentDialog` and a WinRT file picker.

`build.sh` also installs the .NET 10 SDK when it is absent, and the release scripts were corrected:
`dotnet restore` was being given `-f net10.0`, which it parses as `--force` and forwards as a second
project name, failing with `MSB1008`.

### Signature verification is honest about what it checked

`WinTrust.VerifyEmbeddedSignature` returned a plain `bool`, which cannot distinguish *"checked, and
it passed"* from *"nothing was checked"*. It now returns a three-valued `SignatureCheckResult`:

| Result | Meaning |
|---|---|
| `Valid` | Checked, and it passed |
| `Invalid` | Checked, and it did not pass |
| `Unavailable` | **Nothing was checked** — no Authenticode implementation was available |

Only `Invalid` blocks. `Unavailable` proceeds, because blocking would make the feature impossible
where there is no alternative, but the result is annotated rather than passing as success:

- The import summary dialog shows a **banner in orange with a warning icon**, and its title becomes
  *"N file(s) imported WITHOUT signature verification"*.
- The log records it.
- Windows never returns `Unavailable`, so the banner does not appear in normal use — it is the
  fallback for a build with no Authenticode implementation at all.

Scope worth being precise about: **the download and swap path is hash-verified** — the zip is checked
against `ZipMD5Hash` on download, and the extracted DLL against `MD5Hash` before it is written into a
game. That is plain `System.Security.Cryptography`. This change affects the *import* paths
(`ImportDll` and the NVIDIA driver import), where the file is not in the signed manifest and so has no
expected hash — there, the signature check is the only gate.

The user-facing judgement is unchanged: these are files already on the user's disk that they picked.
The change is that the app now says so rather than implying it verified something.

### Dependencies

`SixLabors.ImageSharp` was moved from 3.1.5 (Apache-2.0) to 4.1.2 under a Community Licence, because
3.2+ is no longer permissively licensed. `SQLitePCLRaw.bundle_e_sqlite3` was moved from 2.1.8 to 3.0.5,
because every 2.x release is affected by CVE-2025-6965 with no patched 2.x available. Both projects now
report **zero known vulnerable packages**:

```
dotnet list src\Kronos.csproj package --vulnerable --include-transitive
# The given project `Kronos` has no vulnerable packages given the current sources.
```

### Unchanged on purpose

The DLL manifest is still fetched from upstream's public `beeradmoore.github.io` endpoint, and the
DLLs themselves still come from NVIDIA. Kronos depends on those staying available.

Per-file detail is in the commit history.

## Release notes

Notes for significant releases are published as [GitHub Releases](https://github.com/SpaceJamp/Kronos/releases)
rather than kept in this file. Routine changes are in the commit history and are not written up.

## Where data is stored

| Configuration | Location | Contents |
|---|---|---|
| Installed | `%LOCALAPPDATA%\Kronos\` | `kronos.db`, `json\`, `dlls\`, `image_cache\`, `logs\` |
| Portable | `<build output>\StoredData\` | same layout, self-contained |

## Branding and assets

`src\Assets\icon.ico` is the one that matters — it is `<ApplicationIcon>` in the csproj, so it sets the
executable icon, and `Assets\icon_256.png` is what the in-window title bar loads. Both must survive
into the published archive; the archive is file-counted before upload for exactly that reason. The
`*Logo*.png` and `*Tile*.png` files are upstream's MSIX tile set and are unused by this unpackaged
build.

## Contributing

This is a personal project and is not accepting contributions. Issues with upstream DLSS Swapper
belong [upstream](https://github.com/beeradmoore/dlss-swapper/issues). Bugs in the local changes may
still be worth reporting upstream — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Upstream project

- GitHub: https://github.com/beeradmoore/dlss-swapper/
- Reddit: https://www.reddit.com/r/DLSS_Swapper/

### Credits

**Kronos would not exist without [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
[beeradmoore](https://github.com/beeradmoore) and contributors.** It is released under the same licence
with the original copyright notices intact. Please support the upstream project rather than treating
this as a replacement for it.

Upstream receives free Windows code signing from [SignPath.io](https://signpath.io/) via the
[SignPath Foundation](https://www.signpath.com/solutions/for-open-source-community-foundation). That
sponsorship covers the official project only; Kronos builds are unsigned and are not covered by it.

## License

[GPL-3.0](LICENSE), the same licence as upstream. Kronos is a derivative work and cannot be relicensed.
Per GPL-3.0 §5(a) the modifications are stated in [What changed](#what-changed). See [NOTICE](NOTICE)
for copyright ownership details.

`DLSS`, `FSR`, `FidelityFX` and `XeSS` are trademarks of their respective owners; this project is not
affiliated with NVIDIA.

### Third-party licences

| Component | Licence | Notes |
|---|---|---|
| [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) 4.1.2 | SixLabors Community Licence | Processes game cover art (download, resize, convert, cache). Free for non-commercial open-source use. Key supplied at build time via `IMAGESHARP_LICENSE_KEY`; never committed. [Apply for a key](https://licensing.sixlabors.com/). |
| SQLitePCLRaw 3.0.5 | MIT | Bundled SQLite, via `sqlite-net`. |

Contributors building from source need their own ImageSharp key; see
[the build section](#the-imagesharp-licence-key).
