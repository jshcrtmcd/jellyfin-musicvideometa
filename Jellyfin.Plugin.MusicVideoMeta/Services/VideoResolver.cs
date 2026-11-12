using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicVideoMeta.Api;
using Jellyfin.Plugin.MusicVideoMeta.Parsing;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Works out the artist, title, year, album and artist photo for a music video file.
/// Order of trust: file and folder names, then the Music library, then audiometa.io, MusicBrainz and TheAudioDB.
/// </summary>
public class VideoResolver
{
    private static readonly TimeSpan _rootsMaxAge = TimeSpan.FromMinutes(5);
    private static readonly HashSet<string> _genericFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "music videos", "musicvideos", "music video", "videos", "video", "clips", "misc", "other", "singles", "unsorted",
    };

    private readonly ILibraryManager _libraryManager;
    private readonly MusicLibraryIndex _index;
    private readonly AudiometaClient _audiometa;
    private readonly MusicBrainzClient _musicBrainz;
    private readonly TheAudioDbClient _theAudioDb;
    private readonly ILogger<VideoResolver> _logger;
    private string[] _roots = [];
    private DateTime _rootsRead = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="index">Music library index.</param>
    /// <param name="audiometa">audiometa.io client.</param>
    /// <param name="musicBrainz">MusicBrainz client.</param>
    /// <param name="theAudioDb">TheAudioDB client.</param>
    /// <param name="logger">Logger.</param>
    public VideoResolver(
        ILibraryManager libraryManager,
        MusicLibraryIndex index,
        AudiometaClient audiometa,
        MusicBrainzClient musicBrainz,
        TheAudioDbClient theAudioDb,
        ILogger<VideoResolver> logger)
    {
        _libraryManager = libraryManager;
        _index = index;
        _audiometa = audiometa;
        _musicBrainz = musicBrainz;
        _theAudioDb = theAudioDb;
        _logger = logger;
    }

    /// <summary>
    /// Resolve a music video file.
    /// </summary>
    /// <param name="path">Video path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result, or null if no artist could be found.</returns>
    public async Task<ResolvedVideo?> ResolveAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var library = await _index.GetAsync(cancellationToken).ConfigureAwait(false);
        var (folderArtist, albumFolder) = ReadFolders(path);
        var parsed = NameParser.Parse(Path.GetFileNameWithoutExtension(path), folderArtist, library.IsKnownArtist);

        var rawArtist = parsed.Artist ?? folderArtist;
        if (string.IsNullOrWhiteSpace(rawArtist))
        {
            _logger.LogInformation("No artist folder and no artist in the file name, skipping {Path}", path);
            return null;
        }

        var result = new ResolvedVideo { Title = parsed.Title, Year = parsed.Year };
        var sources = new List<string> { folderArtist is null ? "file name" : "folder" };

        // Artist: Music library spelling first, so Jellyfin links the video to the existing artist.
        var libArtist = library.FindArtist(rawArtist);
        AmArtist? amArtist = null;
        if (libArtist is not null)
        {
            result.PrimaryArtist = libArtist.Name;
            result.ArtistInMusicLibrary = true;
            result.ArtistImagePath = libArtist.ImagePath;
            result.Genres = libArtist.Genres;
            sources.Add("music library");
        }
        else
        {
            var cleaned = NameNormalizer.CleanArtistName(rawArtist);
            result.PrimaryArtist = cleaned;
            var key = NameNormalizer.ArtistKey(cleaned);

            amArtist = (await _audiometa.SearchArtistsAsync(cleaned, cancellationToken).ConfigureAwait(false))
                .Where(a => NameNormalizer.ArtistKey(a.Name) == key)
                .OrderByDescending(a => a.Popularity ?? 0)
                .FirstOrDefault();
            if (amArtist?.Name is not null)
            {
                result.PrimaryArtist = amArtist.Name;
                sources.Add("audiometa.io");
            }
            else
            {
                var mb = (await _musicBrainz.SearchArtistsAsync(cleaned, cancellationToken).ConfigureAwait(false))
                    .FirstOrDefault(a => (a.Score ?? 0) >= 90 && NameNormalizer.ArtistKey(a.Name) == key);
                if (mb?.Name is not null)
                {
                    result.PrimaryArtist = mb.Name;
                    sources.Add("MusicBrainz");
                }
            }
        }

        result.Artists.Add(result.PrimaryArtist);
        foreach (var f in parsed.FeaturedArtists)
        {
            var name = library.FindArtist(f)?.Name ?? f;
            if (!result.Artists.Any(a => NameNormalizer.ArtistKey(a) == NameNormalizer.ArtistKey(name)))
            {
                result.Artists.Add(name);
            }
        }

        // Folders named after the video itself ("Artist/Song/Song.mkv") are not albums.
        if (albumFolder is not null
            && !_genericFolders.Contains(albumFolder)
            && NameNormalizer.TitleKey(albumFolder) != NameNormalizer.TitleKey(Path.GetFileNameWithoutExtension(path))
            && NameNormalizer.TitleKey(albumFolder) != NameNormalizer.TitleKey(parsed.Title)
            && NameNormalizer.ArtistKey(albumFolder) != NameNormalizer.ArtistKey(result.PrimaryArtist))
        {
            result.Album = albumFolder.Trim();
        }

        // Song details: Music library first.
        var song = library.FindSong(result.PrimaryArtist, result.Title);
        if (song is not null)
        {
            if (NameNormalizer.Normalize(song.Title) == NameNormalizer.Normalize(result.Title))
            {
                result.Title = song.Title;
            }

            result.Year ??= song.Year;
            result.Album = song.Album ?? result.Album;
            result.AlbumImagePath = song.AlbumImagePath;
            if (result.Genres.Count == 0)
            {
                result.Genres = song.Genres;
            }

            sources.Add("music library song");
        }

        var wantCover = (Plugin.Instance?.Configuration.UseAlbumCoverForVideos ?? true) && result.AlbumImagePath is null;
        if (result.Year is null || result.Album is null || wantCover)
        {
            await FillFromAudiometaAsync(result, sources, cancellationToken).ConfigureAwait(false);
        }

        if (result.Year is null)
        {
            await FillYearFromMusicBrainzAsync(result, sources, cancellationToken).ConfigureAwait(false);
        }

        if (result.Genres.Count == 0 && amArtist?.Genres is { Count: > 0 })
        {
            result.Genres = amArtist.Genres;
        }

        if (result.ArtistImagePath is null)
        {
            await FindArtistImageAsync(result, amArtist, cancellationToken).ConfigureAwait(false);
        }

        result.Sources = string.Join(", ", sources.Distinct());
        return result;
    }

    /// <summary>
    /// Whether a folder is an artist folder: directly inside a Music Videos library root.
    /// </summary>
    /// <param name="path">Folder path.</param>
    /// <returns>True if it is.</returns>
    public bool IsArtistFolder(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        if (string.IsNullOrEmpty(parent))
        {
            return false;
        }

        parent = Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar;
        return GetRoots().Contains(parent, StringComparer.Ordinal);
    }

    /// <summary>
    /// The artist folder a video sits in, if it is inside one.
    /// </summary>
    /// <param name="videoPath">Video path.</param>
    /// <returns>Folder path or null.</returns>
    public string? ArtistFolderOf(string videoPath)
    {
        var root = GetRoots()
            .Where(r => videoPath.StartsWith(r, StringComparison.Ordinal))
            .OrderByDescending(r => r.Length)
            .FirstOrDefault();
        if (root is null)
        {
            return null;
        }

        var parts = videoPath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? root + parts[0] : null;
    }

    /// <summary>
    /// Find the photo for an artist folder.
    /// </summary>
    /// <param name="folderName">Folder name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artist name and photo (local path or URL).</returns>
    public async Task<ResolvedVideo> ResolveArtistImageAsync(string folderName, CancellationToken cancellationToken)
    {
        var library = await _index.GetAsync(cancellationToken).ConfigureAwait(false);
        var lib = library.FindArtist(folderName);
        var result = new ResolvedVideo
        {
            PrimaryArtist = lib?.Name ?? NameNormalizer.CleanArtistName(folderName),
            ArtistImagePath = lib?.ImagePath,
            ArtistInMusicLibrary = lib is not null,
        };

        if (result.ArtistImagePath is null)
        {
            await FindArtistImageAsync(result, null, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private async Task FillFromAudiometaAsync(ResolvedVideo result, List<string> sources, CancellationToken ct)
    {
        var artistKey = NameNormalizer.ArtistKey(result.PrimaryArtist);
        var titleKey = NameNormalizer.TitleKey(result.Title);
        var tracks = await _audiometa.SearchTracksAsync(result.PrimaryArtist, result.Title, ct).ConfigureAwait(false);
        var best = tracks
            .Where(t => NameNormalizer.TitleKey(t.Name) == titleKey
                && (t.Artists ?? []).Any(a => NameNormalizer.ArtistKey(a.Name) == artistKey))
            .OrderBy(t => AudiometaClient.YearOf(t.Album?.ReleaseDate) ?? int.MaxValue)
            .ThenBy(t => string.Equals(t.Album?.AlbumType, "album", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
        if (best is null)
        {
            return;
        }

        result.Year ??= AudiometaClient.YearOf(best.Album?.ReleaseDate);
        result.AlbumImageUrl ??= AudiometaClient.BestImageUrl(best.Album?.Images);
        var type = best.Album?.AlbumType;
        if (result.Album is null
            && !string.IsNullOrWhiteSpace(best.Album?.Name)
            && !string.Equals(type, "single", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(type, "compilation", StringComparison.OrdinalIgnoreCase))
        {
            result.Album = best.Album.Name;
        }

        sources.Add("audiometa.io");
    }

    private async Task FillYearFromMusicBrainzAsync(ResolvedVideo result, List<string> sources, CancellationToken ct)
    {
        var artistKey = NameNormalizer.ArtistKey(result.PrimaryArtist);
        var titleKey = NameNormalizer.TitleKey(result.Title);
        var recordings = await _musicBrainz.SearchRecordingsAsync(result.PrimaryArtist, result.Title, ct).ConfigureAwait(false);
        var year = recordings
            .Where(r => (r.Score ?? 0) >= 80
                && NameNormalizer.TitleKey(r.Title) == titleKey
                && (r.ArtistCredit ?? []).Any(c => NameNormalizer.ArtistKey(c.Artist?.Name ?? c.Name) == artistKey))
            .Select(r => AudiometaClient.YearOf(r.FirstReleaseDate))
            .Where(y => y.HasValue)
            .Min();
        if (year.HasValue)
        {
            result.Year = year;
            sources.Add("MusicBrainz");
        }
    }

    private async Task FindArtistImageAsync(ResolvedVideo result, AmArtist? amArtist, CancellationToken ct)
    {
        var key = NameNormalizer.ArtistKey(result.PrimaryArtist);
        if (amArtist is null && _audiometa.IsConfigured)
        {
            amArtist = (await _audiometa.SearchArtistsAsync(result.PrimaryArtist, ct).ConfigureAwait(false))
                .Where(a => NameNormalizer.ArtistKey(a.Name) == key)
                .OrderByDescending(a => a.Popularity ?? 0)
                .FirstOrDefault();
        }

        var url = AudiometaClient.BestImageUrl(amArtist?.Images);
        if (url is null && amArtist?.Id is not null)
        {
            var full = await _audiometa.GetArtistAsync(amArtist.Id, ct).ConfigureAwait(false);
            url = AudiometaClient.BestImageUrl(full?.Images);
        }

        if (url is null)
        {
            url = (await _theAudioDb.SearchArtistsAsync(result.PrimaryArtist, ct).ConfigureAwait(false))
                .Where(a => NameNormalizer.ArtistKey(a.Name) == key && !string.IsNullOrWhiteSpace(a.Thumb))
                .Select(a => a.Thumb)
                .FirstOrDefault();
        }

        result.ArtistImageUrl = url;
    }

    /// <summary>
    /// The artist folder (first folder under the library root) and, for deeper files, the folder holding the file.
    /// </summary>
    private (string? Artist, string? Album) ReadFolders(string path)
    {
        var roots = GetRoots();
        var root = roots
            .Where(r => path.StartsWith(r, StringComparison.Ordinal))
            .OrderByDescending(r => r.Length)
            .FirstOrDefault();

        if (root is null)
        {
            // Not under a known Music Videos library: fall back to the folder holding the file.
            var parent = Path.GetFileName(Path.GetDirectoryName(path));
            return (string.IsNullOrWhiteSpace(parent) ? null : parent, null);
        }

        var parts = Path.GetRelativePath(root, path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var artist = parts.Length >= 2 ? parts[0] : null;
        var album = parts.Length >= 3 ? parts[^2] : null;
        return (artist, album);
    }

    private string[] GetRoots()
    {
        if (DateTime.UtcNow - _rootsRead < _rootsMaxAge)
        {
            return _roots;
        }

        try
        {
            _roots = _libraryManager.GetVirtualFolders()
                .Where(f => f.CollectionType == CollectionTypeOptions.musicvideos)
                .SelectMany(f => f.Locations ?? [])
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .Select(l => Path.TrimEndingDirectorySeparator(l) + Path.DirectorySeparatorChar)
                .ToArray();
            _rootsRead = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read library folders");
        }

        return _roots;
    }
}
