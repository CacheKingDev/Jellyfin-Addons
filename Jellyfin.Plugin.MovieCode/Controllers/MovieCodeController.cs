using System.Text;
using Jellyfin.Plugin.MovieCode.Configuration;
using Jellyfin.Plugin.MovieCode.Models;
using Jellyfin.Plugin.MovieCode.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MovieCode.Controllers;

/// <summary>
/// Öffentliche Freigabeseite und Code-Einlösung.
/// </summary>
[ApiController]
[Route("MovieCode")]
public sealed class MovieCodeController : ControllerBase
{
    private const string UnlockPageResource = "Jellyfin.Plugin.MovieCode.Web.unlock.html";
    private const string ClientScriptResource = "Jellyfin.Plugin.MovieCode.Web.client.js";
    private const string AdminScriptResource = "Jellyfin.Plugin.MovieCode.Web.admin.js";

    private static readonly Lazy<string> UnlockPage = new(LoadUnlockPage);
    private static readonly Lazy<string> ClientScript = new(LoadClientScript);
    private static readonly Lazy<string> AdminScript = new(LoadAdminScript);

    private readonly GrantStore _grantStore;
    private readonly UnlockRateLimiter _rateLimiter;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<MovieCodeController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieCodeController"/> class.
    /// </summary>
    /// <param name="grantStore">Freigaben.</param>
    /// <param name="rateLimiter">Rate-Limit für Code-Eingaben.</param>
    /// <param name="libraryManager">Medienverwaltung.</param>
    /// <param name="logger">Logger.</param>
    public MovieCodeController(
        GrantStore grantStore,
        UnlockRateLimiter rateLimiter,
        ILibraryManager libraryManager,
        ILogger<MovieCodeController> logger)
    {
        _grantStore = grantStore;
        _rateLimiter = rateLimiter;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Liefert die Freigabeseite aus.
    /// </summary>
    /// <returns>HTML-Seite.</returns>
    [HttpGet("")]
    [HttpGet("Unlock")]
    [AllowAnonymous]
    [Produces("text/html")]
    public IActionResult GetUnlockPage()
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";

        return Content(UnlockPage.Value, "text/html; charset=utf-8");
    }

    /// <summary>
    /// Liefert das JavaScript, das im Jellyfin-Web-Client das Code-Popup anzeigt.
    /// </summary>
    /// <returns>JavaScript-Datei.</returns>
    [HttpGet("client.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public IActionResult GetClientScript()
    {
        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Content(ClientScript.Value, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Liefert das JavaScript, das die Code-Verwaltung in der Benutzer-Kindersicherung einblendet.
    /// </summary>
    /// <returns>JavaScript-Datei.</returns>
    [HttpGet("admin.js")]
    [AllowAnonymous]
    [Produces("application/javascript")]
    public IActionResult GetAdminScript()
    {
        Response.Headers.CacheControl = "public, max-age=300";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Content(AdminScript.Value, "application/javascript; charset=utf-8");
    }

    /// <summary>
    /// Liefert den Freigabestatus des angemeldeten Benutzers für einen Film.
    /// </summary>
    /// <param name="itemId">Der Film, der gestartet werden soll.</param>
    /// <returns>Status für das Popup im Web-Client.</returns>
    [HttpGet("Status")]
    [Authorize]
    [Produces("application/json")]
    public ActionResult<GrantStatusDto> GetStatus([FromQuery] Guid? itemId)
    {
        var userIdValue = User.FindFirst("Jellyfin-UserId")?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return Unauthorized();
        }

        var configuration = GetConfiguration();
        var item = itemId.HasValue ? _libraryManager.GetItemById(itemId.Value) : null;

        if (itemId.HasValue && item is null)
        {
            return NotFound(new { message = "Medium nicht gefunden." });
        }

        // Musik und Hörbücher bleiben standardmäßig uneingeschränkt.
        if (item is not null && configuration.RestrictOnlyVideo && item is not Video)
        {
            return Ok(Unrestricted());
        }

        var grants = _grantStore.GetAllForUser(userId);
        if (grants.Count == 0)
        {
            return Ok(Unrestricted());
        }

        var active = grants.FirstOrDefault(grant => grant.Status == GrantStatus.Active);
        if (active is not null)
        {
            if (active.ItemId is null || !itemId.HasValue || active.ItemId == itemId)
            {
                return Ok(new GrantStatusDto(
                    true,
                    true,
                    false,
                    active.Status.ToString(),
                    active.ItemId,
                    active.ItemName,
                    null));
            }

            return Ok(new GrantStatusDto(
                true,
                false,
                false,
                active.Status.ToString(),
                active.ItemId,
                active.ItemName,
                $"Für dich ist nur \"{active.ItemName}\" freigegeben."));
        }

        var pending = grants
            .Where(grant => grant.Status == GrantStatus.Pending)
            .OrderBy(grant => grant.BatchIndex)
            .FirstOrDefault();
        if (pending is not null)
        {
            var requestedItemName = item?.Name ?? "diesen Film";
            return Ok(new GrantStatusDto(
                true,
                false,
                true,
                pending.Status.ToString(),
                null,
                requestedItemName,
                "Bitte gib einen deiner 6-stelligen Einmalcodes ein."));
        }

        var latest = grants
            .OrderByDescending(grant => grant.CreatedUtc)
            .First();

        return latest.Status switch
        {
            GrantStatus.Completed => Ok(new GrantStatusDto(
                true,
                false,
                false,
                latest.Status.ToString(),
                latest.ItemId,
                latest.ItemName,
                "Es sind keine gültigen Einmalcodes mehr vorhanden. Bitte wende dich an den Administrator.")),

            _ => Ok(new GrantStatusDto(
                true,
                false,
                false,
                latest.Status.ToString(),
                latest.ItemId,
                latest.ItemName,
                "Die Einmalcodes sind abgelaufen. Bitte wende dich an den Administrator."))
        };
    }

    /// <summary>
    /// Löst einen 6-stelligen Einmalcode ein.
    /// </summary>
    /// <param name="request">Der eingegebene Code und optional der zu startende Film.</param>
    /// <returns>Ergebnis für die Freigabeseite bzw. das Popup.</returns>
    [HttpPost("Unlock")]
    [AllowAnonymous]
    [Produces("application/json")]
    public ActionResult<UnlockResponse> Unlock([FromBody] UnlockRequest request)
    {
        var configuration = GetConfiguration();
        var clientKey = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        if (!_rateLimiter.TryAcquire(
                clientKey,
                configuration.MaxFailedAttemptsPerTenMinutes,
                TimeSpan.FromMinutes(10)))
        {
            Response.Headers["Retry-After"] = "600";
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new UnlockResponse(false, "Zu viele Fehlversuche. Bitte warte einige Minuten."));
        }

        var normalized = CodeHasher.Normalize(request?.Code);
        if (normalized is null)
        {
            return Ok(new UnlockResponse(false, "Bitte gib einen 6-stelligen Code ein."));
        }

        var item = request?.ItemId is Guid requestedItemId
            ? _libraryManager.GetItemById(requestedItemId)
            : null;

        if (request?.ItemId is not null && item is not Video)
        {
            return Ok(new UnlockResponse(false, "Der Film wurde nicht gefunden."));
        }

        var result = _grantStore.TryUnlock(normalized, item?.Id, item?.Name);
        switch (result.Status)
        {
            case UnlockStatus.Success:
                _rateLimiter.Reset(clientKey);
                _logger.LogInformation(
                    "Movie Code: Code für Benutzer {UserId} und Film {ItemName} eingelöst.",
                    result.Grant!.UserId,
                    string.IsNullOrWhiteSpace(result.Grant.ItemName) ? "(noch nicht gewählt)" : result.Grant.ItemName);

                var unlockedName = string.IsNullOrWhiteSpace(result.Grant.ItemName)
                    ? null
                    : result.Grant.ItemName;

                return Ok(new UnlockResponse(
                    true,
                    unlockedName is null
                        ? "Code akzeptiert. Du kannst jetzt den gewünschten Film in Jellyfin starten."
                        : $"Code akzeptiert. Du kannst jetzt \"{unlockedName}\" starten.",
                    unlockedName));

            case UnlockStatus.AlreadyUsed:
                return Ok(new UnlockResponse(false, "Dieser Code wurde bereits verwendet."));

            case UnlockStatus.AlreadyActive:
            {
                var activeName = result.Grant?.ItemName;
                return Ok(new UnlockResponse(
                    false,
                    string.IsNullOrWhiteSpace(activeName)
                        ? "Es ist bereits ein Film freigegeben. Schließe zuerst diesen Film ab."
                        : $"Es ist bereits \"{activeName}\" freigegeben. Schließe zuerst diesen Film ab."));
            }

            default:
                return Ok(new UnlockResponse(false, "Der Code ist ungültig oder abgelaufen."));
        }
    }

    private static PluginConfiguration GetConfiguration()
        => MovieCodePlugin.Instance?.Configuration ?? new PluginConfiguration();

    private static GrantStatusDto Unrestricted()
        => new(false, true, false, "None", null, null, null);

    private static string LoadUnlockPage() => LoadResource(UnlockPageResource);

    private static string LoadClientScript() => LoadResource(ClientScriptResource);

    private static string LoadAdminScript() => LoadResource(AdminScriptResource);

    private static string LoadResource(string resourceName)
    {
        using var stream = typeof(MovieCodeController).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
