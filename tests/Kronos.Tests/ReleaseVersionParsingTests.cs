using System;
using System.IO;
using Kronos.Data.GitHub;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards how a published release is turned into a version number.
/// </summary>
/// <remarks>
/// This is the whole update check. If it yields 0, every comparison against a real version says
/// "up to date", and there is no error anywhere - the app simply never offers an update again.
///
/// The trap is that the version comes from the release *title*, not the tag. A title reading
/// "Kronos 1.52 - bug fixes" has a first token of "Kronos", which does not start with "v", so the
/// original code returned 0. That is not hypothetical: this repository's own v1.52 release was
/// published under exactly such a title and would have shipped with the updater permanently disabled.
/// </remarks>
public class ReleaseVersionParsingTests
{
    /// <summary>
    /// The packed form the updater compares against: major, minor, build and revision each shifted
    /// into a 16-bit slot. Built the same way <c>GetCurrentVersionNumber</c> builds it.
    /// </summary>
    static ulong Packed(ulong major, ulong minor = 0, ulong build = 0, ulong revision = 0) =>
        (major << 48) + (minor << 32) + (build << 16) + revision;

    static GitHubRelease Release(string name, string tag) => new() { Name = name, TagName = tag };

    // ------------------------------------------------------------------ the trap

    [Fact]
    public void ATitleThatDoesNotStartWithTheVersionFallsBackToTheTag()
    {
        // The shape this repository actually published under. Returning 0 here would make the app
        // report itself as permanently up to date.
        var release = Release("Kronos 1.52 — bug fixes and dependency security", "v1.52");

        Assert.Equal(Packed(1, 52), release.GetVersionNumber());
    }

    [Theory]
    [InlineData("Kronos 1.52", "v1.52")]
    [InlineData("DLSS Swapper 2.0", "v2.0")]
    [InlineData("Release", "v3.1")]
    [InlineData("", "v1.53")]
    public void TheTagIsUsedWheneverTheTitleCarriesNoVersion(string name, string tag)
    {
        Assert.NotEqual(0ul, Release(name, tag).GetVersionNumber());
    }

    // ------------------------------------------------------------------ the original behaviour

    [Theory]
    [InlineData("v1.52.0.0", 1UL, 52UL, 0UL, 0UL)]
    [InlineData("v1", 1UL, 0UL, 0UL, 0UL)]
    [InlineData("v1.52.3", 1UL, 52UL, 3UL, 0UL)]
    [InlineData("v1.52.3.4", 1UL, 52UL, 3UL, 4UL)]
    [InlineData("V1.52.0.0", 1UL, 52UL, 0UL, 0UL)]
    public void ALeadingVersionTokenIsStillParsedTheSameWay(string title, ulong major, ulong minor, ulong build, ulong revision)
    {
        Assert.Equal(Packed(major, minor, build, revision), Release($"{title} something", $"{title}").GetVersionNumber());
    }

    [Fact]
    public void TheTitleIsPreferredOverTheTagWhenBothParse()
    {
        // Upstream's convention, where the title carries a friendlier version than the tag.
        Assert.Equal(Packed(1, 2, 6, 1), Release("v1.2.6.1", "v9.9.9.9").GetVersionNumber());
    }

    [Fact]
    public void APartialVersionIsAcceptedRatherThanRejected()
    {
        // "v1.53" is a valid title for a release tagged v1.53, and must not be discarded.
        Assert.Equal(Packed(1, 53), Release("v1.53", "v1.53").GetVersionNumber());
    }

    // ------------------------------------------------------------------ genuinely unparseable

    [Theory]
    [InlineData("", "")]
    [InlineData("no version here", "no version here")]
    [InlineData("vx.y.z", "vx.y.z")]
    public void NeitherFieldCarryingAVersionYieldsZero(string name, string tag)
    {
        Assert.Equal(0ul, Release(name, tag).GetVersionNumber());
    }

    // ------------------------------------------------------------------ the comparison it feeds

    [Fact]
    public void ANewerReleaseIsDetectedWhenTheTitleIsUnconventional()
    {
        // The end-to-end claim: publishing under a friendly title must not silently disable updates.
        var current = new Version(1, 52, 0, 0);
        var release = Release("Kronos 1.53 — bug fixes", "v1.53");

        Assert.True(GitHubUpdater.IsNewerThan(release, current));
    }

    [Fact]
    public void TheSameVersionIsStillNotOfferedAsAnUpdate()
    {
        var current = new Version(1, 52, 0, 0);
        var release = Release("Kronos 1.52 — bug fixes", "v1.52");

        Assert.False(GitHubUpdater.IsNewerThan(release, current));
    }

    [Fact]
    public void VersionOrderingIsNumericRatherThanLexicographic()
    {
        // A string comparison would call 1.9 newer than 1.52.
        Assert.True(GitHubUpdater.IsNewerThan(Release("v1.10.0.0", "v1.10.0.0"), new Version(1, 9, 0, 0)));
    }
}

/// <summary>
/// Guards the Linux updater's asset digest, which the Windows test assembly cannot reach.
/// </summary>
/// <remarks>
/// The test project targets the Windows target framework, and <c>src/Updater.cs</c> is excluded from
/// that build - it is compiled only for Linux. So unlike the version parsing above, this cannot be
/// asserted on directly and is checked by reading the source, which is the pattern the existing
/// updater tests already use.
///
/// The bug: the digest was read from a JSON property named <c>sha256</c>. GitHub's release API has no
/// such field, it publishes <c>digest</c> as "sha256:&lt;hex&gt;", so the value was always null and
/// verification was skipped on every download while appearing to be supported.
/// </remarks>
public class LinuxUpdaterDigestSourceTests
{
    static string UpdaterSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "src")) == false)
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "src", "Updater.cs");
        Assert.True(File.Exists(path), $"Could not find {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void TheLinuxUpdaterReadsTheDigestFieldAndNotTheOneGitHubDoesNotSend()
    {
        var source = UpdaterSource();

        Assert.Contains("[JsonPropertyName(\"digest\")]", source, StringComparison.Ordinal);

        Assert.DoesNotContain(
            "[JsonPropertyName(\"sha256\")]",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheLinuxUpdaterStillVerifiesTheDownloadWhenADigestIsPresent()
    {
        // The fix must not turn into "give up on verification" - a missing digest has to stay
        // non-fatal, since assets uploaded before GitHub computed digests have none.
        var source = UpdaterSource();

        Assert.Contains("SHA256 mismatch", source, StringComparison.Ordinal);
        Assert.Contains("not hash-verified", source, StringComparison.Ordinal);
    }
}
