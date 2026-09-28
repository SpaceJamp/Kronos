using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kronos.Data.Steam.SteamAPI;

internal class GetItemsResponse
{
    [JsonPropertyName("store_items")]
    public List<SteamStoreItem> StoreItems { get; set; } = new List<SteamStoreItem>();
}
