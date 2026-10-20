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
/// Ergebnis der Erzeugung eines Code-Pakets.
/// </summary>
/// <param name="BatchId">ID des Pakets.</param>
/// <param name="Codes">Die Klartext-Codes; werden nur einmal zurückgegeben.</param>
/// <param name="Grants">Die gespeicherten Freigaben.</param>
public sealed record GrantBatchCreation(
    Guid BatchId,
    IReadOnlyList<string> Codes,
    IReadOnlyList<MovieGrant> Grants);

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
    private readonly CodeLookupKeyProvider _lookupKeyProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GrantStore> _logger;

    private List<MovieGrant> _grants = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="GrantStore"/> class.
    /// </summary>
    /// <param name="filePath">Pfad der Zustandsdatei.</param>
    /// <param name="lookupKeyProvider">Schlüssel für schnelle Code-Lookups.</param>
    /// <param name="timeProvider">Zeitquelle (für Tests austauschbar).</param>
    /// <param name="logger">Logger.</param>
    public GrantStore(
        string filePath,
        CodeLookupKeyProvider lookupKeyProvider,
        TimeProvider timeProvider,
        ILogger<GrantStore> logger)
    {
        _filePath = filePath;
        _lookupKeyProvider = lookupKeyProvider;
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
                .ThenBy(grant => grant.BatchIndex)
                .Select(grant => grant.Clone())
                .ToList();
        }
    }

    /// <summary>
    /// Gets alle Codes eines Benutzers.
    /// </summary>
    /// <param name="userId">Benutzer-ID.</param>
    /// <returns>Kopien der Freigaben.</returns>
    public IReadOnlyList<MovieGrant> GetAllForUser(Guid userId)
    {
        lock (_sync)
        {
            if (NormalizeExpired())
            {
                Save();
            }

            return _grants
                .Where(grant => grant.UserId == userId)
                .OrderBy(grant => grant.BatchIndex)
                .ThenByDescending(grant => grant.CreatedUtc)
                .Select(grant => grant.Clone())
                .ToList();
        }
    }

    /// <summary>
    /// Gets die wichtigste Freigabe eines Benutzers: zuerst die aktive,
    /// danach den nächsten unbenutzten Code, sonst den letzten Eintrag.
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

            var grants = _grants.Where(grant => grant.UserId == userId).ToList();
            if (grants.Count == 0)
            {
                return null;
            }

            var active = grants
                .FirstOrDefault(grant => grant.Status == GrantStatus.Active);
            if (active is not null)
            {
                return active.Clone();
            }

            var pending = grants
                .Where(grant => grant.Status == GrantStatus.Pending)
                .OrderBy(grant => grant.BatchIndex)
                .FirstOrDefault();
            if (pending is not null)
            {
                return pending.Clone();
            }

            return grants
                .OrderByDescending(grant => grant.CreatedUtc)
                .First()
                .Clone();
        }
    }

    /// <summary>
    /// Erzeugt ein neues Paket aus Einmalcodes und ersetzt alle bisherigen
    /// Codes des Benutzers. Die Klartext-Codes werden nur einmal zurückgegeben.
    /// </summary>
    /// <param name="userId">Benutzer-ID.</param>
    /// <param name="userName">Benutzername.</param>
    /// <param name="codeCount">Anzahl der Codes.</param>
    /// <param name="validityHours">Gültigkeit in Stunden; 0 bedeutet unbegrenzt.</param>
    /// <returns>Paket-ID, Klartext-Codes und gespeicherte Freigaben.</returns>
    public GrantBatchCreation CreateBatch(
        Guid userId,
        string userName,
        int codeCount,
        int validityHours)
    {
        if (codeCount < 1)
        {
            codeCount = 1;
        }

        lock (_sync)
        {
            NormalizeExpired();

            // Ein neues Paket ersetzt die alten Codes des Benutzers. Dadurch
            // werden bereits ausgegebene, aber noch nicht verwendete Codes
            // ungültig, sobald der Administrator eine neue Liste erzeugt.
            _grants.RemoveAll(grant => grant.UserId == userId);

            var batchId = Guid.NewGuid();
            var now = _timeProvider.GetUtcNow();
            var expiresUtc = validityHours > 0 ? now.AddHours(validityHours) : (DateTimeOffset?)null;

            var usedLookups = _grants
                .Select(grant => grant.LookupHash)
                .Where(hash => !string.IsNullOrEmpty(hash))
                .ToHashSet(StringComparer.Ordinal);

            var codes = new List<string>(codeCount);
            var grants = new List<MovieGrant>(codeCount);

            for (var index = 1; index <= codeCount; index++)
            {
                string code;
                string lookupHash;
                var attempts = 0;

                do
                {
                    code = CodeHasher.GenerateCode();
                    lookupHash = _lookupKeyProvider.Compute(code);
                    attempts++;
                }
                while (usedLookups.Contains(lookupHash) && attempts < 100);

                if (usedLookups.Contains(lookupHash))
                {
                    throw new InvalidOperationException("Es konnte kein freier Einmalcode erzeugt werden.");
                }

                usedLookups.Add(lookupHash);
                var (hash, salt) = CodeHasher.Hash(code);

                var grant = new MovieGrant
                {
                    Id = Guid.NewGuid(),
                    BatchId = batchId,
                    BatchIndex = index,
                    UserId = userId,
                    UserName = userName,
                    CodeHash = hash,
                    CodeSalt = salt,
                    LookupHash = lookupHash,
                    CreatedUtc = now,
                    ExpiresUtc = expiresUtc,
                    Status = GrantStatus.Pending
                };

                codes.Add(code);
                grants.Add(grant);
                _grants.Add(grant);
            }

            Save();
            return new GrantBatchCreation(
                batchId,
                codes,
                grants.Select(grant => grant.Clone()).ToList());
        }
    }

    /// <summary>
    /// Versucht, einen Code einzulösen. Wird ein Film mitgegeben, wird die Freigabe
    /// sofort an diesen Film gebunden.
    /// </summary>
    /// <param name="code">Benutzereingabe.</param>
    /// <param name="itemId">Der gerade gestartete Film, falls bekannt.</param>
    /// <param name="itemName">Titel des Films, falls bekannt.</param>
    /// <returns>Ergebnis der Prüfung.</returns>
    public UnlockResult TryUnlock(string code, Guid? itemId = null, string? itemName = null)
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

            var lookupHash = _lookupKeyProvider.Compute(normalized);
            var pendingCandidates = _grants
                .Where(grant => grant.Status == GrantStatus.Pending
                    && (grant.LookupHash == lookupHash || string.IsNullOrEmpty(grant.LookupHash)))
                .ToList();

            foreach (var grant in pendingCandidates)
            {
                if (!CodeHasher.Verify(normalized, grant.CodeHash, grant.CodeSalt))
                {
                    continue;
                }

                var active = _grants.FirstOrDefault(candidate =>
                    candidate.UserId == grant.UserId
                    && candidate.Status == GrantStatus.Active);
                if (active is not null)
                {
                    return UnlockResult.AlreadyActive(active.Clone());
                }

                grant.Status = GrantStatus.Active;
                grant.UnlockedUtc = _timeProvider.GetUtcNow();
                grant.LookupHash = lookupHash;
                BindItem(grant, itemId, itemName);
                Save();
                return UnlockResult.Success(grant.Clone());
            }

            var activeCandidates = _grants
                .Where(grant => grant.Status == GrantStatus.Active
                    && (grant.LookupHash == lookupHash || string.IsNullOrEmpty(grant.LookupHash)));

            foreach (var grant in activeCandidates)
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
    /// Prüft, ob ein Benutzer ein Medium abspielen darf. Ist die Freigabe noch an
    /// keinen Film gebunden, wird sie beim ersten Start an dieses Medium gebunden.
    /// </summary>
    /// <param name="userId">Benutzer-ID.</param>
    /// <param name="itemId">Medien-ID.</param>
    /// <param name="itemName">Titel des Mediums.</param>
    /// <returns>Entscheidung und Freigabe.</returns>
    public PlaybackEvaluation Evaluate(Guid userId, Guid itemId, string? itemName = null)
    {
        lock (_sync)
        {
            if (NormalizeExpired())
            {
                Save();
            }

            var grants = _grants.Where(grant => grant.UserId == userId).ToList();
            if (grants.Count == 0)
            {
                return new PlaybackEvaluation(PlaybackDecision.Unrestricted, null);
            }

            var active = grants.FirstOrDefault(grant => grant.Status == GrantStatus.Active);
            if (active is not null)
            {
                if (active.ItemId is null)
                {
                    BindItem(active, itemId, itemName);
                    Save();
                    return new PlaybackEvaluation(PlaybackDecision.Allowed, active.Clone());
                }

                if (active.ItemId == itemId)
                {
                    return new PlaybackEvaluation(PlaybackDecision.Allowed, active.Clone());
                }

                return new PlaybackEvaluation(PlaybackDecision.Blocked, active.Clone());
            }

            var pending = grants
                .Where(grant => grant.Status == GrantStatus.Pending)
                .OrderBy(grant => grant.BatchIndex)
                .FirstOrDefault();
            if (pending is not null)
            {
                return new PlaybackEvaluation(PlaybackDecision.Blocked, pending.Clone());
            }

            var latest = grants
                .OrderByDescending(grant => grant.CreatedUtc)
                .First();
            return new PlaybackEvaluation(PlaybackDecision.Blocked, latest.Clone());
        }
    }

    /// <summary>
    /// Markiert einen Code als verbraucht. Bei <paramref name="releaseUser"/>
    /// werden alle übrigen Codes des Benutzers ebenfalls entfernt.
    /// </summary>
    /// <param name="grantId">ID des Codes.</param>
    /// <param name="releaseUser">Ob der Benutzer vollständig freigegeben werden soll.</param>
    /// <returns><see langword="true"/>, wenn der Code existierte.</returns>
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
                _grants.RemoveAll(candidate => candidate.UserId == grant.UserId);
            }
            else
            {
                grant.Status = GrantStatus.Completed;
                grant.CompletedUtc = _timeProvider.GetUtcNow();
                grant.CodeHash = string.Empty;
                grant.CodeSalt = string.Empty;
                grant.LookupHash = string.Empty;
            }

            Save();
            return true;
        }
    }

    /// <summary>
    /// Entfernt einen einzelnen Code.
    /// </summary>
    /// <param name="grantId">ID des Codes.</param>
    /// <returns><see langword="true"/>, wenn der Code existierte.</returns>
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

    /// <summary>
    /// Entfernt ein gesamtes Code-Paket.
    /// </summary>
    /// <param name="batchId">ID des Pakets.</param>
    /// <returns><see langword="true"/>, wenn mindestens ein Code existierte.</returns>
    public bool RemoveBatch(Guid batchId)
    {
        lock (_sync)
        {
            var removed = _grants.RemoveAll(grant => grant.BatchId == batchId) > 0;
            if (removed)
            {
                Save();
            }

            return removed;
        }
    }

    private static void BindItem(MovieGrant grant, Guid? itemId, string? itemName)
    {
        if (itemId is null || grant.ItemId is not null)
        {
            return;
        }

        grant.ItemId = itemId;
        if (!string.IsNullOrWhiteSpace(itemName))
        {
            grant.ItemName = itemName;
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
                grant.LookupHash = string.Empty;
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

            // Altbestand ohne Paketinformationen: jeden Code als eigenes Paket behandeln.
            var changed = false;
            foreach (var grant in _grants)
            {
                if (grant.BatchId == Guid.Empty)
                {
                    grant.BatchId = Guid.NewGuid();
                    changed = true;
                }

                if (grant.BatchIndex <= 0)
                {
                    grant.BatchIndex = 1;
                    changed = true;
                }
            }

            if (changed)
            {
                Save();
            }
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
