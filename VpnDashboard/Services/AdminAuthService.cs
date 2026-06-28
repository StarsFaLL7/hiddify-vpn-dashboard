using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using VpnDashboard.Options;

namespace VpnDashboard.Services;

/// <summary>
/// Проверка пароля администратора через PBKDF2 (хеш+соль хранятся в конфиге).
/// </summary>
public sealed class AdminAuthService(IOptions<AdminOptions> options)
{
    private const int Iterations = 100_000;
    private const int KeySize = 32;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    public bool VerifyPassword(string password)
    {
        var o = options.Value;
        if (!o.IsConfigured || string.IsNullOrEmpty(password))
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(o.PasswordSalt);
            expected = Convert.FromBase64String(o.PasswordHash);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>
    /// Генерирует пару (hash, salt) в base64 для записи в конфиг. Используется setup-командой.
    /// </summary>
    public static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, KeySize);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }
}
