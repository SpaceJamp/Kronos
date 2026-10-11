using System;
using System.IO;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards publish-release.ps1 against leaving a release in a state that looks finished but is not.
/// </summary>
/// <remarks>
/// Both faults here happened for real rather than being hypothetical.
///
/// The first is a failing test suite reaching a release. Three times in one session a release was committed
/// and pushed with a red suite, because the version bump, the release notes and the commit were chained into
/// one command and the test output was read afterwards rather than before. Twice the failure was a version
/// guard written minutes earlier - one demanded exactly two version parts and rejected 1.58.1, the next
/// demanded a patch number that 1.59 did not need. The guards were fine to fix; what was missing was
/// anything that stopped a red suite getting out.
///
/// The second is the ImageSharp key. It is passed to build.ps1 as
/// <c>-LicenseKey $env:IMAGESHARP_LICENSE_KEY</c>, and when the variable is empty that argument expands to
/// nothing, so the child is launched with a trailing <c>-LicenseKey</c> and PowerShell rejects it. The
/// error is harmless. Where it arrived was not: the tag was pushed and the release created first, so v1.59
/// was published with correct notes and no artifact - which looks complete and downloads as a 404.
/// </remarks>
public class PublishReleasePreflightTests
{
    static string ReadPublishScript()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "src")) == false)
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, "publish-release.ps1");
        Assert.True(File.Exists(path), $"Could not find {path}");

        return File.ReadAllText(path);
    }

    [Fact]
    public void TheTestSuiteMustPassBeforeAnythingIsPublished()
    {
        var source = ReadPublishScript();

        var run = source.IndexOf("dotnet test", StringComparison.Ordinal);
        var createTag = source.IndexOf("& git tag -a", StringComparison.Ordinal);
        var createRelease = source.IndexOf("& $gh release create", StringComparison.Ordinal);

        Assert.True(run > 0, "the publish script must run the tests");
        Assert.True(createTag > 0 && createRelease > 0, "the tag and release creation should still be there");
        Assert.True(run < createTag, "the tests must run before the tag is created");
        Assert.True(run < createRelease, "the tests must run before the release is created");
    }

    [Fact]
    public void AFailedTestRunStopsThePublishWithSomethingActionable()
    {
        var source = ReadPublishScript();

        Assert.Contains("The test suite failed, so nothing has been published", source, StringComparison.Ordinal);
        Assert.Contains("-SkipTests", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SkippingTheTestsIsOptIn()
    {
        // A switch is opt-in by construction. What matters is that it exists and is consulted, so bypassing
        // the gate is a decision rather than something that happens by accident.
        var source = ReadPublishScript();

        Assert.Matches(@"\[switch\]\$SkipTests", source);
        Assert.Contains("if (-not $SkipTests)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheImageSharpKeyIsCheckedBeforeAnythingIsCreatedOnGitHub()
    {
        var source = ReadPublishScript();

        var check = source.IndexOf("IMAGESHARP_LICENSE_KEY is not set", StringComparison.Ordinal);
        var createTag = source.IndexOf("& git tag -a", StringComparison.Ordinal);
        var createRelease = source.IndexOf("& $gh release create", StringComparison.Ordinal);

        Assert.True(check > 0, "the key must be checked for");
        Assert.True(check < createTag, "the key check must come before the tag is created");
        Assert.True(check < createRelease, "the key check must come before the release is created");
    }

    [Fact]
    public void TheKeyCheckExplainsWhatToSetRatherThanOnlyThatItIsMissing()
    {
        // A message that says "key missing" and stops leaves the next person with a search.
        var source = ReadPublishScript();

        Assert.Contains("IMAGESHARP_LICENSE_KEY = ", source, StringComparison.Ordinal);
        Assert.Contains("Nothing has been created on GitHub", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeyCheckIsSkippedWhenNoBuildWillRun()
    {
        var source = ReadPublishScript();

        var check = source.IndexOf("IMAGESHARP_LICENSE_KEY is not set", StringComparison.Ordinal);
        var before = source.Substring(Math.Max(0, check - 400), Math.Min(400, check));

        Assert.Contains("-not $SkipBuild", before, StringComparison.Ordinal);
    }

    [Fact]
    public void ABuildFailureAfterTheReleaseExistsSaysHowToRecover()
    {
        // The tag, release and notes are already correct at that point, so deleting the release would throw
        // away work and lose the tag the updater reads.
        var source = ReadPublishScript();

        Assert.Contains("The Windows build failed, so the release exists but has no artifact", source, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "The Windows build failed; the release exists without an artifact.",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheReplaceArtifactPathIsReachableForAnExistingRelease()
    {
        // Verified against v1.59, where this was the recovery actually used. A mention in an error message
        // with no switch behind it would be advising something that cannot be run.
        var source = ReadPublishScript();

        Assert.Matches(@"\[switch\]\$ReplaceArtifactOnly", source);
        Assert.Contains("does not exist locally", source, StringComparison.OrdinalIgnoreCase);
    }
}