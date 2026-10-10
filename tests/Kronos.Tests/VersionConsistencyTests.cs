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
    public void TheVersionIsATwoPartNumber()
    {
        // The project has always used major.minor here, 1.45 then 1.46, and the installer filename
        // carries it verbatim. The SDK expands it to 1.47.0.0 in the assembly and the version
        // resource, which is what the About page and the registry entry show.
        //
        // This test caught itself: it was written expecting three parts and failed against 1.47. The
        // two part form is the established one and is what every existing tag uses, so the assertion
        // was wrong rather than the version.
        Assert.Matches(@"^\d+\.\d+$", CsprojVersion());
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