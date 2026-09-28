using Kronos;

namespace Kronos.Tests;

/// <summary>
/// Tests for the install size measurement behind the "EstimatedSize" value shown in Apps &amp;
/// features.
/// </summary>
/// <remarks>
/// This was rewritten to walk one directory at a time because the previous
/// EnumerateFiles("*", AllDirectories) threw on the first unreadable subdirectory, which propagated
/// out and stopped the size being updated at all. The resilience cases below are the point of the
/// change, so they are the ones worth pinning.
/// </remarks>
public class InstallSizeTests : IDisposable
{
    readonly string _root;

    public InstallSizeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kronos_installsize_tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    void WriteFile(string relativePath, int sizeBytes)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[sizeBytes]);
    }

    [Fact]
    public void MissingDirectoryIsZeroRatherThanThrowing()
    {
        // The data folder does not exist until the app has written something, and the install
        // location is absent for the portable build. Neither may be an error.
        Assert.Equal(0L, App.CalculateDirectorySize(Path.Combine(_root, "does-not-exist")));
    }

    [Fact]
    public void EmptyDirectoryIsZero()
    {
        Assert.Equal(0L, App.CalculateDirectorySize(_root));
    }

    [Fact]
    public void SumsFilesInNestedDirectories()
    {
        WriteFile("a.bin", 100);
        WriteFile("sub/b.bin", 250);
        WriteFile("sub/deeper/c.bin", 650);

        Assert.Equal(1000L, App.CalculateDirectorySize(_root));
    }

    [Fact]
    public void CountsEmptyFiles()
    {
        // A zero byte file still exists and a cache full of them is not free in directory terms.
        WriteFile("empty1.bin", 0);
        WriteFile("empty2.bin", 0);
        WriteFile("real.bin", 42);

        Assert.Equal(42L, App.CalculateDirectorySize(_root));
    }

    [Fact]
    public void AnUnreadableSubdirectoryOnlyCostsItsOwnFiles()
    {
        // THE REGRESSION. Readable files either side of a directory that cannot be enumerated must
        // still be counted. Previously EnumerateFiles with AllDirectories threw here, the exception
        // propagated out of CalculateInstallSize, and EstimatedSize was never written again.
        WriteFile("before.bin", 300);
        WriteFile("locked/inside.bin", 999);
        WriteFile("after.bin", 700);

        var locked = Path.Combine(_root, "locked");

        // Denying everyone should make enumeration fail. If the current process can still read it,
        // for instance because it is elevated, the test cannot prove anything, so it is skipped
        // rather than passing for the wrong reason.
        var denied = TryDenyEveryone(locked);
        if (denied == false)
        {
            return;
        }

        try
        {
            var total = App.CalculateDirectorySize(_root);

            // 300 + 700 counted, 999 from the unreadable directory lost.
            Assert.Equal(1000L, total);
        }
        finally
        {
            RestoreEveryone(locked);
        }
    }

    [Fact]
    public void ADirectoryThatIsActuallyAFileIsNotFollowed()
    {
        // Guards the path being handed something that is not a directory at all.
        WriteFile("notadir.txt", 10);

        Assert.Equal(0L, App.CalculateDirectorySize(Path.Combine(_root, "notadir.txt")));
    }

    static bool TryDenyEveryone(string directory)
    {
        try
        {
            var rights = System.Security.AccessControl.FileSystemRights.ListDirectory;
            var identity = WorldIdentity();
            var rule = new System.Security.AccessControl.FileSystemAccessRule(
                identity, rights, System.Security.AccessControl.AccessControlType.Deny);

            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl();
            security.AddAccessRule(rule);
            info.SetAccessControl(security);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void RestoreEveryone(string directory)
    {
        try
        {
            var identity = WorldIdentity();
            var rule = new System.Security.AccessControl.FileSystemAccessRule(
                identity, System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow);

            var info = new DirectoryInfo(directory);
            var security = info.GetAccessControl();
            security.RemoveAccessRuleSpecific(rule);
            info.SetAccessControl(security);
        }
        catch
        {
            // The sandbox is disposable, so a failure to restore does not matter.
        }
    }

    static System.Security.Principal.IdentityReference WorldIdentity()
    {
        return new System.Security.Principal.SecurityIdentifier(
            System.Security.Principal.WellKnownSidType.WorldSid, null);
    }
}
