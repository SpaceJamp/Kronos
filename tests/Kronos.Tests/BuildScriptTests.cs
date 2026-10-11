using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the two build scripts against faults that only show up as a raw MSBuild or CLI error.
/// </summary>
/// <remarks>
/// These are shell scripts, so there is no compiler to catch a bad argument - it either works or it
/// fails at the first restore. Both faults here had already broken a build before they were caught.
///
/// The first is a framework filter passed to <c>dotnet restore</c>. <c>dotnet restore</c> has no
/// <c>--framework</c> option at all, and its <c>-f</c> means <c>--force</c>. So
/// <c>dotnet restore Kronos.csproj -f net10.0</c> parses as <c>--force net10.0</c>, and
/// <c>net10.0</c> is then forwarded to MSBuild as a second project path:
/// <c>MSB1008: Only one project can be specified.</c> Both scripts had this line, and neither had
/// ever successfully run it - the Windows path was only ever exercised with restore skipped, and the
/// Linux path failed at the first attempt. Note that <c>-f</c> is genuinely correct on
/// <c>dotnet build</c> and <c>dotnet publish</c>, which is what makes it easy to write here by mistake.
///
/// The second is a build and a publish that disagree about the runtime identifier. An RID-less
/// <c>build</c> followed by an RID'd <c>publish --no-build</c> looks for output under
/// <c>bin/&lt;cfg&gt;/&lt;tfm&gt;/&lt;rid&gt;/</c> that the build never produced, so publish fails on
/// missing files rather than on anything to do with the publish itself.
/// </remarks>
public class BuildScriptTests
{
    /// <summary>Scripts are checked by name, and every one must exist in the repo root.</summary>
    public static IEnumerable<object[]> BuildScripts => new[] { new object[] { "build.sh" }, new object[] { "build.ps1" } };

    static string ReadRepoFile(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && Directory.Exists(Path.Combine(dir.FullName, "src")) == false)
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);

        var path = Path.Combine(dir!.FullName, relativePath);
        Assert.True(File.Exists(path), $"Could not find {path}");

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Non-blank lines with comments removed. The scripts carry explanatory comments that legitimately
    /// name the very flags this class forbids, so matching raw text would test the prose.
    /// </summary>
    static IReadOnlyList<string> CodeLines(string relativePath) =>
        ReadRepoFile(relativePath)
            .Split('\n')
            .Select(line => line.TrimEnd('\r').Trim())
            .Where(line => line.Length > 0)
            .Where(line => !line.StartsWith("#", StringComparison.Ordinal)
                        && !line.StartsWith("//", StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// Extracts each top-level function body. Shell functions close on a brace in column zero and so do
    /// PowerShell ones, which is what makes this reliable on both without a real parser.
    /// </summary>
    static IReadOnlyList<(string Name, string Body)> Functions(string relativePath, string declarationPattern) =>
        Regex.Matches(ReadRepoFile(relativePath), declarationPattern, RegexOptions.Multiline)
            .Select(match => (match.Groups["name"].Value, match.Value))
            .ToList();

    /// <summary>
    /// Lines that invoke restore. build.sh branches on the pass index inside a <c>case</c>, so the
    /// command is not always at the start of the line.
    /// </summary>
    static IReadOnlyList<string> RestoreLines(string script) =>
        CodeLines(script)
            .Where(line => Regex.IsMatch(line, @"(^|\)\s*|\|\s*)dotnet\s+restore\b", RegexOptions.IgnoreCase))
            .ToList();

    // ------------------------------------------------------------------ restore framework filter

    [Theory]
    [MemberData(nameof(BuildScripts))]
    public void RestoreIsNeverGivenAFrameworkFilterSwitch(string script)
    {
        var restores = RestoreLines(script);

        // Guards against the assertion passing because the calls moved somewhere unrecognised.
        Assert.NotEmpty(restores);

        foreach (var line in restores)
        {
            // -f and --framework are the trap. -p:TargetFramework=net10.0 is deliberate and correct:
            // it is how the fallback passes avoid evaluating the Windows target framework.
            Assert.False(
                Regex.IsMatch(line, @"(?:^|\s)(-f|--framework)(?:\s|=|$)"),
                $"{script}: 'dotnet restore' has no --framework option and its -f means --force, so a "
                    + $"framework argument is passed to MSBuild as a second project (MSB1008). "
                    + $"Offending line: {line}");
        }
    }

    [Theory]
    [MemberData(nameof(BuildScripts))]
    public void RestoreIsActuallyPerformed(string script)
    {
        // The fix for the above is to drop the -f switch, not to drop the restore. Asserting the
        // restore still exists keeps a well-meaning edit from turning this into a --no-restore by
        // omission.
        Assert.NotEmpty(RestoreLines(script));
    }

    // ------------------------------------------------------------------ build/publish RID agreement

    [Theory]
    [InlineData("build.sh", @"(?ms)^(?<name>\w+)\s*\(\)\s*\{")]
    [InlineData("build.ps1", @"(?ms)^function\s+(?<name>[\w-]+)\s*\{")]
    public void TheBuildAndPublishOfEachFunctionAgreeOnTheRuntime(string script, string declarationPattern)
    {
        var functions = Functions(script, declarationPattern);
        Assert.NotEmpty(functions);

        foreach (var (name, body) in functions)
        {
            var invocations = body
                .Split('\n')
                .Select(line => line.TrimEnd('\r').Trim())
                .Where(line => Regex.IsMatch(line, @"^(\S*/)?dotnet\s+(build|publish)\b", RegexOptions.IgnoreCase))
                .ToList();

            if (invocations.Count == 0)
            {
                continue;
            }

            var runtimes = invocations
                .SelectMany(line => Regex.Matches(line, @"(?:^|\s)-r\s+""?\$?([A-Za-z0-9._-]+)""?(?:\s|$)"))
                .Select(match => match.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.NotEmpty(runtimes);

            Assert.True(
                runtimes.Count == 1,
                $"{script}: function '{name}' uses {runtimes.Count} different runtime identifiers "
                    + $"({string.Join(", ", runtimes)}). A build and publish that disagree on the RID make "
                    + $"publish look for bin/<cfg>/<tfm>/<rid>/ output the build never produced.");
        }
    }

    // ------------------------------------------------------------------ error reporting

    [Fact]
    public void EveryDotnetCommandInThePowerShellScriptIsFollowedByAnExitCodeCheck()
    {
        // $ErrorActionPreference = 'Stop' does not apply to native executables. Without an explicit
        // check, a failed restore is followed by `dotnet build --no-restore`, which then reports
        // "NETSDK1004: Assets file project.assets.json not found" - naming the symptom and burying the
        // restore error that actually caused it.
        var lines = CodeLines("build.ps1");

        var invocations = lines
            .Select((line, index) => (Line: line, Index: index))
            .Where(entry => Regex.IsMatch(entry.Line, @"^&?\s*(\S*/)?dotnet\s", RegexOptions.IgnoreCase))
            .ToList();

        Assert.NotEmpty(invocations);

        foreach (var (line, index) in invocations)
        {
            var next = index + 1;
            Assert.True(
                next < lines.Count
                    && (lines[next].Contains("Assert-DotnetSucceeded", StringComparison.Ordinal)
                        || lines[next].Contains("$LASTEXITCODE", StringComparison.Ordinal)),
                $"build.ps1: '{line}' is not followed by an exit code check. A native command that fails "
                    + $"does not throw, so the script would continue to the next step and report a later, "
                    + $"misleading error instead. Offending line: {line}");
        }
    }

    [Fact]
    public void TheBashScriptStopsOnTheFirstFailure()
    {
        // Without this, a failed step is followed by the next one and the user is told about the
        // consequence rather than the cause.
        Assert.Contains(CodeLines("build.sh"), line => line == "set -e");
    }

    [Theory]
    [InlineData("build.sh")]
    [InlineData("build.ps1")]
    public void RestoreIsVerifiedToHaveWrittenTheAssetsFile(string script)
    {
        // A restore that exits 0 without producing project.assets.json would otherwise surface as
        // NETSDK1004 from the --no-restore build that follows it.
        Assert.True(
            CodeLines(script).Any(line => line.Contains("project.assets.json", StringComparison.Ordinal)),
            $"{script}: nothing checks that restore actually wrote the assets file, so a restore that "
                + "quietly failed is reported as a missing file by the build step instead.");
    }

    [Fact]
    public void TheBashBuildFunctionsAreNotRunInsideACommandSubstitution()
    {
        // "$(build_linux ...)" runs the function in a subshell and captures its standard output, so
        // every progress message and every restore and compiler error was collected into an array
        // element instead of being printed. The build appeared to produce no output at all.
        // Matched against comment-stripped lines: the script explains this exact mistake in a
        // comment, and matching raw text would test the prose.
        Assert.False(
            CodeLines("build.sh").Any(line => Regex.IsMatch(line, @"\$\(\s*build_(?:linux|windows)")),
            "build.sh: the build functions are called in a command substitution, which swallows all of "
                + "their output including errors.");
    }

    // ------------------------------------------------------------------ restore fallback

    [Theory]
    [MemberData(nameof(BuildScripts))]
    public void RestoreIsTriedMoreThanOnceBeforeGivingUp(string script)
    {
        // A single restore command is not reliable everywhere. On Linux, evaluating the Windows target
        // framework can fail, and the script then has to fall back to restoring net10.0 alone rather
        // than stopping. Each attempt must therefore be a separate call, not a loop that reuses one.
        var restores = RestoreLines(script);

        Assert.True(
            restores.Count >= 2,
            $"{script}: found {restores.Count} restore command(s). A single one cannot fall back when "
                + "evaluating the Windows target framework fails on Linux.");
    }

    [Theory]
    [MemberData(nameof(BuildScripts))]
    public void TheCommittedLockFileIsSavedAndRestoredAroundASingleFrameworkRestore(string script)
    {
        // -p:TargetFramework=net10.0 rewrites packages.lock.json to hold only that framework, which
        // deletes 553 lines - the Windows target's entire dependency graph - from a committed file.
        var lines = CodeLines(script);
        var text = string.Join("\n", lines);

        Assert.True(
            text.Contains("packages.lock.json", StringComparison.Ordinal),
            $"{script}: a single-framework restore rewrites the committed packages.lock.json, but the "
                + "script never mentions it.");

        Assert.True(
            lines.Count(line => line.Contains("net10.0", StringComparison.Ordinal)) >= 2
                && Regex.IsMatch(text, @"net10\.0.*restore|restore.*net10\.0"),
            $"{script}: expected a fallback restore for net10.0 alongside the full restore.");
    }

    [Theory]
    [MemberData(nameof(BuildScripts))]
    public void PublishDoesNotRestoreAgain(string script)
    {
        // Publish runs its own restore by default, which would evaluate the Windows target framework
        // again on Linux and rewrite the committed lock file outside the protection above.
        foreach (var line in CodeLines(script).Where(line => Regex.IsMatch(line, @"^&?\s*(\S*/)?dotnet\s+publish\b", RegexOptions.IgnoreCase)))
        {
            Assert.True(
                line.Contains("--no-restore", StringComparison.Ordinal),
                $"{script}: publish without --no-restore restores again, defeating the fallback and "
                    + $"touching the committed lock file. Offending line: {line}");
        }
    }

    // ------------------------------------------------------------------ release publishing

    [Fact]
    public void TheReleaseIsTitledWithTheVersionLeadingAndNothingElse()
    {
        // GitHubUpdater.GetVersionNumber takes the first space-delimited token of the release *title*
        // and requires a leading "v". A title like "Kronos 1.53" therefore parses to 0, which compares
        // below every real version, so the app reports itself as permanently up to date and nothing is
        // logged. The tag fallback added later stops that, but the title should still be correct -
        // it is also what the update dialog displays.
        var source = ReadRepoFile("publish-release.ps1");

        var match = Regex.Match(source, @"--title\s+""([^""]*)""");
        Assert.True(match.Success, "publish-release.ps1 does not pass an explicit --title.");

        var template = match.Groups[1].Value;
        Assert.False(
            Regex.IsMatch(template, @"(?<![A-Za-z0-9])Kronos\s"),
            "The release title must not begin with the product name; the leading token is parsed as "
            + "the version.");

        // The template has to expand to something starting with v, whatever the version interpolates to.
        var expanded = template.Replace("$Version", "1.53");
        Assert.StartsWith("v", expanded, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(@"^v[0-9]", expanded);
    }

    [Fact]
    public void TheReleasePublishesAPrebuiltArtifactUnlessExplicitlySkipped()
    {
        var source = ReadRepoFile("publish-release.ps1");

        Assert.Contains("gh release upload", source, StringComparison.Ordinal);
        Assert.Contains("-SkipBuild", source, StringComparison.Ordinal);
        Assert.Contains("Kronos.exe", source, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ Windows portable packaging

    [Fact]
    public void TheWindowsPortableBuildPublishesThePortableConfiguration()
    {
        // The project has a Release_Portable configuration, and a portable zip should be built from
        // it. Not because Release is broken - a plain `-c Release` publish runs fine, which was
        // measured rather than assumed - but because Release_Portable defines PORTABLE, which compiles
        // out the updater's -installer.exe branch. Leaving that branch in would have a portable build
        // looking for an installer asset that a portable release never publishes.
        //
        // The variable, not the caller's -Configuration, is what reaches dotnet publish.
        var source = ReadRepoFile("build.ps1");

        Assert.True(
            source.Contains("'Release_Portable'", StringComparison.Ordinal),
            "build.ps1 no longer names Release_Portable, so it may be publishing the packaged Release "
                + "configuration, which leaves the installer auto-update branch compiled into a "
                + "portable build.");

        var publish = CodeLines("build.ps1")
            .FirstOrDefault(line => Regex.IsMatch(line, @"dotnet\s+publish.*windows10\.0", RegexOptions.IgnoreCase));

        Assert.NotNull(publish);
        Assert.True(
            publish!.Contains("$portableConfiguration", StringComparison.Ordinal),
            $"The Windows publish must use the portable configuration, not the caller's -Configuration. Offending line: {publish}");
    }

    [Theory]
    [InlineData("PublishSingleFile")]
    [InlineData("WindowsAppSDKSelfContained")]
    public void TheWindowsPortableBuildAvoidsThePropertiesThatBreakStartup(string property)
    {
        // Both were tried on a hunch, and both produce a binary that dies before showing a window.
        // Every combination was measured rather than reasoned about:
        //
        //   -c Release                  plain publish                   runs
        //   -c Release                  + PublishSingleFile=true       CRASHES   <- what shipped
        //   -c Release                  + WindowsAppSDKSelfContained   CRASHES
        //   -c Release_Portable         plain publish                   runs
        //   -c Release_Portable         + WindowsAppSDKSelfContained   CRASHES
        //
        // All the crashes are 0xC000027B, STATUS_STOWED_RESOURCE_NOT_FOUND, in Microsoft.UI.Xaml.dll.
        // PublishSingleFile is the clear mechanism: the csproj imports CopyPriFile.targets only when
        // it is not true, so the resource index is never embedded. WindowsAppSDKSelfContained is the
        // less obvious one - it looks like it would remove the runtime prerequisite and does the
        // opposite.
        var lines = CodeLines("build.ps1");
        var offending = lines.Where(line => line.Contains(property, StringComparison.Ordinal)).ToList();

        Assert.True(
            offending.TrueForAll(line => line.TrimStart().StartsWith("#", StringComparison.Ordinal)),
            $"build.ps1 sets {property} on the Windows publish, which produces a binary that crashes on "
                + $"startup. Offending line(s): {string.Join(" | ", offending)}");
    }

    [Fact]
    public void ThePublishedArchiveIsStagedRecursivelyAndItsSizeVerified()
    {
        // This shipped as v1.53: Copy-Item without -Recurse copied the *directories* but none of
        // their contents, and Compress-Archive then omits empty directories entirely. The archive
        // came out 97 files short - the whole of Assets, Translations and StoredData - and the only
        // symptom anyone noticed was the app's logo missing from the title bar.
        //
        // Nothing about that is a build failure. The build succeeded, every exit code was zero, and
        // the archive was a valid zip; it was simply short, and the shortfall only appears on the
        // user's desktop.
        var source = ReadRepoFile("publish-release.ps1");

        // Comments are stripped first: the script explains this exact mistake in prose, and matching
        // raw text would test the explanation rather than the code.
        var copy = CodeLines("publish-release.ps1")
            .FirstOrDefault(line => line.Contains("Copy-Item", StringComparison.Ordinal));

        Assert.NotNull(copy);
        Assert.Contains("-Recurse", copy!, StringComparison.Ordinal);

        // Counted rather than trusted, because the failure mode is a silently short archive.
        Assert.Contains("stagedCount", source, StringComparison.Ordinal);

        // Named explicitly, because these are what went missing.
        foreach (var required in new[] { "Kronos.exe", "Kronos.pri", @"Assets\icon_256.png" })
        {
            Assert.Contains(required, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheWindowsBuildSmokeTestsTheBinaryItIsAboutToPublish()
    {
        // The first artifact published exited immediately with 0xC000027B and nothing in the build
        // output said so. A configuration test cannot catch that class of fault on its own - it
        // encodes a belief about what breaks, and the belief was wrong twice - so the build starts
        // the binary and looks at it.
        var source = ReadRepoFile("build.ps1");

        Assert.Contains("Start-Process", source, StringComparison.Ordinal);
        Assert.Contains("HasExited", source, StringComparison.Ordinal);
        Assert.Contains("Kronos.pri", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePortableConfigurationIsAWindowedApplicationAndNotAConsoleOne()
    {
        // Release_Portable overrode the output type to Exe, which is the *console* subsystem, undoing
        // the WinExe the Windows target framework sets. Launching the shipped build then opened a
        // command prompt behind the app and left it there for as long as the app ran.
        //
        // Checked against the element rather than the absence of one, because a later group could
        // reintroduce it without this noticing.
        var csproj = ReadRepoFile(Path.Combine("src", "Kronos.csproj"));

        // Matched on the whole opening tag, because the condition has to be exactly this. There is
        // also a group conditioned on 'Release' OR 'Release_Portable', whose condition text contains
        // the same substring, and it carries Optimize and nothing else.
        const string opening = "<PropertyGroup Condition=\"'$(Configuration)'=='Release_Portable'\">";

        var start = csproj.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No {opening} found in src/Kronos.csproj.");

        var bodyStart = csproj.IndexOf('>', start) + 1;
        var bodyEnd = csproj.IndexOf("</PropertyGroup>", bodyStart, StringComparison.Ordinal);
        Assert.True(bodyEnd > bodyStart, "The Release_Portable PropertyGroup is not closed.");

        Assert.Contains(
            "<OutputType>WinExe</OutputType>",
            csproj.Substring(bodyStart, bodyEnd - bodyStart),
            StringComparison.Ordinal);
    }
}
