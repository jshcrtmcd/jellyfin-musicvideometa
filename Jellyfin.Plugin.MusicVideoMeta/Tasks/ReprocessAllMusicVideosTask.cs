using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.MusicVideoMeta.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.MusicVideoMeta.Tasks;

/// <summary>
/// Scheduled task: redo every music video, including ones already processed.
/// </summary>
public class ReprocessAllMusicVideosTask : IScheduledTask
{
    private readonly VideoProcessor _processor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReprocessAllMusicVideosTask"/> class.
    /// </summary>
    /// <param name="processor">Processor.</param>
    public ReprocessAllMusicVideosTask(VideoProcessor processor)
    {
        _processor = processor;
    }

    /// <inheritdoc />
    public string Name => "Redo all music videos";

    /// <inheritdoc />
    public string Key => "MusicVideoMetaAll";

    /// <inheritdoc />
    public string Description => "Processes every music video again, even ones already done. Use after renaming folders or adding artists to the Music library.";

    /// <inheritdoc />
    public string Category => "Music Video Metadata";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _processor.RunAsync(onlyNew: false, progress, cancellationToken);

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];
}
