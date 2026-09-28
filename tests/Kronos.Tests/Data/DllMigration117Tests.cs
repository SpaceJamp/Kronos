using System.IO.Compression;
using Kronos.Data;

namespace Kronos.Tests.Data;

/// <summary>
/// Regression tests for the v1.1.7 zip -> raw dll migration in
/// <see cref="DLLManager.CheckDllRecordsForMigration_117"/>.
/// </summary>
/// <remarks>
/// These run against the real storage folder, which for a Debug build is
/// %LOCALAPPDATA%\DLSS Swapper\DEBUG. Each test uses a unique token inside the record's
/// Version/MD5Hash so it cannot collide with another test or with real data, and only ever creates
/// files (never deletes anything it did not create).
/// </remarks>
public class DllMigration117Tests
{
    // Two genuinely swappable types, so GetRecordSimpleType() and DllNameForGameAssetType() both
    // resolve. They use different zip folders, which keeps the two lists independent.
    const GameAssetType _manifestOnlyType = GameAssetType.DLSS;
    const GameAssetType _importedOnlyType = GameAssetType.FSR_31_DX12;

    readonly string _unique = Guid.NewGuid().ToString("N")[..12];

    static DLLRecord CreateRecord(GameAssetType assetType, string token)
    {
        return new DLLRecord
        {
            Version = $"{token}_v1",
            MD5Hash = $"{token}_hash",
            AssetType = assetType,
        };
    }

    /// <summary>
    /// Creates a legacy "{recordType}_zip/{Version}_{MD5Hash}.zip" folder containing the DLL,
    /// exactly as v1.1.6 and earlier stored it. Imported records lived under an "imported_" prefix.
    /// </summary>
    static string CreateLegacyZip(DLLRecord record, bool isImported)
    {
        var recordType = record.GetRecordSimpleType();
        Assert.NotEqual(string.Empty, recordType);

        var zipFolder = Path.Combine(Storage.GetStorageFolder(), $"{(isImported ? "imported_" : string.Empty)}{recordType}_zip");
        Directory.CreateDirectory(zipFolder);

        // The file name has to match CheckDllRecordForMigration_117 exactly.
        var zipPath = Path.Combine(zipFolder, $"{record.Version}_{record.MD5Hash}.zip");

        var dllName = DLLManager.DllNameForGameAssetType(record.AssetType);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry(dllName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write("fake dll contents");
        }

        return zipPath;
    }

    [Fact]
    public void CheckDllRecordsForMigration_117_MigratesManifestRecord()
    {
        var record = CreateRecord(_manifestOnlyType, $"{_unique}_m");
        var zipPath = CreateLegacyZip(record, false);

        DLLManager.CheckDllRecordsForMigration_117(new List<DLLRecord> { record }, null);

        var expectedDllPath = DLLManager.GetExpectedDllFileName(record, false);
        Assert.NotEqual(string.Empty, expectedDllPath);
        Assert.True(File.Exists(expectedDllPath),
            $"Expected the legacy zip to be extracted to {expectedDllPath}.");
        Assert.False(File.Exists(zipPath), "The legacy zip should have been deleted after migration.");
    }

    [Fact]
    public void CheckDllRecordsForMigration_117_MigratesImportedRecord()
    {
        // This is the regression test for the copy/paste bug: the imported loop used to iterate the
        // *manifest* list, so a record that exists only in the imported list was silently never
        // migrated out of its legacy zip folder.
        var record = CreateRecord(_importedOnlyType, $"{_unique}_i");
        var zipPath = CreateLegacyZip(record, true);

        // Deliberately pass an empty manifest list. If the imported loop iterates the wrong list,
        // nothing happens and the assertion below fails.
        DLLManager.CheckDllRecordsForMigration_117(
            new List<DLLRecord>(),
            new List<DLLRecord> { record });

        var expectedDllPath = DLLManager.GetExpectedDllFileName(record, true);
        Assert.NotEqual(string.Empty, expectedDllPath);
        Assert.True(File.Exists(expectedDllPath),
            $"Expected the imported legacy zip to be extracted to {expectedDllPath}. " +
            "If this fails, the imported-record migration loop is iterating the wrong list again.");
        Assert.False(File.Exists(zipPath), "The legacy imported zip should have been deleted after migration.");
    }

    [Fact]
    public void CheckDllRecordsForMigration_117_MigratesManifestAndImportedIndependently()
    {
        // Both lists populated at once: each record must land in its own destination folder.
        var manifestRecord = CreateRecord(_manifestOnlyType, $"{_unique}_bm");
        var importedRecord = CreateRecord(_importedOnlyType, $"{_unique}_bi");

        var manifestZip = CreateLegacyZip(manifestRecord, false);
        var importedZip = CreateLegacyZip(importedRecord, true);

        DLLManager.CheckDllRecordsForMigration_117(
            new List<DLLRecord> { manifestRecord },
            new List<DLLRecord> { importedRecord });

        Assert.True(File.Exists(DLLManager.GetExpectedDllFileName(manifestRecord, false)));
        Assert.True(File.Exists(DLLManager.GetExpectedDllFileName(importedRecord, true)));

        // The two destinations must not be the same folder.
        Assert.NotEqual(
            DLLManager.GetExpectedDllFileName(manifestRecord, false),
            DLLManager.GetExpectedDllFileName(importedRecord, true));

        Assert.False(File.Exists(manifestZip));
        Assert.False(File.Exists(importedZip));
    }

    [Fact]
    public void CheckDllRecordsForMigration_117_LeavesUnmappableAssetTypeAlone()
    {
        // A record whose asset type we cannot map to a record type should be a no-op, not a throw.
        var record = CreateRecord(GameAssetType.Unknown, $"{_unique}_u");

        DLLManager.CheckDllRecordsForMigration_117(new List<DLLRecord> { record }, null);
    }

    [Fact]
    public void CheckDllRecordsForMigration_117_DoesNothingWhenNoLegacyFolderExists()
    {
        // No zip was created for this record, so there is nothing to migrate and no throw.
        var record = CreateRecord(_manifestOnlyType, $"{_unique}_absent");

        DLLManager.CheckDllRecordsForMigration_117(new List<DLLRecord> { record }, null);
    }

    [Fact]
    public void CheckDllRecordsForMigration_117_AcceptsNullImportedList()
    {
        var record = CreateRecord(_manifestOnlyType, $"{_unique}_null");

        DLLManager.CheckDllRecordsForMigration_117(new List<DLLRecord> { record }, null);
    }

    [Fact]
    public void CheckDllRecordsForMigration_117_HandlesEmptyLists()
    {
        DLLManager.CheckDllRecordsForMigration_117(new List<DLLRecord>(), new List<DLLRecord>());
    }
}
