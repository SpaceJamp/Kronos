using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Kronos.Tests;

/// <summary>
/// Guards that the three places the version is declared cannot drift apart.
/// </summary>
/// <remarks>
/// The version is declared in <c>src/Kronos.csproj</c>, in <c>package/config.cmd</c> and as a fallback
/// literal in <c>package/Installer.iss</c>. They have to agree, and nothing in the build enforces it:
/// the Inno script receives the real value as /DAppVersion, so its literal is never exercised, and
/// config.cmd drives the output filename, so a stale value there silently produces an installer called
/// the old version.
///
/// That is not hypothetical. Two locally built binaries both reported 1.46 and had to be told apart by
/// their commit hashes, which is why the log now records the hash. A test is cheaper than that.
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

    static string ConfigCmdVersion()
    {
        var config = ReadRepoFile("package", "config.cmd");
        var match = Regex.Match(config, @"set app_version=(\S+)", RegexOptions.IgnoreCase);

        Assert.True(match.Success, "No app_version found in config.cmd.");

        return match.Groups[1].Value.Trim();
    }

    static string InstallerFallbackVersion()
    {
        var iss = ReadRepoFile("package", "Installer.iss");
        var match = Regex.Match(iss, @"#define AppVersion (\S+)");

        Assert.True(match.Success, "No AppVersion fallback found in Installer.iss.");

        return match.Groups[1].Value.Trim();
    }

    [Fact]
    public void AllThreeVersionDeclarationsAgree()
    {
        var csproj = CsprojVersion();
        var config = ConfigCmdVersion();
        var iss = InstallerFallbackVersion();

        Assert.Equal(csproj, config);
        Assert.Equal(csproj, iss);
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

    [Fact]
    public void TheInstallerReceivesTheVersionRatherThanRelyingOnTheFallback()
    {
        // package_Installer.cmd passes /DAppVersion, which overrides the literal. If that ever stops
        // happening, the literal above becomes the real version and the two can drift silently, so the
        // call is asserted rather than assumed.
        var cmd = ReadRepoFile("package", "package_Installer.cmd");

        Assert.Contains("/DAppVersion=%app_version%", cmd, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheOutputFilenamesFollowTheConfiguredVersion()
    {
        // The installer and zip names are built from app_version, so a stale value there produces an
        // artifact with the wrong name rather than failing.
        var config = ReadRepoFile("package", "config.cmd");

        Assert.Contains("Kronos-%app_version%-installer.exe", config, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kronos-%app_version%-portable.zip", config, StringComparison.OrdinalIgnoreCase);
    }
}
