using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.MusicVideoMeta.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.MusicVideoMeta;

/// <summary>
/// Music video metadata plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Plugin GUID.
    /// </summary>
    public const string PluginGuid = "e5862843-b50a-454e-a7b7-76f3dfa38f79";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Application paths.</param>
    /// <param name="xmlSerializer">XML serializer.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
        Paths = applicationPaths;
    }

    /// <inheritdoc />
    public override string Name => "Music Video Metadata";

    /// <inheritdoc />
    public override string Description => "Artist, title and year for music videos, matched to your music library.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginGuid);

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Gets the server application paths.
    /// </summary>
    public IApplicationPaths Paths { get; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
