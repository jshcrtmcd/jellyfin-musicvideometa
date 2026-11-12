using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Net;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Applies resolved metadata to music videos: file tags, .nfo, the Jellyfin item and the artist photo.
/// </summary>
public class VideoProcessor
{
    private static readonly SemaphoreSlim _runLock = new(1, 1);

    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly VideoResolver _resolver;
    private readonly FileTagWriter _tagWriter;
    private readonly NfoWriter _nfoWriter;
    private readonly ProcessedState _state;
    private readonly MusicLibraryIndex _index;
    private readonly ILogger<VideoProcessor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VideoProcessor"/> class.
    /// </summary>
    /// <param name="libraryManager">Library manager.</param>
    /// <param name="providerManager">Provider manager.</param>
    /// <param name="resolver">Resolver.</param>
    /// <param name="tagWriter">File tag writer.</param>
    /// <param name="nfoWriter">.nfo writer.</param>
    /// <param name="state">Processed-file state.</param>
    /// <param name="index">Music library index.</param>
    /// <param name="logger">Logger.</param>
    public VideoProcessor(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        VideoResolver resolver,
        FileTagWriter tagWriter,
        NfoWriter nfoWriter,
        ProcessedState state,
        MusicLibraryIndex index,
        ILogger<VideoProcessor> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _resolver = resolver;
        _tagWriter = tagWriter;
        _nfoWriter = nfoWriter;
        _state = state;
        _index = index;
        _logger = logger;
    }

    /// <summary>
    /// Process the music videos in every library.
    /// </summary>
    /// <param name="onlyNew">Skip files already processed and unchanged since.</param>
    /// <param name="progress">Progress.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task.</returns>
    public async Task RunAsync(bool onlyNew, IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!await _runLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Music video metadata is already running");
            return;
        }

        try
        {
            _index.Invalidate();
            var videos = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [BaseItemKind.MusicVideo],
                    Recursive = true,
                    IsVirtualItem = false,
                })
                .OfType<MusicVideo>()
                .Where(v => !string.IsNullOrEmpty(v.Path))
                .ToList();

            var todo = onlyNew ? videos.Where(v => !_state.IsDone(v.Path)).ToList() : videos;
            _logger.LogInformation("Music video metadata: {Count} of {Total} videos to process", todo.Count, videos.Count);

            int done = 0, updated = 0, failed = 0;
            foreach (var video in todo)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (await ProcessAsync(video, cancellationToken).ConfigureAwait(false))
                    {
                        updated++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Failed on {Path}", video.Path);
                }

                done++;
                progress.Report(100.0 * done / todo.Count);
                if (done % 25 == 0)
                {
                    _state.Save();
                }
            }

            _logger.LogInformation(
                "Music video metadata finished: {Updated} updated, {Failed} failed, {Skipped} skipped",
                updated,
                failed,
                todo.Count - updated - failed);
        }
        finally
        {
            _state.Save();
            _runLock.Release();
        }
    }

    /// <summary>
    /// Process one music video.
    /// </summary>
    /// <param name="item">The video.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if it was updated.</returns>
    public async Task<bool> ProcessAsync(MusicVideo item, CancellationToken cancellationToken)
    {
        var path = item.Path;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return false;
        }

        if (item.IsLocked)
        {
            _logger.LogDebug("Skipping locked item {Path}", path);
            return false;
        }

        var resolved = await _resolver.ResolveAsync(path, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            _state.MarkDone(path);
            return false;
        }

        var config = Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration();
        _logger.LogInformation(
            "{Prefix}{File}: {Artists} - {Title} ({Year}){Album} [{Sources}]{Library}",
            config.DryRun ? "[dry run] " : string.Empty,
            Path.GetFileName(path),
            resolved.ArtistTag,
            resolved.Title,
            resolved.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no year",
            resolved.Album is null ? string.Empty : ", album " + resolved.Album,
            resolved.Sources,
            resolved.ArtistInMusicLibrary ? string.Empty : " (artist not in Music library)");

        if (config.DryRun)
        {
            return false;
        }

        var filesOk = true;
        if (config.WriteFileTags && FileTagWriter.Supports(path))
        {
            filesOk &= await _tagWriter.WriteAsync(path, resolved, cancellationToken).ConfigureAwait(false);
        }

        if (config.WriteNfo)
        {
            filesOk &= _nfoWriter.Write(path, resolved);
        }

        item.Name = resolved.Title;
        item.Artists = resolved.Artists.ToArray();
        if (resolved.Year.HasValue)
        {
            item.ProductionYear = resolved.Year;
        }

        if (!string.IsNullOrWhiteSpace(resolved.Album))
        {
            item.Album = resolved.Album;
        }

        if (resolved.Genres.Count > 0)
        {
            item.Genres = resolved.Genres.ToArray();
        }

        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

        if (config.UseArtistImage)
        {
            await SetArtistImageAsync(item, resolved, cancellationToken).ConfigureAwait(false);
        }

        // If a file couldn't be written (for example a read-only folder), try again on the next run.
        if (filesOk)
        {
            _state.MarkDone(path);
        }

        return true;
    }

    private async Task SetArtistImageAsync(MusicVideo item, ResolvedVideo resolved, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrEmpty(resolved.ArtistImagePath) && File.Exists(resolved.ArtistImagePath))
            {
                var stream = File.OpenRead(resolved.ArtistImagePath);
                await using (stream.ConfigureAwait(false))
                {
                    await _providerManager.SaveImage(
                        item,
                        stream,
                        MimeTypes.GetMimeType(resolved.ArtistImagePath),
                        ImageType.Primary,
                        null,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            else if (!string.IsNullOrEmpty(resolved.ArtistImageUrl))
            {
                await _providerManager.SaveImage(item, resolved.ArtistImageUrl, ImageType.Primary, null, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _logger.LogInformation("No artist photo found for {Artist}", resolved.PrimaryArtist);
                return;
            }

            await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not set the artist photo for {Path}", item.Path);
        }
    }
}
