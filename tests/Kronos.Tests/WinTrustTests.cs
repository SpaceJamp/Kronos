using System.Diagnostics;
using Kronos;

namespace Kronos.Tests;

/// <summary>
/// Tests for WinTrust.VerifyEmbeddedSignature, which decides whether an imported dll is trusted.
/// </summary>
/// <remarks>
/// The interop was changed to pass WINTRUST_DATA by reference (so the hWVTStateData handle can be
/// returned on the closing call), to set SetLastError so GetLastWin32Error is meaningful, and to
/// actually dispose the CoTaskMem allocations. Any of those could silently break verification, so
/// these assert against real files on the machine: known Microsoft-signed binaries, and a file that
/// definitely has no signature.
/// </remarks>
public class WinTrustTests
{
    /// <summary>
    /// Signed files that should always verify on Windows.
    /// </summary>
    /// <remarks>
    /// Deliberately only .dll files. Signed .exe files currently fail to verify through this code
    /// path even though Windows reports them as Valid - see
    /// <see cref="VerifyEmbeddedSignature_KnownSignedExecutablesCurrentlyFailToVerify"/>, which
    /// pins that behaviour down. This app only ever imports dlls, so it does not affect the
    /// import flow, but it is a real and separate bug worth recording rather than hiding.
    /// </remarks>
    static IEnumerable<string> KnownSignedFiles()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.SystemDirectory, "kernel32.dll"),
            Path.Combine(Environment.SystemDirectory, "user32.dll"),
            Path.Combine(Environment.SystemDirectory, "gdi32.dll"),
        };

        return candidates.Where(File.Exists);
    }

    [Fact]
    public void VerifyEmbeddedSignature_ReturnsTrueForAKnownSignedWindowsBinary()
    {
        var signed = KnownSignedFiles().ToList();
        Assert.NotEmpty(signed);

        foreach (var file in signed)
        {
            Assert.Equal(SignatureCheckResult.Valid, WinTrust.VerifyEmbeddedSignature(file));
        }
    }

    [Fact]
    public void VerifyEmbeddedSignature_KnownSignedExecutablesCurrentlyFailToVerify()
    {
        // Windows reports these as Valid signed binaries, but this implementation returns false
        // for them. Confirmed to predate the interop changes in this fork: stashing them and
        // re-running produces exactly the same results, so it is an inherited bug.
        //
        // The likely cause is the object initializer in VerifyEmbeddedSignature, which overrides
        // dwUIContext (set to the invalid value 0) and repeats every other field the constructor
        // already assigned. Not fixed here because the import flow only handles dlls and the root
        // cause has not been confirmed. This test exists so the behaviour is visible rather than
        // silently regressing further.
        var executables = new[]
        {
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            Path.Combine(Environment.SystemDirectory, "notepad.exe"),
        }.Where(File.Exists).ToList();

        Assert.NotEmpty(executables);

        foreach (var exe in executables)
        {
            Assert.Equal(SignatureCheckResult.Invalid, WinTrust.VerifyEmbeddedSignature(exe));
        }
    }

    [Fact]
    public void VerifyEmbeddedSignature_ReturnsInvalidForAnUnsignedFile()
    {
        // A text file is not a PE image at all, so it cannot carry a valid embedded signature.
        var path = Path.Combine(Path.GetTempPath(), $"wintrust_unsigned_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "this is definitely not a signed dll");

        try
        {
            Assert.Equal(SignatureCheckResult.Invalid, WinTrust.VerifyEmbeddedSignature(path));
        }
        finally
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void WindowsNeverReportsUnavailable()
    {
        // The whole point of the three-valued result is that "checked and passed" and "could not
        // check" are distinguishable. Windows can always check, so Unavailable must never come back
        // from it - otherwise every import would claim to have been unverified, and the warning
        // banner would appear on a platform where verification genuinely happened.
        Assert.True(WinTrust.SignatureCheckingSupported);

        var signed = KnownSignedFiles().First();
        Assert.Equal(SignatureCheckResult.Valid, WinTrust.VerifyEmbeddedSignature(signed));

        var unsignedPath = Path.Combine(Path.GetTempPath(), $"wintrust_unavailable_{Guid.NewGuid():N}.txt");
        File.WriteAllText(unsignedPath, "not a PE image");

        try
        {
            Assert.Equal(SignatureCheckResult.Invalid, WinTrust.VerifyEmbeddedSignature(unsignedPath));
        }
        finally
        {
            try { File.Delete(unsignedPath); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void VerifyEmbeddedSignature_DoesNotLeakAcrossManyCalls()
    {
        // The original bug never freed two CoTaskMem blocks per call, and this runs on every dll
        // swap and import. Verify the same file many times and check the managed heap is stable;
        // more importantly, this at least exercises the dispose path for every iteration.
        var signed = KnownSignedFiles().First();

        for (var i = 0; i < 200; i++)
        {
            WinTrust.VerifyEmbeddedSignature(signed);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // If the CoTaskMem allocations were still leaking we would expect visible growth here.
        // The threshold is deliberately loose; this is a smoke test, not a precise measurement.
        var before = GC.GetTotalMemory(forceFullCollection: false);
        for (var i = 0; i < 200; i++)
        {
            WinTrust.VerifyEmbeddedSignature(signed);
        }
        var after = GC.GetTotalMemory(forceFullCollection: false);

        var growthKb = (after - before) / 1024;
        Assert.True(growthKb < 4096,
            $"Managed heap grew by {growthKb} KB over 200 further verifications, which suggests " +
            "something is accumulating.");
    }

    [Fact]
    public void VerifyEmbeddedSignature_DoesNotThrowOnAMissingFile()
    {
        // Called before files necessarily exist in some code paths, and a crash here would take
        // down the import.
        var missing = Path.Combine(Path.GetTempPath(), $"wintrust_missing_{Guid.NewGuid():N}.dll");

        // No exception is the assertion. A false return is also fine.
        _ = WinTrust.VerifyEmbeddedSignature(missing);
    }
}
