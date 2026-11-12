using System.Collections.Generic;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Metadata worked out for one music video.
/// </summary>
public class ResolvedVideo
{
    /// <summary>Gets or sets the primary artist, spelled as in the Music library when it is there.</summary>
    public string PrimaryArtist { get; set; } = string.Empty;

    /// <summary>Gets the artists: primary first, then featured.</summary>
    public List<string> Artists { get; } = [];

    /// <summary>Gets or sets the title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the year.</summary>
    public int? Year { get; set; }

    /// <summary>Gets or sets the album.</summary>
    public string? Album { get; set; }

    /// <summary>Gets or sets the genres.</summary>
    public IReadOnlyList<string> Genres { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the artist exists in the Music library.</summary>
    public bool ArtistInMusicLibrary { get; set; }

    /// <summary>Gets or sets the Music library artist's photo on disk.</summary>
    public string? ArtistImagePath { get; set; }

    /// <summary>Gets or sets an online artist photo, used when there is no local one.</summary>
    public string? ArtistImageUrl { get; set; }

    /// <summary>Gets or sets the album or single cover on disk, from the Music library.</summary>
    public string? AlbumImagePath { get; set; }

    /// <summary>Gets or sets an online album or single cover, used when there is no local one.</summary>
    public string? AlbumImageUrl { get; set; }

    /// <summary>Gets or sets where the data came from, for the log.</summary>
    public string Sources { get; set; } = string.Empty;

    /// <summary>Gets the artist tag value: all artists joined with "; ".</summary>
    public string ArtistTag => string.Join("; ", Artists);
}
