using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.MusicVideoMeta.Parsing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Artist names and songs from the Music libraries, so music videos use the exact same artist spelling
/// (which is what makes Jellyfin link them) and the same song titles, years and albums.
/// </summary>
public sealed class MusicLibraryIndex : IDisposable
{
    private static readonly TimeSpan _maxAge = TimeSpan.FromMinutes(30);

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MusicLibraryIndex> _logger;
    private readonly SemaphoreSlim _buildLock = new(1, 1);
    private Snapshot? _snapshot;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicLibraryIndex"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="logger">Logger.</param>
    public MusicLibraryIndex(ILibraryManager libraryManager, ILogger<MusicLibraryIndex> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public void Dispose() => _buildLock.Dispose();

    /// <summary>
    /// Drop the cached index so the next lookup rebuilds it.
    /// </summary>
    public void Invalidate() => _snapshot = null;

    /// <summary>
    /// Get the current index, building it if needed.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Snapshot.</returns>
    public async Task<Snapshot> GetAsync(CancellationToken cancellationToken)
    {
        var snap = _snapshot;
        if (snap is not null && DateTime.UtcNow - snap.Built < _maxAge)
        {
            return snap;
        }

        await _buildLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            snap = _snapshot;
            if (snap is not null && DateTime.UtcNow - snap.Built < _maxAge)
            {
                return snap;
            }

            snap = Build();
            _snapshot = snap;
            return snap;
        }
        finally
        {
            _buildLock.Release();
        }
    }

    private Snapshot Build()
    {
        var snap = new Snapshot();
        // All songs on the server. Music videos aren't Audio items and audiobooks are a different type,
        // so this is effectively every Music library, without depending on how library folders are linked.
        var songs = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Audio],
            Recursive = true,
            IsVirtualItem = false,
        });

        foreach (var song in songs.OfType<Audio>())
        {
            var names = song.AlbumArtists.Concat(song.Artists).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            foreach (var name in names)
            {
                snap.AddArtistName(name);
            }

            if (string.IsNullOrWhiteSpace(song.Name))
            {
                continue;
            }

            foreach (var name in names)
            {
                snap.AddSong(name, song);
            }
        }

        // Artist entries hold the photos and genres. Only keep those whose name is used in a Music library,
        // so messy names that music videos created earlier are never picked up.
        var artistItems = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicArtist],
            Recursive = true,
        });

        foreach (var artist in artistItems.OfType<MusicArtist>())
        {
            snap.AddArtistItem(artist);
        }

        if (songs.Count == 0)
        {
            _logger.LogWarning("No songs found in any Music library; artist names will come from folders and online services only");
        }

        _logger.LogInformation(
            "Music library index: {Artists} artists, {Songs} songs",
            snap.ArtistCount,
            songs.Count);
        return snap;
    }

    /// <summary>
    /// A song from the Music library.
    /// </summary>
    /// <param name="Title">Title.</param>
    /// <param name="Year">Year.</param>
    /// <param name="Album">Album.</param>
    /// <param name="Genres">Genres.</param>
    /// <param name="AlbumImagePath">The album cover on disk, if any.</param>
    public sealed record SongInfo(string Title, int? Year, string? Album, IReadOnlyList<string> Genres, string? AlbumImagePath);

    /// <summary>
    /// A Music library artist.
    /// </summary>
    public sealed class ArtistInfo
    {
        /// <summary>Gets or sets the name exactly as the Music library spells it.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Gets or sets the photo on disk, if any.</summary>
        public string? ImagePath { get; set; }

        /// <summary>Gets or sets the genres.</summary>
        public IReadOnlyList<string> Genres { get; set; } = [];

        /// <summary>Gets or sets how many songs use this spelling.</summary>
        public int Uses { get; set; }
    }

    /// <summary>
    /// Point-in-time index.
    /// </summary>
    public sealed class Snapshot
    {
        private readonly Dictionary<string, ArtistInfo> _artistsByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, ArtistInfo> _artistsByExactName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<SongInfo>> _songs = new(StringComparer.Ordinal);
        private readonly HashSet<string> _imagePaths = new(StringComparer.Ordinal);

        /// <summary>Gets when this was built.</summary>
        public DateTime Built { get; } = DateTime.UtcNow;

        /// <summary>Gets the number of artists.</summary>
        public int ArtistCount => _artistsByKey.Count;

        /// <summary>
        /// Find a Music library artist by name, ignoring case, accents, punctuation and a leading "The".
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Artist or null.</returns>
        public ArtistInfo? FindArtist(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (_artistsByKey.TryGetValue(NameNormalizer.ArtistKey(name), out var a))
            {
                return a;
            }

            var cleaned = NameNormalizer.CleanArtistName(name);
            return _artistsByKey.TryGetValue(NameNormalizer.ArtistKey(cleaned), out a) ? a : null;
        }

        /// <summary>
        /// Whether a name is a Music library artist.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>True if known.</returns>
        public bool IsKnownArtist(string name) => FindArtist(name) is not null;

        /// <summary>
        /// Whether a path is a Music library artist photo or album cover.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <returns>True if it is.</returns>
        public bool IsArtistImage(string path) => _imagePaths.Contains(path);

        /// <summary>
        /// Find a song by an artist, matching the title loosely.
        /// </summary>
        /// <param name="artist">Artist name.</param>
        /// <param name="title">Title.</param>
        /// <returns>The earliest matching song, or null.</returns>
        public SongInfo? FindSong(string artist, string title)
        {
            var artistKey = NameNormalizer.ArtistKey(artist);
            var full = NameNormalizer.Normalize(title);
            var loose = NameNormalizer.TitleKey(title);

            List<SongInfo>? hits = null;
            if (full.Length > 0)
            {
                _songs.TryGetValue(artistKey + "|" + full, out hits);
            }

            if ((hits is null || hits.Count == 0) && loose.Length > 0)
            {
                _songs.TryGetValue(artistKey + "|~" + loose, out hits);
            }

            return hits?
                .OrderBy(s => s.Year ?? int.MaxValue)
                .FirstOrDefault();
        }

        internal void AddArtistName(string name)
        {
            var key = NameNormalizer.ArtistKey(name);
            if (key.Length == 0)
            {
                return;
            }

            if (_artistsByExactName.TryGetValue(name, out var existing))
            {
                existing.Uses++;
                return;
            }

            var info = new ArtistInfo { Name = name, Uses = 1 };
            _artistsByExactName[name] = info;

            // When the library spells one artist several ways, the most used spelling wins.
            if (!_artistsByKey.TryGetValue(key, out var current) || current.Uses < info.Uses)
            {
                _artistsByKey[key] = info;
            }
        }

        internal void AddSong(string artistName, Audio song)
        {
            var key = NameNormalizer.ArtistKey(artistName);
            if (key.Length == 0)
            {
                return;
            }

            var cover = song.AlbumEntity?.GetImagePath(ImageType.Primary, 0);
            if (string.IsNullOrEmpty(cover) || !System.IO.File.Exists(cover))
            {
                cover = null;
            }
            else
            {
                _imagePaths.Add(cover);
            }

            var info = new SongInfo(song.Name, song.ProductionYear, song.Album, song.Genres ?? [], cover);
            Add(key + "|" + NameNormalizer.Normalize(song.Name), info);
            Add(key + "|~" + NameNormalizer.TitleKey(song.Name), info);

            // Re-rank spellings now that use counts have grown.
            if (_artistsByExactName.TryGetValue(artistName, out var a)
                && _artistsByKey.TryGetValue(key, out var current)
                && !ReferenceEquals(a, current)
                && a.Uses > current.Uses)
            {
                _artistsByKey[key] = a;
            }
        }

        internal void AddArtistItem(MusicArtist artist)
        {
            if (string.IsNullOrWhiteSpace(artist.Name) || !_artistsByExactName.TryGetValue(artist.Name, out var info))
            {
                return;
            }

            var image = artist.GetImagePath(ImageType.Primary, 0);
            if (info.ImagePath is null && !string.IsNullOrEmpty(image) && System.IO.File.Exists(image))
            {
                info.ImagePath = image;
                _imagePaths.Add(image);
            }

            if (info.Genres.Count == 0 && artist.Genres is { Length: > 0 })
            {
                info.Genres = artist.Genres;
            }
        }

        private void Add(string key, SongInfo info)
        {
            if (!_songs.TryGetValue(key, out var list))
            {
                list = [];
                _songs[key] = list;
            }

            list.Add(info);
        }
    }
}
