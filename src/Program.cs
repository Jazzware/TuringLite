using System.Diagnostics;

namespace TuringLite;

static class Program
{
    /// <summary>
    /// Sin argumentos: abre la ventana de ajustes. Con --tray: arranca solo en la bandeja
    /// (es lo que usa el inicio con Windows, sin privilegios elevados). --config ruta: usa otro archivo de configuración.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        // Servicio de sensores: lo arranca Windows, sin interfaz.
        if (args.Contains("--service"))
        {
            SensorWindowsService.Run();
            return;
        }
        // Instalación, desinstalación y autoarranque: el código de salida dice si funcionó (lo leen el instalador y la app).
        if (args.Contains("--install-service") || args.Contains("--uninstall-service"))
        {
            bool install = args.Contains("--install-service");
            string message;
            int code = install ? ServiceInstaller.Install(out message) : ServiceInstaller.Uninstall(out message);
            if (!args.Contains("--quiet")) ReportServiceResult(code, message);
            Environment.Exit(code);
            return;
        }
        if (args.Contains("--autostart-on") || args.Contains("--autostart-off"))
        {
            bool ok = args.Contains("--autostart-on") ? AutoStart.Enable() : AutoStart.Disable();
            Environment.Exit(ok ? 0 : 1);
            return;
        }
        // Segunda pasada de huellas, minutos después de desinstalar (copia temporal de la app).
        if (args.Contains(DelayedTraces.Flag))
        {
            DelayedTraces.Run(args);
            return;
        }
        // Trabajo elevado del desinstalador (Prefetch y servicio): un solo aviso de UAC para todo.
        if (args.Contains("--elevated-cleanup"))
        {
            Environment.Exit(SystemCleanup.Run(args));
            return;
        }
        // Desinstalación: desde "Aplicaciones instaladas" (con ventana) o con --silent.
        if (args.Contains("--uninstall"))
        {
            Uninstaller.Entry(args);
            return;
        }

        // La temperatura de CPU necesita administrador, salvo que la lea el servicio de sensores. Si no hay servicio y
        // abres la app a mano, nos relanzamos elevados (aviso de UAC). El inicio con Windows (--tray) no se eleva.
        if (!Elevation.IsElevated() && !args.Contains("--no-elevate") && !args.Contains("--tray")
            && !RemoteSensors.IsRunning() && TryRelaunchElevated(args)) return;

        using var mutex = new Mutex(true, @"Global\TuringLite", out bool first);
        if (!first)
        {
            // Un inicio automático con la app ya abierta sale en silencio: no hay nada que mostrar.
            if (args.Contains("--tray")) return;
            MessageBox.Show("TuringLite ya se está ejecutando. Búscalo en la bandeja del sistema, junto al reloj.",
                "TuringLite", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Prioridad baja: la pantalla puede esperar unos milisegundos, tus juegos no.
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        string cfgPath = args.SkipWhile(a => a != "--config").Skip(1).FirstOrDefault()
                         ?? Path.Combine(AppContext.BaseDirectory, "config.json");
        MonitorService svc;
        try { svc = new MonitorService(cfgPath); }
        catch (Exception ex)
        {
            MessageBox.Show($"No se pudo leer la configuración:\n{cfgPath}\n\n{ex.Message}", "TuringLite",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Si la aplicación se rompe sin pasar por "Salir", intenta apagar la pantalla antes de terminar.
        // Con límite de tiempo: si el puerto serie está bloqueado, el proceso termina igualmente.
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            try { Task.Run(svc.Dispose).Wait(TimeSpan.FromSeconds(3)); }
            catch (Exception) { /* el proceso termina de todos modos */ }
        };

        Application.Run(new TrayApp(svc, showWindow: !args.Contains("--tray")));
    }

    static void ReportServiceResult(int code, string message) =>
        MessageBox.Show(message, "TuringLite", MessageBoxButtons.OK,
            code == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);

    static bool TryRelaunchElevated(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add("--no-elevate"); // evita un bucle si la elevación no surte efecto
            Process.Start(psi);
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false; // el usuario canceló UAC: seguimos sin admin (la temperatura de CPU puede salir en 0)
        }
    }
}
