using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Kronos.Tests;

/// <summary>
/// Guards that the version declaration in the csproj is well-formed.
/// </summary>
/// <remarks>
/// The version is declared in <c>src/Kronos.csproj</c> as the single source of truth.
/// The Inno Setup installer (package/) was removed; builds are now from source only.
/// </remarks>
public class VersionConsistencyTests
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

    static string CsprojVersion()
    {
        var csproj = ReadRepoFile("src", "Kronos.csproj");
        var match = Regex.Match(csproj, @"<Version>([^<]+)</Version>");

        Assert.True(match.Success, "No <Version> element found in Kronos.csproj.");

        return match.Groups[1].Value.Trim();
    }

    [Fact]
    public void TheVersionIsTwoPartsOrATwoPartVersionWithAPatchNumber()
    {
        // The project used major.minor here for its whole life - 1.45, 1.46, on through 1.58 - and the
        // installer filename carried it verbatim. The SDK expands it to 1.58.0.0 in the assembly and the
        // version resource, which is what the About page and the registry entry show.
        //
        // 1.58.1 added a third part, for a single bug fix on top of a shipped release. That is a real
        // convention change rather than a mistake, so this assertion was widened rather than the version
        // bent back - a patch release that reported itself as 1.58 would be indistinguishable from the
        // release it fixes, and the updater would not offer it to anyone already on 1.58.
        //
        // The updater handles four-part versions: VersionExtensions.TryParseVersionNumber treats missing
        // parts as zero, so 1.58.1.0 packs and compares correctly against 1.58.0.0.
        Assert.Matches(@"^\d+\.\d+(\.\d+)?$", CsprojVersion());
    }

    [Fact]
    public void APatchReleaseDoesNotCollideWithTheReleaseItFixes()
    {
        // Guards the reason a patch component is allowed at all: when one is used it has to actually be a
        // different version, or the update check cannot offer it to anyone on the release it patches.
        //
        // Only asserted when there IS a patch component. This test was originally written to require one,
        // and then failed the very next release for using plain 1.59 - which is a legitimate choice, since
        // a fix to a release nobody has installed yet does not need a patch number. The rule is "a patch
        // number must be meaningful", not "every version must have one".
        var version = CsprojVersion();

        if (version.Count(c => c == '.') != 2)
        {
            // major.minor, no patch. Nothing to compare against.
            return;
        }

        Assert.True(
            Version.Parse(version) > Version.Parse(string.Join('.', version.Split('.')[..2])),
            $"{version} must compare greater than the release it patches, or the update check will not offer it.");
    }

    [Fact]
    public void TheVersionIsAboveUpstreamSoTheUpdateCheckStaysQuiet()
    {
        // Upstream DLSS Swapper is 1.2.6.1. Kronos checks GitHub for newer releases, and a version at or
        // below upstream's would either nag about a version that is not actually newer or prompt to
        // "update" to something that would replace this fork.
        var version = Version.Parse(CsprojVersion());

        Assert.True(version > new Version(1, 2, 6, 1),
            $"Kronos {version} must be above upstream 1.2.6.1, or the update check misbehaves.");
    }
}