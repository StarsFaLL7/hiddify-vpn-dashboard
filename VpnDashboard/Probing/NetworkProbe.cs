using System.Diagnostics;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using Microsoft.Extensions.Options;
using VpnDashboard.Options;

namespace VpnDashboard.Probing;

/// <summary>
/// Дешёвая проверка: жив ли порт сервера и говорит ли он TLS.
/// <para>
/// Ловит мёртвый хост, закрытый порт и отвалившийся домен — то есть большую часть реальных
/// поломок. Успех при этом ничего не гарантирует: у reality сервер, не узнавший «своего»
/// клиента, просто проксирует хендшейк на настоящий сайт-прикрытие и отвечает его сертификатом.
/// Поэтому положительный вердикт отсюда — это «сервер жив», а не «подключение работает».
/// </para>
/// </summary>
public sealed class NetworkProbe(IOptions<ProbeOptions> options)
{
    private readonly ProbeOptions _options = options.Value;

    public async Task<ProbeResult> CheckAsync(ProbeTarget target, CancellationToken ct)
    {
        var wantsTls = target.Security is "tls" or "reality";
        var method = wantsTls ? "tls" : "tcp";

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.ConnectTimeoutSeconds));

        var started = Stopwatch.GetTimestamp();

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(target.Address, target.Port, timeout.Token);

            if (wantsTls)
            {
                await using var ssl = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = string.IsNullOrWhiteSpace(target.Sni) ? target.Address : target.Sni,
                    // Сертификат не проверяем: у reality он принадлежит чужому сайту-прикрытию,
                    // а нас интересует лишь то, что на порту кто-то живой.
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                }, timeout.Token);
            }

            return ProbeResult.Ok(method, Elapsed(started));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProbeResult.Failed(method, $"таймаут {_options.ConnectTimeoutSeconds} с");
        }
        catch (SocketException ex)
        {
            return ProbeResult.Failed(method, ex.SocketErrorCode.ToString());
        }
        catch (Exception ex) when (ex is AuthenticationException or IOException)
        {
            return ProbeResult.Failed(method, ex.Message);
        }
    }

    internal static int Elapsed(long startedAt) =>
        (int)Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;
}
