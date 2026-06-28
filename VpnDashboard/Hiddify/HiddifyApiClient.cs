using System.Net.Http.Json;
using System.Text.Json;
using VpnDashboard.Data.Entities;
using VpnDashboard.Hiddify.Dtos;

namespace VpnDashboard.Hiddify;

/// <summary>
/// Типизированный клиент Hiddify admin-API. Базовый адрес строится per-server:
/// https://{Domain}/{AdminProxyPath}/api/v2/. Все ошибки сети/сервера обрабатываются gracefully.
/// </summary>
public sealed class HiddifyApiClient(HttpClient http, ILogger<HiddifyApiClient> logger) : IHiddifyApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> PingAsync(HiddifyServer server, CancellationToken ct = default)
    {
        try
        {
            using var request = BuildRequest(server, HttpMethod.Get, "panel/ping/");
            using var response = await http.SendAsync(request, ct);
            logger.LogInformation("Hiddify ping {Server}: {Status}", server.Name, (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Hiddify ping {Server} failed: {Message}", server.Name, ex.Message);
            return false;
        }
    }

    public async Task<HiddifyResult<List<HiddifyUserDto>>> GetUsersAsync(HiddifyServer server, CancellationToken ct = default)
    {
        try
        {
            using var request = BuildRequest(server, HttpMethod.Get, "admin/user/");
            using var response = await http.SendAsync(request, ct);
            logger.LogInformation("Hiddify get-users {Server}: {Status}", server.Name, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
                return HiddifyResult<List<HiddifyUserDto>>.Fail($"HTTP {(int)response.StatusCode}");

            var users = await response.Content.ReadFromJsonAsync<List<HiddifyUserDto>>(JsonOptions, ct);
            return HiddifyResult<List<HiddifyUserDto>>.Ok(users ?? []);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Hiddify get-users {Server} failed: {Message}", server.Name, ex.Message);
            return HiddifyResult<List<HiddifyUserDto>>.Fail(ex.Message);
        }
    }

    public async Task<HiddifyResult<HiddifyUserDto>> CreateUserAsync(HiddifyServer server, CreateHiddifyUserRequest req, CancellationToken ct = default)
    {
        try
        {
            var body = new
            {
                name = req.Name,
                package_days = req.PackageDays,
                usage_limit_GB = req.UsageLimitGb,
                mode = req.Mode,
                enable = true,
                is_active = true,
                uuid = (string?)null
            };
            using var request = BuildRequest(server, HttpMethod.Post, "admin/user/", body);
            using var response = await http.SendAsync(request, ct);
            logger.LogInformation("Hiddify create-user {Server}: {Status}", server.Name, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
                return HiddifyResult<HiddifyUserDto>.Fail($"HTTP {(int)response.StatusCode}");

            var user = await response.Content.ReadFromJsonAsync<HiddifyUserDto>(JsonOptions, ct);
            return user is { Uuid: not null }
                ? HiddifyResult<HiddifyUserDto>.Ok(user)
                : HiddifyResult<HiddifyUserDto>.Fail("Сервер не вернул UUID");
        }
        catch (Exception ex)
        {
            logger.LogWarning("Hiddify create-user {Server} failed: {Message}", server.Name, ex.Message);
            return HiddifyResult<HiddifyUserDto>.Fail(ex.Message);
        }
    }

    public async Task<HiddifyResult<HiddifyUserDto>> UpdateUserAsync(HiddifyServer server, string uuid, object patch, CancellationToken ct = default)
    {
        try
        {
            using var request = BuildRequest(server, HttpMethod.Patch, $"admin/user/{uuid}/", patch);
            using var response = await http.SendAsync(request, ct);
            logger.LogInformation("Hiddify update-user {Server}: {Status}", server.Name, (int)response.StatusCode);

            if (!response.IsSuccessStatusCode)
                return HiddifyResult<HiddifyUserDto>.Fail($"HTTP {(int)response.StatusCode}");

            var user = await response.Content.ReadFromJsonAsync<HiddifyUserDto>(JsonOptions, ct);
            return HiddifyResult<HiddifyUserDto>.Ok(user!);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Hiddify update-user {Server} failed: {Message}", server.Name, ex.Message);
            return HiddifyResult<HiddifyUserDto>.Fail(ex.Message);
        }
    }

    public async Task<bool> DeleteUserAsync(HiddifyServer server, string uuid, CancellationToken ct = default)
    {
        try
        {
            using var request = BuildRequest(server, HttpMethod.Delete, $"admin/user/{uuid}/");
            using var response = await http.SendAsync(request, ct);
            logger.LogInformation("Hiddify delete-user {Server}: {Status}", server.Name, (int)response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Hiddify delete-user {Server} failed: {Message}", server.Name, ex.Message);
            return false;
        }
    }

    private static HttpRequestMessage BuildRequest(HiddifyServer server, HttpMethod method, string relative, object? body = null)
    {
        var domain = server.Domain.TrimEnd('/');
        var adminPath = server.AdminProxyPath.Trim('/');
        var baseUrl = $"https://{domain}/{adminPath}/api/v2/{relative}";

        var request = new HttpRequestMessage(method, baseUrl);
        request.Headers.Add("Hiddify-API-Key", server.AdminUuid);
        request.Headers.Add("Accept", "application/json");
        if (body is not null)
            request.Content = JsonContent.Create(body, options: JsonOptions);
        return request;
    }
}
