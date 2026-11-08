namespace TuringLite.Setup;

static class Program
{
    /// <summary>
    /// Ventana por defecto. Con /silent instala sin ventana (para pruebas y despliegues):
    /// /dir=RUTA /shortcut=0|1 /autostart=0|1 /service=0|1 /launch=0|1.
    /// Códigos de salida: 0 correcto, 1 error, 2 TuringLite está abierto desde esa carpeta.
    /// </summary>
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("/silent", StringComparer.OrdinalIgnoreCase)) return RunSilent(args);

        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
        return 0;
    }

    static int RunSilent(string[] args)
    {
        string Value(string name, string fallback) =>
            args.FirstOrDefault(a => a.StartsWith("/" + name + "=", StringComparison.OrdinalIgnoreCase))?[(name.Length + 2)..] ?? fallback;
        bool Flag(string name, bool fallback) => Value(name, fallback ? "1" : "0") == "1";

        var options = new SetupOptions(
            InstallDir: Value("dir", Installer.DefaultDir),
            Shortcut: Flag("shortcut", true),
            Autostart: Flag("autostart", true),
            Service: Flag("service", false),
            Launch: Flag("launch", false));
        if (Installer.AppRunningFrom(options.InstallDir)) return 2;
        try
        {
            Installer.Run(options, _ => { });
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }
}
