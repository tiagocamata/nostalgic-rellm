using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace NostalgicRellm.UI;

/// <summary>
/// Aparência carregada de skins/&lt;nome&gt;/skin.json. Cores em #RRGGBB ou #AARRGGBB.
/// Qualquer campo omitido no JSON usa o valor padrão daqui.
/// </summary>
internal sealed class Skin
{
    public string Name { get; set; } = "Padrão";
    public string Font { get; set; } = "Segoe UI";
    /// <summary>Imagem opcional (PNG com transparência) esticada no fundo da barra.</summary>
    public string? BackgroundImage { get; set; }

    public PanelStyle Panel { get; set; } = new();
    public CellStyle Cell { get; set; } = new();
    public TextStyle Text { get; set; } = new();
    public BarStyle Bar { get; set; } = new();
    public GraphStyle Graph { get; set; } = new();
    public LedStyle Led { get; set; } = new();
    public PopupStyle Popup { get; set; } = new();

    public sealed class PanelStyle
    {
        public string Top { get; set; } = "#CC1A2433";
        public string Bottom { get; set; } = "#CC0A0F18";
        public string Border { get; set; } = "#6690B8FF";
        /// <summary>Reflexo de vidro na metade de cima; transparente desliga.</summary>
        public string Gloss { get; set; } = "#22FFFFFF";
        public float Radius { get; set; } = 6;
        public bool Bevel { get; set; }
    }

    public sealed class CellStyle
    {
        public string Fill { get; set; } = "#26000000";
        public string Border { get; set; } = "#1EFFFFFF";
        public string Hover { get; set; } = "#1E8FD0FF";
        public float Radius { get; set; } = 4;
        /// <summary>Relevo estilo E16/GKrellM (claro em cima/esquerda, escuro embaixo/direita).</summary>
        public bool Bevel { get; set; }
    }

    public sealed class TextStyle
    {
        public string Label { get; set; } = "#FF9FB8D0";
        public string Value { get; set; } = "#FFFFFFFF";
        public string Detail { get; set; } = "#FF7F98B0";
        public string Hot { get; set; } = "#FFFF6A4D";
        public string Shadow { get; set; } = "#99000000";
        public bool Bold { get; set; } = true;
    }

    public sealed class BarStyle
    {
        public string Low { get; set; } = "#FF3FA9F5";
        public string High { get; set; } = "#FFFF4D4D";
        public string Track { get; set; } = "#40000000";
        public bool Gloss { get; set; } = true;
    }

    public sealed class GraphStyle
    {
        public string Line { get; set; } = "#FF5FC8FF";
        public string Fill { get; set; } = "#505FC8FF";
        public string Grid { get; set; } = "#14FFFFFF";
    }

    public sealed class LedStyle
    {
        public string On { get; set; } = "#FF4DFF7A";
        public string Off { get; set; } = "#FF26402E";
        public string Button { get; set; } = "#40FFFFFF";
    }

    public sealed class PopupStyle
    {
        public string Background { get; set; } = "#D8101822";
        public string Border { get; set; } = "#6690B8FF";
        public string Title { get; set; } = "#FF8FD0FF";
        public string Text { get; set; } = "#FFE0ECFF";
        public string Hover { get; set; } = "#305FC8FF";
        public string Kill { get; set; } = "#FFFF5A5A";
    }

    [System.Text.Json.Serialization.JsonIgnore] public string Folder { get; private set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public Image? Background { get; private set; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string SkinsRoot => Path.Combine(AppContext.BaseDirectory, "skins");

    public static IEnumerable<string> Available()
    {
        if (!Directory.Exists(SkinsRoot)) return [];
        return Directory.GetDirectories(SkinsRoot)
            .Where(d => File.Exists(Path.Combine(d, "skin.json")))
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(n => n);
    }

    public static Skin Load(string folderName)
    {
        string dir = Path.Combine(SkinsRoot, folderName);
        Skin skin;
        try
        {
            skin = JsonSerializer.Deserialize<Skin>(File.ReadAllText(Path.Combine(dir, "skin.json")), JsonOpts) ?? new Skin();
        }
        catch (Exception)
        {
            skin = new Skin();
        }
        skin.Folder = dir;
        if (skin.BackgroundImage is { } img && File.Exists(Path.Combine(dir, img)))
        {
            // Copia para memória para não travar o arquivo enquanto o usuário edita a skin
            using var fs = File.OpenRead(Path.Combine(dir, img));
            skin.Background = new Bitmap(Image.FromStream(fs));
        }
        return skin;
    }

    /// <summary>Extrai as skins embutidas para ./skins sem sobrescrever as que já existem.</summary>
    public static void ExtractDefaults()
    {
        var asm = Assembly.GetExecutingAssembly();
        foreach (string res in asm.GetManifestResourceNames().Where(r => r.StartsWith("skins/")))
        {
            string target = Path.Combine(AppContext.BaseDirectory, res.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target)) continue;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var src = asm.GetManifestResourceStream(res)!;
                using var dst = File.Create(target);
                src.CopyTo(dst);
            }
            catch (Exception) { }
        }
    }

    private static readonly Dictionary<string, Color> ColorCache = [];

    public static Color C(string hex)
    {
        if (ColorCache.TryGetValue(hex, out var c)) return c;
        string h = hex.TrimStart('#');
        uint v = uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0xFFFF00FF;
        if (h.Length == 6) v |= 0xFF000000;
        c = Color.FromArgb(unchecked((int)v));
        ColorCache[hex] = c;
        return c;
    }
}
