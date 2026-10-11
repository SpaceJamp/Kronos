# Uno spike — findings

**Status: the migration question is answered, and the answer is yes.** This document records what was
tested, what passed, what did not, and what remains unproven.

Run on `uno-spike`. Nothing here touches `main`, which continues to carry the shipping WinUI 3 app.

---

## The question

Uno Platform was chosen because it runs **WinUI XAML** on Linux, so Kronos's ~2,980 lines of XAML would
survive a port rather than being rewritten. That premise was untested. Avalonia had been tried before
and abandoned after 141 compile errors, so "the XAML mostly carries over" could not be assumed.

## Method

Not a full port. A single page exercising every construct Kronos actually uses, in the shape Kronos uses
it — the list was read out of `SettingsPage.xaml` and `GameGridPage.xaml` rather than invented.

- **Toolchain:** Uno.Sdk **6.7.30** (latest stable; 6.8 and 7.0 are previews), `Uno.Templates` 6.7.30
- **Target:** `net10.0-desktop`, Skia renderer, Fluent theme
- **Verification:** compiled for the Windows head and published for the Linux head, then the Linux
  binary was run under WSL2 (Ubuntu 26.04)

## Results

### Toolchain — passes

| | |
|---|---|
| Builds on Windows | yes, 0 errors |
| Publishes a Linux binary **from Windows** | yes |
| Published artifact | ELF (`7F-45-4C-46`), 147.8 MB self-contained |

Being able to cut a Linux build from this Windows machine, without a Linux CI runner, retires a whole
category of ongoing cost.

### The XAML dialect — passes

This was the decisive question, and the answer is better than expected. **Every construct Kronos uses
compiles under Uno unchanged.**

| Construct | Where Kronos uses it | Result |
|---|---|---|
| `CommunityToolkit.WinUI.Converters` — all four | every page's `<Page.Resources>` | ✅ |
| `x:Bind`, two levels deep through `TranslationProperties` | every string in the app | ✅ |
| `ObservableProperty` + `RelayCommand` | whole MVVM layer | ✅ |
| `AppBarButton` + `FontIcon` + command binding | games page toolbar | ✅ |
| `InfoBar` | games page | ✅ |
| `ComboBox`, `ToggleSwitch`, `CheckBox`, `RadioButton`, `Expander` | settings page | ✅ |
| `muxc:ProgressRing`, `muxc:ProgressBar` | games page | ✅ |
| `ListView` + `DataTemplate` + `x:DataType` | both list templates | ✅ |
| `ThemeResource` WinUI brushes (`CardBackgroundFillColorDefaultBrush`, `OverlayCornerRadius`, `SystemFillColorCriticalBrush`, …) | throughout | ✅ |
| `StaticResource` WinUI styles (`DefaultButtonStyle`, `AccentButtonStyle`, `CaptionTextBlockStyle`) | throughout | ✅ |

The one construct that **failed** was `Hyperlink` wrapping a `TextBlock`: Uno's derives from `Inline`
and takes `Inline` children, where WinUI 3 also accepts a `UIElement`. All four Hyperlinks in Kronos
already wrap a `Run`, which is the form that works — so this cost nothing. Worth knowing anyway.

### Runtime — starts, loads its graphics stack, unproven rendering

The Linux binary runs and stays alive. It loads **ICU, SkiaSharp, HarfBuzzSharp, X11, fontconfig and
Mesa GL** — so it gets well past initialisation.

**It does not open a window here, and that is not established as Uno's fault.** This WSL session has
`/mnt/wslg`, a `DISPLAY`, and an `X0` socket, but **no weston compositor process is running**, so there
is nothing to present to. The process spins at 120% CPU across 83 threads polling, which is consistent
with a renderer retrying against a display that is not really there.

**Rendering is therefore unproven.** Someone with a working Linux desktop, or a container with Xvfb,
should run `spike/Kronos.UnoSpike` and confirm before this goes further.

## Deployment finding: ICU is mandatory

Uno's text engine loads ICU directly. `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` does **not** work —
it fails in `Microsoft.UI.Xaml.Documents.UnicodeText.ICU.Init()`, unlike a plain .NET app. Any Linux
distro Kronos targets needs `libicu`, which is a default package almost everywhere.

## What this does *not* solve

Uno migrates the UI, which is about 8% of `src/`. It does nothing for the other 92%: 88 registry
references, 53 P/Invoke sites, and eight store-discovery libraries.

**The Linux CLI today swaps nothing.** `src/Program.cs` registers only `update`, `version` and
`self-update`, and says so itself: *"These require porting the full game detection and DLL management
logic."*

So after a full Uno migration there would be a Linux GUI attached to a product that cannot swap a DLL.
**That is the next piece of work, and it is not a UI task.**

## Remaining UI work, known

Not yet tested — each is a small, bounded substitution rather than a rewrite:

| Package | Used for | Cross-platform? |
|---|---|---|
| `CommunityToolkit.Labs.WinUI.Controls.MarkdownTextBlock` | update notes | needs a replacement |
| `WinUI.TableView` / `CommunityToolkit.WinUI.UI.Controls.DataGrid` | DLL lists | needs a replacement |
| `Microsoft.Web.WebView2` | — | needs a replacement |
| `SixLabors.ImageSharp` | cover images | works on Linux; licensing unchanged |
| `Microsoft.WindowsAppSDK` | WinAppSDK runtime | **dropped** — Uno replaces it |

Dropping the Windows App Runtime removes the "install the App Runtime or Kronos exits silently" problem
in the release notes.

## Reproducing

```powershell
dotnet build spike\Kronos.UnoSpike\Kronos.UnoSpike.csproj -c Release
dotnet publish spike\Kronos.UnoSpike\Kronos.UnoSpike.csproj -c Release -f net10.0-desktop -r linux-x64
```

Run on Linux with `libicu` installed.
