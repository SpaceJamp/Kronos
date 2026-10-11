using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SQLite;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the two DROP INDEX statements in the database migration.
/// </summary>
/// <remarks>
/// An audit of the repository reported these as dead code, on the reasoning that sqlite-net names its
/// indexes {table}_{hash} rather than {table}_{column} - so the names could never match and the statements
/// could be deleted.
///
/// They are not dead, and they are load-bearing. Verified against sqlite-net-e 1.11.0 directly: index
/// names are built as table name + "_" + column name, so an install that predates the snake_case migration
/// has indexes literally called GameAsset_id and GameHistory_game_id, which is exactly what these drop.
///
/// The subtlety is SQLite's behaviour on rename. ALTER TABLE ... RENAME TO retargets an index at the new
/// table but leaves its *name* alone, so after RenameTable the schema has a table called game_asset
/// carrying an index still called GameAsset_id. sqlite-net's CreateTable then creates its own index,
/// game_asset_id, which succeeds regardless - leaving two indexes on the same column, one of them the
/// stale one. Dropping it first is what keeps that from accumulating.
///
/// So the correct outcome of acting on that report would have been to delete working migration code,
/// on the strength of a grep. These tests exist so the next reader - or the next audit - can check the
/// behaviour in about thirty seconds instead of re-deriving it, and so that deleting either statement
/// fails here rather than in somebody's database.
/// </remarks>
public class DatabaseIndexMigrationTests : IDisposable
{
    readonly string _dbPath;
    readonly string _root;

    public DatabaseIndexMigrationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"kronos-dbmigrate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "kronos.db");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    /// <param name="tableName">
    /// The table to list indexes for. Pre-rename the table is called GameAsset, after it is game_asset,
    /// so this cannot be hard-coded to one of them.
    /// </param>
    List<string> IndexNames(SQLiteConnection connection, string tableName = "game_asset")
    {
        return connection
            .Query<SQLiteConnection.ColumnInfo>(
                $"SELECT name FROM sqlite_master WHERE type='index' AND tbl_name = '{tableName}'")
            .Select(c => c.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n) && n.StartsWith("sqlite_autoindex", StringComparison.Ordinal) == false)
            .ToList();
    }

    /// <summary>
    /// Builds the schema a pre-snake_case install had: a PascalCase table with a PascalCase index.
    /// </summary>
    void CreateLegacyInstall(SQLiteConnection connection)
    {
        // Exactly what sqlite-net produced for the old [Table("GameAsset")] / [Indexed] [Column("id")]
        // shape, including the index name it chose.
        connection.Execute("CREATE TABLE IF NOT EXISTS \"GameAsset\" (\"id\" TEXT PRIMARY KEY NOT NULL, \"hash\" TEXT NOT NULL)");
        connection.Execute("CREATE INDEX IF NOT EXISTS \"GameAsset_id\" ON \"GameAsset\" (\"id\")");
    }

    [Fact]
    public void SqliteNamesAutoIndexesByTableAndColumnNotByHash()
    {
        // The premise the audit's report rested on. If this ever fails, the DROP INDEX statements really
        // have become dead code and this file should be deleted rather than believed.
        using var connection = new SQLiteConnection(_dbPath);
        CreateLegacyInstall(connection);

        // Listed against the pre-rename table name, which is what a legacy install actually has.
        Assert.Contains("GameAsset_id", IndexNames(connection, "GameAsset"));
    }

    [Fact]
    public void RenamingATableLeavesItsIndexUnderTheOldName()
    {
        // The fact the whole thing hinges on, and the one that is easy to assume wrongly.
        using var connection = new SQLiteConnection(_dbPath);
        CreateLegacyInstall(connection);

        connection.Execute("ALTER TABLE \"GameAsset\" RENAME TO game_asset");

        var indexes = IndexNames(connection);

        Assert.Contains("GameAsset_id", indexes);
        Assert.DoesNotContain("game_asset_id", indexes);
    }

    [Fact]
    public void WithoutTheDropAStaleIndexIsLeftBehindOnEveryMigration()
    {
        // What the statements are for. Replays the migration's index step in isolation.
        using var connection = new SQLiteConnection(_dbPath);
        CreateLegacyInstall(connection);
        connection.Execute("ALTER TABLE \"GameAsset\" RENAME TO game_asset");

        // No DROP. sqlite-net's CreateTable would then add its own index alongside the stale one.
        Assert.Contains("GameAsset_id", IndexNames(connection));
    }

    [Fact]
    public void TheDropRemovesTheStaleIndexAndLeavesRoomForTheCorrectOne()
    {
        using var connection = new SQLiteConnection(_dbPath);
        CreateLegacyInstall(connection);
        connection.Execute("ALTER TABLE \"GameAsset\" RENAME TO game_asset");

        connection.Execute("DROP INDEX IF EXISTS GameAsset_id");

        Assert.DoesNotContain("GameAsset_id", IndexNames(connection));
    }

    [Fact]
    public void TheDropIsSafeOnAnInstallThatNeverHadTheOldIndex()
    {
        // IF EXISTS, and a current install has no GameAsset_id to drop. Every upgrade runs these, so
        // every upgrade since the rename has taken this path.
        using var connection = new SQLiteConnection(_dbPath);
        connection.Execute("CREATE TABLE game_asset (id TEXT PRIMARY KEY NOT NULL)");

        connection.Execute("DROP INDEX IF EXISTS GameAsset_id");
        connection.Execute("DROP INDEX IF EXISTS GameHistory_game_id");

        Assert.Empty(IndexNames(connection));
    }

    [Fact]
    public void TheMigrationStillContainsBothDropStatements()
    {
        // A direct pin on the two lines, so deleting either one fails a test rather than being noticed
        // by a user with a duplicated index. The behavioural tests above explain why they are needed; this
        // one makes removal impossible by accident.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory!.FullName, "src", "Database.cs"));

        Assert.Contains("DROP INDEX IF EXISTS GameAsset_id", source, StringComparison.Ordinal);
        Assert.Contains("DROP INDEX IF EXISTS GameHistory_game_id", source, StringComparison.Ordinal);
    }
}