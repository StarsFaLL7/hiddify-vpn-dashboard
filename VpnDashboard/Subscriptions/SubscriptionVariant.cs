using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using VpnDashboard.Options;

namespace VpnDashboard.Subscriptions;

/// <summary>Какие записи агрегата попадают в подписку.</summary>
public enum SubscriptionScope
{
    /// <summary>Всё: серверы вне групп плюс блок сгруппированных.</summary>
    All,

    /// <summary>Только серверы, не попавшие ни под одно правило группировки.</summary>
    Ungrouped,

    /// <summary>Только записи одной группы.</summary>
    Group
}

/// <summary>Вариант агрегата — одна ссылка пользователя.</summary>
/// <param name="Slug">Сегмент пути после токена; пусто — основная ссылка /u/{token}.</param>
/// <param name="Title">Заголовок карточки на витрине.</param>
/// <param name="ShortName">Пометка варианта в profile-title; пусто — без пометки.</param>
/// <param name="Scope">Какие записи попадают в подписку.</param>
/// <param name="GroupIndex">Индекс правила в GroupRules — только для <see cref="SubscriptionScope.Group"/>.</param>
public sealed record SubscriptionVariant(
    string Slug,
    string Title,
    string ShortName,
    SubscriptionScope Scope,
    int GroupIndex = -1);

/// <summary>
/// Набор вариантов подписки, собранный из настроек агрегатора.
/// <para>
/// Кроме общей ссылки пользователю выдаются ссылка на серверы вне групп и по ссылке на каждую
/// группу, у которой задан <see cref="ConfigGroupRule.Slug"/>. Новое правило со слагом добавляет
/// и ссылку, и карточку на витрине без правок кода.
/// </para>
/// </summary>
public sealed class SubscriptionVariants
{
    private readonly Dictionary<string, SubscriptionVariant> _bySlug =
        new(StringComparer.OrdinalIgnoreCase);

    public SubscriptionVariants(IOptions<AggregationOptions> options)
    {
        var config = options.Value;
        var extra = new List<SubscriptionVariant>();

        if (Slug(config.DirectSlug) is { } directSlug)
            extra.Add(new SubscriptionVariant(
                directSlug,
                config.DirectTitle,
                config.DirectShortName,
                SubscriptionScope.Ungrouped));

        for (var i = 0; i < config.GroupRules.Count; i++)
        {
            var rule = config.GroupRules[i];
            if (Slug(rule.Slug) is not { } groupSlug)
                continue;

            extra.Add(new SubscriptionVariant(
                groupSlug,
                rule.Name,
                string.IsNullOrWhiteSpace(rule.ShortName) ? rule.Name : rule.ShortName,
                SubscriptionScope.Group,
                i));
        }

        // Повторяющийся слаг в конфигурации — оставляем первый: отдавать по одной ссылке
        // то одно, то другое хуже, чем игнорировать дубль.
        var unique = new List<SubscriptionVariant>(extra.Count);
        foreach (var variant in extra)
        {
            if (_bySlug.TryAdd(variant.Slug, variant))
                unique.Add(variant);
        }

        Extra = unique;
    }

    /// <summary>Основная ссылка /u/{token} — весь агрегат, как и раньше.</summary>
    public SubscriptionVariant Full { get; } =
        new(string.Empty, string.Empty, string.Empty, SubscriptionScope.All);

    /// <summary>Дополнительные ссылки в порядке показа: сначала прямые серверы, затем группы.</summary>
    public IReadOnlyList<SubscriptionVariant> Extra { get; }

    /// <summary>Вариант по сегменту пути; пустой сегмент — основная ссылка.</summary>
    public bool TryResolve(string? slug, [NotNullWhen(true)] out SubscriptionVariant? variant)
    {
        if (string.IsNullOrEmpty(slug))
        {
            variant = Full;
            return true;
        }

        return _bySlug.TryGetValue(slug, out variant);
    }

    private static string? Slug(string? value)
    {
        var slug = value?.Trim().Trim('/');
        return string.IsNullOrEmpty(slug) ? null : slug;
    }
}
