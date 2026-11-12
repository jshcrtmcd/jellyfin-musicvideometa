using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MusicVideoMeta.Api;

/// <summary>Image object.</summary>
public class AmImage
{
    /// <summary>Gets or sets the URL.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Gets or sets the height.</summary>
    [JsonPropertyName("height")]
    public int? Height { get; set; }

    /// <summary>Gets or sets the width.</summary>
    [JsonPropertyName("width")]
    public int? Width { get; set; }
}

/// <summary>Artist.</summary>
public class AmArtist
{
    /// <summary>Gets or sets the ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets images.</summary>
    [JsonPropertyName("images")]
    public List<AmImage>? Images { get; set; }

    /// <summary>Gets or sets popularity.</summary>
    [JsonPropertyName("popularity")]
    public int? Popularity { get; set; }

    /// <summary>Gets or sets genres.</summary>
    [JsonPropertyName("genres")]
    public List<string>? Genres { get; set; }
}

/// <summary>Album.</summary>
public class AmAlbum
{
    /// <summary>Gets or sets the ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the album type.</summary>
    [JsonPropertyName("album_type")]
    public string? AlbumType { get; set; }

    /// <summary>Gets or sets the release date (YYYY, YYYY-MM or YYYY-MM-DD).</summary>
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }
}

/// <summary>Track.</summary>
public class AmTrack
{
    /// <summary>Gets or sets the ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>Gets or sets the name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the artists.</summary>
    [JsonPropertyName("artists")]
    public List<AmArtist>? Artists { get; set; }

    /// <summary>Gets or sets the album.</summary>
    [JsonPropertyName("album")]
    public AmAlbum? Album { get; set; }

    /// <summary>Gets or sets popularity.</summary>
    [JsonPropertyName("popularity")]
    public int? Popularity { get; set; }
}

/// <summary>Paging wrapper.</summary>
/// <typeparam name="T">Item type.</typeparam>
public class AmPaging<T>
{
    /// <summary>Gets or sets the items.</summary>
    [JsonPropertyName("items")]
    public List<T>? Items { get; set; }
}

/// <summary>Search response.</summary>
public class AmSearchResponse
{
    /// <summary>Gets or sets artists.</summary>
    [JsonPropertyName("artists")]
    public AmPaging<AmArtist>? Artists { get; set; }

    /// <summary>Gets or sets tracks.</summary>
    [JsonPropertyName("tracks")]
    public AmPaging<AmTrack>? Tracks { get; set; }
}
