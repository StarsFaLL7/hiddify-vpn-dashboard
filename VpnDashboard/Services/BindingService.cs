using Microsoft.EntityFrameworkCore;
using VpnDashboard.Data;
using VpnDashboard.Data.Entities;
using VpnDashboard.Hiddify;
using VpnDashboard.Hiddify.Dtos;

namespace VpnDashboard.Services;

/// <summary>Управление привязками пользователя к серверам Hiddify.</summary>
public sealed class BindingService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHiddifyApiClient hiddify)
{
    /// <summary>Привязать к уже существующему на сервере Hiddify-юзеру по его UUID.</summary>
    public async Task<HiddifyResult<UserServerBinding>> BindExistingAsync(
        Guid localUserId, Guid serverId, string hiddifyUuid, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (await db.UserServerBindings.AnyAsync(
                b => b.LocalUserId == localUserId && b.HiddifyServerId == serverId, ct))
            return HiddifyResult<UserServerBinding>.Fail("Привязка к этому серверу уже существует");

        var binding = new UserServerBinding
        {
            Id = Guid.NewGuid(),
            LocalUserId = localUserId,
            HiddifyServerId = serverId,
            HiddifyUuid = hiddifyUuid.Trim(),
            IsEnabled = true
        };
        db.UserServerBindings.Add(binding);
        await db.SaveChangesAsync(ct);
        return HiddifyResult<UserServerBinding>.Ok(binding);
    }

    /// <summary>Создать нового Hiddify-юзера на сервере через API и привязать к нему.</summary>
    public async Task<HiddifyResult<UserServerBinding>> CreateViaApiAsync(
        Guid localUserId, Guid serverId, int packageDays, double usageLimitGb, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (await db.UserServerBindings.AnyAsync(
                b => b.LocalUserId == localUserId && b.HiddifyServerId == serverId, ct))
            return HiddifyResult<UserServerBinding>.Fail("Привязка к этому серверу уже существует");

        var user = await db.LocalUsers.FirstOrDefaultAsync(u => u.Id == localUserId, ct);
        var server = await db.HiddifyServers.FirstOrDefaultAsync(s => s.Id == serverId, ct);
        if (user is null || server is null)
            return HiddifyResult<UserServerBinding>.Fail("Пользователь или сервер не найден");

        var request = new CreateHiddifyUserRequest(user.Name, packageDays, usageLimitGb);
        var apiResult = await hiddify.CreateUserAsync(server, request, ct);
        if (!apiResult.Success || apiResult.Value?.Uuid is null)
            return HiddifyResult<UserServerBinding>.Fail(apiResult.Error ?? "Не удалось создать пользователя на сервере");

        var binding = new UserServerBinding
        {
            Id = Guid.NewGuid(),
            LocalUserId = localUserId,
            HiddifyServerId = serverId,
            HiddifyUuid = apiResult.Value.Uuid,
            IsEnabled = true
        };
        db.UserServerBindings.Add(binding);
        await db.SaveChangesAsync(ct);
        return HiddifyResult<UserServerBinding>.Ok(binding);
    }

    public async Task SetEnabledAsync(Guid bindingId, bool enabled, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var binding = await db.UserServerBindings.FirstOrDefaultAsync(b => b.Id == bindingId, ct);
        if (binding is null)
            return;

        binding.IsEnabled = enabled;
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid bindingId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var binding = await db.UserServerBindings.FirstOrDefaultAsync(b => b.Id == bindingId, ct);
        if (binding is null)
            return;

        db.UserServerBindings.Remove(binding);
        await db.SaveChangesAsync(ct);
    }
}
