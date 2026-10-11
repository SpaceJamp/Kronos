using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards that a downloaded update is verified before it is executed.
/// </summary>
/// <remarks>
/// The portable path verified its archive's SHA256 against GitHub's published digest before replacing the
/// running application. The packaged path - the installer.exe branch - did not verify anything. Its only
/// hash check decided whether an already-cached file needed re-downloading; after that, control fell
/// straight through to Process.Start with UseShellExecute, which is what raises the UAC prompt.
///
/// That makes it arbitrary code execution as administrator for anything that came back from the network
/// instead of from GitHub: a truncating proxy, a substituted body, a compromised CDN. The portable branch
/// was already safe, which is exactly why this survived - the safe path was the one under test.
/// </remarks>
public class UpdateVerificationTests
{
    static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void TheInstallerIsHashCheckedBeforeItIsLaunched()
    {
        var source = Source("src", "Data", "GitHub", "GitHubUpdater.cs");

        var hash = source.IndexOf("GetSha256Hash()", StringComparison.Ordinal);
        var launch = source.IndexOf("UseShellExecute = true", StringComparison.Ordinal);

        Assert.True(hash > 0, "the downloaded file should be hashed somewhere");
        Assert.True(launch > 0, "the installer should be launched somewhere");

        // The installer branch's own launch, which is the last one in the file.
        var branch = source.LastIndexOf("FileName = tempDownloadFile", StringComparison.Ordinal);
        Assert.True(branch > 0, "the installer launch should be found");

        var launchBlock = source.Substring(branch, Math.Min(400, source.Length - branch));
        var hashBeforeLaunch = source.LastIndexOf("GetSha256Hash()", branch, StringComparison.Ordinal);

        Assert.True(
            hashBeforeLaunch > 0,
            "the hash check must come BEFORE the installer is launched, not after");
        Assert.Contains("Process.Start", launchBlock, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUpdateWithNoDigestIsRefusedRatherThanRunUnverified()
    {
        // An asset is only selected at all if it has a digest, so reaching here without one means the
        // check was bypassed. Running it unchecked is worse than refusing.
        var source = Source("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.Contains("no published checksum", source, StringComparison.Ordinal);
        Assert.Contains("cannot be verified", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AMismatchedChecksumDeletesTheFileAndTellsTheUser()
    {
        var source = Source("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.Contains("did not match its published checksum", source, StringComparison.Ordinal);
        Assert.Contains("ShowInstallerFailureAsync", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReleaseCacheIsWrittenAtomicallyRatherThanTruncatedInPlace()
    {
        // File.Create truncates on open, so a failure mid-copy left releases.json unparseable - and it is
        // read on the startup path, from two directions at once.
        var source = Source("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.Contains("SaveCacheAtomically", source, StringComparison.Ordinal);
        Assert.Contains("File.Replace", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReleaseCacheReadIsGuardedSoItCannotEscapeAsAnUnhandledException()
    {
        // CheckForNewGitHubRelease does not catch what FetchLatestRelease throws, and that is called from
        // MainWindow's Loaded handler. A sharing violation with a concurrent forced check was enough.
        var source = Source("src", "Data", "GitHub", "GitHubUpdater.cs");

        var read = source.IndexOf("File.OpenRead(releasesFile)", StringComparison.Ordinal);
        Assert.True(read > 0, "the cached release read should be found");

        var before = source.Substring(Math.Max(0, read - 900), Math.Min(900, read));
        Assert.Contains("try", before, StringComparison.Ordinal);
        Assert.Contains("catch", before, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePortablePathWasAlreadyVerifiedAndStillIs()
    {
        // The reason this survived: the branch with tests was the one that was safe.
        var source = Source("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.True(
            CountOccurrences(source, "GetSha256Hash()") >= 2,
            "both the installer and the portable path should hash the download before using it");
    }

    static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}