using System.IO;
using Chronos.Helpers;

namespace Chronos.Tests.Helpers;

/// <summary>
/// Tests for RepackDetector.
/// </summary>
/// <remarks>
/// The two "real repack" cases point at actual folders on the machine when they exist, and are
/// skipped otherwise, so the test suite still passes on a clean machine or in CI.
/// </remarks>
public class RepackDetectorTests : IDisposable
{
    // The real repacks observed on the test machine. Both are FitGirl releases whose folder names
    // carry no group tag at all, which is why name matching is not used.
    const string RealControlRepack = @"C:\Games\CONTROL Resonant";
    const string RealResonanceRepack = @"C:\Games\Resonance - A Plague Tale Legacy";

    readonly string _sandbox;

    public RepackDetectorTests()
    {
        _sandbox = Path.Combine(Storage.GetStorageFolder(), "repack_detector_tests", Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try { Directory.Delete(_sandbox, true); } catch { /* best effort */ }
    }

    /// <summary>Creates a throwaway game folder with the given files.</summary>
    string MakeGameFolder(string name, params string[] files)
    {
        var path = Path.Combine(_sandbox, name);
        Directory.CreateDirectory(path);
        foreach (var file in files)
        {
            var full = Path.Combine(path, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }

        return path;
    }

    [Fact]
    public void RealRepack_IsDetectedAsHighConfidence()
    {
        if (Directory.Exists(RealControlRepack) == false) { return; }

        var result = RepackDetector.Detect(RealControlRepack);

        Assert.True(result.IsLikelyRepack, "the real repack folder should be detected");
        Assert.Equal(RepackDetectionConfidence.High, result.Confidence);
        Assert.NotEmpty(result.Reasons);
    }

    [Fact]
    public void SecondRealRepack_IsAlsoDetected()
    {
        if (Directory.Exists(RealResonanceRepack) == false) { return; }

        var result = RepackDetector.Detect(RealResonanceRepack);

        Assert.True(result.IsLikelyRepack);
        Assert.Equal(RepackDetectionConfidence.High, result.Confidence);
    }

    [Fact]
    public void RealRepack_ReasonsNameFitGirlSpecifically()
    {
        if (Directory.Exists(RealControlRepack) == false) { return; }

        var result = RepackDetector.Detect(RealControlRepack);

        Assert.Contains(result.Reasons, r => r.Contains("FitGirl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlainGameFolder_IsNotDetected()
    {
        // A normal install: the game exe, its dlls, a redist folder, and nothing emu related.
        var path = MakeGameFolder("PlainGame",
            "MyGame.exe",
            "nvngx_dlss.dll",
            "engine.dll",
            "_Redist\\dxwebsetup.exe",
            "_Redist\\vc_redist.x64.exe",
            "unins000.exe",
            "unins000.dat");

        var result = RepackDetector.Detect(path);

        Assert.False(result.IsLikelyRepack);
        Assert.Equal(RepackDetectionConfidence.None, result.Confidence);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void SteamEmulatorConfig_IsHighConfidence()
    {
        var path = MakeGameFolder("Emulated", "Game.exe", "steam_emu.ini");

        var result = RepackDetector.Detect(path);

        Assert.True(result.IsLikelyRepack);
        Assert.Equal(RepackDetectionConfidence.High, result.Confidence);
    }

    [Fact]
    public void RunePayload_IsHighConfidence()
    {
        // The .rne sat next to a replaced Steam API dll rather than being explicitly listed.
        var path = MakeGameFolder("Rune", "Game.exe", "steam_api64.dll", "steam_api64.rne");

        var result = RepackDetector.Detect(path);

        Assert.True(result.IsLikelyRepack);
        Assert.Contains(result.Reasons, r => r.Contains("RUNE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FitGirlMarkerInRedist_IsHighConfidence()
    {
        var path = MakeGameFolder("FitGirlLike", "Game.exe", "_Redist\\fitgirl.md5", "_Redist\\QuickSFV.EXE");

        var result = RepackDetector.Detect(path);

        Assert.True(result.IsLikelyRepack);
        Assert.Equal(RepackDetectionConfidence.High, result.Confidence);
    }

    [Fact]
    public void WeakMarkerAlone_IsLowConfidenceAndDoesNotSuggest()
    {
        // A lone emulator dll is suggestive but not conclusive, so it must not pre-tick the flag.
        var path = MakeGameFolder("Weak", "Game.exe", "steam_emu64.dll");

        var result = RepackDetector.Detect(path);

        Assert.False(result.IsLikelyRepack);
        Assert.Equal(RepackDetectionConfidence.Low, result.Confidence);
        Assert.NotEmpty(result.Reasons);
    }

    [Fact]
    public void QuickSFVWithoutAGroupMarkerIsNotEnough()
    {
        // QuickSFV is a generic checksum tool, so on its own it must not imply a repack.
        var path = MakeGameFolder("QuickSfvOnly", "Game.exe", "_Redist\\QuickSFV.EXE", "_Redist\\dxwebsetup.exe");

        var result = RepackDetector.Detect(path);

        Assert.False(result.IsLikelyRepack);
        Assert.Equal(RepackDetectionConfidence.None, result.Confidence);
    }

    [Fact]
    public void FolderNameAloneNeverCausesDetection()
    {
        // The real repacks proved this is the failure mode of name matching: both were named with
        // no group tag whatsoever.
        var path = MakeGameFolder("Some Game FLT-R3PACK 2024", "Game.exe", "data.bin");

        var result = RepackDetector.Detect(path);

        Assert.False(result.IsLikelyRepack);
    }

    [Fact]
    public void MissingFolder_IsHandledGracefully()
    {
        var result = RepackDetector.Detect(Path.Combine(_sandbox, "does-not-exist"));

        Assert.False(result.IsLikelyRepack);
        Assert.Equal(RepackDetectionConfidence.None, result.Confidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPathIsHandledGracefully(string? path)
    {
        Assert.False(RepackDetector.Detect(path).IsLikelyRepack);
    }

    [Fact]
    public void DetectionIsCaseInsensitiveOnWindowsPaths()
    {
        var path = MakeGameFolder("Cased", "Game.exe", "STEAM_EMU.INI");

        Assert.True(RepackDetector.Detect(path).IsLikelyRepack);
    }
}
