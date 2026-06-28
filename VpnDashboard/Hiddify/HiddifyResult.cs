namespace VpnDashboard.Hiddify;

/// <summary>
/// Результат вызова Hiddify API. Ошибки сервера не бросаются наружу, а возвращаются как Fail,
/// чтобы недоступность панели не роняла UI.
/// </summary>
public sealed record HiddifyResult<T>(bool Success, T? Value, string? Error)
{
    public static HiddifyResult<T> Ok(T value) => new(true, value, null);
    public static HiddifyResult<T> Fail(string error) => new(false, default, error);
}
