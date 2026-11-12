using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MusicVideoMeta.Api;

/// <summary>TheAudioDB artist search response.</summary>
public class TadbArtistSearch
{
    /// <summary>Gets or sets the artists.</summary>
    [JsonPropertyName("artists")]
    public List<TadbArtist>? Artists { get; set; }
}

/// <summary>TheAudioDB artist.</summary>
public class TadbArtist
{
    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("strArtist")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the photo URL.</summary>
    [JsonPropertyName("strArtistThumb")]
    public string? Thumb { get; set; }

    /// <summary>Gets or sets the genre.</summary>
    [JsonPropertyName("strGenre")]
    public string? Genre { get; set; }
}
