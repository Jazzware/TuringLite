using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace TuringLite;

/// <summary>
/// Núcleo del servicio de sensores. Lee LibreHardwareMonitor (con privilegios de sistema) cada 2 s y sirve el último
/// muestreo por una tubería local que los usuarios solo pueden leer. No escribe nada ni acepta configuración.
/// </summary>
public sealed class SensorHost : IDisposable
{
    static readonly TimeSpan Period = TimeSpan.FromSeconds(2);
    static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    readonly Func<ISensorSource> _factory;
    readonly string _pipe;
    readonly object _gate = new();
    ISensorSource? _src;
    string _gpu = "";
    string[] _ids = Array.Empty<string>();
    string _catalogJson = "{\"Catalog\":[]}";
    Dictionary<string, float?> _values = new();
    CancellationTokenSource? _cts;
    Task[] _tasks = Array.Empty<Task>();

    public SensorHost(Func<ISensorSource> factory, string pipe = SensorPipe.DefaultName)
    {
        _factory = factory;
        _pipe = pipe;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _tasks = new[] { Task.Run(() => UpdateLoop(_cts.Token)), Task.Run(() => ServeLoop(_cts.Token)) };
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { Task.WaitAll(_tasks, TimeSpan.FromSeconds(5)); }
        catch (AggregateException) { }
        ISensorSource? src;
        lock (_gate) { src = _src; _src = null; }
        try { src?.Dispose(); }
        catch (Exception) { }
    }

    public void Dispose() => Stop();

    async Task UpdateLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var wait = Period;
            try
            {
                ISensorSource? src;
                lock (_gate) src = _src;
                if (src == null)
                {
                    src = CreateSource();
                    lock (_gate) _src = src;
                }
                src.Update();
                var ids = _ids;
                var fresh = new Dictionary<string, float?>(ids.Length);
                foreach (var id in ids) fresh[id] = src.Get(id);
                lock (_gate) _values = fresh;
            }
            catch (Exception)
            {
                // Un fallo (p. ej. el controlador no carga) no tumba el servicio: se reintenta más tarde.
                ISensorSource? broken;
                lock (_gate) { broken = _src; _src = null; }
                try { broken?.Dispose(); }
                catch (Exception) { }
                wait = RetryAfterFailure;
            }
            try { await Task.Delay(wait, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Abre los sensores y registra todo el catálogo: así cada identificador tiene lectura.</summary>
    ISensorSource CreateSource()
    {
        var src = _factory();
        var catalog = src.Catalog();
        foreach (var c in catalog) src.Track(c.Key);
        var json = JsonSerializer.Serialize(new SensorPipe.CatalogReply(catalog.Select(c => new SensorPipe.CatalogEntry(
            c.Key, c.HardwareName, c.HardwareType.ToString(), c.Name, c.Type.ToString())).ToList()));
        lock (_gate)
        {
            _ids = catalog.Select(c => c.Key).ToArray();
            _catalogJson = json;
        }
        return src;
    }

    async Task ServeLoop(CancellationToken ct)
    {
        var security = BuildSecurity();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Búferes de 4 KB: con 0 bytes, un cliente que escribe a la vez que el servidor responde se corta.
                using var server = NamedPipeServerStreamAcl.Create(_pipe, PipeDirection.InOut, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
                await server.WaitForConnectionAsync(ct);
                await HandleAsync(server, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Cliente que se corta a mitad o tubería ocupada: se atiende al siguiente sin girar en vacío.
                try { await Task.Delay(500, ct); }
                catch (OperationCanceledException) { return; }
            }
        }
    }

    async Task HandleAsync(Stream pipe, CancellationToken ct)
    {
        // Un cliente que no envía nada no puede bloquear el servicio: se corta a los 2 s.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(2));
        var line = await ReadRequestAsync(pipe, limit.Token);
        var bytes = Encoding.UTF8.GetBytes(Respond(line) + "\n");
        await pipe.WriteAsync(bytes, limit.Token);
        await pipe.FlushAsync(limit.Token);
    }

    static async Task<string> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        var one = new byte[1];
        var buffer = new List<byte>(64);
        while (true)
        {
            int n = await stream.ReadAsync(one, ct);
            if (n == 0 || one[0] == (byte)'\n') break;
            if (buffer.Count >= SensorPipe.MaxRequestBytes) return "TOO_LONG";
            buffer.Add(one[0]);
        }
        return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r');
    }

    string Respond(string line)
    {
        try
        {
            if (line == "PING") return "PONG";
            if (line == "TOO_LONG") return "ERR";
            if (line == "CATALOG")
                lock (_gate) return _catalogJson;
            if (line == "SNAPSHOT" || line.StartsWith("SNAPSHOT ", StringComparison.Ordinal))
            {
                var filter = SensorPipe.CleanFilter(line.Length > 9 ? line[9..] : "");
                lock (_gate)
                {
                    var src = _src;
                    if (src != null && filter != _gpu)
                    {
                        src.SetGpuFilter(filter);
                        _gpu = filter;
                    }
                    var aliases = new Dictionary<string, string>();
                    if (src != null)
                        foreach (var alias in SensorPipe.KnownAliases)
                            if (src.Canonical(alias) is { } id) aliases[alias] = id;
                    return JsonSerializer.Serialize(new SensorPipe.SnapshotReply(_values, aliases));
                }
            }
            return "ERR";
        }
        catch (Exception)
        {
            return "ERR";
        }
    }

    /// <summary>
    /// Lectura para todos los usuarios conectados y administración solo para administradores y el propio sistema.
    /// Un usuario normal no puede escribir ni cambiar la tubería.
    /// </summary>
    static PipeSecurity BuildSecurity()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }
}

/// <summary>
/// Servicio de Windows "TuringLiteSensors": arranca con el equipo, antes de iniciar sesión. Se hospeda con llamadas
/// directas a advapi32 (sin paquetes externos). Solo responde al gestor de servicios: si se abre con doble clic, no hace nada.
/// </summary>
public static class SensorWindowsService
{
    public const string ServiceId = "TuringLiteSensors";

    const int ServiceWin32OwnProcess = 0x10;
    const int StateStopped = 1, StateStartPending = 2, StateStopPending = 3, StateRunning = 4;
    const int AcceptStop = 0x1, AcceptShutdown = 0x4;
    const int ControlStop = 0x1, ControlShutdown = 0x5;

    [StructLayout(LayoutKind.Sequential)]
    struct ServiceStatus
    {
        public int ServiceType, CurrentState, ControlsAccepted, Win32ExitCode, ServiceSpecificExitCode, CheckPoint, WaitHint;
    }

    delegate void ServiceMainDelegate(int argc, IntPtr argv);
    delegate void HandlerDelegate(int control);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ServiceTableEntry
    {
        public string? ServiceName;
        public ServiceMainDelegate? ServiceProc;
    }

    [DllImport("advapi32.dll", EntryPoint = "StartServiceCtrlDispatcherW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool StartServiceCtrlDispatcher(ServiceTableEntry[] table);

    [DllImport("advapi32.dll", EntryPoint = "RegisterServiceCtrlHandlerW", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr RegisterServiceCtrlHandler(string name, HandlerDelegate handler);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool SetServiceStatus(IntPtr handle, ref ServiceStatus status);

    // Los delegados se guardan en campos: si el recolector los liberase, Windows llamaría a código ya borrado.
    static ServiceMainDelegate? _serviceMain;
    static HandlerDelegate? _handler;
    static IntPtr _statusHandle;
    static ServiceStatus _status;
    static int _checkPoint;
    static SensorHost? _host;
    static readonly ManualResetEventSlim Done = new(false);

    /// <summary>Entra en el ciclo del gestor de servicios. Devuelve false si no lo ha iniciado el gestor.</summary>
    public static bool Run()
    {
        _serviceMain = ServiceMain;
        var table = new[]
        {
            new ServiceTableEntry { ServiceName = ServiceId, ServiceProc = _serviceMain },
            new ServiceTableEntry(), // entrada final vacía, obligatoria
        };
        return StartServiceCtrlDispatcher(table);
    }

    static void ServiceMain(int argc, IntPtr argv)
    {
        _handler = Control;
        _statusHandle = RegisterServiceCtrlHandler(ServiceId, _handler);
        SetState(StateStartPending, 0, 3000);
        _host = new SensorHost(() => new Sensors(Array.Empty<string>(), ""));
        _host.Start();
        SetState(StateRunning, AcceptStop | AcceptShutdown, 0);
        Done.Wait();
    }

    static void Control(int control)
    {
        if (control is not (ControlStop or ControlShutdown)) return;
        SetState(StateStopPending, 0, 10000);
        var host = _host;
        _host = null;
        host?.Dispose();
        SetState(StateStopped, 0, 0);
        Done.Set();
    }

    static void SetState(int state, int accepted, int waitHintMs)
    {
        if (_statusHandle == IntPtr.Zero) return;
        _status.ServiceType = ServiceWin32OwnProcess;
        _status.CurrentState = state;
        _status.ControlsAccepted = accepted;
        _status.WaitHint = waitHintMs;
        _status.CheckPoint = state is StateStartPending or StateStopPending ? ++_checkPoint : 0;
        SetServiceStatus(_statusHandle, ref _status);
    }
}

/// <summary>
/// Instala, actualiza o quita el servicio. Requiere administrador: la app lo lanza elevada con un único aviso de UAC.
/// El servicio se copia a Program Files, donde solo un administrador puede reemplazar el ejecutable.
/// </summary>
public static class ServiceInstaller
{
    static readonly string[] NotCopied = { "config.json", "status.txt" };

    public static string InstallDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TuringLite");

    public static bool IsInstalled() => Sc(out _, "query", SensorWindowsService.ServiceId) == 0;

    public static int Install(out string message)
    {
        if (!Elevation.IsElevated())
        {
            message = "Instalar el servicio requiere administrador.";
            return 5;
        }
        var source = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var dest = InstallDir;
        try
        {
            bool existed = IsInstalled();
            if (existed) StopAndWait();
            CopyApp(source, dest);
            var binPath = $"\"{Path.Combine(dest, "TuringLite.exe")}\" --service";
            int rc = existed
                ? Sc(out _, "config", SensorWindowsService.ServiceId, "binPath=", binPath, "start=", "auto", "DisplayName=", "TuringLite sensores")
                : Sc(out _, "create", SensorWindowsService.ServiceId, "binPath=", binPath, "start=", "auto", "DisplayName=", "TuringLite sensores");
            if (rc != 0) throw new InvalidOperationException($"sc.exe devolvió {rc}.");
            Sc(out _, "description", SensorWindowsService.ServiceId, "Lee temperaturas y cargas del equipo para TuringLite.");
            Sc(out _, "failure", SensorWindowsService.ServiceId, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/300000");
            Sc(out _, "start", SensorWindowsService.ServiceId); // 1056 = ya estaba en marcha: no es un error
            message = $"Servicio de sensores instalado en:\n{dest}\n\n" +
                      "La temperatura de CPU ya no depende de abrir la app con administrador. La app de bandeja sigue sin privilegios.";
            return 0;
        }
        catch (Exception ex)
        {
            message = "No se pudo instalar el servicio de sensores:\n" + ex.Message;
            return 1;
        }
    }

    public static int Uninstall(out string message)
    {
        if (!Elevation.IsElevated())
        {
            message = "Quitar el servicio requiere administrador.";
            return 5;
        }
        try
        {
            if (IsInstalled())
            {
                StopAndWait();
                Sc(out _, "delete", SensorWindowsService.ServiceId);
            }
            var dest = InstallDir;
            var running = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.Exists(dest) && !string.Equals(dest, running, StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(dest, recursive: true); }
                catch (IOException) { /* algún archivo en uso: el resto ya no se usa */ }
            }
            message = "Servicio de sensores quitado. La temperatura de CPU volverá a requerir abrir la app con administrador.";
            return 0;
        }
        catch (Exception ex)
        {
            message = "No se pudo quitar el servicio:\n" + ex.Message;
            return 1;
        }
    }

    static void StopAndWait()
    {
        Sc(out _, "stop", SensorWindowsService.ServiceId);
        for (int i = 0; i < 30; i++)
        {
            Sc(out var text, "query", SensorWindowsService.ServiceId);
            if (text.Contains("STOPPED", StringComparison.OrdinalIgnoreCase)) return;
            Thread.Sleep(500);
        }
    }

    static void CopyApp(string source, string dest)
    {
        if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase)) return;
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (NotCopied.Contains(name, StringComparer.OrdinalIgnoreCase)
                || name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>Ejecuta sc.exe con argumentos separados (sin concatenar en una línea de comandos).</summary>
    static int Sc(out string output, params string[] args)
    {
        var psi = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        output = stdout + stderr;
        return p.ExitCode;
    }
}
