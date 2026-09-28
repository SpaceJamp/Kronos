using System.Text.Json.Serialization;
using Kronos.Interfaces;

namespace Kronos.Data;

public class GameLibrarySettings
{
    [JsonPropertyName("GameLibrary")]
    [JsonConverter(typeof(JsonStringEnumConverter<GameLibrary>))]
    public GameLibrary GameLibrary { get; set; }

    [JsonPropertyName("IsEnabled")]
    public bool IsEnabled { get; set; } = true;
}
