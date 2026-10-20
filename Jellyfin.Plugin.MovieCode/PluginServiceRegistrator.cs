using Jellyfin.Plugin.MovieCode.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MovieCode;

/// <summary>
/// Registriert die Dienste des Plugins im Jellyfin-Container.
/// </summary>
public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton(serviceProvider =>
        {
            var applicationPaths = serviceProvider.GetRequiredService<IApplicationPaths>();
            var logger = serviceProvider.GetRequiredService<ILogger<GrantStore>>();
            var filePath = Path.Combine(
                applicationPaths.PluginConfigurationsPath,
                "Jellyfin.Plugin.MovieCode.grants.json");

            return new GrantStore(filePath, TimeProvider.System, logger);
        });

        serviceCollection.AddSingleton(new UnlockRateLimiter(TimeProvider.System));
        serviceCollection.AddSingleton<IStartupFilter, ClientScriptInjectionStartupFilter>();
        serviceCollection.AddHostedService<PlaybackGuard>();
    }
}
