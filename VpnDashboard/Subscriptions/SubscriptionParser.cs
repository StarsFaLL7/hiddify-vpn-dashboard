using System.Text.Json;
using System.Text.Json.Nodes;

namespace VpnDashboard.Subscriptions;

/// <summary>Результат разбора ответа провайдера.</summary>
/// <param name="Format">Человекочитаемое название формата для диагностики.</param>
/// <param name="Entries">Записи-серверы: полные Xray-конфиги (объекты) и ссылки (строки).</param>
/// <param name="Skipped">Сколько элементов ответа не удалось опознать.</param>
public sealed record ParsedSubscription(string Format, List<JsonNode> Entries, int Skipped);

/// <summary>
/// Приводит ответ провайдера к единому виду — массиву записей, каждая из которых
/// либо полный Xray-конфиг, либо ссылка-конфиг строкой.
/// <para>
/// Именно так подписку отдают панели, ориентированные на INCY/Happ: конфиги нужны там,
/// где есть балансировщики и собственный роутинг, а ссылки — для протоколов вроде
/// hysteria2, которых в Xray нет. Обе формы переносятся как есть, поэтому склейка
/// нескольких подписок ничего не теряет.
/// </para>
/// </summary>
public static class SubscriptionParser
{
    public static ParsedSubscription Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new ParsedSubscription("пустой ответ", [], 0);

        var trimmed = body.Trim();

        if (trimmed[0] is '[' or '{' && TryParseJson(trimmed) is { } fromJson)
            return fromJson;

        // Не JSON — значит список ссылок, обычно в base64.
        var decoded = ProxyUri.TryDecodeBase64(trimmed);
        if (decoded is not null && decoded.Contains("://", StringComparison.Ordinal))
            return FromUriList(decoded, "base64-список ссылок");

        if (trimmed.Contains("://", StringComparison.Ordinal))
            return FromUriList(trimmed, "список ссылок");

        return new ParsedSubscription("формат не распознан", [], 0);
    }

    private static ParsedSubscription? TryParseJson(string body)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }

        switch (node)
        {
            // Основной формат: массив, в котором объекты-конфиги могут соседствовать со ссылками.
            case JsonArray array:
            {
                var entries = new List<JsonNode>();
                var skipped = 0;

                foreach (var item in array)
                {
                    switch (item)
                    {
                        case JsonObject obj when obj["outbounds"] is JsonArray:
                            entries.Add(obj.DeepClone());
                            break;
                        case JsonValue value when value.TryGetValue<string>(out var s) && ProxyUri.LooksLikeUri(s):
                            entries.Add(JsonValue.Create(s.Trim()));
                            break;
                        default:
                            skipped++;
                            break;
                    }
                }

                return new ParsedSubscription(Describe(entries, "JSON"), entries, skipped);
            }

            // Один конфиг целиком.
            case JsonObject obj when obj["outbounds"] is JsonArray:
                return new ParsedSubscription("JSON, один конфиг", [obj.DeepClone()], 0);

            default:
                return null;
        }
    }

    private static ParsedSubscription FromUriList(string text, string format)
    {
        var entries = new List<JsonNode>();

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ProxyUri.LooksLikeUri(line))
                entries.Add(JsonValue.Create(line));
        }

        return new ParsedSubscription($"{format}, {entries.Count} ссылок", entries, 0);
    }

    private static string Describe(List<JsonNode> entries, string prefix)
    {
        var configs = entries.Count(e => e is JsonObject);
        var links = entries.Count - configs;
        return links == 0
            ? $"{prefix}, {configs} конфигов"
            : $"{prefix}, {configs} конфигов + {links} ссылок";
    }
}
