# Contributing

This repository is **Chronos**, a private, personal modification of
[DLSS Swapper](https://github.com/beeradmoore/dlss-swapper) by
[beeradmoore](https://github.com/beeradmoore). See the README for what changed and for the
licensing/GPL-3.0 section 5(a) modification notice.

The changes here were written with AI assistance. Please read
[AI_ASSISTED.md](AI_ASSISTED.md) — it records what was and was not AI-generated, and which of the
fixes have and have not been verified.

**This project is not accepting contributions.** It is private and has no users beyond its author.
The guidance below is kept because it documents how the code is meant to be worked on, and because
several of the fixes are worth sending upstream.

## Where to file things

| Topic | Where |
| --- | --- |
| Bug in the **official** app | [upstream issue tracker](https://github.com/beeradmoore/dlss-swapper/issues) |
| Feature request for the **official** app | [upstream issue tracker](https://github.com/beeradmoore/dlss-swapper/issues) |
| Bug in Chronos' local changes | Nowhere public — fix it here |
| Malicious site impersonating DLSS Swapper | [upstream](https://github.com/beeradmoore/dlss-swapper/issues/new?template=other_issue.yml) |

The bug fixes made here were found by auditing upstream and are mostly independent of the Chronos
rename, so they are worth offering upstream. The repack detection and store-linked cover art are
Chronos-specific features and are not.

## Building and testing

```powershell
dotnet build ".\Chronos.sln" -c Debug
dotnet test ".\tests\Chronos.Tests\Chronos.Tests.csproj" -c Debug
```

All four configurations (`Debug`, `Release`, `Debug_Portable`, `Release_Portable`) are expected to
build with zero warnings. Please include test results in any change that alters logic.

Note that the `Debug` configurations write to `%LOCALAPPDATA%\Chronos\DEBUG` and the `Portable`
configurations keep everything inside the build output, so neither touches a real installation.

## Adding a new DLL/upscaler type

All per-type logic is driven by a single table in
[`src/Data/DLLAssetTypeInfo.cs`](src/Data/DLLAssetTypeInfo.cs). Adding a type should mean adding one
entry there plus its manifest and resource strings, **not** editing the `// NOTE: DLL type` chains
that used to be spread through `DLLManager`, `Game` and `LibraryPageModel`. If you find yourself
adding another if/else over `GameAssetType`, that is a signal the registry needs extending instead.

## A warning about resource names

Embedded resource names are derived from `RootNamespace`, and three places look resources up by a
hardcoded string: the static DLL manifest in `App.xaml.cs` and `DLLManager.cs`, and
`AcknowledgementsPageModel.AcknowledgementsPrefix`. **If `RootNamespace` and those strings disagree,
nothing fails to compile.** The app starts, cannot find its manifest, and closes itself with a
"could not load manifest" dialog. Keep `RootNamespace` and `AssemblyName` in step, and update all
three strings in the same change.

## Licensing

This project is licensed under the [GNU General Public License v3.0](LICENSE). It is a derivative
work of DLSS Swapper and **cannot be relicensed**. Any contribution is licensed under the same
GPL-3.0 terms. Do not add code under a different license, and do not add dependencies whose licenses
are incompatible with GPL-3.0.

## Trademark note

`DLSS`, `FSR`, `FidelityFX` and `XeSS` are trademarks of NVIDIA. This project is not affiliated with
or endorsed by NVIDIA, nor with the original DLSS Swapper maintainer. Please do not add
official-looking branding, and do not redistribute NVIDIA's DLLs from here. The application icon is
still upstream's original — see the README's branding section if you want to replace it.
