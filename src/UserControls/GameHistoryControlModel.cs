using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Kronos.Data;

namespace Kronos.UserControls;

public partial class GameHistoryControlModel : ObservableObject
{
    readonly WeakReference<GameHistoryControl> _weakControl;

    public GameHistoryControlModelTranslationProperties TranslationProperties { get; } = new GameHistoryControlModelTranslationProperties();

    /// <summary>
    /// The history rows, newest first.
    /// </summary>
    /// <remarks>
    /// An ObservableCollection filled on the UI thread, rather than a plain List filled from a
    /// background thread. Both parts of the old version were wrong:
    ///
    /// The list was a <see cref="List{T}"/>, whose AddRange raises no CollectionChanged, so the
    /// DataGrid's OneTime binding was already resolved to an empty list and never saw the rows. The
    /// history grid showed "no history" for the life of the dialog no matter what was in the database.
    ///
    /// And it was filled from a Task.Run continuation, so even with a raising collection the
    /// DataGrid would have been handed rows from a thread pool thread, which is a 0x8001010E waiting
    /// to happen the same way Game.cs:1706 was.
    ///
    /// The read is left on a background thread because it is a database query and blocking the UI on
    /// it is its own bug. Only the hand off to the collection is marshalled.
    /// </remarks>
    public ObservableCollection<GameHistory> HistoryRows { get; } = new ObservableCollection<GameHistory>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoaded))]
    public partial bool IsLoading { get; private set; } = true;

    public bool IsLoaded => IsLoading == false;

    public GameHistoryControlModel(GameHistoryControl control, Game game)
    {
        _weakControl = new WeakReference<GameHistoryControl>(control);

        _ = LoadAsync(game);
    }

    /// <summary>
    /// Creates a model with no control attached, for tests and for a caller that only wants the rows.
    /// </summary>
    /// <remarks>
    /// The control reference is only used to keep the dialog alive for as long as the model is, and the
    /// load needs neither it nor a live database, so a null control and a null game are safe here. The
    /// load catches everything and leaves the collection empty.
    /// </remarks>
    internal GameHistoryControlModel()
    {
        _weakControl = new WeakReference<GameHistoryControl>(null!);
    }

    async Task LoadAsync(Game game)
    {
        try
        {
            // Initialised rather than declared and assigned inside the closure, because the compiler
            // cannot prove the closure ran and the catch block can still reach the finally.
            var rows = new List<GameHistory>();

            // The query stays off the UI thread, and only the result is handed over. Doing the whole
            // thing on the UI thread would freeze the dialog for as long as the query takes.
            await Task.Run(async () =>
            {
                var found = await Database.Instance.Connection
                    .Table<GameHistory>()
                    .Where(x => x.GameId == game.ID)
                    .ToListAsync();

                rows = found;
            }).ConfigureAwait(true);

            var ordered = rows.OrderByDescending(x => x.EventTime).ToList();

            // Added on the UI thread, one at a time, so the DataGrid's binding sees each row arrive.
            // This is the whole point of the rewrite: the old plain List raised nothing at all, and the
            // OneTime binding had already resolved to an empty collection.
            foreach (var row in ordered)
            {
                HistoryRows.Add(row);
            }
        }
        catch (Exception err)
        {
            Logger.Error(err);
        }
        finally
        {
            IsLoading = false;
        }
    }
}
