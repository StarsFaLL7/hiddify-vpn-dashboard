using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VpnDashboard.Data.Entities;
using VpnDashboard.Options;

namespace VpnDashboard.Subscriptions;

/// <summary>
/// Синтетический идентификатор устройства для запросов к провайдерам.
/// <para>
/// HWID выводится из идентификатора ИСТОЧНИКА, а не пользователя или реального устройства.
/// Панели с лимитом устройств считают устройства по заголовку X-Hwid при скачивании подписки,
/// поэтому стабильный HWID на источник означает ровно одно «устройство» на провайдера —
/// сколько бы наших пользователей ни делили общую подписку и сколько бы телефонов у них ни было.
/// </para>
/// <para>
/// Само VPN-подключение (VLESS/Trojan/Hysteria) никакого HWID не передаёт — там есть только
/// UUID/пароль из конфига, поэтому реальный идентификатор устройства нодам недоступен.
/// Ограничением остаётся лимит одновременных подключений/IP на стороне провайдера.
/// </para>
/// </summary>
public sealed class SyntheticIdentity(IOptions<AggregationOptions> aggregation, IOptions<AdminOptions> admin)
{
    private readonly byte[] _key = BuildKey(aggregation.Value.HwidSecret, admin.Value.PasswordSalt);

    /// <summary>
    /// Детерминированный HWID источника: 16 hex-символов без дефисов — как Android ID
    /// реального устройства. Часть панелей (например, frservice) отдаёт 403 по
    /// nginx-правилу на HWID в формате UUID с дефисами, поэтому Guid-формат запрещён.
    /// </summary>
    public string HwidFor(SubscriptionSourceKind kind, Guid sourceId)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{kind}:{sourceId:N}"));
        return Convert.ToHexString(mac, 0, 8).ToLowerInvariant();
    }

    private static byte[] BuildKey(string secret, string passwordSalt)
    {
        // Соль пароля админки — приемлемый запасной ключ: она стабильна между рестартами
        // и не покидает сервер. Пустой ключ дал бы одинаковый HWID во всех инсталляциях.
        var material = !string.IsNullOrWhiteSpace(secret) ? secret : passwordSalt;
        return Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(material) ? "vpndashboard-hwid" : material);
    }
}
