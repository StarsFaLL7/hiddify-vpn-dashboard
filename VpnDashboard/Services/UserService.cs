using Microsoft.EntityFrameworkCore;
using VpnDashboard.Data;
using VpnDashboard.Data.Entities;

namespace VpnDashboard.Services;

/// <summary>CRUD локальных пользователей и управление их токеном витрины.</summary>
public sealed class UserService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<List<LocalUser>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LocalUsers
            .Include(u => u.Bindings)
            .OrderBy(u => u.Name)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    /// <summary>Полная загрузка пользователя со всеми связями для диалога редактирования.</summary>
    public async Task<LocalUser?> GetWithRelationsAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.LocalUsers
            .Include(u => u.Bindings).ThenInclude(b => b.HiddifyServer)
            .Include(u => u.ManualSubscriptions)
            .Include(u => u.GlobalSubscriptions)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<LocalUser> CreateAsync(string name, string? comment, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = new LocalUser
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            ShowcaseToken = TokenGenerator.NewShowcaseToken()
        };
        db.LocalUsers.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateBasicAsync(Guid id, string name, string? comment, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.LocalUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return;

        user.Name = name.Trim();
        user.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.LocalUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return;

        db.LocalUsers.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Перевыпуск токена витрины — старая ссылка перестаёт работать.</summary>
    public async Task<string> RegenerateTokenAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.LocalUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return string.Empty;

        user.ShowcaseToken = TokenGenerator.NewShowcaseToken();
        await db.SaveChangesAsync(ct);
        return user.ShowcaseToken;
    }
}
