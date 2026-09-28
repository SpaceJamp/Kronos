using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Kronos.Data.Steam.SteamAPI;

namespace Kronos.Helpers;

/// <summary>
/// Works out which Steam appid a manually added game folder is a copy of, by searching Steam's
/// public store search and scoring the results against the folder name.
/// </summary>
/// <remarks>
/// The search and scoring parts are deliberately separated. <see cref="SelectBest"/> is pure, so
/// the part that decides whether a candidate is acceptable can be tested without a network call,
/// which matters because that decision is what stands between a repack and the wrong game's cover.
/// </remarks>
internal static class SteamAppIdLookup
{
    /// <summary>
    /// Minimum score, out of 100, before a candidate is good enough to use unprompted. Low enough to
    /// absorb the punctuation differences real folder names have ("Resonance - A Plague Tale
    /// Legacy" against "Resonance: A Plague Tale Legacy"), high enough to reject a merely related
    /// game.
    /// </summary>
    internal const int AutoAcceptScore = 80;

    /// <summary>
    /// Substrings that mark a search hit as an add-on rather than the base game. Steam returns
    /// these in the same list as games, and an expansion's artwork is not the artwork anyone wants
    /// on a game's tile.
    /// </summary>
    static readonly string[] AddOnMarkers =
    {
        "dlc", "soundtrack", "artbook", "expansion", "season pass", "season",
        "upgrade", "deluxe", "pass", "pack", "bundle", "kit", "ost",
        "demo", "trial", "playtest", "test", "toolkit", "video", "skin",
        "avatar", "background", "sound effects", "server",
    };

    /// <summary>
    /// Reduces a title to something comparable: lowercase, punctuation turned into spaces, runs of
    /// whitespace collapsed.
    /// </summary>
    /// <remarks>
    /// Needed because store titles and folder names disagree on punctuation far more often than
    /// they disagree on words. "Resonance - A Plague Tale Legacy" and "Resonance: A Plague Tale
    /// Legacy" are the same game and must score identically.
    /// </remarks>
    internal static string Normalise(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var chars = title.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// True when a store name looks like an add-on to another game rather than a game itself.
    /// </summary>
    internal static bool IsAddOnName(string? storeName)
    {
        var normalised = Normalise(storeName);
        if (normalised.Length == 0)
        {
            return true;
        }

        return AddOnMarkers.Any(marker =>
        {
            // Match on whole words so "Passage" is not read as containing "pass".
            var padded = $" {normalised} ";
            return padded.Contains($" {marker} ", StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Scores a store name against a folder name, 0 to 100.
    /// </summary>
    internal static int ScoreName(string folderName, string storeName)
    {
        var a = Normalise(folderName);
        var b = Normalise(storeName);
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (a == b)
        {
            return 100;
        }

        // Uses the project's own LevenshteinDistance rather than pulling in a second string
        // comparison, so the score is consistent with the other fuzzy title matching in the app.
        var distance = CommonHelpers.LevenshteinDistance(a, b);
        var longest = Math.Max(a.Length, b.Length);
        var score = (int)Math.Round(100.0 * (1.0 - ((double)distance / longest)));
        return Math.Clamp(score, 0, 100);
    }

    /// <summary>
    /// Picks the best candidate for a folder, or null when nothing scores highly enough.
    /// </summary>
    /// <param name="folderName">The game folder or title we are trying to identify.</param>
    /// <param name="candidates">Store search hits.</param>
    /// <param name="minimumScore">Lowest acceptable score.</param>
    internal static StoreSearchItem? SelectBest(string folderName, IEnumerable<StoreSearchItem> candidates, int minimumScore = AutoAcceptScore)
    {
        StoreSearchItem? best = null;
        var bestScore = -1;

        foreach (var candidate in candidates)
        {
            // Only base games, and only ones that actually run on Windows. This app cannot swap a
            // dll in a Linux-only or Mac-only title, so its cover would be misleading anyway.
            if (string.Equals(candidate.Type, "app", StringComparison.OrdinalIgnoreCase) == false)
            {
                continue;
            }

            if (candidate.Platforms is not null && candidate.Platforms.Windows == false)
            {
                continue;
            }

            if (IsAddOnName(candidate.Name))
            {
                continue;
            }

            var score = ScoreName(folderName, candidate.Name);
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        if (best is null || bestScore < minimumScore)
        {
            return null;
        }

        return best;
    }

    /// <summary>
    /// Searches Steam for a game matching the given title and returns the best candidate, or null.
    /// </summary>
    /// <remarks>
    /// Steam's search is sensitive to punctuation. Searching the folder name verbatim for
    /// "Resonance - A Plague Tale Legacy" returns nothing at all, while the same words without the
    /// hyphen returns the game. So the raw title is tried first and a normalised one second, rather
    /// than always sending the normalised form, since the raw form is more precise when it works.
    /// </remarks>
    internal static async Task<StoreSearchItem?> FindBestMatchAsync(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        foreach (var query in BuildQueries(title))
        {
            var items = await SearchAsync(query).ConfigureAwait(false);
            if (items.Count == 0)
            {
                continue;
            }

            var best = SelectBest(title, items);
            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    /// <summary>
    /// The search terms to try, most precise first, with duplicates removed.
    /// </summary>
    internal static IEnumerable<string> BuildQueries(string title)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var trimmed = title.Trim();

        if (seen.Add(trimmed))
        {
            yield return trimmed;
        }

        // A repack folder is often the store title with the punctuation changed, or with the
        // publisher tag glued on, so fall back to the words alone.
        var normalised = Normalise(title);
        if (normalised.Length > 0 && seen.Add(normalised))
        {
            yield return normalised;
        }
    }

    static async Task<List<StoreSearchItem>> SearchAsync(string query)
    {
        try
        {
            var urlEncoded = HttpUtility.UrlEncode(query);
            var url = $"https://store.steampowered.com/api/storesearch/?term={urlEncoded}&l=en&cc=US";

            using var response = await App.CurrentApp.HttpClient
                .GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode == false)
            {
                Logger.Error($"Steam store search returned {response.StatusCode} for \"{query}\".");
                return new List<StoreSearchItem>();
            }

            using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var parsed = await JsonSerializer
                .DeserializeAsync(stream, SourceGenerationContext.Default.StoreSearchResponse)
                .ConfigureAwait(false);

            return parsed?.Items ?? new List<StoreSearchItem>();
        }
        catch (Exception err)
        {
            // A failed lookup must never stop a game being added, it just means no cover.
            Logger.Error(err, $"Steam store search failed for \"{query}\".");
            return new List<StoreSearchItem>();
        }
    }
}
