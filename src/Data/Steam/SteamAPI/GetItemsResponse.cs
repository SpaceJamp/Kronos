using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Chronos.Data.Steam.SteamAPI;

internal class GetItemsResponse
{
    [JsonPropertyName("store_items")]
    public List<SteamStoreItem> StoreItems { get; set; } = new List<SteamStoreItem>();
}
