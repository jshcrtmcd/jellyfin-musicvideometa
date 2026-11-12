using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicVideoMeta.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MusicVideoMeta.Providers;

/// <summary>
/// Metadata downloader for music videos: used by library refreshes and Identify.
/// </summary>
public class MusicVideoMetadataProvider : IRemoteMetadataProvider<MusicVideo, MusicVideoInfo>, IHasOrder
{
    /// <summary>Provider name shown in library settings.</summary>
    public const string ProviderName = "Music Video Metadata";

    private readonly VideoResolver _resolver;
    private readonly ArtistImageSource _images;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicVideoMetadataProvider"/> class.
    /// </summary>
    /// <param name="resolver">Resolver.</param>
    /// <param name="images">Image source.</param>
    public MusicVideoMetadataProvider(VideoResolver resolver, ArtistImageSource images)
    {
        _resolver = resolver;
        _images = images;
    }

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public async Task<MetadataResult<MusicVideo>> GetMetadata(MusicVideoInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<MusicVideo>();
        var resolved = await _resolver.ResolveAsync(info.Path, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return result;
        }

        var item = new MusicVideo
        {
            Name = resolved.Title,
            Artists = resolved.Artists.ToArray(),
            ProductionYear = resolved.Year,
            Album = resolved.Album,
        };
        if (resolved.Genres.Count > 0)
        {
            item.Genres = resolved.Genres.ToArray();
        }

        result.Item = item;
        result.HasMetadata = true;
        result.Provider = Name;
        return result;
    }

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MusicVideoInfo searchInfo, CancellationToken cancellationToken)
    {
        var resolved = await _resolver.ResolveAsync(searchInfo.Path, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return [];
        }

        return
        [
            new RemoteSearchResult
            {
                Name = resolved.Title,
                ProductionYear = resolved.Year,
                Overview = resolved.ArtistTag + (resolved.Album is null ? string.Empty : " · " + resolved.Album),
                ImageUrl = resolved.ArtistImageUrl,
                SearchProviderName = Name,
            }
        ];
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _images.GetResponseAsync(url, cancellationToken);
}
