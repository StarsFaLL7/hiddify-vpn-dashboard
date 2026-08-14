namespace VpnDashboard.Probing;

/// <summary>Итог проверки сервера.</summary>
public enum ProbeVerdict
{
    /// <summary>Сервер ответил.</summary>
    Ok,

    /// <summary>Сервер не ответил — кандидат на исключение из подписки.</summary>
    Failed,

    /// <summary>Проверить не смогли. Такие записи из подписки не выбрасываются.</summary>
    Unsupported
}

/// <param name="Method">Чем проверяли: xray, tls, tcp.</param>
public sealed record ProbeResult(ProbeVerdict Verdict, string Method, int? LatencyMs = null, string? Error = null)
{
    public static ProbeResult Ok(string method, int latencyMs) => new(ProbeVerdict.Ok, method, latencyMs);

    public static ProbeResult Failed(string method, string error) => new(ProbeVerdict.Failed, method, Error: error);

    public static ProbeResult Unsupported(string reason) => new(ProbeVerdict.Unsupported, "—", Error: reason);
}
