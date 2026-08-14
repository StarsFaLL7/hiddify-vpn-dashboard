using Microsoft.Extensions.Options;
using VpnDashboard.Options;

namespace VpnDashboard.Probing;

/// <summary>
/// Проверка одного сервера: сначала пробуем настоящее подключение через ядро,
/// и только если его нет — довольствуемся проверкой порта.
/// </summary>
public sealed class ProbeService(
    NetworkProbe network,
    XrayProbe xray,
    IOptions<ProbeOptions> options,
    ILogger<ProbeService> logger)
{
    private readonly ProbeOptions _options = options.Value;

    public async Task<ProbeResult> CheckAsync(ProbeTarget target, CancellationToken ct)
    {
        if (!target.Supported)
            return ProbeResult.Unsupported($"протокол {target.Protocol} не проверяется");

        if (target.CanRunInXray && _options.XrayEnabled && await xray.IsAvailableAsync(ct))
        {
            if (await xray.TryCheckAsync(target, ct) is { } result)
                return result;

            // Ядро не поднялось — это наша проблема, а не сервера. Вердикт выносим
            // по доступности порта, чтобы сломанное ядро не выкосило всю подписку.
            logger.LogDebug("{Name}: ядро не запустилось, проверяем порт", target.DisplayName);
        }

        return await network.CheckAsync(target, ct);
    }
}
