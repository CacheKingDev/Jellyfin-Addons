using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.MovieCode.Configuration;

/// <summary>
/// Konfiguration des Plugins.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets die öffentliche Adresse der Freigabeseite. Leer = automatisch ermitteln.
    /// </summary>
    public string UnlockPageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets die Gültigkeit der Einmalcodes in Stunden. 0 = unbegrenzt, bis die Codes verwendet oder aufgehoben werden.
    /// </summary>
    public int CodeValidityHours { get; set; } = 24;

    /// <summary>
    /// Gets or sets die maximale Anzahl an Code-Fehlversuchen pro IP im 10-Minuten-Fenster. 0 = unbegrenzt.
    /// </summary>
    public int MaxFailedAttemptsPerTenMinutes { get; set; } = 5;

    /// <summary>
    /// Gets or sets a value indicating whether die Freigabe auch beim vorzeitigen Stoppen verfällt.
    /// </summary>
    public bool ExpireOnEarlyStop { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether der Benutzer nach dem Filmende automatisch entsperrt wird.
    /// Dabei werden alle übrigen Codes des Pakets ungültig.
    /// </summary>
    public bool ReleaseAfterCompletion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether nur Videos gesperrt werden (Musik und Hörbücher bleiben erlaubt).
    /// </summary>
    public bool RestrictOnlyVideo { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether das Code-Popup im Jellyfin-Web-Client angezeigt wird.
    /// </summary>
    public bool EnableWebPopup { get; set; } = true;

    /// <summary>
    /// Gets or sets die Überschrift der Sperrmeldung.
    /// </summary>
    public string LockedMessageHeader { get; set; } = "Film gesperrt";

    /// <summary>
    /// Gets or sets den Text der Sperrmeldung.
    /// </summary>
    public string LockedMessageText { get; set; } = "Bitte gib zuerst einen deiner 6-stelligen Einmalcodes auf der Freigabeseite ein.";
}
