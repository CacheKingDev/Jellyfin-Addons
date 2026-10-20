using System.Collections.Generic;
using Jellyfin.Plugin.MovieCode.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.MovieCode;

/// <summary>
/// Hauptklasse des Plugins.
/// </summary>
public sealed class MovieCodePlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MovieCodePlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Pfade der Jellyfin-Installation.</param>
    /// <param name="xmlSerializer">XML-Serializer für die Konfiguration.</param>
    public MovieCodePlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets die aktuelle Plugin-Instanz.
    /// </summary>
    public static MovieCodePlugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Movie Code";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("6f2b1c9e-4a3d-4f7b-9c21-8e5d0a7b3c64");

    /// <inheritdoc />
    public override string Description
        => "Erlaubt einem ausgewählten Benutzer die Wiedergabe genau eines Films, nachdem er einen 6-stelligen Einmalcode eingegeben hat.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "MovieCode",
                EmbeddedResourcePath = $"{typeof(MovieCodePlugin).Namespace}.Configuration.configPage.html"
            }
        ];
    }
}
