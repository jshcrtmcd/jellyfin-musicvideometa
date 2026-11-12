using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicVideoMeta.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.MusicVideoMeta.Tasks;

/// <summary>
/// Scheduled task: fix metadata for music videos that haven't been processed yet.
/// </summary>
public class ProcessMusicVideosTask : IScheduledTask
{
    private readonly VideoProcessor _processor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessMusicVideosTask"/> class.
    /// </summary>
    /// <param name="processor">Processor.</param>
    public ProcessMusicVideosTask(VideoProcessor processor)
    {
        _processor = processor;
    }

    /// <inheritdoc />
    public string Name => "Fix new music videos";

    /// <inheritdoc />
    public string Key => "MusicVideoMetaNew";

    /// <inheritdoc />
    public string Description => "Sets artist, title, year and artist photo for music videos that haven't been processed yet, and writes tags and .nfo files.";

    /// <inheritdoc />
    public string Category => "Music Video Metadata";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _processor.RunAsync(onlyNew: true, progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
