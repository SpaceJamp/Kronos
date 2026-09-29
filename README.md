<p align="center">
 <h2 align="center">Kronos
</h2>
 <p align="center">A tool that lets you conveniently download, manage, and swap <strong>DLSS</strong>, <strong>FSR</strong> and <strong>XeSS</strong> dlls, letting you upgrade or downgrade the DLSS, FSR and XeSS version in a game without the game needing an update.</p>
</p>

> [!WARNING]
> ## 32-bit CPUs are NOT supported
>
> **Kronos requires a 64-bit processor. There is no 32-bit build, and there will not be one.**
>
> * **There is no 32-bit version to download.** If you are looking for one, it does not exist and
>   never has.
> * **A 32-bit processor cannot run it, ever.** The application, the .NET runtime, the SQLite
>   native library and the CoreCLR it ships with are all compiled for the `x86_64` instruction set.
>   A 32-bit CPU cannot execute those instructions at all, so 64-bit Windows cannot be installed on
>   that hardware in the first place. This is a hardware limit, not a setting.
> * **64-bit Windows is also required.** A 32-bit *operating system* cannot load a 64-bit program, so
>   Kronos will not run on 32-bit Windows either, even when the CPU underneath is perfectly capable.
>   If your CPU is 64-bit but your Windows is 32-bit, installing 64-bit Windows will fix it. Note that
>   Windows cannot be upgraded in place from 32-bit to 64-bit, it needs a clean install.
> * **You will not get a useful error message.** Windows reports an application that cannot run on
>   your PC, which never mentions processor architecture. If you see that, check the two fields below
>   before anything else.
> * **If it is somehow launched anyway**, Kronos detects a 32-bit operating system and shows a window
>   explaining the situation rather than failing obscurely.
>
> **Why:** Kronos exists to swap 64-bit graphics driver DLLs, namely `nvngx_dlss.dll`,
> `nvngx_dlssg.dll`, `nvngx_dlss_d.dll` and `amd_fidelityfx_dx12.dll`, into game installations.
> Games on Windows are 64-bit, and these DLLs exist only as 64-bit binaries, so there is nothing
> Kronos could usefully do on 32-bit hardware.
>
> **How to check.** Press <kbd>Win</kbd>+<kbd>R</kbd>, type `msinfo32`, and read two separate fields,
> because they answer different questions and are commonly confused:
>
> | Field | Value | Meaning |
> | --- | --- | --- |
> | **System Type** | `x64-based PC` | 64-bit Windows. Kronos can run. |
> | **System Type** | `x86-based PC` | 32-bit **Windows**, so Kronos will not run. Check *Processor* next. |
> | **Processor > Architecture** | `x64` or `ARM64` | 64-bit **CPU**. Supported. |
> | **Processor > Architecture** | `x86` | 32-bit **CPU**. Not supported, and no version of Windows can change that. |
>
> `System Type` describes your operating system, not your processor. An `x86-based PC` there does not
> necessarily mean a 32-bit CPU, which is exactly why `Processor > Architecture` is the field that
> actually settles whether this machine could ever run Kronos.

> [!IMPORTANT]
> **Kronos is based on [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
> [beeradmoore](https://github.com/beeradmoore), which is licensed under the
> [GNU GPL v3.0](LICENSE).** Kronos is a private, personal modification of that project. It is not
> affiliated with, endorsed by, or associated with the original DLSS Swapper maintainer and NVIDIA.
>
> All original copyright notices are preserved, and the original author is credited in
> [LICENSE](LICENSE), in the installer's file properties, and in the Credits section below. The
> changes made here are listed in [What changed](#what-changed-in-kronos) and were written with AI
> assistance, disclosed in [AI_ASSISTED.md](AI_ASSISTED.md).
>
> If you want the official DLSS Swapper, including signed binaries, use
> **[github.com/beeradmoore/dlss-swapper](https://github.com/beeradmoore/dlss-swapper)** rather than
> this. The upstream project warns that malicious sites impersonate DLSS Swapper; the distinct name
> here, and the publisher string in the installer, exist to make that confusion unlikely.

> [!NOTE]
> **This project was developed with AI assistance.** The original upstream code is not AI-generated,
> but the changes listed below were written with the help of an AI coding assistant (Claude, via
> OpenCode), under my direction. See [AI_ASSISTED.md](AI_ASSISTED.md) for exactly what was and was
> not involved, and for the things I checked by hand rather than taking on trust.

## What changed in Kronos

Kronos is a private, personal modification of upstream DLSS Swapper. The significant changes
relative to upstream are:

**Renamed to avoid impersonation**

- The product, assembly and output files are named `Kronos` (`Kronos.exe`). The installer uses its
  own uninstall registry key and Start Menu entry, so it does not collide with an existing official
  installation.
- The C# `RootNamespace` was changed to `Kronos` to match `AssemblyName`, which renamed every
  `namespace` and `using` in the codebase. It is kept in step with `AssemblyName` deliberately: the
  embedded resource names derive from the root namespace, and both the static DLL manifest and the
  acknowledgements page look resources up by name, so letting the two drift breaks startup.
- **The on-disk data folder moved** to `%LOCALAPPDATA%\Kronos`, and the database is now
  `kronos.db`. This is a behaviour change, and it is intentional: Kronos is a separate program and
  should not read or write another product's data. **A Kronos build therefore starts with an empty
  database** rather than inheriting an existing DLSS Swapper install's settings, game history and
  downloaded DLLs. The uninstaller removes the `Kronos` folder only.
- `package/config.cmd` and the release workflow now produce
  `Kronos-<version>-{portable.zip,installer.exe}`.

**Installer rewritten in Inno Setup**

The installer is now `package\Installer.iss` (Inno Setup 6) instead of an NSIS script. The payload is
unchanged; the same `dotnet publish` output is installed.

- **It offers the choice that Windows applications normally offer.** Run it and it asks whether to
  install for everyone or just for you:

  | Choice | Installs to | Registered in | Administrator rights |
  |---|---|---|---|
  | Just for you *(default)* | `%LOCALAPPDATA%\Programs\Kronos` | `HKCU` | No, so no UAC prompt |
  | For all users | `Program Files\Kronos` | `HKLM` | Yes, and a UAC prompt |

  The installer's folder, registry hive and Start Menu entry all follow that answer. Upstream's NSIS
  script could not express this: the components page it needed ran before the user had chosen
  anything, so the "for all users" option silently behaved like "just for you".
- The generated 757-line `FileList.nsh` and the script that produced it are gone. One
  `Source: ... recursesubdirs` entry covers the payload, so a build from a clean clone no longer
  depends on an uncommitted file existing.
- The uninstaller is Inno's own, rather than a replay of an `uninstall.log` written during install.
  That log-based uninstaller left behind anything the app created afterwards and stopped short if
  the app had already deleted a listed file.
- The install no longer refuses to run while Kronos is open. It offers to close it and carries on.
- `%LOCALAPPDATA%\Kronos` is removed on uninstall only for a per-user install. It is per-user data
  even for a machine-wide install, so deleting it during a machine-wide uninstall would throw away
  one user's games and settings while every other user kept theirs.
- `src\App.xaml.cs` now finds its own uninstall entry in `HKLM` as well as `HKCU`. A machine-wide
  install is registered under `HKLM`, and the old HKCU-only lookup found nothing, so the "Estimated
  size" shown in Apps & features silently stopped updating for anyone who installed for all users.

**Swapping down is now warned about**

- Kronos now says something before swapping in an **older DLSS runtime than the game is already
  using**. Frame Generation and some other DLSS dependent features can stop working when the runtime
  is older than the version the game shipped with, usually showing up as stutter or no frame
  generation at all. Previously the swap was completely silent: `UpdateDllAsync` checked that the
  file existed, that its hash matched and that it was signed, but never compared versions, so
  choosing an old entry from a long list broke the game with nothing on screen to explain it. The
  warning asks rather than blocks, because downgrading deliberately is a valid reason to be in that
  dialog, and the same dialog already warns about repacks. A version that cannot be read produces no
  warning rather than a guess.
- No database change was needed for this. The baseline is the game's own backup record, which already
  stored the version on disk before the first overwrite, so it is the runtime the game shipped with.

**Swaps can be undone more than once**

- DLL backups are now a numbered chain, `nvngx_dlss.dll.kronosbak1`, `.kronosbak2` and so on, rather
  than a single `.dlsss`. The single backup was **moved** back over the file when you reset, which
  consumed it, and that had two consequences: a second reset had nothing to restore, and the next
  swap after a reset saved the swapped file as the new "original", so the runtime the game shipped
  with was gone for good. Backups are now copied rather than moved and the chain is kept intact, so
  you can step back through as many swaps as you made.
- A legacy `.dlsss` from an earlier version is adopted as the bottom of the chain rather than
  discarded, so an existing install is not backed up a second time. Games whose DLL changed
  externally, for example by a game update, have the whole chain cleared, since every entry then
  describes a state the game can no longer run in.

**Correctness fixes**

- Game DLL records are no longer deleted from the database *before* the install folder is scanned.
  Previously a single unreadable subdirectory (which throws during enumeration) silently wiped
  everything the app knew about a game's DLLs and left the game showing no swappable items. The
  scan now completes first, and the directory walk skips folders it cannot read.
- Fixed a copy/paste bug that meant imported DLLs were never migrated out of the legacy pre-1.1.7
  zip folders.
- Fixed an inverted condition that meant orphaned imported DLL files were never deleted.
- `App.RunOnUIThreadAsync` no longer silently discards the callback it was given. It previously
  returned without running the function, which could leave a game's `Processing` flag stuck on and
  the game permanently unopenable.
- `Game.ProcessGame` no longer runs as `async void` on the thread pool, where an exception in its
  `finally` block would terminate the process.
- Deleting a game no longer walks the entire shared image cache, and no longer aborts partway
  through when a subdirectory cannot be read.
- `PathHelpers.NormalizePath` no longer turns a drive root (`C:\`) into the drive-relative path
  `C:`, which made games installed at a drive root appear to be missing.
- `Logger.ChangeLoggingLevel` no longer ignores its own argument.
- A cached game title's base64 encoding now invalidates when the title changes, so renamed games
  are no longer misreported as having unknown DLLs.
- Backups are now created per-DLL rather than all-or-nothing. Previously, if a game had several
  copies of the same DLL and *any* one of them had a backup, none of the others were backed up
  before being overwritten, and the original was unrecoverable. A backup file that existed on disk
  but had no database record was also left orphaned, unusable for reset.

**Cancellation and responsiveness**

- The DLL import progress dialog had no buttons at all, so a long import could not be stopped. It
  and the NVIDIA driver/server import dialogs are now cancellable.
- Cancelling the NVIDIA driver import previously still went on to import; the cancellation check
  after the hashing phase was missing.
- The "Refresh" and "Check for updates" busy flags are now cleared on failure instead of leaving
  the buttons permanently disabled.
- Concurrent failed downloads no longer race to show two dialogs at once, which threw
  "There is already a ContentDialog open".
- The import work no longer runs as an unobserved thread-pool work item, which could either crash
  the app or leave a modal dialog stuck on screen forever.
- Importing no longer re-hashes the same file once per candidate record.
- Toggling a DLSS setting (on-screen indicator, logging level, console logging) no longer freezes
  the window. Writing the NGXCore registry key shells out to an elevated `reg.exe` and blocked on
  `WaitForExit()`, including waiting on a UAC prompt, on the UI thread.
- `WinTrust.VerifyEmbeddedSignature` no longer leaks unmanaged memory. It allocated two `CoTaskMem`
  blocks per call and its cleanup was commented out, so every DLL swap and every DLL import leaked.
  It also passed `WINTRUST_DATA` by value, which discarded the verification-state handle the native
  call writes, so that state was never released; and it declared `SetLastError = false`, which made
  the `GetLastWin32Error()` call in the "not signed" branch return a stale value and log the wrong
  message.

**Maintainability**

- All per-DLL-type logic is driven from a single table
  ([`src/Data/DLLAssetTypeInfo.cs`](src/Data/DLLAssetTypeInfo.cs)) instead of nine hand-maintained
  if/else chains. Adding a DLL type is now one entry rather than edits in a dozen places.
- Added an xUnit test project ([`tests/`](tests/)) covering the asset-type registry, the v1.1.7
  migration, the zip-hash lookup, and path/Levenshtein helpers.

**Not changed:** the app still fetches the DLL manifest from the upstream project's public
`beeradmoore.github.io` endpoint, and still retrieves the DLLs themselves from NVIDIA. Kronos
depends on those remaining available, and deliberately does not repoint them.

**Builds of Kronos are unsigned.** Upstream signs its releases via SignPath; those credentials are
not available here, so expect a SmartScreen warning on first run.

## License

Kronos remains under the [GNU General Public License v3.0](LICENSE), the same license as upstream.
It is a derivative work and cannot be relicensed. Per GPL-3.0 section 5(a), the modifications above
are stated here. The original copyright notices are preserved in `LICENSE`, and beeradmoore is
credited in the installer's file properties and in [Credits](#credits) below.

`DLSS`, `FSR`, `FidelityFX` and `XeSS` are trademarks of their respective owners. This project is
not affiliated with NVIDIA.

<!-- The badges and links below point at the UPSTREAM project on purpose, so visitors can find the
     official releases. Do not repoint these at a fork. -->

<p align="center">
    <a href="https://github.com/beeradmoore/dlss-swapper/releases"><img alt="Upstream Release" src="https://img.shields.io/github/v/release/beeradmoore/dlss-swapper" /></a>
    <a href="https://github.com/beeradmoore/dlss-swapper/graphs/contributors"><img alt="Upstream Contributors" src="https://img.shields.io/github/contributors/beeradmoore/dlss-swapper" /></a>
    <a href="https://github.com/beeradmoore/dlss-swapper/issues"><img alt="Upstream Issues" src="https://img.shields.io/github/issues/beeradmoore/dlss-swapper?color=0088ff" /></a>
</p>

<p align="center">
    <a href="https://github.com/beeradmoore/dlss-swapper/releases">Official releases</a>
    ·
    <a href="https://github.com/beeradmoore/dlss-swapper/issues/new?template=bug_report.yml">Report a bug upstream</a>
    ·
    <a href="https://github.com/beeradmoore/dlss-swapper/issues/new?template=feature_request.yml">Request a feature upstream</a>
</p>

<p align="center">
    <a href="./readmes/readme_ca.md">Català</a>
    ·
    English
    ·
    <a href="./readmes/readme_es.md">Español</a>
    ·
    <a href="./readmes/readme_ja-JP.md">日本語</a>    
    ·
    <a href="./readmes/readme_pt-BR.md">Português BR</a>
    ·
    <a href="./readmes/readme_tr-TR.md">Türkçe</a>
    ·
    <a href="./readmes/readme_zh-Hans.md">简体中文</a>
    ·
    <a href="./readmes/readme_zh-TW.md">繁體中文</a>
</p>

## What game libraries are supported?

- [Steam](https://store.steampowered.com/)
- [GOG](https://www.gog.com/en/)
- [Epic Games](https://store.epicgames.com/)
- [Ubisoft Connect](https://www.ubisoft.com/)
- [Xbox App](https://www.xbox.com/)
- [Battle.net](https://shop.battle.net/)
- Manually added via the `Add Game` button.

## Why would you want to change the DLSS dlls in your game?

See [this](https://youtube.com/clip/UgzYyeox3s7jFJZAvYF4AaABCQ) clip, or better yet just watch the entire video ([Lego Builder's Journey Ray Tracing Showcase + DLSS 2.2 Upgrades Analysis](https://www.youtube.com/watch?v=dtbqJXb1UDw)) from Digital Foundry. DLSS 2.2 discussions start at 11:40.

## Please note

This tool does **NOT** allow you to add DLSS to games that don't support it.

This tool does **NOT** guarantee that swapping DLSS dlls will:

- Improve DLSS performance.
- Reduce DLSS artifacts.
- Give a crash free experience.

In many cases you may fix some issues, in other cases you may prevent a game from launching (until you restore your original dll, provided in the tool).

Happy experimenting. As my university professor once said,

> The good thing about computer [science] is we will never die wondering 'What if...?'

Please, come and share your DLSS experience over in [r/DLSS_Swapper](https://www.reddit.com/r/DLSS_Swapper/).

## How do I get it?

**Before anything else: Kronos needs a 64-bit processor and 64-bit Windows.** A 32-bit CPU cannot run
it under any circumstances. A 32-bit Windows will not run it either, though that one may be fixable by
installing 64-bit Windows. Check both **System Type** and **Processor > Architecture** in
<kbd>Win</kbd>+<kbd>R</kbd> `msinfo32`; the warning at the top of this file explains which is which.

**For the official, signed build**, use the upstream project:

- [Upstream GitHub releases](https://github.com/beeradmoore/dlss-swapper/releases)
- `winget install --id=beeradmoore.dlss-swapper -e`

Those are the only official sources. Do not install a build labelled "DLSS Swapper" from any other
site, including this repository.

**For Kronos**, there are no published binaries. This repository is source only, so the only way
to get a build is to compile it yourself — see [Building](#building). Two things follow from that:

- Kronos is not on winget, and no installer or portable zip is attached to any release here. If you
  find one anywhere claiming to be from this project, it did not come from this repository.
- Any build you make is unsigned, so Windows SmartScreen will warn on first run. The official project
  signs its releases via SignPath, so expect a difference.

## It would be cool if DLSS Swapper could...

Create a [feature request upstream](https://github.com/beeradmoore/dlss-swapper/issues/new?template=feature_request.yml).

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a Windows SDK matching the
project's target platform (`10.0.26100.0`). The solution is
`Kronos.sln`.

```powershell
dotnet build ".\Kronos.sln" -c Debug
dotnet test ".\tests\Kronos.Tests\Kronos.Tests.csproj" -c Debug
```

All four configurations (`Debug`, `Release`, `Debug_Portable`, `Release_Portable`) are expected to
build with zero warnings, and the test suite is expected to pass.

**x64 only, for the project as well as the output.** `<Platforms>` is `x64` and `RuntimeIdentifier`
is `win-x64`, so `dotnet build -p:Platform=x86` will not produce anything usable, and
`-r win-x86` is not a supported configuration. Do not add one; see the warning at the top of this
file for why.

The `Debug` configurations write their data to a separate `DEBUG` folder under `%LOCALAPPDATA%\Kronos`,
and the `Portable` configurations keep all data inside the build output, so neither touches a real
installation's settings or database.

### Building the release artifacts

The `package\` scripts need two extra tools that the CI workflow installs for itself:

```powershell
winget install Microsoft.PowerShell    # the scripts call pwsh.exe (PowerShell 7)
winget install JRSoftware.InnoSetup    # only needed to build the installer
```

Then, from the `package` directory:

```powershell
.\build_all.cmd
```

This produces `Output\Kronos-<version>-portable.zip` and `Output\Kronos-<version>-installer.exe`.

Two things to know before you use it:

- `build_all.cmd` starts by running `extras\update_manifest.cmd`, which re-downloads
  `src\Assets\static_manifest.json` from the upstream manifest builder. That modifies a tracked
  file, so check `git status` afterwards and decide whether you want to keep the refresh. To build
  without it, call `build_Portable.cmd`, `package_Portable.cmd`, `build_Installer.cmd` and
  `package_Installer.cmd` individually instead.
- Everything is **unsigned**, so Windows SmartScreen will warn when a user runs the installer.

## Branding and assets

The application icon is a K monogram generated by `extras\generate_icon.ps1`, and the roughly
sixty `Assets\*Logo*.png` / `*Tile*.png` files are still upstream's MSIX tile set. Kronos does not
ship new artwork beyond the icon.

- `src\Assets\icon.ico` is the one that matters. It is used by `<ApplicationIcon>` in the csproj and
  by `SetupIconFile` in `package\Installer.iss`, so it sets the executable's icon and the installer's
  icon. Replace this file to change both.
- `src\Assets\icon_256.png` is `<PackageIcon>` and is only used for MSIX packaging. This project sets
  `WindowsPackageType=None`, so it has no effect on the built exe or installer.
- The `*Logo*.png` and `*Tile*.png` files follow the MSIX asset-naming convention and are likewise
  unused by the unpackaged build. They are only relevant if you ever switch to MSIX packaging.

Note that `DLSS`, `FSR` and `XeSS` are NVIDIA trademarks. A fork icon should not imply NVIDIA
endorsement.

## How can I contribute?

This is a private, personal project and is not accepting contributions.

Bug reports and feature requests belong
[upstream](https://github.com/beeradmoore/dlss-swapper/issues), whose maintainers decide what lands
in the official app. If you have found a bug in the local changes listed above rather than in
upstream DLSS Swapper, note that the underlying fixes may be worth sending upstream — see
[CONTRIBUTING.md](CONTRIBUTING.md).

## Minimum System Requirements

| Requirement | Description                                                     |
| ----------- | --------------------------------------------------------------- |
| OS          | **64-bit only.** Windows 10 (20H1, build 19041) or newer, x64   |
| CPU         | **64-bit only** (x64 or ARM64). 32-bit CPUs cannot run it. |
| GPU         | Any                                                             |
| To build    | .NET 10 SDK, Windows SDK 10.0.26100                             |
| To package  | PowerShell 7 (`pwsh`), and Inno Setup 6 for the installer        |

**32-bit CPUs are not supported, and neither is 32-bit Windows.** There is no 32-bit build and
there will not be one. A 32-bit CPU is a hard hardware limit. A 32-bit Windows is a separate,
softer case: the CPU may well be 64-bit and capable, and installing 64-bit Windows would fix it.

Neither case is an oversight or a missing feature; x64 is the only sensible target, and it is
enforced rather than left to chance:

* The published `Kronos.exe` has the PE machine type `0x8664`, and so do the native and runtime
  files it ships with, `coreclr.dll`, `hostfxr.dll`, `e_sqlite3.dll` and `System.Private.CoreLib.dll`.
  A 32-bit Windows cannot load any of them.
* `<PlatformTarget>x64</PlatformTarget>` and `<Prefer32Bit>false</Prefer32Bit>` are set explicitly in
  [`src/Kronos.csproj`](src/Kronos.csproj), alongside the existing `win-x64` runtime identifier, so
  the architecture is declared in one place rather than inferred.
* `App.OnLaunched` checks `Environment.Is64BitOperatingSystem` before touching the disk, the
  database or the network, and shows a dedicated window explaining the situation.

The underlying reason is that the DLLs Kronos swaps are 64-bit only, so a 32-bit operating system
could not use the feature even if the application loaded.

## Upstream project

Kronos is a derivative of DLSS Swapper. The original project, its maintainer, and its official
releases live at:

- GitHub: https://github.com/beeradmoore/dlss-swapper/
- Twitter: https://twitter.com/dlss_swapper
- Reddit: https://www.reddit.com/r/DLSS_Swapper/

If you want an official, signed DLSS Swapper, use the upstream project rather than a build of this
one. Bug reports and feature requests for the official app belong upstream. This is a private,
personal project and provides no support.

### Credits

**Kronos would not exist without the original DLSS Swapper, by
[beeradmoore](https://github.com/beeradmoore) and contributors.** The original project is the
foundation of this one, and this project is released under the same licence with the original
copyright notices intact. Please support the upstream project rather than treating this as a
replacement for it.

Upstream DLSS Swapper receives free Windows code signing from
[SignPath.io](https://signpath.io/) via the
[SignPath Foundation](https://www.signpath.com/solutions/for-open-source-community-foundation).
That sponsorship applies to the official project. Kronos builds are unsigned and are not covered
by it.
