using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicVideoMeta.Services;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.MusicVideoMeta.Tasks;

/// <summary>
/// Processes new music videos after each library scan.
/// </summary>
public class PostScanTask : ILibraryPostScanTask
{
    private readonly VideoProcessor _processor;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostScanTask"/> class.
    /// </summary>
    /// <param name="processor">Processor.</param>
    public PostScanTask(VideoProcessor processor)
    {
        _processor = processor;
    }

    /// <inheritdoc />
    public Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (!(Plugin.Instance?.Configuration.RunAfterLibraryScan ?? true))
        {
            return Task.CompletedTask;
        }

        return _processor.RunAsync(onlyNew: true, progress, cancellationToken);
    }
}
