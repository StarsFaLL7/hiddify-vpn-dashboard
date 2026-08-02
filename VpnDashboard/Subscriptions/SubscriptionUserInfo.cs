using System.Globalization;

namespace VpnDashboard.Subscriptions;

/// <summary>
/// Содержимое заголовка subscription-userinfo: расход трафика и срок действия.
/// Формат: <c>upload=0; download=1073741824; total=10737418240; expire=1735689600</c>.
/// </summary>
public sealed record SubscriptionUserInfo(long Upload, long Download, long Total, long Expire)
{
    public static readonly SubscriptionUserInfo Empty = new(0, 0, 0, 0);

    public static SubscriptionUserInfo? Parse(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            return null;

        long upload = 0, download = 0, total = 0, expire = 0;
        var seen = false;

        foreach (var part in header.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = part[..eq].Trim();
            if (!long.TryParse(part[(eq + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                continue;

            switch (key.ToLowerInvariant())
            {
                case "upload": upload = value; seen = true; break;
                case "download": download = value; seen = true; break;
                case "total": total = value; seen = true; break;
                case "expire": expire = value; seen = true; break;
            }
        }

        return seen ? new SubscriptionUserInfo(upload, download, total, expire) : null;
    }

    public string ToHeaderValue() =>
        $"upload={Upload}; download={Download}; total={Total}; expire={Expire}";
}
