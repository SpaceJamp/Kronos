using System.Text.Json.Serialization;
using Chronos.Interfaces;

namespace Chronos.Data;

public class GameLibrarySettings
{
    [JsonPropertyName("GameLibrary")]
    [JsonConverter(typeof(JsonStringEnumConverter<GameLibrary>))]
    public GameLibrary GameLibrary { get; set; }

    [JsonPropertyName("IsEnabled")]
    public bool IsEnabled { get; set; } = true;
}
