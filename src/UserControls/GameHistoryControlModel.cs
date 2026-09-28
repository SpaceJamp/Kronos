using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kronos.Data;

namespace Kronos.UserControls;

public class GameHistoryControlModel
{
    readonly WeakReference<GameHistoryControl> _weakControl;

    public GameHistoryControlModelTranslationProperties TranslationProperties { get; } = new GameHistoryControlModelTranslationProperties();

    public List<GameHistory> HistoryRows { get; } = new List<GameHistory>();

    public GameHistoryControlModel(GameHistoryControl control, Game game)
    {
        _weakControl = new WeakReference<GameHistoryControl>(control);

        Task.Run(async () =>
        {
            var historyRows = await Database.Instance.Connection.Table<GameHistory>().Where(x => x.GameId == game.ID).ToListAsync();
            HistoryRows.AddRange(historyRows.OrderByDescending(x => x.EventTime));
        });
    }
}
