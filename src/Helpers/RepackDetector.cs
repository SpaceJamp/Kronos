using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DLSS_Swapper.Helpers;

/// <summary>
/// How confident we are that a game folder is a repack rather than a normal store install.
/// </summary>
internal enum RepackDetectionConfidence
{
    /// <summary>No repack markers found. Treat as a normal install.</summary>
    None = 0,

    /// <summary>Only weak, generic markers matched. Not enough to act on without asking.</summary>
    Low = 1,

    /// <summary>A marker specific to repack tooling matched. Worth defaulting the flag to on.</summary>
    High = 2,
}

/// <summary>
/// Result of inspecting a game folder for repack markers.
/// </summary>
internal sealed class RepackDetectionResult
{
    internal RepackDetectionResult(bool isLikelyRepack, RepackDetectionConfidence confidence, IReadOnlyList<string> reasons)
    {
        IsLikelyRepack = isLikelyRepack;
        Confidence = confidence;
        Reasons = reasons;
    }

    /// <summary>True when the evidence is strong enough to suggest this to the user.</summary>
    internal bool IsLikelyRepack { get; }

    internal RepackDetectionConfidence Confidence { get; }

    /// <summary>Human readable description of every marker that matched, for display in the UI.</summary>
    internal IReadOnlyList<string> Reasons { get; }

    internal static RepackDetectionResult NotARepack { get; } =
        new(false, RepackDetectionConfidence.None, Array.Empty<string>());
}

/// <summary>
/// Detects whether a game folder looks like a repack (a "scene release") rather than a normal
/// store install.
/// </summary>
/// <remarks>
/// This only ever produces a *suggestion*. The stored per-game flag is the source of truth, because
/// these heuristics cannot be reliable: repack tooling varies wildly, and a false positive would
/// mislabel a legitimate game. Repacks are only ever seen by this app through the Manually Added
/// library, since store libraries report their own installed titles.
///
/// Folder and file *names* turned out to be useless as a signal in practice. Two real FitGirl
/// repacks on the test machine were named "CONTROL Resonant" and
/// "Resonance - A Plague Tale Legacy", with no group name anywhere, so a name pattern would have
/// scored zero. What both did carry were artefacts of the repacking/emulation tooling, which is
/// what this looks for.
/// </remarks>
internal static class RepackDetector
{
    /// <summary>
    /// Markers that only appear in combination with repack or emulation tooling. Each of these on
    /// its own is a strong signal.
    /// </summary>
    static readonly (string FileName, string Description)[] _strongRootMarkers =
    {
        // Steam emulator configuration. A legitimate Steam install never contains one.
        ("steam_emu.ini", "Steam emulator configuration (steam_emu.ini)"),
        ("SmartSteamEmu.ini", "SmartSteamEmu configuration"),

        // The RUNE Steam emulator writes its library as a .rne file alongside the real dll.
        ("steam_api64.rne", "RUNE-encrypted Steam API (steam_api64.rne)"),
        ("steam_api.rne", "RUNE-encrypted Steam API (steam_api.rne)"),

        // Group specific files. FitGirl drops these into an _Redist folder in every release.
        ("fitgirl.md5", "FitGirl checksum file"),
        ("fitgirl.cfg", "FitGirl configuration file"),
    };

    /// <summary>
    /// Markers that are suggestive but not conclusive on their own, because legitimate installers
    /// can produce them too. They only raise confidence when a strong marker is also present.
    /// </summary>
    static readonly (string FileName, string Description)[] _weakRootMarkers =
    {
        ("steam_emu64.dll", "Steam emulator library"),
        ("steam_emu.dll", "Steam emulator library"),
    };

    /// <summary>Subfolders checked for group specific files.</summary>
    static readonly string[] _markerSubfolders = { "_Redist", "Redist" };

    /// <summary>
    /// Inspects a game folder. Never throws: a missing or unreadable folder simply yields no match.
    /// </summary>
    /// <param name="installPath">Root folder of the game install.</param>
    internal static RepackDetectionResult Detect(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return RepackDetectionResult.NotARepack;
        }

        try
        {
            if (Directory.Exists(installPath) == false)
            {
                return RepackDetectionResult.NotARepack;
            }

            var reasons = new List<string>();

            // Strong markers: either directly in the game root, or inside one of the redist folders
            // that repack groups use to ship their own extras.
            var strongMatched = false;
            var strongSubfolderMatched = false;

            foreach (var (fileName, description) in _strongRootMarkers)
            {
                if (File.Exists(Path.Combine(installPath, fileName)))
                {
                    reasons.Add(description);
                    strongMatched = true;
                }
            }

            foreach (var marker in _strongRootMarkers)
            {
                foreach (var subfolder in _markerSubfolders)
                {
                    if (File.Exists(Path.Combine(installPath, subfolder, marker.FileName)))
                    {
                        reasons.Add($"{marker.Description} in {subfolder}");
                        strongMatched = true;
                        strongSubfolderMatched = true;
                    }
                }
            }

            // RUNE writes a .rne next to each Steam API dll it replaces.
            if (strongMatched == false)
            {
                strongMatched |= HasRunePayload(installPath, reasons);
            }

            var weakMatched = false;
            foreach (var (fileName, description) in _weakRootMarkers)
            {
                if (File.Exists(Path.Combine(installPath, fileName)))
                {
                    reasons.Add(description);
                    weakMatched = true;
                }
            }

            // QuickSFV ships inside FitGirl's _Redist but is a generic checksum tool, so on its own
            // it proves nothing. It is only worth mentioning when we already believe this is a
            // repack, because it usefully confirms which group produced it.
            if (strongSubfolderMatched)
            {
                foreach (var subfolder in _markerSubfolders)
                {
                    var quickSfv = Directory
                        .EnumerateFiles(Path.Combine(installPath, subfolder), "QuickSFV*", SearchOption.TopDirectoryOnly)
                        .FirstOrDefault();

                    if (quickSfv is not null)
                    {
                        reasons.Add($"Repack integrity checker in {subfolder} ({Path.GetFileName(quickSfv)})");
                    }

                    break;
                }
            }

            if (strongMatched)
            {
                return new RepackDetectionResult(true, RepackDetectionConfidence.High, reasons);
            }

            if (weakMatched)
            {
                return new RepackDetectionResult(false, RepackDetectionConfidence.Low, reasons);
            }

            return RepackDetectionResult.NotARepack;
        }
        catch (Exception err)
        {
            // Detection is a convenience. Never let it break adding a game.
            Logger.Verbose($"Repack detection failed for {installPath}: {err.Message}");
            return RepackDetectionResult.NotARepack;
        }
    }

    /// <summary>
    /// Looks for the RUNE emulator's .rne payload files, which can sit next to any Steam API dll.
    /// </summary>
    static bool HasRunePayload(string installPath, List<string> reasons)
    {
        try
        {
            var found = Directory
                .EnumerateFiles(installPath, "*.rne", SearchOption.TopDirectoryOnly)
                .ToList();

            foreach (var file in found)
            {
                reasons.Add($"RUNE-encrypted Steam API ({Path.GetFileName(file)})");
            }

            return found.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
