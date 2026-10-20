using Jellyfin.Plugin.MovieCode.Configuration;
using Jellyfin.Plugin.MovieCode.Models;
using Jellyfin.Plugin.MovieCode.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.MovieCode.Controllers;

/// <summary>
/// Admin-Endpunkte für Freigaben.
/// </summary>
[ApiController]
[Route("MovieCode/Admin")]
[Authorize(Policy = "RequiresElevation")]
public sealed class MovieCodeAdminController : ControllerBase
{
    private readonly GrantStore _grantStore;
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovieCodeAdminController"/> class.
    /// </summary>
    /// <param name="grantStore">Freigaben.</param>
    /// <param name="userManager">Benutzerverwaltung.</param>
    public MovieCodeAdminController(GrantStore grantStore, IUserManager userManager)
    {
        _grantStore = grantStore;
        _userManager = userManager;
    }

    /// <summary>
    /// Gets alle Freigaben.
    /// </summary>
    /// <returns>Liste der Freigaben.</returns>
    [HttpGet("Grants")]
    [Produces("application/json")]
    public ActionResult<IReadOnlyList<GrantDto>> GetGrants()
    {
        var grants = _grantStore
            .GetAll()
            .Select(grant => new GrantDto(
                grant.Id,
                grant.UserId,
                grant.UserName,
                grant.ItemId,
                grant.ItemName,
                grant.Status.ToString(),
                grant.CreatedUtc,
                grant.UnlockedUtc,
                grant.ExpiresUtc,
                grant.CompletedUtc))
            .ToList();

        return Ok(grants);
    }

    /// <summary>
    /// Erzeugt einen neuen Einmalcode für einen Benutzer. Der Film wird erst beim
    /// Start der Wiedergabe festgelegt.
    /// </summary>
    /// <param name="request">Der ausgewählte Benutzer.</param>
    /// <returns>Der Code wird nur in dieser Antwort einmalig zurückgegeben.</returns>
    [HttpPost("Grants")]
    [Produces("application/json")]
    public ActionResult<CreateGrantResponse> CreateGrant([FromBody] CreateGrantRequest request)
    {
        var user = _userManager.GetUserById(request.UserId);
        if (user is null)
        {
            return BadRequest(new { message = "Benutzer nicht gefunden." });
        }

        var configuration = GetConfiguration();
        var (grant, code) = _grantStore.Create(
            user.Id,
            user.Username,
            configuration.CodeValidityHours);

        var unlockUrl = GetUnlockUrl(configuration);
        return Ok(new CreateGrantResponse(
            grant.Id,
            code,
            user.Username,
            grant.ExpiresUtc,
            unlockUrl));
    }

    /// <summary>
    /// Hebt eine Freigabe auf. Danach ist der Benutzer wieder uneingeschränkt.
    /// </summary>
    /// <param name="id">ID der Freigabe.</param>
    /// <returns>204, wenn die Freigabe entfernt wurde.</returns>
    [HttpPost("Grants/{id:guid}/Revoke")]
    public IActionResult RevokeGrant(Guid id)
        => _grantStore.Remove(id) ? NoContent() : NotFound();

    private static PluginConfiguration GetConfiguration()
        => MovieCodePlugin.Instance?.Configuration ?? new PluginConfiguration();

    private string GetUnlockUrl(PluginConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration.UnlockPageUrl))
        {
            return configuration.UnlockPageUrl;
        }

        var pathBase = Request.PathBase.HasValue ? Request.PathBase.Value : string.Empty;
        return $"{Request.Scheme}://{Request.Host}{pathBase}/MovieCode/";
    }
}
