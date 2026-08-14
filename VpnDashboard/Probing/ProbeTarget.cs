using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using VpnDashboard.Subscriptions;

namespace VpnDashboard.Probing;

/// <summary>Сервер из подписки, приведённый к виду, пригодному для проверки.</summary>
/// <param name="Key">Хэш конфига без учётных данных — общий для всех пользователей этого сервера.</param>
/// <param name="DisplayName">Имя записи для админки.</param>
/// <param name="Supported">Умеем ли мы такое проверять; иначе запись только показывается.</param>
/// <param name="XrayConfig">Полный конфиг записи для запуска ядра; null — запись-ссылка.</param>
public sealed record ProbeTarget(
    string Key,
    string DisplayName,
    string Protocol,
    string Address,
    int Port,
    string? Security,
    string? Network,
    string? Sni,
    bool Supported,
    JsonObject? XrayConfig)
{
    /// <summary>Можно ли проверить запись по-настоящему, подняв ядро.</summary>
    public bool CanRunInXray => Supported && XrayConfig is not null;
}

/// <summary>
/// Извлечение проверяемого сервера из записи подписки.
/// <para>
/// Ключ считается по аутбаунду, из которого вычищены учётные данные и отображаемое имя:
/// одна и та же точка входа, выданная разным пользователям, должна получить один ключ,
/// а разные транспорты одного хоста (gRPC, xhttp, reality) — разные.
/// </para>
/// </summary>
public static class ProbeTargets
{
    /// <summary>Протоколы поверх TCP, которые умеет и Xray, и наша проверка порта.</summary>
    private static readonly HashSet<string> TcpProtocols =
        new(["vless", "vmess", "trojan", "shadowsocks", "socks", "http"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Поля с учётными данными: в ключ не идут, иначе он станет персональным.</summary>
    private static readonly HashSet<string> SecretFields =
        new(["id", "password", "pass", "user", "email", "secretKey", "privateKey"], StringComparer.OrdinalIgnoreCase);

    public static ProbeTarget? TryExtract(JsonNode entry)
    {
        var name = SubscriptionEntry.Name(entry);

        return SubscriptionEntry.AsUri(entry) is { } uri
            ? FromUri(uri, name)
            : FromConfig(entry, name);
    }

    private static ProbeTarget? FromConfig(JsonNode entry, string name)
    {
        if (SubscriptionEntry.PrimaryOutbound(entry) is not { } outbound)
            return null;

        var protocol = outbound["protocol"]?.ToString() ?? string.Empty;
        var stream = outbound["streamSettings"];
        var security = stream?["security"]?.ToString();
        var network = stream?["network"]?.ToString() ?? "tcp";
        var sni = stream?["realitySettings"]?["serverName"]?.ToString()
                  ?? stream?["tlsSettings"]?["serverName"]?.ToString();

        var (address, port) = ReadEndpoint(outbound["settings"]);

        // Без адреса проверять нечего: так приходит, например, hysteria2 — Xray его не знает,
        // и панель кладёт в запись только имя и streamSettings.
        if (string.IsNullOrEmpty(address) || port <= 0)
            return null;

        var supported = TcpProtocols.Contains(protocol);

        return new ProbeTarget(
            Key: Hash(Scrub(outbound)),
            DisplayName: name,
            Protocol: protocol,
            Address: address,
            Port: port,
            Security: security,
            Network: network,
            Sni: sni,
            Supported: supported,
            // Ядру отдаём запись целиком: вместе с роутингом, фрагментацией и балансировщиками
            // провайдера — проверяем ровно то, что получит пользователь. Здесь держим ссылку
            // на исходный узел: копию снимет уже сама проверка, а в агрегаторе ключ считается
            // для каждой записи на каждый запрос, и лишнее копирование там ни к чему.
            XrayConfig: supported ? entry as JsonObject : null);
    }

    /// <summary>
    /// Ссылку разбираем только в простом виде «схема://данные@хост:порт». Ядром такие записи
    /// мы не проверяем, поэтому от разбора нужен лишь адрес для проверки порта.
    /// </summary>
    private static ProbeTarget? FromUri(string uri, string name)
    {
        var schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
            return null;

        var protocol = uri[..schemeEnd].ToLowerInvariant();
        var rest = uri[(schemeEnd + 3)..];

        var hash = rest.IndexOf('#');
        if (hash >= 0)
            rest = rest[..hash];

        string? query = null;
        var question = rest.IndexOf('?');
        if (question >= 0)
        {
            query = rest[(question + 1)..];
            rest = rest[..question];
        }

        var at = rest.LastIndexOf('@');
        var authority = at >= 0 ? rest[(at + 1)..] : rest;
        authority = authority.TrimEnd('/');

        var colon = authority.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(authority[(colon + 1)..], out var port) || port is <= 0 or > 65535)
            return null;

        var address = authority[..colon].Trim('[', ']');
        if (address.Length == 0)
            return null;

        var security = QueryValue(query, "security");
        var network = QueryValue(query, "type");
        var sni = QueryValue(query, "sni") ?? QueryValue(query, "host");

        return new ProbeTarget(
            Key: Hash($"uri|{protocol}|{address}|{port}|{security}|{network}|{sni}"),
            DisplayName: name,
            Protocol: protocol,
            Address: address,
            Port: port,
            Security: security,
            Network: network,
            Sni: sni,
            Supported: TcpProtocols.Contains(protocol),
            XrayConfig: null);
    }

    private static (string Address, int Port) ReadEndpoint(JsonNode? settings)
    {
        // vless/vmess держат адрес в vnext, trojan/shadowsocks/socks/http — в servers.
        var endpoint = (settings?["vnext"] as JsonArray)?.FirstOrDefault()
                       ?? (settings?["servers"] as JsonArray)?.FirstOrDefault()
                       ?? settings;

        var address = endpoint?["address"]?.ToString() ?? string.Empty;
        var port = endpoint?["port"] is { } portNode && int.TryParse(portNode.ToString(), out var parsed)
            ? parsed
            : 0;

        return (address, port);
    }

    private static string? QueryValue(string? query, string key)
    {
        if (string.IsNullOrEmpty(query))
            return null;

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0 && pair.AsSpan(0, eq).Equals(key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        return null;
    }

    /// <summary>Копия аутбаунда без имени и учётных данных — по ней считается ключ.</summary>
    private static string Scrub(JsonObject outbound)
    {
        var clone = outbound.DeepClone().AsObject();
        clone.Remove("tag");
        RemoveSecrets(clone);
        return clone.ToJsonString();
    }

    private static void RemoveSecrets(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(SecretFields.Contains).ToList())
                    obj.Remove(key);

                foreach (var (_, value) in obj)
                    RemoveSecrets(value);
                break;

            case JsonArray array:
                foreach (var item in array)
                    RemoveSecrets(item);
                break;
        }
    }

    private static string Hash(string value) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
