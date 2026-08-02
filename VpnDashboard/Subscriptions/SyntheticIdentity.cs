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

    /// <summary>Детерминированный HWID источника в виде UUID — формат, который ожидают панели.</summary>
    public string HwidFor(SubscriptionSourceKind kind, Guid sourceId)
    {
        var mac = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{kind}:{sourceId:N}"));
        return new Guid(mac.AsSpan(0, 16)).ToString();
    }

    private static byte[] BuildKey(string secret, string passwordSalt)
    {
        // Соль пароля админки — приемлемый запасной ключ: она стабильна между рестартами
        // и не покидает сервер. Пустой ключ дал бы одинаковый HWID во всех инсталляциях.
        var material = !string.IsNullOrWhiteSpace(secret) ? secret : passwordSalt;
        return Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(material) ? "vpndashboard-hwid" : material);
    }
}
