using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using LibreHardwareMonitor.Hardware;

namespace TuringLite;

/// <summary>
/// Lecturas de sensores, sea de este proceso (<see cref="Sensors"/>, requiere administrador para CPU)
/// o del servicio de sensores (<see cref="RemoteSensors"/>, sin privilegios en la app).
/// El monitor, el editor y los widgets solo ven esta interfaz.
/// </summary>
public interface ISensorSource : IDisposable
{
    void Update();
    float? Get(string key);
    bool Has(string key);
    bool Track(string key);
    string? Canonical(string key);
    List<SensorInfo> Catalog();
    string DumpAll();
    void SetGpuFilter(string filter);
}

/// <summary>
/// Protocolo entre el servicio y la app: una línea de texto por petición y una línea JSON por respuesta,
/// por una tubería con nombre local. Solo lectura: no hay comandos que cambien nada.
/// </summary>
public static class SensorPipe
{
    public const string DefaultName = "TuringLite.Sensors";
    public const int MaxRequestBytes = 256;
    public const int MaxReplyBytes = 1 << 20;

    /// <summary>Alias de config.json que resuelve el servicio (los mismos que entiende <see cref="Sensors"/>).</summary>
    public static readonly string[] KnownAliases =
        { "cpu.temp", "cpu.load", "cpu.power", "gpu.temp", "gpu.load", "gpu.power", "ram.load", "ram.used" };

    public sealed record CatalogEntry(string Key, string Hardware, string HardwareType, string Name, string Type);
    public sealed record CatalogReply(List<CatalogEntry> Catalog);
    public sealed record SnapshotReply(Dictionary<string, float?> Values, Dictionary<string, string> Aliases);

    /// <summary>Solo letras, cifras y algunos signos, como mucho 64 caracteres: un filtro de GPU no necesita más.</summary>
    public static string CleanFilter(string? text) =>
        new((text ?? "").Where(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or '-' or '_' or '(' or ')').Take(64).ToArray());
}

/// <summary>
/// Lecturas del servicio de sensores. No abre hardware: pide por la tubería el último muestreo del servicio.
/// Si el servicio se para, los valores pasan a "sin dato" al cabo de <see cref="StaleAfter"/>.
/// </summary>
public sealed class RemoteSensors : ISensorSource
{
    static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(10);

    sealed record State(Dictionary<string, float?> Values, Dictionary<string, string> Aliases, DateTime At);
    static readonly State Empty = new(new(), new(), DateTime.MinValue);

    readonly string _pipe;
    string _gpu;
    volatile State _state = Empty;
    List<SensorInfo> _catalog = new();
    HashSet<string> _known = new();

    RemoteSensors(string pipe, string gpuFilter)
    {
        _pipe = pipe;
        _gpu = SensorPipe.CleanFilter(gpuFilter);
    }

    /// <summary>Conecta con el servicio, o devuelve null si no está en marcha. No tarda más de unos segundos.</summary>
    public static RemoteSensors? TryConnect(string gpuFilter, string pipe = SensorPipe.DefaultName)
    {
        try
        {
            var remote = new RemoteSensors(pipe, gpuFilter);
            remote.LoadCatalog();
            remote.Update();
            return remote;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>¿Responde el servicio? Sirve para decidir si hace falta elevar la app y para la barra de estado.</summary>
    public static bool IsRunning(string pipe = SensorPipe.DefaultName)
    {
        try { return Request(pipe, "PING", connectMs: 300, replyMs: 500) == "PONG"; }
        catch (Exception) { return false; }
    }

    static string Request(string pipe, string line, int connectMs = 500, int replyMs = 1500)
    {
        using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.None);
        client.Connect(connectMs);
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        client.Write(bytes, 0, bytes.Length);
        client.Flush();
        using var buffered = new BufferedStream(client, 4096);
        var reply = Task.Run(() => ReadLine(buffered));
        if (!reply.Wait(replyMs)) throw new TimeoutException("El servicio de sensores no respondió a tiempo.");
        return reply.Result;
    }

    static string ReadLine(Stream stream)
    {
        var bytes = new List<byte>(256);
        int b;
        while ((b = stream.ReadByte()) != -1 && b != '\n')
        {
            bytes.Add((byte)b);
            if (bytes.Count > SensorPipe.MaxReplyBytes) throw new IOException("Respuesta demasiado grande.");
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    void LoadCatalog()
    {
        var reply = JsonSerializer.Deserialize<SensorPipe.CatalogReply>(Request(_pipe, "CATALOG"))
                    ?? throw new InvalidDataException("Catálogo vacío.");
        _catalog = reply.Catalog.Select(e => new SensorInfo(e.Key, e.Hardware,
            Enum.Parse<HardwareType>(e.HardwareType), e.Name, Enum.Parse<SensorType>(e.Type))).ToList();
        _known = new HashSet<string>(_catalog.Select(c => c.Key));
    }

    public void Update()
    {
        try
        {
            if (_catalog.Count == 0) LoadCatalog();
            var reply = JsonSerializer.Deserialize<SensorPipe.SnapshotReply>(Request(_pipe, "SNAPSHOT " + _gpu))
                        ?? throw new InvalidDataException("Muestreo vacío.");
            _state = new State(reply.Values, reply.Aliases, DateTime.UtcNow);
        }
        catch (Exception)
        {
            // Se conserva la última lectura. Si el servicio no vuelve, Get() devuelve null al pasar StaleAfter.
        }
    }

    public float? Get(string key)
    {
        if (TotalPower.Is(key)) return TotalPower.Measured(this);
        var state = _state;
        if (DateTime.UtcNow - state.At > StaleAfter) return null;
        var id = Canonical(key);
        return id != null && state.Values.TryGetValue(id, out var value) ? value : null;
    }

    public bool Has(string key) => TotalPower.Is(key)
        ? Has("cpu.power") || Has("gpu.power")
        : Canonical(key) is { } id && _state.Values.ContainsKey(id);

    public bool Track(string key) => Canonical(key) != null;

    public string? Canonical(string key)
    {
        if (TotalPower.Is(key)) return TotalPower.Available(this) ? key : null;
        if (key.StartsWith('/')) return _known.Contains(key) ? key : null;
        return _state.Aliases.GetValueOrDefault(key);
    }

    public List<SensorInfo> Catalog() => _catalog.ToList();

    public string DumpAll()
    {
        if (_catalog.Count == 0) return "Sin datos del servicio de sensores.";
        var sb = new StringBuilder();
        foreach (var hw in _catalog.GroupBy(c => (c.HardwareType, c.HardwareName)))
        {
            sb.AppendLine($"[{hw.Key.HardwareType}] {hw.Key.HardwareName}");
            foreach (var s in hw.Where(s => s.Type is SensorType.Temperature or SensorType.Load or SensorType.Power or SensorType.Data))
            {
                var v = Get(s.Key);
                var text = v is float f ? f.ToString("0.#", CultureInfo.InvariantCulture) : "--";
                sb.AppendLine($"   {s.Type,-12} {s.Name,-28} {text}");
            }
        }
        return sb.ToString();
    }

    public void SetGpuFilter(string filter) => _gpu = SensorPipe.CleanFilter(filter);

    public void Dispose()
    {
        // No hay conexión persistente que cerrar: cada petición abre y cierra su tubería.
    }
}
