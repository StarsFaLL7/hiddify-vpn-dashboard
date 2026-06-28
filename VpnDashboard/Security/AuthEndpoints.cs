using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using VpnDashboard.Services;

namespace VpnDashboard.Security;

/// <summary>
/// Endpoint'ы входа/выхода. Вынесены из Blazor-компонентов, потому что установка cookie должна
/// происходить в обычном HTTP-ответе, а не в стриминговом SSR-контуре.
/// </summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (
            HttpContext http,
            IAntiforgery antiforgery,
            AdminAuthService auth) =>
        {
            try
            {
                await antiforgery.ValidateRequestAsync(http);
            }
            catch (AntiforgeryValidationException)
            {
                return Results.Redirect($"{http.Request.PathBase}/login?error=1");
            }

            var form = await http.Request.ReadFormAsync();
            var password = form["password"].ToString();

            if (!auth.VerifyPassword(password))
                return Results.Redirect($"{http.Request.PathBase}/login?error=1");

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "admin")],
                CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });

            return Results.Redirect($"{http.Request.PathBase}/");
        });

        // Logout — GET-навигацией из меню. Для single-admin за секретным путём CSRF-риск
        // пренебрежимо мал, а cookie снимается корректно вне SSR-контура.
        app.MapGet("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect($"{http.Request.PathBase}/login");
        });
    }
}
