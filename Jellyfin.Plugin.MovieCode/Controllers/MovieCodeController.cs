using System.Text;
using Jellyfin.Plugin.MovieCode.Configuration;
using Jellyfin.Plugin.MovieCode.Models;
using Jellyfin.Plugin.MovieCode.Services;
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

    private static readonly Lazy<string> UnlockPage = new(LoadUnlockPage);

    private readonly GrantStore _grantStore;
    private readonly UnlockRateLimiter _rateLimiter;
    private readonly ILogger<MovieCodeController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieCodeController"/> class.
    /// </summary>
    /// <param name="grantStore">Freigaben.</param>
    /// <param name="rateLimiter">Rate-Limit für Code-Eingaben.</param>
    /// <param name="logger">Logger.</param>
    public MovieCodeController(
        GrantStore grantStore,
        UnlockRateLimiter rateLimiter,
        ILogger<MovieCodeController> logger)
    {
        _grantStore = grantStore;
        _rateLimiter = rateLimiter;
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
    /// Löst einen 6-stelligen Einmalcode ein.
    /// </summary>
    /// <param name="request">Der eingegebene Code.</param>
    /// <returns>Ergebnis für die Freigabeseite.</returns>
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

        var result = _grantStore.TryUnlock(normalized);
        switch (result.Status)
        {
            case UnlockStatus.Success:
                _rateLimiter.Reset(clientKey);
                _logger.LogInformation(
                    "Movie Code: Code für Benutzer {UserId} und Film {ItemName} eingelöst.",
                    result.Grant!.UserId,
                    result.Grant.ItemName);
                return Ok(new UnlockResponse(
                    true,
                    "Code akzeptiert. Du kannst den Film jetzt in Jellyfin starten.",
                    result.Grant.ItemName));

            case UnlockStatus.AlreadyUsed:
                return Ok(new UnlockResponse(false, "Dieser Code wurde bereits verwendet."));

            default:
                return Ok(new UnlockResponse(false, "Der Code ist ungültig oder abgelaufen."));
        }
    }

    private static PluginConfiguration GetConfiguration()
        => MovieCodePlugin.Instance?.Configuration ?? new PluginConfiguration();

    private static string LoadUnlockPage()
    {
        using var stream = typeof(MovieCodeController).Assembly.GetManifestResourceStream(UnlockPageResource)
            ?? throw new InvalidOperationException($"Embedded resource '{UnlockPageResource}' not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
