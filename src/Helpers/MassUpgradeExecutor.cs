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
    /// </param>
    /// <param name="report">Called after each item, for progress.</param>
    /// <param name="cancellation">Stops between items. Never interrupts a file write midway.</param>
    public static async Task<IReadOnlyList<Result>> ExecuteAsync(
        MassUpgradePlanner.Plan plan,
        Func<MassUpgradePlanner.PlanItem, DLLRecord?> resolveTarget,
        Action<Result>? report = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<Result>();

        if (resolveTarget is null)
        {
            return results;
        }

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

            var result = await ApplyOneAsync(item, resolveTarget).ConfigureAwait(false);
            results.Add(result);
            report?.Invoke(result);
        }

        return results;
    }

    static async Task<Result> ApplyOneAsync(
        MassUpgradePlanner.PlanItem item,
        Func<MassUpgradePlanner.PlanItem, DLLRecord?> resolveTarget)
    {
        var game = FindGame(item.GameId);
        if (game is null)
        {
            return new Result(item, false, "The game is no longer in the library.", false);
        }

        var record = resolveTarget(item);
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

    static Game? FindGame(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        var collection = GameManager.Instance.GetGameCollection();

        if (collection is null)
        {
            return null;
        }

        return collection.Cast<Game>()
            .FirstOrDefault(x => string.Equals(x.ID, gameId, StringComparison.OrdinalIgnoreCase));
    }

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
