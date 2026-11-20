using LibreHardwareMonitor.Hardware;

namespace TuringLite;

/// <summary>Una lectura disponible en este PC, lista para el selector del editor.</summary>
public sealed record SensorInfo(string Key, string HardwareName, HardwareType HardwareType, string Name, SensorType Type)
{
    public string Unit => Sensors.DefaultUnit(Type);
    /// <summary>Texto para listas: "CPU · CPU Package (Temperatura)".</summary>
    public string Display => $"{HardwareName} · {Name} ({Sensors.TypeLabel(Type)})";
}

/// <summary>
/// Lectura de sensores. Cada sensor tiene una clave estable con el mismo formato que LibreHardwareMonitor
/// (p. ej. "/amdcpu/0/temperature/2"). Las claves antiguas ("cpu.temp", "gpu.load"...) siguen funcionando
/// como alias: se resuelven con heurísticas al arrancar. Todo el acceso está protegido con un lock porque
/// el hilo de monitoreo y el editor de componentes leen y escriben a la vez.
/// </summary>
public sealed class Sensors : ISensorSource
{
    readonly object _lock = new();
    Computer? _pc;
    readonly Dictionary<string, ISensor> _map = new();     // clave -> sensor, solo los que se usan
    readonly Dictionary<string, ISensor> _all = new();     // clave -> sensor, todo el catálogo
    readonly List<IHardware> _active = new();
    string _gpuFilter;
    bool _disposed;

    public Sensors(IEnumerable<string> keys, string gpuFilter = "")
    {
        _gpuFilter = gpuFilter;
        _pc = new Computer
        {
            IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
            IsMotherboardEnabled = true, IsStorageEnabled = true,
        };
        _pc.Open();
        BuildCatalog();
        foreach (var key in keys) Track(key);
    }

    static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> list)
    {
        foreach (var h in list)
        {
            yield return h;
            foreach (var sub in Flatten(h.SubHardware)) yield return sub;
        }
    }

    void BuildCatalog()
    {
        foreach (var hw in Flatten(_pc!.Hardware))
        {
            hw.Update(); // primera lectura para poblar los sensores
            foreach (var s in hw.Sensors)
                _all[KeyOf(hw, s)] = s;
        }
    }

    /// <summary>Clave estable: mismo formato que Identifier de LHM (hardware + tipo + índice).</summary>
    public static string KeyOf(IHardware hw, ISensor s) =>
        $"{hw.Identifier}/{s.SensorType.ToString().ToLowerInvariant()}/{s.Index}";

    /// <summary>Catálogo completo de lecturas detectadas en este PC.</summary>
    public List<SensorInfo> Catalog()
    {
        lock (_lock)
        {
            if (_disposed) return new();
            return _all.Select(kv => new SensorInfo(kv.Key, kv.Value.Hardware.Name,
                    kv.Value.Hardware.HardwareType, kv.Value.Name, kv.Value.SensorType))
                .OrderBy(i => i.HardwareName).ThenBy(i => i.Type).ThenBy(i => i.Name)
                .ToList();
        }
    }

    /// <summary>
    /// Asegura que una clave (identificador o alias) esté lista para leerse. Devuelve false si el PC no
    /// tiene ese sensor. Activa el hardware correspondiente para que se actualice en cada ciclo.
    /// </summary>
    public bool Track(string key)
    {
        if (TotalPower.Is(key)) return TotalPower.Track(this);
        lock (_lock)
        {
            if (_disposed || _pc == null) return false;
            if (_map.ContainsKey(key)) return true;
            var s = key.StartsWith('/') ? (_all.TryGetValue(key, out var f) ? f : null) : ResolveAlias(key);
            if (s == null) return false;
            _map[key] = s;
            if (!_active.Contains(s.Hardware)) _active.Add(s.Hardware);
            return true;
        }
    }

    /// <summary>Identificador real al que apunta un alias antiguo, o la propia clave si ya es un identificador.</summary>
    public string? Canonical(string key)
    {
        if (TotalPower.Is(key)) return TotalPower.Available(this) ? key : null;
        lock (_lock)
        {
            if (key.StartsWith('/')) return _all.ContainsKey(key) ? key : null;
            var s = ResolveAlias(key);
            return s == null ? null : KeyOf(s.Hardware, s);
        }
    }

    ISensor? ResolveAlias(string key)
    {
        // Las GPU dedicadas van primero; la integrada solo se usa si no hay otra.
        var gpus = _all.Values.Select(s => s.Hardware).Distinct()
            .OrderByDescending(h => GpuScore(h, _gpuFilter));
        var all = _all.Values.Select(s => s.Hardware).Distinct();
        foreach (var hw in key.StartsWith("gpu.") ? gpus : all)
        {
            var s = Resolve(key, hw);
            if (s != null) return s;
        }
        return null;
    }

    static int GpuScore(IHardware h, string filter)
    {
        if (!IsGpu(h.HardwareType)) return 0;
        if (filter.Length > 0) return h.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ? 100 : -1;
        // Las integradas suelen llamarse "AMD Radeon(TM) Graphics", "Intel(R) UHD Graphics"...
        bool integrated = h.Name.TrimEnd().EndsWith("Graphics", StringComparison.OrdinalIgnoreCase);
        int score = integrated ? 1 : 10;
        if (h.HardwareType == HardwareType.GpuNvidia) score += 5;
        return score;
    }

    static bool IsGpu(HardwareType t) => t is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    static ISensor? Pick(IHardware hw, SensorType type, params string[] names)
    {
        foreach (var n in names)
        {
            var s = hw.Sensors.FirstOrDefault(x => x.SensorType == type && x.Name == n);
            if (s != null) return s;
        }
        return null;
    }

    static ISensor? Resolve(string key, IHardware hw) => key switch
    {
        "cpu.temp" when hw.HardwareType == HardwareType.Cpu =>
            Pick(hw, SensorType.Temperature, "CPU Package", "Core (Tctl/Tdie)", "Core (Tdie)", "Core Max", "Core Average")
            ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature),
        "cpu.load" when hw.HardwareType == HardwareType.Cpu => Pick(hw, SensorType.Load, "CPU Total"),
        "cpu.power" when hw.HardwareType == HardwareType.Cpu => Pick(hw, SensorType.Power, "CPU Package", "Package"),
        "gpu.temp" when IsGpu(hw.HardwareType) =>
            Pick(hw, SensorType.Temperature, "GPU Core")
            ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Temperature),
        "gpu.load" when IsGpu(hw.HardwareType) =>
            Pick(hw, SensorType.Load, "GPU Core", "D3D 3D", "GPU", "D3D Graphics_1")
            ?? hw.Sensors.FirstOrDefault(s => s.SensorType == SensorType.Load),
        "gpu.power" when IsGpu(hw.HardwareType) => Pick(hw, SensorType.Power, "GPU Package", "GPU Core"),
        // Solo la RAM física ("/ram"): "/vram" es la memoria virtual (archivo de paginación) y tiene sensores con el mismo nombre.
        "ram.load" when hw.Identifier.ToString() == "/ram" => Pick(hw, SensorType.Load, "Memory"),
        "ram.used" when hw.Identifier.ToString() == "/ram" => Pick(hw, SensorType.Data, "Memory Used"),
        _ => null,
    };

    public void Update()
    {
        lock (_lock)
        {
            if (_disposed) return;
            foreach (var hw in _active) hw.Update();
        }                                                                                                   
    }

    public float? Get(string key)
    {
        if (TotalPower.Is(key)) return TotalPower.Measured(this);
        lock (_lock)
        {
            if (_disposed || !_map.TryGetValue(key, out var s)) return null;
            // LHM devuelve 0 cuando no puede leer un sensor (p. ej. la temperatura de CPU sin administrador).
            // Un 0 °C o 0 W no es una lectura real: se trata como "sin dato" y se muestra "--".
            if (s.Value == 0 && s.SensorType is SensorType.Temperature or SensorType.Power) return null;
            return s.Value;
        }
    }

    public bool Has(string key)
    {
        if (TotalPower.Is(key)) return Has("cpu.power") || Has("gpu.power");
        lock (_lock) return _map.ContainsKey(key);
    }

    /// <summary>Filtro de GPU para resolver los alias (cpu.temp, gpu.load...). Lo usa el servicio, que lo recibe por petición.</summary>
    public void SetGpuFilter(string filter)
    {
        lock (_lock) _gpuFilter = filter;
    }

    /// <summary>Lista todo lo que LibreHardwareMonitor ve (últimos valores leídos).</summary>
    public string DumpAll()
    {
        lock (_lock)
        {
            if (_disposed || _pc == null) return "Sensores no disponibles.";
            var sb = new System.Text.StringBuilder();
            foreach (var hw in Flatten(_pc.Hardware))
            {
                sb.AppendLine($"[{hw.HardwareType}] {hw.Name}");
                foreach (var s in hw.Sensors.Where(s => s.SensorType is SensorType.Temperature or SensorType.Load or SensorType.Power or SensorType.Data))
                    sb.AppendLine($"   {s.SensorType,-12} {s.Name,-28} {s.Value:0.#}");
            }
            return sb.ToString();
        }
    }

    public static string DefaultUnit(SensorType t) => t switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load or SensorType.Control or SensorType.Level => "%",
        SensorType.Power => " W",
        SensorType.Energy => " Wh",
        SensorType.Voltage => " V",
        SensorType.Current => " A",
        SensorType.Clock => " MHz",
        SensorType.Frequency => " Hz",
        SensorType.Fan => " RPM",
        SensorType.Flow => " L/h",
        SensorType.Data => " GB",
        SensorType.SmallData => " MB",
        SensorType.Throughput => " B/s",
        _ => "",
    };

    public static string TypeLabel(SensorType t) => t switch
    {
        SensorType.Temperature => "Temperatura",
        SensorType.Load => "Carga",
        SensorType.Power => "Potencia",
        SensorType.Clock => "Reloj",
        SensorType.Fan => "Ventilador",
        SensorType.Voltage => "Voltaje",
        SensorType.Data => "Datos",
        SensorType.SmallData => "Datos",
        SensorType.Throughput => "Velocidad",
        _ => t.ToString(),
    };

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _pc?.Close();
            _pc = null;
            _map.Clear();
            _all.Clear();
            _active.Clear();
        }
    }
}
