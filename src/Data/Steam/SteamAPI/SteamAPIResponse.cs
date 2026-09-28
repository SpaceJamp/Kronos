using System.Text.Json.Serialization;

namespace Chronos.Data.Steam.SteamAPI;

internal class SteamAPIResponse<T>
{
    [JsonPropertyName("response")]
    public T? Response { get; set; }
}
