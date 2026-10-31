namespace NostalgicRellm.Sensors;

internal enum AiProvider { Claude, Gpt }

/// <param name="Expired">A janela já reiniciou depois da última leitura: o valor atual é desconhecido.</param>
internal sealed record UsageWindow(string Name, double Percent, DateTimeOffset? ResetsAt, bool Expired = false);

/// <param name="ReadAt">Quando os números foram lidos, se vieram de uma leitura guardada (linha de status do Claude Code).</param>
internal sealed record AiUsageResult(AiProvider Provider, string? Plan, IReadOnlyList<UsageWindow> Windows, string? Error, DateTimeOffset? ReadAt = null);

/// <summary>
/// Uso dos planos de IA. Claude: direto da Anthropic com o login do Claude Code (renovado pelo próprio
/// Claude Code), com a linha de status como reserva. GPT: app-server oficial do Codex.
/// </summary>
internal static class AiUsage
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);
    private static readonly Dictionary<AiProvider, (DateTime at, AiUsageResult result)> Cache = [];

    public static async Task<AiUsageResult> GetAsync(AiProvider provider)
    {
        lock (Cache)
        {
            // Erros e leituras guardadas (reserva) não ficam em cache: o próximo clique tenta a consulta direta de novo
            if (Cache.TryGetValue(provider, out var c) && DateTime.UtcNow - c.at < CacheFor && c.result is { Error: null, ReadAt: null })
                return c.result;
        }

        AiUsageResult result;
        try
        {
            result = provider == AiProvider.Claude ? await ClaudeAsync() : await CodexCli.ReadLimitsAsync();
        }
        catch (Exception ex)
        {
            result = new AiUsageResult(provider, null, [], ex.Message);
        }

        lock (Cache) Cache[provider] = (DateTime.UtcNow, result);
        return result;
    }

    /// <summary>
    /// Claude: primeiro direto da Anthropic (login do Claude Code); se não der, a última leitura da linha de status.
    /// </summary>
    private static async Task<AiUsageResult> ClaudeAsync()
    {
        var live = await ClaudeApi.ReadAsync();
        if (live is { Error: null }) return live;
        // A leitura guardada só vale se ainda tiver algum número atual; com tudo zerado, o erro diz mais
        if (ClaudeStatusLine.Load() is { } reading && reading.Windows.Any(w => !w.Expired)) return reading;
        if (live != null) return live;
        return new AiUsageResult(AiProvider.Claude, null, [], L.T("err.claudeLogin"));
    }

    /// <summary>Nome da janela de limite a partir da duração: "Sessão (5 h)", "Semanal" ou "N dias".</summary>
    public static string WindowName(long seconds, string fallback = "") => seconds switch
    {
        <= 0 => fallback,
        < 86400 => L.F("usage.session", seconds / 3600),
        604800 => L.T("usage.weekly"),
        _ => L.F("usage.days", seconds / 86400),
    };

    /// <summary>"zera em 2 h 13 min" ou "zera qui 14:30".</summary>
    public static string DescribeReset(DateTimeOffset? at)
    {
        if (at is not { } t) return "";
        var left = t - DateTimeOffset.Now;
        if (left <= TimeSpan.Zero) return L.T("reset.now");
        if (left < TimeSpan.FromHours(24))
            return left.TotalHours >= 1 ? L.F("reset.inHours", (int)left.TotalHours, left.Minutes) : L.F("reset.inMinutes", left.Minutes);
        return L.F("reset.at", t.ToLocalTime().DateTime);
    }
}
