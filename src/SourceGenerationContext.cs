using System.Collections.Generic;
using System.Text.Json.Serialization;
using Kronos.Data;
using Kronos.Data.EAApp;
using Kronos.Data.GitHub;
using Kronos.Data.Steam.SteamAPI;

#if WINDOWS
using Kronos.Data.BattleNet;
using Kronos.Data.DLSS;
using Kronos.Data.EpicGamesStore;
using Kronos.Data.GOG;
using Microsoft.UI.Windowing;
#endif

namespace Kronos;

[JsonSourceGenerationOptions(WriteIndented = true)]
// Cross-platform types (available on both Windows and Linux)
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(Data.GameLibrarySettings))]
[JsonSerializable(typeof(Data.HashedKnownDLL))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(SteamAPIResponse<GetItemsResponse>))]
// The Steam store request body, used by SteamCoverUrlResolver on both targets.
[JsonSerializable(typeof(GetItemsInput))]
// The DLL manifest and its records. Both targets download the same manifest and both need to
// deserialize it, so these cannot live under the Windows-only block below - without them the
// Linux target cannot read a manifest at all.
[JsonSerializable(typeof(Data.Manifest))]
[JsonSerializable(typeof(Data.DLLRecord))]
// Linux-only types (GitHub API DTOs for updater)
#if LINUX
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubAsset))]
#endif
// Windows-only library types
#if WINDOWS
[JsonSerializable(typeof(Data.GitHub.GitHubRelease))]
[JsonSerializable(typeof(Data.GitHub.GitHubReleaseAsset))]
[JsonSerializable(typeof(Data.DLSS.PresetOption))]
[JsonSerializable(typeof(List<PresetOption>))]
[JsonSerializable(typeof(Data.EpicGamesStore.CacheItem[]))]
[JsonSerializable(typeof(Data.EpicGamesStore.ManifestFile))]
[JsonSerializable(typeof(Data.GOG.LimitedDetail.LimitedDetailImages))]
[JsonSerializable(typeof(Data.GOG.GamePiece.GamePieceOriginalImages))]
[JsonSerializable(typeof(Data.GOG.ResourceImages))]
[JsonSerializable(typeof(Data.GOG.GOGEmbedFilteredResponse))]
[JsonSerializable(typeof(Data.GOG.GOGCatalogResponse))]
[JsonSerializable(typeof(Data.GOG.GOGProduct))]
[JsonSerializable(typeof(Aggregate))]
[JsonSerializable(typeof(List<GameSearchResult>))]
[JsonSerializable(typeof(GetItemsInput))]
[JsonSerializable(typeof(StoreSearchResponse))]
[JsonSerializable(typeof(Microsoft.UI.Windowing.OverlappedPresenterState))]
#endif
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
