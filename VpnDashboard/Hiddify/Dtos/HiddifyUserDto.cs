using System.Text.Json.Serialization;

namespace VpnDashboard.Hiddify.Dtos;

/// <summary>
/// DTO ответа Hiddify admin-API по пользователю. Десериализует ВСЕ поля, включая приватные ключи,
/// но в доменные сущности маппится только безопасное подмножество (uuid/name/enable/usage/...).
/// Приватные ключи НИКОГДА не сохраняются в нашу БД и не логируются.
/// </summary>
public sealed class HiddifyUserDto
{
    [JsonPropertyName("uuid")]
    public string? Uuid { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("enable")]
    public bool Enable { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }

    [JsonPropertyName("current_usage_GB")]
    public double CurrentUsageGb { get; set; }

    [JsonPropertyName("usage_limit_GB")]
    public double UsageLimitGb { get; set; }

    [JsonPropertyName("package_days")]
    public int? PackageDays { get; set; }

    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("last_online")]
    public string? LastOnline { get; set; }

    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    [JsonPropertyName("telegram_id")]
    public long? TelegramId { get; set; }

    [JsonPropertyName("lang")]
    public string? Lang { get; set; }

    // --- Приватные поля: десериализуются, но НЕ покидают слой клиента ---
    [JsonPropertyName("ed25519_private_key")]
    public string? Ed25519PrivateKey { get; set; }

    [JsonPropertyName("wg_pk")]
    public string? WireguardPrivateKey { get; set; }

    [JsonPropertyName("wg_psk")]
    public string? WireguardPresharedKey { get; set; }
}
