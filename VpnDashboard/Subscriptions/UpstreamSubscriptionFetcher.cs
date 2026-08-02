using System.Net;
using Microsoft.Extensions.Options;
using VpnDashboard.Options;

namespace VpnDashboard.Subscriptions;

/// <summary>Сырой ответ провайдера. Ошибки не бросаются наружу — недоступный провайдер не должен ронять выдачу.</summary>
public sealed record UpstreamResponse(
    bool Success,
    int? StatusCode,
    string? Body,
    SubscriptionUserInfo? UserInfo,
    string? Error);

/// <summary>
/// HTTP-клиент к подпискам провайдеров. Представляется одним и тем же клиентом с постоянным
/// синтетическим HWID, поэтому провайдер видит одно устройство на источник.
/// </summary>
public sealed class UpstreamSubscriptionFetcher(
    HttpClient http,
    SyntheticIdentity identity,
    IOptions<AggregationOptions> options,
    ILogger<UpstreamSubscriptionFetcher> logger)
{
    private readonly AggregationOptions _options = options.Value;

    public async Task<UpstreamResponse> FetchAsync(SubscriptionSource source, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source.FetchUrl);
            var hwid = identity.HwidFor(source.Kind, source.Id);

            request.Headers.TryAddWithoutValidation("User-Agent", source.UserAgent ?? _options.UserAgent);
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
            request.Headers.TryAddWithoutValidation("X-Hwid", hwid);
            // Часть панелей ждёт именно X-Device-ID (так его шлют android-клиенты).
            request.Headers.TryAddWithoutValidation("X-Device-ID", hwid);
            request.Headers.TryAddWithoutValidation("X-Device-Os", _options.DeviceOs);
            request.Headers.TryAddWithoutValidation("X-Ver-Os", _options.DeviceOsVersion);
            request.Headers.TryAddWithoutValidation("X-Device-Model", _options.DeviceModel);
            request.Headers.TryAddWithoutValidation("X-Device-Locale", _options.DeviceLocale);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(_options.FetchTimeoutSeconds));

            using var response = await http.SendAsync(request, cts.Token);
            var status = (int)response.StatusCode;

            logger.LogInformation("Подписка {Label} ({Kind}): HTTP {Status}", source.Label, source.Kind, status);

            if (!response.IsSuccessStatusCode)
                return new UpstreamResponse(false, status, null, null, DescribeStatus(response.StatusCode));

            var body = await response.Content.ReadAsStringAsync(cts.Token);

            var userInfo = SubscriptionUserInfo.Parse(
                response.Headers.TryGetValues("subscription-userinfo", out var values)
                    ? values.FirstOrDefault()
                    : null);

            return new UpstreamResponse(true, status, body, userInfo, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Подписка {Label}: таймаут {Seconds} c", source.Label, _options.FetchTimeoutSeconds);
            return new UpstreamResponse(false, null, null, null, $"Таймаут {_options.FetchTimeoutSeconds} c");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Подписка {Label}: ошибка запроса — {Message}", source.Label, ex.Message);
            return new UpstreamResponse(false, null, null, null, ex.Message);
        }
    }

    private static string DescribeStatus(HttpStatusCode code) => code switch
    {
        HttpStatusCode.NotFound => "HTTP 404 — ссылка недействительна",
        HttpStatusCode.Forbidden => "HTTP 403 — провайдер отклонил запрос (возможно, лимит устройств)",
        HttpStatusCode.Unauthorized => "HTTP 401 — требуется авторизация",
        _ => $"HTTP {(int)code}"
    };
}
