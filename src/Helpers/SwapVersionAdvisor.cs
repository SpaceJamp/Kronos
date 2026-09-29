using System;
using System.Collections.Generic;
using System.Linq;
using Kronos.Data;
using Kronos.Extensions;

namespace Kronos.Helpers;

// Referenced unqualified below. Using directives do not apply to extension methods called as
// instance methods, so CompareVersionStrings and FormatVersionForDisplay need this.
using static Kronos.Extensions.VersionExtensions;

/// <summary>
/// Decides whether swapping a particular DLL into a particular game is a downgrade, and explains why.
/// </summary>
/// <remarks>
/// The baseline for "what the game shipped with" is the game's own backup record. That record is
/// written by <c>Game.UpdateDllAsync</c> before the first overwrite, storing the version and hash of
/// what was on disk at that moment, so it is the shipped file. It is not a new concept and needs no
/// schema change, which is why the previously commented out "base_dlss_version" idea was not needed.
///
/// This is advisory, not a block. Plenty of people downgrade deliberately, including to work around
/// a broken Frame Generation build, so the caller warns and then continues. What it prevents is the
/// silent case, where someone picks an old version from a long list and a DLSS dependent feature
/// quietly stops working with no explanation anywhere.
/// </remarks>
/// <remarks>
/// Declared public only so the nested <see cref="Advice"/> enum can appear as a Theory parameter in
/// the test project. A public test method cannot take a parameter of an internal type, and the
/// parameterised test is what proves the warning stays silent for everything that is not a
/// downgrade.
/// </remarks>
public static class SwapVersionAdvisor
{
    /// <summary>
    /// The kind of comparison that was possible, so the caller can phrase the warning correctly.
    /// </summary>
    /// <remarks>
    /// Declared public rather than internal purely so a Theory in the test project can take one as a
    /// parameter. A public method cannot expose an internal type, and the parameterised test is what
    /// proves the rule stays silent for everything that is not a downgrade.
    /// </remarks>
    public enum Advice
    {
        /// <summary>No useful comparison was possible. Nothing to say.</summary>
        NoAdvice,

        /// <summary>The incoming version is older than the one already installed.</summary>
        Downgrade,

        /// <summary>The incoming version is the same as the one already installed.</summary>
        NoChange,

        /// <summary>The incoming version is newer. This is the expected case.</summary>
        Upgrade,
    }

    /// <summary>
    /// Classifies an incoming version against the version already on disk.
    /// </summary>
    /// <param name="installedVersion">Version currently in the game, may be empty or unparseable.</param>
    /// <param name="incomingVersion">Version of the dll being swapped in.</param>
    /// <remarks>
    /// The comparison is installed against incoming, not the other way round, which is the whole point
    /// of the method and the reason it is not a one liner over
    /// <see cref="CompareVersionStrings"/>. A positive result means the game currently has the newer
    /// file, so the incoming one is a downgrade.
    /// </remarks>
    internal static Advice Classify(string? installedVersion, string? incomingVersion)
    {
        return Classify(CompareVersionStrings(installedVersion, incomingVersion));
    }

    /// <summary>
    /// Classifies from a precomputed comparison, so the rule can be tested without version strings.
    /// </summary>
    /// <remarks>
    /// The comparison is of the installed version against the incoming one, so a positive result
    /// means the incoming version is older and the swap is a downgrade. An early version of this
    /// mapped the sign the other way round and warned on every upgrade instead, which is why the
    /// direction is spelled out here and pinned by tests in both directions.
    ///
    /// A null comparison means at least one version could not be parsed. That maps to
    /// <see cref="Advice.NoAdvice"/> rather than to a guess. It is deliberately not treated as a
    /// downgrade: an unparseable version is a gap in what we know, and inventing a warning from it
    /// would train people to click past warnings that mean something.
    /// </remarks>
    internal static Advice Classify(int? installedVersusIncoming)
    {
        if (installedVersusIncoming is null)
        {
            return Advice.NoAdvice;
        }

        return installedVersusIncoming.Value switch
        {
            > 0 => Advice.Downgrade,
            0 => Advice.NoChange,
            _ => Advice.Upgrade,
        };
    }

    /// <summary>
    /// Works out the baseline version for a game and asset type from its backup record.
    /// </summary>
    /// <remarks>
    /// Returns an empty string when the game has no backup yet, which is the case before the first
    /// swap. At that point whatever is on disk is the shipped file, so there is nothing to compare
    /// against and no warning is warranted.
    /// </remarks>
    internal static string GetShippedVersion(Game game, GameAssetType assetType, GameAssetType backupAssetType)
    {
        // A snapshot, not the live list. See GetGameAssetsSnapshot: enumerating it while a swap is
        // mutating it on a background thread can throw.
        var backup = game.GetGameAssetsSnapshot()
            .FirstOrDefault(x => x.AssetType == backupAssetType && string.IsNullOrWhiteSpace(x.Path) == false);

        return backup?.Version ?? string.Empty;
    }

    /// <summary>
    /// The versions already installed in a game for an asset type, deduplicated and ordered.
    /// </summary>
    /// <remarks>
    /// A game can have several copies of the same DLL, for instance one per graphics API, so a
    /// warning that named one arbitrarily would be confusing. This exists so the message can say
    /// "2.5.0.0" rather than picking a file.
    /// </remarks>
    internal static IReadOnlyList<string> GetInstalledVersions(IEnumerable<GameAsset> assets, GameAssetType assetType)
    {
        return assets
            .Where(x => x.AssetType == assetType && string.IsNullOrWhiteSpace(x.Version) == false)
            .Select(x => x.Version)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds the warning text for a downgrade, or null when there is nothing worth saying.
    /// </summary>
    /// <remarks>
    /// Returns null for anything other than a downgrade so the caller can simply test for null rather
    /// than switching on an enum, which keeps the prompt in one place.
    /// </remarks>
    internal static string? BuildWarning(Advice advice, IEnumerable<string> installedVersions, string? incomingVersion)
    {
        if (advice != Advice.Downgrade)
        {
            return null;
        }

        var incoming = FormatVersionForDisplay(incomingVersion);
        var installed = installedVersions
            .Select(FormatVersionForDisplay)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (installed.Count == 0)
        {
            installed.Add("the version currently installed");
        }

        var installedText = installed.Count == 1
            ? installed[0]
            : string.Join(", ", installed.Take(installed.Count - 1)) + $" or {installed[^1]}";

        return
            $"This DLL is an older DLSS runtime than the one this game is using ({installedText})." +
            $"\n\nYou are about to swap in v{incoming}." +
            "\n\nDLSS Frame Generation, and some other DLSS dependent features, can stop working when" +
            " the runtime is older than the version the game shipped with. The game will usually still" +
            " run, but expect stutter, or no frame generation at all." +
            "\n\nKronos keeps a backup of the file it replaces, so you can put it back from the game's" +
            " history if this does not work out. Continue anyway?";
    }

    /// <summary>
    /// Concatenates every applicable reason into the body of a single warning dialog, or returns null
    /// when there is nothing to warn about.
    /// </summary>
    /// <remarks>
    /// Exists because <c>ContentDialog</c> permits only one open at a time, and WinUI throws
    /// "Only a single ContentDialog can be open at any time" if a second is shown before the first has
    /// finished closing. Showing the repack warning and the downgrade warning as two sequential dialogs
    /// therefore crashes the app for any game that is both a repack and a downgrade, which is a common
    /// combination and reproducible every time.
    ///
    /// Combining them into one dialog is the correct fix rather than a semaphore. A semaphore would
    /// stop the crash but still interrupt the user with two dialogs in a row for what is a single
    /// decision, and it would only work within one process for one call site, so any other pair of
    /// warnings added later would reintroduce the same crash.
    /// </remarks>
    internal static string? ComposeWarning(bool isRepack, string? downgradeWarning)
    {
        var hasRepack = isRepack;
        var hasDowngrade = string.IsNullOrWhiteSpace(downgradeWarning) == false;

        if (hasRepack == false && hasDowngrade == false)
        {
            return null;
        }

        if (hasRepack && hasDowngrade == false)
        {
            return RepackWarningText;
        }

        if (hasRepack == false)
        {
            // Already a full warning ending in its own "Continue anyway?" question.
            return downgradeWarning;
        }

        // Both. Strip the question from each half, join the explanations, and ask once at the end.
        // Leaving the repack half's question in place was the first attempt and produced two
        // "Continue anyway?" lines in one dialog, which the test below caught.
        var repackBody = StripTrailingQuestion(RepackWarningText);
        var downgradeBody = StripTrailingQuestion(downgradeWarning ?? string.Empty);

        return repackBody.TrimEnd() + "\n\n" + downgradeBody.TrimStart() + "\n\nContinue anyway?";
    }

    /// <summary>
    /// Removes a trailing "Continue anyway?" from a warning body, if it has one.
    /// </summary>
    /// <remarks>
    /// Both halves are written to stand alone as a complete dialog, so each carries its own closing
    /// question. When they are combined, only one question should survive, and it belongs at the very
    /// end so the user answers after reading everything rather than halfway through.
    /// </remarks>
    internal static string StripTrailingQuestion(string body)
    {
        const string question = "Continue anyway?";

        if (body.EndsWith(question, StringComparison.Ordinal) == false)
        {
            return body;
        }

        // Trim the whitespace in front of the question as well, so the halves join cleanly whatever
        // separated them. The two warnings were written at different times and use different
        // separators, one "\n\n" and one ". ", so matching a fixed prefix here silently failed and
        // left both questions in the combined message.
        return body[..^question.Length].TrimEnd();
    }

    /// <summary>
    /// Convenience: classify and build the warning in one call.
    /// </summary>
    internal static string? GetDowngradeWarning(
        string? installedVersion,
        string? incomingVersion,
        IEnumerable<string>? allInstalledVersions = null)
    {
        var advice = Classify(installedVersion, incomingVersion);
        var versions = allInstalledVersions ?? new[] { installedVersion ?? string.Empty };

        return BuildWarning(advice, versions, incomingVersion);
    }

    /// <summary>
    /// The repack warning, which predates this class and previously lived inline in the caller.
    /// </summary>
    /// <remarks>
    /// Moved here so that both warnings can be composed into one dialog, and so the wording is
    /// covered by tests rather than only reachable through a click.
    /// </remarks>
    internal const string RepackWarningText =
        "This game is flagged as a repack. Repacks often ship their own runtime or launcher, so a " +
        "swapped DLL may be ignored, or may stop the repack's launcher from working. Swapping may " +
        "also invalidate the repack's integrity check.\n\nContinue anyway?";
}
