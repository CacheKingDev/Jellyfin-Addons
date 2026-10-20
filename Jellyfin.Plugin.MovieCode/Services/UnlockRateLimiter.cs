namespace Jellyfin.Plugin.MovieCode.Services;

/// <summary>
/// Einfaches In-Memory-Rate-Limit für die Code-Eingabe.
/// </summary>
public sealed class UnlockRateLimiter
{
    private readonly object _sync = new();
    private readonly Dictionary<string, List<DateTimeOffset>> _attempts = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnlockRateLimiter"/> class.
    /// </summary>
    /// <param name="timeProvider">Zeitquelle.</param>
    public UnlockRateLimiter(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Prüft, ob ein weiterer Versuch erlaubt ist, und zählt ihn.
    /// </summary>
    /// <param name="key">Schlüssel, üblicherweise die IP-Adresse.</param>
    /// <param name="maxAttempts">Maximale Versuche im Zeitfenster; 0 deaktiviert das Limit.</param>
    /// <param name="window">Zeitfenster.</param>
    /// <returns><see langword="true"/>, wenn der Versuch erlaubt ist.</returns>
    public bool TryAcquire(string key, int maxAttempts, TimeSpan window)
    {
        if (maxAttempts <= 0)
        {
            return true;
        }

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            if (!_attempts.TryGetValue(key, out var timestamps))
            {
                timestamps = [];
                _attempts[key] = timestamps;
            }

            timestamps.RemoveAll(timestamp => now - timestamp > window);
            if (timestamps.Count >= maxAttempts)
            {
                return false;
            }

            timestamps.Add(now);
            return true;
        }
    }

    /// <summary>
    /// Setzt die Versuche für einen Schlüssel zurück (z. B. nach erfolgreicher Eingabe).
    /// </summary>
    /// <param name="key">Schlüssel.</param>
    public void Reset(string key)
    {
        lock (_sync)
        {
            _attempts.Remove(key);
        }
    }
}
