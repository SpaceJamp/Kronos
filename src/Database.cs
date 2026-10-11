using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using Kronos.Data;
#if WINDOWS
using Kronos.Data.ManuallyAdded;
#endif
using Nito.AsyncEx;
using SQLite;

#if WINDOWS
using Kronos.Data.BattleNet;
using Kronos.Data.EAApp;
using Kronos.Data.EpicGamesStore;
using Kronos.Data.GOG;
using Kronos.Data.Steam;
using Kronos.Data.UbisoftConnect;
using Kronos.Data.Xbox;
#endif

namespace Kronos;

#if DEBUG
public class SQLiteTableInfo
{
    [Column("name")]
    public string Name { get; set; } = string.Empty;
}
#endif

internal class Database
{
    static Database? _instance;
    internal static Database Instance => _instance ??= new Database();

    internal AsyncLock Mutex { get; init; }
    internal SQLiteAsyncConnection Connection { get; init; }

    public Database()
    {

    void RenameTable(SQLiteConnection connection, string from, string to)
    {
        try
        {
            var tableExists = connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{from}'") > 0;
            if (tableExists)
            {
                connection.Execute($"ALTER TABLE {from} RENAME TO {to}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Debugger.Break();
        }
    }

    void RenameColumn(SQLiteConnection connection, string table, string from, string to)
    {
        try
        {
            var columns = connection.Query<SQLiteConnection.ColumnInfo>($"PRAGMA table_info({table})");

            if (columns.Any(c => c.Name == from))
            {
                connection.Execute($"ALTER TABLE {table} RENAME COLUMN {from} TO {to}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Debugger.Break();
        }
    }

    /// <summary>
    /// Adds a column to a table if it is not already there, and if the table exists at all.
    /// </summary>
    void AddColumn(SQLiteConnection connection, string table, string column, string definition)
    {
        try
        {
            // PRAGMA table_info returns an empty set for a table that does not exist, so this
            // doubles as the "does the table exist" check. Deliberately avoids SQLiteTableInfo,
            // which is only declared in DEBUG builds.
            var columns = connection.Query<SQLiteConnection.ColumnInfo>($"PRAGMA table_info({table})");

            if (columns.Count == 0)
            {
                // CreateTable below will make it with the new column already present.
                return;
            }

            if (columns.Any(c => c.Name == column))
            {
                return;
            }

            connection.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            Debugger.Break();
        }
    }

    // Use a single syncronous connection to make tables
    using (var syncConnection = new SQLiteConnection(Storage.GetDBPath()))
    {
        // Some nonesense migrations as of v1.2.2+
        // Make all tables snake case
        RenameTable(syncConnection, "SteamGame", "steam_game");
        RenameTable(syncConnection, "ManuallyAddedGame", "manually_added_game");
        RenameTable(syncConnection, "GOGGame", "gog_game");
        RenameTable(syncConnection, "eaapp_game", "ea_app_game");
        RenameTable(syncConnection, "EpicGamesStoreGame", "epic_games_store_game");
        RenameTable(syncConnection, "UbisoftConnectGame", "ubisoft_connect_game");
        RenameTable(syncConnection, "XboxGame", "xbox_game");
        RenameTable(syncConnection, "BattleNetGame", "battlenet_game");
        RenameTable(syncConnection, "GameHistory", "game_history");
        RenameTable(syncConnection, "GameAsset", "game_asset");

        // Make all columns snake case
        RenameColumn(syncConnection, "battlenet_game", "StatePlayable", "state_playable");
        RenameColumn(syncConnection, "battlenet_game", "RemoteCoverImage", "remote_cover_image");
        RenameColumn(syncConnection, "battlenet_game", "LauncherId", "launcher_id");
        RenameColumn(syncConnection, "gog_game", "FallbackHeaderUrl", "fallback_header_url");
        RenameColumn(syncConnection, "game_asset", "Hash", "hash");

        // Add the repack flag to every per-library game table. sqlite-net's CreateTable is
        // "CREATE TABLE IF NOT EXISTS", so it will not add a column to a table that already
        // exists, and an existing install would fail with "no such column: is_repack".
        string[] gameTables =
        {
            "steam_game",
            "gog_game",
            "epic_games_store_game",
            "ubisoft_connect_game",
            "xbox_game",
            "manually_added_game",
            "battlenet_game",
            "ea_app_game",
        };

        foreach (var gameTable in gameTables)
        {
            AddColumn(syncConnection, gameTable, "is_repack", "INTEGER NOT NULL DEFAULT 0");
        }

        // Same story for the optional store link on a manually added game, which is what lets
        // a repack borrow the artwork of the game it is a copy of.
        AddColumn(syncConnection, "manually_added_game", "linked_app_id", "TEXT NOT NULL DEFAULT ''");

        // Drop the indexes that predate the snake_case migration. DO NOT DELETE - they are load-bearing.
//
// An audit of this repository reported these two statements as dead code, reasoning that sqlite-net names
// its indexes {table}_{hash} rather than {table}_{column}, so the names could never match. That is not
// how sqlite-net-e 1.11.0 works, and these names are exactly right:
//
//   1. An install from before the migration has indexes named GameAsset_id and GameHistory_game_id,
//      because sqlite-net builds an index name from the table name and the column name.
//   2. RenameTable above has by now run ALTER TABLE ... RENAME TO for both tables. SQLite retargets an
//      index at the renamed table but leaves the index's own name alone, so the schema now has a table
//      called game_asset carrying an index still called GameAsset_id.
//   3. CreateTable<GameAsset>() further down then creates its own game_asset_id, which succeeds either
//      way - leaving two indexes on the same column, one of them stale.
//
// Dropping them here is what prevents that. DatabaseIndexMigrationTests demonstrates each step against a
// real SQLite file, and pins these two lines: deleting either one fails a test rather than quietly
// leaving every user's database carrying a duplicate index.
        syncConnection.Execute("DROP INDEX IF EXISTS GameAsset_id");
        syncConnection.Execute("DROP INDEX IF EXISTS GameHistory_game_id");

        // Debug code to make sure all tables and columns are snake case. This is made to protect adding data to
        // the database that was not intended to go to the database. (eg. you need to add [Column( and [Table( to
        // show your intent for this data to be stored.
#if DEBUG
        var validDbParts = new List<string>()
        {
            "id",
            "platform",
            "title",
            "install",
            "path",
            "cover",
            "image",
            "has",
            "swappable",
            "items",
            "notes",
            "is",
            "hidden",
            "epic",
            "games",
            "store",
            "game",
            "remote",
            "header",
            "history",
            "event",
            "type",
            "asset",
            "steam",
            "state",
            "flags",
            "favourite",
            "gog",
            "ubisoft",
            "connect",
            "xbox",
            "application",
            "battlenet",
            "time",
            "version",
            "manually",
            "added",
            "playable",
            "ea",
            "app",
            "display",
            "icon",
            "fallback",
            "header",
            "url",
            "launcher",
            "hash",
            "repack",
            "linked",
        };

        var hasIssues = false;
        var validRegex = new Regex(@"^[a-z_]+$", RegexOptions.Compiled);
        var tables = syncConnection.Query<SQLiteTableInfo>("SELECT name FROM sqlite_master WHERE type='table'");
        foreach (var table in tables)
        {
            if (validRegex.IsMatch(table.Name) == false)
            {
                Logger.Error($"{table.Name} is an invalid table name. Table names can only be lowercase a-z and underscore.");
                hasIssues = true;
            }

            var tableNameParts = table.Name.Split('_');
            foreach (var tableNamePart in tableNameParts)
            {
                if (validDbParts.Contains(tableNamePart) == false)
                {
                    Logger.Error($"validDbParts list does not contain '{tableNamePart}'.");
                    hasIssues = true;
                }
            }

            // Now for every column in this table we should also check it.
            var columns = syncConnection.Query<SQLiteConnection.ColumnInfo>($"PRAGMA table_info({table.Name})");
            foreach (var column in columns)
            {
                if (validRegex.IsMatch(column.Name) == false)
                {
                    Logger.Error($"{column.Name} in {table.Name} is an invalid column name. Column names can only be lowercase a-z and underscore.");
                    hasIssues = true;
                }

                var columnNameParts = column.Name.Split('_');
                foreach (var columnNamePart in columnNameParts)
                {
                    if (validDbParts.Contains(columnNamePart) == false)
                    {
                        Logger.Error($"validDbParts list does not contain '{columnNamePart}'.");
                        hasIssues = true;
                    }
                }
            }
        }

        if (hasIssues)
        {
            Logger.Error($"You will need to delete {Storage.GetDBPath()} to remove these errors.");
            // If you got here you should go fix this, you likely will have to delete the .db file to prevent it re-appearing.
            // Check your debug output for specific information.
            Debugger.Break();
        }
#endif

        // Create the tables normally.
#if WINDOWS
        try
        {
            syncConnection.CreateTable<GameHistory>();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }
#endif

#if WINDOWS
        try
        {
            syncConnection.CreateTable<SteamGame>();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }


        try
        {
            syncConnection.CreateTable<GOGGame>();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }


        try
        {
            syncConnection.CreateTable<EpicGamesStoreGame>();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }


        try
        {
            syncConnection.CreateTable<UbisoftConnectGame>();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }


        try
        {
            syncConnection.CreateTable<XboxGame>();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }



        try
        {
            syncConnection.CreateTable<BattleNetGame>();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }

        try
        {
            syncConnection.CreateTable<EAAppGame>();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }
#endif

        try
        {
#if WINDOWS
            syncConnection.CreateTable<ManuallyAddedGame>();
#endif
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }

        #if WINDOWS
        try
        {
            syncConnection.CreateTable<GameAsset>();
        }
        catch (Exception err)
        {
            Logger.Error(err);
            Debugger.Break();
        }
#endif

        syncConnection.Close();
    }

    Mutex = new AsyncLock();
    Connection = new SQLiteAsyncConnection(Storage.GetDBPath());
}

public void Init()
{
    // If we didn't get this the database was not created as the above threw an exception
    Logger.Verbose("Database Init");
}

}