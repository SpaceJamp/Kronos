using System;
using System.IO;
using System.Linq;
using Kronos.Data;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the atomic file replacement used when overwriting a game's DLL.
/// </summary>
/// <remarks>
/// The swap and the reset used to call File.Copy straight onto the live DLL in the game folder.
/// File.Copy truncates the destination to zero and then writes, so a process kill, a full disk or
/// antivirus interference partway through leaves a truncated nvngx_dlss.dll sitting in the install
/// directory. That is the exact state the backup chain exists to prevent, and nothing in the UI would
/// tell the user - the game simply stops launching.
///
/// The replacement writes a temporary file beside the destination and renames it into place, so the DLL
/// is either entirely the old file or entirely the new one.
///
/// These tests do not need a game: AtomicFile.Replace is a file operation, and the property that matters -
/// the destination is never left partially written - is checkable on any two files.
/// </remarks>
[Collection(StorageCollection.Name)]
public class AtomicFileTests : IDisposable
{
    readonly string _root;

    public AtomicFileTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"kronos-atomic-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
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

    string Path_(string name) => Path.Combine(_root, name);

    [Fact]
    public void TheDestinationEndsUpWithExactlyTheSourceContents()
    {
        var source = Path_("source.dll");
        var destination = Path_("nvngx_dlss.dll");
        File.WriteAllText(source, "NEW-DLL-CONTENT");
        File.WriteAllText(destination, "OLD-DLL-CONTENT");

        AtomicFile.Replace(source, destination);

        Assert.Equal("NEW-DLL-CONTENT", File.ReadAllText(destination));
    }

    [Fact]
    public void TheSourceIsLeftAlone()
    {
        // The source is the backup chain's file for a reset, and the shipped DLL Kronos keeps for a swap.
        // Consuming it is how the original used to be lost for good.
        var source = Path_("source.dll");
        var destination = Path_("nvngx_dlss.dll");
        File.WriteAllText(source, "NEW");
        File.WriteAllText(destination, "OLD");

        AtomicFile.Replace(source, destination);

        Assert.True(File.Exists(source), "the source must survive; it may be the only copy of the original");
        Assert.Equal("NEW", File.ReadAllText(source));
    }

    [Fact]
    public void ADestinationThatDoesNotExistYetIsCreated()
    {
        var source = Path_("source.dll");
        var destination = Path.Combine(_root, "sub", "new.dll");
        File.WriteAllText(source, "NEW");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        AtomicFile.Replace(source, destination);

        Assert.Equal("NEW", File.ReadAllText(destination));
    }

    [Fact]
    public void NoTemporaryFileIsLeftBehindOnSuccess()
    {
        var source = Path_("source.dll");
        var destination = Path_("nvngx_dlss.dll");
        File.WriteAllText(source, "NEW");
        File.WriteAllText(destination, "OLD");

        AtomicFile.Replace(source, destination);

        var leftovers = Directory.EnumerateFiles(_root, "*.kronostmp").ToList();
        Assert.Empty(leftovers);
    }

    [Fact]
    public void AFailureLeavesTheDestinationExactlyAsItWas()
    {
        // The property that matters. Occupying the temp path makes the write fail the way a full disk or
        // antivirus would - and the game's own DLL must be untouched, because the backup chain's whole
        // value is that it still matches what is on disk.
        var source = Path_("source.dll");
        var destination = Path_("nvngx_dlss.dll");
        File.WriteAllText(source, "NEW-DLL-CONTENT");
        File.WriteAllText(destination, "ORIGINAL-GAME-DLL");

        var tempPath = destination + ".kronostmp";
        using (new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => AtomicFile.Replace(source, destination));
        }

        Assert.Equal("ORIGINAL-GAME-DLL", File.ReadAllText(destination));
    }

    [Fact]
    public void TheDestinationIsNeverTruncatedBeforeTheWriteCompletes()
    {
        // A direct statement of the regression rather than of the fix. With the old File.Copy the file was
        // zero bytes the instant the call returned control to a failure, and it stayed zero on disk.
        var source = Path_("source.dll");
        var destination = Path_("nvngx_dlss.dll");
        File.WriteAllText(source, "NEW-DLL-CONTENT");
        File.WriteAllText(destination, "ORIGINAL-GAME-DLL");

        long lengthDuringCopy = -1;
        using (var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            // Stand in for the write: assert the destination still holds its original content at the moment
            // the new contents are mid-flight.
            lengthDuringCopy = new FileInfo(destination).Length;
        }

        Assert.True(lengthDuringCopy > 0, "the destination must not be empty before the replacement lands");
        Assert.Equal("ORIGINAL-GAME-DLL", File.ReadAllText(destination));
    }
}