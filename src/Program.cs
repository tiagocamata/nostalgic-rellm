using System.Text;
using NostalgicRellm.Sensors;
using NostalgicRellm.UI;

namespace NostalgicRellm;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Chamado pela linha de status do Claude Code: roda sem elevação e sai rápido
        L.Load(Settings.Load().Language);

        if (args.Contains("--statusline"))
        {
            ClaudeStatusLine.Run();
            return;
        }

        if (args.Contains("--dump"))
        {
            Dump();
            return;
        }

        // A barra precisa de administrador (temperatura da CPU, finalizar processos). O exe não exige elevação
        // no manifesto para que o modo --statusline não abra o aviso do UAC a cada atualização.
        if (!IsAdministrator() && !args.Contains("--no-elevate") && RelaunchElevated(args)) return;

        using var mutex = new Mutex(true, @"Local\NostalgicRellm.SingleInstance", out bool isNew);
        if (!isNew) return;

        ApplicationConfiguration.Initialize();
        Application.Run(new MainBar());
    }

    private static bool IsAdministrator()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>Reabre o exe pedindo elevação. Se o usuário recusar o UAC, segue sem admin (sem temperatura da CPU).</summary>
    private static bool RelaunchElevated(string[] args)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(' ', args.Select(a => $"\"{a}\"")),
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }

    /// <summary>Grava em dump.txt tudo o que os sensores enxergam. Útil para diagnosticar hardware diferente.</summary>
    private static void Dump()
    {
        var sb = new StringBuilder();
        using var hw = new HardwareMonitor();
        hw.Open();
        hw.Sample();
        Thread.Sleep(1000);
        var s = hw.Sample();

        sb.AppendLine($"CPU {s.CpuLoad:0.0}%  {s.CpuTemp:0.0}°C");
        sb.AppendLine($"RAM {s.RamLoad:0.0}%  {s.RamUsedGb:0.0}/{s.RamTotalGb:0.0} GB");
        sb.AppendLine($"GPU {s.GpuName}: {s.GpuLoad:0.0}%  {s.GpuTemp:0.0}°C  VRAM {s.VramLoad:0.0}% ({s.VramUsedMb:0}/{s.VramTotalMb:0} MB)");
        foreach (var d in s.Disks)
            sb.AppendLine($"Disco {d.Label} ({d.Letter}) {d.Percent:0.0}%  livre {ProcessSampler.FormatBytes(d.FreeBytes)}");

        foreach (var kind in Enum.GetValues<ProcKind>())
        {
            sb.AppendLine($"--- Top 5 {kind}");
            foreach (var e in new ProcessSampler(kind).SampleTop())
                sb.AppendLine($"  {e.Name} x{e.Pids.Length}  {e.Text}");
        }

        sb.AppendLine("--- Sensores");
        foreach (var line in hw.DescribeSensors()) sb.AppendLine(line);

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "dump.txt"), sb.ToString());
    }
}
