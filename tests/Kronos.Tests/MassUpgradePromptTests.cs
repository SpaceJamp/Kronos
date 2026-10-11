using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kronos.Data;
using Kronos.Helpers;
using Kronos.Pages;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the confirmation shown before a whole-library update.
/// </summary>
/// <remarks>
/// "Update all" writes to every game in the library, so the dialog is the only thing standing between a
/// misclick and a few hundred games being rewritten. Three things about it were wrong.
///
/// The counts were there and the names were not. The planner collected a title for every item and
/// Summarise never used it, so a user was told "3 cannot be updated" and left to guess which three - the
/// games that will be left untouched are exactly what someone confirming a library-wide write needs to
/// check.
///
/// The content was a bare string in a dialog with no scroll viewer, so a library with many blocked entries
/// rendered taller than the dialog and pushed the buttons off the bottom.
///
/// And the primary button was focused by default, so a stray Enter confirmed a whole-library write.
/// </remarks>
public class MassUpgradePromptTests
{
    static MassUpgradePlanner.PlanItem Item(string title, MassUpgradePlanner.ActionKind kind) =>
        new(
            GameId: title.ToLowerInvariant(),
            GameTitle: title,
            AssetType: GameAssetType.DLSS,
            InstalledVersion: "2.5.0.0",
            TargetVersion: "3.0.0.0",
            Kind: kind);

    static MassUpgradePlanner.Plan Plan(params MassUpgradePlanner.PlanItem[] items) =>
        new(items);

    // ------------------------------------------------------------------ naming the blocked games

    [Fact]
    public void TheGamesThatWillNotBeTouchedAreNamed()
    {
        // The whole point of the dialog. "3 cannot be updated because the file is not downloaded yet" says
        // how many; it does not say whether the three are the ones the user cares about.
        var summary = MassUpgradePlanner.Summarise(
            Plan(
                Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade),
                Item("Hogwarts Legacy", MassUpgradePlanner.ActionKind.NotDownloaded),
                Item("Baldurs Gate 3", MassUpgradePlanner.ActionKind.NotDownloaded),
                Item("Elden Ring", MassUpgradePlanner.ActionKind.NotDownloaded)));

        Assert.Contains("Hogwarts Legacy", summary, StringComparison.Ordinal);
        Assert.Contains("Baldurs Gate 3", summary, StringComparison.Ordinal);
        Assert.Contains("Elden Ring", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheBlockedGamesAreNamed()
    {
        // A game being updated is already accounted for by the count of updates. Naming it again would
        // bury the ones that are being skipped.
        var summary = MassUpgradePlanner.Summarise(
            Plan(
                Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade),
                Item("Hogwarts Legacy", MassUpgradePlanner.ActionKind.NotDownloaded)));

        var blockedLine = summary
            .Split("\n\n")
            .First(x => x.Contains("Hogwarts Legacy", StringComparison.Ordinal));

        Assert.DoesNotContain("Cyberpunk 2077", blockedLine, StringComparison.Ordinal);
    }

    [Fact]
    public void AGameWithSeveralBlockedDllsIsNamedOnce()
    {
        // nx and x DLLs in one install both being undownloaded is one game that will not be touched, not
        // two. Listing it twice reads as a counting bug.
        var summary = MassUpgradePlanner.Summarise(
            Plan(
                Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade),
                Item("Hogwarts Legacy", MassUpgradePlanner.ActionKind.NotDownloaded),
                new(
                    GameId: "hogwarts-legacy-2",
                    GameTitle: "Hogwarts Legacy",
                    AssetType: GameAssetType.DLSS_G,
                    InstalledVersion: "2.5.0.0",
                    TargetVersion: string.Empty,
                    Kind: MassUpgradePlanner.ActionKind.NotDownloaded)));

        var occurrences = summary.Split("Hogwarts Legacy").Length - 1;

        Assert.Equal(1, occurrences);
    }

    [Fact]
    public void TheSameGameTitleFromTwoStoresIsListedOnce()
    {
        // A game owned on Steam and on GOG produces one item per install path, with the same title. Two
        // copies of the same name in a confirmation reads as a bug in the planner.
        var summary = MassUpgradePlanner.Summarise(
            Plan(
                Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade),
                Item("Hogwarts Legacy", MassUpgradePlanner.ActionKind.NotDownloaded),
                new(
                    GameId: "hogwarts-legacy-gog",
                    GameTitle: "Hogwarts Legacy",
                    AssetType: GameAssetType.DLSS,
                    InstalledVersion: "2.5.0.0",
                    TargetVersion: string.Empty,
                    Kind: MassUpgradePlanner.ActionKind.NotDownloaded)));

        Assert.Equal(1, summary.Split("Hogwarts Legacy").Length - 1);
    }

    [Fact]
    public void GamesBeingUpdatedOrLeftAloneOnTheirOwnAreNotListedAsBlocked()
    {
        var summary = MassUpgradePlanner.Summarise(
            Plan(
                Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade),
                Item("Elden Ring", MassUpgradePlanner.ActionKind.AlreadyCurrent),
                Item("Baldurs Gate 3", MassUpgradePlanner.ActionKind.NoCandidate)));

        Assert.DoesNotContain("Left unchanged", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void NoBlockedGamesMeansNoEmptyListLine()
    {
        // The section only appears when it has something in it. "Left unchanged: ." is worse than silence.
        var summary = MassUpgradePlanner.Summarise(
            Plan(
                Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade),
                Item("Elden Ring", MassUpgradePlanner.ActionKind.AlreadyCurrent)));

        Assert.DoesNotContain("Left unchanged", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AGameWithNoTitleIsSkippedRatherThanShownAsABlank()
    {
        var summary = MassUpgradePlanner.Summarise(
            Plan(
                Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade),
                new(
                    GameId: "orphan",
                    GameTitle: "   ",
                    AssetType: GameAssetType.DLSS,
                    InstalledVersion: "2.5.0.0",
                    TargetVersion: string.Empty,
                    Kind: MassUpgradePlanner.ActionKind.NotDownloaded)));

        Assert.DoesNotContain("Left unchanged", summary, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ bounding it

    [Fact]
    public void ALongListOfBlockedGamesIsTruncatedWithACount()
    {
        // This is a confirmation, not a report. The dialog scrolls, so this is not about the buttons being
        // pushed off the bottom any more - it is that nobody reads three hundred game titles before
        // pressing the key that acts on all of them.
        var items = new List<MassUpgradePlanner.PlanItem> { Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade) };
        items.AddRange(Enumerable
            .Range(0, 40)
            .Select(i => Item($"Blocked Game {i:00}", MassUpgradePlanner.ActionKind.NotDownloaded)));

        var summary = MassUpgradePlanner.Summarise(Plan(items.ToArray()));

        Assert.Contains($"and {40 - MassUpgradePlanner.MaxNamedGames} more", summary, StringComparison.Ordinal);

        // The first ones are named, the last ones are not.
        Assert.Contains("Blocked Game 00", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Blocked Game 39", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void AListExactlyAtTheLimitIsNotTruncated()
    {
        var items = new List<MassUpgradePlanner.PlanItem> { Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade) };
        items.AddRange(Enumerable
            .Range(0, MassUpgradePlanner.MaxNamedGames)
            .Select(i => Item($"Blocked Game {i:00}", MassUpgradePlanner.ActionKind.NotDownloaded)));

        var summary = MassUpgradePlanner.Summarise(Plan(items.ToArray()));

        Assert.DoesNotContain("and", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("more.", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDialogStillEndsWithAQuestion()
    {
        // The new section must not have pushed the actual question out of the way.
        var summary = MassUpgradePlanner.Summarise(
            Plan(
                Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade),
                Item("Hogwarts Legacy", MassUpgradePlanner.ActionKind.NotDownloaded)));

        Assert.Contains("Continue?", summary, StringComparison.Ordinal);
        Assert.True(
            summary.IndexOf("Continue?", StringComparison.Ordinal) > summary.IndexOf("Left unchanged", StringComparison.Ordinal),
            "the question must come after the detail, not before it");
    }

    [Fact]
    public void TheSummaryStillSaysBackupsAreKept()
    {
        // Pre-existing behaviour that the new section must not have displaced.
        var summary = MassUpgradePlanner.Summarise(Plan(Item("Cyberpunk 2077", MassUpgradePlanner.ActionKind.Upgrade)));

        Assert.Contains("backs up every file it replaces", summary, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ the dialog itself

    static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    static string ViewModelSource => Source("src", "Pages", "MassUpgradeViewModel.cs");

    [Fact]
    public void TheConfirmationOpensOnCancelRatherThanUpdate()
    {
        // A destructive action that starts focused invites the wrong Enter keypress. This is a
        // whole-library write.
        var source = ViewModelSource;

        var previewAt = source.IndexOf("Update every game?", StringComparison.Ordinal);
        Assert.True(previewAt > 0, "the confirmation should still exist");

        var block = source.Substring(previewAt, 1200);

        Assert.Contains("DefaultButton = ContentDialogButton.Close", block, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanIsShownInSomethingThatScrollsAndIsBounded()
    {
        // Otherwise a library with many blocked entries renders taller than the dialog, and both buttons
        // end up below the bottom of the window with no way to reach them.
        var source = ViewModelSource;

        var previewAt = source.IndexOf("Update every game?", StringComparison.Ordinal);
        var block = source.Substring(previewAt, 1400);

        Assert.Contains("new ScrollViewer", block, StringComparison.Ordinal);
        Assert.Contains("MaxHeight", block, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility", block, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = TextWrapping.Wrap", block, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePlanTextCanStillBeCopiedOutOfTheDialog()
    {
        // Small courtesy, and the only way to get the list into a bug report without transcribing it.
        var source = ViewModelSource;

        var previewAt = source.IndexOf("Update every game?", StringComparison.Ordinal);
        var block = source.Substring(previewAt, 1400);

        Assert.Contains("IsTextSelectionEnabled = true", block, StringComparison.Ordinal);
    }
}
