using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kronos.Data;

public class HashedKnownDLL
{
    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    [JsonPropertyName("sources")]
    public Dictionary<string, List<string>> Sources { get; set; } = new Dictionary<string, List<string>>();
}
