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
> **The Windows GUI requires a 64-bit processor. There is no 32-bit build and never
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

## Platform Support
 
| Platform | GUI | CLI | Notes |
|---|---|---|---|
| **Windows x64** | ✅ WinUI 3 | ✅ | Full support, native DLL swapping, auto game detection |
| **Linux x64** | 🚧 Avalonia (WIP) | ✅ CLI | CLI works; GUI needs fixes |
| **macOS** | ❌ | ❌ | Not supported (no DLSS/FSR/XeSS on macOS) |
 
**Windows GUI** (WinUI 3): Primary interface with full game library detection (Steam, GOG, Epic, etc.), visual DLL management, and one-click swapping.
 
**Linux GUI** (Avalonia): *Work in progress* — core cross-platform libraries work, but GUI has compilation errors. Runs on **Wayland only** when fixed (no X11 support). Game detection will be manual (no Windows registry access).
 
**Linux CLI**: Working — core cross-platform libraries compile and run. Implements `update`, `version`, and `self-update` commands. Full DLL management commands (list, swap, reset, import) need cross-platform file list completed.

> **Ownership notice:** The Linux build (CLI and GUI) is **fully owned and maintained by this repository's maintainer**. It is not affiliated with, endorsed by, or supported by the upstream DLSS Swapper project. Bugs, issues, and feature requests for the Linux build should be filed in this repository, not upstream.

## Requirements

### Windows GUI
| | |
|---|---|
| **OS** | Windows 10 (20H1, build 19041) or newer, **x64** |
| **CPU** | **64-bit** (x64 or ARM64) — 32-bit is not supported |
| **GPU** | Any |
| **To build** | .NET 10 SDK, Windows SDK 10.0.26100 |

### Linux CLI (Working) / GUI (Work in Progress)
| | |
|---|---|
| **OS** | Any modern Linux (glibc 2.31+) |
| **CPU** | x64 |
| **To build** | .NET 10 SDK |
| **Runtime** | Self-contained (no system .NET required) |
| **Status** | CLI ✅ Builds and runs · GUI 🚧 Compilation errors |

> **⚠️ Ownership:** The Linux build is **fully owned by this repository's maintainer**. It is not part of the upstream DLSS Swapper project. Do not report Linux issues upstream — file them here.

## Getting it

**Official, signed Windows build:** [upstream releases](https://github.com/beeradmoore/dlss-swapper/releases) or
`winget install --id=beeradmoore.dlss-swapper -e`. Those are the only official sources.

**Kronos:** This repository **does not provide pre-built executables or installers**. You must build from source:
- See [Building](#building) for Windows GUI build instructions.
- See [Building for Linux](#building-for-linux) for Linux GUI/CLI build instructions.

> **Why no binaries?** Kronos builds are **unsigned** (upstream uses SignPath credentials not available here). Distributing unsigned binaries triggers SmartScreen warnings and security concerns. Building from source ensures you get exactly what's in the repository.

Kronos does not add DLSS to games that do not support it, and swapping DLLs is not guaranteed to
improve performance, reduce artifacts, or avoid crashes. Downgrading a runtime below the version a
game shipped with can disable Frame Generation, so Kronos warns before it does that. Originals are
always restorable.

## Supported libraries (Windows GUI only)

Steam · GOG · Epic Games · Ubisoft Connect · Xbox App · Battle.net · manually added via **Add Game**

On Linux, you must manually specify game installation paths (no Windows registry access).

## What changed

Kronos is a derivative of upstream DLSS Swapper. The substantive differences:

**Renamed, and moved off upstream's data**

- Everything is named `Kronos` (`Kronos.exe`), including the installer's registry key and Start Menu
  entry, so it cannot collide with an official installation. `RootNamespace` was changed to match
  `AssemblyName` — the two are kept in step deliberately, because embedded resource names derive from
  the root namespace and both the DLL manifest and the acknowledgements page look resources up by
  name.
- **Data now lives in `%LOCALAPPDATA%\Kronos` with database `kronos.db`**, not in DLSS Swapper's
  folder. A Kronos build therefore starts with an empty database rather than inheriting an existing
  installation's games and settings.
- **Linux data** lives in `$XDG_DATA_HOME/Kronos` (typically `~/.local/share/Kronos`) with database `kronos.db`.

**Installer rewritten in Inno Setup 6** — *Removed in this fork*

The installer/packaging scripts have been removed. Users build from source.

**Downgrades are warned about**

Swapping in an older runtime than the game shipped with can break Frame Generation. This was silent:
`UpdateDllAsync` verified the file existed, matched its hash and was signed, but never compared
versions. Kronos asks rather than blocks, because downgrading deliberately is a valid reason to be in
that dialog.

**Swaps are undoable more than once**

DLL backups are a numbered chain (`.kronosbak1`, `.kronosbak2`, …) rather than one `.dlsss`. The old
single backup was *moved* back on reset, consuming it — so a second reset had nothing to restore, and
the next swap saved the swapped file as the new "original", permanently losing what the game shipped
with. Backups are copied now and the chain is kept. Legacy `.dlsss` files are adopted, not discarded.

**Correctness fixes**

Among others: game DLL records are no longer deleted from the database before the install folder is
scanned (one unreadable subdirectory used to wipe everything known about a game's DLLs);
`RunOnUIThreadAsync` no longer discards its callback (which could leave a game permanently
unopenable); `WinTrust.VerifyEmbeddedSignature` no longer leaks two unmanaged blocks per DLL swap;
imported DLLs are actually migrated out of legacy pre-1.1.7 zip folders; and backups are created
per-DLL rather than all-or-nothing.

**Responsiveness**

Import dialogs are cancellable, and cancelling the NVIDIA driver import actually stops it. Toggling a
DLSS setting no longer freezes the window — writing the NGXCore registry key shelled out to an
elevated `reg.exe` and blocked on the UAC prompt on the UI thread. Busy flags are cleared on failure
instead of leaving buttons permanently disabled.

**Maintainability**

Per-DLL-type logic is driven from one table ([`src/Data/DLLAssetTypeInfo.cs`](src/Data/DLLAssetTypeInfo.cs))
instead of nine parallel if/else chains. There is an xUnit suite ([`tests/`](tests/)) which CI now runs
on every build.

**Cross-platform core**

The core DLL management logic (downloading, verification, swapping, database) is now multi-targeted
for .NET 10, enabling Linux GUI (Avalonia) and CLI builds. Windows-specific code (WinUI 3, Registry, WinTrust, Win32)
is conditionally compiled.

**Unchanged on purpose**

The DLL manifest is still fetched from upstream's public `beeradmoore.github.io` endpoint and the
DLLs themselves still come from NVIDIA. Kronos depends on those staying available.

Per-file detail is in the commit history.

## Building

### Windows GUI (WinUI 3)

```powershell
dotnet build ".\Kronos.sln" -c Release_Portable
dotnet test ".\tests\Kronos.Tests\Kronos.Tests.csproj" -c Release_Portable
```

All four configurations (`Debug`, `Release`, `Debug_Portable`, `Release_Portable`) build with zero
warnings. `Debug` writes to a separate `DEBUG` folder under `%LOCALAPPDATA%\Kronos`, and `Portable`
keeps all data inside the build output, so neither touches a real installation.

**x64 only, for the project as well as the output.** `<Platforms>` is `x64` and `RuntimeIdentifier`
is `win-x64`. Do not add a 32-bit configuration; see the warning at the top.

### Linux GUI (Avalonia) & CLI
 
**CLI (working):**
```bash
./build.sh
# Output: Output/linux-x64/Kronos (CLI)
```

**GUI (work in progress):**
> ⚠️ GUI has compilation errors. The cross-platform core libraries work, but GUI-specific files need fixes.
 
**On Windows (cross-compile):**
```powershell
.\build.ps1
```
 
**Requirements:** .NET 10 SDK
 
**Expected Output:** 
- `Output/linux-x64/Kronos` — CLI (self-contained single binary)
- `Output/linux-x64/Kronos.Avalonia` — GUI (self-contained, *when fixed*)
 
Both builds are self-contained — no .NET runtime installation required on the target machine.

### Release artifacts (cross-platform)
 
**Publishing a release.** Tag the commit with `v<version>` (e.g., `v1.50`). The GitHub Actions workflow
builds and publishes the following artifacts:
 
| Platform | Artifact | Notes |
|---|---|---|
| Windows | `Kronos-v{version}-portable.zip` | Portable, self-contained GUI |
| Linux x64 | `kronos-v{version}-linux-x64.tar.gz` | CLI (CLI works, GUI WIP) |
 
The in-app/CLI update check reads the release *title* (`v{version}`) and finds exactly one matching
asset per platform. Assets must include a `sha256` field (auto-generated by workflow) for verification.

## Linux Usage
 
### CLI (Working)
```bash
# Check for updates
./Kronos update --check
 
# Apply update
./Kronos update
 
# Show version info
./Kronos version
 
# Internal self-update (called by external updater)
./Kronos self-update --path /path/to/update.tar.gz
```

> **Note:** The CLI implements `update`, `version`, and internal `self-update` commands. Full DLL management commands (list, swap, reset, import) require completing the cross-platform file list.

### GUI (Avalonia) — *Work in Progress*
```bash
./Kronos.Avalonia
```
Full graphical interface with game list, DLL version management, settings, and auto-updater. Game detection is manual — add games via "Add Game" with their install path. **Currently has compilation errors.**

## Branding and assets

`src\Assets\icon.ico` is the one that matters — it is `<ApplicationIcon>` in the csproj, so it sets the executable icon. The `*Logo*.png` and `*Tile*.png` files are upstream's MSIX tile set and are unused by this unpackaged build; `icon_256.png` is only for MSIX, which is not used.

`DLSS`, `FSR`, `FidelityFX` and `XeSS` are trademarks of their respective owners.

## Contributing

This is a personal project and is not accepting contributions. Issues with upstream DLSS Swapper
belong [upstream](https://github.com/beeradmoore/dlss-swapper/issues). Bugs in the local changes may
still be worth reporting upstream — see [CONTRIBUTING.md](CONTRIBUTING.md).

## Upstream project

- GitHub: https://github.com/beeradmoore/dlss-swapper/
- Reddit: https://www.reddit.com/r/DLSS_Swapper/

### Credits

**Kronos would not exist without [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
[beeradmoore](https://github.com/beeradmoore) and contributors.** It is released under the same
licence with the original copyright notices intact. Please support the upstream project rather than
treating this as a replacement for it.

Upstream receives free Windows code signing from [SignPath.io](https://signpath.io/) via the
[SignPath Foundation](https://www.signpath.com/solutions/for-open-source-community-foundation). That
sponsorship covers the official project only; Kronos builds are unsigned and are not covered by it.

## License

[GPL-3.0](LICENSE), the same licence as upstream. Kronos is a derivative work and cannot be
relicensed. Per GPL-3.0 §5(a) the modifications are stated in
[What changed](#what-changed). See [NOTICE](NOTICE) for copyright ownership details.
`DLSS`, `FSR`, `FidelityFX` and `XeSS` are trademarks of their
respective owners; this project is not affiliated with NVIDIA.

### Third-Party Licenses

**SixLabors.ImageSharp 3.2.0+** — Used under [SixLabors Community License](https://sixlabors.com/pricing#community).
This library processes game cover art (download, resize, convert, cache). The community license
is granted for non-commercial open-source projects. The license key is provided at build time
via the `IMAGESHARP_LICENSE_KEY` environment variable (not committed to source).
