using System;
using System.IO;
using Kronos.Helpers;

namespace Kronos.Data;

/// <summary>
/// Replaces a file's contents without ever leaving it half-written.
/// </summary>
/// <remarks>
/// Both the swap and the reset used to call File.Copy straight onto the live DLL in the game's install
/// directory. File.Copy truncates the destination to zero and then writes, so a process kill, a power
/// loss, the disk filling or antivirus interference partway through leaves a truncated nvngx_dlss.dll
/// sitting in the game folder. That is precisely the state the backup chain exists to prevent, and there is
/// no UI anywhere that would tell the user their DLL is corrupt - the game just fails to start.
///
/// The sequence is the same one settings and the imported manifest now use: write a temporary file beside
/// the destination, flush it to disk, then move it into place. The rename is atomic on the same volume, so
/// the DLL is either entirely the old file or entirely the new one.
///
/// The temporary file is written beside the destination rather than to the system temp folder because a
/// rename across volumes is a copy, which reintroduces exactly the truncation this avoids. It is named
/// after the destination so two concurrent operations on different DLLs cannot collide, and a leftover
/// from a killed process is recognisable and removable.
///
/// If anything fails the destination is left untouched, which is the point: the game's own file is still
/// intact and the backup chain still matches it.
/// </remarks>
internal static class AtomicFile
{
    /// <summary>
    /// Replaces <paramref name="destinationPath"/> with the contents of <paramref name="sourcePath"/>.
    /// </summary>
    /// <param name="sourcePath">File to read. Left in place; not deleted or moved.</param>
    /// <param name="destinationPath">File to replace. Only ever the old contents or the complete new ones.</param>
    /// <exception cref="IOException">The replacement did not complete. The destination is unchanged.</exception>
    internal static void Replace(string sourcePath, string destinationPath)
    {
        // A name that cannot collide with a real asset, and cannot collide between two operations either,
        // since the destination differs per DLL.
        var tempPath = $"{destinationPath}.kronostmp";

        try
        {
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var temp = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(temp);
                // Flushed before the rename. Without this the rename can be durable while the contents
                // are not, which is the same corruption by a different route.
                temp.Flush(flushToDisk: true);
            }

            if (File.Exists(destinationPath))
            {
                File.Replace(tempPath, destinationPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, destinationPath);
            }
        }
        catch
        {
            // The half-written temporary file must not be left where the next scan or swap could mistake it
            // for a real asset.
            Storage.TryDeleteFile(tempPath);
            throw;
        }
    }
}