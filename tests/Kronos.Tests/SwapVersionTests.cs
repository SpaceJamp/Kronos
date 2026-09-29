using System;
using System.Collections.Generic;
using System.Linq;
using Kronos.Data;
using Kronos.Extensions;
using Kronos.Helpers;
using Kronos.UserControls;

namespace Kronos.Tests;

/// <summary>
/// Tests for parsing version strings, and for the rule that decides whether a swap is a downgrade.
/// </summary>
/// <remarks>
/// The behaviour being protected is one that used not to exist. <c>Game.UpdateDllAsync</c> validated
/// that the file existed, that its hash matched the record, and that it was signed, but never
/// compared its version against what the game already had. So swapping in an older DLSS runtime
/// silently disabled DLSS Frame Generation, with nothing in the UI to explain why.
///
/// Version strings arrive from <c>FileVersionInfo.GetFormattedFileVersion()</c>, which is a display
/// string rather than a four part number, so most of the parsing tests below are about inputs that
/// are not quite what a developer would expect.
/// </remarks>
public class VersionParsingTests
{
    // ---- Parsing ----

    [Theory]
    [InlineData("1.0.0.0", 1UL << 48)]
    [InlineData("2.5.0.0", (2UL << 48) + (5UL << 32))]
    public void AFourPartVersionPacksAsVersionWould(string version, ulong expected)
    {
        Assert.Equal(expected, VersionExtensions.TryParseVersionNumber(version));
    }

    [Fact]
    public void AMissingPartIsTreatedAsZero()
    {
        // "2.5" and "2.5.0.0" are the same version, and a game's files will not all be formatted the
        // same way, so treating them as different would produce spurious warnings.
        Assert.Equal(
            VersionExtensions.TryParseVersionNumber("2.5.0.0"),
            VersionExtensions.TryParseVersionNumber("2.5"));
    }

    [Fact]
    public void ASingleNumberIsAccepted()
    {
        Assert.Equal(10UL << 48, VersionExtensions.TryParseVersionNumber("10"));
    }

    [Fact]
    public void TrailingJunkIsIgnoredRatherThanLosingTheVersion()
    {
        // The whole point of parsing rather than requiring a strict four numbers is that an unusual
        // version should still be comparable. Losing the comparison here would silently disable the
        // downgrade warning for exactly the versions that are hardest to read.
        Assert.Equal(
            VersionExtensions.TryParseVersionNumber("2.5.0.0"),
            VersionExtensions.TryParseVersionNumber("2.5.0.0-beta"));
    }

    [Fact]
    public void AFifthComponentIsIgnored()
    {
        // Version holds four at most, so a fifth cannot be compared anyway.
        Assert.Equal(
            VersionExtensions.TryParseVersionNumber("2.5.0.0"),
            VersionExtensions.TryParseVersionNumber("2.5.0.0.99"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    [InlineData("beta")]
    public void SomethingThatIsNotAVersionIsNullRatherThanZero(string? version)
    {
        // Null matters: zero would compare as older than everything, so every unparseable version
        // would produce a downgrade warning.
        Assert.Null(VersionExtensions.TryParseVersionNumber(version));
    }

    [Fact]
    public void ParsedVersionsOrderTheSameWayAsSystemVersion()
    {
        // The two must agree, or a version parsed here could order differently from one compared via
        // the existing GetVersionNumber path used by the update checker.
        Assert.Equal(
            new Version(2, 5, 0, 0).GetVersionNumber(),
            VersionExtensions.TryParseVersionNumber("2.5.0.0"));
    }

    [Fact]
    public void AnAbsurdlyLargeComponentSaturatesInsteadOfWrapping()
    {
        // Wrapping would order a huge component below a small one, which is worse than calling it big.
        // Asserted as "greater than any real version" rather than as ulong.MaxValue, because the value
        // is then shifted into a field and the result is not the saturated number itself.
        var huge = VersionExtensions.TryParseVersionNumber(new string('9', 40));
        var real = VersionExtensions.TryParseVersionNumber("9999.9999.9999.9999");

        Assert.NotNull(huge);
        Assert.NotNull(real);
        Assert.True(huge > real,
            "A 40 digit component must order above any plausible real version rather than wrapping.");
    }

    [Fact]
    public void ARealVersionNeverSaturates()
    {
        // The guard above is only correct if it does not fire on anything real, so check the largest
        // version Version can actually hold.
        var max = VersionExtensions.TryParseVersionNumber("65535.65535.65535.65535");

        Assert.NotNull(max);
        Assert.Equal(ulong.MaxValue, max);
    }

    // ---- Comparing ----

    [Fact]
    public void AnOlderVersionComparesAsLess()
    {
        Assert.Equal(-1, VersionExtensions.CompareVersionStrings("2.5.0.0", "3.0.0.0"));
    }

    [Fact]
    public void ANewerVersionComparesAsGreater()
    {
        Assert.Equal(1, VersionExtensions.CompareVersionStrings("3.1.0.0", "3.0.0.0"));
    }

    [Fact]
    public void DifferentFormatsOfTheSameVersionCompareEqual()
    {
        Assert.Equal(0, VersionExtensions.CompareVersionStrings("2.5", "2.5.0.0"));
    }

    [Fact]
    public void ComparingWithSomethingUnparseableIsUnknownRatherThanWrong()
    {
        Assert.Null(VersionExtensions.CompareVersionStrings("2.5.0.0", "unknown"));
        Assert.Null(VersionExtensions.CompareVersionStrings(null, "2.5.0.0"));
    }

    [Fact]
    public void ABuildComponentIsComparedNotIgnored()
    {
        // DLSS versions where only the build differs are common, and dropping the build would make
        // 2.5.1 and 2.5.2 look identical.
        Assert.Equal(-1, VersionExtensions.CompareVersionStrings("2.5.1.0", "2.5.2.0"));
    }

    // ---- Display formatting ----

    [Theory]
    [InlineData("2.5.0.0", "2.5")]
    [InlineData("2.5", "2.5")]
    [InlineData("3.0.0.0", "3")]
    [InlineData("3.1.2.3", "3.1.2.3")]
    public void TrailingZeroComponentsAreTrimmedForDisplay(string version, string expected)
    {
        // Matches how GameAsset.DisplayVersion presents the same value, so a warning and the picker
        // do not show the same version two different ways.
        Assert.Equal(expected, VersionExtensions.FormatVersionForDisplay(version));
    }

    [Fact]
    public void AnUnknownVersionDisplaysAsUnknownRatherThanBlank()
    {
        Assert.Equal("unknown", VersionExtensions.FormatVersionForDisplay(null));
        Assert.Equal("unknown", VersionExtensions.FormatVersionForDisplay(""));
    }
}

/// <summary>
/// Tests for the downgrade decision and the warning it produces.
/// </summary>
public class SwapVersionAdvisorTests
{
    [Fact]
    public void AnOlderIncomingVersionIsADowngrade()
    {
        // The installed version is the first argument and the incoming one the second, so "2.5 now,
        // 2.0 going in" must read as a downgrade.
        Assert.Equal(
            SwapVersionAdvisor.Advice.Downgrade,
            SwapVersionAdvisor.Classify("2.5.0.0", "2.0.0.0"));
    }

    [Fact]
    public void TheSameVersionIsNoChange()
    {
        Assert.Equal(
            SwapVersionAdvisor.Advice.NoChange,
            SwapVersionAdvisor.Classify("2.5.0.0", "2.5.0.0"));
    }

    [Fact]
    public void ANewerIncomingVersionIsAnUpgrade()
    {
        Assert.Equal(
            SwapVersionAdvisor.Advice.Upgrade,
            SwapVersionAdvisor.Classify("2.0.0.0", "2.5.0.0"));
    }

    [Fact]
    public void AnUnparseableVersionProducesNoAdviceRatherThanAGuess()
    {
        // THE DECISION. Mapping this to Downgrade would mean warning on every game whose files have
        // an unusual version string, which is how a warning stops being read.
        Assert.Equal(
            SwapVersionAdvisor.Advice.NoAdvice,
            SwapVersionAdvisor.Classify("unknown", "2.5.0.0"));

        Assert.Equal(
            SwapVersionAdvisor.Advice.NoAdvice,
            SwapVersionAdvisor.Classify("2.5.0.0", "unknown"));
    }

    [Fact]
    public void ADowngradeProducesAWarning()
    {
        var warning = SwapVersionAdvisor.BuildWarning(
            SwapVersionAdvisor.Advice.Downgrade, new[] { "3.0.0.0" }, "2.5.0.0");

        Assert.NotNull(warning);
    }

    [Theory]
    [InlineData(SwapVersionAdvisor.Advice.NoAdvice)]
    [InlineData(SwapVersionAdvisor.Advice.NoChange)]
    [InlineData(SwapVersionAdvisor.Advice.Upgrade)]
    public void NothingOtherThanADowngradeProducesAWarning(SwapVersionAdvisor.Advice advice)
    {
        // An upgrade is the expected case and must stay silent, otherwise every ordinary swap
        // interrupts with a dialog.
        Assert.Null(SwapVersionAdvisor.BuildWarning(advice, new[] { "2.0.0.0" }, "2.5.0.0"));
    }

    [Fact]
    public void TheWarningNamesBothVersions()
    {
        var warning = SwapVersionAdvisor.BuildWarning(
            SwapVersionAdvisor.Advice.Downgrade, new[] { "3.0.0.0" }, "2.5.0.0");

        Assert.NotNull(warning);
        Assert.Contains("3", warning);
        Assert.Contains("2.5", warning);
    }

    [Fact]
    public void TheWarningMentionsFrameGenerationAndTheWayBack()
    {
        // The two things that make the warning actionable rather than merely alarming: what breaks,
        // and the fact that it can be undone.
        var warning = SwapVersionAdvisor.BuildWarning(
            SwapVersionAdvisor.Advice.Downgrade, new[] { "3.0.0.0" }, "2.5.0.0");

        Assert.NotNull(warning);
        Assert.Contains("Frame Generation", warning);
        Assert.Contains("backup", warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeveralInstalledVersionsAreAllNamed()
    {
        // A game can carry more than one copy of the dll, one per graphics API, and naming only one
        // arbitrarily would be confusing.
        var warning = SwapVersionAdvisor.BuildWarning(
            SwapVersionAdvisor.Advice.Downgrade, new[] { "3.0.0.0", "3.1.0.0" }, "2.5.0.0");

        Assert.NotNull(warning);
        Assert.Contains("3", warning);
        Assert.Contains("3.1", warning);
    }

    [Fact]
    public void TheWarningDoesNotBlockOnItsOwn()
    {
        // The text must ask rather than refuse, because downgrading deliberately is a valid reason
        // to be in this dialog at all.
        var warning = SwapVersionAdvisor.BuildWarning(
            SwapVersionAdvisor.Advice.Downgrade, new[] { "3.0.0.0" }, "2.5.0.0");

        Assert.NotNull(warning);
        Assert.Contains("Continue anyway?", warning);
    }

    [Fact]
    public void InstalledVersionsAreDeduplicatedAndOrdered()
    {
        var assets = new List<GameAsset>
        {
            new() { AssetType = GameAssetType.DLSS, Version = "3.1.0.0" },
            new() { AssetType = GameAssetType.DLSS, Version = "3.0.0.0" },
            new() { AssetType = GameAssetType.DLSS, Version = "3.0.0.0" },
            new() { AssetType = GameAssetType.DLSS_G, Version = "9.9.9.9" },
            new() { AssetType = GameAssetType.DLSS, Version = "  " },
        };

        var versions = SwapVersionAdvisor.GetInstalledVersions(assets, GameAssetType.DLSS);

        // Only DLSS, not DLSS_G, deduplicated, and sorted. The blank version is dropped because it
        // carries no information and would render as an empty entry in the warning.
        Assert.Equal(new[] { "3.0.0.0", "3.1.0.0" }, versions);
    }

    // ---- Combining warnings into one message ----
    //
    // The repack warning and the downgrade warning used to be separate ContentDialogs. The DLL picker
    // is itself a ContentDialog, so showing a second one threw 0x80000019 "Only a single
    // ContentDialog can be open at any time" and killed the app. That is fixed by using the picker's
    // own InfoBar; these tests cover the text composition that replaced the two dialogs.

    [Fact]
    public void ARepackOnItsOwnStillWarns()
    {
        var text = SwapVersionAdvisor.ComposeWarning(isRepack: true, downgradeWarning: null);

        Assert.NotNull(text);
        Assert.Contains("repack", text!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ADowngradeOnItsOwnStillWarns()
    {
        var downgrade = SwapVersionAdvisor.BuildWarning(
            SwapVersionAdvisor.Advice.Downgrade, new[] { "3.0.0.0" }, "2.5.0.0");

        var text = SwapVersionAdvisor.ComposeWarning(isRepack: false, downgradeWarning: downgrade);

        Assert.NotNull(text);
        Assert.Contains("2.5", text!);
    }

    [Fact]
    public void ARepackAndADowngradeProduceOneMessageCarryingBoth()
    {
        // This is the combination that produced two dialogs, and so the crash.
        var downgrade = SwapVersionAdvisor.BuildWarning(
            SwapVersionAdvisor.Advice.Downgrade, new[] { "3.0.0.0" }, "2.5.0.0");

        var text = SwapVersionAdvisor.ComposeWarning(isRepack: true, downgradeWarning: downgrade);

        Assert.NotNull(text);
        Assert.Contains("repack", text!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2.5", text);
    }

    [Fact]
    public void TheCombinedMessageAsksOnlyOnce()
    {
        // Two "Continue anyway?" questions in one dialog would be nonsense.
        var downgrade = SwapVersionAdvisor.BuildWarning(
            SwapVersionAdvisor.Advice.Downgrade, new[] { "3.0.0.0" }, "2.5.0.0");

        var text = SwapVersionAdvisor.ComposeWarning(isRepack: true, downgradeWarning: downgrade);

        Assert.NotNull(text);
        var occurrences = text!.Split("Continue anyway?").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void NothingToWarnAboutMeansNoMessageAtAll()
    {
        Assert.Null(SwapVersionAdvisor.ComposeWarning(isRepack: false, downgradeWarning: null));
    }

    [Fact]
    public void TheDowngradeExplanationComesAfterTheRepackOne()
    {
        // The version specific detail is the part that actually tells the user what will break, so it
        // is read last.
        var downgrade = SwapVersionAdvisor.BuildWarning(
            SwapVersionAdvisor.Advice.Downgrade, new[] { "3.0.0.0" }, "2.5.0.0");

        var text = SwapVersionAdvisor.ComposeWarning(isRepack: true, downgradeWarning: downgrade)!;

        Assert.True(
            text.IndexOf("repack", StringComparison.OrdinalIgnoreCase) < text.IndexOf("Frame Generation", StringComparison.OrdinalIgnoreCase),
            "The repack warning should come first and the downgrade explanation after it.");
    }

    // ---- Warning then proceed on the second press ----

    [Fact]
    public void AWarningStopsTheFirstAttempt()
    {
        var accepted = false;

        Assert.True(DLLPickerControlModel.ShouldShowWarningAndStop(hasWarning: true, ref accepted));
    }

    [Fact]
    public void TheSecondAttemptProceedsWithoutWarningAgain()
    {
        var accepted = false;

        Assert.True(DLLPickerControlModel.ShouldShowWarningAndStop(hasWarning: true, ref accepted));
        Assert.False(DLLPickerControlModel.ShouldShowWarningAndStop(hasWarning: true, ref accepted));
    }

    [Fact]
    public void AcceptingOneWarningDoesNotCarryOverToTheNext()
    {
        // Otherwise a later swap of something the user was never shown would proceed silently.
        var accepted = false;

        Assert.True(DLLPickerControlModel.ShouldShowWarningAndStop(hasWarning: true, ref accepted));
        Assert.False(DLLPickerControlModel.ShouldShowWarningAndStop(hasWarning: true, ref accepted));
        Assert.True(DLLPickerControlModel.ShouldShowWarningAndStop(hasWarning: true, ref accepted));
    }

    [Fact]
    public void ASwapWithNothingToWarnAboutProceedsImmediately()
    {
        var accepted = false;

        Assert.False(DLLPickerControlModel.ShouldShowWarningAndStop(hasWarning: false, ref accepted));
    }

    [Fact]
    public void AnUnwarnedSwapClearsAnyLeftoverAcceptance()
    {
        // A warning that is no longer applicable must not leave the next one pre-approved.
        var accepted = true;

        Assert.False(DLLPickerControlModel.ShouldShowWarningAndStop(hasWarning: false, ref accepted));
        Assert.True(DLLPickerControlModel.ShouldShowWarningAndStop(hasWarning: true, ref accepted));
    }

    [Fact]
    public void GetDowngradeWarningComposesTheTwoSteps()
    {
        Assert.NotNull(SwapVersionAdvisor.GetDowngradeWarning("3.0.0.0", "2.0.0.0"));
        Assert.Null(SwapVersionAdvisor.GetDowngradeWarning("2.0.0.0", "3.0.0.0"));
        Assert.Null(SwapVersionAdvisor.GetDowngradeWarning("unknown", "2.0.0.0"));
    }

    [Fact]
    public void NoShippedVersionIsReportedBeforeTheFirstSwap()
    {
        // The backup record only exists once something has been swapped, so before that there is no
        // baseline and nothing should be claimed.
        var assets = new List<GameAsset>
        {
            new() { AssetType = GameAssetType.DLSS, Version = "2.0.0.0" },
        };

        var backup = assets.FirstOrDefault(x => x.AssetType == GameAssetType.DLSS_BACKUP);

        Assert.Null(backup);
    }
}
