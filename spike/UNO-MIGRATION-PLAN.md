# Uno Platform migration plan

How to move Kronos off Windows App SDK / WinUI 3 onto Uno Platform, in an order where the Windows app
keeps shipping throughout.

Follows [SPIKE-FINDINGS.md](SPIKE-FINDINGS.md), which established that the XAML dialect carries over
unchanged. This document covers what that spike did **not** test, and what has to happen next.

---

## What the spike proved, and what it didn't

**Proved:** every XAML construct Kronos uses compiles under Uno, including the
`CommunityToolkit.WinUI.Converters` declared in every page's resources, `x:Bind` two levels deep
through the generated `TranslationProperties`, and the WinUI system brushes and styles behind
`ThemeResource` and `StaticResource`. A Linux ELF publishes from a Windows machine.

**Not tested — and this is the actual migration cost:**

> The spike was a XAML dialect probe. It contained **no window, no dialog, and no image**. Those are
> where the C# work is, and where the uncertainty is.

---

## The real work, by size

| Area | Size | Risk |
|---|---|---|
| **Windowing** — `AppWindow`, `Microsoft.UI.Windowing` | 42 refs, 4 files | 🔴 **Highest.** Uno has its own windowing model. |
| **XAML files** | 28 files, ~2,980 lines | 🟢 Proven by the spike |
| **WinUI-only packages** | 4 packages | 🟡 Bounded substitutions |
| **Build/release scripts** | 3 scripts | 🟡 Mechanical, well understood |
| **`#if WINDOWS` in the data layer** | 15 files, 36 blocks | 🟢 Already handled for Linux |

### 1. Windowing — do this first, as a probe

The concentration is the whole story:

| File | `AppWindow` refs |
|---|---|
| `WindowManager.cs` | 24 |
| `MainWindow.xaml.cs` | 12 |
| `WindowPositionRect.cs` | 5 |
| `TranslationToolboxWindow.xaml.cs` | 1 |

`WindowManager` is the main class (243 lines) and is worth reading in full before anything else — it
owns activation, sizing, position restore and the custom title bar. If Uno's windowing model cannot
express it, that is a design problem, not a porting problem, and it should be discovered in week one
rather than month three.

**Probe:** port `WindowManager` and `WindowPositionRect` to Uno in an isolated branch, behind the same
interface. Nothing else moves until that works.

### 2. Packages that must be replaced

| Package | Used for | Replacement |
|---|---|---|
| `Microsoft.WindowsAppSDK` | the framework | **`Uno.Sdk`** — the whole point |
| `Microsoft.Windows.SDK.BuildTools` | build-time | dropped with WinAppSDK |
| `Microsoft.Web.WebView2` | **nothing** — zero call sites | dropped; Uno has no such requirement |
| `CommunityToolkit.Labs.WinUI.Controls.MarkdownTextBlock` | update notes (1 use) | Markdown view or plain text |
| `WinUI.TableView` / `CommunityToolkit.WinUI.UI.Controls.DataGrid` | DLL grids | `Uno.Toolkit`, or plain `ListView` |

Kept: `CommunityToolkit.Mvvm`, `CommunityToolkit.WinUI.Converters` (**verified working under Uno**),
`SixLabors.ImageSharp`, `sqlite-net-e`, `Serilog`, `ValveKeyValue`, `ByteSize`, `CsvHelper`,
`FuzzySharp`, `HtmlAgilityPack`, `Nito.AsyncEx`, `YamlDotNet`, `Microsoft.Windows.CsWin32` (MIT,
build-time).

> **Do not** drop `CommunityToolkit.WinUI.Converters` in the name of leaving the WinUI ecosystem. The
> spike proved it works, and it is MIT. Uno's replacements would mean editing a converter
> declaration on every page for no licence benefit.

### 3. Build and release scripts

`build.ps1` hardcodes `net10.0-windows10.0.26100.0`, `win-x64`, and the `Release_Portable`
configuration.

**`Release_Portable` exists only because of WinAppSDK.** `build.ps1` documents it plainly:

```
#   -c Release                     + WindowsAppSDKSelfContained    CRASHES
#   -c Release_Portable            plain publish                    runs
```

Uno has no equivalent constraint. The configuration, the crash, and the reasoning all go away, and
`build.ps1` gets simpler rather than more complex.

The README's **"Needs the Windows App Runtime. If `Kronos.exe` exits immediately without showing a
window, install it"** instruction disappears — which removes the single most common support question
the release notes carry.

---

## Phasing

Each phase ends with something runnable. `main` ships WinUI until phase 5 says otherwise.

| Phase | Work | Done when |
|---|---|---|
| **0** | **Windowing probe.** Port `WindowManager` + `WindowPositionRect` to Uno behind the existing interface. | A window opens, moves, restores position and shows its title bar |
| **1** | **csproj swap.** `Uno.Sdk`, single `net10.0-desktop` TFM, delete `Release_Portable`. | The 28 XAML files compile unchanged |
| **2** | **Package substitutions.** MarkdownTextBlock, TableView/DataGrid, WebView2. | Nothing references a Windows-only package |
| **3** | **Dialogs and images.** `EasyContentDialog`, `ContentDialog` behaviour, cover loading. | A swap works end to end |
| **4** | **Scripts and release.** `build.ps1`, `publish-release.ps1`, README, release notes. | A portable zip publishes for Windows with no App Runtime |
| **5** | **Cut over.** `main` moves to Uno; WinUI archived in git. | — |

**Phases 0–2 are the risk. 3–4 are work.** If phase 0 fails, the migration is a design conversation,
and it costs one week to find out rather than the whole plan.

---

## What this does *not* do

**It does not make the Linux app work.**

Uno migrates the UI — roughly 8% of `src/`. It does nothing for the 88 registry references, the 53
P/Invoke sites, or the eight store-discovery libraries. The Linux CLI still registers only `update`,
`version` and `self-update`, and says itself that the rest needs porting.

After phases 0–5 there would be a Linux GUI attached to a product that cannot swap a DLL. **That is
the next project**, and it is not a UI task.

Two independent reasons the migration is still worth doing:

1. **Windows** — it removes the Windows App Runtime dependency, the App Runtime install problem, and
   the `Release_Portable` workaround.
2. **Linux** — it is the only realistic path to a Linux GUI, once the data layer exists.

---

## Risks

| Risk | Mitigation |
|---|---|
| Uno's windowing can't express `WindowManager` | Phase 0 exists solely to find this out early |
| `NavigationView` behaviour differs (10 uses) | Spotted in phase 1; the shell is small and rebuildable |
| Uno's `AppBarButton` (70 uses) has gaps | Highest-count XAML construct after `ToolTipService`; phase 1 |
| Cover images / `BitmapImage` on Linux | Phase 3; Skia-backed, likely fine, untested |
| Runtime differences the spike couldn't reach — rendering, input, DPI | **The spike proved compilation, not behaviour.** Needs a real Linux desktop |
| Uno release cadence vs. this project's stability bar | Pin an exact `Uno.Sdk` version; don't float |

> **The rendering gap is the one real unknown left.** The spike built a Linux binary and started it,
> but no window appeared — the test environment had no Wayland compositor. Everything above is
> verified as far as compilation; behaviour on a real Linux desktop is unproven and should be
> confirmed before phase 1 starts.

---

## Explicitly out of scope

- Store discovery on Linux (the real blocker)
- The CLI's `list` / `swap` / `reset`
- Replacing `CommunityToolkit.WinUI.*` for tidiness
- The Windows 10 19045 floor — a deliberate product decision, unrelated to the framework
- Signature verification (`WinTrust`), which stays Windows-only
