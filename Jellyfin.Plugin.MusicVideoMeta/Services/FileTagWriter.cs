using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.MediaEncoding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Writes artist, title, year, album and genre tags into video files with ffmpeg.
/// Streams are copied as-is (no re-encode). The new file replaces the original; no backup is kept.
/// </summary>
public class FileTagWriter
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(15);

    private static readonly HashSet<string> _supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".mkv", ".webm",
    };

    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILogger<FileTagWriter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTagWriter"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Media encoder (for the ffmpeg path).</param>
    /// <param name="logger">Logger.</param>
    public FileTagWriter(IMediaEncoder mediaEncoder, ILogger<FileTagWriter> logger)
    {
        _mediaEncoder = mediaEncoder;
        _logger = logger;
    }

    /// <summary>
    /// Whether tags can be written into this kind of file.
    /// </summary>
    /// <param name="path">Path.</param>
    /// <returns>True if supported.</returns>
    public static bool Supports(string path) => _supported.Contains(Path.GetExtension(path));

    /// <summary>
    /// Write the tags.
    /// </summary>
    /// <param name="path">Video file.</param>
    /// <param name="video">Metadata.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the file was rewritten.</returns>
    public async Task<bool> WriteAsync(string path, ResolvedVideo video, CancellationToken cancellationToken)
    {
        if (!Supports(path))
        {
            _logger.LogDebug("Tags not supported for {Path}", path);
            return false;
        }

        var ffmpeg = _mediaEncoder.EncoderPath;
        if (string.IsNullOrEmpty(ffmpeg))
        {
            ffmpeg = "ffmpeg";
        }

        var dir = Path.GetDirectoryName(path)!;
        var ext = Path.GetExtension(path);
        var temp = Path.Combine(dir, "." + Path.GetFileNameWithoutExtension(path) + ".mvmeta-tmp" + ext);

        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-loglevel", "error", "-y",
            "-i", path,
            "-map", "0", "-map", "-0:d?",
            "-c", "copy",
            "-map_metadata", "0",
            "-metadata", "title=" + video.Title,
            "-metadata", "artist=" + video.ArtistTag,
            "-metadata", "album_artist=" + video.PrimaryArtist,
        };

        if (video.Year.HasValue)
        {
            args.Add("-metadata");
            args.Add("date=" + video.Year.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(video.Album))
        {
            args.Add("-metadata");
            args.Add("album=" + video.Album);
        }

        if (video.Genres.Count > 0)
        {
            args.Add("-metadata");
            args.Add("genre=" + string.Join("; ", video.Genres));
        }

        args.Add(temp);

        var replacing = false;
        try
        {
            var (exitCode, stderr) = await RunAsync(ffmpeg, args, cancellationToken).ConfigureAwait(false);
            var tempInfo = new FileInfo(temp);
            var original = new FileInfo(path);
            if (exitCode != 0 || !tempInfo.Exists || tempInfo.Length < original.Length / 2)
            {
                _logger.LogError("ffmpeg could not tag {Path} (exit {Code}): {Error}", path, exitCode, stderr);
                TryDelete(temp);
                return false;
            }

            // Overwrite the original's contents rather than swapping files, so its owner and permissions stay.
            replacing = true;
            InPlaceFile.Replace(temp, path);
            return true;
        }
        catch (OperationCanceledException)
        {
            TryDelete(temp);
            throw;
        }
        catch (Exception ex) when (replacing)
        {
            // The original may be half overwritten; keep the tagged copy so nothing is lost.
            _logger.LogError(ex, "Overwriting {Path} failed; the tagged copy was kept at {Temp}", path, temp);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Writing tags failed for {Path}", path);
            TryDelete(temp);
            return false;
        }
    }

    private static async Task<(int ExitCode, string StdErr)> RunAsync(string exe, List<string> args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };
        process.Start();
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }

            throw;
        }

        await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        return (process.ExitCode, string.Join(' ', stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(5)));
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove temp file {Path}", path);
        }
    }
}
