# Contributing

This repository is an **unofficial fork** of
[DLSS Swapper](https://github.com/beeradmoore/dlss-swapper). See the README for what changed and
for the licensing/GPL-3.0 section 5(a) modification notice.

The changes in this fork were written with AI assistance. Please read
[AI_ASSISTED.md](AI_ASSISTED.md) before contributing — it records what was and was not
AI-generated, and which of the fixes have and have not been verified.

## Where to file things

| Topic | Where |
| --- | --- |
| Bug in the **official** app | [upstream issue tracker](https://github.com/beeradmoore/dlss-swapper/issues) |
| Feature request for the **official** app | [upstream issue tracker](https://github.com/beeradmoore/dlss-swapper/issues) |
| Bug in **this fork's** local changes | Here |
| Malicious site impersonating DLSS Swapper | [upstream](https://github.com/beeradmoore/dlss-swapper/issues/new?template=other_issue.yml) |

## Building and testing

```powershell
dotnet build ".\Unofficial DLSS Swapper.sln" -c Debug
dotnet test ".\tests\DLSS_Swapper.Tests\DLSS_Swapper.Tests.csproj" -c Debug
```

All four configurations (`Debug`, `Release`, `Debug_Portable`, `Release_Portable`) are expected to
build with zero warnings. Please include test results in any pull request that changes logic.

## Adding a new DLL/upscaler type

All per-type logic is driven by a single table in
[`src/Data/DLLAssetTypeInfo.cs`](src/Data/DLLAssetTypeInfo.cs). Adding a type should mean adding one
entry there plus its manifest and resource strings, **not** editing the `// NOTE: DLL type` chains
that used to be spread through `DLLManager`, `Game` and `LibraryPageModel`. If you find yourself
adding another if/else over `GameAssetType`, that is a signal the registry needs extending instead.

## Licensing

This project is licensed under the [GNU General Public License v3.0](LICENSE).

By contributing you agree that your contribution is licensed under the same GPL-3.0 terms. Do not
add code under a different license, and do not add dependencies whose licenses are incompatible
with GPL-3.0.

## Trademark note

`DLSS`, `FSR`, `FidelityFX` and `XeSS` are trademarks of NVIDIA. This project is not affiliated with
or endorsed by NVIDIA. Please do not add official-looking branding to this fork, and do not
redistribute NVIDIA's DLLs from here.
