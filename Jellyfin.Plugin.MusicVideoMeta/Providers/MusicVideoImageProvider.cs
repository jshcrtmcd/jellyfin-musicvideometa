using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicVideoMeta.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.MusicVideoMeta.Providers;

/// <summary>
/// Offers the artist's photo for artist folders in Music Videos libraries (and, if turned on, for each video).
/// </summary>
public class MusicVideoImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly VideoResolver _resolver;
    private readonly ArtistImageSource _images;

    /// <summary>
    /// Initializes a new instance of the <see cref="MusicVideoImageProvider"/> class.
    /// </summary>
    /// <param name="resolver">Resolver.</param>
    /// <param name="images">Image source.</param>
    public MusicVideoImageProvider(VideoResolver resolver, ArtistImageSource images)
    {
        _resolver = resolver;
        _images = images;
    }

    /// <inheritdoc />
    public string Name => MusicVideoMetadataProvider.ProviderName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool Supports(BaseItem item)
    {
        var config = Plugin.Instance?.Configuration;
        if (item is MusicVideo)
        {
            return config?.UseArtistImageForVideos ?? false;
        }

        return item is Folder and not CollectionFolder
            && (config?.UseArtistImage ?? true)
            && _resolver.IsArtistFolder(item.Path);
    }

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item) => [ImageType.Primary];

    /// <inheritdoc />
    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        var resolved = item is MusicVideo
            ? await _resolver.ResolveAsync(item.Path, cancellationToken).ConfigureAwait(false)
            : await _resolver.ResolveArtistImageAsync(System.IO.Path.GetFileName(item.Path), cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return [];
        }

        var list = new List<RemoteImageInfo>();
        var local = ArtistImageSource.ToUrl(resolved.ArtistImagePath);
        if (local is not null)
        {
            list.Add(new RemoteImageInfo { ProviderName = Name, Url = local, Type = ImageType.Primary });
        }

        if (!string.IsNullOrEmpty(resolved.ArtistImageUrl))
        {
            list.Add(new RemoteImageInfo { ProviderName = Name, Url = resolved.ArtistImageUrl, Type = ImageType.Primary });
        }

        return list;
    }

    /// <inheritdoc />
    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
        => _images.GetResponseAsync(url, cancellationToken);
}
