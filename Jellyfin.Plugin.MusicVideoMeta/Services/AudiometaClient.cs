using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Jellyfin.Plugin.MusicVideoMeta.Api;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// audiometa.io lookups. Uses this plugin's key, or the key saved in the Audiometa plugin.
/// </summary>
public class AudiometaClient : PacedJsonClient
{
    // One request a second leaves room under audiometa.io's 100/min limit for the Audiometa plugin sharing the key.
    private const string BaseUrl = "https://api.audiometa.io/v1";
    private const string AudiometaConfigFile = "Jellyfin.Plugin.Audiometa.xml";

    private readonly object _keyLock = new();
    private string? _sharedKey;
    private DateTime _sharedKeyRead = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudiometaClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public AudiometaClient(IHttpClientFactory httpClientFactory, ILogger<AudiometaClient> logger)
        : base(httpClientFactory, logger, TimeSpan.FromMilliseconds(1000))
    {
    }

    /// <summary>Gets a value indicating whether a key is available.</summary>
    public bool IsConfigured => !string.IsNullOrEmpty(GetApiKey());

    /// <inheritdoc />
    protected override string ServiceName => "audiometa.io";

    /// <summary>Search artists by name.</summary>
    /// <param name="name">Name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artists.</returns>
    public async Task<IReadOnlyList<AmArtist>> SearchArtistsAsync(string name, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var res = await GetJsonAsync<AmSearchResponse>(
            BaseUrl + "/search?type=artist&limit=10&q=" + Uri.EscapeDataString(name),
            cancellationToken).ConfigureAwait(false);
        return res?.Artists?.Items ?? [];
    }

    /// <summary>Get a full artist.</summary>
    /// <param name="id">Artist ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artist or null.</returns>
    public Task<AmArtist?> GetArtistAsync(string id, CancellationToken cancellationToken)
        => IsConfigured
            ? GetJsonAsync<AmArtist>(BaseUrl + "/artists/" + Uri.EscapeDataString(id), cancellationToken)
            : Task.FromResult<AmArtist?>(null);

    /// <summary>Search tracks.</summary>
    /// <param name="artist">Artist.</param>
    /// <param name="title">Title.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tracks.</returns>
    public async Task<IReadOnlyList<AmTrack>> SearchTracksAsync(string artist, string title, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var res = await GetJsonAsync<AmSearchResponse>(
            BaseUrl + "/search?type=track&limit=20&q=" + Uri.EscapeDataString(artist + " " + title),
            cancellationToken).ConfigureAwait(false);
        return res?.Tracks?.Items ?? [];
    }

    /// <summary>Pick the largest image URL.</summary>
    /// <param name="images">Images.</param>
    /// <returns>URL or null.</returns>
    public static string? BestImageUrl(IEnumerable<AmImage>? images)
        => images?
            .Where(i => !string.IsNullOrWhiteSpace(i.Url))
            .OrderByDescending(i => (i.Width ?? 0) * (i.Height ?? 0))
            .Select(i => i.Url)
            .FirstOrDefault();

    /// <summary>Read the year from a release date.</summary>
    /// <param name="date">YYYY, YYYY-MM or YYYY-MM-DD.</param>
    /// <returns>Year or null.</returns>
    public static int? YearOf(string? date)
        => date is { Length: >= 4 } && int.TryParse(date.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y > 1900
            ? y
            : null;

    /// <inheritdoc />
    protected override void Prepare(HttpRequestMessage request)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetApiKey());
    }

    private string? GetApiKey()
    {
        var own = Plugin.Instance?.Configuration.AudiometaApiKey?.Trim();
        if (!string.IsNullOrEmpty(own))
        {
            return own;
        }

        lock (_keyLock)
        {
            if (DateTime.UtcNow - _sharedKeyRead < TimeSpan.FromMinutes(5))
            {
                return _sharedKey;
            }

            _sharedKeyRead = DateTime.UtcNow;
            _sharedKey = null;
            var dir = Plugin.Instance?.Paths.PluginConfigurationsPath;
            if (string.IsNullOrEmpty(dir))
            {
                return null;
            }

            var file = Path.Combine(dir, AudiometaConfigFile);
            try
            {
                if (File.Exists(file))
                {
                    var key = XDocument.Load(file).Root?.Element("ApiKey")?.Value.Trim();
                    _sharedKey = string.IsNullOrEmpty(key) ? null : key;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Could not read the Audiometa plugin's API key from {File}", file);
            }

            return _sharedKey;
        }
    }
}
