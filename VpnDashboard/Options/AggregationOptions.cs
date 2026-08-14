namespace VpnDashboard.Options;

/// <summary>
/// Настройки агрегатора подписок: витрина отдаёт VPN-клиентам одну общую подписку,
/// собранную из всех подписок пользователя.
/// </summary>
public sealed class AggregationOptions
{
    public const string SectionName = "Aggregation";

    /// <summary>Включена ли агрегация. При false витрина ведёт себя как раньше.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Сколько минут считать снимок свежим. В пределах TTL наружу не ходим —
    /// это же защищает провайдеров от шквала запросов, когда у пользователя несколько устройств.
    /// </summary>
    public int CacheTtlMinutes { get; set; } = 10;

    /// <summary>Таймаут одного запроса к провайдеру, секунды.</summary>
    public int FetchTimeoutSeconds { get; set; } = 15;

    /// <summary>
    /// User-Agent по умолчанию. Happ выбран потому, что провайдеры отдают ему
    /// полный каталог в виде JSON-массива Xray-конфигов; часть из них другие клиенты блокирует.
    /// Переопределяется на каждом источнике.
    /// </summary>
    public string UserAgent { get; set; } = "Happ/3.13.0";

    /// <summary>Значение X-Device-Os, отправляемое провайдерам.</summary>
    public string DeviceOs { get; set; } = "Android";

    /// <summary>Значение X-Ver-Os, отправляемое провайдерам.</summary>
    public string DeviceOsVersion { get; set; } = "14";

    /// <summary>Значение X-Device-Model, отправляемое провайдерам.</summary>
    public string DeviceModel { get; set; } = "Pixel 7";

    /// <summary>Значение X-Device-Locale, отправляемое провайдерам.</summary>
    public string DeviceLocale { get; set; } = "ru_RU";

    /// <summary>
    /// Ключ для детерминированной генерации синтетического HWID.
    /// HWID выводится из идентификатора источника, поэтому провайдер видит одно устройство
    /// независимо от того, сколько реальных устройств у наших пользователей.
    /// Пусто — берётся соль пароля админки (стабильна, но лучше задать явно).
    /// </summary>
    public string HwidSecret { get; set; } = string.Empty;

    /// <summary>Заголовок profile-title в ответе. Пусто — имя пользователя. Клиенты режут до 25 символов.</summary>
    public string ProfileTitle { get; set; } = string.Empty;

    /// <summary>
    /// Приписка перед названием профиля — название сервиса. Разделитель задаётся здесь же,
    /// например «Prism-X | ». При нехватке лимита в 25 символов режется название, а не приписка.
    /// </summary>
    public string ProfileTitlePrefix { get; set; } = string.Empty;

    /// <summary>
    /// Разделитель перед пометкой варианта в profile-title, например «Prism-X | Иван · Прямые».
    /// Пометка задаётся вариантом и в лимит 25 символов доезжает целиком.
    /// </summary>
    public string VariantTitleSeparator { get; set; } = " · ";

    /// <summary>Заголовок profile-update-interval, часы.</summary>
    public int UpdateIntervalHours { get; set; } = 12;

    /// <summary>
    /// Как клиент сортирует список серверов: <c>none</c> — как отдали мы, <c>ping</c> — по задержке,
    /// <c>name</c> — по имени. При <c>ping</c> клиент простукивает все серверы и раскладывает их
    /// по-своему, так что порядок групп из <see cref="GroupInsertAfter"/> перестаёт действовать.
    /// Неизвестное значение — заголовок не отправляется.
    /// </summary>
    public string SortOrder { get; set; } = "none";

    /// <summary>
    /// На сколько дней вперёд объявлять срок подписки в subscription-userinfo.
    /// Сроки у провайдеров разные и продлеваются вручную, поэтому клиенту показываем «бессрочно».
    /// </summary>
    public int ExpireDays { get; set; } = 9999;

    /// <summary>Ссылка на поддержку в заголовке support-url. Пусто — заголовок не отправляется.</summary>
    public string SupportUrl { get; set; } = string.Empty;

    /// <summary>
    /// Подстроки в именах серверов, при совпадении с которыми запись выбрасывается.
    /// Провайдеры подмешивают в каталог псевдо-серверы вида «Осталось 29д.» или «Канал в TG»,
    /// и в агрегате из нескольких подписок они превращаются в мусор.
    /// Сравнение регистронезависимое.
    /// </summary>
    public List<string> ExcludeNameContains { get; set; } = [];

    /// <summary>
    /// Правила объединения однотипных серверов разных провайдеров в одну группу.
    /// Совпавшие записи получают общее имя с порядковым номером вместо провайдерского,
    /// и показываются в клиенте подряд, а не вперемешку по провайдерам.
    /// Порядок групп в списке — порядок объявления правил.
    /// <para>
    /// Значения по умолчанию здесь не задаются: список из конфигурации биндер не заменяет,
    /// а дописывает к дефолтному, и правила задвоились бы. Штатный набор — в appsettings.json.
    /// </para>
    /// </summary>
    public List<ConfigGroupRule> GroupRules { get; set; } = [];

    /// <summary>
    /// После скольких обычных серверов вставлять блок сгруппированных записей.
    /// 0 — в самое начало списка. Если обычных серверов меньше, группы идут после всех.
    /// </summary>
    public int GroupInsertAfter { get; set; } = 20;

    /// <summary>
    /// Сегмент пути дополнительной ссылки только на серверы вне групп: <c>/u/{token}/{DirectSlug}</c>.
    /// Пусто — такая ссылка не формируется.
    /// </summary>
    public string DirectSlug { get; set; } = "direct";

    /// <summary>Заголовок карточки прямых серверов на витрине.</summary>
    public string DirectTitle { get; set; } = "Только прямые серверы";

    /// <summary>Пометка прямых серверов в profile-title. Пусто — профиль без пометки.</summary>
    public string DirectShortName { get; set; } = "Прямые";

    /// <summary>
    /// Шаблоны deep-link-кнопок витрины: подпись — шаблон URL.
    /// {url} заменяется на адрес подписки, {urlenc} — на percent-encoded адрес.
    /// </summary>
    public Dictionary<string, string> DeepLinks { get; set; } = new()
    {
        ["INCY"] = "incy://import/{url}",
        ["Happ"] = "happ://add/{url}",
        ["Hiddify"] = "hiddify://import/{url}"
    };
}

/// <summary>
/// Правило группировки: записи, в имени которых встречается любая из подстрок,
/// переименовываются в «{Name} #N» и выводятся одним блоком.
/// </summary>
public sealed class ConfigGroupRule
{
    /// <summary>Базовое имя группы; к нему добавляется порядковый номер.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Подстроки имени, по которым запись относится к группе. Регистр не важен.</summary>
    public List<string> MatchContains { get; set; } = [];

    /// <summary>
    /// Сегмент пути отдельной ссылки только на серверы этой группы: <c>/u/{token}/{Slug}</c>.
    /// Пусто — отдельной ссылки и карточки на витрине у группы нет.
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Короткая пометка группы в profile-title. Пусто — берётся <see cref="Name"/>,
    /// который вместе с эмодзи обычно не влезает в лимит клиента в 25 символов.
    /// </summary>
    public string ShortName { get; set; } = string.Empty;

    public bool Matches(string name) =>
        MatchContains.Any(m => !string.IsNullOrWhiteSpace(m) &&
                               name.Contains(m, StringComparison.OrdinalIgnoreCase));
}
