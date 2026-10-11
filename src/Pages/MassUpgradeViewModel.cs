using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kronos.Data;
using Kronos.Helpers;
using Kronos.UserControls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kronos.Pages;

/// <summary>
/// The mass update flow for the games page: preview, confirm, then apply.
/// </summary>
/// <remarks>
/// Split out of <see cref="GameGridPageModel"/> because the flow is a self contained three step
/// interaction, and mixing it into a model that already owns filtering, sorting and view switching
/// made both harder to follow.
///
/// The steps are deliberately three dialogs rather than one. A mass update writes to game folders
/// spread across a disk, so the user sees what would change, confirms, and only then does anything
/// happen. Collapsing that into a single "are you sure" would show them a count and not a plan.
///
/// This used to require ticking games one at a time. It no longer does: the button updates the whole
/// library, so there is no selection state on <see cref="Game"/>, no checkbox over each tile, and
/// nothing to get stuck. The confirmation dialog is what makes that safe, and it names every game it
/// is about to write to.
/// </remarks>
public partial class MassUpgradeViewModel : ObservableObject
{
    readonly GameGridPageModel _parent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    public partial bool IsRunning { get; private set; }

    /// <summary>Whether the mass update button can be pressed.</summary>
    public bool CanUpdate => IsRunning == false;

    /// <summary>What the button will act on, for its tooltip.</summary>
    public string TargetDescription => "Every game in your library";

    public MassUpgradeViewModel(GameGridPageModel parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Every game the mass update acts on.
    /// </summary>
    /// <remarks>
    /// The whole library, not whatever the search box or the current view is showing.
    ///
    /// A button labelled "Update all" that silently updated only the filtered subset would be the
    /// worse of the two behaviours: the user narrows the list to three games to check something, hits
    /// Update all, and three games change while two hundred others look untouched and un-updated. The
    /// preview dialog names what will be written to, so the scope is visible before anything happens.
    ///
    /// Read live rather than cached, so a game removed by a refresh cannot be written to after it has
    /// gone.
    /// </remarks>
    internal List<Game> GetTargetGames()
    {
        var collection = GameManager.Instance.GetGameCollection();
        if (collection is null)
        {
            return new List<Game>();
        }

        return collection.Cast<Game>().ToList();
    }

    /// <summary>
    /// Preview, confirm, then apply.
    /// </summary>
    /// <remarks>
    /// Each stage can end the interaction early, and every return is a normal outcome rather than a
    /// failure. The dialogs are shown against the page's own XamlRoot; nothing here is a
    /// <see cref="ContentDialog"/> inside another, which is the mistake that crashed the swap flow.
    /// </remarks>
    [RelayCommand]
    public async Task RunMassUpgradeAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var games = GetTargetGames();
        if (games.Count == 0)
        {
            await ShowInfoAsync(GetXamlRoot(), "Nothing to update", "No games were found.").ConfigureAwait(true);
            return;
        }

        var xamlRoot = GetXamlRoot();
        if (xamlRoot is null)
        {
            return;
        }

        var library = GetLibraryRecords();
        var plan = MassUpgradePlanner.PlanForGames(games, library, Settings.Instance.AllowDebugDlls);

        if (plan.HasWork == false)
        {
            await ShowInfoAsync(
                xamlRoot,
                "Nothing to update",
                MassUpgradePlanner.Summarise(plan)).ConfigureAwait(true);

            return;
        }

        // Stage one: the preview. This is the whole reason the flow is split, so it shows the plan
        // rather than a count - and it is also what makes acting on the whole library safe.
        var preview = new EasyContentDialog(xamlRoot)
        {
            Title = "Update every game?",
            CloseButtonText = "Cancel",
            PrimaryButtonText = "Update",
            DefaultButton = ContentDialogButton.Close,
            Content = MassUpgradePlanner.Summarise(plan),
        };

        // No ConfigureAwait here. ContentDialog.ShowAsync returns a WinRT IAsyncOperation, not a Task,
        // so the extension does not apply. Awaiting it directly also keeps the continuation on the UI
        // thread, which matters because the next step starts a background command.
        if (await preview.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // Stage two: do the work, reporting as it goes.
        IsRunning = true;

        // Everything the executor needs is snapshotted here, on the UI thread, before it starts.
        // The executor runs its continuation on a thread pool thread, so it cannot read these itself:
        // the game collection is a WinRT ICollectionView and throws 0x8001010E, and the DLL records
        // are ObservableCollections that DLLManager mutates from the UI thread, so reading them
        // concurrently gives a torn or stale view.
        var targetGames = GetTargetGames();
        var librarySnapshot = GetLibraryRecords();
        var allowDevDlls = Settings.Instance.AllowDebugDlls;

        // Resolving the target up front, on this thread, rather than per item on the executor's. The
        // library does not change while a run is in progress in any way that matters, and resolving
        // once means the preview and what is written cannot disagree.
        var targets = new Dictionary<string, DLLRecord?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in plan.Items.Where(x => x.Kind == MassUpgradePlanner.ActionKind.Upgrade))
        {
            targets[item.GameId + "|" + item.AssetType] =
                MassUpgradePlanner.FindTargetRecord(librarySnapshot, item.AssetType, allowDevDlls);
        }

        try
        {
            var results = await MassUpgradeExecutor.ExecuteAsync(
                plan,
                targetGames,
                item => targets.GetValueOrDefault(item.GameId + "|" + item.AssetType),
                OnProgress).ConfigureAwait(true);

            await ShowInfoAsync(xamlRoot, "Mass update finished", MassUpgradeExecutor.Summarise(results))
                .ConfigureAwait(true);
        }
        catch (Exception err)
        {
            // A mass run touches many folders. An exception escaping here would leave the user with no
            // idea how far it got, which is worse than reporting a partial result.
            Logger.Error(err);

            await ShowInfoAsync(xamlRoot, "Mass update stopped", err.Message).ConfigureAwait(true);
        }
        finally
        {
            IsRunning = false;
        }
    }

    void OnProgress(MassUpgradeExecutor.Result result)
    {
        // Logger.Info takes a single formatted string, not Serilog's message template arguments, so the
        // values are interpolated here.
        Logger.Info(
            $"Mass update {(result.Succeeded ? "succeeded" : "failed")}: " +
            $"{result.Item.GameTitle} ({result.Item.AssetType}) {result.Message}");
    }

    /// <summary>
    /// Finds the release to write for a plan item, on the caller's thread.
    /// </summary>
    /// <remarks>
    /// Only used by the tests and by the preview path. The run itself resolves every target up front
    /// on the UI thread, because the executor's continuation is on a thread pool thread and reading
    /// DLLManager's ObservableCollections from there races the UI thread's own mutations of them.
    /// </remarks>
    internal DLLRecord? ResolveTarget(MassUpgradePlanner.PlanItem item)
    {
        return MassUpgradePlanner.FindTargetRecord(
            GetLibraryRecords(),
            item.AssetType,
            Settings.Instance.AllowDebugDlls);
    }

    /// <summary>
    /// Every DLL record the library knows about, across all asset types.
    /// </summary>
    /// <remarks>
    /// Built the same way <c>LibraryPageModel</c> builds its own combined list, by adding up the
    /// per type collections, because DLLManager exposes them individually and has no combined getter.
    /// Undownloaded records are included here on purpose: the planner filters them, and filtering
    /// earlier would hide the "cannot be updated" lines from the preview.
    /// </remarks>
    internal static IReadOnlyList<DLLRecord> GetLibraryRecords()
    {
        var all = new List<DLLRecord>();
        all.AddRange(DLLManager.Instance.DLSSRecords);
        all.AddRange(DLLManager.Instance.DLSSGRecords);
        all.AddRange(DLLManager.Instance.DLSSDRecords);
        all.AddRange(DLLManager.Instance.FSR31DX12Records);
        all.AddRange(DLLManager.Instance.FSR31VKRecords);
        all.AddRange(DLLManager.Instance.XeSSRecords);
        all.AddRange(DLLManager.Instance.XeSSFGRecords);
        all.AddRange(DLLManager.Instance.XeSSDX11Records);

        return all;
    }

    async Task ShowInfoAsync(XamlRoot? xamlRoot, string title, string message)
    {
        if (xamlRoot is null)
        {
            return;
        }

        var dialog = new EasyContentDialog(xamlRoot)
        {
            Title = title,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            Content = message,
        };

        await dialog.ShowAsync();
    }

    XamlRoot? GetXamlRoot()
    {
        return _parent.XamlRoot;
    }
}
