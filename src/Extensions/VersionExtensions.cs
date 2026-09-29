using System;

namespace Kronos.Extensions;

internal static class VersionExtensions
{
    internal static ulong GetVersionNumber(this Version version)
    {
        return ((ulong)version.Major << 48) +
                ((ulong)version.Minor << 32) +
                ((ulong)version.Build << 16) +
                ((ulong)version.Revision);
    }

    /// <summary>
    /// Parses a file version string into a comparable number, or returns null when it is not one.
    /// </summary>
    /// <remarks>
    /// Versions reach us as <c>FileVersionInfo.GetFormattedFileVersion()</c> output, which is a
    /// display string rather than a guaranteed four part number. Observed shapes include "2.5.0.0",
    /// "1.0" and "10.0.22000.114", but nothing guarantees the format, so anything unparseable
    /// returns null and the caller treats it as "version unknown" rather than guessing.
    ///
    /// Parsing takes the first four dot separated runs of digits and ignores everything after them.
    /// That is deliberate: a trailing " (release)" or a fifth component must not make an otherwise
    /// comparable version look unknown, because the cost of that is losing the downgrade warning
    /// exactly when a version is unusual.
    ///
    /// The number packs identically to <see cref="GetVersionNumber"/>, so two values parsed here
    /// and two Versions parsed by the app compare consistently. Missing parts are zero, so "2.5"
    /// equals "2.5.0.0".
    /// </remarks>
    internal static ulong? TryParseVersionNumber(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        Span<ulong> parts = stackalloc ulong[4];
        var partIndex = 0;
        ulong current = 0;
        var sawDigit = false;

        foreach (var c in version)
        {
            if (char.IsAsciiDigit(c))
            {
                // Saturate rather than overflow. A version component that large is nonsense, but
                // wrapping would order it below a small one, which is worse than treating it as big.
                current = current > (ulong.MaxValue - (ulong)(c - '0')) / 10
                    ? ulong.MaxValue
                    : current * 10 + (ulong)(c - '0');
                sawDigit = true;
                continue;
            }

            if (sawDigit)
            {
                if (partIndex >= parts.Length)
                {
                    // Four components is all Version can hold. The rest is ignored on purpose.
                    break;
                }

                parts[partIndex++] = current;
                current = 0;
                sawDigit = false;
            }

            if (c == '.')
            {
                continue;
            }

            // Any other character ends the numeric run. "2.5.0.0-beta" stops here with 2.5.0.0
            // already collected, and "abc" yields nothing at all.
            break;
        }

        if (sawDigit && partIndex < parts.Length)
        {
            parts[partIndex++] = current;
        }

        if (partIndex == 0)
        {
            return null;
        }

        return (parts[0] << 48) + (parts[1] << 32) + (parts[2] << 16) + parts[3];
    }

    /// <summary>
    /// Compares two version strings, returning null when either cannot be parsed.
    /// </summary>
    /// <remarks>
    /// Returns -1, 0 or 1 for older, equal and newer respectively, and null for "cannot tell". A
    /// null result is not a licence to assume new is fine, which is why the caller warns on null
    /// too: an unparseable version is exactly the case worth being cautious about.
    /// </remarks>
    internal static int? CompareVersionStrings(string? left, string? right)
    {
        var leftNumber = TryParseVersionNumber(left);
        var rightNumber = TryParseVersionNumber(right);

        if (leftNumber is null || rightNumber is null)
        {
            return null;
        }

        return leftNumber.Value.CompareTo(rightNumber.Value);
    }

    /// <summary>
    /// Whether a version string looks like it came from a real file, for logging.
    /// </summary>
    internal static string DescribeVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "unknown";
        }

        var trimmed = version.Trim();
        var parsed = TryParseVersionNumber(trimmed);

        return parsed is null
            ? $"{trimmed} (unrecognised)"
            : trimmed;
    }

    /// <summary>
    /// Formats a version string for display, trimming trailing zero components.
    /// </summary>
    /// <remarks>
    /// Used so a warning reads "2.5" rather than "2.5.0.0", matching how
    /// <see cref="Data.GameAsset.DisplayVersion"/> presents the same value elsewhere in the UI. Kept
    /// here so the two cannot drift.
    /// </remarks>
    internal static string FormatVersionForDisplay(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return "unknown";
        }

        var trimmed = version.Trim();

        // Only trim when what remains is still parseable, so a single number is not reduced to "".
        var span = trimmed.AsSpan();
        while (span.Length > 0 && span.EndsWith(".0", StringComparison.Ordinal))
        {
            var candidate = span[..^2];
            if (TryParseVersionNumber(candidate.ToString()) is null)
            {
                break;
            }

            span = candidate;
        }

        return span.ToString();
    }
}
