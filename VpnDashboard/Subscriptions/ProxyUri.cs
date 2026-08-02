using System.Text;

namespace VpnDashboard.Subscriptions;

/// <summary>
/// Работа со ссылкой-конфигом (vless/hysteria2/ss/…) как со строкой: имя сервера
/// лежит во фрагменте после «#». Разбирать саму ссылку не нужно — она передаётся
/// клиенту как есть, включая протоколы, которых нет в Xray.
/// </summary>
public static class ProxyUri
{
    public static bool LooksLikeUri(string value) =>
        value.Contains("://", StringComparison.Ordinal) && !value.StartsWith('#');

    /// <summary>Отображаемое имя из фрагмента; если его нет — хост.</summary>
    public static string ReadName(string uri)
    {
        var hash = uri.IndexOf('#');
        if (hash >= 0 && hash < uri.Length - 1)
            return Uri.UnescapeDataString(uri[(hash + 1)..]);

        var scheme = uri.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
            return "сервер";

        var rest = uri[(scheme + 3)..];
        var at = rest.IndexOf('@');
        if (at >= 0)
            rest = rest[(at + 1)..];

        var end = rest.IndexOfAny(['/', '?', '#']);
        return end < 0 ? rest : rest[..end];
    }

    /// <summary>Ссылка с заменённым отображаемым именем.</summary>
    public static string WithName(string uri, string name)
    {
        var hash = uri.IndexOf('#');
        var body = hash >= 0 ? uri[..hash] : uri;
        return $"{body}#{Uri.EscapeDataString(name)}";
    }

    /// <summary>Ссылка без имени — по ней сравниваем записи на дубли.</summary>
    public static string WithoutName(string uri)
    {
        var hash = uri.IndexOf('#');
        return hash >= 0 ? uri[..hash] : uri;
    }

    /// <summary>Декодирует base64/base64url, дописывая недостающий padding.</summary>
    public static string? TryDecodeBase64(string value)
    {
        var normalized = string.Concat(value.Where(c => !char.IsWhiteSpace(c)))
            .Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '=');
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
