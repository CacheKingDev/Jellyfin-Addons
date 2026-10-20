using System.Globalization;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.MovieCode.Services;

/// <summary>
/// Erzeugt und prüft 6-stellige Einmalcodes. Gespeichert wird nur ein PBKDF2-Hash.
/// </summary>
public static class CodeHasher
{
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const int Iterations = 100_000;

    /// <summary>
    /// Erzeugt einen zufälligen 6-stelligen Code inklusive führender Nullen.
    /// </summary>
    /// <returns>Der Code, z. B. <c>004271</c>.</returns>
    public static string GenerateCode()
        => RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    /// <summary>
    /// Entfernt Trennzeichen und prüft, ob genau 6 Ziffern übrig bleiben.
    /// </summary>
    /// <param name="input">Benutzereingabe.</param>
    /// <returns>Normalisierter Code oder <see langword="null"/>.</returns>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var digits = new string(input.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 6 ? digits : null;
    }

    /// <summary>
    /// Hasht einen Code mit einem zufälligen Salt.
    /// </summary>
    /// <param name="code">Normalisierter Code.</param>
    /// <returns>Hash und Salt als Base64.</returns>
    public static (string Hash, string Salt) Hash(string code)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(code, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>
    /// Prüft einen Code gegen einen gespeicherten Hash.
    /// </summary>
    /// <param name="code">Normalisierter Code.</param>
    /// <param name="hash">Gespeicherter Hash (Base64).</param>
    /// <param name="salt">Gespeichertes Salt (Base64).</param>
    /// <returns><see langword="true"/>, wenn der Code passt.</returns>
    public static bool Verify(string code, string hash, string salt)
    {
        if (string.IsNullOrEmpty(hash) || string.IsNullOrEmpty(salt))
        {
            return false;
        }

        try
        {
            var expected = Convert.FromBase64String(hash);
            var saltBytes = Convert.FromBase64String(salt);
            var actual = Rfc2898DeriveBytes.Pbkdf2(code, saltBytes, Iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
