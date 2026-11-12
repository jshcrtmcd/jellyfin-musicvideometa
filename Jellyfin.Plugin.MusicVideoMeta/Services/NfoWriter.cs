using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MusicVideoMeta.Services;

/// <summary>
/// Writes Kodi/Jellyfin style &lt;musicvideo&gt; .nfo files next to the videos.
/// An existing .nfo is updated in place so fields this plugin doesn't manage are kept.
/// </summary>
public class NfoWriter
{
    private static readonly string[] _managed = ["title", "artist", "album", "year", "genre"];

    private readonly ILogger<NfoWriter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NfoWriter"/> class.
    /// </summary>
    /// <param name="logger">Logger.</param>
    public NfoWriter(ILogger<NfoWriter> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// The .nfo path for a video.
    /// </summary>
    /// <param name="videoPath">Video path.</param>
    /// <returns>.nfo path.</returns>
    public static string NfoPath(string videoPath) => Path.ChangeExtension(videoPath, ".nfo");

    /// <summary>
    /// Write or update the .nfo for a video.
    /// </summary>
    /// <param name="videoPath">Video path.</param>
    /// <param name="video">Metadata.</param>
    /// <returns>True on success.</returns>
    public bool Write(string videoPath, ResolvedVideo video)
    {
        var path = NfoPath(videoPath);
        try
        {
            var doc = Load(path);
            var root = doc.Root!;

            // Drop the fields we manage and add them back in a fixed order at the top.
            foreach (var name in _managed)
            {
                root.Elements(name).Remove();
            }

            var fields = new List<XElement> { new("title", video.Title) };
            fields.AddRange(video.Artists.Select(a => new XElement("artist", a)));
            if (!string.IsNullOrWhiteSpace(video.Album))
            {
                fields.Add(new XElement("album", video.Album));
            }

            if (video.Year.HasValue)
            {
                fields.Add(new XElement("year", video.Year.Value.ToString(CultureInfo.InvariantCulture)));
            }

            fields.AddRange(video.Genres.Select(g => new XElement("genre", g)));
            root.AddFirst(fields);

            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                Encoding = new UTF8Encoding(false),
            };
            var temp = path + ".mvmeta-tmp";
            using (var writer = XmlWriter.Create(temp, settings))
            {
                doc.Save(writer);
            }

            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not write {Path}", path);
            return false;
        }
    }

    private XDocument Load(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var existing = XDocument.Load(path);
                if (existing.Root is not null && existing.Root.Name.LocalName == "musicvideo")
                {
                    return existing;
                }
            }
            catch (XmlException ex)
            {
                _logger.LogInformation(ex, "Replacing unreadable {Path}", path);
            }
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), new XElement("musicvideo"));
    }
}
