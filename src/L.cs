using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace NostalgicRellm;

/// <summary>
/// Textos da interface. Cada idioma é um JSON chave → texto: os embutidos ficam em src/lang e
/// um arquivo em ./lang ao lado do exe com o mesmo código substitui o embutido (ou acrescenta um idioma novo).
/// Chave sem tradução cai no inglês e, por fim, na própria chave.
/// </summary>
internal static class L
{
    private static Dictionary<string, string> _strings = [];
    private static Dictionary<string, string> _fallback = [];

    public static string Code { get; private set; } = "en";
    public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture;

    private static string ExternalDir => Path.Combine(AppContext.BaseDirectory, "lang");

    /// <summary>Carrega o idioma pedido; null ou desconhecido = idioma do Windows, senão inglês.</summary>
    public static void Load(string? code)
    {
        var available = Available().Select(a => a.code).ToList();
        string ui = CultureInfo.CurrentUICulture.Name;
        code = code != null && available.Contains(code) ? code
            : available.FirstOrDefault(c => c.Equals(ui, StringComparison.OrdinalIgnoreCase))
            ?? available.FirstOrDefault(c => c.Split('-')[0].Equals(ui.Split('-')[0], StringComparison.OrdinalIgnoreCase))
            ?? "en";

        _fallback = Read("en") ?? [];
        _strings = Read(code) ?? _fallback;
        Code = code;
        try { Culture = CultureInfo.GetCultureInfo(code); } catch (CultureNotFoundException) { Culture = CultureInfo.InvariantCulture; }
    }

    public static string T(string key) =>
        _strings.TryGetValue(key, out var s) ? s : _fallback.TryGetValue(key, out var f) ? f : key;

    public static string F(string key, params object?[] args) => string.Format(Culture, T(key), args);

    /// <summary>Idiomas disponíveis (embutidos + pasta ./lang), com o nome de cada um no próprio idioma.</summary>
    public static List<(string code, string name)> Available()
    {
        var codes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string res in Assembly.GetExecutingAssembly().GetManifestResourceNames().Where(r => r.StartsWith("lang/")))
            codes.Add(Path.GetFileNameWithoutExtension(res));
        if (Directory.Exists(ExternalDir))
            foreach (string f in Directory.GetFiles(ExternalDir, "*.json"))
                codes.Add(Path.GetFileNameWithoutExtension(f));
        return codes.Select(c => (c, Read(c)?.GetValueOrDefault("lang.name") ?? c)).ToList();
    }

    private static Dictionary<string, string>? Read(string code)
    {
        try
        {
            string external = Path.Combine(ExternalDir, code + ".json");
            if (File.Exists(external))
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(external));
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"lang/{code}.json");
            return s == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(s);
        }
        catch (Exception) { return null; }
    }
}
