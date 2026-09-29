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
/// Selection and the mass upgrade flow for the games page.
/// </summary>
/// <remarks>
/// Split out of <see cref="GameGridPageModel"/> because the flow is a self contained three step
/// interaction, preview then confirm then run, and mixing it into a model that already owns
/// filtering, sorting and view switching made both harder to follow.
///
/// The steps are deliberately three dialogs rather than one. A mass update writes to game folders
/// spread across a disk, so the user sees what would change, confirms, and only then does anything
/// happen. Collapsing that into a single "are you sure" would show them a count and not a plan.
/// </remarks>
public partial class MassUpgradeViewModel : ObservableObject
{
    readonly GameGridPageModel _parent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectionSummary))]
    public partial int SelectedCount { get; private set; }

    [ObservableProperty]
    public partial bool IsRunning { get; private set; }

    /// <summary>Whether the mass update button can be pressed.</summary>
    public bool HasSelection => SelectedCount > 0 && IsRunning == false;

    /// <summary>Short description of the current selection, for the button tooltip.</summary>
    public string SelectionSummary => SelectedCount switch
    {
        0 => "Tick games to update",
        1 => "1 game selected",
        _ => $"{SelectedCount} games selected",
    };

    public MassUpgradeViewModel(GameGridPageModel parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// The games currently ticked.
    /// </summary>
    /// <remarks>
    /// Read from the live collection rather than kept as a separate set, so a game removed by a
    /// refresh cannot linger in the selection and be written to after it has gone.
    /// </remarks>
    internal List<Game> GetSelectedGames()
    {
        var collection = _parent.CurrentCollectionView;

        if (collection is null)
        {
            return new List<Game>();
        }

        return collection.Cast<Game>()
            .Where(x => x.IsSelectedForMassUpdate)
            .ToList();
    }

    /// <summary>
    /// Recomputes the selection count. Called whenever a checkbox changes.
    /// </summary>
    public void RefreshSelection()
    {
        SelectedCount = GetSelectedGames().Count;
    }

    /// <summary>
    /// Ticks every game in the current view, or clears the ticks if all are already ticked.
    /// </summary>
    /// <remarks>
    /// Toggling rather than only ever selecting, because "select all" with no way back short of
    /// opening every dialog again is a trap when the list is two hundred games long.
    /// </remarks>
    [RelayCommand]
    public void ToggleSelectAll()
    {
        var collection = _parent.CurrentCollectionView;
        if (collection is null)
        {
            return;
        }

        var games = collection.Cast<Game>().ToList();
        if (games.Count == 0)
        {
            return;
        }

        var shouldSelect = games.Any(x => x.IsSelectedForMassUpdate == false);

        foreach (var game in games)
        {
            game.IsSelectedForMassUpdate = shouldSelect;
        }

        RefreshSelection();
    }

    /// <summary>
    /// Clears every tick, including ones on games filtered out of the current view.
    /// </summary>
    [RelayCommand]
    public void ClearSelection()
    {
        ClearAllTicks();
        RefreshSelection();
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

        var games = GetSelectedGames();
        if (games.Count == 0)
        {
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
        // rather than a count.
        var preview = new EasyContentDialog(xamlRoot)
        {
            Title = "Update selected games?",
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
        OnPropertyChanged(nameof(HasSelection));

        try
        {
            // The games are collected here, on the UI thread, and passed in. The executor cannot look
            // them up itself: GameManager.GetGameCollection returns a WinRT ICollectionView, and
            // calling it from the executor's continuation, which runs on a thread pool thread after
            // ConfigureAwait(false), throws COMException 0x8001010E. That is what stopped the very
            // first mass update on its first item.
            var selectedGames = GetSelectedGames();

            var results = await MassUpgradeExecutor.ExecuteAsync(
                plan,
                selectedGames,
                ResolveTarget,
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
            OnPropertyChanged(nameof(HasSelection));
            RefreshSelection();
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
    /// Finds the release to write for a plan item.
    /// </summary>
    /// <remarks>
    /// Re-resolved at write time rather than carried in the plan item, so a record that has since been
    /// deleted from the library fails that one item cleanly instead of writing a stale file.
    /// </remarks>
    DLLRecord? ResolveTarget(MassUpgradePlanner.PlanItem item)
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

    async Task ShowInfoAsync(XamlRoot xamlRoot, string title, string message)
    {
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

    /// <summary>
    /// Clears every tick across the whole library, not just the visible page.
    /// </summary>
    internal void ClearAllTicks()
    {
        var collection = GameManager.Instance.GetGameCollection();
        if (collection is null)
        {
            return;
        }

        foreach (var game in collection.Cast<Game>())
        {
            game.IsSelectedForMassUpdate = false;
        }
    }
}
