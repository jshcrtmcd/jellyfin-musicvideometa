using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicVideoMeta.Api;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// MusicBrainz lookups (no key; one request per second as MusicBrainz asks).
/// </summary>
public class MusicBrainzClient : PacedJsonClient
{
    private const string BaseUrl = "https://musicbrainz.org/ws/2";

    private static readonly string _userAgent =
        "Jellyfin-MusicVideoMeta/"
        + (typeof(MusicBrainzClient).Assembly.GetName().Version?.ToString() ?? "1.0")
        + " ( https://github.com/jshcrtmcd/jellyfin-musicvideometa )";

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicBrainzClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public MusicBrainzClient(IHttpClientFactory httpClientFactory, ILogger<MusicBrainzClient> logger)
        : base(httpClientFactory, logger, TimeSpan.FromMilliseconds(1100))
    {
    }

    /// <summary>Gets a value indicating whether MusicBrainz lookups are enabled.</summary>
    public static bool IsEnabled => Plugin.Instance?.Configuration.UseMusicBrainz ?? true;

    /// <inheritdoc />
    protected override string ServiceName => "MusicBrainz";

    /// <summary>Search recordings by artist and title.</summary>
    /// <param name="artist">Artist.</param>
    /// <param name="title">Title.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recordings.</returns>
    public async Task<IReadOnlyList<MbRecording>> SearchRecordingsAsync(string artist, string title, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return [];
        }

        var query = "recording:\"" + Escape(title) + "\" AND artist:\"" + Escape(artist) + "\"";
        var res = await GetJsonAsync<MbRecordingSearch>(
            BaseUrl + "/recording?fmt=json&limit=25&query=" + Uri.EscapeDataString(query),
            cancellationToken).ConfigureAwait(false);
        return res?.Recordings ?? [];
    }

    /// <summary>Search artists by name.</summary>
    /// <param name="name">Name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artists.</returns>
    public async Task<IReadOnlyList<MbArtist>> SearchArtistsAsync(string name, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return [];
        }

        var query = "artist:\"" + Escape(name) + "\"";
        var res = await GetJsonAsync<MbArtistSearch>(
            BaseUrl + "/artist?fmt=json&limit=10&query=" + Uri.EscapeDataString(query),
            cancellationToken).ConfigureAwait(false);
        return res?.Artists ?? [];
    }

    /// <inheritdoc />
    protected override void Prepare(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("User-Agent", _userAgent);
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c == '"' || c == '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }
}
