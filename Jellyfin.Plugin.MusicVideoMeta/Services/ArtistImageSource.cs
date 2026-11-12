using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Net;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Serves artist photos to Jellyfin: Music library photos from disk, others from the web.
/// </summary>
public class ArtistImageSource
{
    private readonly MusicLibraryIndex _index;
    private readonly AudiometaClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtistImageSource"/> class.
    /// </summary>
    /// <param name="index">Music library index.</param>
    /// <param name="http">Client used for plain downloads.</param>
    public ArtistImageSource(MusicLibraryIndex index, AudiometaClient http)
    {
        _index = index;
        _http = http;
    }

    /// <summary>
    /// A file:// URL for a local artist photo.
    /// </summary>
    /// <param name="path">Path.</param>
    /// <returns>URL or null.</returns>
    public static string? ToUrl(string? path)
        => string.IsNullOrEmpty(path) ? null : new Uri(path).AbsoluteUri;

    /// <summary>
    /// Get an image. Local files are only served if they are a Music library artist's photo.
    /// </summary>
    /// <param name="url">URL.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response.</returns>
    public async Task<HttpResponseMessage> GetResponseAsync(string url, CancellationToken cancellationToken)
    {
        if (!url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return await _http.GetRawAsync(url, cancellationToken).ConfigureAwait(false);
        }

        var path = new Uri(url).LocalPath;
        var library = await _index.GetAsync(cancellationToken).ConfigureAwait(false);
        if (!library.IsArtistImage(path) || !File.Exists(path))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var content = new StreamContent(File.OpenRead(path));
        content.Headers.ContentType = new MediaTypeHeaderValue(MimeTypes.GetMimeType(path));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }
}
