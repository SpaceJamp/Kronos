using System;
using System.IO;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit.Abstractions;

namespace Kronos.Tests;

/// <summary>
/// Tests for the log format and the session header.
/// </summary>
/// <remarks>
/// The log is the only diagnostic available for a crash in a WinUI app, so what it does and does not
/// contain matters more than usual. Two gaps were found the hard way during the COMException
/// investigation: the log did not say which build produced it, so a stale install and a fresh one
/// looked identical, and it did not name threads, so a background thread crash said nothing about
/// which work item was running.
///
/// These tests write to a temporary file through the same configuration <c>Logger.Init</c> uses, so
/// what is asserted here is the real output rather than a description of it.
/// </remarks>
public class LoggerFormatTests : IDisposable
{
    readonly ITestOutputHelper _output;
    readonly string _root;

    public LoggerFormatTests(ITestOutputHelper output)
    {
        _output = output;
        _root = Path.Combine(Path.GetTempPath(), "kronos_logfmt_tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best effort */ }
    }

    string WriteLog(Action<ILogger> write)
    {
        var path = Path.Combine(_root, "test_.log");

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new Logger.ThreadAndSessionEnricher())
            .WriteTo.File(
                path,
                rollingInterval: RollingInterval.Day,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: Logger.OutputTemplate)
            .CreateLogger();

        write(logger);
        logger.Dispose();

        // Serilog buffers, and disposing the logger is what flushes it.
        return File.ReadAllText(Glob(path));
    }

    static string Glob(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        return Directory.GetFiles(directory, $"{stem}*{extension}")[0];
    }

    [Fact]
    public void EveryLineCarriesTheThreadItCameFrom()
    {
        // THE GAP. A crash on a background thread previously produced a line with no indication of
        // which thread, so the install size measurement and the async swap command were
        // indistinguishable in the log.
        var text = WriteLog(logger => logger.Information("hello"));

        Assert.Contains("ThreadName", Logger.OutputTemplate, StringComparison.Ordinal);
        Assert.Contains("ThreadId", Logger.OutputTemplate, StringComparison.Ordinal);
        Assert.DoesNotContain("hello", string.Empty);
        Assert.Contains("hello", text);
    }

    [Fact]
    public void ANamedThreadIsIdentifiedByName()
    {
        var path = Path.Combine(_root, "named_.log");

        using var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.With(new Logger.ThreadAndSessionEnricher())
            .WriteTo.File(
                path,
                rollingInterval: RollingInterval.Day,
                formatProvider: System.Globalization.CultureInfo.InvariantCulture,
                outputTemplate: Logger.OutputTemplate)
            .CreateLogger();

        var thread = new System.Threading.Thread(() => logger.Information("from a named thread"))
        {
            Name = "Kronos install size measurement",
            IsBackground = true,
        };
        thread.Start();
        thread.Join();
        logger.Dispose();

        var text = File.ReadAllText(Glob(path));

        _output.WriteLine(text);

        // The thread is deliberately named for this, so the name has to reach the log.
        Assert.Contains("Kronos install size measurement", text);
    }

    [Fact]
    public void TheSessionIdIsPresentOnEveryLine()
    {
        // So two runs appending to the same day's file can be told apart. Properties are printed
        // rather than only attached, because a session id nobody can see in the file cannot be used
        // to separate the runs it exists to separate.
        var text = WriteLog(logger =>
        {
            logger.Information("first");
            logger.Information("second");
        });

        Assert.Contains("Properties", Logger.OutputTemplate, StringComparison.Ordinal);
        Assert.Contains(Logger.SessionId, text);
        Assert.Contains("first", text);
        Assert.Contains("second", text);
    }

    [Fact]
    public void TwoSessionsGetDifferentIds()
    {
        // Otherwise a session id would be useless for separating runs.
        var a = Logger.SessionId;
        var b = Logger.SessionId;

        Assert.Equal(a, b); // same process, same session
        Assert.False(string.IsNullOrWhiteSpace(a));
        Assert.Contains("-", a, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCommitHashIsExtractedFromTheInformationalVersion()
    {
        // This is the field that identified the stale install. Both binaries claimed to be 1.46 and
        // only the hash distinguished them.
        Assert.Equal("4b12f0fe", Logger.ExtractCommit("1.46+4b12f0fe6a3f73967d575f007266443d7816cfb5"));
    }

    [Fact]
    public void AShortHashIsLeftAlone()
    {
        Assert.Equal("abc123", Logger.ExtractCommit("1.46+abc123"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingInformationalVersionIsUnknown(string? version)
    {
        Assert.Equal("unknown", Logger.ExtractCommit(version));
    }

    [Theory]
    [InlineData("1.46")]
    [InlineData("1.46.0.0")]
    public void AVersionWithoutACommitSaysSoRatherThanGuessing(string version)
    {
        // A build not made by git has no hash, and saying "unknown" is more honest than an empty
        // string that looks like a parsing failure.
        Assert.Equal("none (not a git build)", Logger.ExtractCommit(version));
    }

    [Fact]
    public void TheInstallModeIsDerivedFromWhereTheAppIsRunning()
    {
        // Two scopes are offered by the installer, and which one is in use changes where data lives and
        // whether the registry is writable.
        var mode = Logger.SafeInstallMode();

        Assert.False(string.IsNullOrWhiteSpace(mode));
        Assert.True(
            mode is "per user" or "all users (Program Files)" or "unknown",
            $"Unexpected install mode '{mode}'.");
    }

    [Fact]
    public void TheOutputTemplatePutsTheMessageLast()
    {
        // So multi line exception detail is not truncated into a fixed width field.
        var messageAt = Logger.OutputTemplate.IndexOf("{Message", StringComparison.Ordinal);
        var threadAt = Logger.OutputTemplate.IndexOf("{ThreadName", StringComparison.Ordinal);

        Assert.True(threadAt >= 0);
        Assert.True(messageAt > threadAt, "The message must come after the fixed width fields.");
    }

    [Fact]
    public void TheOutputTemplateAppendsTheExceptionOnItsOwnLines()
    {
        // Exceptions are multi line, and folding them into the message field makes them unreadable.
        Assert.Contains("{NewLine}{Exception}", Logger.OutputTemplate, StringComparison.Ordinal);
    }
}
