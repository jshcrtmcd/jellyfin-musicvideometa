using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Remembers which video files were already processed (by path, size and modified time),
/// so scans only touch new or changed files.
/// </summary>
public class ProcessedState
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    private readonly ILogger<ProcessedState> _logger;
    private readonly object _lock = new();
    private Dictionary<string, string>? _entries;
    private bool _dirty;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessedState"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public ProcessedState(ILogger<ProcessedState> logger)
    {
        _logger = logger;
    }

    private static string? FilePath
    {
        get
        {
            var dir = Plugin.Instance?.DataFolderPath;
            return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "processed.json");
        }
    }

    /// <summary>
    /// Whether a file was processed and hasn't changed since.
    /// </summary>
    /// <param name="path">Video path.</param>
    /// <returns>True if done.</returns>
    public bool IsDone(string path)
    {
        var stamp = Stamp(path);
        lock (_lock)
        {
            return stamp is not null && Entries.TryGetValue(path, out var saved) && saved == stamp;
        }
    }

    /// <summary>
    /// Record a file as processed in its current state.
    /// </summary>
    /// <param name="path">Video path.</param>
    public void MarkDone(string path)
    {
        var stamp = Stamp(path);
        if (stamp is null)
        {
            return;
        }

        lock (_lock)
        {
            Entries[path] = stamp;
            _dirty = true;
        }
    }

    /// <summary>
    /// Forget every file, so the next run processes everything again.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            Entries.Clear();
            _dirty = true;
        }

        Save();
    }

    /// <summary>
    /// Write changes to disk.
    /// </summary>
    public void Save()
    {
        var file = FilePath;
        if (file is null)
        {
            return;
        }

        lock (_lock)
        {
            if (!_dirty || _entries is null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                var temp = file + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_entries, _json));
                File.Move(temp, file, overwrite: true);
                _dirty = false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not save {File}", file);
            }
        }
    }

    private Dictionary<string, string> Entries
    {
        get
        {
            if (_entries is not null)
            {
                return _entries;
            }

            _entries = new Dictionary<string, string>(StringComparer.Ordinal);
            var file = FilePath;
            if (file is not null && File.Exists(file))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file));
                    if (loaded is not null)
                    {
                        _entries = new Dictionary<string, string>(loaded, StringComparer.Ordinal);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read {File}; starting fresh", file);
                }
            }

            return _entries;
        }
    }

    private static string? Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists
            ? info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
