<p align="center">
  <h2 align="center">Kronos</h2>
  <p align="center">Download, manage and swap <strong>DLSS</strong>, <strong>FSR</strong> and <strong>XeSS</strong> DLLs, so you can change the version a game runs without the game itself updating.</p>
</p>

> [!IMPORTANT]
> **Kronos is based on [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
> [beeradmoore](https://github.com/beeradmoore), licensed under the
> [GNU GPL v3.0](LICENSE).** It is an independent modification, not affiliated with or endorsed by
> the original maintainer or NVIDIA. Original copyright notices are preserved and beeradmoore is
> credited in [LICENSE](LICENSE), in the installer's file properties and under [Credits](#credits).
>
> Upstream warns that malicious sites impersonate DLSS Swapper. If you want the **official, signed**
> build, use **[beeradmoore/dlss-swapper](https://github.com/beeradmoore/dlss-swapper)**, not this.
>
> Changes made here are listed in [What changed](#what-changed) and were written with AI assistance,
> disclosed in [AI_ASSISTED.md](AI_ASSISTED.md).

> [!WARNING]
> **Kronos requires a 64-bit processor and 64-bit Windows. There is no 32-bit build and never will
> be.** A 32-bit CPU cannot execute the `x86_64` instructions in the app, the .NET runtime and the
> bundled SQLite — it is a hardware limit, not a setting. A 32-bit *Windows* cannot load a 64-bit
> program at all, though installing 64-bit Windows fixes that case. Windows will not offer you a
> meaningful error, it just says the app cannot run on your PC, so check the two fields below first.
>
> | `msinfo32` field (`Win`+`R` → `msinfo32`) | Value | Meaning |
> |---|---|---|
> | **System Type** | `x64-based PC` | 64-bit Windows. Kronos runs. |
> | **System Type** | `x86-based PC` | 32-bit **Windows**. Check *Processor* next. |
> | **Processor → Architecture** | `x64` or `ARM64` | 64-bit **CPU**. Supported. |
> | **Processor → Architecture** | `x86` | 32-bit **CPU**. No version of Windows can fix this. |
>
> `System Type` describes your OS, not your CPU — an `x86-based PC` there does not necessarily mean
> a 32-bit processor, which is why *Processor → Architecture* is the field that settles it.
>
> Kronos checks `Environment.Is64BitOperatingSystem` on launch and shows an explanation rather than
> failing obscurely. The underlying reason it is x64-only: the DLLs it swaps
> (`nvngx_dlss.dll`, `nvngx_dlssg.dll`, `amd_fidelityfx_dx12.dll`, …) only exist as 64-bit binaries.

## Requirements

| | |
|---|---|
| **OS** | Windows 10 (20H1, build 19041) or newer, **x64** |
| **CPU** | **64-bit** (x64 or ARM64) — 32-bit is not supported |
| **GPU** | Any |
| **To build** | .NET 10 SDK, Windows SDK 10.0.26100 |
| **To package** | PowerShell 7 (`pwsh`); Inno Setup 6 for the installer |

## Getting it

**Official, signed build:** [upstream releases](https://github.com/beeradmoore/dlss-swapper/releases) or
`winget install --id=beeradmoore.dlss-swapper -e`. Those are the only official sources.

**Kronos:** grab an installer from [Releases](../../releases). Binaries are **unsigned** — upstream
signs via SignPath, and those credentials are not available here — so expect a SmartScreen warning on
first run. You can also [build it yourself](#building).

Kronos does not add DLSS to games that do not support it, and swapping DLLs is not guaranteed to
improve performance, reduce artifacts, or avoid crashes. Downgrading a runtime below the version a
game shipped with can disable Frame Generation, so Kronos warns before it does that. Originals are
always restorable.

## Supported libraries

Steam · GOG · Epic Games · Ubisoft Connect · Xbox App · Battle.net · manually added via **Add Game**

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

**Installer rewritten in Inno Setup 6** (`package\Installer.iss`, was NSIS)

- Asks per-user or per-machine. Upstream's NSIS script could not express this — its components page
  ran before the user chose anything, so "for all users" silently behaved like "just for you".
- Inno's own uninstaller replaces one that replayed an `uninstall.log` written during install, which
  left behind anything the app created afterwards. Per-user data is removed on uninstall only for a
  per-user install, since deleting it machine-wide would discard one user's games while everyone else
  kept theirs.
- The generated 757-line `FileList.nsh` is gone, so a clean clone builds without an uncommitted file.
- `App.xaml.cs` finds its uninstall entry in `HKLM` as well as `HKCU`; the old HKCU-only lookup meant
  "Estimated size" in Apps & features silently stopped updating for machine-wide installs.

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

**Unchanged on purpose**

The DLL manifest is still fetched from upstream's public `beeradmoore.github.io` endpoint and the
DLLs themselves still come from NVIDIA. Kronos depends on those staying available.

Per-file detail is in the commit history.

## Building

```powershell
dotnet build ".\Kronos.sln" -c Debug
dotnet test ".\tests\Kronos.Tests\Kronos.Tests.csproj" -c Debug
```

All four configurations (`Debug`, `Release`, `Debug_Portable`, `Release_Portable`) build with zero
warnings. `Debug` writes to a separate `DEBUG` folder under `%LOCALAPPDATA%\Kronos`, and `Portable`
keeps all data inside the build output, so neither touches a real installation.

**x64 only, for the project as well as the output.** `<Platforms>` is `x64` and `RuntimeIdentifier`
is `win-x64`. Do not add a 32-bit configuration; see the warning at the top.

### Release artifacts

```powershell
winget install Microsoft.PowerShell    # the scripts call pwsh.exe (PowerShell 7)
winget install JRSoftware.InnoSetup    # only needed for the installer
```

From `package\`:

```powershell
.\build_all.cmd
```

Produces `Output\Kronos-<version>-portable.zip` and `Output\Kronos-<version>-installer.exe`, both
unsigned. Note that `build_all.cmd` first runs `extras\update_manifest.cmd`, which re-downloads
`src\Assets\static_manifest.json` from upstream and modifies a tracked file — check `git status`
afterwards. To skip it, call `build_Portable.cmd`, `package_Portable.cmd`, `build_Installer.cmd` and
`package_Installer.cmd` individually.

**Publishing a release.** The in-app update check reads the release *title*, which must begin with
`v` followed by the version (`v1.49`). It then looks for exactly one asset whose name ends with
`-installer.exe`; two matches disable updating rather than guessing. Tag the commit, create the
release with the installer attached, and the update prompt appears in installed copies.

## Branding and assets

`src\Assets\icon.ico` is the one that matters — it is `<ApplicationIcon>` in the csproj and
`SetupIconFile` in `package\Installer.iss`, so it sets both the executable and installer icons. The
`*Logo*.png` and `*Tile*.png` files are upstream's MSIX tile set and are unused by this unpackaged
build; `icon_256.png` is only for MSIX, which is not used.

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
[What changed](#what-changed). `DLSS`, `FSR`, `FidelityFX` and `XeSS` are trademarks of their
respective owners; this project is not affiliated with NVIDIA.
