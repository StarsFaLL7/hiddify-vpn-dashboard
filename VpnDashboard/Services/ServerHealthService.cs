using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnDashboard.Data;
using VpnDashboard.Data.Entities;
using VpnDashboard.Options;
using VpnDashboard.Probing;
using VpnDashboard.Subscriptions;

namespace VpnDashboard.Services;

/// <summary>Итог одного обхода — для лога и кнопки в админке.</summary>
public sealed record HealthRunSummary(int Targets, int Ok, int Failed, int Skipped, TimeSpan Elapsed)
{
    public static readonly HealthRunSummary Empty = new(0, 0, 0, 0, TimeSpan.Zero);
}

/// <summary>
/// Здоровье серверов из подписок: обход всех снимков с проверкой каждого сервера
/// и выдача списка тех, кого больше не стоит отдавать пользователям.
/// <para>
/// Источник данных — снимки в БД, а не живые запросы к провайдерам: в подписку уходит
/// именно снимок, значит и проверять надо его. Обновлением снимков занимается агрегатор.
/// </para>
/// </summary>
public sealed class ServerHealthService(
    IDbContextFactory<AppDbContext> dbFactory,
    ProbeService probe,
    IOptions<ProbeOptions> options,
    ILogger<ServerHealthService> logger)
{
    private readonly ProbeOptions _options = options.Value;

    public async Task<List<ServerHealth>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ServerHealth
            .OrderBy(h => h.IsOk)
            .ThenBy(h => h.DisplayName)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>Ключи серверов, провалившихся столько раз подряд, что их пора не отдавать.</summary>
    public async Task<HashSet<string>> GetDeadKeysAsync(CancellationToken ct = default)
    {
        if (!_options.Enabled || !_options.FilterEnabled)
            return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var keys = await db.ServerHealth
            .Where(h => h.IsSupported && h.ConsecutiveFailures >= _options.FailureThreshold)
            .Select(h => h.Key)
            .ToListAsync(ct);

        return new HashSet<string>(keys, StringComparer.Ordinal);
    }

    /// <summary>Полный обход: проверить всё, что лежит в снимках, и записать результат.</summary>
    public async Task<HealthRunSummary> RunAsync(CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var targets = await CollectTargetsAsync(ct);

        if (targets.Count == 0)
            return HealthRunSummary.Empty;

        var results = await ProbeAllAsync(targets, ct);
        await SaveAsync(results, ct);

        var summary = new HealthRunSummary(
            targets.Count,
            results.Count(r => r.Result.Verdict == ProbeVerdict.Ok),
            results.Count(r => r.Result.Verdict == ProbeVerdict.Failed),
            results.Count(r => r.Result.Verdict == ProbeVerdict.Unsupported),
            DateTime.UtcNow - startedAt);

        logger.LogInformation(
            "Проверка серверов: {Ok} живых, {Failed} нерабочих, {Skipped} без проверки за {Elapsed:0.#} с",
            summary.Ok, summary.Failed, summary.Skipped, summary.Elapsed.TotalSeconds);

        return summary;
    }

    /// <summary>
    /// Уникальные серверы из всех снимков. Один и тот же сервер приходит многим пользователям
    /// с разными UUID, но ключ у него общий — проверяем такой сервер один раз.
    /// </summary>
    private async Task<List<ProbeTarget>> CollectTargetsAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var snapshots = await db.SubscriptionSnapshots
            .Where(s => s.ContentGzip != null)
            .Select(s => s.ContentGzip)
            .AsNoTracking()
            .ToListAsync(ct);

        var targets = new Dictionary<string, ProbeTarget>(StringComparer.Ordinal);

        foreach (var content in snapshots)
        {
            foreach (var entry in SnapshotContent.Decompress(content))
            {
                if (ProbeTargets.TryExtract(entry) is { } target)
                    targets.TryAdd(target.Key, target);
            }
        }

        return targets.Values.ToList();
    }

    private async Task<List<(ProbeTarget Target, ProbeResult Result)>> ProbeAllAsync(
        List<ProbeTarget> targets,
        CancellationToken ct)
    {
        var results = new ConcurrentBag<(ProbeTarget, ProbeResult)>();
        using var gate = new SemaphoreSlim(Math.Max(1, _options.Concurrency));

        await Task.WhenAll(targets.Select(async target =>
        {
            await gate.WaitAsync(ct);
            try
            {
                results.Add((target, await probe.CheckAsync(target, ct)));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Падение проверки не должно ронять обход: считаем такой сервер непроверенным.
                logger.LogWarning("Проверка {Name} сорвалась: {Message}", target.DisplayName, ex.Message);
                results.Add((target, ProbeResult.Unsupported(ex.Message)));
            }
            finally
            {
                gate.Release();
            }
        }));

        return results.ToList();
    }

    private async Task SaveAsync(List<(ProbeTarget Target, ProbeResult Result)> results, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var keys = results.Select(r => r.Target.Key).ToList();
        var existing = await db.ServerHealth
            .Where(h => keys.Contains(h.Key))
            .ToDictionaryAsync(h => h.Key, ct);

        var now = DateTime.UtcNow;

        foreach (var (target, result) in results)
        {
            if (!existing.TryGetValue(target.Key, out var row))
            {
                row = new ServerHealth { Id = Guid.NewGuid(), Key = target.Key };
                db.ServerHealth.Add(row);
            }

            row.DisplayName = target.DisplayName;
            row.Protocol = target.Protocol;
            row.Address = target.Address;
            row.Port = target.Port;
            row.Security = target.Security;
            row.Network = target.Network;
            row.IsSupported = result.Verdict != ProbeVerdict.Unsupported;
            row.Method = result.Method;
            row.LastCheckAt = now;

            switch (result.Verdict)
            {
                case ProbeVerdict.Ok:
                    row.IsOk = true;
                    row.ConsecutiveFailures = 0;
                    row.LatencyMs = result.LatencyMs;
                    row.LastError = null;
                    row.LastOkAt = now;
                    break;

                case ProbeVerdict.Failed:
                    row.IsOk = false;
                    row.ConsecutiveFailures++;
                    row.LastError = result.Error;
                    break;

                // Непроверяемые не копят неудачи: отсутствие проверки не повод выкидывать сервер.
                default:
                    row.LastError = result.Error;
                    break;
            }
        }

        // Серверы, пропавшие из подписок, держим ещё какое-то время — провайдер мог
        // временно отдать урезанный каталог, и накопленная история ещё пригодится.
        var cutoff = now.AddDays(-Math.Max(1, _options.ForgetAfterDays));
        var forgotten = await db.ServerHealth.Where(h => h.UpdatedAt < cutoff).ToListAsync(ct);
        if (forgotten.Count > 0)
            db.ServerHealth.RemoveRange(forgotten);

        await db.SaveChangesAsync(ct);
    }
}
