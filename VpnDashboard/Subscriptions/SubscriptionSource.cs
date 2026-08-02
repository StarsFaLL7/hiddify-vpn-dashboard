using VpnDashboard.Data.Entities;

namespace VpnDashboard.Subscriptions;

/// <summary>
/// Одна подписка провайдера, входящая в набор пользователя.
/// Общий вход и для витрины (показать ссылку), и для агрегатора (сходить за конфигами).
/// </summary>
/// <param name="Kind">Тип источника.</param>
/// <param name="Id">Id привязки / ручной подписки / общей подписки.</param>
/// <param name="Label">Подпись для витрины и диагностики.</param>
/// <param name="Prefix">Префикс имён серверов в агрегате.</param>
/// <param name="Url">Ссылка, которую видит пользователь.</param>
/// <param name="FetchUrl">Ссылка, с которой агрегатор забирает конфиги (у Hiddify отличается от <paramref name="Url"/>).</param>
/// <param name="UserAgent">User-Agent для запроса; null — дефолт из конфига.</param>
public sealed record SubscriptionSource(
    SubscriptionSourceKind Kind,
    Guid Id,
    string Label,
    string Prefix,
    string Url,
    string FetchUrl,
    string? UserAgent);

/// <summary>Набор подписок одного пользователя, разрешённый по токену витрины.</summary>
public sealed record UserSubscriptions(
    Guid UserId,
    string UserName,
    IReadOnlyList<SubscriptionSource> Sources);
