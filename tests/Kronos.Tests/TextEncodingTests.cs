using System;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Kronos.Tests;

/// <summary>
/// Guards the text encoding of files that are edited by hand and read by people.
/// </summary>
/// <remarks>
/// The README was corrupted this way once: a one-line edit to change a number was done with
/// <c>(Get-Content -Raw) ... | Set-Content -Encoding utf8</c>, and Windows PowerShell's Get-Content
/// defaults to the ANSI codepage for a file with no byte-order mark. So the UTF-8 bytes were decoded
/// as Windows-1252 and written back out as UTF-8, turning every em dash into three characters of
/// mojibake, every arrow into another three, and both platform-status emoji into nonsense. It also
/// added a BOM.
///
/// Fifty-eight lines. It looked like a change to two numbers, and it passed the build, because nothing
/// compiles a README.
/// </remarks>
public class TextEncodingTests
{
    /// <summary>
    /// Characters in the Latin-1 supplement range, which is where UTF-8 continuation bytes land.
    /// </summary>
    /// <remarks>
    /// In English prose these are vanishingly rare, so their presence is a signal rather than a
    /// heuristic. Note the reverse of the rule: this checks for mojibake, not for mojibake's absence,
    /// so a genuinely accented word in a future translation would need this list narrowed rather than
    /// the check removed.
    /// </remarks>
    static readonly int[] SuspectCodePoints = { 0xC2, 0xC3, 0xE2, 0xEF, 0xF0 };

    public static TheoryData<string> DocumentationFiles() => new()
    {
        "README.md",
        "AI_ASSISTED.md",
        "CONTRIBUTING.md",
        "NOTICE",
    };

    [Theory]
    [MemberData(nameof(DocumentationFiles))]
    public void DocumentationIsValidUtf8WithoutAByteOrderMark(string relativePath)
    {
        var bytes = File.ReadAllBytes(RepoFile(relativePath));

        // Strict decode: throws rather than substituting replacement characters, so a file that is not
        // UTF-8 at all is caught here instead of being quietly repaired somewhere downstream.
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        try
        {
            _ = strict.GetString(bytes);
        }
        catch (DecoderFallbackException err)
        {
            Assert.Fail($"{relativePath} is not valid UTF-8: {err.Message}");
        }

        // A BOM is legal UTF-8 but not wanted. It renders as an invisible character at the start of the
        // file in some viewers, and it is what Set-Content -Encoding utf8 adds under Windows PowerShell,
        // so its presence is the fingerprint of the same mishandling.
        var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;

        Assert.False(hasBom, $"{relativePath} starts with a UTF-8 byte-order mark");
    }

    [Theory]
    [MemberData(nameof(DocumentationFiles))]
    public void DocumentationContainsNoMojibake(string relativePath)
    {
        var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFile(relativePath)));

        var offenders = text
            .Split('\n')
            .Select((line, index) => (Line: line, Number: index + 1))
            .Where(entry => entry.Line.Any(c => SuspectCodePoints.Contains(c)))
            .ToList();

        // Guarded rather than passed as the message to Assert.True. The message argument is evaluated
        // eagerly, before the assertion runs, so offenders[0] would throw on exactly the case this test
        // exists to report - a clean file. Which it did, on all four, the first time it ran.
        if (offenders.Count != 0)
        {
            Assert.Fail(
                $"{relativePath} contains {offenders.Count} line(s) with characters typical of UTF-8 "
                    + "decoded as Windows-1252. First: line " + offenders[0].Number + ". This is what "
                    + "(Get-Content -Raw) | Set-Content -Encoding utf8 does to a BOM-less UTF-8 file "
                    + "under Windows PowerShell.");
        }
    }

    [Fact]
    public void TheReadmesNonAsciiCharactersSurviveARoundTrip()
    {
        // The specific characters the corruption destroyed, asserted so that a future edit which loses
        // them fails loudly. Without this the test above would still pass on a file where someone had
        // deleted every em dash to make the problem go away - which is exactly what happened here.
        var text = new UTF8Encoding(false, true).GetString(File.ReadAllBytes(RepoFile("README.md")));

        foreach (var (name, character) in new[]
        {
            ("em dash", '\u2014'),
            ("right arrow", '\u2192'),
            ("section sign", '\u00A7'),
            ("middle dot", '\u00B7'),
            ("ellipsis", '\u2026'),
        })
        {
            Assert.True(
                text.Contains(character),
                $"README.md no longer contains a {name} (U+{(int)character:X4}). It may have been "
                    + "stripped rather than repaired, which hides the encoding fault rather than fixing it.");
        }
    }

    static string RepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && File.Exists(Path.Combine(directory.FullName, "Kronos.sln")) == false)
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var path = Path.Combine(directory!.FullName, relativePath);
        Assert.True(File.Exists(path), $"Could not find {path}");
        return path;
    }
}
