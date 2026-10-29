using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace NostalgicRellm.Sensors;

/// <summary>
/// Lê os limites do GPT perguntando ao próprio Codex: "codex app-server" fala JSON-RPC por linhas
/// (stdin/stdout) e tem o método account/rateLimits/read, o mesmo que o app do Codex usa.
/// Assim a consulta sai pelo cliente oficial da OpenAI e o monitor não toca no token do Codex.
/// </summary>
internal static class CodexCli
{
    private static string? _cached;

    /// <summary>Caminho do codex.exe: o do app do Codex (Microsoft Store) ou o da CLI no PATH.</summary>
    public static string? Find()
    {
        if (_cached != null && File.Exists(_cached)) return _cached;
        _cached = FromStorePackage() ?? FromPath();
        return _cached;
    }

    /// <summary>O app do Codex é um pacote MSIX; o registro do Windows diz em que pasta ele está instalado.</summary>
    private static string? FromStorePackage()
    {
        const string repo = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(repo);
            if (key == null) return null;
            foreach (string name in key.GetSubKeyNames()
                         .Where(n => n.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(n => n))
            {
                using var pkg = key.OpenSubKey(name);
                if (pkg?.GetValue("PackageRootFolder") is string root &&
                    Path.Combine(root, "app", "resources", "codex.exe") is var exe && File.Exists(exe))
                    return exe;
            }
        }
        catch (Exception) { }
        return null;
    }

    private static string? FromPath() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(dir => Path.Combine(dir.Trim(), "codex.exe"))
            .FirstOrDefault(File.Exists);

    /// <summary>Abre o app-server, pede os limites e fecha. Leva menos de um segundo.</summary>
    public static async Task<AiUsageResult> ReadLimitsAsync()
    {
        string? exe = Find();
        if (exe == null) return Fail(L.T("err.codexMissing"));

        var psi = new ProcessStartInfo(exe, "app-server")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), // sem BOM: o app-server rejeita a primeira linha se houver
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var proc = Process.Start(psi);
        if (proc == null) return Fail(L.T("err.codexMissing"));
        try
        {
            await proc.StandardInput.WriteAsync(
                """{"jsonrpc":"2.0","id":0,"method":"initialize","params":{"clientInfo":{"name":"nostalgic-rellm","title":"Nostalgic Rellm","version":"0.3.1"}}}""" + "\n" +
                """{"jsonrpc":"2.0","method":"initialized"}""" + "\n" +
                """{"jsonrpc":"2.0","id":1,"method":"account/rateLimits/read"}""" + "\n");
            await proc.StandardInput.FlushAsync();
            _ = proc.StandardError.ReadToEndAsync(); // drena o log para o processo não travar com o buffer cheio

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (await proc.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (!root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || id.GetInt32() != 1) continue;
                if (root.TryGetProperty("error", out var err))
                    return Fail(L.F("err.codexFailed", err.TryGetProperty("message", out var m) ? m.GetString() : "?"));
                return Parse(root.GetProperty("result"));
            }
            return Fail(L.T("err.codexNoAnswer"));
        }
        catch (OperationCanceledException) { return Fail(L.T("err.codexNoAnswer")); }
        finally
        {
            try { proc.Kill(entireProcessTree: true); } catch (Exception) { }
        }
    }

    private static AiUsageResult Parse(JsonElement result)
    {
        if (!result.TryGetProperty("rateLimits", out var rl) || rl.ValueKind != JsonValueKind.Object)
            return Fail(L.T("err.codexNoLogin"));
        string? plan = rl.TryGetProperty("planType", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        var windows = new List<UsageWindow>();
        foreach (var key in new[] { "primary", "secondary" })
        {
            if (!rl.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) continue;
            if (!w.TryGetProperty("usedPercent", out var used) || used.ValueKind != JsonValueKind.Number) continue;
            long mins = w.TryGetProperty("windowDurationMins", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt64() : 0;
            DateTimeOffset? reset = w.TryGetProperty("resetsAt", out var r) && r.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()) : null;
            windows.Add(new UsageWindow(AiUsage.WindowName(mins * 60, key), used.GetDouble(), reset));
        }
        return new AiUsageResult(AiProvider.Gpt, plan, windows, windows.Count == 0 ? L.T("err.noData") : null);
    }

    private static AiUsageResult Fail(string msg) => new(AiProvider.Gpt, null, [], msg);
}
