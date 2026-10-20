namespace Jellyfin.Plugin.MovieCode.Models;

/// <summary>
/// Anfrage zum Einlösen eines Codes.
/// </summary>
/// <param name="Code">Der eingegebene 6-stellige Code.</param>
public sealed record UnlockRequest(string? Code);

/// <summary>
/// Antwort der Freigabeseite.
/// </summary>
/// <param name="Success">Ob der Code akzeptiert wurde.</param>
/// <param name="Message">Meldung für den Benutzer.</param>
/// <param name="ItemName">Der freigegebene Film, falls bekannt.</param>
public sealed record UnlockResponse(bool Success, string Message, string? ItemName = null);

/// <summary>
/// Anfrage zum Erzeugen einer Freigabe.
/// </summary>
/// <param name="UserId">Der ausgewählte Benutzer.</param>
/// <param name="ItemId">Der ausgewählte Film.</param>
public sealed record CreateGrantRequest(Guid UserId, Guid ItemId);

/// <summary>
/// Antwort nach dem Erzeugen einer Freigabe. Der Code wird nur hier einmalig zurückgegeben.
/// </summary>
public sealed record CreateGrantResponse(
    Guid Id,
    string Code,
    string UserName,
    string ItemName,
    DateTimeOffset? ExpiresUtc,
    string UnlockUrl);

/// <summary>
/// Freigabe für die Admin-Oberfläche.
/// </summary>
public sealed record GrantDto(
    Guid Id,
    Guid UserId,
    string UserName,
    Guid ItemId,
    string ItemName,
    string Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? UnlockedUtc,
    DateTimeOffset? ExpiresUtc,
    DateTimeOffset? CompletedUtc);

/// <summary>
/// Ergebnis einer Code-Prüfung.
/// </summary>
public enum UnlockStatus
{
    /// <summary>
    /// Der Code war korrekt und wurde eingelöst.
    /// </summary>
    Success = 0,

    /// <summary>
    /// Der Code ist unbekannt oder abgelaufen.
    /// </summary>
    Invalid = 1,

    /// <summary>
    /// Der Code wurde bereits eingelöst.
    /// </summary>
    AlreadyUsed = 2
}

/// <summary>
/// Ergebnis einer Code-Prüfung inklusive Freigabe.
/// </summary>
/// <param name="Status">Ergebnisstatus.</param>
/// <param name="Grant">Betroffene Freigabe, falls vorhanden.</param>
public sealed record UnlockResult(UnlockStatus Status, MovieGrant? Grant)
{
    /// <summary>
    /// Erstellt ein ungültiges Ergebnis.
    /// </summary>
    /// <returns>Ergebnis.</returns>
    public static UnlockResult Invalid() => new(UnlockStatus.Invalid, null);

    /// <summary>
    /// Erstellt ein Ergebnis für einen bereits verwendeten Code.
    /// </summary>
    /// <param name="grant">Betroffene Freigabe.</param>
    /// <returns>Ergebnis.</returns>
    public static UnlockResult AlreadyUsed(MovieGrant grant) => new(UnlockStatus.AlreadyUsed, grant);

    /// <summary>
    /// Erstellt ein Erfolgsergebnis.
    /// </summary>
    /// <param name="grant">Freigeschaltete Freigabe.</param>
    /// <returns>Ergebnis.</returns>
    public static UnlockResult Success(MovieGrant grant) => new(UnlockStatus.Success, grant);
}
