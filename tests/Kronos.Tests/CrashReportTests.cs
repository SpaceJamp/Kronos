using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the unhandled-exception reporter.
/// </summary>
/// <remarks>
/// The handler used to log, close Serilog and let the process die. Now it shows the user what happened,
/// where the log is, and keeps the app alive - which is a real behavioural change, and one that only ever
/// runs when something has already gone wrong, so it is exactly the kind of code that gets no testing by
/// accident.
///
/// CrashReport is static and string-only precisely so it can be asserted on here. A crash reporter that can
/// only be tested by crashing the app is a crash reporter with no tests.
/// </remarks>
public class CrashReportTests
{
    [Fact]
    public void CancellationIsNotReportedAsACrash()
    {
        // Closing a dialog mid-download throws TaskCanceledException. Telling someone they had a crash
        // because they cancelled something is worse than saying nothing.
        Assert.False(CrashReport.ShouldReport(new TaskCanceledException()));
        Assert.False(CrashReport.ShouldReport(new OperationCanceledException()));
    }

    [Fact]
    public void ARealFailureIsReported()
    {
        Assert.True(CrashReport.ShouldReport(new InvalidOperationException("boom")));
        Assert.True(CrashReport.ShouldReport(new AggregateException(new Exception("a"), new Exception("b"))));
        Assert.True(CrashReport.ShouldReport(new IOException("disk")));
    }

    [Fact]
    public void OutOfMemoryIsLoggedButNotShown()
    {
        // By the time this is thrown the process is usually in no state to build a dialog, and building one
        // allocates strings. Still logged; not surfaced.
        Assert.False(CrashReport.ShouldReport(new OutOfMemoryException()));
    }

    [Fact]
    public void NullIsNotReported()
    {
        Assert.False(CrashReport.ShouldReport(null!));
    }

    [Fact]
    public void TheReportNamesTheExceptionAndTheLog()
    {
        var report = CrashReport.Build(new InvalidOperationException("the DLL was locked"), @"C:\apps\Kronos\logs");

        Assert.Contains("InvalidOperationException", report, StringComparison.Ordinal);
        Assert.Contains("the DLL was locked", report, StringComparison.Ordinal);
        Assert.Contains(@"C:\apps\Kronos\logs", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportReassuresAboutSavedWork()
    {
        // The user's first question after a crash is "did I lose anything". Answer it before they ask.
        var report = CrashReport.Build(new InvalidOperationException("x"), null);

        Assert.Contains("already saved is safe", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NestedErrorsAreListedBecauseTheOuterMessageIsUseless()
    {
        var aggregate = new AggregateException(
            new InvalidOperationException("inner one"),
            new IOException("inner two"));

        var report = CrashReport.Build(aggregate, null);

        Assert.Contains("inner one", report, StringComparison.Ordinal);
        Assert.Contains("inner two", report, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReportIsTruncatedRatherThanProducingAnUnreadableWall()
    {
        // A dialog nobody can read past its own scrollbar is not a report.
        var huge = new InvalidOperationException(new string('x', 50_000));

        var report = CrashReport.Build(huge, null);

        Assert.True(report.Length < 7000, $"report was {report.Length} characters");
        Assert.Contains("truncated", report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheTitleIsOneLineAndBounded()
    {
        // Newlines would break a title bar, and a full message is unreadable at that size.
        var title = CrashReport.BuildTitle(new InvalidOperationException("first line\nsecond line\nthird"));

        Assert.DoesNotContain("\n", title);
        Assert.True(title.Length < 160, $"title was {title.Length} characters");
    }

    [Fact]
    public void TheTitleSurvivesAnExceptionWithNoMessage()
    {
        var title = CrashReport.BuildTitle(new Exception(string.Empty));

        Assert.Contains("Exception", title, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnwritableInstallDoesNotBreakTheCrashReporter()
    {
        // TryGetLogPathForDisplay resolves a storage path, which touches the filesystem, while the app is
        // already failing. It has to degrade to null rather than throw a second exception.
        var path = CrashReport.TryGetLogPathForDisplay(DateTime.Now);

        // Either a real path or null; what must not happen is an exception.
        Assert.True(path is null || path.Length > 0);
    }
}

/// <summary>
/// Guards the wiring of both new user-facing behaviours.
/// </summary>
/// <remarks>
/// The behaviour of each is tested in its own class. What is asserted here is that the parts are actually
/// connected, because an unreferenced, untested feature is the state UpdateErrorLog sat in when it shipped
/// in 1.58.1: fully tested, and invisible to every user.
/// </remarks>
public class NewFeatureWiringTests
{
    static string Source(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kronos.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray()));
    }

    [Fact]
    public void ThePartialUpdateLogIsActuallyShownToTheUser()
    {
        // UpdateErrorLog shipped in 1.58.1 as a tested reader that nothing called. This is the assertion that
        // it is no longer in that state.
        var source = Source("src", "MainWindow.xaml.cs");

        Assert.Contains("ShowUpdateErrorReportIfPresentAsync", source, StringComparison.Ordinal);
        Assert.Contains("UpdateErrorLog.TryRead", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePartialUpdateReportIsShownBeforeOtherStartupPrompts()
    {
        // A half-applied update is the most likely explanation for whatever the user is told next.
        var source = Source("src", "MainWindow.xaml.cs");

        var report = source.IndexOf("await ShowUpdateErrorReportIfPresentAsync()", StringComparison.Ordinal);
        var multiplayer = source.IndexOf("HasShownMultiplayerWarning", StringComparison.Ordinal);

        Assert.True(report > 0, "the report must actually be awaited");
        Assert.True(report < multiplayer, "the update report must come before the other startup prompts");
    }

    [Fact]
    public void TheCrashHandlerKeepsTheAppAlive()
    {
        // The behavioural change: e.Handled stops WinUI propagating the exception, so the app no longer
        // vanishes. Losing an operation is recoverable; losing the window forces a full library re-scan.
        var source = Source("src", "App.xaml.cs");

        Assert.Contains("e.Handled = true", source, StringComparison.Ordinal);
        Assert.Contains("CrashReport.ShouldReport", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCrashHandlerLogsBeforeItTriesToShowAnything()
    {
        var source = Source("src", "App.xaml.cs");

        // Search from the method's own signature rather than its first mention, which is inside its doc
        // comment and would put the window reference before the logging one.
        var start = source.IndexOf("private void App_UnhandledException", StringComparison.Ordinal);
        var body = source.Substring(start, Math.Min(2000, source.Length - start));

        var log = body.IndexOf("Serilog.Log.Error", StringComparison.Ordinal);
        var window = body.IndexOf("new CrashWindow", StringComparison.Ordinal);

        Assert.True(log > 0, "the exception must be logged");
        Assert.True(window > log, "logging must come before anything that can itself fail");
    }

    [Fact]
    public void TheCrashWindowOffersAWayToReachTheLog()
    {
        // A crash report the user cannot act on is just a longer stack trace.
        //
        // Asserted on the XAML, which is where the buttons are bound from. The commands themselves are
        // source-generated from [RelayCommand] methods in the model, so their names never appear in the
        // code-behind - looking for "OpenLogFolderCommand" there would find nothing and would be testing
        // the wrong file.
        var xaml = Source("src", "CrashWindow.xaml");

        Assert.Contains("OpenLogFolderCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("CopyLogPathCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("CopyTextCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryTranslationKeyTheNewWindowsUseExists()
    {
        // The XAML compiler reports a missing translation property behind a binding as an opaque internal
        // error with no file or line, which cost real time to track down. This turns that into a message
        // that names the problem.
        var resw = Source("src", "Translations", "en-US", "Resources.resw");

        var required = new[]
        {
            "MainWindow_UpdateIncomplete_Title",
            "MainWindow_UpdateIncomplete_ShowLog",
            "CrashWindow_WindowTitle",
            "CrashWindow_ExplanationText",
            "CrashWindow_OpenLogFolder",
            "CrashWindow_CopyLogPath",
        };

        var missing = required.Where(key => resw.Contains($"name=\"{key}\"", StringComparison.Ordinal) == false).ToList();

        Assert.True(missing.Count == 0, $"Missing translation keys: {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryTranslationPropertyTheCrashWindowBindsToIsDeclared()
    {
        // The same failure as above, caught from the C# side where the error message is useful.
        var xaml = Source("src", "CrashWindow.xaml");
        var model = Source("src", "CrashWindowModelTranslationProperties.cs");

        var referenced = System.Text.RegularExpressions.Regex
            .Matches(xaml, @"TranslationProperties\.(\w+)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        var declared = System.Text.RegularExpressions.Regex
            .Matches(model, @"public string (\w+) =>")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var undeclared = referenced.Where(name => declared.Contains(name) == false).ToList();

        Assert.True(undeclared.Count == 0, $"Bound but not declared: {string.Join(", ", undeclared)}");
    }
}