using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Kronos.Data;
using Kronos.Extensions;

namespace Kronos.Helpers;

/// <summary>
/// A versioned chain of backups for one DLL, so a swap can be undone rather than merely reversed to
/// whatever happened to be on disk when the feature arrived.
/// </summary>
/// <remarks>
/// The single <c>.dlsss</c> backup this replaces was written once and then consumed by
/// <c>Game.ResetDllAsync</c>, which moved it back over the dll. That has two consequences. A second
/// reset had nothing to restore, and a second swap after a reset re-snapshotted the swapped file as
/// the new "original", so the shipped runtime was gone for good. Chaining fixes both: the baseline is
/// never overwritten, and any number of previous states can be stepped back through.
///
/// Every method here is pure or takes its paths as arguments, so the rules can be tested against a
/// temporary directory rather than a real game install.
/// </remarks>
public static class DllBackupStack
{
    /// <summary>
    /// The original single-backup suffix, still written for backwards compatibility.
    /// </summary>
    /// <remarks>
    /// An install that already has a <c>.dlsss</c> from a previous version keeps working. The first
    /// swap on such an install adopts the existing file as the bottom of the chain rather than
    /// replacing it, so nothing is thrown away.
    /// </remarks>
    public const string LegacyBackupSuffix = ".dlsss";

    /// <summary>
    /// Suffix for a numbered entry in the chain, e.g. <c>.kronosbak1</c>.
    /// </summary>
    public const string BackupSuffixPrefix = ".kronosbak";

    /// <summary>
    /// Builds the backup path for a given position in the chain, 1 being the oldest.
    /// </summary>
    public static string GetStackedBackupPath(string dllPath, int index)
    {
        if (index < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "Backup chain positions start at 1.");
        }

        return dllPath + BackupSuffixPrefix + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Every existing chain entry for a DLL, oldest first.
    /// </summary>
    /// <remarks>
    /// Scans for <c>.kronosbakN</c> paths rather than trusting a count, because entries can be
    /// deleted from the middle by an uninstall or a clean up. Returns them in numeric order, which is
    /// the order they were created in, so index 1 is always the original file.
    /// </remarks>
    public static IReadOnlyList<string> GetStackedBackupPaths(string dllPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath))
        {
            return Array.Empty<string>();
        }

        var directory = Path.GetDirectoryName(dllPath);
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory) == false)
        {
            return Array.Empty<string>();
        }

        var fileName = Path.GetFileName(dllPath);

        return Directory.GetFiles(directory, fileName + BackupSuffixPrefix + "*")
            .Where(path => TryGetChainIndex(path, fileName, out _))
            .OrderBy(path => GetChainIndex(path, fileName))
            .ToList();
    }

    /// <summary>
    /// Extracts the numeric position from a chain entry's file name.
    /// </summary>
    /// <remarks>
    /// The suffix is taken from the file name rather than by counting back from the end of the full
    /// path, because the full path's length tells you nothing useful here and an off-by-one in that
    /// arithmetic silently rejects every entry, leaving the chain looking empty.
    /// </remarks>
    static bool TryGetChainIndex(string backupPath, string dllFileName, out int index)
    {
        index = 0;

        var backupFileName = Path.GetFileName(backupPath);
        var expectedPrefixLength = dllFileName.Length + BackupSuffixPrefix.Length;

        if (backupFileName.Length <= expectedPrefixLength)
        {
            return false;
        }

        if (backupFileName.StartsWith(dllFileName + BackupSuffixPrefix, StringComparison.Ordinal) == false)
        {
            return false;
        }

        return int.TryParse(
            backupFileName[expectedPrefixLength..],
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out index);
    }

    static int GetChainIndex(string backupPath, string dllFileName)
    {
        return TryGetChainIndex(backupPath, dllFileName, out var index) ? index : int.MaxValue;
    }

    /// <summary>
    /// The suffix used by builds from before the backup chain existed.
    /// </summary>
    public const string LegacySuffix = ".dlsss";

    /// <summary>
    /// Recovers the DLL path a backup belongs to, or null if the path is not a backup at all.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="GetStackedBackupPath"/> and of the legacy <c>.dlsss</c> suffix.
    /// Callers that map a tracked backup record back to the live DLL used to strip the literal
    /// string ".dlsss" instead, which is a no-op against a ".kronosbakN" path - the record resolves
    /// to itself, the primary lookup finds nothing, and reset gives up with "repair your game
    /// manually" for every DLL swapped by the current build. Keeping both shapes here means the
    /// suffix knowledge lives in one place.
    /// </remarks>
    public static string? GetPrimaryPathFromBackup(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
        {
            return null;
        }

        if (backupPath.EndsWith(LegacySuffix, StringComparison.OrdinalIgnoreCase))
        {
            return backupPath[..^LegacySuffix.Length];
        }

        var backupFileName = Path.GetFileName(backupPath);

        // LastIndexOf, not the trailing digits: for a name like "foo.kronosbak2.dll.kronosbak1" the
        // boundary is the final marker, and scanning back from the end finds it without having to
        // know how many digits the index has.
        var markerIndex = backupFileName.LastIndexOf(BackupSuffixPrefix, StringComparison.Ordinal);
        if (markerIndex <= 0)
        {
            return null;
        }

        // Everything after the marker must be the chain index and nothing else, otherwise this is
        // some unrelated file that happens to contain ".kronosbak" in its name.
        var indexText = backupFileName[(markerIndex + BackupSuffixPrefix.Length)..];
        if (indexText.Length == 0 ||
            int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out _) == false)
        {
            return null;
        }

        var directory = Path.GetDirectoryName(backupPath);
        var dllFileName = backupFileName[..markerIndex];

        return string.IsNullOrEmpty(directory) ? dllFileName : Path.Combine(directory, dllFileName);
    }

    /// <summary>
    /// The position a new backup should be written at.
    /// </summary>
    /// <remarks>
    /// One past the highest existing entry, so the chain grows rather than overwriting. Returns 1 for
    /// a dll that has never been backed up.
    /// </remarks>
    public static int GetNextBackupIndex(string dllPath)
    {
        var existing = GetStackedBackupPaths(dllPath);

        return existing.Count == 0 ? 1 : GetChainIndex(existing[^1], Path.GetFileName(dllPath)) + 1;
    }

    /// <summary>
    /// Which backup should be restored to undo the most recent swap.
    /// </summary>
    /// <remarks>
    /// The newest entry, which holds the file as it was immediately before the last swap. Returns
    /// null when there is no chain, so callers do not have to distinguish "no backup" from "backup at
    /// index zero".
    ///
    /// Restoring the newest entry is the right default even though it is not the original. A user who
    /// swapped A then B then C wants to get back to B, and the chain exists so that getting all the
    /// way back to A is also possible rather than impossible.
    /// </remarks>
    public static string? GetUndoPath(string dllPath)
    {
        var existing = GetStackedBackupPaths(dllPath);

        return existing.Count == 0 ? null : existing[^1];
    }

    /// <summary>
    /// Records a description of a chain entry, so history can name what is being restored.
    /// </summary>
    /// <remarks>
    /// Held in memory and mirrored into the asset list by the caller. The version is taken from the
    /// file's own version resource, which is what determines whether restoring it is an upgrade or a
    /// downgrade, so storing it avoids re-reading the file after it has been moved.
    /// </remarks>
    public readonly record struct BackupEntry(string Path, string Version, string Hash, int Index);

    /// <summary>
    /// Reads the version and hash of a file for storage alongside its backup.
    /// </summary>
    /// <remarks>
    /// Returns an entry with empty strings when the file cannot be read, rather than throwing. A
    /// backup that cannot be described is still a usable backup, and losing it because its metadata
    /// could not be read would be the wrong trade.
    /// </remarks>
    public static BackupEntry DescribeBackup(string path, int index)
    {
        try
        {
            if (File.Exists(path) == false)
            {
                return new BackupEntry(path, string.Empty, string.Empty, index);
            }

            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);

            return new BackupEntry(path, info.GetFormattedFileVersion(), info.GetMD5Hash(), index);
        }
        catch (Exception err)
        {
            Logger.Error(err);

            return new BackupEntry(path, string.Empty, string.Empty, index);
        }
    }

    /// <summary>
    /// Works out which of a dll's existing files still need their first backup taken.
    /// </summary>
    /// <remarks>
    /// A dll needs a backup when it is on disk and the chain does not already hold its current
    /// content. The hash check matters because a file can be on disk and still have been swapped since
    /// the chain was last written, in which case backing it up again would capture the swapped version
    /// as though it were the original.
    ///
    /// The legacy <c>.dlsss</c> file is also honoured, so an install carried over from a previous
    /// version is recognised as already having a baseline rather than being backed up a second time.
    /// </remarks>
    /// <param name="currentHash">Hash of the file as it is on disk right now.</param>
    /// <param name="stackedBackupPaths">Existing chain entries for this dll.</param>
    /// <param name="legacyBackupPath">The <c>.dlsss</c> path for this dll, which may not exist.</param>
    public static bool NeedsBackup(string currentHash, IEnumerable<string> stackedBackupPaths, string legacyBackupPath)
    {
        if (string.IsNullOrWhiteSpace(currentHash))
        {
            // Cannot tell what is on disk, so preserve it. Being wrong in this direction costs a
            // duplicate backup; being wrong the other way loses the game's original file.
            return true;
        }

        foreach (var path in stackedBackupPaths)
        {
            if (File.Exists(path) == false)
            {
                continue;
            }

            if (string.Equals(DescribeBackup(path, 0).Hash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // A legacy .dlsss that hashes to the current content is a genuine baseline for this install.
        if (File.Exists(legacyBackupPath))
        {
            if (string.Equals(DescribeBackup(legacyBackupPath, 0).Hash, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether restoring a backup would leave the game on a different version than it is now.
    /// </summary>
    /// <remarks>
    /// Used to label an undo in the UI, so a user can see at a glance that they are about to step back
    /// to an older runtime. Returns null when the versions cannot be compared, which the UI renders as
    /// "unknown" rather than as a downgrade.
    /// </remarks>
#if WINDOWS
    public static SwapVersionAdvisor.Advice? GetRestoreAdvice(string? currentVersion, string? backupVersion)
    {
        var comparison = VersionExtensions.CompareVersionStrings(currentVersion, backupVersion);

        if (comparison is null)
        {
            return null;
        }

        return SwapVersionAdvisor.Classify(comparison);
    }
#endif
}
