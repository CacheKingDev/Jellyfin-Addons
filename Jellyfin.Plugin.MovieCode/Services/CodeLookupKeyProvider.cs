using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.MovieCode.Services;

/// <summary>
/// Stellt einen installspezifischen Schlüssel für schnelle Code-Lookups bereit.
/// Der Schlüssel liegt getrennt von den Freigabedaten, damit ein gestohlenes
/// Freigabe-Backup allein keine Rückschlüsse auf gültige Codes zulässt.
/// </summary>
public sealed class CodeLookupKeyProvider
{
    private const int KeySizeBytes = 32;

    private readonly byte[] _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="CodeLookupKeyProvider"/> class.
    /// </summary>
    /// <param name="keyFilePath">Pfad der Schlüsseldatei.</param>
    /// <param name="logger">Logger.</param>
    public CodeLookupKeyProvider(string keyFilePath, ILogger<CodeLookupKeyProvider> logger)
    {
        _key = LoadOrCreateKey(keyFilePath, logger);
    }

    /// <summary>
    /// Berechnet den schnellen Lookup-Hash eines normalisierten Codes.
    /// </summary>
    /// <param name="normalizedCode">Der 6-stellige Code.</param>
    /// <returns>Hexadezimaler HMAC-SHA256-Wert.</returns>
    public string Compute(string normalizedCode)
    {
        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(normalizedCode));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] LoadOrCreateKey(string keyFilePath, ILogger logger)
    {
        if (File.Exists(keyFilePath))
        {
            try
            {
                var encoded = File.ReadAllText(keyFilePath, Encoding.UTF8).Trim();
                var existing = Convert.FromBase64String(encoded);
                if (existing.Length >= 16)
                {
                    return existing;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Movie Code: Lookup-Schlüssel konnte nicht gelesen werden; erzeuge einen neuen Schlüssel.");
            }
        }

        var key = RandomNumberGenerator.GetBytes(KeySizeBytes);
        try
        {
            var directory = Path.GetDirectoryName(keyFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(keyFilePath, Convert.ToBase64String(key), Encoding.UTF8);
        }
        catch (Exception ex)
        {
            // Ohne Schlüsseldatei funktioniert der Lookup weiterhin im Arbeitsspeicher;
            // nach einem Neustart greift die langsamere PBKDF2-Prüfung.
            logger.LogWarning(ex, "Movie Code: Lookup-Schlüssel konnte nicht gespeichert werden.");
        }

        return key;
    }
}
