using System;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace Kronos.Data.GitHub;

internal class GitHubRelease
{
    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;
    // https://github.com/beeradmoore/dlss-swapper/releases/tag/v0.9.8.0

    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;
    // v0.9.8.0

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    // v0.9.8.0

    [JsonPropertyName("draft")]
    public bool Draft { get; set; } = false;
    // false

    [JsonPropertyName("prerelease")]
    public bool PreRelease { get; set; } = false;
    // false

    [JsonPropertyName("created_at")]
    public string CreatedAt { get; set; } = string.Empty;
    // 2022-01-29T04:57:30Z

    [JsonIgnore]
    public DateTime CreateAtDateTime => DateTime.Parse(CreatedAt, CultureInfo.InvariantCulture);

    [JsonPropertyName("published_at")]
    public string PublishedAt { get; set; } = string.Empty;
    // 2022-01-29T05:02:29Z

    public DateTime PublishedAtDateTime => DateTime.Parse(PublishedAt, CultureInfo.InvariantCulture);

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;
    // ## What's Changed\r\n* Fixed issue where circular symbolic links would...

    [JsonPropertyName("assets")]
    public GitHubReleaseAsset[] Assets { get; set; } = [];

    /// <summary>
    /// The packed version of this release, or 0 if neither the name nor the tag carries one.
    /// </summary>
    /// <remarks>
    /// Upstream parses only <see cref="Name"/>, and requires its first space-delimited token to start
    /// with "v". That is a silent-failure trap: a release titled "Kronos 1.52 - bug fixes" yields 0,
    /// which compares below every real version, so <see cref="GitHubUpdater.IsNewerThan"/> reports
    /// the app as up to date forever and the update check never prompts. Nothing anywhere reports an
    /// error, because from the code's point of view there simply is no update.
    ///
    /// <see cref="TagName"/> is the field GitHub guarantees to be the version, so it is tried when the
    /// name does not parse. Publishing with a title that begins with the version is still the better
    /// habit - it is what the dialog displays - but it is no longer the only thing standing between a
    /// release and a permanently disabled updater.
    /// </remarks>
    internal ulong GetVersionNumber()
    {
        var fromName = ParseVersionToken(Name?.Split(" ").FirstOrDefault());
        return fromName != 0 ? fromName : ParseVersionToken(TagName);
    }

    /// <summary>
    /// Parses a single token such as "v1.52.0.0" into the packed form. Returns 0 if it is not one.
    /// </summary>
    private static ulong ParseVersionToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return 0;
        }

        var trimmed = token.Trim();
        if (!trimmed.StartsWith("v", StringComparison.InvariantCultureIgnoreCase))
        {
            return 0;
        }

        // This will split v1 through to v1.1.1.1 as 4 parts of the latest release version.
        ulong version = 0;
        var latestReleaseVersionParts = trimmed.Substring(1).Split(".");
        if (latestReleaseVersionParts.Length >= 1)
        {
            if (ulong.TryParse(latestReleaseVersionParts[0], out ulong latestReleaseMajor) == false)
            {
                return 0;
            }
            version += (latestReleaseMajor << 48);

            if (latestReleaseVersionParts.Length >= 2)
            {
                if (ulong.TryParse(latestReleaseVersionParts[1], out ulong latestReleaseMinor) == false)
                {
                    return 0;
                }
                version += (latestReleaseMinor << 32);

                if (latestReleaseVersionParts.Length >= 3)
                {
                    if (ulong.TryParse(latestReleaseVersionParts[2], out ulong latestReleaseBuild) == false)
                    {
                        return 0;
                    }
                    version += (latestReleaseBuild << 16);

                    if (latestReleaseVersionParts.Length >= 4)
                    {
                        if (ulong.TryParse(latestReleaseVersionParts[3], out ulong latestReleaseRevision) == false)
                        {
                            return 0;
                        }
                        version += latestReleaseRevision;
                    }
                }
            }

            return version;
        }
        else
        {
            // This shouldn't be able to happen, but if our list was 0 items this will be hit.
            return 0;
        }
    }
}
