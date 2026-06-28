using System.Security.Cryptography;

namespace VpnDashboard.Services;

/// <summary>Генерация криптослучайных токенов витрины (base64url, ≥32 байта).</summary>
public static class TokenGenerator
{
    public static string NewShowcaseToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
