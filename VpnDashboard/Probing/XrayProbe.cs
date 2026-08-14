using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using VpnDashboard.Options;

namespace VpnDashboard.Probing;

/// <summary>
/// Настоящая проверка подключения: поднимает ядро Xray с конфигом записи и ходит через него наружу.
/// <para>
/// Записи провайдеров — это готовые конфиги целиком, поэтому конвертировать ничего не нужно:
/// подменяем инбаунд на локальный SOCKS со свободным портом и запускаем как есть. Вместе с
/// конфигом сохраняются роутинг, фрагментация и балансировщики провайдера — проверяется ровно то,
/// что получит пользователь.
/// </para>
/// <para>
/// Оговорка: собственный роутинг провайдера может увести проверочный запрос в direct, и тогда
/// успех означает лишь «интернет есть». Такие конфиги встречаются редко, а ошибка эта
/// безопасная — сервер останется в подписке.
/// </para>
/// </summary>
public sealed class XrayProbe(IOptions<ProbeOptions> options, ILogger<XrayProbe> logger)
{
    private readonly ProbeOptions _options = options.Value;
    private readonly SemaphoreSlim _availabilityGate = new(1, 1);
    private bool? _available;

    /// <summary>Есть ли рабочий бинарь. Проверяется один раз и запоминается.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        if (_available is { } known)
            return known;

        await _availabilityGate.WaitAsync(ct);
        try
        {
            _available ??= await DetectAsync(ct);
            return _available.Value;
        }
        finally
        {
            _availabilityGate.Release();
        }
    }

    /// <summary>
    /// Проверяет запись через ядро. Возвращает null, если ядро не удалось запустить, —
    /// это наша проблема, а не сервера, и вердикт выносить нельзя.
    /// </summary>
    public async Task<ProbeResult?> TryCheckAsync(ProbeTarget target, CancellationToken ct)
    {
        if (target.XrayConfig is null)
            return null;

        var port = FreePort();
        var configPath = Path.Combine(Path.GetTempPath(), $"vpndash-probe-{Guid.NewGuid():N}.json");

        Process? process = null;
        try
        {
            await File.WriteAllTextAsync(configPath, BuildConfig(target.XrayConfig, port).ToJsonString(), ct);

            process = Start(configPath);
            if (process is null)
                return null;

            // Вычитываем stderr сразу: ядро пишет туда ошибки конфига, и незабранный вывод
            // рано или поздно упрёт процесс в переполненный буфер.
            var diagnostics = process.StandardError.ReadToEndAsync(ct);

            if (!await WaitForPortAsync(port, process, ct))
            {
                logger.LogDebug(
                    "Ядро не подняло прокси для {Name}: {Error}",
                    target.DisplayName, await FirstLineAsync(diagnostics));
                return null;
            }

            return await RequestAsync(port, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug("Проверка {Name} через xray не состоялась: {Message}", target.DisplayName, ex.Message);
            return null;
        }
        finally
        {
            Stop(process);
            TryDelete(configPath);
        }
    }

    /// <summary>Конфиг записи с единственным локальным SOCKS-инбаундом на свободном порту.</summary>
    private static JsonObject BuildConfig(JsonObject entry, int port)
    {
        var config = entry.DeepClone().AsObject();

        config["log"] = new JsonObject { ["loglevel"] = "error" };
        config["inbounds"] = new JsonArray
        {
            new JsonObject
            {
                ["tag"] = "probe",
                ["listen"] = "127.0.0.1",
                ["port"] = port,
                ["protocol"] = "socks",
                ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = false }
            }
        };

        // Витринные поля ядру не нужны и в некоторых сборках вызывают предупреждения.
        config.Remove("remarks");
        config.Remove("stats");

        return config;
    }

    private async Task<ProbeResult> RequestAsync(int port, CancellationToken ct)
    {
        using var handler = new HttpClientHandler
        {
            Proxy = new WebProxy($"socks5://127.0.0.1:{port}"),
            UseProxy = true
        };
        using var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_options.XrayTimeoutSeconds)
        };

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var response = await http.GetAsync(_options.ProbeUrl, HttpCompletionOption.ResponseHeadersRead, ct);

            return response.IsSuccessStatusCode
                ? ProbeResult.Ok("xray", NetworkProbe.Elapsed(started))
                : ProbeResult.Failed("xray", $"HTTP {(int)response.StatusCode}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return ProbeResult.Failed("xray", $"таймаут {_options.XrayTimeoutSeconds} с");
        }
        catch (HttpRequestException ex)
        {
            return ProbeResult.Failed("xray", ex.Message);
        }
    }

    private Process? Start(string configPath)
    {
        var info = new ProcessStartInfo(_options.XrayPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("run");
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(configPath);

        try
        {
            return Process.Start(info);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Не удалось запустить {Path}: {Message}", _options.XrayPath, ex.Message);
            _available = false;
            return null;
        }
    }

    /// <summary>Ждём, пока ядро откроет локальный порт; упавший процесс ждать бессмысленно.</summary>
    private static async Task<bool> WaitForPortAsync(int port, Process process, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            if (process.HasExited)
                return false;

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port, ct);
                return true;
            }
            catch (SocketException)
            {
                await Task.Delay(100, ct);
            }
        }

        return false;
    }

    private async Task<bool> DetectAsync(CancellationToken ct)
    {
        if (!_options.XrayEnabled)
            return false;

        try
        {
            var info = new ProcessStartInfo(_options.XrayPath)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            info.ArgumentList.Add("version");

            using var process = Process.Start(info);
            if (process is null)
                return false;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));

            var version = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);

            logger.LogInformation("Ядро для проверки: {Version}", version.Split('\n')[0].Trim());
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Бинарь {Path} недоступен ({Message}); остаётся только проверка порта",
                _options.XrayPath, ex.Message);
            return false;
        }
    }

    /// <summary>Первая строка диагностики ядра; ждать её долго не имеет смысла.</summary>
    private static async Task<string> FirstLineAsync(Task<string> diagnostics)
    {
        var completed = await Task.WhenAny(diagnostics, Task.Delay(TimeSpan.FromSeconds(1)));
        if (completed != diagnostics)
            return "нет вывода";

        var text = (await diagnostics).Trim();
        return text.Length == 0 ? "нет вывода" : text.Split('\n')[0].Trim();
    }

    /// <summary>Свободный порт занимаем и сразу отпускаем — гонка здесь маловероятна и не страшна.</summary>
    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void Stop(Process? process)
    {
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception)
        {
            // Процесс мог завершиться сам — гасить нечего.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Временный файл не удалился — переживём, чистить его не наша забота.
        }
    }
}
