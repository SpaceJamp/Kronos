using System;
using System.IO;
using System.Linq;
using Kronos.Data.GitHub;
using Kronos.Helpers;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the four defects the update-and-download audit found.
/// </summary>
/// <remarks>
/// Each was live in the shipped Windows build. They are grouped because they are all in the update path
/// and all have the same character: a failure that surfaces to the user as the wrong message, rather than
/// as no message.
/// </remarks>
public class UpdateHardeningTests
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

    // ------------------------------------------------------------------ the shared HttpClient

    [Fact]
    public void RegeneratingTheHttpClientDoesNotDisposeTheOneDownloadsAreUsing()
    {
        // It did. Regenerate disposed the live client and replaced it, so any download mid-flight got an
        // ObjectDisposedException and the user saw "Update download failed" - for a proxy setting they
        // changed at some earlier point.
        var source = Source("src", "Http.cs");

        Assert.DoesNotContain("_client?.Dispose()", source, StringComparison.Ordinal);
        Assert.Contains("lock (_clientLock)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheClientIsPublishedBeforeTheOldOneIsAbandoned()
    {
        // Publishing after disposing would hand a disposed client to anyone arriving mid-swap.
        var source = Source("src", "Http.cs");

        var regenerate = source.Substring(source.IndexOf("internal static void Regenerate", StringComparison.Ordinal));
        var body = regenerate.Substring(0, Math.Min(400, regenerate.Length));

        var assign = body.IndexOf("_client = GenerateNewHttpClient()", StringComparison.Ordinal);
        Assert.True(assign > 0, "the new client should be assigned inside Regenerate");
        Assert.DoesNotContain("Dispose", body, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ staging cleanup

    [Fact]
    public void TheUpdateScriptCleansUpItsOwnStagingFolder()
    {
        // The app cannot: it exits as soon as the handoff is handed off, and the folder holds the payload
        // the script is still copying from. So one full copy of the install was left in %TEMP% on every
        // successful update.
        var source = Source("src", "Helpers", "PortableSelfUpdate.cs");

        Assert.Contains("Remove-Item -LiteralPath $PSScriptRoot -Recurse -Force", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCleanupComesAfterTheCopyAndAfterTheRelaunch()
    {
        // In the middle of the copy it would delete the payload from under itself.
        var script = PortableSelfUpdate.BuildHandoffScript(@"C:\tmp\extract", @"C:\apps\Kronos", 4321, relaunch: true);

        // The per-file copy loop, which is what actually moves the payload. Matched on the enumeration
        // rather than "Copy-Item", which also matches the rollback and backup copies.
        var copy = script.IndexOf("Get-ChildItem -LiteralPath", StringComparison.Ordinal);
        var relaunchLine = script.IndexOf("Start-Process -FilePath $entry", StringComparison.Ordinal);
        var cleanup = script.IndexOf("Remove-Item -LiteralPath $PSScriptRoot", StringComparison.Ordinal);

        Assert.True(copy > 0, "the copy should be in the script");
        Assert.True(cleanup > copy, "the cleanup must come after the copy");
        Assert.True(cleanup > relaunchLine, "and after the relaunch, so nothing is reading the folder");
    }

    // ------------------------------------------------------------------ digest parsing

    [Theory]
    [InlineData("sha256:ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789", true)]
    [InlineData("SHA256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789", true)]
    // Everything below was previously treated as a hash to compare against, so a sha512 digest produced a
    // "checksum mismatch" blaming the file for our inability to check it.
    [InlineData("sha512:0123456789ABCDEF", false)]
    [InlineData("md5:0123456789ABCDEF0123456789ABCDEF", false)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", false)]
    [InlineData("sha256:tooshort", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyAWellFormedSha256DigestIsAccepted(string? digest, bool expected)
    {
        var parsed = GitHubUpdater.TryParseSha256Digest(digest);

        Assert.Equal(expected, parsed is not null);
    }

    [Fact]
    public void BothUpdatePathsUseTheSameDigestParser()
    {
        // They had drifted into two different answers to the same question, which is how the slicing bug
        // survived in one place and not the other.
        var source = Source("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.DoesNotContain("Digest[\"sha256:\".Length..]", source, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(source, "TryParseSha256Digest(") >= 3,
            "the parser, and both call sites, should reference it");
    }

    // ------------------------------------------------------------------ progress values

    [Fact]
    public void TheDownloadPercentageCannotBecomeNaNOrInfinity()
    {
        // It divided by TotalBytesToDownload, which is assigned on the UI thread through a non-blocking
        // TryEnqueue and could still be zero when the first tick fired. Zero total gives Infinity, and
        // bytes over Infinity is NaN, which reaches ProgressBar.Value and throws on the UI thread.
        var source = Source("src", "Helpers", "FileDownloader.cs");

        Assert.Contains("double.IsNaN(newPercent)", source, StringComparison.Ordinal);
        Assert.Contains("double.IsInfinity(newPercent)", source, StringComparison.Ordinal);
        Assert.Contains("contentLength > 0", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProgressBarsAreClampedAtThePointOfUse()
    {
        // Belt and braces: ProgressBar.Value throws on NaN and Infinity, and a throw inside the progress
        // callback lands in the unhandled handler, which since 1.59 means the crash window mid-download.
        var source = Source("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.True(
            CountOccurrences(source, "double.IsNaN(percent)") >= 2,
            "both progress callbacks should clamp before touching a ProgressBar");
        Assert.DoesNotContain("filesProgressBar.Value = percent;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("progressBar.Value = percent;", source, StringComparison.Ordinal);
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