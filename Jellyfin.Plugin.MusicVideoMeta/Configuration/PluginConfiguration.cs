using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MusicVideoMeta.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the audiometa.io API key. Leave empty to use the key saved in the Audiometa plugin.
    /// </summary>
    public string AudiometaApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether MusicBrainz is used for years and artist spelling.
    /// </summary>
    public bool UseMusicBrainz { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether TheAudioDB is used for artist photos.
    /// </summary>
    public bool UseTheAudioDb { get; set; } = true;

    /// <summary>
    /// Gets or sets the TheAudioDB API key. "123" is the free public key.
    /// </summary>
    public string TheAudioDbApiKey { get; set; } = "123";

    /// <summary>
    /// Gets or sets a value indicating whether artist, title and year are written into the video files.
    /// </summary>
    public bool WriteFileTags { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether .nfo files are written next to the videos.
    /// </summary>
    public bool WriteNfo { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the artist's photo becomes the video's main image.
    /// </summary>
    public bool UseArtistImage { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether new videos are processed after each library scan.
    /// </summary>
    public bool RunAfterLibraryScan { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether to only log what would change, without touching files or items.
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Gets or sets how many hours online lookups are cached in memory.
    /// </summary>
    public int CacheHours { get; set; } = 24;
}
