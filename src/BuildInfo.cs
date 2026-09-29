
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace Kronos;

/// <summary>
/// Build metadata, populated at compile time from attributes the csproj injects.
/// </summary>
/// <remarks>
/// Every field here used to be an auto property that nothing ever assigned. GitBranch, GitCommit and
/// GitTag were therefore always empty and BuildTimestamp was always 0, which meant
/// <see cref="BuildDateTime"/> returned 1 January 1970 and the Settings page displayed that as the
/// build date. The commit line was blank for the same reason.
///
/// GitBranch, GitCommit and GitTag come from the build scripts, which pass them in with -p: because
/// MSBuild cannot run git while evaluating properties. The timestamp is computed by the csproj itself,
/// so it is present in every build including one made straight from an IDE.
///
/// Everything here degrades rather than throwing. A build with no metadata at all, such as one made
/// from an exported archive with no git, leaves the strings empty and the timestamp null, and the
/// consumers handle that instead of assuming it is a release build.
/// </remarks>
internal static class BuildInfo
{
    /// <summary>
    /// Prefix shared by every injected metadata key, so they cannot collide with the SDK's own
    /// AssemblyMetadata keys.
    /// </summary>
    const string MetadataPrefix = "Kronos";

    static string? ReadMetadata(string name)
    {
        var value = typeof(BuildInfo).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => string.Equals(x.Key, MetadataPrefix + name, StringComparison.Ordinal))?
            .Value;

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Branch the build was made from, or empty when git was not available.
    /// </summary>
    public static string GitBranch { get; } = ReadMetadata("GitBranch") ?? string.Empty;

    /// <summary>
    /// Full commit hash the build was made from, or empty.
    /// </summary>
    public static string GitCommit { get; } = ReadMetadata("GitCommit") ?? string.Empty;

    /// <summary>
    /// Tag the build was made from, or empty.
    /// </summary>
    public static string GitTag { get; } = ReadMetadata("GitTag") ?? string.Empty;

    /// <summary>
    /// When the build was made, in UTC, or null when the build recorded no timestamp.
    /// </summary>
    /// <remarks>
    /// Null rather than a default value, because the default reads as a real date. This is the
    /// distinction that matters: zero, or DateTimeOffset.MinValue, formats as 1 January 1970, and that
    /// is what the Settings page used to show.
    /// </remarks>
    public static DateTimeOffset? BuildTimestamp { get; } = ParseTimestamp(ReadMetadata("BuildTimestamp"));

    /// <summary>
    /// Whether a timestamp was actually recorded, as opposed to being missing.
    /// </summary>
    /// <remarks>
    /// Anything formatting a build date has to check this rather than formatting
    /// <see cref="BuildDateTime"/> unconditionally.
    /// </remarks>
    public static bool HasBuildTimestamp => BuildTimestamp.HasValue;

    /// <summary>
    /// Parses an ISO 8601 round-trip timestamp, returning null for anything unparseable.
    /// </summary>
    /// <remarks>
    /// The csproj writes the timestamp with DateTime.UtcNow.ToString("o"), which is round-trip format
    /// and therefore carries an explicit UTC offset. RoundtripKind is required so that the offset is
    /// honoured rather than assumed to be local time, which would shift the build date for anyone west
    /// of UTC.
    ///
    /// An unparseable value is treated as missing rather than as an error, because a build that
    /// recorded a date it could not read is no more able to report its build date than one that
    /// recorded nothing, and throwing here would take the whole app down over a cosmetic field.
    /// </remarks>
    internal static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
                ? parsed
                : null;
    }

    /// <summary>
    /// The build time in local time, or null when the build recorded none.
    /// </summary>
    public static DateTime? BuildDateTime => BuildTimestamp?.LocalDateTime;

    /// <summary>
    /// The build time for display, or an honest placeholder when there was none.
    /// </summary>
    /// <remarks>
    /// The placeholder is deliberate. Showing 1 January 1970, which is what an unconditional
    /// conversion of an unset value produces, reads as a real build date and is worse than admitting
    /// the build did not record one.
    /// </remarks>
    public static string BuildDateTimeFormattedString => BuildDateTime?.ToString("g", CultureInfo.CurrentCulture)
        ?? "unknown";

    /// <summary>
    /// The first seven characters of the commit hash, or empty when there is none.
    /// </summary>
    /// <remarks>
    /// A commit hash is 40 characters, but the Scripts that pass it in substitute the literal string
    /// "unknown" when git was unavailable, so the length is checked rather than assumed.
    /// </remarks>
    public static string GitCommitShort
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GitCommit) || GitCommit.Length < 7)
            {
                return string.Empty;
            }

            return GitCommit.Substring(0, 7);
        }
    }

    /// <summary>
    /// Whether this build came from a tagged commit, which is what decides whether the Settings page
    /// offers a link to the release notes.
    /// </summary>
    public static bool IsFromTagBuild => string.IsNullOrWhiteSpace(GitTag) == false;
}
