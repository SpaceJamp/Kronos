using System.Collections.Generic;
using System.Text.Json.Serialization;
using Kronos.Data.BattleNet;
using Kronos.Data.DLSS;
using Kronos.Data.EAApp;
using Kronos.Data.Steam.SteamAPI;
using Microsoft.UI.Windowing;

namespace Kronos;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(Data.GitHub.GitHubRelease))]
[JsonSerializable(typeof(Data.EpicGamesStore.CacheItem[]))]
[JsonSerializable(typeof(Data.EpicGamesStore.ManifestFile))]
[JsonSerializable(typeof(Data.GOG.LimitedDetail.LimitedDetailImages))]
[JsonSerializable(typeof(Data.GOG.GamePiece.GamePieceOriginalImages))]
[JsonSerializable(typeof(Data.GOG.ResourceImages))]
[JsonSerializable(typeof(Data.GOG.GOGEmbedFilteredResponse))]
[JsonSerializable(typeof(Data.GOG.GOGCatalogResponse))]
[JsonSerializable(typeof(Data.GOG.GOGProduct))]
[JsonSerializable(typeof(Data.Manifest))]
[JsonSerializable(typeof(Data.DLLRecord))]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(Data.WindowPositionRect))]
[JsonSerializable(typeof(OverlappedPresenterState))]
[JsonSerializable(typeof(Data.HashedKnownDLL))]
[JsonSerializable(typeof(Data.GameLibrarySettings))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(SteamAPIResponse<GetItemsResponse>))]
[JsonSerializable(typeof(Aggregate))]
[JsonSerializable(typeof(List<GameSearchResult>))]
[JsonSerializable(typeof(List<PresetOption>))]
[JsonSerializable(typeof(GetItemsInput))]
[JsonSerializable(typeof(StoreSearchResponse))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}
