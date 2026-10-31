using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NostalgicRellm.Sensors;

/// <summary>
/// Uso do plano do Claude direto da Anthropic, com o login que o Claude Code mantém em ~/.claude/.credentials.json.
/// O token só é lido e só vai para api.anthropic.com; quando venceu, quem o renova é o próprio Claude Code
/// ("claude auth status", que não gasta nada do limite). Sem o Claude Code instalado e logado, devolve null.
/// </summary>
internal static class ClaudeApi
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string CredentialsFile => Path.Combine(Home, ".claude", ".credentials.json");

    private sealed record Login(string Token, DateTimeOffset? ExpiresAt, string? Plan);

    /// <summary>Plano do login do Claude Code (ex.: "pro"), ou null se não houver login de assinatura.</summary>
    public static string? SignedInPlan() => ReadLogin() is { } l ? l.Plan ?? "" : null;

    public static async Task<AiUsageResult?> ReadAsync()
    {
        if (ReadLogin() is not { } login) return null;

        // Renova antes de usar se faltar pouco para vencer; o Claude Code grava o token novo no mesmo arquivo
        if (login.ExpiresAt is { } exp && exp < DateTimeOffset.UtcNow.AddMinutes(5))
            login = await RefreshAsync() ?? login;

        var (status, windows, detail) = await QueryAsync(login.Token);
        // Qualquer resposta sem os números (recusa, falha, corpo inesperado): o Claude Code renova o login
        // e tenta-se mais uma vez. Só não insiste no 429, que é pedido explícito de pausa.
        // Na recusa (401/403) o token pode ainda parecer válido pelo arquivo, e aí o "auth status" não renova:
        // por isso a renovação é forçada.
        string refresh = "";
        if (windows == null && status != 429)
        {
            string oldToken = login.Token;
            var fresh = await RefreshAsync(force: status is 401 or 403);
            refresh = fresh == null ? " | renovação falhou" : fresh.Token != oldToken ? " | login renovado" : " | login inalterado";
            if (fresh != null)
            {
                login = fresh;
                (status, windows, detail) = await QueryAsync(login.Token);
            }
        }

        if (windows == null)
        {
            Log($"usage falhou: status {status}{refresh}{(detail is { Length: > 0 } ? " | " + detail : "")}");
            return new AiUsageResult(AiProvider.Claude, login.Plan, [], status switch
            {
                401 or 403 => L.T("err.claudeRejected"),
                429 => L.T("err.claudeBusy"),
                0 => L.T("err.noServer"),
                200 => L.T("err.noData"),
                _ => L.F("err.claudeFailed", status),
            });
        }
        return new AiUsageResult(AiProvider.Claude, login.Plan, windows, null);
    }

    /// <summary>Consulta o uso; devolve as janelas só se a resposta veio completa (200 com pelo menos uma janela).</summary>
    /// <remarks>Na falha, detail traz o começo da resposta (mensagem de erro da Anthropic, sem dados de login).</remarks>
    private static async Task<(int status, List<UsageWindow>? windows, string? detail)> QueryAsync(string token)
    {
        var (status, body) = await GetUsageAsync(token);
        string? detail = body is { Length: > 0 } ? body.ReplaceLineEndings(" ")[..Math.Min(body.Length, 200)] : null;
        if (status != 200 || body == null) return (status, null, detail);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var windows = new List<UsageWindow>();
            AddWindow(doc.RootElement, "five_hour", AiUsage.WindowName(5 * 3600), windows);
            AddWindow(doc.RootElement, "seven_day", AiUsage.WindowName(7 * 86400), windows);
            AddWindow(doc.RootElement, "seven_day_opus", L.T("usage.weeklyOpus"), windows);
            AddWindow(doc.RootElement, "seven_day_sonnet", L.T("usage.weeklySonnet"), windows);
            return (status, windows.Count > 0 ? windows : null, detail);
        }
        catch (JsonException) { return (status, null, detail); }
    }

    private static Login? ReadLogin()
    {
        try
        {
            if (!File.Exists(CredentialsFile)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(CredentialsFile));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o) || o.ValueKind != JsonValueKind.Object) return null;
            if (o.GetProperty("accessToken").GetString() is not { Length: > 0 } token) return null;
            DateTimeOffset? exp = o.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeMilliseconds(e.GetInt64()) : null;
            string? plan = o.TryGetProperty("subscriptionType", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            return new Login(token, exp, plan);
        }
        catch (Exception) { return null; }
    }

    private static async Task<(int status, string? body)> GetUsageAsync(string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            req.Headers.Accept.ParseAdd("application/json");
            using var resp = await Http.SendAsync(req);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }
        catch (Exception) { return (0, null); }
    }

    /// <summary>
    /// Pede ao Claude Code para renovar o próprio login e relê o arquivo. O "auth status" só renova token vencido;
    /// com force, a validade no arquivo é zerada antes (o token em si não é lido nem alterado), e devolvida se o
    /// Claude Code não conseguir renovar.
    /// </summary>
    private static async Task<Login?> RefreshAsync(bool force = false)
    {
        if (FindCli() is not { } cli) return null;
        long? oldExpiry = force ? SetExpiry(0) : null;
        try
        {
            var psi = new ProcessStartInfo(cli, "auth status")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = Process.Start(psi);
            if (proc == null) return null;
            _ = proc.StandardOutput.ReadToEndAsync();
            _ = proc.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await proc.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { try { proc.Kill(true); } catch (Exception) { } }
        }
        catch (Exception) { }
        finally
        {
            // Se o Claude Code não gravou um token novo, a validade original volta para o lugar
            if (oldExpiry is { } exp && ReadLogin() is { ExpiresAt: { } now } && now.ToUnixTimeMilliseconds() == 0)
                SetExpiry(exp);
        }
        return ReadLogin();
    }

    /// <summary>Troca só o campo expiresAt do login no arquivo do Claude Code; devolve o valor anterior.</summary>
    private static long? SetExpiry(long value)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(CredentialsFile));
            if (root?["claudeAiOauth"] is not JsonObject o || o["expiresAt"] is not JsonValue v || !v.TryGetValue(out long old))
                return null;
            o["expiresAt"] = value;
            string tmp = CredentialsFile + ".nr-tmp";
            File.WriteAllText(tmp, root.ToJsonString(), new System.Text.UTF8Encoding(false));
            File.Move(tmp, CredentialsFile, overwrite: true);
            return old;
        }
        catch (Exception) { return null; }
    }

    /// <summary>
    /// Registra falhas em %LOCALAPPDATA%\NostalgicRellm\ai.log (só hora e código HTTP, nunca o token),
    /// para dar para saber depois por que uma consulta não voltou.
    /// </summary>
    private static void Log(string message)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NostalgicRellm");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "ai.log");
            if (File.Exists(file) && new FileInfo(file).Length > 64 * 1024) File.Delete(file);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}", new System.Text.UTF8Encoding(false));
        }
        catch (Exception) { }
    }

    /// <summary>claude.exe da instalação nativa, ou o que estiver no PATH.</summary>
    public static string? FindCli()
    {
        string native = Path.Combine(Home, ".local", "bin", "claude.exe");
        if (File.Exists(native)) return native;
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim(), "claude.exe"))
            .FirstOrDefault(File.Exists);
    }

    private static void AddWindow(JsonElement root, string key, string name, List<UsageWindow> into)
    {
        if (!root.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) return;
        if (!w.TryGetProperty("utilization", out var u) || u.ValueKind != JsonValueKind.Number) return;
        DateTimeOffset? reset = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(r.GetString(), out var dt) ? dt : null;
        into.Add(new UsageWindow(name, u.GetDouble(), reset));
    }
}
