using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the build metadata the About section displays.
/// </summary>
/// <remarks>
/// The About section shows three things - version, build commit, build date - and all three come from the
/// assembly rather than from a file that could be refreshed. So if the build does not stamp them, the
/// About section reports the build it was compiled as, correctly, forever. That is not a bug anyone can
/// fix from inside the app, which is why it is pinned here.
///
/// The commit and the timestamp are injected by the csproj itself and therefore need nothing from the
/// build scripts. The tag and the branch cannot be: MSBuild cannot run git while evaluating properties,
/// so build.ps1 has to read them and pass them in with -p:. It did not, which left the version
/// hyperlink pointing at the generic releases page instead of the tagged release.
/// </remarks>
public class BuildMetadataTests
{
    static string RepositoryFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryRoot() }.Concat(parts).ToArray()));

    static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    [Fact]
    public void TheCsprojInjectsTheCommitAndTheTimestampWithoutHelpFromTheBuildScript()
    {
        // These two come from $(SourceRevisionId) and the clock, so a build from an IDE gets them too.
        var csproj = RepositoryFile("src", "Kronos.csproj");

        Assert.Contains("KronosGitCommit", csproj, StringComparison.Ordinal);
        Assert.Contains("$(SourceRevisionId)", csproj, StringComparison.Ordinal);
        Assert.Contains("KronosBuildTimestamp", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTagInjectedIntoTheAssemblyComesFromAPropertyTheBuildScriptSets()
    {
        // Not from $(SourceRevisionId) - SourceRevisionId is the commit, and it never carries the tag.
        var csproj = RepositoryFile("src", "Kronos.csproj");

        Assert.Contains("<AssemblyMetadata Include=\"KronosGitTag\" Value=\"$(KronosGitTag)\" />", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBuildScriptPassesTheTagAndTheBranchToThePublish()
    {
        // Without this the two properties above are always empty, IsFromTagBuild is always false, and the
        // About section's version link lands on the releases index instead of this build's release.
        var buildScript = RepositoryFile("build.ps1");

        Assert.Contains("-p:KronosGitBranch=", buildScript, StringComparison.Ordinal);
        Assert.Contains("-p:KronosGitTag=", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePortablePublishIsTheOneThatReceivesTheMetadata()
    {
        // The portable publish is the build that ships, so it is the one whose About section users see.
        var buildScript = RepositoryFile("build.ps1");

        Assert.Contains("@gitMetadataArgs | Write-Host", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyATagOnThisVeryCommitIsUsed()
    {
        // `git tag` lists every tag in the repository. Reading it without filtering would bake an old
        // tag into whatever was built next, and the About section would link to a release the build is
        // not actually part of.
        var buildScript = RepositoryFile("build.ps1");

        Assert.Contains("--points-at HEAD", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTagIsReadWithoutBreakingThePipeOnGit()
    {
    // v1.55 shipped with an empty tag, so IsFromTagBuild was false and the About section's version link
        // went to the releases index instead of the release it belongs to - the very thing the tag exists
        // to fix. The cause: `git tag --points-at HEAD | Select-Object -First 1` closes the pipe before git
        // finishes writing, git takes a broken pipe, and $LASTEXITCODE comes back as -1. The script then
        // read that as failure and discarded a tag that was there all along.
        //
        // So: no Select-Object in the middle of the git call, and no exit code guarding the tag.
        var buildScript = RepositoryFile("build.ps1");

        Assert.DoesNotContain("git tag --points-at HEAD 2>$null | Select-Object -First 1", buildScript, StringComparison.Ordinal);
        Assert.Contains("@(& git tag --points-at HEAD 2>$null)", buildScript, StringComparison.Ordinal);
        Assert.Contains("if (-not [string]::IsNullOrWhiteSpace($tagName))", buildScript, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInfoTreatsAnAbsentTagAsNotAReleaseBuildRatherThanFailing()
    {
        // A build from an exported archive has no git at all. That has to read as "no tag", not as an error.
        var csproj = RepositoryFile("src", "Kronos.csproj");
        var buildInfo = RepositoryFile("src", "BuildInfo.cs");

        Assert.Contains("IsFromTagBuild", buildInfo, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(GitTag) == false", buildInfo, StringComparison.Ordinal);
        Assert.Contains("KronosGitTag", csproj, StringComparison.Ordinal);
    }
}