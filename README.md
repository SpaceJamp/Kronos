<p align="center">
 <img width="150px" src="https://beeradmoore.github.io/dlss-swapper/logo_250.png" align="center" alt="GitHub Readme Stats" />
 <h2 align="center">DLSS Swapper
</h2>
 <p align="center">DLSS Swapper is a tool that allows you to conveniently download, manage, and swap <strong>DLSS</strong>, <strong>FSR</strong> and <strong>XeSS</strong> dlls allowing you to upgrade or downgrade DLSS, FSR and XeSS version in a game without the game needing an update.</p>
</p>
> [!IMPORTANT]
> **This is an unofficial fork.** It is not affiliated with, endorsed by, or associated with the
> original DLSS Swapper maintainer or with NVIDIA. The official project, and the only place you
> should get official builds, is at
> **[github.com/beeradmoore/dlss-swapper](https://github.com/beeradmoore/dlss-swapper)**.
>
> If you want the official release, including signed binaries, use that link rather than this one.
>
> The upstream project also warns that malicious sites impersonate DLSS Swapper. This fork is
> deliberately labelled as unofficial for that reason.
What changed in this fork
This fork exists to carry local modifications. The significant changes relative to upstream are:
Correctness fixes
Game DLL records are no longer deleted from the database before the install folder is scanned.
Previously a single unreadable subdirectory (which throws during enumeration) silently wiped
everything the app knew about a game's DLLs and left the game showing no swappable items. The
scan now completes first, and the directory walk skips folders it cannot read.
Fixed a copy/paste bug that meant imported DLLs were never migrated out of the legacy pre-1.1.7
zip folders.
Fixed an inverted condition that meant orphaned imported DLL files were never deleted.
`App.RunOnUIThreadAsync` no longer silently discards the callback it was given. It previously
returned without running the function, which could leave a game's `Processing` flag stuck on and
the game permanently unopenable.
`Game.ProcessGame` no longer runs as `async void` on the thread pool, where an exception in its
`finally` block would terminate the process.
Deleting a game no longer walks the entire shared image cache, and no longer aborts partway
through when a subdirectory cannot be read.
`PathHelpers.NormalizePath` no longer turns a drive root (`C:\`) into the drive-relative path
`C:`, which made games installed at a drive root appear to be missing.
`Logger.ChangeLoggingLevel` no longer ignores its own argument.
A cached game title's base64 encoding now invalidates when the title changes, so renamed games
are no longer misreported as having unknown DLLs.
Cancellation and responsiveness
The DLL import progress dialog had no buttons at all, so a long import could not be stopped. It
and the NVIDIA driver/server import dialogs are now cancellable.
Cancelling the NVIDIA driver import previously still went on to import; the cancellation check
after the hashing phase was missing.
The "Refresh" and "Check for updates" busy flags are now cleared on failure instead of leaving
the buttons permanently disabled.
Concurrent failed downloads no longer race to show two dialogs at once, which threw
"There is already a ContentDialog open".
The import work no longer runs as an unobserved thread-pool work item, which could either crash
the app or leave a modal dialog stuck on screen forever.
Importing no longer re-hashes the same file once per candidate record.
Maintainability
All per-DLL-type logic is driven from a single table
(`src/Data/DLLAssetTypeInfo.cs`) instead of nine hand-maintained
if/else chains. Adding a DLL type is now one entry rather than edits in a dozen places.
Added an xUnit test project (`tests/`) covering the asset-type registry, the v1.1.7
migration, the zip-hash lookup, and path/Levenshtein helpers.
Not changed: the app still fetches the DLL manifest from the upstream project's public
`beeradmoore.github.io` endpoint, and still retrieves the DLLs themselves from NVIDIA. This fork
depends on those remaining available.
Builds from this fork are unsigned. Upstream signs its releases via SignPath; those credentials
are not available here, so expect a SmartScreen warning on first run.
License
This fork remains under the GNU General Public License v3.0, the same license as
upstream. Per GPL-3.0 section 5(a), the modifications above are stated here. The original copyright
notices are preserved in `LICENSE`.
`DLSS`, `FSR`, `FidelityFX` and `XeSS` are trademarks of their respective owners. This project is
not affiliated with NVIDIA.
<!-- The badges and links below point at the UPSTREAM project on purpose, so visitors can find the
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
<p align="center">
    <img src="https://beeradmoore.github.io/dlss-swapper/images/usage/usage_4.gif" />
</p>
What game libraries are supported?
Steam
GOG
Epic Games
Ubisoft Connect
Xbox App
Battle.net
Manually added via the `Add Game` button.
Why would you want to change the DLSS dlls in your game?
See this clip, or better yet just watch the entire video (Lego Builder's Journey Ray Tracing Showcase + DLSS 2.2 Upgrades Analysis) from Digital Foundry. DLSS 2.2 discussions start at 11:40.
Please note
This tool does NOT allow you to add DLSS to games that don't support it.
This tool does NOT guarantee that swapping DLSS dlls will:
Improve DLSS performance.
Reduce DLSS artifacts.
Give a crash free experience.
In many cases you may fix some issues, in other cases you may prevent a game from launching (until you restore your original dll, provided in the tool).
Happy experimenting. As my university professor once said,
> The good thing about computer [science] is we will never die wondering 'What if...?'
Please, come and share your DLSS experience over in r/DLSS_Swapper.
How do I get it?
For the official, signed build, use the upstream project:
Upstream GitHub releases
`winget install --id=beeradmoore.dlss-swapper -e`
Those are the only official sources. Do not install a build labelled "DLSS Swapper" from any other
site, including this repository.
For this fork, build it yourself from source (see Building) or download a release
from this repository's releases page. Forks are not published to winget, and fork builds are
unsigned, so Windows SmartScreen will warn on first run.
It would be cool if DLSS Swapper could...
Create a feature request upstream.
Building
Requires the .NET 10 SDK and a Windows SDK matching the
project's target platform (`10.0.26100.0`).
```powershell
dotnet build ".\DLSS Swapper.sln" -c Debug
dotnet test ".\tests\DLSS_Swapper.Tests\DLSS_Swapper.Tests.csproj" -c Debug
```
There are four configurations: `Debug`, `Release`, `Debug_Portable` and `Release_Portable`. The
`Debug` configurations write their data to a separate `DEBUG` folder under `%LOCALAPPDATA%\DLSS Swapper`, and the `Portable` configurations keep all data inside the build output, so neither
touches a real installation's settings or database.
How can I contribute?
Bug reports and feature requests for the official app belong
upstream. Pull requests against this fork are
welcome if they are generally useful, but note the maintainers of the original project are the
ones who decide what lands in the official app.
Minimum System Requirements
Requirement	Description
OS	Windows 10 64-bit (20H1, build 19041)
GPU	Any
Upstream project
This is a fork. The original project, its maintainer, and its official releases live at:
GitHub: https://github.com/beeradmoore/dlss-swapper/
Twitter: https://twitter.com/dlss_swapper
Reddit: https://www.reddit.com/r/DLSS_Swapper/
Please report impersonation or malicious sites to the
upstream issue tracker,
not here. Bug reports and feature requests for the official app also belong upstream — this fork
does not provide support.
Credits
Upstream DLSS Swapper is by beeradmoore and contributors, and
receives free Windows code signing from
SignPath.io via the
SignPath Foundation.
That sponsorship applies to the official project, not to this fork, whose builds are unsigned.
