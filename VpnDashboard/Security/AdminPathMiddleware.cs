using Microsoft.Extensions.Options;
using VpnDashboard.Options;

namespace VpnDashboard.Security;

/// <summary>
/// Сторож админ-зоны. Работает в паре с <c>UsePathBase(secretPrefix)</c>: фреймворк уже снял
/// секретный префикс в <see cref="HttpRequest.PathBase"/>, а этот middleware решает, обслуживать
/// запрос или скрыть админку.
/// <list type="bullet">
/// <item>Запрос в админ-зоне (PathBase == секретный префикс): требуем cookie-аутентификацию,
/// кроме логина, auth-инфраструктуры Blazor и статики.</item>
/// <item>Запрос без префикса: пропускаем витрину (/u/...), инфраструктуру Blazor (/_...),
/// статику и error-страницы; остальные (попытки достучаться до админки) → нейтральный 404.</item>
/// </list>
/// </summary>
public sealed class AdminPathMiddleware(RequestDelegate next, IOptions<AdminOptions> options)
{
    private const string LoginPath = "/login";

    public async Task InvokeAsync(HttpContext context)
    {
        var admin = options.Value;

        // До первичной настройки секрета (например, в dev) ничего не скрываем.
        if (!admin.IsConfigured)
        {
            await next(context);
            return;
        }

        var path = context.Request.Path.Value ?? "/";
        var inAdminArea = context.Request.PathBase.Value?.Equals(admin.SecretPrefix, StringComparison.Ordinal) == true;

        if (inAdminArea)
        {
            var authFree = path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase)
                           || path.StartsWith("/_", StringComparison.Ordinal)
                           || Path.HasExtension(path);

            if (!authFree && context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.Redirect(context.Request.PathBase + LoginPath);
                return;
            }

            await next(context);
            return;
        }

        if (IsPublicRootPath(path))
        {
            await next(context);
            return;
        }

        // Попытка достучаться до админ-страницы без секретного префикса — нейтральный 404.
        var statusCodePages = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>();
        if (statusCodePages is not null)
            statusCodePages.Enabled = false;
        context.Response.StatusCode = StatusCodes.Status404NotFound;
    }

    private static bool IsPublicRootPath(string path) =>
        path.StartsWith("/u/", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/u", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/_", StringComparison.Ordinal)
        || path.Equals("/not-found", StringComparison.OrdinalIgnoreCase)
        || path.Equals("/Error", StringComparison.OrdinalIgnoreCase)
        || Path.HasExtension(path);
}
