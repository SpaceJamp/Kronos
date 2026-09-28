using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kronos.Data.Steam.SteamAPI;

/// <summary>
/// Response shape of Steam's public store search endpoint.
/// </summary>
internal class StoreSearchResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<StoreSearchItem> Items { get; set; } = new List<StoreSearchItem>();
}

/// <summary>
/// One search hit. The endpoint returns add-ons (DLC, soundtracks, upgrades) in the same list as
/// base games, so <see cref="Type"/> and <see cref="Name"/> are both needed to tell them apart.
/// </summary>
internal class StoreSearchItem
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("tiny_image")]
    public string TinyImage { get; set; } = string.Empty;

    [JsonPropertyName("platforms")]
    public StoreSearchPlatforms? Platforms { get; set; }
}

internal class StoreSearchPlatforms
{
    [JsonPropertyName("windows")]
    public bool Windows { get; set; }

    [JsonPropertyName("mac")]
    public bool Mac { get; set; }

    [JsonPropertyName("linux")]
    public bool Linux { get; set; }
}
