using System.Text.Json.Serialization;

namespace VpnDashboard.Hiddify.Dtos;

/// <summary>Ответ GET panel/ping/.</summary>
public sealed class PingResponseDto
{
    [JsonPropertyName("msg")]
    public string? Msg { get; set; }
}

/// <summary>Ответ DELETE admin/user/{uuid}/.</summary>
public sealed class DeleteResultDto
{
    [JsonPropertyName("msg")]
    public string? Msg { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }
}

/// <summary>Параметры создания пользователя Hiddify через API.</summary>
public sealed record CreateHiddifyUserRequest(
    string Name,
    int PackageDays,
    double UsageLimitGb,
    string Mode = "no_reset");
