using System;
using System.Collections.Generic;
using System.Linq;
using Kronos.Data;
using Kronos.Extensions;

namespace Kronos.Helpers;

/// <summary>
/// Works out, without touching a single file, what a mass upgrade would do to a set of games.
/// </summary>
/// <remarks>
/// This is the dry run. It is deliberately separated from the code that writes anything, because the
/// whole point of previewing is that it is trusted: if the plan and the execution were the same code,
/// a bug in the plan would be a bug in what gets written, and the preview would agree with the bug.
///
/// Every decision that could stop a swap, or silently do nothing, is represented here as a status on a
/// plan item. The executor then does the minimum: look up the plan, write the files, report the result.
/// </remarks>
public static class MassUpgradePlanner
{
    /// <summary>
    /// Asset types a mass upgrade will consider.
    /// </summary>
    /// <remarks>
    /// Backup types are excluded. They hold the game's own file, not something to upgrade, and
    /// including them would put a game's shipped runtime back under a swap.
    ///
    /// XeLL and DirectStorage are excluded too: they are not versioned DLSS runtimes, so there is no
    /// "newer" to move to, and swapping them by version order would be meaningless.
    /// </remarks>
    internal static readonly GameAssetType[] UpgradableAssetTypes =
    [
        GameAssetType.DLSS,
        GameAssetType.DLSS_G,
        GameAssetType.DLSS_D,
        GameAssetType.FSR_31_DX12,
        GameAssetType.FSR_31_VK,
        GameAssetType.XeSS,
        GameAssetType.XeSS_FG,
        GameAssetType.XeSS_DX11,
    ];

    /// <summary>
    /// Whether an asset type takes part in a mass upgrade at all.
    /// </summary>
    internal static bool IsUpgradable(GameAssetType assetType)
    {
        return Array.IndexOf(UpgradableAssetTypes, assetType) >= 0;
    }

    /// <summary>
    /// What a mass upgrade would do to one DLL in one game.
    /// </summary>
    public enum ActionKind
    {
        /// <summary>The game already has this version, or newer. Nothing to do.</summary>
        AlreadyCurrent,

        /// <summary>Would be upgraded to a newer runtime.</summary>
        Upgrade,

        /// <summary>Would be replaced with an older runtime. Not done without asking.</summary>
        Downgrade,

        /// <summary>The newest version is not downloaded, so there is no file to copy.</summary>
        NotDownloaded,

        /// <summary>No records exist for this asset type in the game.</summary>
        NoRecords,

        /// <summary>Nothing in the library matched this asset type at all.</summary>
        NoCandidate,
    }

    /// <summary>
    /// One DLL in one game, and what would happen to it.
    /// </summary>
    /// <param name="GameId">Identifies the game, for the report.</param>
    /// <param name="GameTitle">Shown in the preview.</param>
    /// <param name="AssetType">Which DLL this is.</param>
    /// <param name="InstalledVersion">Version currently in the game, empty if unknown.</param>
    /// <param name="TargetVersion">Version that would be written, empty when there is no target.</param>
    /// <param name="Kind">What the plan would do.</param>
    public readonly record struct PlanItem(
        string GameId,
        string GameTitle,
        GameAssetType AssetType,
        string InstalledVersion,
        string TargetVersion,
        ActionKind Kind)
    {
        /// <summary>Whether this item would actually write a file.</summary>
        public bool IsChange => Kind == ActionKind.Upgrade;

        /// <summary>Whether a dry run must stop and ask about this one.</summary>
        public bool NeedsConfirmation => Kind == ActionKind.Downgrade;
    }

    /// <summary>
    /// The whole plan, plus the counts a summary needs.
    /// </summary>
    /// <param name="Items">Every DLL considered, including the ones that would be skipped.</param>
    public readonly record struct Plan(IReadOnlyList<PlanItem> Items)
    {
        /// <summary>Items that would write a newer file.</summary>
        public int UpgradeCount => Items.Count(x => x.Kind == ActionKind.Upgrade);

        /// <summary>Items that are already current.</summary>
        public int AlreadyCurrentCount => Items.Count(x => x.Kind == ActionKind.AlreadyCurrent);

        /// <summary>Items that would replace a newer runtime with an older one.</summary>
        public int DowngradeCount => Items.Count(x => x.Kind == ActionKind.Downgrade);

        /// <summary>Items blocked because the target file is not downloaded.</summary>
        public int NotDownloadedCount => Items.Count(x => x.Kind == ActionKind.NotDownloaded);

        /// <summary>Games that would change at least one file.</summary>
        public int GamesAffected => Items.Where(x => x.IsChange).Select(x => x.GameId).Distinct().Count();

        /// <summary>Whether there is anything at all to do.</summary>
        public bool HasWork => UpgradeCount > 0 || DowngradeCount > 0;
    }

    /// <summary>
    /// Chooses the newest downloaded release for an asset type.
    /// </summary>
    /// <remarks>
    /// "Downloaded" is part of the criterion rather than something checked afterwards, because a
    /// record with no local file is the single most common reason a mass update silently does nothing.
    /// Skipping it here means it appears in the preview as blocked, with a reason, rather than
    /// disappearing.
    ///
    /// Dev builds are only considered when <paramref name="allowDevDlls"/> is set, matching the
    /// per game picker which hides them behind the same setting.
    /// </remarks>
    internal static DLLRecord? FindTargetRecord(
        IReadOnlyList<DLLRecord> records,
        GameAssetType assetType,
        bool allowDevDlls)
    {
        return records
            .Where(x => x.AssetType == assetType)
            .Where(x => x.IsDevFile == false || allowDevDlls)
            .Where(x => x.LocalRecord is not null && x.LocalRecord.IsDownloaded)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefault();
    }

    /// <summary>
    /// Classifies one DLL in one game against the chosen target.
    /// </summary>
    /// <remarks>
    /// The comparison is installed against target, matching <c>SwapVersionAdvisor.Classify</c>: a
    /// positive result means the game already has something newer, so the target is a downgrade.
    /// </remarks>
    internal static ActionKind Classify(
        string? installedVersion,
        bool hasTarget,
        string? targetVersion)
    {
        if (hasTarget == false)
        {
            return ActionKind.NoCandidate;
        }

        var comparison = VersionExtensions.CompareVersionStrings(installedVersion, targetVersion);

        // An unparseable installed version is treated as "not current" rather than skipped, so the
        // preview shows it and the user can decide. Silently leaving a file alone is the outcome that
        // makes a mass update look like it did nothing.
        if (comparison is null)
        {
            return ActionKind.Upgrade;
        }

        return comparison.Value switch
        {
            > 0 => ActionKind.Downgrade,
            0 => ActionKind.AlreadyCurrent,
            _ => ActionKind.Upgrade,
        };
    }

    /// <summary>
    /// Builds the plan for one game.
    /// </summary>
    /// <param name="game">The game to plan for.</param>
    /// <param name="library">Every known DLL release, from DLLManager.</param>
    /// <param name="allowDevDlls">Whether dev builds are eligible targets.</param>
    public static Plan PlanForGame(Game game, IReadOnlyList<DLLRecord> library, bool allowDevDlls)
    {
        if (game is null)
        {
            return new Plan(Array.Empty<PlanItem>());
        }

        var items = new List<PlanItem>();

        // Group the game's files by type, so one pass covers every copy of that DLL. A game can have
        // several, one per graphics API or per install directory, and they are swapped together.
        var installed = game.GameAssets
            .Where(x => IsUpgradable(x.AssetType))
            .GroupBy(x => x.AssetType);

        var seen = new HashSet<GameAssetType>();

        foreach (var group in installed)
        {
            seen.Add(group.Key);

            var installedVersion = group
                .Select(x => x.Version)
                .FirstOrDefault(x => string.IsNullOrWhiteSpace(x) == false) ?? string.Empty;

            var target = FindTargetRecord(library, group.Key, allowDevDlls);

            items.Add(new PlanItem(
                game.ID,
                game.Title ?? string.Empty,
                group.Key,
                installedVersion,
                target?.DisplayVersion ?? string.Empty,
                Classify(installedVersion, target is not null, target?.DisplayVersion)));
        }

        // A game with no files of a type that the library knows about still deserves a line, otherwise
        // the preview is silent about types it could not offer.
        foreach (var assetType in UpgradableAssetTypes)
        {
            if (seen.Contains(assetType))
            {
                continue;
            }

            if (library.Any(x => x.AssetType == assetType) == false)
            {
                continue;
            }

            items.Add(new PlanItem(
                game.ID,
                game.Title ?? string.Empty,
                assetType,
                string.Empty,
                string.Empty,
                ActionKind.NoRecords));
        }

        return new Plan(items);
    }

    /// <summary>
    /// Builds the plan for many games, keeping only the items worth reporting.
    /// </summary>
    /// <remarks>
    /// Games with nothing to do are dropped rather than listed as a row of "already current", because
    /// a preview listing 200 unchanged games buries the three that matter. The counts survive in the
    /// returned <see cref="Plan"/>, so a summary line can still say how many were skipped.
    /// </remarks>
    public static Plan PlanForGames(IEnumerable<Game> games, IReadOnlyList<DLLRecord> library, bool allowDevDlls)
    {
        var items = new List<PlanItem>();

        foreach (var game in games ?? Enumerable.Empty<Game>())
        {
            var plan = PlanForGame(game, library, allowDevDlls);
            items.AddRange(plan.Items);
        }

        return new Plan(items);
    }

    /// <summary>
    /// A one line summary for the confirmation dialog.
    /// </summary>
    /// <remarks>
    /// Reports the downgrade count explicitly when it is non zero, because a downgrade is the one
    /// outcome that can make things worse and the user should not have to read the list to notice.
    /// </remarks>
    internal static string Summarise(Plan plan)
    {
        if (plan.Items.Count == 0)
        {
            return "No games with a DLSS runtime were selected.";
        }

        if (plan.UpgradeCount == 0 && plan.DowngradeCount == 0)
        {
            return "Everything selected is already up to date. Nothing to change.";
        }

        var parts = new List<string>();

        if (plan.UpgradeCount > 0)
        {
            parts.Add($"{plan.UpgradeCount} DLL{(plan.UpgradeCount == 1 ? string.Empty : "s")} across {plan.GamesAffected} game{(plan.GamesAffected == 1 ? string.Empty : "s")} will be updated");
        }

        if (plan.DowngradeCount > 0)
        {
            parts.Add($"{plan.DowngradeCount} will be replaced with an older runtime, which can break Frame Generation");
        }

        if (plan.NotDownloadedCount > 0)
        {
            parts.Add($"{plan.NotDownloadedCount} cannot be updated because the file is not downloaded yet");
        }

        parts.Add("Kronos backs up every file it replaces, so each change can be undone from the game's history.");

        return string.Join("\n\n", parts) + "\n\nContinue?";
    }
}
