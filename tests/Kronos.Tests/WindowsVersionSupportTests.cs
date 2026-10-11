using System;
using System.IO;
using Kronos.Helpers;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the minimum-Windows rule and the arithmetic around it.
/// </summary>
/// <remarks>
/// The interesting cases are the ones a plain <c>Version</c> comparison gets wrong. Comparing two
/// <see cref="Version"/> values across a major-version boundary is fine, but comparing <c>Build</c>
/// without first establishing that <c>Major</c> is 10 is not: a hypothetical 6.2.9999 would satisfy a
/// bare build comparison, and the 6.2.9200.0 value that the legacy GetVersionEx shim returns is exactly
/// the kind of thing that turns a working gate into a decorative one.
///
/// Windows 11 reports 10.0.22000 and above, so it is deliberately handled by the same Major==10 path
/// rather than a separate case. If that ever changes, these tests are what should notice.
/// </remarks>
public class WindowsVersionSupportTests
{
    [Fact]
    public void TheFloorIsWindows10TwentyTwoHTwo()
    {
        Assert.Equal(10, WindowsVersionSupport.MinimumSupported.Major);
        Assert.Equal(19045, WindowsVersionSupport.MinimumSupported.Build);
    }

    // ------------------------------------------------------------------ refused

    [Theory]
    [InlineData(6, 3, 9600)]      // Windows 8.1
    [InlineData(10, 0, 10240)]    // 1507
    [InlineData(10, 0, 10586)]    // 1509
    [InlineData(10, 0, 14393)]    // 1607
    [InlineData(10, 0, 17763)]    // 1809
    [InlineData(10, 0, 18362)]    // 1903
    [InlineData(10, 0, 18363)]    // 1909
    [InlineData(10, 0, 19041)]    // 21H2 - what TargetPlatformMinVersion declares
    [InlineData(10, 0, 19042)]    // 21H2
    [InlineData(10, 0, 19043)]    // 21H2
    [InlineData(10, 0, 19044)]    // 21H2
    public void OlderWindowsIsRefused(int major, int minor, int build)
    {
        Assert.False(WindowsVersionSupport.IsSupported(new Version(major, minor, build)));
    }

    [Fact]
    public void TheLegacyVersionShimIsRefused()
    {
        // 6.2.9200.0 is what GetVersionEx reports on a modern machine when the app is manifested for
        // compatibility. It has a build number far above the floor, so a check that compared Build
        // without pinning Major would wave it through.
        Assert.False(WindowsVersionSupport.IsSupported(new Version(6, 2, 9200, 0)));
    }

    [Fact]
    public void AnUnreadableVersionIsRefusedRatherThanAllowed()
    {
        // Fails closed. A gate that can be defeated by making the version report as nothing useful is
        // not a gate, so null is a refusal.
        Assert.False(WindowsVersionSupport.IsSupported(null));
    }

    [Fact]
    public void AZeroVersionIsRefused()
    {
        Assert.False(WindowsVersionSupport.IsSupported(new Version(0, 0, 0, 0)));
    }

    // ------------------------------------------------------------------ allowed

    [Theory]
    [InlineData(10, 0, 19045)]    // 22H2 - the floor itself
    [InlineData(10, 0, 19046)]
    [InlineData(10, 0, 20348)]    // 22H2
    [InlineData(10, 0, 22000)]    // Windows 11 21H2, which reports 10.0.22000
    [InlineData(10, 0, 22631)]
    [InlineData(10, 0, 26100)]    // Windows 11 24H2
    [InlineData(11, 0, 26100)]   // in case a future Windows reports 11.x
    [InlineData(12, 0, 9999)]
    public void CurrentWindowsIsAccepted(int major, int minor, int build)
    {
        Assert.True(WindowsVersionSupport.IsSupported(new Version(major, minor, build)));
    }

    [Fact]
    public void TheFloorIsInclusive()
    {
        // Off-by-one on an inclusive boundary either locks out every user on the minimum supported
        // release, or lets one build below through.
        Assert.True(WindowsVersionSupport.IsSupported(WindowsVersionSupport.MinimumSupported));
        Assert.False(WindowsVersionSupport.IsSupported(new Version(10, 0, 19044)));
    }

    // ------------------------------------------------------------------ the message

    [Fact]
    public void TheRefusalNamesBothWhatIsNeededAndWhatWasFound()
    {
        var message = WindowsVersionSupport.DescribeRefusal(new Version(10, 0, 19042, 0));

        Assert.Contains("19042", message, StringComparison.Ordinal);
        Assert.Contains("19045", message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRefusalSaysSoWhenTheVersionIsUnreadable()
    {
        Assert.Contains("unknown", WindowsVersionSupport.DescribeRefusal(null), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThisMachineIsSupported()
    {
        // The gate is live, so it is worth knowing whether the machine running the tests is on the
        // wrong side of it. Without this, a suite green on Windows 8 would look identical to one green
        // on a supported machine.
        Assert.True(WindowsVersionSupport.IsSupportedCurrentMachine());
    }

    [Fact]
    public void TheReadmeStatesTheSameFloorTheCodeEnforces()
    {
        // A documented requirement and an enforced one drift apart quietly, and the drift is only
        // discovered by someone whose Windows is the wrong version. The README is the place people read
        // before they try, so it has to agree with the gate.
        var readme = File.ReadAllText(ReadRepoFile("README.md"));

        Assert.Contains(WindowsVersionSupport.MinimumSupportedDisplay, readme, StringComparison.Ordinal);
        Assert.Contains(WindowsVersionSupport.MinimumSupported.Build.ToString(), readme, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGateIsNotBehindSomethingThatCanBeSwitchedOff()
    {
        // A floor with an escape hatch is a suggestion. There is no setting, no environment variable
        // and no command line switch that waives it, and this fails if one is ever added.
        var source = File.ReadAllText(ReadRepoFile("src", "Helpers", "WindowsVersionSupport.cs"));

        foreach (var escape in new[] { "Environment.GetEnvironmentVariable", "AppSettings", "Configuration", "--force", "AllowUnsupported" })
        {
            Assert.DoesNotContain(escape, source, StringComparison.OrdinalIgnoreCase);
        }

        // Nor may it read a persisted setting that the user could edit.
        Assert.DoesNotContain("Settings.Instance", source, StringComparison.Ordinal);
    }

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
        return path;
    }
}
