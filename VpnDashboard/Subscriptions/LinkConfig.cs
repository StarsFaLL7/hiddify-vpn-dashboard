using System.Text.Json;
using System.Text.Json.Nodes;

namespace VpnDashboard.Subscriptions;

/// <summary>
/// Превращает ссылку-конфиг (vless/vmess/trojan/ss) в полный Xray-конфиг.
/// <para>
/// Клиенты разбирают JSON-подписку как массив конфигов-объектов: строка-ссылка посреди массива
/// ломает разбор — Happ либо отбрасывает всё после неё, либо, если она первая, не находит ни
/// одного конфига. Поэтому подписки ссылками, склеенные с подписками конфигами, приходится
/// приводить к одному виду. Протоколы, которых нет в Xray (hysteria2, tuic…), так не перенести.
/// </para>
/// </summary>
public static class LinkConfig
{
    public static JsonObject? TryConvert(string uri, string name)
    {
        try
        {
            var outbound = TryBuildOutbound(uri.Trim());
            return outbound is null ? null : Wrap(outbound, CleanName(name));
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static JsonObject? TryBuildOutbound(string uri)
    {
        var schemeEnd = uri.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0)
            return null;

        var scheme = uri[..schemeEnd].ToLowerInvariant();
        var rest = uri[(schemeEnd + 3)..];

        var hash = rest.IndexOf('#');
        if (hash >= 0)
            rest = rest[..hash];

        return scheme switch
        {
            "vmess" => FromVmess(rest),
            "vless" or "trojan" or "ss" => FromStandard(scheme, rest),
            _ => null
        };
    }

    /// <summary>Ссылки вида «схема://учётка@хост:порт?параметры».</summary>
    private static JsonObject? FromStandard(string scheme, string rest)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var question = rest.IndexOf('?');
        if (question >= 0)
        {
            foreach (var pair in rest[(question + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0)
                    query[pair[..eq]] = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            }

            rest = rest[..question];
        }

        rest = rest.TrimEnd('/');

        // Старый формат shadowsocks: всё «метод:пароль@хост:порт» целиком в base64.
        if (scheme == "ss" && !rest.Contains('@'))
            rest = ProxyUri.TryDecodeBase64(rest) ?? rest;

        var at = rest.LastIndexOf('@');
        if (at <= 0)
            return null;

        var user = Uri.UnescapeDataString(rest[..at]);
        if (!TryReadEndpoint(rest[(at + 1)..], out var address, out var port))
            return null;

        switch (scheme)
        {
            case "vless":
            {
                var account = new JsonObject
                {
                    ["id"] = user,
                    ["encryption"] = Get(query, "encryption") ?? "none"
                };
                if (Get(query, "flow") is { } flow)
                    account["flow"] = flow;

                return Outbound("vless", Vnext(address, port, account), StreamSettings(query));
            }

            case "trojan":
                return Outbound("trojan",
                    Servers(new JsonObject { ["address"] = address, ["port"] = port, ["password"] = user }),
                    StreamSettings(query));

            default:
            {
                // Плагины (obfs, v2ray-plugin) ядро Xray не запускает.
                if (Get(query, "plugin") is not null)
                    return null;

                // SIP002: «метод:пароль» в base64, но встречается и открытым текстом.
                var credentials = user.Contains(':') ? user : ProxyUri.TryDecodeBase64(user);
                var colon = credentials?.IndexOf(':') ?? -1;
                if (colon <= 0)
                    return null;

                return Outbound("shadowsocks",
                    Servers(new JsonObject
                    {
                        ["address"] = address,
                        ["port"] = port,
                        ["method"] = credentials![..colon],
                        ["password"] = credentials[(colon + 1)..]
                    }),
                    StreamSettings(query));
            }
        }
    }

    /// <summary>vmess://base64(JSON) — формат v2rayN.</summary>
    private static JsonObject? FromVmess(string rest)
    {
        if (ProxyUri.TryDecodeBase64(rest) is not { } json || JsonNode.Parse(json) is not JsonObject data)
            return null;

        string? Read(string key) => data[key]?.ToString() is { Length: > 0 } value ? value : null;

        var address = Read("add");
        if (address is null || !int.TryParse(Read("port"), out var port) || port is <= 0 or > 65535)
            return null;

        // Поля v2rayN сводим к параметрам ссылки, чтобы транспорт собирался одним кодом.
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Put(string key, string? value)
        {
            if (value is not null)
                query[key] = value;
        }

        Put("type", Read("net"));
        Put("headerType", Read("type"));
        Put("host", Read("host"));
        Put("path", Read("path"));
        Put("security", Read("tls"));
        Put("sni", Read("sni"));
        Put("alpn", Read("alpn"));
        Put("fp", Read("fp"));
        // У grpc имя сервиса лежит в path.
        if (Read("net") == "grpc")
            Put("serviceName", Read("path"));

        var account = new JsonObject
        {
            ["id"] = Read("id"),
            ["alterId"] = int.TryParse(Read("aid"), out var aid) ? aid : 0,
            ["security"] = Read("scy") ?? "auto"
        };

        return Outbound("vmess", Vnext(address, port, account), StreamSettings(query));
    }

    /// <summary>Транспорт и шифрование; null — транспорт, которого нет в Xray.</summary>
    private static JsonObject? StreamSettings(Dictionary<string, string> query)
    {
        var network = (Get(query, "type") ?? "tcp").ToLowerInvariant();
        var host = Get(query, "host");
        var path = Get(query, "path");

        var stream = new JsonObject();

        switch (network)
        {
            case "tcp" or "raw":
                // «raw» — новое имя tcp; старое понимают все версии ядра, включая встроенные в клиенты.
                stream["network"] = "tcp";
                if (Get(query, "headerType") == "http")
                {
                    var request = new JsonObject { ["path"] = new JsonArray(path ?? "/") };
                    if (host is not null)
                        request["headers"] = new JsonObject { ["Host"] = new JsonArray(host) };
                    stream["tcpSettings"] = new JsonObject
                    {
                        ["header"] = new JsonObject { ["type"] = "http", ["request"] = request }
                    };
                }
                break;

            case "ws":
                stream["network"] = "ws";
                stream["wsSettings"] = PathAndHost(path, host);
                break;

            case "httpupgrade":
                stream["network"] = "httpupgrade";
                stream["httpupgradeSettings"] = PathAndHost(path, host);
                break;

            case "xhttp" or "splithttp":
            {
                var settings = PathAndHost(path, host);
                if (Get(query, "mode") is { } mode)
                    settings["mode"] = mode;
                if (Get(query, "extra") is { } extra && JsonNode.Parse(extra) is JsonObject extraObject)
                    settings["extra"] = extraObject;

                stream["network"] = "xhttp";
                stream["xhttpSettings"] = settings;
                break;
            }

            case "grpc":
            {
                var settings = new JsonObject
                {
                    ["serviceName"] = Get(query, "serviceName") ?? string.Empty,
                    ["multiMode"] = Get(query, "mode") == "multi"
                };
                if (Get(query, "authority") is { } authority)
                    settings["authority"] = authority;

                stream["network"] = "grpc";
                stream["grpcSettings"] = settings;
                break;
            }

            default:
                return null;
        }

        var security = (Get(query, "security") ?? "none").ToLowerInvariant();
        var sni = Get(query, "sni");
        var fingerprint = Get(query, "fp");

        switch (security)
        {
            case "tls":
            {
                var tls = new JsonObject();
                if ((sni ?? host) is { } serverName)
                    tls["serverName"] = serverName;
                if (fingerprint is not null)
                    tls["fingerprint"] = fingerprint;
                if (Get(query, "alpn") is { } alpn)
                    tls["alpn"] = new JsonArray(alpn.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(a => (JsonNode)a).ToArray());
                if (Get(query, "allowInsecure") is "1" or "true")
                    tls["allowInsecure"] = true;

                stream["security"] = "tls";
                stream["tlsSettings"] = tls;
                break;
            }

            case "reality":
            {
                if (Get(query, "pbk") is not { } publicKey)
                    return null;

                var reality = new JsonObject
                {
                    ["serverName"] = sni ?? string.Empty,
                    ["fingerprint"] = fingerprint ?? "chrome",
                    ["publicKey"] = publicKey,
                    ["shortId"] = Get(query, "sid") ?? string.Empty
                };
                if (Get(query, "spx") is { } spiderX)
                    reality["spiderX"] = spiderX;
                if (Get(query, "pqv") is { } verify)
                    reality["mldsa65Verify"] = verify;

                stream["security"] = "reality";
                stream["realitySettings"] = reality;
                break;
            }

            case "none" or "":
                stream["security"] = "none";
                break;

            default:
                return null;
        }

        return stream;
    }

    /// <summary>
    /// Конфиг целиком — в том же виде, что отдают панели: локальные SOCKS/HTTP, сервер и
    /// служебные аутбаунды. Роутинг минимальный: локальная сеть напрямую, остальное через сервер.
    /// </summary>
    private static JsonObject Wrap(JsonObject outbound, string name) => new()
    {
        ["remarks"] = name,
        ["log"] = new JsonObject { ["loglevel"] = "warning" },
        ["inbounds"] = new JsonArray(
            LocalInbound("socks", 10808),
            LocalInbound("http", 10809)),
        ["outbounds"] = new JsonArray(
            outbound,
            new JsonObject { ["tag"] = "direct", ["protocol"] = "freedom", ["settings"] = new JsonObject() },
            new JsonObject { ["tag"] = "block", ["protocol"] = "blackhole", ["settings"] = new JsonObject() }),
        ["routing"] = new JsonObject
        {
            ["domainStrategy"] = "AsIs",
            ["rules"] = new JsonArray(new JsonObject
            {
                ["type"] = "field",
                ["ip"] = new JsonArray("geoip:private"),
                ["outboundTag"] = "direct"
            })
        }
    };

    private static JsonObject LocalInbound(string protocol, int port) => new()
    {
        ["tag"] = protocol,
        ["port"] = port,
        ["listen"] = "127.0.0.1",
        ["protocol"] = protocol,
        ["sniffing"] = new JsonObject
        {
            ["enabled"] = true,
            ["destOverride"] = new JsonArray("http", "tls")
        },
        ["settings"] = protocol == "socks"
            ? new JsonObject { ["auth"] = "noauth", ["udp"] = true }
            : new JsonObject()
    };

    private static JsonObject? Outbound(string protocol, JsonObject settings, JsonObject? stream) =>
        stream is null
            ? null
            : new JsonObject
            {
                ["tag"] = "proxy",
                ["protocol"] = protocol,
                ["settings"] = settings,
                ["streamSettings"] = stream
            };

    private static JsonObject Vnext(string address, int port, JsonObject account) => new()
    {
        ["vnext"] = new JsonArray(new JsonObject
        {
            ["address"] = address,
            ["port"] = port,
            ["users"] = new JsonArray(account)
        })
    };

    private static JsonObject Servers(JsonObject server) => new() { ["servers"] = new JsonArray(server) };

    private static JsonObject PathAndHost(string? path, string? host)
    {
        var settings = new JsonObject { ["path"] = path ?? "/" };
        if (host is not null)
            settings["host"] = host;
        return settings;
    }

    private static bool TryReadEndpoint(string authority, out string address, out int port)
    {
        address = string.Empty;
        port = 0;

        var colon = authority.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(authority[(colon + 1)..], out port) || port is <= 0 or > 65535)
            return false;

        address = authority[..colon].Trim('[', ']');
        return address.Length > 0;
    }

    /// <summary>
    /// Happ дописывает к имени в ссылке параметры вроде «?serverDescription=…»; в конфиге
    /// им не место — иначе они окажутся в названии сервера.
    /// </summary>
    private static string CleanName(string name)
    {
        var marker = name.IndexOf("?serverDescription=", StringComparison.OrdinalIgnoreCase);
        return marker > 0 ? name[..marker].TrimEnd() : name;
    }

    private static string? Get(Dictionary<string, string> query, string key) =>
        query.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
}
