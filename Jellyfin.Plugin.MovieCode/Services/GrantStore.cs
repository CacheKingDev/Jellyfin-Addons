using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.MovieCode.Models;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MovieCode.Services;

/// <summary>
/// Entscheidung für eine laufende Wiedergabe.
/// </summary>
public enum PlaybackDecision
{
    /// <summary>
    /// Für diesen Benutzer gibt es keine Einschränkung.
    /// </summary>
    Unrestricted = 0,

    /// <summary>
    /// Die Wiedergabe ist erlaubt.
    /// </summary>
    Allowed = 1,

    /// <summary>
    /// Die Wiedergabe muss gestoppt werden.
    /// </summary>
    Blocked = 2
}

/// <summary>
/// Ergebnis der Prüfung einer Wiedergabe.
/// </summary>
/// <param name="Decision">Entscheidung.</param>
/// <param name="Grant">Die zugehörige Freigabe, falls vorhanden.</param>
public readonly record struct PlaybackEvaluation(PlaybackDecision Decision, MovieGrant? Grant);

/// <summary>
/// Speichert alle Freigaben als JSON-Datei und kapselt die Zustandsübergänge.
/// </summary>
public sealed class GrantStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GrantStore> _logger;

    private List<MovieGrant> _grants = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="GrantStore"/> class.
    /// </summary>
    /// <param name="filePath">Pfad der Zustandsdatei.</param>
    /// <param name="timeProvider">Zeitquelle (für Tests austauschbar).</param>
    /// <param name="logger">Logger.</param>
    public GrantStore(string filePath, TimeProvider timeProvider, ILogger<GrantStore> logger)
    {
        _filePath = filePath;
        _timeProvider = timeProvider;
        _logger = logger;
        Load();
    }

    /// <summary>
    /// Gets alle Freigaben, neueste zuerst.
    /// </summary>
    /// <returns>Kopien der Freigaben.</returns>
    public IReadOnlyList<MovieGrant> GetAll()
    {
        lock (_sync)
        {
            if (NormalizeExpired())
            {
                Save();
            }

            return _grants
                .OrderByDescending(grant => grant.CreatedUtc)
                .Select(grant => grant.Clone())
                .ToList();
        }
    }

    /// <summary>
    /// Gets die Freigabe eines Benutzers (unabhängig vom Zustand).
    /// </summary>
    /// <param name="userId">Benutzer-ID.</param>
    /// <returns>Kopie der Freigabe oder <see langword="null"/>.</returns>
    public MovieGrant? GetForUser(Guid userId)
    {
        lock (_sync)
        {
            if (NormalizeExpired())
            {
                Save();
            }

            return _grants
                .Where(grant => grant.UserId == userId)
                .OrderByDescending(grant => grant.CreatedUtc)
                .FirstOrDefault()
                ?.Clone();
        }
    }

    /// <summary>
    /// Erzeugt eine neue Freigabe und ersetzt eine eventuell vorhandene Freigabe des Benutzers.
    /// </summary>
    /// <param name="userId">Benutzer-ID.</param>
    /// <param name="userName">Benutzername.</param>
    /// <param name="itemId">Medien-ID.</param>
    /// <param name="itemName">Medientitel.</param>
    /// <param name="validityHours">Gültigkeit in Stunden; 0 bedeutet unbegrenzt.</param>
    /// <returns>Die neue Freigabe und der Klartext-Code.</returns>
    public (MovieGrant Grant, string Code) Create(
        Guid userId,
        string userName,
        Guid itemId,
        string itemName,
        int validityHours)
    {
        lock (_sync)
        {
            NormalizeExpired();
            _grants.RemoveAll(grant => grant.UserId == userId);

            var code = CodeHasher.GenerateCode();
            var (hash, salt) = CodeHasher.Hash(code);
            var now = _timeProvider.GetUtcNow();

            var grant = new MovieGrant
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                UserName = userName,
                ItemId = itemId,
                ItemName = itemName,
                CodeHash = hash,
                CodeSalt = salt,
                CreatedUtc = now,
                ExpiresUtc = validityHours > 0 ? now.AddHours(validityHours) : null,
                Status = GrantStatus.Pending
            };

            _grants.Add(grant);
            Save();
            return (grant.Clone(), code);
        }
    }

    /// <summary>
    /// Versucht, einen Code einzulösen.
    /// </summary>
    /// <param name="code">Benutzereingabe.</param>
    /// <returns>Ergebnis der Prüfung.</returns>
    public UnlockResult TryUnlock(string code)
    {
        lock (_sync)
        {
            if (NormalizeExpired())
            {
                Save();
            }

            var normalized = CodeHasher.Normalize(code);
            if (normalized is null)
            {
                return UnlockResult.Invalid();
            }

            foreach (var grant in _grants.Where(grant => grant.Status == GrantStatus.Pending))
            {
                if (!CodeHasher.Verify(normalized, grant.CodeHash, grant.CodeSalt))
                {
                    continue;
                }

                grant.Status = GrantStatus.Active;
                grant.UnlockedUtc = _timeProvider.GetUtcNow();
                Save();
                return UnlockResult.Success(grant.Clone());
            }

            foreach (var grant in _grants.Where(grant => grant.Status == GrantStatus.Active))
            {
                if (CodeHasher.Verify(normalized, grant.CodeHash, grant.CodeSalt))
                {
                    return UnlockResult.AlreadyUsed(grant.Clone());
                }
            }

            return UnlockResult.Invalid();
        }
    }

    /// <summary>
    /// Prüft, ob ein Benutzer ein Medium abspielen darf.
    /// </summary>
    /// <param name="userId">Benutzer-ID.</param>
    /// <param name="itemId">Medien-ID.</param>
    /// <returns>Entscheidung und Freigabe.</returns>
    public PlaybackEvaluation Evaluate(Guid userId, Guid itemId)
    {
        lock (_sync)
        {
            if (NormalizeExpired())
            {
                Save();
            }

            var grant = _grants
                .Where(candidate => candidate.UserId == userId)
                .OrderByDescending(candidate => candidate.CreatedUtc)
                .FirstOrDefault();

            if (grant is null)
            {
                return new PlaybackEvaluation(PlaybackDecision.Unrestricted, null);
            }

            return grant.Status switch
            {
                GrantStatus.Pending => new PlaybackEvaluation(PlaybackDecision.Blocked, grant.Clone()),
                GrantStatus.Active when grant.ItemId == itemId => new PlaybackEvaluation(PlaybackDecision.Allowed, grant.Clone()),
                _ => new PlaybackEvaluation(PlaybackDecision.Blocked, grant.Clone())
            };
        }
    }

    /// <summary>
    /// Markiert eine Freigabe als verbraucht.
    /// </summary>
    /// <param name="grantId">ID der Freigabe.</param>
    /// <param name="releaseUser">Ob die Freigabe entfernt werden soll, damit der Benutzer wieder alles sehen darf.</param>
    /// <returns><see langword="true"/>, wenn die Freigabe existierte.</returns>
    public bool Complete(Guid grantId, bool releaseUser)
    {
        lock (_sync)
        {
            var grant = _grants.FirstOrDefault(candidate => candidate.Id == grantId);
            if (grant is null)
            {
                return false;
            }

            if (releaseUser)
            {
                _grants.Remove(grant);
            }
            else
            {
                grant.Status = GrantStatus.Completed;
                grant.CompletedUtc = _timeProvider.GetUtcNow();
                grant.CodeHash = string.Empty;
                grant.CodeSalt = string.Empty;
            }

            Save();
            return true;
        }
    }

    /// <summary>
    /// Entfernt eine Freigabe vollständig. Danach ist der Benutzer wieder uneingeschränkt.
    /// </summary>
    /// <param name="grantId">ID der Freigabe.</param>
    /// <returns><see langword="true"/>, wenn die Freigabe existierte.</returns>
    public bool Remove(Guid grantId)
    {
        lock (_sync)
        {
            var removed = _grants.RemoveAll(grant => grant.Id == grantId) > 0;
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    private bool NormalizeExpired()
    {
        var now = _timeProvider.GetUtcNow();
        var changed = false;

        foreach (var grant in _grants)
        {
            if (grant.Status is GrantStatus.Pending or GrantStatus.Active
                && grant.ExpiresUtc is not null
                && grant.ExpiresUtc <= now)
            {
                grant.Status = GrantStatus.Expired;
                grant.CodeHash = string.Empty;
                grant.CodeSalt = string.Empty;
                changed = true;
            }
        }

        return changed;
    }

    private void Load()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_filePath, Encoding.UTF8);
            _grants = JsonSerializer.Deserialize<List<MovieGrant>>(json, SerializerOptions) ?? [];
        }
        catch (Exception ex)
        {
            // Eine kaputte Datei darf den Server nicht am Start hindern.
            _logger.LogError(ex, "Freigaben konnten nicht geladen werden; starte mit leerer Liste.");
            _grants = [];
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(_grants, SerializerOptions);
            var temporaryFile = _filePath + ".tmp";
            File.WriteAllText(temporaryFile, json, Encoding.UTF8);
            File.Move(temporaryFile, _filePath, true);
        }
        catch (Exception ex)
        {
            // Der In-Memory-Zustand bleibt gültig, auch wenn das Schreiben fehlschlägt.
            _logger.LogError(ex, "Freigaben konnten nicht gespeichert werden.");
        }
    }
}
