using Jellyfin.Plugin.MusicVideoMeta.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.MusicVideoMeta;

/// <summary>
/// Registers the plugin's shared services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<AudiometaClient>();
        serviceCollection.AddSingleton<MusicBrainzClient>();
        serviceCollection.AddSingleton<TheAudioDbClient>();
        serviceCollection.AddSingleton<MusicLibraryIndex>();
        serviceCollection.AddSingleton<VideoResolver>();
        serviceCollection.AddSingleton<FileTagWriter>();
        serviceCollection.AddSingleton<NfoWriter>();
        serviceCollection.AddSingleton<ProcessedState>();
        serviceCollection.AddSingleton<VideoProcessor>();
        serviceCollection.AddSingleton<ArtistImageSource>();
    }
}
