using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kronos.Data;

namespace Kronos.Helpers;

/// <summary>
/// Carries out a <see cref="MassUpgradePlanner.Plan"/>, one DLL at a time, reporting as it goes.
/// </summary>
/// <remarks>
/// Deliberately thin. Every decision about what should happen was made by the planner, and this class
/// does the writing and records what actually happened. Keeping the two apart is what makes the
/// preview trustworthy: if the preview were produced by the same code that writes, a bug would be
/// invisible in both.
///
/// Sequential on purpose. Swapping files in several game folders at once multiplies the chances of
/// hitting a locked file, and gives no way to attribute a failure to a particular game. One at a time
/// is slower and predictable.
/// </remarks>
public static class MassUpgradeExecutor
{
    /// <summary>
    /// Outcome of one planned change.
    /// </summary>
    /// <param name="Item">The plan item this result is for.</param>
    /// <param name="Succeeded">Whether the file was written.</param>
    /// <param name="Message">Why it failed, or empty on success.</param>
    /// <param name="NeedsAdmin">Whether rerunning as administrator would probably fix it.</param>
    public readonly record struct Result(
        MassUpgradePlanner.PlanItem Item,
        bool Succeeded,
        string Message,
        bool NeedsAdmin);

    /// <summary>
    /// Runs every change in the plan, in order.
    /// </summary>
    /// <param name="plan">What to do, from <see cref="MassUpgradePlanner"/>.</param>
    /// <param name="resolveTarget">
    /// Looks up the release to write for a plan item. Injected so the executor can be tested without
    /// a real library, and so the plan's record choice cannot drift from the one actually written.
    ///
    /// Called on a background thread, so an implementation that touches anything the UI thread owns
    /// must marshal first. Reading an <c>ObservableCollection</c> that another thread is mutating is
    /// not a crash but will either throw "collection was modified" or return a torn, stale view, so a
    /// resolver over DLLManager's collections has to snapshot on the UI thread and pass the snapshot in.
    /// </param>
    /// <param name="report">
    /// Called after each item, on a background thread. Must not touch bound UI either, for the same
    /// reason. Logging and counters are fine.
    /// </param>
    /// <param name="games">
    /// The games the plan was built from, looked up on the calling thread. Required rather than
    /// resolved from here, because <see cref="GameManager.GetGameCollection"/> returns a WinRT
    /// <c>ICollectionView</c> and calling it off the UI thread throws 0x8001010E. An earlier version
    /// looked games up from inside this class, which runs after a ConfigureAwait(false) and so is on a
    /// thread pool thread, and it failed on the very first item every time.
    /// </param>
    /// <param name="report">Called after each item, for progress.</param>
    /// <param name="cancellation">Stops between items. Never interrupts a file write midway.</param>
    public static async Task<IReadOnlyList<Result>> ExecuteAsync(
        MassUpgradePlanner.Plan plan,
        IReadOnlyList<Game> games,
        Func<MassUpgradePlanner.PlanItem, DLLRecord?> resolveTarget,
        Action<Result>? report = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<Result>();

        if (resolveTarget is null)
        {
            return results;
        }

        // Indexed once, on the calling thread, so the per item lookup is a plain dictionary hit and no
        // WinRT call happens on the background thread at all.
        var gamesById = (games ?? Array.Empty<Game>())
            .Where(x => string.IsNullOrWhiteSpace(x.ID) == false)
            .GroupBy(x => x.ID, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        var changes = plan.Items
            .Where(x => x.Kind == MassUpgradePlanner.ActionKind.Upgrade)
            .ToList();

        foreach (var item in changes)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // Reported as a failure rather than silently dropped, so the summary counts match what
                // the preview promised and a cancelled run cannot be mistaken for a complete one.
                results.Add(new Result(item, false, "Cancelled before this change was made.", false));
                report?.Invoke(results[^1]);

                continue;
            }

            var result = await ApplyOneAsync(item, gamesById, resolveTarget).ConfigureAwait(false);
            results.Add(result);
            report?.Invoke(result);
        }

        return results;
    }

    static async Task<Result> ApplyOneAsync(
        MassUpgradePlanner.PlanItem item,
        IReadOnlyDictionary<string, Game> gamesById,
        Func<MassUpgradePlanner.PlanItem, DLLRecord?> resolveTarget)
    {
        if (gamesById.TryGetValue(item.GameId, out var game) == false)
        {
            return new Result(item, false, "The game is no longer in the library.", false);
        }

        DLLRecord? record;
        try
        {
            record = resolveTarget(item);
        }
        catch (Exception err)
        {
            // The resolver reads DLLManager's collections, which are bound to the UI thread. If it ever
            // throws, that is one item failing rather than the whole run ending.
            Logger.Error(err);

            return new Result(item, false, err.Message, false);
        }

        if (record?.LocalRecord is null)
        {
            return new Result(item, false, "The selected file is not available locally.", false);
        }

        try
        {
            // The same call the per game picker uses, so the backup chain, the hash check, the
            // signature check and the admin prompt all behave identically. Reimplementing any of that
            // here would be how the two paths drift apart.
            var outcome = await game.UpdateDllAsync(record).ConfigureAwait(false);

            return new Result(item, outcome.Success, outcome.Message, outcome.PromptToRelaunchAsAdmin);
        }
        catch (Exception err)
        {
            // A mass run touches many folders and any one of them can fail in a way UpdateDllAsync does
            // not catch. One game's failure must not end the run for the rest.
            Logger.Error(err);

            return new Result(item, false, err.Message, false);
        }
    }

    /// <summary>
    /// The executor makes no WinRT calls at all.
    /// </summary>
    /// <remarks>
    /// Worth stating because getting this wrong is invisible until it runs. An earlier version called
    /// <see cref="GameManager.GetGameCollection"/> from <c>ApplyOneAsync</c>, which runs after a
    /// <c>ConfigureAwait(false)</c> and so is on a thread pool thread. That returns a WinRT
    /// <c>ICollectionView</c>, which requires the UI thread, and it failed on the first item of every
    /// run with COMException 0x8001010E. The games are now passed in by the caller, which looks them up
    /// before the executor starts.
    ///
    /// If a WinRT call is ever added here, it must be resolved on the caller's thread and passed in the
    /// same way.
    /// </remarks>
    internal static bool TouchesWinRt => false;

    /// <summary>
    /// Builds the end of run report, naming the failures and why the rest succeeded.
    /// </summary>
    /// <remarks>
    /// Failures are listed individually rather than as a count, because each one has a different
    /// remedy: some need administrator rights, some need the file downloaded, some are a game that
    /// has moved on disk. A single "3 failed" line would not tell the user which is which.
    /// </remarks>
    internal static string Summarise(IReadOnlyList<Result> results)
    {
        if (results.Count == 0)
        {
            return "Nothing needed changing.";
        }

        var succeeded = results.Count(x => x.Succeeded);
        var failed = results.Where(x => x.Succeeded == false).ToList();

        if (failed.Count == 0)
        {
            return succeeded == 1
                ? "1 DLL updated."
                : $"{succeeded} DLLs updated.";
        }

        var lines = new List<string>
        {
            succeeded == 0
                ? "Nothing could be updated."
                : $"{succeeded} of {results.Count} updated, {failed.Count} failed.",
        };

        foreach (var failure in failed)
        {
            var reason = string.IsNullOrWhiteSpace(failure.Message)
                ? "unknown reason"
                : failure.Message;

            var remedy = failure.NeedsAdmin ? " Running Kronos as administrator may fix this." : string.Empty;

            lines.Add($"  {failure.Item.GameTitle} ({failure.Item.AssetType}): {reason}.{remedy}");
        }

        lines.Add("Everything that succeeded can be undone from each game's history.");

        return string.Join("\n", lines);
    }
}
