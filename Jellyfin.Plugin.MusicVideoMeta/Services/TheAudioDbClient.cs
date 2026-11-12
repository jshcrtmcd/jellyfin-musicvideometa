using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicVideoMeta.Api;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// TheAudioDB artist lookups (the free key allows 30 requests a minute).
/// </summary>
public class TheAudioDbClient : PacedJsonClient
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TheAudioDbClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">HTTP client factory.</param>
    /// <param name="logger">Logger.</param>
    public TheAudioDbClient(IHttpClientFactory httpClientFactory, ILogger<TheAudioDbClient> logger)
        : base(httpClientFactory, logger, TimeSpan.FromMilliseconds(2100))
    {
    }

    /// <summary>Gets a value indicating whether TheAudioDB lookups are enabled.</summary>
    public static bool IsEnabled
        => (Plugin.Instance?.Configuration.UseTheAudioDb ?? true) && !string.IsNullOrWhiteSpace(Key);

    /// <inheritdoc />
    protected override string ServiceName => "TheAudioDB";

    private static string Key => Plugin.Instance?.Configuration.TheAudioDbApiKey?.Trim() ?? "123";

    /// <summary>Search artists by name.</summary>
    /// <param name="name">Name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Artists.</returns>
    public async Task<IReadOnlyList<TadbArtist>> SearchArtistsAsync(string name, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return [];
        }

        var res = await GetJsonAsync<TadbArtistSearch>(
            "https://www.theaudiodb.com/api/v1/json/" + Uri.EscapeDataString(Key) + "/search.php?s=" + Uri.EscapeDataString(name),
            cancellationToken).ConfigureAwait(false);
        return res?.Artists ?? [];
    }
}
