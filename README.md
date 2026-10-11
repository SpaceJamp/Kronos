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
> **The Windows build requires a 64-bit processor. There is no 32-bit build and never
> will be.** A 32-bit CPU cannot execute the `x86_64` instructions in the app, the .NET runtime and the
> bundled SQLite — it is a hardware limit.
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
| **Windows x64** | `net10.0-windows10.0.26100.0` | ✅ **Working.** WinUI 3 GUI with game detection and DLL swapping. |
| **Linux x64** | `net10.0` | ⏸️ **Paused — not being worked on.** The target still compiles, but no build has ever been run on Linux and the CLI cannot do anything useful. See [Linux support is paused](#linux-support-is-paused). |
| **Linux x64 GUI** | — | ❌ **Does not build.** The Avalonia project depends on types not present in the Linux target. |
| **macOS** | — | ❌ Not supported (no DLSS/FSR/XeSS on macOS). |

Everything below describes the **Windows** build unless stated otherwise. That is the only platform
where Kronos does the thing its name describes end to end.

### Linux support is paused

**Linux support has been dropped for now.** It is not unfinished work waiting to be picked up — it is
being set aside, and this section says so plainly so nobody wastes time on it.

The reason is straightforward: **no Kronos build has ever been run on Linux.** Every Linux claim in
this repository rests on the target compiling on a Windows machine and on tests that mock the
platform. The first real run on real Linux kept failing on restore, and each fix was verified against
a stub rather than against Linux itself. Continuing to build on that basis would be guessing.

What exists, and what state it is in:

- The `net10.0` target compiles. `build.sh` works, including installing the .NET 10 SDK when it is
  absent — that part was confirmed on a real Linux machine before the pause.
- The game model (`Game`, `GameAsset`, `GameHistory`), `DLLManager`, `DLLRecord`, `DLLAssetTypes` and
  the SQLite database are compiled and reachable.
- Manifest download and deserialisation work, including localised display names — the `.resw` files
  are parsed directly instead of through WinUI's `ResourceManager`.
- **But the CLI only has `update`, `version` and `self-update`.** Nothing wires `list`, `swap` or
  `reset` to command-line arguments, so there is nothing on Linux that does what Kronos is for.
- **No game detection.** Steam, GOG, Epic, Ubisoft, Xbox, Battle.net and EA App are all discovered
  through the Windows registry, and there is no registry on Linux. Each would need discovery written
  against that platform's on-disk layout — Steam's `libraryfolders.vdf` and `appmanifest_*.acf`, for
  instance, which is plain file parsing rather than a registry key.
- **The Avalonia GUI does not compile.**

None of this is hidden or reverted. If Linux is picked up again, the starting point is the one
described above rather than the broken state an earlier change left it in — see
[What changed](#what-changed).

> **Ownership notice:** Any Linux work is owned and maintained by this repository's maintainer. It is
> not affiliated with, endorsed by, or supported by the upstream DLSS Swapper project.

## Requirements

### Windows (building and running)

| | |
|---|---|
| **OS** | Windows 10 (20H1, build 19041) or newer, **x64** |
| **CPU** | **64-bit** (x64 or ARM64) — 32-bit is not supported |
| **GPU** | Any |
| **To build** | .NET 10 SDK, Windows SDK 10.0.26100 |

### Linux (building only — paused)

> Paused. See [Linux support is paused](#linux-support-is-paused).

| | |
|---|---|
| **OS** | Any modern Linux (glibc 2.31+) |
| **CPU** | x64 |
| **To build** | .NET 10 SDK — `build.sh` installs it if missing |
| **Runtime** | Self-contained (no system .NET required) |

## Getting it

**Official, signed Windows build:** [upstream releases](https://github.com/beeradmoore/dlss-swapper/releases) or
`winget install --id=beeradmoore.dlss-swapper -e`. Those are the only signed builds of this software.

**Kronos:** download `Kronos-<version>.0-portable.zip` from the
[Releases page](https://github.com/SpaceJamp/Kronos/releases). Unzip anywhere and run `Kronos.exe`.
No installer, no console window, and no .NET runtime required.

> **Needs the Windows App Runtime.** This is an unpackaged WinUI application, so it resolves
> `Microsoft.UI.Xaml.dll` from the Windows App Runtime rather than carrying it. If `Kronos.exe` exits
> immediately without showing a window, install the
> [Windows App Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) — upstream's
> signed installer installs it as part of setup.

> **These builds are unsigned.** Expect SmartScreen to warn; that is inherent to distributing binaries
> without signing credentials, not a sign of tampering. GitHub publishes a SHA256 for every asset, shown
> beside its download link, and Kronos verifies it on any in-app update it performs.

> **Kronos will not auto-update a portable build.** The in-app updater looks for an installer-style
> executable and deliberately ignores a portable zip, rather than replacing a running copy unasked.
> Take new versions from the Releases page.

**Building from source** produces the same binary, and is covered in [Building](#building). It needs
your own ImageSharp licence key.

Kronos does not add DLSS to games that do not support it, and swapping DLLs is not guaranteed to
improve performance, reduce artifacts, or avoid crashes. Downgrading a runtime below the version a
game shipped with can disable Frame Generation, so Kronos warns before it does that. Originals are
always restorable.

## Supported game libraries (Windows)

Steam · GOG · Epic Games Store · Ubisoft Connect · Xbox App · Battle.net · EA App · games added
manually via **Add Game**

## Building

### Windows (WinUI 3 GUI)

```powershell
# Build
dotnet build ".\src\Kronos.csproj" -f net10.0-windows10.0.26100.0 -c Release

# Test (366 tests)
dotnet test ".\Kronos.sln" -c Release

# Self-contained portable build → Output/win-x64-portable-<timestamp>/
.\build.ps1 -Target Windows
```

Or build the solution directly: `dotnet build ".\Kronos.sln" -c Release`.

All four configurations (`Debug`, `Release`, `Debug_Portable`, `Release_Portable`) build. `Debug`
writes to a separate `DEBUG` folder under `%LOCALAPPDATA%\Kronos`, and the `Portable`
configurations keep all data inside the build output, so neither touches a real installation.

**x64 only, for the project as well as the output.** `<Platforms>` is `x64` and the runtime is
`win-x64`. Do not add a 32-bit configuration; see the warning at the top.

### Linux

> **Paused — see [Linux support is paused](#linux-support-is-paused).** `build.sh` still works and the
> `net10.0` target still compiles, but the CLI has no commands that do anything useful, and no
> resulting binary has ever been run on Linux. Treat anything below as untested on the platform it
> describes.

```bash
# CLI — updater commands only
./build.sh
# → Output/linux-x64-<timestamp>/Kronos

# Cross-compile from Windows
.\build.ps1 -Target Linux
```

Output is self-contained — no .NET runtime needed on the target machine.

**The .NET 10 SDK is installed for you if it is missing.** The project targets `net10.0`, which
needs the .NET 10 SDK specifically — an installed .NET 8 or 9 will not build it. `build.sh` checks
for the right major version and, if it is not there, downloads Microsoft's official
[`dotnet-install.sh`](https://dot.net/v1/dotnet-install.sh) and installs into `~/.dotnet`. That is a
per-user install, so it does not need `sudo` and does not touch a system-wide installation.

It also appends `~/.dotnet` to `PATH` in your `~/.profile`, so later shells find it too. That
applies to **new** shells — run `source ~/.profile`, or open a new terminal, before building again
by hand.

To install somewhere else, or to skip the auto-install entirely:

```bash
DOTNET_INSTALL_DIR=/opt/dotnet ./build.sh     # different location
```

If the automatic install fails, it prints the manual instructions and exits rather than carrying on
with a missing SDK. You can also install it yourself from
[dotnet.microsoft.com/download/dotnet/10.0](https://dotnet.microsoft.com/download/dotnet/10.0).

### Prerequisites at a glance

| Building | Needs |
|---|---|
| Windows | .NET 10 SDK, Windows SDK 10.0.26100 |
| Linux | .NET 10 SDK — or just run `build.sh`, which installs it |

#### Linux commands

```bash
./Kronos version                          # Show version information
./Kronos update --check                   # Check for updates, do not apply
./Kronos update                           # Apply an available update
./Kronos update --force                   # Reinstall the current release even if already on it
./Kronos self-update --path ./update.tar.gz   # Internal: apply a downloaded update
```

`list`, `swap`, `reset` and `import` are **not implemented**, and are not scheduled. The code they
would call is compiled and reachable in the Linux build; what is missing is the argument parsing and
output formatting on top of it.

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
$env:IMAGESHARP_LICENSE_KEY = "<your key>"   # PowerShell
export IMAGESHARP_LICENSE_KEY="<your key>"    # bash
```

or pass `-LicenseKey` / as the fourth argument to `build.ps1` / `build.sh`. Without it the build fails
with a clear error from the ImageSharp MSBuild target. See
[Third-party licences](#third-party-licences).

### Releasing

There is **no CI and no release automation.** Tagging a commit publishes nothing. To cut a release,
run the publishing script from an authenticated checkout:

```powershell
./publish-release.ps1                 # tag, release, build and upload the portable artifact
./publish-release.ps1 -SkipBuild      # notes only
```

It refuses to run rather than doing the wrong thing quietly:

- **version mismatch** — aborts if the requested version disagrees with `src/Kronos.csproj`, because a
  tag that disagrees makes the updater compare against the wrong number
- **dirty working tree** — aborts
- **tag already exists** — aborts
- **release exists but the upload failed** — says so, because the retry will otherwise fail on
  "tag already exists" and leave you guessing

The release title must begin with the version, and the script sets it to `v<version>` for a reason:
`GitHubUpdater.GetVersionNumber` parses the first space-delimited token of the release **title** and
requires a leading `v`. A title like "Kronos 1.53" parses to 0, which compares below every real
version, so the app would report itself as up to date forever with nothing logged. The tag is used as
a fallback now, but the title should still be right — it is what the update dialog displays.

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
> If you want automation back, a `build.yml` that restores, builds and runs the tests on both target
> frameworks is about twenty lines — the hard parts already work.

## What changed

Kronos is a derivative of upstream DLSS Swapper. The substantive differences:

### Renamed, and moved off upstream's data

- Everything is named `Kronos` (`Kronos.exe`), so it cannot collide with an official installation.
  `RootNamespace` matches `AssemblyName` deliberately: embedded resource names derive from the root
  namespace, and both the DLL manifest and the acknowledgements page look resources up by name.
- **Data lives in `%LOCALAPPDATA%\Kronos` with database `kronos.db`**, not in DLSS Swapper's folder,
  so a Kronos build starts empty rather than inheriting an existing installation.
- The Inno Setup installer and packaging scripts were **removed**. Users build from source.

### Bugs fixed

**The Games page rendered empty.** A refactor for Linux support rebuilt the collection views with
`Source = _allGames` — a list of `Game` — while leaving `IsSourceGrouped = true`, and gave each
library's view a throwaway `ObservableCollection` instead of the real collection. WinUI tried to group
individual games by a property they do not have and produced nothing, so the Games page showed no
games on any build, even though detection was working and the database was full. The original design —
a list of `GameGroup` objects over filtered views of the shared collection — has been restored.

**"Reset to default" never worked.** When backups were changed from one `.dlsss` file to a numbered
chain (`.kronosbak1`, `.kronosbak2`, …), the write paths were updated but the read paths were not.
Reset looked for the literal string `".dlsss"` in a path that does not contain it, matched no primary
record, and reported *"repair your game manually"* for every DLL swapped by the current build. The
same omission meant a rescan found no backups, dropped those records from memory and deleted them from
the database — leaving the files on disk untracked, which made reset permanently impossible and made
each later swap append yet another chain entry.

**One unreadable registry key could delete a user's game data.** The Ubisoft Connect library used
`break` where `continue` was meant, so a single permission-denied install subkey abandoned the rest of
the enumeration — and the cleanup pass deletes cached games that were not rediscovered, taking their
assets, history and notes with them.

**A corrupt cover file hid the game entirely.** The GOG library used `continue` on a cover-art
failure, which skipped the calls that save and process the game, so a missing or corrupt
`webcache.zip` made an installed, working game never appear at all.

**Five thread-safety holes in the game scanner.** Several call sites handed the live game-assets list
to the database or to a `Where`/`Select` while running on a thread pool thread. `ProcessGame` runs up
to four scans concurrently and clears that list to replace it, so any interleaving threw "collection
was modified" — which a catch-all converted into "this game has no DLLs", permanently.

**A single failed cover fetch broke cover art for the whole library.** The in-progress flag guarding
cover loading was cleared by a trailing statement rather than a `finally`, so one network failure left
it stuck and every subsequent call returned immediately.

**Assorted correctness fixes** — the Xbox library dereferenced a possibly-null `WindowsIdentity` and
replaced rather than merged artwork when a title was installed in two locations; Steam's
`appmanifest` regex was pinned to a literal backslash and matched nothing on a forward-slash path;
the EA App library resolved its titles file against the working directory and stored install paths
unnormalised, so its games could not be matched by the ignore list; `kronos update --force`
dereferenced a null when already up to date.

### Features

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
chains. There is an xUnit suite in [`tests/`](tests/) — 366 tests covering backup decisions, swap
versioning, equality contracts, path helpers and more. There is no CI, so run it yourself before
pushing: `dotnet test ".\Kronos.sln" -c Release`.

### Linux core restored to the build

The Linux target had stopped compiling the game model. A change made during the original port
removed `Game.cs`, `GameManager.cs`, `DLLManager.cs`, `DLLRecord.cs`, `Manifest.cs`, `GameHistory.cs`,
`GameAsset.cs` and the manually-added library from the `net10.0` compile list, without touching their
cross-platform branches — so those branches had never been compiled, and the Linux CLI was an
updater that shared nothing with the Windows app. Those files are back in the list. Getting them to
compile surfaced three pieces of coupling that were worth removing regardless of platform:

- **`App.CurrentApp.RunOnUIThread` was called from the game model.** Marshalling bound-property
  updates is a genuine concern of the UI layer, so calling into a WinUI `Application` from
  `Game.cs` put the whole file out of reach of the Linux build. It now goes through
  `UiDispatcher.Invoke`, which forwards to the WinUI dispatcher on Windows and runs inline on Linux.
  The Windows behaviour is unchanged.
- **The shared `HttpClient` lived on `App`.** It was reachable only as `App.CurrentApp.HttpClient`,
  so the file downloader and the Steam cover URL resolver — both of which the Linux target needs —
  could not compile there. It moved to a standalone `Http` holder that `App` now delegates to.
  Nothing about it was Windows-specific.
- **`ResourceHelper` was built entirely on WinUI's `ResourceManager`.** On Linux it now parses the
  `.resw` XML directly, which is the same data WinUI would have compiled into a PRI, so the CLI is
  genuinely localised rather than printing resource keys. A missing string falls back to
  en-US and then to the key, because a missing label is not a reason to abort whatever wanted it.

Three things were also needlessly Windows-only and are now shared:

- `DLLAssetTypes`, a pure data table, was behind `#if WINDOWS` despite having no Windows types.
- `IsInKnownGameAsset` existed twice behind `#if WINDOWS` with **different signatures** —
  `(GameAsset, Game)` and `(GameAsset, GameLibrary, string?)` — and otherwise identical bodies. The
  signature mismatch meant its only two call sites could not compile for Linux at all. There is now
  one implementation taking the `Game`.
- `Game.ResizeCoverAsync` and `AddCustomCover` are plain ImageSharp work, and were excluded on Linux
  even though the ImageSharp package is licensed for both.

The genuinely Windows-only parts stayed Windows-only: `IsInstalled`, the registry-backed store
libraries, and the `PromptTo*` methods that drive a `ContentDialog` and a WinRT file picker.

**The Avalonia GUI still does not build**, and the Linux CLI still has no DLL commands. Linux work
is paused — see [Linux support is paused](#linux-support-is-paused).

### Signature verification is honest about what it checked

`WinTrust.VerifyEmbeddedSignature` used to return `bool`, and its Linux implementation returned
`true` — which reads as "verified" when nothing was checked at all. Anyone importing a DLL on Linux
got a clean success and no indication that the one check Windows performs simply does not exist
there.

It now returns a three-valued `SignatureCheckResult`:

| Result | Meaning |
|---|---|
| `Valid` | Checked, and it passed |
| `Invalid` | Checked, and it did not pass |
| `Unavailable` | **Nothing was checked** — the platform has no Authenticode implementation |

Only `Invalid` blocks. `Unavailable` proceeds, because blocking would make the feature impossible
where it has no alternative, but the result is annotated:

- The import summary dialog shows a **banner in orange with a warning icon**, and its title becomes
  *"N file(s) imported WITHOUT signature verification"*.
- The log records it.
- Windows never returns `Unavailable`, so the banner never appears there.

Scope worth being precise about: **the download and swap path was already hash-verified** on both
platforms — the zip is checked against `ZipMD5Hash` on download, and the extracted DLL against
`MD5Hash` before it is written into a game. That check is plain `System.Security.Cryptography` and
worked on Linux the whole time. This change affects the *import* paths (`ImportDll` and the NVIDIA
driver import), where the file is not in the signed manifest and so has no expected hash — there,
the signature check was the only gate, and on Linux there is none.

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

The DLL manifest is still fetched from upstream's public `beeradmoore.github.io` endpoint, and the DLLs
themselves still come from NVIDIA. Kronos depends on those staying available.

Per-file detail is in the commit history.

## Release notes

Notes for significant releases are published as [GitHub Releases](https://github.com/SpaceJamp/Kronos/releases)
rather than kept in this file. Routine changes are in the commit history and are not written up.

## Where data is stored

| Platform | Location | Contents |
|---|---|---|
| Windows | `%LOCALAPPDATA%\Kronos\` | `kronos.db`, `json\`, `dlls\`, `image_cache\`, `logs\` |
| Windows (Portable) | `<build output>\StoredData\` | same layout, self-contained |
| Linux | `$XDG_DATA_HOME/Kronos/` (default `~/.local/share/Kronos`) | same layout |

## Branding and assets

`src\Assets\icon.ico` is the one that matters — it is `<ApplicationIcon>` in the csproj, so it sets the
executable icon. The `*Logo*.png` and `*Tile*.png` files are upstream's MSIX tile set and are unused by
this unpackaged build.

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