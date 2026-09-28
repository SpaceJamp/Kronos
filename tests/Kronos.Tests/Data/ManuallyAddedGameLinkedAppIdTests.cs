using Kronos.Data.ManuallyAdded;
using Kronos.Data.Steam;

namespace Kronos.Tests.Data;

/// <summary>
/// Tests for linking a manually added game to a store appid so it can borrow that game's cover.
/// </summary>
public class ManuallyAddedGameLinkedAppIdTests
{
    // The two repacks on the test machine and the appids of the store games they are copies of.
    const string ControlAppId = "3669870";
    const string ResonanceAppId = "2713000";

    [Fact]
    public void LinkedAppIdDefaultsToEmptySoUnlinkedGamesAreUnaffected()
    {
        var game = new ManuallyAddedGame("some-guid");

        Assert.Equal(string.Empty, game.LinkedAppId);
        Assert.False(game.HasValidLinkedAppId);
        Assert.Null(game.GetLinkedAppId());
    }

    [Theory]
    [InlineData(ControlAppId)]
    [InlineData(ResonanceAppId)]
    [InlineData("228980")]
    public void NumericAppIdIsAccepted(string appId)
    {
        var game = new ManuallyAddedGame("some-guid") { LinkedAppId = appId };

        Assert.True(game.HasValidLinkedAppId);
        Assert.Equal(int.Parse(appId), game.GetLinkedAppId());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("notanumber")]
    [InlineData("3669870abc")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("99999999999999")]
    public void NonAppIdTextIsRejectedRatherThanThrowing(string appId)
    {
        // The user types into a free text box, so anything has to be survivable. A bad value must
        // simply mean "no cover to fetch" and never take the add game dialog down with it.
        // Note "-1" and "0" both parse cleanly as Int32, so the range has to be checked explicitly
        // or they would be sent to Steam as genuine requests.
        var game = new ManuallyAddedGame("some-guid") { LinkedAppId = appId };

        Assert.False(game.HasValidLinkedAppId);
        Assert.Null(game.GetLinkedAppId());
    }

    [Theory]
    [InlineData("+3669870", 3669870)]
    [InlineData(" 3669870 ", 3669870)]
    public void SignAndWhitespaceAreTolerated(string appId, int expected)
    {
        // Both are harmless noise from copy and paste, and both still name the right app, so there
        // is no reason to reject them and leave the user with a blank tile.
        var game = new ManuallyAddedGame("some-guid") { LinkedAppId = appId };

        Assert.Equal(expected, game.GetLinkedAppId());
    }

    [Theory]
    [InlineData(" 3669870 ")]
    [InlineData("3669870\n")]
    public void AppIdWithSurroundingWhitespaceStillParses(string appId)
    {
        // Copy and paste out of a browser address bar or Steam often brings whitespace along.
        var game = new ManuallyAddedGame("some-guid") { LinkedAppId = appId };

        Assert.Equal(3669870, game.GetLinkedAppId());
    }


    [Fact]
    public void ChangingTheAppIdIsReportedAsAChange()
    {
        var existing = new ManuallyAddedGame("some-guid") { LinkedAppId = ControlAppId };
        var updated = new ManuallyAddedGame("some-guid") { LinkedAppId = ResonanceAppId };

        Assert.True(existing.UpdateFromGame(updated));
        Assert.Equal(ResonanceAppId, existing.LinkedAppId);
    }

    [Fact]
    public void UnchangedAppIdIsNotReportedAsAChange()
    {
        // The app rebuilds every game on each load, so a stable appid has to be a no-op or every
        // manually added game would be marked dirty (and its cover dropped) on every launch.
        var existing = new ManuallyAddedGame("some-guid") { LinkedAppId = ControlAppId };
        var same = new ManuallyAddedGame("some-guid") { LinkedAppId = ControlAppId };

        // Title and InstallPath are identical too, so nothing at all should differ.
        Assert.False(existing.UpdateFromGame(same));
    }
}

/// <summary>
/// Tests for turning Steam's asset url format into a usable cover url.
/// </summary>
public class SteamCoverUrlResolverTests
{
    // A real response from IStoreBrowseService for CONTROL Resonant (appid 3669870).
    const string RealAssetUrlFormat = "steam/apps/3669870/${FILENAME}?t=1790259464";
    const string RealLibraryCapsule2x = "7d4b4f430dc4d07e6562eb445456f9615cb52f77/library_capsule_2x.jpg";

    [Fact]
    public void BuildsOneUrlPerCdn()
    {
        var urls = SteamCoverUrlResolver.BuildCoverUrls(RealAssetUrlFormat, RealLibraryCapsule2x);

        Assert.Equal(SteamCoverUrlResolver.Cdns.Length, urls.Length);
        for (var i = 0; i < urls.Length; i++)
        {
            Assert.StartsWith(SteamCoverUrlResolver.Cdns[i], urls[i]);
        }
    }

    [Fact]
    public void SubstitutesThePlaceholderAndKeepsTheCacheBustingQuery()
    {
        var urls = SteamCoverUrlResolver.BuildCoverUrls(RealAssetUrlFormat, RealLibraryCapsule2x);

        // Dropping either half is the easy mistake here, and both break the download: a url with an
        // unreplaced ${FILENAME} 404s, and losing ?t= just makes Steam serve a stale cached asset.
        Assert.DoesNotContain("${FILENAME}", urls[0]);
        Assert.Contains(RealLibraryCapsule2x, urls[0]);
        Assert.Contains("?t=1790259464", urls[0]);
    }

    [Fact]
    public void ProducesTheUrlThatSteamActuallyServes()
    {
        var urls = SteamCoverUrlResolver.BuildCoverUrls(RealAssetUrlFormat, RealLibraryCapsule2x);

        Assert.Equal(
            "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/3669870/7d4b4f430dc4d07e6562eb445456f9615cb52f77/library_capsule_2x.jpg?t=1790259464",
            urls[0]);
    }

    [Fact]
    public void EveryCdnUrlDiffersOnlyByHost()
    {
        // The CDNs are only a fallback for each other, so the path must be identical across all of
        // them or a retry would be requesting something other than the asset that just failed.
        // The hostnames are different lengths, so compare from the asset path onwards rather than
        // by slicing off a fixed host length.
        var urls = SteamCoverUrlResolver.BuildCoverUrls(RealAssetUrlFormat, RealLibraryCapsule2x);

        const string pathMarker = "/store_item_assets/";
        var path = urls[0][urls[0].IndexOf(pathMarker, StringComparison.Ordinal)..];
        Assert.All(urls, url => Assert.Equal(path, url[url.IndexOf(pathMarker, StringComparison.Ordinal)..]));
    }

    [Theory]
    [InlineData("", "library_capsule_2x.jpg")]
    [InlineData("steam/apps/1/${FILENAME}", "")]
    [InlineData("steam/apps/1/${FILENAME}", "   ")]
    public void MissingAssetDetailsYieldNoUrlsRatherThanABrokenUrl(string format, string capsule)
    {
        // An app with no vertical cover is a normal thing to hit. Returning an empty list lets the
        // caller say "no artwork available", where a url with an empty hole in it would just fail
        // noisily and much later.
        Assert.Empty(SteamCoverUrlResolver.BuildCoverUrls(format, capsule));
    }

    [Fact]
    public void HandlesAnAssetFormatWithNoCacheBustingQuery()
    {
        var urls = SteamCoverUrlResolver.BuildCoverUrls("steam/apps/292030/${FILENAME}", "abc/library_capsule_2x.jpg");

        Assert.Equal(
            "https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/292030/abc/library_capsule_2x.jpg",
            urls[0]);
    }
}
