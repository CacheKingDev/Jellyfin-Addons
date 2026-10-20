namespace Jellyfin.Plugin.MovieCode.Models;

/// <summary>
/// Eine Freigabe für genau einen Benutzer. Der Film wird beim Einlösen des Codes
/// oder beim ersten Wiedergabestart festgelegt.
/// </summary>
public sealed class MovieGrant
{
    /// <summary>
    /// Gets or sets die interne ID der Freigabe.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets die ID des Code-Pakets, zu dem dieser Code gehört.
    /// </summary>
    public Guid BatchId { get; set; }

    /// <summary>
    /// Gets or sets die Position des Codes innerhalb des Pakets (1-basiert).
    /// </summary>
    public int BatchIndex { get; set; }

    /// <summary>
    /// Gets or sets die Jellyfin-Benutzer-ID.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets der Benutzername zum Zeitpunkt der Erstellung.
    /// </summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets die ID des freigegebenen Mediums. <see langword="null"/>,
    /// solange der Benutzer noch keinen Film gestartet hat.
    /// </summary>
    public Guid? ItemId { get; set; }

    /// <summary>
    /// Gets or sets der Titel des freigegebenen Mediums.
    /// </summary>
    public string ItemName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets der PBKDF2-Hash des Einmalcodes.
    /// </summary>
    public string CodeHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets der HMAC-Lookup-Hash des Einmalcodes.
    /// </summary>
    public string LookupHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets das Salt des PBKDF2-Hashes.
    /// </summary>
    public string CodeSalt { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets der Zeitpunkt der Erstellung (UTC).
    /// </summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets der Zeitpunkt der Code-Eingabe (UTC).
    /// </summary>
    public DateTimeOffset? UnlockedUtc { get; set; }

    /// <summary>
    /// Gets or sets der Zeitpunkt des Filmendes (UTC).
    /// </summary>
    public DateTimeOffset? CompletedUtc { get; set; }

    /// <summary>
    /// Gets or sets der Zeitpunkt, ab dem der Code nicht mehr gültig ist (UTC).
    /// </summary>
    public DateTimeOffset? ExpiresUtc { get; set; }

    /// <summary>
    /// Gets or sets der aktuelle Zustand.
    /// </summary>
    public GrantStatus Status { get; set; } = GrantStatus.Pending;

    /// <summary>
    /// Erstellt eine flache Kopie, damit Aufrufer den internen Zustand nicht verändern können.
    /// </summary>
    /// <returns>Kopie der Freigabe.</returns>
    public MovieGrant Clone() => (MovieGrant)MemberwiseClone();
}
