using Microsoft.EntityFrameworkCore;
using VpnDashboard.Data;
using VpnDashboard.Data.Entities;
using VpnDashboard.Subscriptions;

namespace VpnDashboard.Services;

/// <summary>
/// Разрешение персонального токена витрины в набор подписок пользователя.
/// Единственное место, где решается, что именно входит в набор, — им пользуются
/// и страница витрины, и агрегатор.
/// </summary>
public sealed class ShowcaseService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<UserSubscriptions?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var user = await db.LocalUsers
            .Include(u => u.Bindings).ThenInclude(b => b.HiddifyServer)
            .Include(u => u.ManualSubscriptions)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.ShowcaseToken == token, ct);

        if (user is null)
            return null;

        var sources = new List<SubscriptionSource>();

        // Привязки к серверам Hiddify: одна универсальная ссылка на каждый активный сервер.
        foreach (var b in user.Bindings.Where(b => b is { IsEnabled: true, IsBroken: false })
                     .OrderBy(b => b.HiddifyServer.Name))
        {
            var domain = b.HiddifyServer.Domain.TrimEnd('/');
            var clientPath = b.HiddifyServer.ClientProxyPath.Trim('/');
            var url = $"https://{domain}/{clientPath}/{b.HiddifyUuid}/";

            sources.Add(new SubscriptionSource(
                SubscriptionSourceKind.Server,
                b.Id,
                b.HiddifyServer.Name,
                Fallback(b.HiddifyServer.Prefix, b.HiddifyServer.Name),
                url,
                // Hiddify выбирает формат по User-Agent; путь xray/ отдаёт JSON-массив
                // Xray-конфигов гарантированно, без зависимости от автодетекта.
                url + "xray/",
                b.HiddifyServer.UpstreamUserAgent));
        }

        // Ручные ссылки.
        foreach (var m in user.ManualSubscriptions.Where(m => m.IsEnabled).OrderBy(m => m.Label))
        {
            sources.Add(new SubscriptionSource(
                SubscriptionSourceKind.Manual,
                m.Id,
                m.Label,
                Fallback(m.Prefix, m.Label),
                m.Url,
                m.Url,
                m.UpstreamUserAgent));
        }

        // Общие подписки: глобально включённые И назначенные этому пользователю.
        var globals = await db.UserGlobalSubscriptions
            .Where(x => x.LocalUserId == user.Id && x.GlobalSubscription.IsEnabled)
            .OrderBy(x => x.GlobalSubscription.Label)
            .Select(x => x.GlobalSubscription)
            .AsNoTracking()
            .ToListAsync(ct);

        foreach (var g in globals)
        {
            sources.Add(new SubscriptionSource(
                SubscriptionSourceKind.Shared,
                g.Id,
                g.Label,
                Fallback(g.Prefix, g.Label),
                g.Url,
                g.Url,
                g.UpstreamUserAgent));
        }

        return new UserSubscriptions(user.Id, user.Name, sources);
    }

    private static string Fallback(string? prefix, string label) =>
        string.IsNullOrWhiteSpace(prefix) ? label : prefix.Trim();
}
