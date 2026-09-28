<p align="center">
 <h2 align="center">Chronos
</h2>
 <p align="center">A tool that lets you conveniently download, manage, and swap <strong>DLSS</strong>, <strong>FSR</strong> and <strong>XeSS</strong> dlls, letting you upgrade or downgrade the DLSS, FSR and XeSS version in a game without the game needing an update.</p>
</p>

> [!IMPORTANT]
> **Chronos is based on [DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
> [beeradmoore](https://github.com/beeradmoore), which is licensed under the
> [GNU GPL v3.0](LICENSE).** Chronos is a private, personal modification of that project. It is not
> affiliated with, endorsed by, or associated with the original DLSS Swapper maintainer.
>
> All original copyright notices are preserved, and the original author is credited in
> [LICENSE](LICENSE), in the installer's file properties, and in the Credits section below. The
> changes made here are listed in [What changed](#what-changed-in-chronos) and were written with AI
> assistance, disclosed in [AI_ASSISTED.md](AI_ASSISTED.md).
>
> If you want the official DLSS Swapper, including signed binaries, use
> **[github.com/beeradmoore/dlss-swapper](https://github.com/beeradmoore/dlss-swapper)** rather than
> this. The upstream project warns that malicious sites impersonate DLSS Swapper; the distinct name
> here, and the publisher string in the installer, exist to make that confusion unlikely.

> [!NOTE]
> **No prebuilt binaries are published from this repository.** There is no releases page here, and
> Chronos is not on winget. If you want a ready-to-run DLSS Swapper, use the official project linked
> above. This repository is source only.

> [!NOTE]
> **This project was developed with AI assistance.** The original upstream code is not AI-generated,
> but the changes listed below were written with the help of an AI coding assistant (Claude, via
> OpenCode), under my direction. See [AI_ASSISTED.md](AI_ASSISTED.md) for exactly what was and was
> not involved, and for the things I checked by hand rather than taking on trust.

## What changed in Chronos

Chronos is a private, personal modification of upstream DLSS Swapper. The significant changes
relative to upstream are:

**Renamed to avoid impersonation**

- The product, assembly and output files are named `Chronos` (`Chronos.exe`). The installer uses its
  own uninstall registry key and Start Menu entry, so it does not collide with an existing official
  installation.
- The C# `RootNamespace` was changed to `Chronos` to match `AssemblyName`, which renamed every
  `namespace` and `using` in the codebase. It is kept in step with `AssemblyName` deliberately: the
  embedded resource names derive from the root namespace, and both the static DLL manifest and the
  acknowledgements page look resources up by name, so letting the two drift breaks startup.
- **The on-disk data folder moved** to `%LOCALAPPDATA%\Chronos`, and the database is now
  `chronos.db`. This is a behaviour change, and it is intentional: Chronos is a separate program and
  should not read or write another product's data. **A Chronos build therefore starts with an empty
  database** rather than inheriting an existing DLSS Swapper install's settings, game history and
  downloaded DLLs. The uninstaller removes the `Chronos` folder only.
- `package/config.cmd` and the release workflow now produce
  `Chronos-<version>-{portable.zip,installer.exe}`.

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
`beeradmoore.github.io` endpoint, and still retrieves the DLLs themselves from NVIDIA. Chronos
depends on those remaining available, and deliberately does not repoint them.

**Builds of Chronos are unsigned.** Upstream signs its releases via SignPath; those credentials are
not available here, so expect a SmartScreen warning on first run.

## License

Chronos remains under the [GNU General Public License v3.0](LICENSE), the same license as upstream.
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

**For the official, signed build**, use the upstream project:

- [Upstream GitHub releases](https://github.com/beeradmoore/dlss-swapper/releases)
- `winget install --id=beeradmoore.dlss-swapper -e`

Those are the only official sources. Do not install a build labelled "DLSS Swapper" from any other
site, including this repository.

**For Chronos**, there are no published binaries. This repository is source only, so the only way
to get a build is to compile it yourself — see [Building](#building). Two things follow from that:

- Chronos is not on winget, and no installer or portable zip is attached to any release here. If you
  find one anywhere claiming to be from this project, it did not come from this repository.
- Any build you make is unsigned, so Windows SmartScreen will warn on first run. The official project
  signs its releases via SignPath, so expect a difference.

## It would be cool if DLSS Swapper could...

Create a [feature request upstream](https://github.com/beeradmoore/dlss-swapper/issues/new?template=feature_request.yml).

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a Windows SDK matching the
project's target platform (`10.0.26100.0`). The solution is
`Chronos.sln`.

```powershell
dotnet build ".\Chronos.sln" -c Debug
dotnet test ".\tests\Chronos.Tests\Chronos.Tests.csproj" -c Debug
```

All four configurations (`Debug`, `Release`, `Debug_Portable`, `Release_Portable`) are expected to
build with zero warnings, and the test suite is expected to pass.

The `Debug` configurations write their data to a separate `DEBUG` folder under `%LOCALAPPDATA%\DLSS
Swapper`, and the `Portable` configurations keep all data inside the build output, so neither
touches a real installation's settings or database.

### Building the release artifacts

The `package\` scripts need two extra tools that the CI workflow installs for itself:

```powershell
winget install Microsoft.PowerShell    # the scripts call pwsh.exe (PowerShell 7)
winget install NSIS.NSIS               # only needed to build the installer
```

Then, from the `package` directory:

```powershell
.\build_all.cmd
```

This produces `Output\Chronos-<version>-portable.zip` and `Output\Chronos-<version>-installer.exe`.

Two things to know before you use it:

- `build_all.cmd` starts by running `extras\update_manifest.cmd`, which re-downloads
  `src\Assets\static_manifest.json` from the upstream manifest builder. That modifies a tracked
  file, so check `git status` afterwards and decide whether you want to keep the refresh. To build
  without it, call `build_Portable.cmd`, `package_Portable.cmd`, `build_Installer.cmd` and
  `package_Installer.cmd` individually instead.
- Everything is **unsigned**, so Windows SmartScreen will warn when a user runs the installer.

## Branding and assets

The application icon is still upstream's original `src\Assets\icon.ico`, and the roughly sixty
`Assets\*Logo*.png` / `*Tile*.png` files are upstream's MSIX tile set. Chronos does not ship its own
artwork.

- `src\Assets\icon.ico` is the one that matters. It is used by `<ApplicationIcon>` in the csproj and
  by `MUI_ICON` in `package\NSIS\Installer.nsi`, so it sets the executable's icon and the installer's
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
| OS          | Windows 10 64-bit (20H1, build 19041)                           |
| GPU         | Any                                                             |
| To build    | .NET 10 SDK, Windows SDK 10.0.26100                             |
| To package  | PowerShell 7 (`pwsh`), and NSIS for the installer               |

## Upstream project

Chronos is a derivative of DLSS Swapper. The original project, its maintainer, and its official
releases live at:

- GitHub: https://github.com/beeradmoore/dlss-swapper/
- Twitter: https://twitter.com/dlss_swapper
- Reddit: https://www.reddit.com/r/DLSS_Swapper/

If you want an official, signed DLSS Swapper, use the upstream project rather than a build of this
one. Bug reports and feature requests for the official app belong upstream. This is a private,
personal project and provides no support.

### Credits

**Chronos would not exist without the original DLSS Swapper, by
[beeradmoore](https://github.com/beeradmoore) and contributors.** The original project is the
foundation of this one, and this project is released under the same licence with the original
copyright notices intact. Please support the upstream project rather than treating this as a
replacement for it.

Upstream DLSS Swapper receives free Windows code signing from
[SignPath.io](https://signpath.io/) via the
[SignPath Foundation](https://www.signpath.com/solutions/for-open-source-community-foundation).
That sponsorship applies to the official project. Chronos builds are unsigned and are not covered
by it.
