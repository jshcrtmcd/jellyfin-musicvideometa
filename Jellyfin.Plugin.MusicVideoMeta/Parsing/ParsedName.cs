using System.Collections.Generic;

namespace Jellyfin.Plugin.MusicVideoMeta.Parsing;

/// <summary>
/// What could be read from a file name.
/// </summary>
public class ParsedName
{
    /// <summary>
    /// Gets or sets the artist named in the file name. Only set when there is no artist folder.
    /// </summary>
    public string? Artist { get; set; }

    /// <summary>
    /// Gets the featured artists.
    /// </summary>
    public List<string> FeaturedArtists { get; } = [];

    /// <summary>
    /// Gets or sets the cleaned title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the year found in the name, if any.
    /// </summary>
    public int? Year { get; set; }
}
