using System.Text.Json.Serialization;

namespace Chronos.Data.Steam.SteamAPI;

internal class SteamStoreItemRelatedItems
{
    [JsonPropertyName("parent_appid")]
    public int ParentAppId { get; set; }
}
