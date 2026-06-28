using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VpnDashboard.Data;
using VpnDashboard.Hiddify;
using VpnDashboard.Options;

namespace VpnDashboard.BackgroundServices;

/// <summary>
/// Фоновый пинг панелей Hiddify раз в N минут. Обновляет IsOnline/LastPingAt серверов.
/// Не блокирует UI и переживает таймауты/исключения.
/// </summary>
public sealed class ServerPingService(
    IServiceScopeFactory scopeFactory,
    IOptions<PingOptions> options,
    ILogger<ServerPingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Пинг-сервис выключен конфигурацией");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, opts.IntervalMinutes));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PingAllAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogWarning("Цикл пинга завершился с ошибкой: {Message}", ex.Message);
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task PingAllAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var hiddify = scope.ServiceProvider.GetRequiredService<IHiddifyApiClient>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var servers = await db.HiddifyServers.ToListAsync(ct);

        foreach (var server in servers)
        {
            server.IsOnline = await hiddify.PingAsync(server, ct);
            server.LastPingAt = DateTime.UtcNow;
        }

        if (servers.Count > 0)
            await db.SaveChangesAsync(ct);
    }
}
