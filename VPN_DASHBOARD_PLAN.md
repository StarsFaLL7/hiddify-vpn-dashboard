# VPN Subscription Dashboard — План разработки

> Документ-задание для Claude Code. Описывает что строим, какие приняты решения,
> и в каком порядке реализовывать. Решения уже приняты владельцем — **не менять
> архитектуру без явного запроса**. Где встречается «РЕШЕНИЕ:» — это финальный
> выбор, а не предложение к обсуждению.

---

## 1. Что это и зачем

Самостоятельный веб-сервис из двух частей:

1. **Админ-панель** (для одного администратора) — управление пользователями, серверами
   Hiddify, привязками и подписками.
2. **Витрина подписок** (для конечных пользователей) — статическая по смыслу страница,
   где человек по своей персональной ссылке видит готовый набор subscription-ссылок
   (на серверы Hiddify, добавленные вручную, и общие) с кнопкой копирования и QR-кодом.

**Проблема, которую решаем:** при миграции / добавлении / падении VPN-сервера не нужно
вручную пересылать новые ссылки каждому близкому человеку. Админ меняет данные в одном
месте — пользователь просто перезаходит на свою витрину и видит актуальные ссылки.

**Масштаб:** один администратор, ~10–30 конечных пользователей, 1–3 сервера Hiddify.
Это не highload. Простота поддержки приоритетнее производительности и «энтерпрайзности».

---

## 2. Стек (РЕШЕНИЕ — не менять)

- **.NET 8*10, **Blazor Server** (InteractiveServer render mode) для админ-панели.
- **MudBlazor** для UI админки.
- **Витрина** — отдельные легковесные страницы. Может быть Razor Component или
  отдельный minimal-API endpoint, отдающий HTML. Витрина НЕ должна тянуть тяжёлый
  Blazor-circuit ради статичного контента: она должна открываться быстро и работать,
  даже если SignalR-цепь недоступна. РЕШЕНИЕ: витрину рендерить как статический
  HTML (Razor Page / minimal API + Razor template или просто строковый шаблон),
  без интерактивного Blazor-circuit. QR генерировать на клиенте JS-библиотекой.
- **EF Core + SQLite** как хранилище.
- **Docker** — финальный артефакт деплоя (контейнер рядом с Hiddify).

---

## 3. Ключевые архитектурные решения

### 3.1. Источник правды — гибрид
- Локальная БД хранит пользователей, серверы, привязки, ручные и общие подписки.
- Hiddify-панели остаются источником правды по самим VPN-конфигам (subscription-контент).
- Наш сервис НЕ дублирует и НЕ хранит конфиги/ключи — только метаданные и UUID-привязки.

### 3.2. Юзеры и привязки создаются независимо (РЕШЕНИЕ)
- Пользователь сначала создаётся локально (имя + автогенерируемый токен витрины).
- Привязка к серверу Hiddify — отдельное действие, двумя путями:
  - **Привязать к существующему** Hiddify-юзеру: админ вводит/выбирает существующий
    UUID на этом сервере.
  - **Создать нового** Hiddify-юзера на выбранном сервере через API — сервис вызывает
    `POST /api/v2/admin/user/`, получает UUID, сохраняет привязку.
- Один локальный пользователь имеет **РАЗНЫЕ UUID на разных серверах**. Привязка хранит
  пару `(ServerId, HiddifyUuid)`. НЕ предполагать единый UUID на всех серверах.

### 3.3. Что НЕ храним из API Hiddify (РЕШЕНИЕ — критично для безопасности)
Ответ `GET /api/v2/admin/user` содержит приватные ключи:
`ed25519_private_key`, `wg_pk`, `wg_psk`. **Их НЕ сохранять в нашу БД никогда.**
Из ответа берём только: `uuid`, `name`, и при необходимости `enable`, `is_active`,
`current_usage_GB`, `usage_limit_GB`, `last_online` (для отображения статистики).
DTO для десериализации может содержать все поля, но в нашу сущность маппим только
безопасное подмножество.

### 3.4. Поведение при рассинхроне (РЕШЕНИЕ — дефолты)
- **Юзер есть в Hiddify, но нет локальной привязки** → не импортировать автоматически.
  В UI сервера показать секцию «непривязанные Hiddify-юзеры» с возможностью привязать
  вручную. Никаких авто-действий.
- **Локальная привязка есть, но UUID в Hiddify уже отсутствует** (нода переустановлена) →
  пометить привязку как «broken» (флаг + цвет в таблице), не удалять автоматически.
  Админ решает: перепривязать или удалить.
- Синхронизация — только по явной кнопке «Обновить с сервера», не фоном без спроса
  (кроме пинг-статуса, см. 3.6).

### 3.5. Формат subscription-ссылок на витрине (РЕШЕНИЕ)
Для каждого привязанного сервера витрина показывает ОДНУ универсальную ссылку:
```
https://{server_domain}/{client_proxy_path}/{user_uuid}/
```
(`client_proxy_path` — это «Proxy Path for Clients», настраивается per-server в справочнике
серверов; отличается от admin proxy path). Дополнительные форматы (`/sub/auto`, `/clash/`,
`/singbox/`) — опционально на будущее, в MVP не делать. Плюс ручные ссылки и общие ссылки
как есть. Для каждой ссылки на витрине: подпись (label), текст ссылки, кнопка «Копировать»,
QR-код.

### 3.6. Пинг-статус (включён в MVP)
- Фоновый `IHostedService` раз в N минут (конфигурируемо, дефолт 5 мин) пингует каждый
  сервер через `GET /api/v2/panel/ping/`.
- Результат (online/offline + время последней проверки) хранится в памяти/БД и
  показывается индикатором в таблице серверов и опционально рядом с привязками юзеров.
- Пинг НЕ должен блокировать UI и должен gracefully переживать таймауты/исключения.

---

## 4. Безопасность (РЕШЕНИЯ — обязательны)

### 4.1. Доступ к админ-панели
- Админка находится по пути с двумя GUID-сегментами: `/{guid1}/{guid2}/...`
  (security-by-obscurity, ПЕРВЫЙ барьер, НЕ единственный).
- Поверх пути — **обязательная аутентификация**: cookie-based auth с одним
  администраторским паролем. Пароль хранить как соль+хеш (PBKDF2 или `PasswordHasher<T>`), НЕ в открытом виде.
- Cookie: `HttpOnly`, `Secure`, `SameSite=Lax` (не Strict — чтобы не ломать переходы).
- Без аутентификации любой запрос к админ-путям → редирект на страницу логина.

### 4.2. Секреты Hiddify
- Admin UUID (Hiddify-API-Key) и proxy-paths серверов хранятся в БД (это справочник
  серверов), НО НЕ попадают в логи, не отдаются на фронт в открытом виде в составе
  API-ответов витрины.
- Конфигурационные секреты приложения (пароль админки seed, ключи) — через
  переменные окружения / user-secrets, НЕ в репозитории, НЕ в appsettings.json в гите.
- В `.gitignore`: `appsettings.*.json` с секретами, `*.db`, `.env`.

### 4.3. Витрина
- Персональный токен юзера в URL витрины — криптослучайный, ≥32 байта,
  `RandomNumberGenerator`, base64url-encoded. НЕ человекочитаемый (не имя).
- Кнопка «перевыпустить токен» в админке (старая ссылка витрины умирает).
- Заголовки витрины: `X-Robots-Tag: noindex, nofollow`.
- Доступ к витрине по токену не требует логина (это и есть пользовательский доступ),
  но токен = доступ к VPN-ссылкам, поэтому относиться к нему как к секрету.

---

## 5. Модель данных (EF Core, SQLite)

> Это ориентир, не догма по именам полей. Структуру связей сохранить. Все PK = GUID

### `LocalUser`
- `Id` (PK)
- `Name` (string)
- `ShowcaseToken` (string, unique, crypto-random) — токен в URL витрины
- `Comment` (string, nullable)
- `CreatedAt`, `UpdatedAt` (timestamps)

### `HiddifyServer` (справочник серверов)
- `Id` (PK)
- `Name` (string) — человекочитаемое имя («NL-01»)
- `Domain` (string) — домен сервера
- `AdminProxyPath` (string) — Proxy Path for Admins (для API)
- `ClientProxyPath` (string) — Proxy Path for Clients (для sub-ссылок)
- `AdminUuid` (string) — Hiddify-API-Key
- `IsOnline` (bool, nullable), `LastPingAt` (datetime, nullable) — для пинг-статуса

### `UserServerBinding`
- `Id` (PK)
- `LocalUserId` (FK → LocalUser)
- `HiddifyServerId` (FK → HiddifyServer)
- `HiddifyUuid` (string) — UUID этого юзера НА ЭТОМ сервере
- `IsEnabled` (bool) — показывать ли эту подписку на витрине
- `IsBroken` (bool) — UUID не найден на сервере при последней синхронизации
- Уникальность: `(LocalUserId, HiddifyServerId)`

### `ManualSubscription` (ручные ссылки конкретного юзера)
- `Id` (PK)
- `LocalUserId` (FK → LocalUser)
- `Label` (string)
- `Url` (string)
- `IsEnabled` (bool)

### `GlobalSubscription` (общие ссылки)
- `Id` (PK)
- `Label` (string)
- `Url` (string)
- `IsEnabled` (bool) — глобальный вкл/выкл

### `UserGlobalSubscription` (many-to-many: каким юзерам включена общая ссылка)
- `LocalUserId` (FK)
- `GlobalSubscriptionId` (FK)
- composite PK `(LocalUserId, GlobalSubscriptionId)`

---

## 6. Hiddify API — клиент

Реализовать типизированный `HiddifyApiClient` (`HttpClient` + DI, `IHttpClientFactory`).

Базовый URL: `https://{Domain}/{AdminProxyPath}/api/v2/`
Заголовок на все запросы: `Hiddify-API-Key: {AdminUuid}`, `Accept: application/json`.

Методы:
- `Task<bool> PingAsync(server)` → `GET panel/ping/`
- `Task<List<HiddifyUserDto>> GetUsersAsync(server)` → `GET admin/user`
- `Task<HiddifyUserDto> CreateUserAsync(server, name, packageDays, usageLimitGb, mode)`
  → `POST admin/user/` (тело — см. схему API; не нужные поля = null/дефолт,
  uuid=null чтобы сервер сгенерил сам)
- `Task<HiddifyUserDto> UpdateUserAsync(server, uuid, patch)` → `PATCH admin/user/{uuid}/`
- `Task<bool> DeleteUserAsync(server, uuid)` → `DELETE admin/user/{uuid}/`

Требования:
- Таймауты на запросы (5–10 сек), graceful обработка исключений (сервер недоступен —
  не валить UI, возвращать понятную ошибку/статус).
- DTO маппит весь ответ, но в доменные сущности — только безопасное подмножество (см. 3.3).
- Логировать факт вызова и статус-код, НЕ логировать ключи/приватные поля.

---

## 7. Экраны админ-панели (MudBlazor)

1. **Логин** — поле пароля, cookie-auth.
2. **Пользователи** (главный экран) — `MudTable` со списком `LocalUser`:
   - колонки: имя, кол-во активных привязок, статус, дата создания;
   - действия в строке: «Копировать ссылку витрины», «Редактировать», «Удалить».
   - Редактирование — `MudDialog` (попап): имя, комментарий; управление привязками
     к серверам (добавить: к существующему UUID / создать нового через API; вкл/выкл;
     отвязать); управление ручными ссылками (CRUD); чекбоксы общих подписок;
     кнопка «перевыпустить токен витрины».
3. **Серверы** — `MudTable` справочника `HiddifyServer`:
   - CRUD сервера (домен, оба proxy-path, admin uuid);
   - индикатор пинг-статуса;
   - кнопка «Обновить с сервера» (подтянуть список Hiddify-юзеров, показать непривязанных,
     пометить broken-привязки);
   - секция «непривязанные Hiddify-юзеры» с кнопкой привязки.
4. **Общие подписки** — `MudTable` `GlobalSubscription`: CRUD, глобальный вкл/выкл,
   быстрый выбор «кому включено».

---

## 8. Витрина (`/u/{token}` или аналог)

- Резолв токена → `LocalUser`. Не найден → 404 (нейтральный, без подсказок).
- Собрать список ссылок:
  - по каждой `UserServerBinding` где `IsEnabled && !IsBroken` → собрать sub-URL (см. 3.5);
  - все `ManualSubscription` где `IsEnabled`;
  - все `GlobalSubscription` где глобально `IsEnabled` И есть `UserGlobalSubscription`.
- Рендер: имя пользователя, карточки ссылок (label, ссылка, кнопка «Копировать», QR).
- Короткая инструкция «как добавить в клиент» (Hiddify app / v2rayN) + по возможности
  deep-link кнопка.
- Заголовок `X-Robots-Tag: noindex, nofollow`.
- Никакого тяжёлого Blazor-circuit; страница работает как статика.

---

## 9. Деплой (РЕШЕНИЕ: Docker рядом с Hiddify)

- `Dockerfile` (multi-stage: sdk build → aspnet runtime). Внимательно: имя build-stage
  должно совпадать в `--from=` (частая ошибка).
- Контейнер слушает на отдельном порту (например `8443` или внутренний `8080` +
  внешний маппинг), НЕ конфликтует с портами Hiddify (80/443 заняты её nginx).
- SQLite-файл — на смонтированном volume (для персистентности и бэкапа),
  путь вынести в конфиг.
- `docker-compose.yml` для запуска: сервис + volume + env-файл с секретами.
- НЕ вмешиваться в nginx-конфиг Hiddify (он его перезаписывает при апдейтах).
  Доступ к нашему сервису — напрямую по порту либо через отдельный reverse-proxy,
  который мы контролируем (не трогая Hiddify).
- README с инструкцией: env-переменные, как задать пароль админки, как примонтировать
  volume, как добавить первый сервер.

---

## 10. Порядок реализации (для Claude Code)

1. Каркас проекта: Blazor Server + MudBlazor + EF Core + SQLite, DI, конфиг.
2. Модель данных + миграция + `DbContext`.
3. Cookie-auth + страница логина + защита админ-путей с двумя GUID-сегментами.
4. CRUD серверов (`HiddifyServer`) + `HiddifyApiClient` (ping первым — проверить связь).
5. CRUD пользователей + управление привязками (existing UUID / create via API).
6. Ручные подписки + общие подписки + many-to-many назначение.
7. Витрина `/u/{token}`: сборка ссылок, копирование, QR, noindex.
8. Пинг-статус `IHostedService` + индикаторы в UI.
9. Синхронизация с сервером (кнопка): непривязанные + broken-привязки.
10. Dockerfile + docker-compose + README.

После каждого крупного шага — проверить, что собирается и запускается, прежде чем идти дальше.

---

## 11. Формат API

### Получить список пользователей:
GET http://www.vogellinder.space/FCwgZL262MN7OALtC9RxdezLgE0/api/v2/admin/user

Response:
```json
[
    {
        "added_by_uuid": "f9e39ba3-84b3-4575-86a6-f718d043f65a",
        "comment": "",
        "current_usage_GB": 0.0,
        "ed25519_private_key": "-----BEGIN OPENSSH PRIVATE KEY-----\nb3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQAAAAAAAAABAAAAMwAAAAtzc2gtZWQyNTUx\nOQAAACDQmupwySuMp6g6lrOlPlG/01oviGZDd0WoXt4NWeObWgAAAIiqkXp0qpF6dAAAAAtzc2gt\nZWQyNTUxOQAAACDQmupwySuMp6g6lrOlPlG/01oviGZDd0WoXt4NWeObWgAAAEBnBlkDHHhJkBZP\nrB8gd/xL3KfTDAgtvguBjou2ZSmlB9Ca6nDJK4ynqDqWs6U+Ub/TWi+IZkN3Rahe3g1Z45taAAAA\nAAECAwQF\n-----END OPENSSH PRIVATE KEY-----\n",
        "ed25519_public_key": "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAINCa6nDJK4ynqDqWs6U+Ub/TWi+IZkN3Rahe3g1Z45ta",
        "enable": true,
        "id": 1,
        "is_active": true,
        "lang": "ru",
        "last_online": "0001-01-01 00:00:00",
        "last_reset_time": "2026-04-15 00:00:00",
        "mode": "no_reset",
        "name": "default",
        "package_days": 3650,
        "start_date": null,
        "telegram_id": null,
        "usage_limit_GB": 3000.0,
        "uuid": "e65451e2-0f8d-4de4-b07f-a2658c948704",
        "wg_pk": "iEyAWWgnlp1nM3G/fhqZ7PMFpZPpYoyhPkZo5hhAbWQ=",
        "wg_psk": "pQ8Oa57vk6PQA2camnPTal2ORJraW8qBdh1CFgBfDws=",
        "wg_pub": "FkrnKytrlnl/9MuBTXr1ZZvK+ZkxUI84tpJo1Ke7jwM="
    }
]
```
### Создать пользователя:
POST /{proxy_path}/api/v2/admin/user/
Body:

```json
{
  "added_by_uuid": null,
  "comment": null,
  "current_usage_GB": 0,
  "ed25519_private_key": "string",
  "ed25519_public_key": "string",
  "enable": true,
  "is_active": true,
  "lang": "en",
  "last_online": null,
  "last_reset_time": null,
  "mode": "no_reset",
  "name": "string",
  "package_days": 0,
  "start_date": "2019-08-24",
  "telegram_id": 0,
  "usage_limit_GB": 0,
  "uuid": null,
  "wg_pk": "string",
  "wg_psk": "string",
  "wg_pub": "string"
}
```
Response:
```json
{
  "added_by_uuid": null,
  "comment": null,
  "current_usage_GB": 0,
  "ed25519_private_key": "string",
  "ed25519_public_key": "string",
  "enable": true,
  "id": 0,
  "is_active": true,
  "lang": "en",
  "last_online": null,
  "last_reset_time": null,
  "mode": "no_reset",
  "name": "string",
  "package_days": 0,
  "start_date": "2019-08-24",
  "telegram_id": 0,
  "usage_limit_GB": 0,
  "uuid": null,
  "wg_pk": "string",
  "wg_psk": "string",
  "wg_pub": "string"
}
```
### Обновить пользователя:
PATCH /{proxy_path}/api/v2/admin/user/{uuid}/
Body:

```json
{
  "added_by_uuid": null,
  "comment": null,
  "current_usage_GB": 0,
  "ed25519_private_key": "string",
  "ed25519_public_key": "string",
  "enable": true,
  "is_active": true,
  "lang": "en",
  "last_online": null,
  "last_reset_time": null,
  "mode": "no_reset",
  "name": "string",
  "package_days": 0,
  "start_date": "2019-08-24",
  "telegram_id": 0,
  "usage_limit_GB": 0,
  "uuid": null,
  "wg_pk": "string",
  "wg_psk": "string",
  "wg_pub": "string"
}
```
Response:
```json
{
  "added_by_uuid": null,
  "comment": null,
  "current_usage_GB": 0,
  "ed25519_private_key": "string",
  "ed25519_public_key": "string",
  "enable": true,
  "id": 0,
  "is_active": true,
  "lang": "en",
  "last_online": null,
  "last_reset_time": null,
  "mode": "no_reset",
  "name": "string",
  "package_days": 0,
  "start_date": "2019-08-24",
  "telegram_id": 0,
  "usage_limit_GB": 0,
  "uuid": null,
  "wg_pk": "string",
  "wg_psk": "string",
  "wg_pub": "string"
}
```
### Удалить пользователя:
DELETE /{proxy_path}/api/v2/admin/user/{uuid}/
Response:

```json
{
  "msg": "string",
  "status": 0
}
```
### Пинг панели:
GET /{proxy_path}/api/v2/panel/ping/
Response:

```json
{
  "msg": "string"
}
```

---

## 12. Чего НЕ делать в MVP (явные не-цели)

- Мультиадминство / роли — один админ.
- Самообслуживание / регистрация конечных юзеров.
- Биллинг, лимиты трафика, статистика расхода — это задача самой Hiddify, не дублировать
  (кроме простого отображения usage, если тривиально).
- ~~Агрегация трафика / прокси-подписки — витрина только ПОКАЗЫВАЕТ ссылки, не проксирует.~~
  **Отменено после MVP:** витрина отдаёт агрегированную подписку по той же ссылке `/u/{token}`,
  см. раздел 13.
- Дополнительные форматы sub-URL (clash/singbox раздельно) — позже.
- Автоимпорт/автосинк без подтверждения админа.

---

## 13. Агрегированная подписка (добавлено после MVP)

Отменяет пункт «агрегация / прокси-подписки» из раздела 12.

**Задача.** Пользователь добавляет в клиент одну нашу ссылку вместо набора провайдерских.
Ссылка витрины `/u/{token}` служит обеим целям: браузеру отдаётся страница, VPN-клиенту —
подписка, собранная из всех подписок этого пользователя.

### 13.1. Формат (РЕШЕНИЕ)
Массив записей, где каждая запись — либо полный Xray-конфиг с `remarks`, либо ссылка-конфиг
строкой. Это формат, который отдают панели, ориентированные на INCY/Happ.

Почему не base64-список ссылок, как изначально планировалось:
- один из провайдеров не отдаёт ничего, кроме этого формата, — на прочие UA возвращает HTML;
- бо́льшая часть его записей содержит несколько outbound'ов, `routing.balancers` и
  `burstObservatory`, то есть это балансировщики, а не отдельные серверы. Ссылкой `vless://`
  такое не выражается;
- в JSON у каждой записи своя таблица роутинга (домашние домены мимо VPN и т. п.).

Следствие: подписку понимают INCY и Happ; клиенты только на base64/Clash/sing-box — нет.

### 13.2. Склейка
Записи переносятся без изменений. Единственная правка — отображаемое имя: `remarks` у конфига,
фрагмент после `#` у ссылки. **Теги outbound'ов не трогать** — на них ссылается роутинг внутри
записи. Дубли отсеиваются по записи без имени.

### 13.3. Заголовки к провайдерам (РЕШЕНИЕ)
Наружу представляемся одним клиентом: `User-Agent` из конфига (дефолт Happ) с переопределением
на источнике, плюс `X-Hwid`, `X-Device-ID`, `X-Device-Os`, `X-Ver-Os`, `X-Device-Model`,
`X-Device-Locale`.

HWID синтетический, детерминированный: `HMAC(секрет, SourceId)`. Ключуется **источником**, а не
пользователем: общая подписка — одна провайдерская ссылка на многих наших пользователей, и HWID
от нашего пользователя размножил бы устройства. Лимиты устройств у панелей считаются по `X-Hwid`
при скачивании подписки; само VPN-подключение HWID не передаёт, поэтому реальные устройства
провайдеру не видны. Ограничение — лимит одновременных подключений/IP на его стороне.

### 13.4. Кэш и отказоустойчивость (РЕШЕНИЕ)
`SubscriptionSnapshot` хранит последний удачный ответ каждого источника (gzip) и диагностику
последней попытки. В пределах TTL наружу не ходим. Провайдер недоступен или прислал мусор —
отдаём прошлый снимок, снимок не затираем. Данных нет вовсе — **502, а не пустой ответ**:
пустой список некоторые клиенты применяют и стирают настроенный профиль.

### 13.5. Встраивание в конвейер
Middleware до `UseRouting`: путь `/u/{token}` уже занят маршрутом страницы витрины, и отдельный
endpoint дал бы конфликт маршрутов. Браузер определяется по `Mozilla/*` + `Accept: text/html`,
переключатели `?page=1` / `?sub=1`.
