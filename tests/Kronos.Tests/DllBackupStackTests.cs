using System;
using System.IO;
using System.Linq;
using Kronos.Data;
using Kronos.Helpers;

namespace Kronos.Tests;

/// <summary>
/// Tests for the versioned backup chain, against a real temporary directory rather than a mocked
/// file system, because the whole point is what happens to files on disk over a sequence of swaps.
/// </summary>
/// <remarks>
/// The behaviour being protected is one that used not to exist. There was a single <c>.dlsss</c>
/// backup per dll and <c>Game.ResetDllAsync</c> moved it back over the dll, consuming it. A second
/// reset then had nothing to restore, and the next swap captured the swapped file as the new
/// "original", so the runtime the game shipped with was gone for good.
///
/// The tests that matter most are the ones that perform a whole sequence, because each individual
/// step looks correct on its own. The bug only showed up across three or more.
/// </remarks>
public class DllBackupStackTests : IDisposable
{
    readonly string _root;

    public DllBackupStackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kronos_bakstack_tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    string DllPath => Path.Combine(_root, "nvngx_dlss.dll");

    /// <summary>
    /// Writes a file whose content identifies it, since the stack logic only ever cares about
    /// content differing, not about what the bytes are.
    /// </summary>
    string WriteFile(string path, string content)
    {
        File.WriteAllText(path, content);
        return path;
    }

    // ---- Chain paths ----

    [Fact]
    public void ChainPositionsStartAtOne()
    {
        Assert.Equal(DllPath + ".kronosbak1", DllBackupStack.GetStackedBackupPath(DllPath, 1));
    }

    [Fact]
    public void AnInvalidChainPositionIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DllBackupStack.GetStackedBackupPath(DllPath, 0));
    }

    [Fact]
    public void AnUnbackedDllWantsPositionOne()
    {
        WriteFile(DllPath, "original");

        Assert.Equal(1, DllBackupStack.GetNextBackupIndex(DllPath));
    }

    [Fact]
    public void EachSwapAddsAnEntryRatherThanOverwriting()
    {
        WriteFile(DllPath, "original");
        WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 1), "original");

        Assert.Equal(2, DllBackupStack.GetNextBackupIndex(DllPath));

        WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 2), "swapped-once");

        Assert.Equal(3, DllBackupStack.GetNextBackupIndex(DllPath));
    }

    [Fact]
    public void ExistingChainEntriesAreFoundOldestFirst()
    {
        WriteFile(DllPath, "current");
        WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 1), "original");
        WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 2), "swapped-once");
        WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 3), "swapped-twice");

        var paths = DllBackupStack.GetStackedBackupPaths(DllPath);

        // The ordering is what makes entry 1 the original, so it has to be by number and not by
        // whatever order the file system happened to return.
        Assert.Equal(3, paths.Count);
        Assert.EndsWith(".kronosbak1", paths[0]);
        Assert.EndsWith(".kronosbak2", paths[1]);
        Assert.EndsWith(".kronosbak3", paths[2]);
    }

    [Fact]
    public void TheLegacyBackupIsNotMistakenForAChainEntry()
    {
        // A .dlsss from a previous version must not be picked up as a chain entry, or its index would
        // be unparseable and the whole chain would be misread.
        WriteFile(DllPath, "current");
        WriteFile(DllPath + DllBackupStack.LegacyBackupSuffix, "legacy");
        WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 1), "original");

        var paths = DllBackupStack.GetStackedBackupPaths(DllPath);

        Assert.Single(paths);
        Assert.EndsWith(".kronosbak1", paths[0]);
    }

    [Fact]
    public void FilesThatMerelyShareThePrefixAreIgnored()
    {
        // A game's directory can hold other files, and a stray "nvngx_dlss.dll.kronosbak-old" must not
        // be treated as an entry, since its position cannot be determined.
        WriteFile(DllPath, "current");
        WriteFile(DllPath + ".kronosbak-old", "junk");
        WriteFile(DllPath + ".kronosbakxyz", "junk");

        Assert.Empty(DllBackupStack.GetStackedBackupPaths(DllPath));
    }

    [Fact]
    public void AMissingDllHasNoChain()
    {
        Assert.Empty(DllBackupStack.GetStackedBackupPaths(Path.Combine(_root, "does-not-exist.dll")));
    }

    [Fact]
    public void AnEmptyPathIsHandledRatherThanThrowing()
    {
        Assert.Empty(DllBackupStack.GetStackedBackupPaths(string.Empty));
    }

    // ---- Undo ----

    [Fact]
    public void ThereIsNothingToUndoWithoutABackup()
    {
        WriteFile(DllPath, "current");

        Assert.Null(DllBackupStack.GetUndoPath(DllPath));
    }

    [Fact]
    public void UndoStepsBackOneSwap()
    {
        // The newest entry holds the state immediately before the last swap, so that is the right
        // thing to return to by default.
        WriteFile(DllPath, "swapped-twice");
        WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 1), "original");
        WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 2), "swapped-once");

        Assert.EndsWith(".kronosbak2", DllBackupStack.GetUndoPath(DllPath)!);
    }

    [Fact]
    public void UndoCanWalkAllTheWayBackToTheOriginal()
    {
        // THE REGRESSION. With the old single consumed .dlsss there was no way to get back to the
        // original after two swaps, because the first reset had already used it up.
        WriteFile(DllPath, "swapped-twice");
        var first = WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 1), "original");
        var second = WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 2), "swapped-once");

        // Step back to the state before the last swap, twice, and the original is still there.
        File.Copy(DllBackupStack.GetUndoPath(DllPath)!, DllPath, true);
        File.Delete(second);
        File.Copy(DllBackupStack.GetUndoPath(DllPath)!, DllPath, true);

        Assert.Equal("original", File.ReadAllText(DllPath));
        Assert.True(File.Exists(first));
    }

    [Fact]
    public void RepeatedRestoresDoNotConsumeTheChain()
    {
        // ResetDllAsync used to move the backup over the dll, so a second reset had nothing to work
        // with. Copying leaves every entry in place.
        WriteFile(DllPath, "swapped");
        var backup = WriteFile(DllBackupStack.GetStackedBackupPath(DllPath, 1), "original");

        File.Copy(backup, DllPath, true);
        Assert.True(File.Exists(backup), "Restoring must not consume the backup.");

        File.Copy(backup, DllPath, true);
        Assert.True(File.Exists(backup), "Restoring twice must still leave the backup in place.");
    }

    // ---- Deciding whether a backup is needed ----

    [Fact]
    public void AFileWithNoBackupNeedsOne()
    {
        Assert.True(DllBackupStack.NeedsBackup("hashA", Array.Empty<string>(), DllPath + ".dlsss"));
    }

    [Fact]
    public void AFileAlreadyInTheChainDoesNotNeedAnother()
    {
        var current = WriteFile(DllPath, "original");
        var chain = new[] { DllBackupStack.GetStackedBackupPath(DllPath, 1) };
        File.Copy(current, chain[0]);

        var hash = DllBackupStack.DescribeBackup(chain[0], 1).Hash;

        Assert.False(DllBackupStack.NeedsBackup(hash, chain, DllPath + ".dlsss"));
    }

    [Fact]
    public void AFileThatChangedSinceTheLastBackupNeedsAnother()
    {
        // This is the case that used to lose the game's original. A file can be on disk and already
        // have a backup while still having been swapped since, in which case backing it up again would
        // capture the swapped version as though it were the original.
        WriteFile(DllPath, "swapped");
        var chain = new[] { DllBackupStack.GetStackedBackupPath(DllPath, 1) };
        WriteFile(chain[0], "original");

        var currentHash = DllBackupStack.DescribeBackup(DllPath, 0).Hash;
        var chainHash = DllBackupStack.DescribeBackup(chain[0], 1).Hash;

        Assert.NotEqual(chainHash, currentHash);
        Assert.True(DllBackupStack.NeedsBackup(currentHash, chain, DllPath + ".dlsss"));
    }

    [Fact]
    public void ALegacyBackupOfTheSameContentCountsAsABaseline()
    {
        // An install carried over from a previous version already has its baseline, and must not be
        // backed up a second time.
        WriteFile(DllPath, "original");
        var legacy = WriteFile(DllPath + DllBackupStack.LegacyBackupSuffix, "original");

        var hash = DllBackupStack.DescribeBackup(legacy, 1).Hash;

        Assert.False(DllBackupStack.NeedsBackup(hash, Array.Empty<string>(), legacy));
    }

    [Fact]
    public void AnUnknownHashIsBackedUpRatherThanSkipped()
    {
        // Wrong in this direction costs a duplicate file. Wrong the other way loses the original.
        Assert.True(DllBackupStack.NeedsBackup(string.Empty, Array.Empty<string>(), DllPath + ".dlsss"));
    }

    [Fact]
    public void ABackupRecordWithNoFileIsIgnored()
    {
        // The record exists in the database but the file is gone, so it cannot be used to restore and
        // must not suppress taking a fresh backup.
        var missing = DllBackupStack.GetStackedBackupPath(DllPath, 1);
        var current = WriteFile(DllPath, "original");
        var hash = DllBackupStack.DescribeBackup(current, 0).Hash;

        Assert.True(DllBackupStack.NeedsBackup(hash, new[] { missing }, DllPath + ".dlsss"));
    }

    // ---- Describing and labelling ----

    [Fact]
    public void AMissingFileIsDescribedWithEmptyMetadata()
    {
        // A backup that cannot be described is still a usable backup. Losing it because its metadata
        // could not be read would be the wrong trade.
        var entry = DllBackupStack.DescribeBackup(Path.Combine(_root, "nope.dll"), 1);

        Assert.Equal(string.Empty, entry.Version);
        Assert.Equal(string.Empty, entry.Hash);
        Assert.Equal(1, entry.Index);
    }

    [Fact]
    public void RestoringToAnOlderRuntimeIsLabelledADowngrade()
    {
        // The UI uses this so a user can see they are about to step back to an older runtime before
        // they do it, which is the same concern as the swap warning.
        Assert.Equal(
            SwapVersionAdvisor.Advice.Downgrade,
            DllBackupStack.GetRestoreAdvice("2.5.0.0", "2.0.0.0"));
    }

    [Fact]
    public void RestoringToTheSameOrANewerRuntimeIsNotADowngrade()
    {
        Assert.Equal(
            SwapVersionAdvisor.Advice.NoChange,
            DllBackupStack.GetRestoreAdvice("2.5.0.0", "2.5.0.0"));

        Assert.Equal(
            SwapVersionAdvisor.Advice.Upgrade,
            DllBackupStack.GetRestoreAdvice("2.0.0.0", "2.5.0.0"));
    }

    [Fact]
    public void RestoringToAnUnknownVersionIsUnlabelledRatherThanGuessed()
    {
        Assert.Null(DllBackupStack.GetRestoreAdvice("2.5.0.0", "unknown"));
    }
}

/// <summary>
/// Tests for the "which dlls still need a backup" decision, which decides what gets preserved before
/// anything is overwritten.
/// </summary>
public class GetPathsNeedingBackupTests : IDisposable
{
    readonly string _root;

    public GetPathsNeedingBackupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kronos_needsbackup_tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    [Fact]
    public void ATrackedBackupOnDiskSuppressesTheBackup()
    {
        var dll = Path.Combine(_root, "nvngx_dlss.dll");
        File.WriteAllText(dll, "original");
        var chain = DllBackupStack.GetStackedBackupPath(dll, 1);
        File.Copy(dll, chain);

        var records = new[] { new GameAsset { Path = dll, AssetType = GameAssetType.DLSS } };
        var assets = new[] { new GameAsset { Path = chain, AssetType = GameAssetType.DLSS_BACKUP } };

        Assert.Empty(Game.GetPathsNeedingBackup(records, assets, GameAssetType.DLSS_BACKUP));
    }

    [Fact]
    public void ATrackedBackupWhoseFileIsMissingStillNeedsOne()
    {
        // A record with no file cannot be used to restore, so it must not count as a baseline.
        var dll = Path.Combine(_root, "nvngx_dlss.dll");
        File.WriteAllText(dll, "original");
        var chain = DllBackupStack.GetStackedBackupPath(dll, 1);

        var records = new[] { new GameAsset { Path = dll, AssetType = GameAssetType.DLSS } };
        var assets = new[] { new GameAsset { Path = chain, AssetType = GameAssetType.DLSS_BACKUP } };

        Assert.Single(Game.GetPathsNeedingBackup(records, assets, GameAssetType.DLSS_BACKUP));
    }

    [Fact]
    public void AFileOnDiskWithNoRecordStillNeedsOne()
    {
        // The original bug this replaced: a file with no record was orphaned by the database rewrite,
        // so a backup was taken but never recorded and could not be restored.
        var dll = Path.Combine(_root, "nvngx_dlss.dll");
        File.WriteAllText(dll, "original");
        var chain = DllBackupStack.GetStackedBackupPath(dll, 1);
        File.Copy(dll, chain);

        var records = new[] { new GameAsset { Path = dll, AssetType = GameAssetType.DLSS } };

        Assert.Single(Game.GetPathsNeedingBackup(records, Array.Empty<GameAsset>(), GameAssetType.DLSS_BACKUP));
    }

    [Fact]
    public void ALegacyBackupOnDiskAndTrackedSuppressesTheBackup()
    {
        // An install carried over from a previous version must not re-back-up what it already has.
        var dll = Path.Combine(_root, "nvngx_dlss.dll");
        File.WriteAllText(dll, "original");
        var legacy = dll + DllBackupStack.LegacyBackupSuffix;
        File.Copy(dll, legacy);

        var records = new[] { new GameAsset { Path = dll, AssetType = GameAssetType.DLSS } };
        var assets = new[] { new GameAsset { Path = legacy, AssetType = GameAssetType.DLSS_BACKUP } };

        Assert.Empty(Game.GetPathsNeedingBackup(records, assets, GameAssetType.DLSS_BACKUP));
    }

    [Fact]
    public void EachDllIsDecidedIndependently()
    {
        // The original bug: a single "do we have any backups" question, so one backed up dll let
        // another be overwritten with no backup at all.
        var backedUp = Path.Combine(_root, "a.dll");
        var notBackedUp = Path.Combine(_root, "b.dll");
        File.WriteAllText(backedUp, "original-a");
        File.WriteAllText(notBackedUp, "original-b");
        var chain = DllBackupStack.GetStackedBackupPath(backedUp, 1);
        File.Copy(backedUp, chain);

        var records = new[]
        {
            new GameAsset { Path = backedUp, AssetType = GameAssetType.DLSS },
            new GameAsset { Path = notBackedUp, AssetType = GameAssetType.DLSS },
        };
        var assets = new[] { new GameAsset { Path = chain, AssetType = GameAssetType.DLSS_BACKUP } };

        var needing = Game.GetPathsNeedingBackup(records, assets, GameAssetType.DLSS_BACKUP);

        Assert.Single(needing);
        Assert.Contains(notBackedUp, needing);
    }

    [Fact]
    public void ARecordWithNoPathIsSkipped()
    {
        var records = new[] { new GameAsset { Path = string.Empty, AssetType = GameAssetType.DLSS } };

        Assert.Empty(Game.GetPathsNeedingBackup(records, Array.Empty<GameAsset>(), GameAssetType.DLSS_BACKUP));
    }
}
