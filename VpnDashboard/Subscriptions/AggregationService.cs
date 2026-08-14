using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnDashboard.Data;
using VpnDashboard.Data.Entities;
using VpnDashboard.Options;

namespace VpnDashboard.Subscriptions;

/// <summary>Состояние одного источника — для диагностики в админке.</summary>
public sealed record SourceStatus(
    string Label,
    SubscriptionSourceKind Kind,
    string FetchUrl,
    bool Ok,
    int? StatusCode,
    string? Format,
    int ConfigCount,
    DateTime? FetchedAt,
    bool FromCache,
    string? Error,
    IReadOnlyList<string> SampleNames);

/// <summary>Сколько записей досталось бы отдельной ссылке — для диагностики в админке.</summary>
public sealed record VariantCount(string Name, int Count);

/// <summary>Готовая агрегированная подписка.</summary>
public sealed record AggregatedSubscription(
    JsonArray Configs,
    SubscriptionUserInfo UserInfo,
    IReadOnlyList<SourceStatus> Sources,
    IReadOnlyList<VariantCount> Breakdown)
{
    /// <summary>Есть ли что отдавать клиенту.</summary>
    public bool HasContent => Configs.Count > 0;
}

/// <summary>
/// Собирает подписки всех провайдеров пользователя в одну.
/// Формат — массив записей: полные Xray-конфиги и ссылки-конфиги строками.
/// Записи переносятся как есть, поэтому склейка не теряет ни балансировщики,
/// ни персональные правила роутинга провайдеров, ни протоколы вроде hysteria2.
/// Меняется только отображаемое имя — теги outbound'ов трогать нельзя,
/// на них ссылается роутинг внутри записи.
/// </summary>
public sealed class AggregationService(
    IDbContextFactory<AppDbContext> dbFactory,
    UpstreamSubscriptionFetcher fetcher,
    SourceLockRegistry locks,
    IOptions<AggregationOptions> options,
    ILogger<AggregationService> logger)
{
    private readonly AggregationOptions _options = options.Value;

    /// <param name="variant">Какая из ссылок пользователя запрошена; null — общая.</param>
    public async Task<AggregatedSubscription> AggregateAsync(
        UserSubscriptions user,
        SubscriptionVariant? variant = null,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var results = await Task.WhenAll(
            user.Sources.Select(s => GetSourceAsync(s, forceRefresh, ct)));

        var statuses = new List<SourceStatus>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Сгруппированные записи собираются отдельно, чтобы в клиенте идти подряд,
        // а не вперемешку с серверами провайдера, у которого нашлись.
        var groups = _options.GroupRules
            .Select(r => (Rule: r, Items: new List<JsonNode>()))
            .ToList();
        var ungrouped = new List<JsonNode>();

        long upload = 0, download = 0, total = 0;
        var unlimited = false;

        foreach (var (source, snapshot, status) in results)
        {
            var names = new List<string>();

            foreach (var entry in Decompress(snapshot?.ContentGzip))
            {
                var name = ReadName(entry);
                if (IsExcluded(name))
                    continue;

                // Дубли ищем по записи без имени: один и тот же сервер у разных
                // провайдеров называется по-разному, но настройки подключения совпадают.
                if (!seen.Add(Signature(entry)))
                    continue;

                var group = groups.FirstOrDefault(g => g.Rule.Matches(name));
                if (group.Items is not null)
                {
                    group.Items.Add(entry);
                }
                else
                {
                    var renamed = WithName(entry, ServerName.WithPrefix(name, source.Prefix));
                    ungrouped.Add(renamed);
                    if (names.Count < 3)
                        names.Add(ReadName(renamed));
                }
            }

            if (snapshot is not null)
            {
                upload += snapshot.UploadBytes;
                download += snapshot.DownloadBytes;
                // total=0 у провайдера означает безлимит; складывать его с числами нельзя,
                // иначе безлимитная подписка занизит общий лимит.
                if (snapshot.TotalBytes == 0)
                    unlimited = true;
                else
                    total += snapshot.TotalBytes;
            }

            statuses.Add(status with { SampleNames = names });
        }

        var configs = new JsonArray();

        switch (variant?.Scope ?? SubscriptionScope.All)
        {
            case SubscriptionScope.Ungrouped:
                foreach (var entry in ungrouped)
                    configs.Add(entry);
                break;

            case SubscriptionScope.Group:
                // Индекс приходит из того же списка правил, но конфигурация могла и опустеть.
                if (variant!.GroupIndex >= 0 && variant.GroupIndex < groups.Count)
                    AddGroup(configs, groups[variant.GroupIndex]);
                break;

            default:
                AddEverything(configs, ungrouped, groups);
                break;
        }

        // Наполнение отдельных ссылок: считается всегда, чтобы админка показывала расклад
        // независимо от того, какой вариант запросили.
        var breakdown = new List<VariantCount>(groups.Count + 1)
        {
            new(Fallback(_options.DirectShortName, "Прямые"), ungrouped.Count)
        };
        breakdown.AddRange(groups.Select(g =>
            new VariantCount(Fallback(g.Rule.ShortName, g.Rule.Name), g.Items.Count)));

        var expire = DateTimeOffset.UtcNow.AddDays(_options.ExpireDays).ToUnixTimeSeconds();
        var info = new SubscriptionUserInfo(upload, download, unlimited ? 0 : total, expire);

        return new AggregatedSubscription(configs, info, statuses, breakdown);
    }

    /// <summary>Полный агрегат: серверы провайдеров и блок сгруппированных записей.</summary>
    private void AddEverything(
        JsonArray configs,
        List<JsonNode> ungrouped,
        List<(ConfigGroupRule Rule, List<JsonNode> Items)> groups)
    {
        // Сгруппированные записи идут одним блоком, но не в самом верху: сначала обычные
        // серверы провайдеров, чтобы группа не заслоняла начало списка.
        var head = Math.Clamp(_options.GroupInsertAfter, 0, ungrouped.Count);

        for (var i = 0; i < head; i++)
            configs.Add(ungrouped[i]);

        foreach (var group in groups)
            AddGroup(configs, group);

        for (var i = head; i < ungrouped.Count; i++)
            configs.Add(ungrouped[i]);
    }

    /// <summary>Записи одной группы под общим именем со сквозной нумерацией.</summary>
    private static void AddGroup(JsonArray configs, (ConfigGroupRule Rule, List<JsonNode> Items) group)
    {
        var index = 1;
        foreach (var entry in group.Items)
            configs.Add(WithName(entry, $"{group.Rule.Name} #{index++}"));
    }

    /// <summary>Берёт свежий снимок источника из БД либо идёт за ним к провайдеру.</summary>
    private async Task<(SubscriptionSource Source, SubscriptionSnapshot? Snapshot, SourceStatus Status)>
        GetSourceAsync(SubscriptionSource source, bool forceRefresh, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var snapshot = await db.SubscriptionSnapshots
            .FirstOrDefaultAsync(s => s.SourceKind == source.Kind && s.SourceId == source.Id, ct);

        if (!forceRefresh && IsFresh(snapshot, source))
            return (source, snapshot, Cached(source, snapshot!));

        var gate = locks.For(source.Kind, source.Id);
        await gate.WaitAsync(ct);
        try
        {
            // Пока ждали замок, соседний запрос мог уже всё обновить.
            snapshot = await db.SubscriptionSnapshots
                .FirstOrDefaultAsync(s => s.SourceKind == source.Kind && s.SourceId == source.Id, ct);
            if (!forceRefresh && IsFresh(snapshot, source))
                return (source, snapshot, Cached(source, snapshot!));

            var response = await fetcher.FetchAsync(source, ct);
            var parsed = response.Success ? SubscriptionParser.Parse(response.Body) : null;

            if (snapshot is null)
            {
                snapshot = new SubscriptionSnapshot
                {
                    Id = Guid.NewGuid(),
                    SourceKind = source.Kind,
                    SourceId = source.Id
                };
                db.SubscriptionSnapshots.Add(snapshot);
            }

            snapshot.LastAttemptAt = DateTime.UtcNow;
            snapshot.LastStatusCode = response.StatusCode;
            snapshot.SourceUrl = source.FetchUrl;

            // Успехом считаем только ответ, из которого удалось достать хотя бы один сервер:
            // пустой или нераспознанный ответ не должен затирать рабочий снимок.
            if (parsed is { Entries.Count: > 0 })
            {
                snapshot.ContentGzip = Compress(parsed.Entries);
                snapshot.ConfigCount = parsed.Entries.Count;
                snapshot.LastFormat = parsed.Format;
                snapshot.LastError = parsed.Skipped > 0 ? $"Не опознано записей: {parsed.Skipped}" : null;
                snapshot.FetchedAt = DateTime.UtcNow;
                snapshot.UploadBytes = response.UserInfo?.Upload ?? 0;
                snapshot.DownloadBytes = response.UserInfo?.Download ?? 0;
                snapshot.TotalBytes = response.UserInfo?.Total ?? 0;

                await db.SaveChangesAsync(ct);
                return (source, snapshot, Live(source, snapshot));
            }

            var error = response.Error ?? parsed?.Format ?? "пустой ответ";
            snapshot.LastError = error;
            if (parsed is not null)
                snapshot.LastFormat = parsed.Format;
            await db.SaveChangesAsync(ct);

            logger.LogWarning(
                "Источник {Label}: обновить не удалось ({Error}); отдаём снимок от {FetchedAt}",
                source.Label, error, snapshot.FetchedAt);

            return (source, snapshot.ContentGzip is null ? null : snapshot, Failed(source, snapshot, error));
        }
        finally
        {
            gate.Release();
        }
    }

    private bool IsFresh(SubscriptionSnapshot? snapshot, SubscriptionSource source) =>
        snapshot is { FetchedAt: not null, ContentGzip: not null } &&
        snapshot.SourceUrl == source.FetchUrl &&
        DateTime.UtcNow - snapshot.FetchedAt.Value < TimeSpan.FromMinutes(_options.CacheTtlMinutes);

    private static SourceStatus Cached(SubscriptionSource s, SubscriptionSnapshot snap) =>
        new(s.Label, s.Kind, s.FetchUrl, true, snap.LastStatusCode, snap.LastFormat,
            snap.ConfigCount, snap.FetchedAt, true, null, []);

    private static SourceStatus Live(SubscriptionSource s, SubscriptionSnapshot snap) =>
        new(s.Label, s.Kind, s.FetchUrl, true, snap.LastStatusCode, snap.LastFormat,
            snap.ConfigCount, snap.FetchedAt, false, snap.LastError, []);

    private static SourceStatus Failed(SubscriptionSource s, SubscriptionSnapshot snap, string error) =>
        new(s.Label, s.Kind, s.FetchUrl, false, snap.LastStatusCode, snap.LastFormat,
            snap.ContentGzip is null ? 0 : snap.ConfigCount, snap.FetchedAt, snap.ContentGzip is not null, error, []);

    private static string Fallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private bool IsExcluded(string name) =>
        _options.ExcludeNameContains.Any(p =>
            !string.IsNullOrWhiteSpace(p) && name.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Имя записи: у конфига — remarks (запасной вариант — тег первого outbound'а),
    /// у ссылки — фрагмент после «#».
    /// </summary>
    private static string ReadName(JsonNode entry)
    {
        if (entry is JsonValue value && value.TryGetValue<string>(out var uri))
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

    /// <summary>Запись с новым отображаемым именем. Теги outbound'ов не трогаем — на них ссылается роутинг.</summary>
    private static JsonNode WithName(JsonNode entry, string name)
    {
        if (entry is JsonValue value && value.TryGetValue<string>(out var uri))
            return JsonValue.Create(ProxyUri.WithName(uri, name));

        if (entry is JsonObject config)
            config["remarks"] = name;

        return entry;
    }

    /// <summary>Признак «того же сервера» — запись без отображаемого имени.</summary>
    private static string Signature(JsonNode entry)
    {
        if (entry is JsonValue value && value.TryGetValue<string>(out var uri))
            return ProxyUri.WithoutName(uri);

        if (entry is JsonObject config)
        {
            config.Remove("remarks");
            return config.ToJsonString();
        }

        return entry.ToJsonString();
    }

    private static byte[] Compress(List<JsonNode> entries)
    {
        var json = new JsonArray(entries.Select(e => e.DeepClone()).ToArray()).ToJsonString();
        var bytes = Encoding.UTF8.GetBytes(json);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(bytes);
        return output.ToArray();
    }

    private static List<JsonNode> Decompress(byte[]? gzipped)
    {
        if (gzipped is null || gzipped.Length == 0)
            return [];

        using var input = new MemoryStream(gzipped);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);

        return JsonNode.Parse(reader.ReadToEnd()) is JsonArray array
            ? array.Where(e => e is not null).Select(e => e!.DeepClone()).ToList()
            : [];
    }
}
