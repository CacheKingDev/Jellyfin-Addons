using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.MovieCode.Models;

/// <summary>
/// Anfrage zum Einlösen eines Codes.
/// </summary>
/// <param name="Code">Der eingegebene 6-stellige Code.</param>
/// <param name="ItemId">Der Film, der gerade gestartet werden soll. Optional.</param>
public sealed record UnlockRequest(
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("itemId")] Guid? ItemId = null);

/// <summary>
/// Antwort der Freigabeseite.
/// </summary>
/// <param name="Success">Ob der Code akzeptiert wurde.</param>
/// <param name="Message">Meldung für den Benutzer.</param>
/// <param name="ItemName">Der freigegebene Film, falls bekannt.</param>
public sealed record UnlockResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("itemName")] string? ItemName = null);

/// <summary>
/// Anfrage zum Erzeugen einer Freigabe.
/// </summary>
/// <param name="UserId">Der ausgewählte Benutzer.</param>
public sealed record CreateGrantRequest(
    [property: JsonPropertyName("userId")] Guid UserId);

/// <summary>
/// Antwort nach dem Erzeugen einer Freigabe. Der Code wird nur hier einmalig zurückgegeben.
/// </summary>
/// <param name="Id">ID der Freigabe.</param>
/// <param name="Code">Der erzeugte 6-stellige Code.</param>
/// <param name="UserName">Benutzername.</param>
/// <param name="ExpiresUtc">Ablaufzeitpunkt des Codes.</param>
/// <param name="UnlockUrl">Adresse der Freigabeseite.</param>
public sealed record CreateGrantResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("expiresUtc")] DateTimeOffset? ExpiresUtc,
    [property: JsonPropertyName("unlockUrl")] string UnlockUrl);

/// <summary>
/// Freigabe für die Admin-Oberfläche.
/// </summary>
/// <param name="Id">ID der Freigabe.</param>
/// <param name="UserId">Benutzer-ID.</param>
/// <param name="UserName">Benutzername.</param>
/// <param name="ItemId">Gebundener Film, falls vorhanden.</param>
/// <param name="ItemName">Titel des gebundenen Films.</param>
/// <param name="Status">Status der Freigabe.</param>
/// <param name="CreatedUtc">Erstellungszeitpunkt.</param>
/// <param name="UnlockedUtc">Zeitpunkt der Code-Eingabe.</param>
/// <param name="ExpiresUtc">Ablaufzeitpunkt.</param>
/// <param name="CompletedUtc">Zeitpunkt des Filmendes.</param>
public sealed record GrantDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("userName")] string UserName,
    [property: JsonPropertyName("itemId")] Guid? ItemId,
    [property: JsonPropertyName("itemName")] string ItemName,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("createdUtc")] DateTimeOffset CreatedUtc,
    [property: JsonPropertyName("unlockedUtc")] DateTimeOffset? UnlockedUtc,
    [property: JsonPropertyName("expiresUtc")] DateTimeOffset? ExpiresUtc,
    [property: JsonPropertyName("completedUtc")] DateTimeOffset? CompletedUtc);

/// <summary>
/// Status einer Freigabe für den aktuell angemeldeten Benutzer.
/// </summary>
/// <param name="Restricted">Ob der Benutzer überhaupt eingeschränkt ist.</param>
/// <param name="Allowed">Ob der angefragte Film abgespielt werden darf.</param>
/// <param name="RequiresCode">Ob für den Film ein Code eingegeben werden muss.</param>
/// <param name="Status">Interner Status der Freigabe.</param>
/// <param name="ItemId">Der bereits festgelegte Film, falls vorhanden.</param>
/// <param name="ItemName">Titel des bereits festgelegten Films, falls vorhanden.</param>
/// <param name="Message">Meldung für den Benutzer.</param>
public sealed record GrantStatusDto(
    [property: JsonPropertyName("restricted")] bool Restricted,
    [property: JsonPropertyName("allowed")] bool Allowed,
    [property: JsonPropertyName("requiresCode")] bool RequiresCode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("itemId")] Guid? ItemId,
    [property: JsonPropertyName("itemName")] string? ItemName,
    [property: JsonPropertyName("message")] string? Message);

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
