using LibreHardwareMonitor.Hardware;

namespace NostalgicRellm.Sensors;

internal sealed record DiskUsage(string Letter, string Label, float Percent, long TotalBytes, long FreeBytes);

/// <summary>Leitura imutável de todos os sensores num instante.</summary>
internal sealed class Snapshot
{
    public float? CpuLoad { get; init; }
    public float? CpuTemp { get; init; }
    public float RamLoad { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public float? GpuLoad { get; init; }
    public float? GpuTemp { get; init; }
    public float? VramLoad { get; init; }
    public float VramUsedMb { get; init; }
    public float VramTotalMb { get; init; }
    public string? GpuName { get; init; }
    public IReadOnlyList<DiskUsage> Disks { get; init; } = [];
}

/// <summary>
/// Coleta CPU/GPU via LibreHardwareMonitor, RAM via Win32 e espaço dos discos via DriveInfo.
/// Deve ser usado sempre a partir da mesma thread.
/// </summary>
internal sealed class HardwareMonitor : IDisposable
{
    // Nomes de sensor de temperatura da CPU por ordem de preferência (AMD e Intel)
    private static readonly string[] CpuTempNames =
        ["Core (Tctl/Tdie)", "Core (Tdie)", "Core (Tctl)", "CPU Package", "Package", "Core Max", "Core Average"];

    private readonly Computer _computer = new() { IsCpuEnabled = true, IsGpuEnabled = true };
    private IHardware? _cpu, _gpu;
    private ISensor? _cpuLoad, _cpuTemp, _gpuLoad, _gpuTemp, _vramUsed, _vramTotal;

    private IReadOnlyList<DiskUsage> _disks = [];
    private DateTime _disksAt = DateTime.MinValue;

    public void Open()
    {
        _computer.Open();
        _cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        // GPU dedicada tem prioridade sobre a integrada
        _gpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd)
            ?? _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.GpuIntel);

        _cpu?.Update();
        _gpu?.Update();

        if (_cpu != null)
        {
            _cpuLoad = Find(_cpu, SensorType.Load, "CPU Total");
            _cpuTemp = CpuTempNames.Select(n => Find(_cpu, SensorType.Temperature, n)).FirstOrDefault(s => s != null)
                ?? _cpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
        }
        if (_gpu != null)
        {
            _gpuLoad = Find(_gpu, SensorType.Load, "GPU Core") ?? Find(_gpu, SensorType.Load, "D3D 3D");
            _gpuTemp = Find(_gpu, SensorType.Temperature, "GPU Core")
                ?? _gpu.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature);
            _vramUsed = Find(_gpu, SensorType.SmallData, "GPU Memory Used")
                ?? Find(_gpu, SensorType.SmallData, "D3D Dedicated Memory Used");
            _vramTotal = Find(_gpu, SensorType.SmallData, "GPU Memory Total")
                ?? Find(_gpu, SensorType.SmallData, "D3D Dedicated Memory Total");
        }
    }

    private static ISensor? Find(IHardware hw, SensorType type, string name) =>
        hw.Sensors.FirstOrDefault(s => s.SensorType == type && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public Snapshot Sample()
    {
        _cpu?.Update();
        _gpu?.Update();

        var mem = Native.GetMemoryStatus();
        double totalGb = mem.ullTotalPhys / 1073741824.0;
        double usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;

        float? vramUsed = _vramUsed?.Value, vramTotal = _vramTotal?.Value;
        float? vramLoad = vramUsed is > 0 && vramTotal is > 0 ? vramUsed / vramTotal * 100f : null;

        // Espaço em disco muda devagar: relê a cada 10 s
        if (DateTime.UtcNow - _disksAt > TimeSpan.FromSeconds(10))
        {
            _disks = ReadDisks();
            _disksAt = DateTime.UtcNow;
        }

        return new Snapshot
        {
            CpuLoad = _cpuLoad?.Value,
            CpuTemp = _cpuTemp?.Value is > 0 and var t ? t : null,
            RamLoad = (float)(usedGb / totalGb * 100.0),
            RamUsedGb = usedGb,
            RamTotalGb = totalGb,
            GpuLoad = _gpuLoad?.Value,
            GpuTemp = _gpuTemp?.Value is > 0 and var gt ? gt : null,
            VramLoad = vramLoad,
            VramUsedMb = vramUsed ?? 0,
            VramTotalMb = vramTotal ?? 0,
            GpuName = _gpu?.Name,
            Disks = _disks,
        };
    }

    private static List<DiskUsage> ReadDisks()
    {
        var list = new List<DiskUsage>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                long total = d.TotalSize, free = d.TotalFreeSpace;
                if (total <= 0) continue;
                string letter = d.Name.TrimEnd('\\');
                list.Add(new DiskUsage(letter, d.VolumeLabel, (float)((total - free) * 100.0 / total), total, free));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }

    /// <summary>Lista todos os sensores encontrados (para diagnóstico com --dump).</summary>
    public IEnumerable<string> DescribeSensors()
    {
        foreach (var hw in _computer.Hardware)
        {
            hw.Update();
            yield return $"[{hw.HardwareType}] {hw.Name}";
            foreach (var s in hw.Sensors)
                yield return $"    {s.SensorType,-12} {s.Name,-32} {s.Value}";
        }
    }

    public void Dispose() => _computer.Close();
}
