using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards phase 2 of the Uno migration: nothing the app depends on is a Windows-only package.
/// </summary>
/// <remarks>
/// Read from src/packages.lock.json rather than the csproj, because the lock file has the whole resolved
/// graph. A Windows-only package arriving transitively - through some other package's dependency - breaks
/// the Linux build just as surely as a direct reference, and would never appear in the csproj.
/// </remarks>
public class CrossPlatformPackageTests
{
    /// <summary>
    /// Packages that ship only Windows App SDK (net*-windows) assets, with what replaced each.
    /// </summary>
    public static TheoryData<string, string> WindowsOnlyPackages => new()
    {
        { "Microsoft.WindowsAppSDK", "Uno.Sdk" },
        { "Microsoft.Windows.SDK.BuildTools", "nothing; it was WinAppSDK's build tooling" },
        { "Microsoft.Web.WebView2", "nothing; it was unused, and collides with Uno.UI's CoreWebView2" },
        { "CommunityToolkit.Labs.WinUI.Controls.MarkdownTextBlock", "Markdig and UserControls/MarkdownView" },
        { "CommunityToolkit.WinUI.UI.Controls.DataGrid", "WinUI.TableView" },
        // Not Windows-only, but the only Uno build is net7.0 against Uno 5 and unmaintained, which is no
        // better a foundation. It was briefly the phase 1 stopgap.
        { "Uno.CommunityToolkit.WinUI.UI.Controls.DataGrid", "WinUI.TableView" },
    };

    [Theory]
    [MemberData(nameof(WindowsOnlyPackages))]
    public void TheAppDoesNotDependOnAWindowsOnlyPackage(string packageId, string replacedBy)
    {
        var resolved = ResolvedPackages();

        Assert.False(
            resolved.Contains(packageId),
            $"{packageId} is in src/packages.lock.json. It has no Uno build, so it cannot run on Linux; "
                + $"it was replaced by {replacedBy}.");
    }

    [Fact]
    public void TheLockFileWasReadForTheUnoTargetFramework()
    {
        // Without this, a renamed target framework would leave ResolvedPackages empty and every test above
        // would pass by finding nothing.
        var resolved = ResolvedPackages();

        Assert.Contains("Uno.WinUI", resolved);
        Assert.Contains("WinUI.TableView", resolved);
        Assert.Contains("Markdig", resolved);
    }

    static HashSet<string> ResolvedPackages()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ReadRepoFile("src", "packages.lock.json")));
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var framework in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            // Both the plain target framework and its per-RID sections, e.g. "net10.0-desktop1.0/linux-x64".
            if (framework.Name.StartsWith("net10.0-desktop", StringComparison.OrdinalIgnoreCase) == false)
            {
                continue;
            }

            foreach (var package in framework.Value.EnumerateObject())
            {
                packages.Add(package.Name);
            }
        }

        return packages;
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
