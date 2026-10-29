using System.Text.Json;

namespace NostalgicRellm;

/// <summary>Preferências persistidas em settings.json ao lado do executável (modo portátil).</summary>
internal sealed class Settings
{
    public string Skin { get; set; } = "E17 Glass";
    public int? X { get; set; }
    public int? Y { get; set; }
    /// <summary>Barra recolhida: só a coluna dos LEDs fica visível.</summary>
    public bool Collapsed { get; set; }
    /// <summary>Ancorada no topo da tela, reservando a faixa como a barra de tarefas.</summary>
    public bool Docked { get; set; }
    public bool ShowGraphs { get; set; } = true;
    /// <summary>Altura em pixels; 0 = mesma altura da barra de tarefas.</summary>
    public int Height { get; set; }
    /// <summary>Código do idioma (ex.: "pt-BR", "en"); null = idioma do Windows.</summary>
    public string? Language { get; set; }

    private static readonly string FilePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch (Exception) { /* arquivo corrompido: volta ao padrão */ }
        return new Settings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception) { /* pasta somente leitura: segue sem salvar */ }
    }
}
