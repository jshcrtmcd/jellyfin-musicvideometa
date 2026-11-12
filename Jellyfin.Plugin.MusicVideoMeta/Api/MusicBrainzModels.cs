using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MusicVideoMeta.Api;

/// <summary>MusicBrainz recording search response.</summary>
public class MbRecordingSearch
{
    /// <summary>Gets or sets the recordings.</summary>
    [JsonPropertyName("recordings")]
    public List<MbRecording>? Recordings { get; set; }
}

/// <summary>MusicBrainz recording.</summary>
public class MbRecording
{
    /// <summary>Gets or sets the ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the match score (0-100).</summary>
    [JsonPropertyName("score")]
    public int? Score { get; set; }

    /// <summary>Gets or sets the title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>Gets or sets the first release date.</summary>
    [JsonPropertyName("first-release-date")]
    public string? FirstReleaseDate { get; set; }

    /// <summary>Gets or sets the artist credit.</summary>
    [JsonPropertyName("artist-credit")]
    public List<MbArtistCredit>? ArtistCredit { get; set; }
}

/// <summary>MusicBrainz artist credit.</summary>
public class MbArtistCredit
{
    /// <summary>Gets or sets the credited name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the artist.</summary>
    [JsonPropertyName("artist")]
    public MbArtist? Artist { get; set; }
}

/// <summary>MusicBrainz artist search response.</summary>
public class MbArtistSearch
{
    /// <summary>Gets or sets the artists.</summary>
    [JsonPropertyName("artists")]
    public List<MbArtist>? Artists { get; set; }
}

/// <summary>MusicBrainz artist.</summary>
public class MbArtist
{
    /// <summary>Gets or sets the ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the match score (0-100).</summary>
    [JsonPropertyName("score")]
    public int? Score { get; set; }
}
