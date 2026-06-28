using Microsoft.EntityFrameworkCore;
using VpnDashboard.Data;

namespace VpnDashboard.Services;

public enum ShowcaseLinkKind
{
    Server,
    Manual,
    Shared
}

public sealed record ShowcaseLink(string Label, string Url, ShowcaseLinkKind Kind);

public sealed record ShowcaseModel(string UserName, IReadOnlyList<ShowcaseLink> Links);

/// <summary>Сборка набора subscription-ссылок витрины по персональному токену пользователя.</summary>
public sealed class ShowcaseService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<ShowcaseModel?> GetByTokenAsync(string token, CancellationToken ct = default)
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

        var links = new List<ShowcaseLink>();

        // Привязки к серверам Hiddify: одна универсальная ссылка на каждый активный сервер.
        foreach (var b in user.Bindings.Where(b => b is { IsEnabled: true, IsBroken: false }))
        {
            var domain = b.HiddifyServer.Domain.TrimEnd('/');
            var clientPath = b.HiddifyServer.ClientProxyPath.Trim('/');
            var url = $"https://{domain}/{clientPath}/{b.HiddifyUuid}/";
            links.Add(new ShowcaseLink(b.HiddifyServer.Name, url, ShowcaseLinkKind.Server));
        }

        // Ручные ссылки.
        foreach (var m in user.ManualSubscriptions.Where(m => m.IsEnabled))
            links.Add(new ShowcaseLink(m.Label, m.Url, ShowcaseLinkKind.Manual));

        // Общие подписки: глобально включённые И назначенные этому пользователю.
        var globalLinks = await db.UserGlobalSubscriptions
            .Where(x => x.LocalUserId == user.Id && x.GlobalSubscription.IsEnabled)
            .OrderBy(x => x.GlobalSubscription.Label)
            .Select(x => new ShowcaseLink(x.GlobalSubscription.Label, x.GlobalSubscription.Url, ShowcaseLinkKind.Shared))
            .ToListAsync(ct);
        links.AddRange(globalLinks);

        return new ShowcaseModel(user.Name, links);
    }
}
