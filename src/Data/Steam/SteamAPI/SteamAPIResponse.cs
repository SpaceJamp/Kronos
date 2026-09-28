using System.Text.Json.Serialization;

namespace Kronos.Data.Steam.SteamAPI;

internal class SteamAPIResponse<T>
{
    [JsonPropertyName("response")]
    public T? Response { get; set; }
}
