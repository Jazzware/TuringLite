using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TuringLite.Setup;

/// <summary>Lo que el usuario elige en el instalador.</summary>
public sealed record SetupOptions(string InstallDir, bool Shortcut, bool Autostart, bool Service, bool Launch);

/// <summary>
/// Instalación por usuario: no necesita administrador, salvo el servicio de sensores, que se instala aparte con un
/// único aviso de UAC. Todo lo que crea queda registrado para que el desinstalador lo pueda quitar.
/// </summary>
public static class Installer
{
    public const string Version = "1.0.1";
    public const string Marker = "install.marker";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TuringLite";

    public static string DefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TuringLite");

    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "TuringLite.lnk");

    /// <summary>Página oficial de descarga del runtime que necesita TuringLite (WinForms en .NET 8, x64).</summary>
    public const string RuntimeUrl = "https://dotnet.microsoft.com/download/dotnet/8.0";

    /// <summary>
    /// true si está instalado el .NET 8 Desktop Runtime (x64) que necesita la app: sus versiones viven en
    /// shared\Microsoft.WindowsDesktop.App\8.x. El instalador corre en .NET Framework 4.8, así que esta comprobación
    /// llega a ejecutarse también cuando falta. Solo lo comprueba; no lo descarga ni lo instala.
    /// </summary>
    public static bool HasDesktopRuntime()
    {
        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "dotnet", "shared", "Microsoft.WindowsDesktop.App");
        return Directory.Exists(shared)
               && Directory.EnumerateDirectories(shared).Any(d => Path.GetFileName(d).StartsWith("8.", StringComparison.Ordinal));
    }

    /// <summary>Instancias de TuringLite abiertas desde esta carpeta: no se puede reemplazar archivos en uso.</summary>
    public static bool AppRunningFrom(string dir)
    {
        var exe = Path.Combine(dir, "TuringLite.exe");
        foreach (var p in Process.GetProcessesByName("TuringLite"))
        {
            try
            {
                if (string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception)
            {
                // Proceso de otro usuario o sin acceso: no es de esta instalación.
            }
            finally { p.Dispose(); }
        }
        return false;
    }

    /// <summary>Instala. Devuelve avisos de lo que no se pudo completar (la instalación base sí queda hecha).</summary>
    public static List<string> Run(SetupOptions o, Action<string> progress)
    {
        var warnings = new List<string>();
        var exe = Path.Combine(o.InstallDir, "TuringLite.exe");

        progress("Copiando archivos…");
        Extract(o.InstallDir);

        progress("Registrando la desinstalación…");
        WriteUninstallEntry(o.InstallDir);

        if (o.Shortcut)
        {
            progress("Creando acceso directo…");
            try { CreateShortcut(exe, o.InstallDir); }
            catch (Exception ex) { warnings.Add("No se pudo crear el acceso directo: " + ex.Message); }
        }

        if (o.Autostart)
        {
            progress("Activando el inicio con Windows…");
            if (Start(exe, "--autostart-on", o.InstallDir) != 0)
                warnings.Add("No se pudo activar el inicio con Windows. Puedes activarlo desde la ventana de TuringLite.");
        }

        bool serviceOk = false;
        if (o.Service)
        {
            progress("Instalando el servicio de sensores (acepta el aviso de UAC)…");
            int rc = RunElevated(exe, "--install-service --quiet", out bool cancelled);
            serviceOk = rc == 0;
            if (cancelled)
                warnings.Add("El servicio no se instaló: se canceló el permiso de administrador. " +
                             "Puedes instalarlo luego desde la ventana de TuringLite.");
            else if (rc != 0)
                warnings.Add($"El servicio no se instaló (código {rc}). TuringLite funciona igual, sin temperatura de CPU.");
        }

        if (o.Launch)
        {
            progress("Abriendo TuringLite…");
            // Sin servicio, abrir la app normal volvería a pedir UAC: en ese caso arranca solo en la bandeja.
            Process.Start(new ProcessStartInfo(exe, serviceOk ? "" : "--tray") { UseShellExecute = true, WorkingDirectory = o.InstallDir });
        }
        return warnings;
    }

    static void Extract(string dir)
    {
        Directory.CreateDirectory(dir);
        var root = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip")
                           ?? throw new InvalidOperationException("El instalador no contiene los archivos de la aplicación.");
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            // Algunos empaquetadores usan "\" en las rutas: se normaliza. Las carpetas no se extraen como archivo.
            var name = entry.FullName.Replace('\\', '/');
            if (name.EndsWith("/") || entry.Name.Length == 0) continue;
            var target = Path.GetFullPath(Path.Combine(root, name));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ruta no válida en el paquete: " + name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // La configuración del usuario no se pisa al actualizar.
            if (entry.Name.Equals("config.json", StringComparison.OrdinalIgnoreCase) && File.Exists(target)) continue;
            entry.ExtractToFile(target, overwrite: true);
        }
        File.WriteAllText(Path.Combine(root, Marker),
            $"TuringLite {Version} instalado el {DateTime.Now:yyyy-MM-dd HH:mm}. No borrar: lo usa el desinstalador.",
            new UTF8Encoding(false));
    }

    static void WriteUninstallEntry(string dir)
    {
        var exe = Path.Combine(dir, "TuringLite.exe");
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey);
        key.SetValue("DisplayName", "TuringLite");
        key.SetValue("DisplayVersion", Version);
        key.SetValue("Publisher", "TuringLite");
        key.SetValue("InstallLocation", dir);
        key.SetValue("DisplayIcon", exe + ",0");
        key.SetValue("UninstallString", $"\"{exe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
        var bytes = new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
    }

    /// <summary>Acceso directo mediante WScript.Shell (COM con enlace tardío, sin referencias extra).</summary>
    static void CreateShortcut(string exe, string dir)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("WScript.Shell no disponible.");
        var shell = Activator.CreateInstance(shellType)!;
        try
        {
            var link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { ShortcutPath })!;
            var t = link.GetType();
            t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { exe });
            t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { dir });
            t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { exe + ",0" });
            t.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Monitor de hardware para la pantalla USB" });
            t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        }
        finally
        {
            Marshal.ReleaseComObject(shell);
        }
    }

    /// <summary>Ejecuta la app instalada con argumentos, sin ventana, y devuelve su código de salida.</summary>
    static int Start(string exe, string args, string dir)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = dir,
        })!;
        p.WaitForExit();
        return p.ExitCode;
    }

    static int RunElevated(string exe, string args, out bool cancelled)
    {
        cancelled = false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(exe),
            })!;
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            cancelled = true;
            return -1;
        }
    }
}
