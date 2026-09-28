using Chronos.Data;

namespace Chronos.Tests.Data;

/// <summary>
/// Tests for Game.GetPathsNeedingBackup, the decision that protects a game's original dll before
/// a swap overwrites it.
/// </summary>
/// <remarks>
/// These create real files under the test storage folder because the decision depends on whether a
/// ".dlsss" backup file actually exists on disk, not just on what is recorded. Every dll gets a
/// unique name so tests cannot interfere with each other.
/// </remarks>
public class BackupDecisionTests : IDisposable
{
    readonly string _dir;

    public BackupDecisionTests()
    {
        _dir = Path.Combine(Storage.GetStorageFolder(), "backup_decision_tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    string MakeDll(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "original");
        return path;
    }

    void MakeBackupFile(string dllPath)
    {
        File.WriteAllText(dllPath + ".dlsss", "original");
    }

    static GameAsset Asset(GameAssetType type, string path) => new() { Id = "test", AssetType = type, Path = path };

    [Fact]
    public void BacksUpEverythingWhenNoBackupExists()
    {
        var a = MakeDll("a.dll");
        var b = MakeDll("b.dll");

        var needed = Game.GetPathsNeedingBackup(
            new[] { Asset(GameAssetType.DLSS, a), Asset(GameAssetType.DLSS, b) },
            Array.Empty<GameAsset>(),
            GameAssetType.DLSS_BACKUP);

        Assert.Equal(2, needed.Count);
    }

    [Fact]
    public void BacksUpTheUnbackedDllWhenAnotherIsAlreadyBackedUp()
    {
        // THE REGRESSION TEST. Two DLSS dlls in one game, only the first has a backup. The old code
        // asked "do we have any backups at all?", saw yes, and skipped backing up b.dll entirely.
        // The swap then overwrote b.dll and its original was gone for good.
        var backedUp = MakeDll("one.dll");
        var notBackedUp = MakeDll("two.dll");
        MakeBackupFile(backedUp);

        var needed = Game.GetPathsNeedingBackup(
            new[] { Asset(GameAssetType.DLSS, backedUp), Asset(GameAssetType.DLSS, notBackedUp) },
            new[] { Asset(GameAssetType.DLSS_BACKUP, backedUp + ".dlsss") },
            GameAssetType.DLSS_BACKUP);

        Assert.DoesNotContain(backedUp, needed);
        Assert.Contains(notBackedUp, needed);
    }

    [Fact]
    public void BacksUpWhenTheRecordExistsButTheFileIsMissing()
    {
        // A recorded backup whose file has been deleted or cleaned up cannot be used to reset, so
        // the original must be preserved again.
        var dll = MakeDll("recordonly.dll");

        var needed = Game.GetPathsNeedingBackup(
            new[] { Asset(GameAssetType.DLSS, dll) },
            new[] { Asset(GameAssetType.DLSS_BACKUP, dll + ".dlsss") },
            GameAssetType.DLSS_BACKUP);

        Assert.Contains(dll, needed);
    }

    [Fact]
    public void BacksUpWhenTheFileExistsButNoRecordDoes()
    {
        // The orphan case: a backup file on disk with nothing pointing at it. The old code saw the
        // file, skipped creating a record, and the record-less backup was then dropped by the
        // DELETE + re-INSERT at the end of the swap, leaving an unusable file behind.
        var dll = MakeDll("fileonly.dll");
        MakeBackupFile(dll);

        var needed = Game.GetPathsNeedingBackup(
            new[] { Asset(GameAssetType.DLSS, dll) },
            Array.Empty<GameAsset>(),
            GameAssetType.DLSS_BACKUP);

        Assert.Contains(dll, needed);
    }

    [Fact]
    public void SkipsWhenBothTheFileAndTheRecordExist()
    {
        var dll = MakeDll("complete.dll");
        MakeBackupFile(dll);

        var needed = Game.GetPathsNeedingBackup(
            new[] { Asset(GameAssetType.DLSS, dll) },
            new[] { Asset(GameAssetType.DLSS_BACKUP, dll + ".dlsss") },
            GameAssetType.DLSS_BACKUP);

        Assert.Empty(needed);
    }

    [Fact]
    public void IgnoresBackupRecordsOfOtherAssetTypes()
    {
        // A DLSS_G backup must not convince us that a DLSS dll is already preserved.
        var dll = MakeDll("otherstype.dll");
        MakeBackupFile(dll);

        var needed = Game.GetPathsNeedingBackup(
            new[] { Asset(GameAssetType.DLSS, dll) },
            new[] { Asset(GameAssetType.DLSS_G_BACKUP, dll + ".dlsss") },
            GameAssetType.DLSS_BACKUP);

        Assert.Contains(dll, needed);
    }

    [Fact]
    public void ComparesPathsCaseInsensitively()
    {
        // Windows paths are case insensitive, and a record saved with different casing than the
        // on-disk file must still count as the same dll.
        var dll = MakeDll("CaseTest.dll");
        MakeBackupFile(dll);

        var needed = Game.GetPathsNeedingBackup(
            new[] { Asset(GameAssetType.DLSS, dll) },
            new[] { Asset(GameAssetType.DLSS_BACKUP, dll.ToUpperInvariant() + ".DLSSS") },
            GameAssetType.DLSS_BACKUP);

        Assert.Empty(needed);
    }

    [Fact]
    public void IgnoresRecordsWithNoPath()
    {
        var dll = MakeDll("hasnullsibling.dll");

        var needed = Game.GetPathsNeedingBackup(
            new[] { Asset(GameAssetType.DLSS, dll) },
            new[] { Asset(GameAssetType.DLSS_BACKUP, string.Empty), Asset(GameAssetType.DLSS_BACKUP, "   ") },
            GameAssetType.DLSS_BACKUP);

        Assert.Contains(dll, needed);
    }

    [Fact]
    public void ReturnsEmptyForNoRecords()
    {
        var needed = Game.GetPathsNeedingBackup(
            Array.Empty<GameAsset>(),
            Array.Empty<GameAsset>(),
            GameAssetType.DLSS_BACKUP);

        Assert.Empty(needed);
    }
}
