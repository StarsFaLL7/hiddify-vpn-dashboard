using Microsoft.EntityFrameworkCore;
using VpnDashboard.Data;
using VpnDashboard.Data.Entities;
using VpnDashboard.Hiddify;

namespace VpnDashboard.Services;

/// <summary>Непривязанный Hiddify-юзер, найденный на сервере при синхронизации.</summary>
public sealed record UnboundHiddifyUser(string Uuid, string Name, bool Enabled, double UsageGb);

/// <summary>Результат синхронизации сервера с панелью Hiddify.</summary>
public sealed record ServerSyncResult(
    bool Success,
    string? Error,
    IReadOnlyList<UnboundHiddifyUser> Unbound,
    int BrokenCount);

/// <summary>CRUD справочника серверов Hiddify и синхронизация с панелью.</summary>
public sealed class ServerService(IDbContextFactory<AppDbContext> dbFactory, IHiddifyApiClient hiddify)
{
    public async Task<List<HiddifyServer>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.HiddifyServers
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
    }

    public async Task<HiddifyServer?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.HiddifyServers.FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<HiddifyServer> CreateAsync(HiddifyServer server, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        server.Id = Guid.NewGuid();
        db.HiddifyServers.Add(server);
        await db.SaveChangesAsync(ct);
        return server;
    }

    public async Task UpdateAsync(HiddifyServer server, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.HiddifyServers.FirstOrDefaultAsync(s => s.Id == server.Id, ct);
        if (existing is null)
            return;

        existing.Name = server.Name;
        existing.Domain = server.Domain;
        existing.AdminProxyPath = server.AdminProxyPath;
        existing.ClientProxyPath = server.ClientProxyPath;
        existing.AdminUuid = server.AdminUuid;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.HiddifyServers.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (existing is null)
            return;

        db.HiddifyServers.Remove(existing);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Синхронизация с панелью по явной кнопке: помечает broken-привязки (UUID исчез) и
    /// возвращает список непривязанных Hiddify-юзеров. Никаких авто-действий не выполняет.
    /// </summary>
    public async Task<ServerSyncResult> SyncAsync(Guid serverId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var server = await db.HiddifyServers
            .Include(s => s.Bindings)
            .FirstOrDefaultAsync(s => s.Id == serverId, ct);
        if (server is null)
            return new ServerSyncResult(false, "Сервер не найден", [], 0);

        var apiResult = await hiddify.GetUsersAsync(server, ct);
        if (!apiResult.Success || apiResult.Value is null)
            return new ServerSyncResult(false, apiResult.Error ?? "Сервер недоступен", [], 0);

        var remoteByUuid = apiResult.Value
            .Where(u => u.Uuid is not null)
            .ToDictionary(u => u.Uuid!, StringComparer.OrdinalIgnoreCase);

        // Помечаем/снимаем broken на основе наличия UUID на сервере.
        var brokenCount = 0;
        foreach (var binding in server.Bindings)
        {
            var nowBroken = !remoteByUuid.ContainsKey(binding.HiddifyUuid);
            if (binding.IsBroken != nowBroken)
                binding.IsBroken = nowBroken;
            if (nowBroken)
                brokenCount++;
        }
        await db.SaveChangesAsync(ct);

        var boundUuids = server.Bindings.Select(b => b.HiddifyUuid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unbound = apiResult.Value
            .Where(u => u.Uuid is not null && !boundUuids.Contains(u.Uuid!))
            .Select(u => new UnboundHiddifyUser(u.Uuid!, u.Name ?? "(без имени)", u.Enable, u.CurrentUsageGb))
            .ToList();

        return new ServerSyncResult(true, null, unbound, brokenCount);
    }
}
