using System.Text;
using System.Text.Json.Nodes;
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
/// <para>
/// Сегмент после токена выбирает вариант агрегата: /u/{token} — всё, /u/{token}/direct —
/// серверы вне групп, /u/{token}/{slug группы} — только её серверы.
/// </para>
/// </summary>
public sealed class AggregatedSubscriptionMiddleware(
    RequestDelegate next,
    IOptions<AggregationOptions> options,
    SubscriptionVariants variants)
{
    /// <summary>Клиенты обрезают profile-title до 25 символов.</summary>
    private const int ProfileTitleMaxLength = 25;

    /// <summary>Значения sort-order, которые понимают клиенты; прочее отправлять бессмысленно.</summary>
    private static readonly HashSet<string> SortOrders =
        new(["none", "ping", "name"], StringComparer.OrdinalIgnoreCase);

    private readonly AggregationOptions _options = options.Value;

    public async Task InvokeAsync(
        HttpContext context,
        ShowcaseService showcase,
        AggregationService aggregation,
        UserService users,
        ILogger<AggregatedSubscriptionMiddleware> logger)
    {
        if (!_options.Enabled || !TryGetRoute(context.Request, out var token, out var slug) ||
            IsBrowserRequest(context.Request))
        {
            await next(context);
            return;
        }

        if (!variants.TryResolve(slug, out var variant))
        {
            // Незнакомый сегмент — не наша ссылка; пусть с ней разбирается маршрутизация.
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

        var result = await aggregation.AggregateAsync(user, variant, ct: context.RequestAborted);

        if (!result.HasContent)
        {
            // Пустой ответ некоторые клиенты применяют и стирают уже настроенные серверы,
            // поэтому честнее вернуть ошибку — клиент оставит текущий профиль.
            logger.LogError(
                "Агрегация для {User} ({Variant}): ни одного конфига, отдаём 502",
                user.UserName, variant.Slug.Length == 0 ? "общая" : variant.Slug);
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("Не удалось получить подписки провайдеров", context.RequestAborted);
            return;
        }

        // Отметку ставим только на удачную выдачу — иначе в админке было бы видно
        // «подписка обновлена», хотя клиент не получил ничего.
        await users.MarkSubscriptionFetchedAsync(user.UserId, context.RequestAborted);

        WriteHeaders(context, user, variant, result);

        var (body, contentType, dropped) = Render(result.Configs);
        if (dropped > 0)
            logger.LogWarning(
                "Агрегация для {User}: {Dropped} ссылок не переводятся в Xray-конфиг и пропущены",
                user.UserName, dropped);

        context.Response.ContentType = contentType;
        await context.Response.WriteAsync(body, context.RequestAborted);
    }

    /// <summary>
    /// Тело ответа. Клиенты не разбирают JSON-массив, где конфиги-объекты перемешаны со
    /// ссылками-строками, поэтому записи приводятся к одному виду: одни ссылки уходят обычным
    /// base64-списком (так сохраняются и протоколы вне Xray), а рядом с конфигами ссылки
    /// становятся конфигами — непереводимые при этом теряются.
    /// </summary>
    private static (string Body, string ContentType, int Dropped) Render(JsonArray entries)
    {
        if (entries.All(e => e is not JsonObject))
        {
            var links = string.Join('\n', entries.Select(e => SubscriptionEntry.AsUri(e!)).OfType<string>());
            return (Convert.ToBase64String(Encoding.UTF8.GetBytes(links)), "text/plain; charset=utf-8", 0);
        }

        var configs = new JsonArray();
        var dropped = 0;

        foreach (var entry in entries)
        {
            if (entry is JsonObject)
                configs.Add(entry.DeepClone());
            else if (SubscriptionEntry.AsUri(entry!) is { } uri &&
                     LinkConfig.TryConvert(uri, ProxyUri.ReadName(uri)) is { } config)
                configs.Add(config);
            else
                dropped++;
        }

        return (configs.ToJsonString(), "application/json; charset=utf-8", dropped);
    }

    private void WriteHeaders(
        HttpContext context,
        UserSubscriptions user,
        SubscriptionVariant variant,
        AggregatedSubscription result)
    {
        var headers = context.Response.Headers;

        headers["X-Robots-Tag"] = "noindex, nofollow";
        headers["Cache-Control"] = "no-store";
        headers["subscription-userinfo"] = result.UserInfo.ToHeaderValue();
        headers["profile-update-interval"] = _options.UpdateIntervalHours.ToString();
        headers["profile-web-page-url"] = BuildShowcaseUrl(context.Request);
        if (SortOrders.Contains(_options.SortOrder))
            headers["sort-order"] = _options.SortOrder;

        var title = BuildProfileTitle(user, variant);
        headers["profile-title"] = "base64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(title));

        if (!string.IsNullOrWhiteSpace(_options.SupportUrl))
            headers["support-url"] = _options.SupportUrl;
    }

    /// <summary>
    /// Название профиля в клиенте: приписка сервиса, имя пользователя и пометка варианта.
    /// Лимит съедает длинные имена, поэтому режем именно название — приписка с брендом и пометка
    /// должны доезжать целиком, иначе профили в списке клиента не различить.
    /// </summary>
    private string BuildProfileTitle(UserSubscriptions user, SubscriptionVariant variant)
    {
        var name = string.IsNullOrWhiteSpace(_options.ProfileTitle) ? user.UserName : _options.ProfileTitle;
        var prefix = _options.ProfileTitlePrefix;
        var suffix = string.IsNullOrWhiteSpace(variant.ShortName)
            ? string.Empty
            : _options.VariantTitleSeparator + variant.ShortName;

        var room = ProfileTitleMaxLength - prefix.Length - suffix.Length;
        return room > 0
            ? prefix + Truncate(name, room) + suffix
            : Truncate(prefix + suffix, ProfileTitleMaxLength);
    }

    /// <summary>
    /// Путь витрины — /u/{token}[/{вариант}]; сегменты за вариантом игнорируем.
    /// </summary>
    private static bool TryGetRoute(HttpRequest request, out string token, out string slug)
    {
        token = string.Empty;
        slug = string.Empty;

        var path = request.Path.Value;
        if (path is null || !path.StartsWith("/u/", StringComparison.OrdinalIgnoreCase))
            return false;

        var rest = path[3..].Trim('/');
        if (rest.Length == 0)
            return false;

        var segments = rest.Split('/');
        token = Uri.UnescapeDataString(segments[0]);
        if (segments.Length > 1)
            slug = Uri.UnescapeDataString(segments[1]);

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
