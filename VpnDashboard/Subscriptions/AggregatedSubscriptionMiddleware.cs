using System.Text;
using Microsoft.Extensions.Options;
using VpnDashboard.Options;
using VpnDashboard.Services;

namespace VpnDashboard.Subscriptions;

/// <summary>
/// Отдаёт агрегированную подписку по той же ссылке витрины, что человек открывает в браузере.
/// <para>
/// Встроен в конвейер до маршрутизации: путь /u/{token} уже занят страницей витрины,
/// и отдельный endpoint конфликтовал бы с её маршрутом. Браузерные запросы пропускаются дальше,
/// на страницу; запросы VPN-клиентов обрабатываются здесь.
/// </para>
/// </summary>
public sealed class AggregatedSubscriptionMiddleware(RequestDelegate next, IOptions<AggregationOptions> options)
{
    /// <summary>Клиенты обрезают profile-title до 25 символов.</summary>
    private const int ProfileTitleMaxLength = 25;

    private readonly AggregationOptions _options = options.Value;

    public async Task InvokeAsync(
        HttpContext context,
        ShowcaseService showcase,
        AggregationService aggregation,
        UserService users,
        ILogger<AggregatedSubscriptionMiddleware> logger)
    {
        if (!_options.Enabled || !TryGetToken(context.Request, out var token) || IsBrowserRequest(context.Request))
        {
            await next(context);
            return;
        }

        var user = await showcase.GetByTokenAsync(token, context.RequestAborted);
        if (user is null)
        {
            // Неизвестный токен — пусть страница витрины отдаст свой нейтральный 404.
            await next(context);
            return;
        }

        var result = await aggregation.AggregateAsync(user, ct: context.RequestAborted);

        if (!result.HasContent)
        {
            // Пустой ответ некоторые клиенты применяют и стирают уже настроенные серверы,
            // поэтому честнее вернуть ошибку — клиент оставит текущий профиль.
            logger.LogError("Агрегация для {User}: ни одного конфига, отдаём 502", user.UserName);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("Не удалось получить подписки провайдеров", context.RequestAborted);
            return;
        }

        // Отметку ставим только на удачную выдачу — иначе в админке было бы видно
        // «подписка обновлена», хотя клиент не получил ничего.
        await users.MarkSubscriptionFetchedAsync(user.UserId, context.RequestAborted);

        WriteHeaders(context, user, result);
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsync(result.Configs.ToJsonString(), context.RequestAborted);
    }

    private void WriteHeaders(HttpContext context, UserSubscriptions user, AggregatedSubscription result)
    {
        var headers = context.Response.Headers;

        headers["X-Robots-Tag"] = "noindex, nofollow";
        headers["Cache-Control"] = "no-store";
        headers["subscription-userinfo"] = result.UserInfo.ToHeaderValue();
        headers["profile-update-interval"] = _options.UpdateIntervalHours.ToString();
        headers["profile-web-page-url"] = BuildShowcaseUrl(context.Request);
        // Порядок задаём мы — записи сгруппированы по провайдерам, клиенту пересортировывать не нужно.
        headers["sort-order"] = "none";

        var title = BuildProfileTitle(user);
        headers["profile-title"] = "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(title));

        if (!string.IsNullOrWhiteSpace(_options.SupportUrl))
            headers["support-url"] = _options.SupportUrl;
    }

    /// <summary>
    /// Название профиля в клиенте: приписка сервиса плюс имя пользователя.
    /// Лимит съедает длинные имена, поэтому режем именно название — приписка с брендом
    /// должна доезжать целиком, иначе в списке профилей её не узнать.
    /// </summary>
    private string BuildProfileTitle(UserSubscriptions user)
    {
        var name = string.IsNullOrWhiteSpace(_options.ProfileTitle) ? user.UserName : _options.ProfileTitle;
        var prefix = _options.ProfileTitlePrefix;

        if (string.IsNullOrEmpty(prefix))
            return Truncate(name, ProfileTitleMaxLength);

        var room = ProfileTitleMaxLength - prefix.Length;
        return room > 0
            ? prefix + Truncate(name, room)
            : Truncate(prefix, ProfileTitleMaxLength);
    }

    /// <summary>Путь витрины — /u/{token}; сегменты за ним игнорируем.</summary>
    private static bool TryGetToken(HttpRequest request, out string token)
    {
        token = string.Empty;
        var path = request.Path.Value;
        if (path is null || !path.StartsWith("/u/", StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = path[3..].Trim('/');
        if (rest.Length == 0)
            return false;

        var slash = rest.IndexOf('/');
        token = Uri.UnescapeDataString(slash < 0 ? rest : rest[..slash]);
        return token.Length > 0;
    }

    /// <summary>
    /// Браузер отличаем по связке «UA от Mozilla» + «просит HTML»: VPN-клиенты либо шлют
    /// собственный UA (Happ, INCY, v2rayNG), либо не шлют его вовсе.
    /// Параметры ?page=1 и ?sub=1 позволяют посмотреть любую из версий вручную.
    /// </summary>
    private static bool IsBrowserRequest(HttpRequest request)
    {
        if (request.Query.ContainsKey("sub"))
            return false;
        if (request.Query.ContainsKey("page"))
            return true;

        var userAgent = request.Headers.UserAgent.ToString();
        if (string.IsNullOrEmpty(userAgent))
            return false;

        return userAgent.StartsWith("Mozilla/", StringComparison.OrdinalIgnoreCase) &&
               request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildShowcaseUrl(HttpRequest request) =>
        $"{request.Scheme}://{request.Host}{request.Path}";

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
