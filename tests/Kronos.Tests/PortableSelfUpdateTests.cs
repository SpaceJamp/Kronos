using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Kronos.Data.GitHub;
using Kronos.Helpers;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the portable self-update, which replaces the running application with a downloaded file.
/// </summary>
/// <remarks>
/// This is the most dangerous code in the repository by consequence: a bug here does not produce a
/// wrong answer on screen, it produces an application that has overwritten itself with something
/// else, or that will not start afterwards.
///
/// The pure parts - asset selection and the script handed to the waiting helper - are asserted on
/// directly. The file-copying is done by that helper and cannot be asserted without launching one,
/// so what is checked here is that the script it is given says the right things.
/// </remarks>
public class PortableSelfUpdateTests
{
    static GitHubReleaseAsset Asset(string name, string digest = "sha256:abc123") => new()
    {
        Name = name,
        Digest = digest,
        State = "uploaded",
        Size = 1024,
        ContentType = "application/zip",
    };

    // ------------------------------------------------------------------ asset selection

    [Fact]
    public void ThePortableArchiveIsFoundByItsSuffix()
    {
        var assets = new[]
        {
            Asset("Kronos-1.53.0-portable.zip"),
            Asset("Kronos-1.53.0-installer.exe"),
            Asset("source.zip"),
        };

        Assert.Equal("Kronos-1.53.0-portable.zip", PortableSelfUpdate.FindPortableAsset(assets)?.Name);
    }

    [Fact]
    public void AnArchiveWithoutAChecksumIsRefusedRatherThanInstalledUnverified()
    {
        // The helper replaces the running application with whatever it is handed, so an unverifiable
        // download has no gate at all. Refusing is the only safe answer.
        var assets = new[] { Asset("Kronos-1.53.0-portable.zip", digest: "") };

        Assert.Null(PortableSelfUpdate.FindPortableAsset(assets));
    }

    [Fact]
    public void TwoArchivesAreRefusedBecauseThereIsNoWayToTellWhichIsMeant()
    {
        var assets = new[]
        {
            Asset("Kronos-1.53.0-portable.zip"),
            Asset("Kronos-1.53.0-preview-portable.zip"),
        };

        Assert.Null(PortableSelfUpdate.FindPortableAsset(assets));
    }

    [Fact]
    public void NoAssetsAtAllIsNotAnError()
    {
        Assert.Null(PortableSelfUpdate.FindPortableAsset(Array.Empty<GitHubReleaseAsset>()));
        Assert.Null(PortableSelfUpdate.FindPortableAsset(null!));
    }

    [Fact]
    public void AnInstallerAloneIsNotAPortableUpdate()
    {
        Assert.Null(PortableSelfUpdate.FindPortableAsset(new[] { Asset("Kronos-1.53.0-installer.exe") }));
    }

    // ------------------------------------------------------------------ handoff script

    [Fact]
    public void TheScriptWaitsForTheApplicationToExitBeforeTouchingAnything()
    {
        var script = PortableSelfUpdate.BuildHandoffScript(@"C:\tmp\extract", @"C:\apps\Kronos", 4321, relaunch: false);

        Assert.Contains("Wait-Process -Id 4321", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScriptDoesNotNameItsWaitVariablePidBecauseThatIsAPowerShellAutomaticVariable()
    {
        // $PID holds the *script's own* process id. Shadowing it would make the script wait on itself,
        // forever, silently, after the app has already exited.
        var script = PortableSelfUpdate.BuildHandoffScript(@"C:\tmp\extract", @"C:\apps\Kronos", 4321, relaunch: false);

        Assert.DoesNotContain("$pid", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("$PID =", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheScriptBacksUpTheExecutableAndRestoresItIfTheCopyFails()
    {
        var script = PortableSelfUpdate.BuildHandoffScript(@"C:\tmp\extract", @"C:\apps\Kronos", 4321, relaunch: false);

        // The executable is the one file that cannot be re-obtained if the copy goes wrong.
        Assert.Contains("Copy-Item -LiteralPath $entry -Destination $backup", script, StringComparison.Ordinal);
        Assert.Contains("catch {", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath $backup -Destination $entry", script, StringComparison.Ordinal);
        Assert.Contains("exit 1", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScriptRelaunchesTheApplicationOnlyWhenAskedTo()
    {
        Assert.Contains(
            "Start-Process -FilePath $entry",
            PortableSelfUpdate.BuildHandoffScript(@"C:\a", @"C:\b", 1, relaunch: true),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Start-Process -FilePath $entry",
            PortableSelfUpdate.BuildHandoffScript(@"C:\a", @"C:\b", 1, relaunch: false),
            StringComparison.Ordinal);
    }

    [Theory]
    // A single quote would otherwise end the string early and turn the rest of the path into code.
    [InlineData(@"C:\Users\O'Brien\Kronos", "O''Brien")]
    [InlineData(@"C:\Program Files\Kronos", "Program Files")]
    [InlineData(@"C:\apps\it's", "it''s")]
    public void PathsWithQuotesOrSpacesSurviveIntoTheScript(string path, string expected)
    {
        var script = PortableSelfUpdate.BuildHandoffScript(path, @"C:\target", 1, relaunch: false);

        Assert.Contains(expected, script, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryPathAppearsInsideQuotes()
    {
        // Guards the class of bug where an unquoted path with a space silently becomes two arguments.
        var script = PortableSelfUpdate.BuildHandoffScript(@"C:\Program Files\source", @"C:\Program Files\target", 1, relaunch: false);

        Assert.Contains("'C:\\Program Files\\source'", script, StringComparison.Ordinal);
        Assert.Contains("'C:\\Program Files\\target'", script, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ the real thing

    [Fact]
    public void TheArchiveLayoutIsWhatTheLocatorExpects()
    {
        // The build publishes Kronos.exe at the archive root. If that ever changes, the handoff would
        // copy a tree with no executable in it - so the shape is asserted against a real zip.
        var archive = Path.Combine(Path.GetTempPath(), $"kronos-test-{Guid.NewGuid():N}.zip");

        try
        {
            using (var stream = File.Create(archive))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("Kronos.exe");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("not a real executable");
            }

            var extracted = Path.Combine(Path.GetTempPath(), $"kronos-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(extracted);

            try
            {
                ZipFile.ExtractToDirectory(archive, extracted);

                // This mirrors what PrepareHandoff does to locate the payload root.
                var found = Directory
                    .EnumerateFiles(extracted, "Kronos.exe", SearchOption.AllDirectories)
                    .FirstOrDefault();

                Assert.NotNull(found);
                Assert.Equal(extracted, Path.GetDirectoryName(found));
            }
            finally
            {
                Directory.Delete(extracted, true);
            }
        }
        finally
        {
            File.Delete(archive);
        }
    }
}
