using System.Text.Json.Nodes;

namespace VpnDashboard.Subscriptions;

/// <summary>
/// Одна запись подписки — либо полный Xray-конфиг (объект), либо ссылка-конфиг (строка).
/// Общие операции над обеими формами.
/// </summary>
public static class SubscriptionEntry
{
    /// <summary>
    /// Имя записи: у конфига — remarks (запасной вариант — тег первого outbound'а),
    /// у ссылки — фрагмент после «#».
    /// </summary>
    public static string Name(JsonNode entry)
    {
        if (AsUri(entry) is { } uri)
            return ProxyUri.ReadName(uri);

        if (entry is not JsonObject config)
            return "сервер";

        if (config["remarks"]?.ToString() is { Length: > 0 } remarks)
            return remarks;

        if (config["outbounds"] is JsonArray { Count: > 0 } outbounds &&
            outbounds[0]?["tag"]?.ToString() is { Length: > 0 } tag)
            return tag;

        return "сервер";
    }

    /// <summary>Ссылка-конфиг строкой либо null, если запись — объект-конфиг.</summary>
    public static string? AsUri(JsonNode entry) =>
        entry is JsonValue value && value.TryGetValue<string>(out var uri) ? uri : null;

    /// <summary>
    /// Аутбаунд, через который запись реально ходит наружу. Служебные (freedom/blackhole/dns)
    /// пропускаем — они есть в каждом конфиге и к серверу провайдера отношения не имеют.
    /// </summary>
    public static JsonObject? PrimaryOutbound(JsonNode entry)
    {
        if (entry is not JsonObject config || config["outbounds"] is not JsonArray outbounds)
            return null;

        foreach (var node in outbounds)
        {
            if (node is not JsonObject outbound)
                continue;

            var protocol = outbound["protocol"]?.ToString();
            if (!string.IsNullOrEmpty(protocol) && !IsUtility(protocol))
                return outbound;
        }

        return null;
    }

    private static bool IsUtility(string protocol) =>
        protocol is "freedom" or "blackhole" or "dns" or "loopback";
}
