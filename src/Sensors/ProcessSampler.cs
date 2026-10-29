using System.Diagnostics;
using System.Text.RegularExpressions;

namespace NostalgicRellm.Sensors;

internal enum ProcKind { Cpu, Ram, Gpu, Vram }

/// <summary>Um processo (ou grupo de processos de mesmo nome) no ranking.</summary>
internal sealed record ProcEntry(string Name, int[] Pids, double Value, string Text);

/// <summary>
/// Mede consumo por processo. Só é instanciado enquanto o popup está aberto,
/// então não custa nada no resto do tempo. Processos com o mesmo nome são agrupados.
/// </summary>
internal sealed partial class ProcessSampler(ProcKind kind)
{
    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase) { "Idle", "_Total" };

    private Dictionary<int, TimeSpan> _cpuPrev = [];
    private long _cpuPrevTicks;
    private Dictionary<string, CounterSample> _gpuPrev = [];

    public ProcKind Kind { get; } = kind;

    /// <summary>Primeira leitura de métricas baseadas em diferença (CPU e GPU) precisa de duas amostras.</summary>
    public List<ProcEntry> SampleTop(int count = 5)
    {
        if (Kind is ProcKind.Cpu or ProcKind.Gpu && !HasBaseline)
        {
            Collect();
            Thread.Sleep(500);
        }
        return Collect().OrderByDescending(e => e.Value).Where(e => e.Value > 0).Take(count).ToList();
    }

    private bool HasBaseline => Kind == ProcKind.Cpu ? _cpuPrevTicks != 0 : _gpuPrev.Count > 0;

    private List<ProcEntry> Collect() => Kind switch
    {
        ProcKind.Cpu => CollectCpu(),
        ProcKind.Ram => CollectRam(),
        ProcKind.Gpu => CollectGpu(),
        _ => CollectVram(),
    };

    private List<ProcEntry> CollectCpu()
    {
        long now = Stopwatch.GetTimestamp();
        double elapsedMs = _cpuPrevTicks == 0 ? 0 : Stopwatch.GetElapsedTime(_cpuPrevTicks, now).TotalMilliseconds;
        var current = new Dictionary<int, TimeSpan>();
        var perPid = new Dictionary<int, (string name, double pct)>();

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id == 0 || Hidden.Contains(p.ProcessName)) continue;
                try
                {
                    var t = p.TotalProcessorTime;
                    current[p.Id] = t;
                    if (elapsedMs > 0 && _cpuPrev.TryGetValue(p.Id, out var prev))
                        perPid[p.Id] = (p.ProcessName, (t - prev).TotalMilliseconds / elapsedMs / Environment.ProcessorCount * 100.0);
                }
                catch { /* processo protegido ou encerrado */ }
            }
        }
        _cpuPrev = current;
        _cpuPrevTicks = now;
        return Group(perPid, v => $"{v:0.0}%");
    }

    private static List<ProcEntry> CollectRam()
    {
        var perPid = new Dictionary<int, (string name, double bytes)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id == 0 || Hidden.Contains(p.ProcessName)) continue;
                try { perPid[p.Id] = (p.ProcessName, p.WorkingSet64); } catch { }
            }
        }
        return Group(perPid, FormatBytes);
    }

    [GeneratedRegex(@"pid_(\d+)_.*engtype_(.+)$")]
    private static partial Regex GpuEngineRegex();

    [GeneratedRegex(@"pid_(\d+)_")]
    private static partial Regex PidRegex();

    private List<ProcEntry> CollectGpu()
    {
        var current = new Dictionary<string, CounterSample>();
        // (pid, tipo de motor) -> soma; o valor do processo é o maior tipo de motor, como no Gerenciador de Tarefas
        var perEngine = new Dictionary<(int pid, string eng), double>();
        try
        {
            var data = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
            if (data != null)
            {
                foreach (InstanceData inst in data.Values)
                {
                    current[inst.InstanceName] = inst.Sample;
                    if (!_gpuPrev.TryGetValue(inst.InstanceName, out var prev)) continue;
                    var m = GpuEngineRegex().Match(inst.InstanceName);
                    if (!m.Success) continue;
                    float v = CounterSample.Calculate(prev, inst.Sample);
                    if (v <= 0) continue;
                    var key = (int.Parse(m.Groups[1].Value), m.Groups[2].Value);
                    perEngine[key] = perEngine.GetValueOrDefault(key) + v;
                }
            }
        }
        catch (Exception) { /* contadores de GPU indisponíveis */ }
        _gpuPrev = current;

        var names = ProcessNames();
        var perPid = perEngine
            .GroupBy(kv => kv.Key.pid)
            .Where(g => names.ContainsKey(g.Key))
            .ToDictionary(g => g.Key, g => (names[g.Key], Math.Min(100.0, g.Max(kv => kv.Value))));
        return Group(perPid, v => $"{v:0.0}%", capAt100: true);
    }

    private static List<ProcEntry> CollectVram()
    {
        var bytesPerPid = new Dictionary<int, double>();
        try
        {
            var data = new PerformanceCounterCategory("GPU Process Memory").ReadCategory()["Dedicated Usage"];
            if (data != null)
            {
                foreach (InstanceData inst in data.Values)
                {
                    var m = PidRegex().Match(inst.InstanceName);
                    if (!m.Success) continue;
                    int pid = int.Parse(m.Groups[1].Value);
                    bytesPerPid[pid] = bytesPerPid.GetValueOrDefault(pid) + inst.RawValue;
                }
            }
        }
        catch (Exception) { }

        var names = ProcessNames();
        var perPid = bytesPerPid
            .Where(kv => names.ContainsKey(kv.Key))
            .ToDictionary(kv => kv.Key, kv => (names[kv.Key], kv.Value));
        return Group(perPid, FormatBytes);
    }

    private static Dictionary<int, string> ProcessNames()
    {
        var map = new Dictionary<int, string>();
        foreach (var p in Process.GetProcesses())
            using (p) map[p.Id] = p.ProcessName;
        return map;
    }

    private static List<ProcEntry> Group(Dictionary<int, (string name, double value)> perPid, Func<double, string> format, bool capAt100 = false) =>
        perPid
            .GroupBy(kv => kv.Value.name, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                double sum = g.Sum(kv => kv.Value.value);
                if (capAt100) sum = Math.Min(100.0, sum);
                return new ProcEntry(g.Key, g.Select(kv => kv.Key).ToArray(), sum, format(sum));
            })
            .ToList();

    public static string FormatBytes(double bytes) => bytes switch
    {
        >= 1073741824 => $"{bytes / 1073741824:0.0} GB",
        >= 1048576 => $"{bytes / 1048576:0} MB",
        _ => $"{bytes / 1024:0} KB",
    };

    // Processos cujo encerramento derruba o Windows ou é sempre negado
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Registry", "Memory Compression", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "lsaiso", "svchost", "dwm", "fontdrvhost", "Secure System", "MsMpEng",
    };

    public static bool CanKill(ProcEntry e) =>
        !Critical.Contains(e.Name) && !e.Pids.Contains(Environment.ProcessId);

    /// <summary>Finaliza todos os processos do grupo. Retorna quantos foram encerrados.</summary>
    public static int Kill(ProcEntry e)
    {
        int killed = 0;
        foreach (int pid in e.Pids)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                p.Kill();
                killed++;
            }
            catch { }
        }
        return killed;
    }
}
