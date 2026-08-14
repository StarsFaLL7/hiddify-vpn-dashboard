using Microsoft.Extensions.Options;
using VpnDashboard.Options;
using VpnDashboard.Services;

namespace VpnDashboard.BackgroundServices;

/// <summary>
/// Фоновый обход серверов из подписок: раз в N минут проверяет каждый и обновляет здоровье.
/// Нерабочие серверы после нескольких неудач подряд перестают попадать в подписку.
/// </summary>
public sealed class ServerHealthCheckService(
    IServiceScopeFactory scopeFactory,
    IOptions<ProbeOptions> options,
    ILogger<ServerHealthCheckService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        if (!opts.Enabled)
        {
            logger.LogInformation("Проверка серверов выключена конфигурацией");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(5, opts.IntervalMinutes));

        try
        {
            // Не проверяем сразу на старте: приложению надо подняться, а рестарт не должен
            // превращаться в мгновенный шквал подключений к провайдерам.
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, opts.StartupDelaySeconds)), stoppingToken);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var health = scope.ServiceProvider.GetRequiredService<ServerHealthService>();
                await health.RunAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Обход проверки завершился с ошибкой: {Message}", ex.Message);
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
}
