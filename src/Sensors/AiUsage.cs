using System.Net.Http.Headers;
using System.Text.Json;

namespace NostalgicRellm.Sensors;

internal enum AiProvider { Claude, Gpt }

internal sealed record UsageWindow(string Name, double Percent, DateTimeOffset? ResetsAt);

/// <param name="ReadAt">Quando os números foram lidos, se vieram de uma leitura guardada (linha de status do Claude Code).</param>
internal sealed record AiUsageResult(AiProvider Provider, string? Plan, IReadOnlyList<UsageWindow> Windows, string? Error, DateTimeOffset? ReadAt = null);

/// <summary>
/// Uso dos planos de IA. Claude: última leitura entregue pela linha de status oficial do Claude Code
/// (nenhuma credencial é lida). GPT: consulta a conta do Codex com o login que o app já salvou no PC;
/// esse endereço não é documentado oficialmente e pode mudar. O token só é lido (nunca renovado aqui,
/// para não derrubar o login do Codex) e só vai para o servidor da OpenAI.
/// </summary>
internal static class AiUsage
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(2);
    private static readonly Dictionary<AiProvider, (DateTime at, AiUsageResult result)> Cache = [];

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string CodexAuthFile => Path.Combine(Home, ".codex", "auth.json");

    public static async Task<AiUsageResult> GetAsync(AiProvider provider)
    {
        if (provider == AiProvider.Claude) return ClaudeFromStatusLine();

        lock (Cache)
        {
            if (Cache.TryGetValue(provider, out var c) && DateTime.UtcNow - c.at < CacheFor && c.result.Error == null)
                return c.result;
        }

        AiUsageResult result;
        try
        {
            result = await FetchGptAsync();
        }
        catch (TaskCanceledException)
        {
            result = Fail(provider, L.T("err.noServer"));
        }
        catch (Exception ex)
        {
            result = Fail(provider, ex.Message);
        }

        lock (Cache) Cache[provider] = (DateTime.UtcNow, result);
        return result;
    }

    // ---------- Claude (linha de status do Claude Code) ----------

    private static AiUsageResult ClaudeFromStatusLine()
    {
        if (ClaudeStatusLine.Load() is { } reading) return reading;
        return Fail(AiProvider.Claude, ClaudeStatusLine.GetState().Kind == ClaudeStatusLine.StateKind.Ours
            ? L.T("err.claudeNoReading")
            : L.T("err.claudeNotConnected"));
    }

    // ---------- GPT / Codex (login do app em ~/.codex/auth.json) ----------

    private static async Task<AiUsageResult> FetchGptAsync()
    {
        if (!File.Exists(CodexAuthFile))
            return Fail(AiProvider.Gpt, L.T("err.codexMissing"));

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(CodexAuthFile));
        if (!doc.RootElement.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Object)
            return Fail(AiProvider.Gpt, L.T("err.codexNoLogin"));

        using var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
        if (tokens.TryGetProperty("account_id", out var acc) && acc.GetString() is { } id)
            req.Headers.Add("ChatGPT-Account-Id", id);
        req.Headers.UserAgent.ParseAdd("codex_cli_rs/0.50.0");
        using var resp = await Http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
            return Fail(AiProvider.Gpt, L.F("err.refused", (int)resp.StatusCode));

        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = body.RootElement;
        string? plan = root.TryGetProperty("plan_type", out var p) ? p.GetString() : null;
        var windows = new List<UsageWindow>();
        if (root.TryGetProperty("rate_limit", out var rl) && rl.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "primary_window", "secondary_window" })
            {
                if (!rl.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) continue;
                // O nome vem da duração da janela, não de ser "primary" ou "secondary"
                long secs = w.TryGetProperty("limit_window_seconds", out var lw) ? lw.GetInt64() : 0;
                DateTimeOffset? reset = w.TryGetProperty("reset_at", out var ra) && ra.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(ra.GetInt64()) : null;
                windows.Add(new UsageWindow(WindowName(secs, key), w.GetProperty("used_percent").GetDouble(), reset));
            }
        }
        return new AiUsageResult(AiProvider.Gpt, plan, windows, windows.Count == 0 ? L.T("err.noData") : null);
    }

    /// <summary>Nome da janela de limite a partir da duração: "Sessão (5 h)", "Semanal" ou "N dias".</summary>
    public static string WindowName(long seconds, string fallback = "") => seconds switch
    {
        <= 0 => fallback,
        < 86400 => L.F("usage.session", seconds / 3600),
        604800 => L.T("usage.weekly"),
        _ => L.F("usage.days", seconds / 86400),
    };

    private static AiUsageResult Fail(AiProvider p, string msg) => new(p, null, [], msg);

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
