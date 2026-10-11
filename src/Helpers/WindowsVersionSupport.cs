using System;

namespace Kronos.Helpers;

/// <summary>
/// The oldest Windows this build will run on.
/// </summary>
/// <remarks>
/// Separated from the launch path so the rule can be asserted on directly. The version arithmetic is
/// the kind of thing that looks right and is not: comparing <see cref="Version"/> objects across a
/// major-version boundary, or comparing Build without accounting for Major, both give plausible wrong
/// answers.
/// </remarks>
public static class WindowsVersionSupport
{
    /// <summary>
    /// Windows 10 22H2, build 19045 — the final Windows 10 release.
    /// </summary>
    /// <remarks>
    /// Not 19041, which is what <c>TargetPlatformMinVersion</c> declares. That is 21H2. 19045 is the
    /// final Windows 10 release, and everything below it has been out of support for years — so the
    /// reason to enforce a floor rather than merely target one is that a user on one of those builds
    /// gets security fixes for nothing, not even for Kronos.
    ///
    /// This used to be justified partly by the WebView2 runtime, on the claim that Kronos hosts one.
    /// It does not, and never did — there was no WebView2 control anywhere in the app, only a package
    /// reference carried over from upstream. The floor did not move, because it stands on its own, but
    /// the reasoning was carrying a false statement to anyone who read it.
    ///
    /// Windows 11 reports 10.0.22000 and above, so it passes on the Major==10 path without needing a
    /// separate case. Its builds are all higher than 19045.
    /// </remarks>
    public static readonly Version MinimumSupported = new(10, 0, 19045);

    /// <summary>Human-readable form of <see cref="MinimumSupported"/>, for the refusal window.</summary>
    public const string MinimumSupportedDisplay = "Windows 10 version 22H2 (build 19045) or newer";

    /// <summary>
    /// Whether an operating system version is new enough to run this build.
    /// </summary>
    /// <remarks>
    /// Fails closed. A version that cannot be read, or that reports a Major below 10, is refused rather
    /// than allowed, because the alternative is a check that can be defeated by making the version read
    /// as something unexpected.
    /// </remarks>
    public static bool IsSupported(Version? osVersion)
    {
        if (osVersion is null)
        {
            return false;
        }

        if (osVersion.Major < 10)
        {
            return false;
        }

        // A later Windows is newer by definition. This has to be settled before the build comparison
        // rather than folded into it, because Windows build numbers are not comparable across major
        // versions - a hypothetical 12.0.9999 is newer than 10.0.19045, but its build number is lower,
        // and comparing the two would refuse it.
        if (osVersion.Major > 10)
        {
            return true;
        }

        // Major is now known to be exactly 10, which is what makes this comparison meaningful. The
        // case it exists to catch is the 6.2.9200.0 the legacy GetVersionEx shim reports: a build
        // number far above the floor under a lower major, which a bare Build comparison waves through.
        return osVersion.Build >= MinimumSupported.Build;
    }

    /// <summary>Whether the machine this is running on is new enough.</summary>
    public static bool IsSupportedCurrentMachine() => IsSupported(Environment.OSVersion.Version);

    /// <summary>
    /// Why the machine was refused, for the log.
    /// </summary>
    /// <remarks>
    /// The reported version goes in, because the whole value of the message is telling someone what to
    /// look up, and they cannot look up what they were not told.
    /// </remarks>
    public static string DescribeRefusal(Version? osVersion)
    {
        var reported = osVersion is null
            ? "unknown"
            : $"{osVersion.Major}.{osVersion.Minor}.{osVersion.Build}";

        return $"Refusing to launch: Windows {reported} is older than the required {MinimumSupportedDisplay}. " +
               "Kronos needs the Windows App SDK, which is only serviced on current Windows, so running " +
               "on an out-of-support build would mean running against an unpatched component rather than " +
               "merely an old copy of Kronos.";
    }
}
