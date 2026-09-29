using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kronos;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the update check and the build metadata it reports alongside.
/// </summary>
/// <remarks>
/// Two separate faults, both of which made the updater look healthy while doing nothing.
///
/// The first is the release endpoint. It was written out as a literal in two places. It pointed at a
/// repository that is not publicly readable, and GitHub answers 404 rather than 403 for a repository
/// that exists but is private, so an unauthenticated read of a private repository is indistinguishable
/// from a repository that does not exist. There is no token in this app and adding one would mean
/// shipping a credential to every install, so update checking cannot work against a private repository
/// at all.
///
/// The second is that a failed check was reported as "No new updates available". FetchLatestRelease
/// returns null both for "no update" and for "request failed", and the settings page turned both into
/// the same message. A dead endpoint therefore looked exactly like being up to date, which is why this
/// was diagnosed as "the check is not working" rather than as an error.
///
/// The build metadata tests here exist because the same silent-failure shape appeared a third time.
/// BuildInfo's four properties were never assigned, so the Settings page showed a build date of
/// 1 January 1970: BuildTimestamp was 0 and FromUnixTimeSeconds(0) is the epoch. Nothing looked broken,
/// the value just rendered as a real date.
/// </remarks>
public class BuildInfoAndUpdateTests
{
    static string ReadRepoFile(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "src")) == false)
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, Path.Combine(segments));
        Assert.True(File.Exists(path), $"Could not find {path}");

        return File.ReadAllText(path);
    }

    // ---------------------------------------------------------------- ParseTimestamp

    [Fact]
    public void ParseTimestampReadsTheRoundTripFormatTheCsprojWrites()
    {
        // Exactly what $[System.DateTime]::UtcNow.ToString("o") produces.
        var parsed = BuildInfo.ParseTimestamp("2026-09-29T18:28:21.6172282Z");

        Assert.NotNull(parsed);
        Assert.Equal(2026, parsed!.Value.Year);
        Assert.Equal(9, parsed.Value.Month);
        Assert.Equal(29, parsed.Value.Day);
        Assert.Equal(TimeSpan.Zero, parsed.Value.Offset);
    }

    [Fact]
    public void ParseTimestampTreatsMissingMetadataAsAbsentRatherThanTheEpoch()
    {
        // This is the distinction that matters. Returning the epoch here would put 1 January 1970 back
        // on screen, which is how the bug looked like a plausible build date in the first place.
        Assert.Null(BuildInfo.ParseTimestamp(null));
        Assert.Null(BuildInfo.ParseTimestamp(string.Empty));
        Assert.Null(BuildInfo.ParseTimestamp("   "));
    }

    [Fact]
    public void ParseTimestampRejectsUnparseableTextInsteadOfThrowing()
    {
        // A build with an unreadable date should degrade to "unknown" rather than take the app down
        // over a cosmetic field.
        Assert.Null(BuildInfo.ParseTimestamp("not a date"));
        Assert.Null(BuildInfo.ParseTimestamp("1759"));
    }

    [Fact]
    public void ParseTimestampHonoursTheUtcOffsetRatherThanAssumingLocalTime()
    {
        // An offset of +05:00 means the instant is five hours earlier than the same wall clock reading
        // in UTC. If the offset were ignored, every build west of UTC would report a date a day out.
        var withOffset = BuildInfo.ParseTimestamp("2026-09-29T23:30:00.0000000+05:00");
        var sameInstantInZ = BuildInfo.ParseTimestamp("2026-09-29T18:30:00.0000000Z");

        Assert.NotNull(withOffset);
        Assert.NotNull(sameInstantInZ);
        Assert.Equal(sameInstantInZ!.Value.ToUnixTimeSeconds(), withOffset!.Value.ToUnixTimeSeconds());
    }

    // ---------------------------------------------------------------- csproj injection

    [Fact]
    public void BuildMetadataIsInjectedFromATargetNotABareItemGroup()
    {
        // A bare ItemGroup is evaluated when the project loads, which is before the SDK has run
        // InitializeSourceControlInformation and assigned SourceRevisionId. Injecting from there wrote
        // KronosGitCommit as an empty string while the informational version still carried the commit,
        // because the two are assembled at different times. Verified against a real build: the
        // metadata attribute came out empty and InformationalVersion came out as 1.47+<commit>.
        var csproj = ReadRepoFile("src", "Kronos.csproj");

        var metadataGroup = Regex.Match(
            csproj,
            @"<ItemGroup>\s*<AssemblyMetadata\s+Include=""KronosGitCommit""",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));

        Assert.False(
            metadataGroup.Success,
            "KronosGitCommit is injected from a bare ItemGroup, which is evaluated before "
            + "SourceRevisionId exists, so the commit is written as an empty string.");

        Assert.Contains("AddKronosBuildMetadata", csproj);
        Assert.Contains(@"BeforeTargets=""GetAssemblyAttributes""", csproj);
    }

    [Fact]
    public void TheBuildTimestampIsWrittenInRoundTripFormat()
    {
        // ToString("o") rather than a Unix seconds value. MSBuild property functions cannot call
        // DateTimeOffset.UtcNow.ToUnixTimeSeconds(), which fails the build with MSB4212.
        var csproj = ReadRepoFile("src", "Kronos.csproj");

        Assert.Contains(@"$([System.DateTime]::UtcNow.ToString(&quot;o&quot;))", csproj);
    }

    [Fact]
    public void EveryInjectedMetadataKeyIsPrefixes()
    {
        // Guards against a key being added without the prefix, which would then collide with the
        // SDK's own AssemblyMetadata keys and BuildInfo would never find it.
        var csproj = ReadRepoFile("src", "Kronos.csproj");

        foreach (Match key in Regex.Matches(csproj, @"<AssemblyMetadata\s+Include=""([^""]+)"""))
        {
            Assert.StartsWith("Kronos", key.Groups[1].Value);
        }
    }

    // ---------------------------------------------------------------- release URL

    [Fact]
    public void TheReleaseApiUrlIsNotWrittenOutAsALiteralAnywhereElse()
    {
        // It used to appear as a literal in two places inside GitHubUpdater.cs. Two copies of a
        // repository name is two places to forget when the repository is renamed, which is precisely
        // what left the app polling a path that no longer resolved.
        var source = ReadRepoFile("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.DoesNotContain("api.github.com/repos/SpaceJamp", source);
        Assert.Contains("api.github.com/repos/{NormaliseRepository(repository)}", source);
    }

    [Fact]
    public void ThereIsASingleRepositoryConstantThatBothUrlsAreBuiltFrom()
    {
        var source = ReadRepoFile("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.Contains("internal const string DefaultRepository", source);

        // Two sites is correct: one for releases/latest and one for releases/tags/{tag}. What must not
        // happen is either of them naming a repository directly, because that reintroduces the second
        // copy of the name that made the rename miss.
        foreach (Match url in Regex.Matches(source, @"api\.github\.com/repos/[^""\s]*"))
        {
            // Assert.StartsWith's third parameter is a StringComparison, not a message, so the reason
            // goes in the loop variable rather than as an argument.
            Assert.True(
                url.Value.StartsWith("api.github.com/repos/{NormaliseRepository(", StringComparison.Ordinal),
                $"A release URL names a repository directly: {url.Value}");
        }
    }

    // ---------------------------------------------------------------- failure reporting

    [Fact]
    public void AFailedCheckIsDistinguishableFromBeingUpToDate()
    {
        // The whole point of UpdateCheckResult. Without it the two cases are both a null release and
        // both render as "No new updates available".
        var source = ReadRepoFile("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.Contains("enum UpdateCheckResult", source);
        Assert.Contains("UpToDate", source);
        Assert.Contains("UpdateAvailable", source);
        Assert.Contains("Failed", source);
    }

    [Fact]
    public void TheSettingsPageReportsAFailedCheckInsteadOfClaimingNoUpdates()
    {
        var source = ReadRepoFile("src", "Pages", "SettingsPageModel.cs");

        Assert.Contains("CheckForUpdateAsync", source);
        Assert.Contains("UpdateCheckResult.Failed", source);

        // The "no updates" message must only be reachable from the up-to-date branch.
        Assert.DoesNotContain(
            "var newUpdate = await githubUpdater.CheckForNewGitHubRelease(true)",
            source);
    }

    [Fact]
    public void TheFailureMessageExplainsAPrivateRepositoryRatherThanBlamingTheNetwork()
    {
        // GitHub returns 404 for a private repository to an unauthenticated read, so "could not reach
        // GitHub" alone would send the reader looking at their firewall instead of at the one thing
        // that actually causes it here.
        var source = ReadRepoFile("src", "Pages", "SettingsPageModel.cs");

        Assert.Contains("private", source);
        Assert.Contains("DefaultRepository", source);
    }

    [Fact]
    public void TheReleaseNotesLinkUsesThisAppsOwnRepositoryNotUpstreams()
    {
        // BuildInfo.GitTag is this app's tag, such as v1.48. It was being interpolated into
        // beeradmoore/dlss-swapper's release URL, which is upstream, and upstream has no v1.48, so the
        // link could only ever reach a 404.
        var source = ReadRepoFile("src", "Pages", "SettingsPageModel.cs");

        Assert.DoesNotContain("beeradmoore/dlss-swapper/releases", source);
        Assert.Contains("https://github.com/{repository}/releases", source);
    }

    [Fact]
    public void TheRepositoryIsNotTheOldForkPath()
    {
        // GitHub redirects the old path for git, so a stale name here is invisible until something
        // reads it without credentials. That is precisely what the update check does.
        var source = ReadRepoFile("src", "Data", "GitHub", "GitHubUpdater.cs");

        Assert.DoesNotContain("unofficial-dlss-swapper\"", source);
        Assert.Contains("kronos-dlss-swapper", source);
    }

    // ---------------------------------------------------------------- repack tag layout

    [Fact]
    public void TheRepackTagIsStackedUnderTheTitleRatherThanOverlaidOnIt()
    {
        // Both used to be direct children of Grid.Column="2": the title centred, the tag pinned to the
        // bottom of the fixed 80px row. The tag therefore floated below a one line title and sat on top
        // of the second line once the title wrapped.
        var xaml = ReadRepoFile("src", "Pages", "GameGridPage.xaml");

        var tagIndex = xaml.IndexOf("REPACK", StringComparison.Ordinal);
        Assert.True(tagIndex > 0, "Could not find the repack tag in GameGridPage.xaml");

        var preceding = xaml[..tagIndex];
        var stackPanelStart = preceding.LastIndexOf("<StackPanel", StringComparison.Ordinal);

        Assert.True(
            stackPanelStart > 0,
            "The repack tag is no longer inside the StackPanel that holds the title.");

        // Scoped to the StackPanel itself. Slicing to the end of the file would also pick up the card
        // template's own VerticalAlignment="Bottom" gradient, which has nothing to do with the tag.
        var stackPanelEnd = xaml.IndexOf("</StackPanel>", tagIndex, StringComparison.Ordinal);
        Assert.True(stackPanelEnd > 0, "Could not find the end of the StackPanel holding the tag.");

        var stackPanel = xaml[stackPanelStart..stackPanelEnd];

        // The tag and the title have to be siblings inside it, or the tag is back to being positioned
        // against the row instead of against the text.
        Assert.Contains("x:Bind Title", stackPanel);

        // Neither of these may come back: a vertical alignment re-decouples the tag from the text, and a
        // negative bottom margin pushes it past the edge of the row.
        Assert.DoesNotContain(@"VerticalAlignment=""Bottom""", stackPanel);
        Assert.DoesNotContain(@"Margin=""0,0,0,-2""", stackPanel);
    }
}
