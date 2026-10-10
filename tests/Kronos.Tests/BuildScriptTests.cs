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

    // ------------------------------------------------------------------ restore framework filter

    [Theory]
    [MemberData(nameof(BuildScripts))]
    public void RestoreIsNeverGivenAFrameworkFilter(string script)
    {
        var restores = CodeLines(script)
            .Where(line => Regex.IsMatch(line, @"^(\S*/)?dotnet\s+restore\b", RegexOptions.IgnoreCase))
            .ToList();

        // Guards against the assertion passing because the filter moved somewhere unrecognised.
        Assert.NotEmpty(restores);

        foreach (var line in restores)
        {
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
        // The fix for the above is to drop the filter, not to drop the restore. Asserting the restore
        // still exists keeps a well-meaning edit from turning this into a --no-restore-by-omission.
        Assert.Contains(CodeLines(script), line => Regex.IsMatch(line, @"^(\S*/)?dotnet\s+restore\b", RegexOptions.IgnoreCase));
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
            .Where(entry => Regex.IsMatch(entry.Line, @"^(\S*/)?dotnet\s", RegexOptions.IgnoreCase))
            .ToList();

        Assert.NotEmpty(invocations);

        foreach (var (line, index) in invocations)
        {
            var next = index + 1;
            Assert.True(
                next < lines.Count && lines[next].Contains("Assert-DotnetSucceeded", StringComparison.Ordinal),
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
}
