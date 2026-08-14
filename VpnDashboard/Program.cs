using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using Serilog;
using VpnDashboard.BackgroundServices;
using VpnDashboard.Components;
using VpnDashboard.Data;
using VpnDashboard.Hiddify;
using VpnDashboard.Options;
using VpnDashboard.Security;
using VpnDashboard.Services;
using VpnDashboard.Subscriptions;

// Setup-команда: `dotnet run -- hash-password <пароль>` печатает hash/salt для конфига.
if (args is ["hash-password", var pwd])
{
    var (hash, salt) = AdminAuthService.HashPassword(pwd);
    Console.WriteLine($"Admin__PasswordHash={hash}");
    Console.WriteLine($"Admin__PasswordSalt={salt}");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// Логирование через Serilog (конфигурируется из appsettings + кода).
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// За reverse-proxy с TLS-терминацией доверяем X-Forwarded-Proto/For, чтобы корректно
// работали Secure-cookie и определение https. Прокси контролируется нами (приватный деплой).
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                         | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

// Конфигурация опций.
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection(AdminOptions.SectionName));
builder.Services.Configure<PingOptions>(builder.Configuration.GetSection(PingOptions.SectionName));
builder.Services.Configure<ShowcaseOptions>(builder.Configuration.GetSection(ShowcaseOptions.SectionName));
builder.Services.Configure<AggregationOptions>(builder.Configuration.GetSection(AggregationOptions.SectionName));

// База данных (SQLite, путь — из конфига для монтирования в Docker volume).
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                       ?? "Data Source=vpndashboard.db";
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseSqlite(connectionString));

// Доменные сервисы и клиент Hiddify.
builder.Services.AddScoped<ServerService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<BindingService>();
builder.Services.AddScoped<SubscriptionService>();
builder.Services.AddScoped<ShowcaseService>();

// Агрегатор подписок: одна ссылка пользователя вместо набора провайдерских.
builder.Services.AddSingleton<SyntheticIdentity>();
builder.Services.AddSingleton<SourceLockRegistry>();
// Варианты подписки: общая ссылка плюс отдельные — на прямые серверы и на каждую группу со слагом.
builder.Services.AddSingleton<SubscriptionVariants>();
builder.Services.AddScoped<AggregationService>();
builder.Services.AddHttpClient<UpstreamSubscriptionFetcher>(c =>
{
    // Реальный таймаут задаётся на каждый запрос из AggregationOptions; здесь — верхняя граница.
    c.Timeout = TimeSpan.FromSeconds(60);
});

// Фоновый пинг серверов.
builder.Services.AddHostedService<ServerPingService>();
builder.Services.AddHttpClient<IHiddifyApiClient, HiddifyApiClient>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});

// Аутентификация админа: cookie-схема.
builder.Services.AddSingleton<AdminAuthService>();
builder.Services.AddHttpContextAccessor();
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "vpndash_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        // Доступ к админке закрыт middleware'ом; здесь схема нужна для Sign-In/Out и валидации.
        options.Events.OnRedirectToLogin = ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

// Blazor + MudBlazor.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();

// Применяем миграции при старте.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext();
    db.Database.Migrate();
    // Агрегатор обновляет снимки подписок параллельно по источникам; WAL позволяет
    // читателям не блокироваться на писателе и снимает «database is locked».
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

// Доверие заголовкам прокси — первым в пайплайне, чтобы https/scheme определялись верно.
app.UseForwardedHeaders();

// Агрегированная подписка отдаётся по адресу витрины и должна перехватываться до маршрутизации:
// иначе её путь конфликтует с маршрутом страницы витрины. Браузеры проходят дальше.
app.UseMiddleware<AggregatedSubscriptionMiddleware>();

// Секретный префикс админки: фреймворк снимает его в PathBase, чтобы NavigationManager и роутинг
// оставались консистентными. Витрина и статика обслуживаются на реальном корне.
var adminOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminOptions>>().Value;
if (adminOptions.IsConfigured)
    app.UsePathBase(adminOptions.SecretPrefix);

// Явный UseRouting ПОСЛЕ UsePathBase: иначе авто-UseRouting встаёт в начало пайплайна и матчит
// эндпоинты по пути с секретным префиксом, из-за чего minimal-API маршруты (/auth/...) не находятся.
app.UseRouting();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseMiddleware<AdminPathMiddleware>();
app.UseAuthorization();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapAuthEndpoints();

app.Run();
