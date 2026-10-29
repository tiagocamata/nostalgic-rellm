using System.Text.Json;
using System.Text.Json.Nodes;

namespace NostalgicRellm.Sensors;

/// <summary>
/// Integração com a linha de status oficial do Claude Code: ele chama "NostalgicRellm.exe --statusline",
/// passa os dados da sessão em JSON pela entrada padrão (inclusive o uso dos limites, para planos Pro/Max)
/// e mostra o que for impresso. Guardamos os limites num arquivo que o popup de IAs lê depois.
/// Também liga e desliga essa integração nas configurações do Claude Code (tela Conexões).
/// </summary>
internal static class ClaudeStatusLine
{
    public static string DataFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NostalgicRellm", "claude-usage.json");

    private static string ClaudeDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
    public static string SettingsFile => Path.Combine(ClaudeDir, "settings.json");

    /// <summary>Comando que o Claude Code deve rodar: este exe, com barras normais (o Git Bash come as invertidas).</summary>
    public static string OurCommand => $"\"{Environment.ProcessPath!.Replace('\\', '/')}\" --statusline";

    // ---------- Modo --statusline ----------

    /// <summary>Executado pelo Claude Code a cada atualização da linha de status. Precisa ser rápido.</summary>
    public static void Run()
    {
        string line = "";
        try
        {
            var input = JsonNode.Parse(Console.In.ReadToEnd());
            string model = input?["model"]?["display_name"]?.GetValue<string>() ?? "Claude";
            double? ctx = Number(input?["context_window"]?["used_percentage"]);
            var limits = input?["rate_limits"];
            double? fiveHour = Number(limits?["five_hour"]?["used_percentage"]);
            double? sevenDay = Number(limits?["seven_day"]?["used_percentage"]);

            // Só regrava quando os limites vieram: no começo da sessão eles ainda não existem
            if (limits is JsonObject)
                Save(limits);

            line = $"[{model}]" +
                (ctx is { } c ? " " + L.F("status.context", c) : "") +
                (fiveHour is { } f ? " · " + L.F("status.session", f) : "") +
                (sevenDay is { } s ? " · " + L.F("status.week", s) : "");
        }
        catch (Exception) { /* entrada inesperada: imprime vazio em vez de quebrar a linha de status */ }
        // Sem console próprio (exe gráfico): escreve UTF-8 direto na saída que o Claude Code lê
        using var stdout = Console.OpenStandardOutput();
        stdout.Write(System.Text.Encoding.UTF8.GetBytes(line));
    }

    private static double? Number(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<double>(out double d) ? d : null;

    private static void Save(JsonNode limits)
    {
        var data = new JsonObject
        {
            ["read_at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["rate_limits"] = limits.DeepClone(),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(DataFile)!);
        // Grava num temporário e troca, para o monitor nunca ler um arquivo pela metade
        string tmp = DataFile + ".tmp";
        File.WriteAllText(tmp, data.ToJsonString());
        File.Move(tmp, DataFile, overwrite: true);
    }

    // ---------- Leitura para o popup ----------

    /// <summary>Última leitura gravada pela linha de status, ou null se ainda não houve nenhuma.</summary>
    public static AiUsageResult? Load()
    {
        if (!File.Exists(DataFile)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(DataFile));
            var root = doc.RootElement;
            var readAt = DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("read_at").GetInt64());
            var windows = new List<UsageWindow>();
            var limits = root.GetProperty("rate_limits");
            AddWindow(limits, "five_hour", AiUsage.WindowName(5 * 3600), windows);
            AddWindow(limits, "seven_day", AiUsage.WindowName(7 * 86400), windows);
            return new AiUsageResult(AiProvider.Claude, null, windows, windows.Count == 0 ? L.T("err.noData") : null, readAt);
        }
        catch (Exception) { return null; }
    }

    public static DateTimeOffset? LastReading() => Load()?.ReadAt;

    private static void AddWindow(JsonElement limits, string key, string name, List<UsageWindow> into)
    {
        if (!limits.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) return;
        if (!w.TryGetProperty("used_percentage", out var p) || p.ValueKind != JsonValueKind.Number) return;
        DateTimeOffset? reset = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.Number
            ? DateTimeOffset.FromUnixTimeSeconds(r.GetInt64()) : null;
        // Janela já reiniciou desde a última leitura: o uso voltou a zero
        bool expired = reset is { } t && t < DateTimeOffset.UtcNow;
        into.Add(new UsageWindow(name, expired ? 0 : p.GetDouble(), expired ? null : reset));
    }

    // ---------- Conectar / desconectar nas configurações do Claude Code ----------

    public enum StateKind { NoClaudeCode, None, Ours, OursElsewhere, Other }

    public sealed record State(StateKind Kind, string? Command);

    public static State GetState()
    {
        if (!Directory.Exists(ClaudeDir)) return new State(StateKind.NoClaudeCode, null);
        string? cmd = ReadSettings()?["statusLine"]?["command"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(cmd)) return new State(StateKind.None, null);
        bool ours = cmd.Contains("--statusline") &&
            (cmd.Contains("NostalgicRellm.exe", StringComparison.OrdinalIgnoreCase) ||
             cmd.Contains("MonitorRecursos.exe", StringComparison.OrdinalIgnoreCase));
        if (!ours) return new State(StateKind.Other, cmd);
        return new State(cmd.Equals(OurCommand, StringComparison.OrdinalIgnoreCase) ? StateKind.Ours : StateKind.OursElsewhere, cmd);
    }

    /// <summary>Grava (ou substitui) a linha de status apontando para este exe, preservando o resto das configurações.</summary>
    public static void Connect()
    {
        var root = ReadSettings() ?? new JsonObject();
        root["statusLine"] = new JsonObject { ["type"] = "command", ["command"] = OurCommand };
        WriteSettings(root);
    }

    public static void Disconnect()
    {
        if (ReadSettings() is not { } root) return;
        root.Remove("statusLine");
        WriteSettings(root);
    }

    private static JsonObject? ReadSettings()
    {
        if (!File.Exists(SettingsFile)) return null;
        var opts = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return JsonNode.Parse(File.ReadAllText(SettingsFile), documentOptions: opts) as JsonObject;
    }

    private static void WriteSettings(JsonObject root)
    {
        Directory.CreateDirectory(ClaudeDir);
        File.WriteAllText(SettingsFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
