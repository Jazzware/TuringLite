using System.Diagnostics;
using System.Security.Principal;

namespace TuringLite;

/// <summary>Comprobación de privilegios del proceso actual.</summary>
static class Elevation
{
    /// <summary>true si el proceso corre con el token de administrador completo (UAC ya aprobado).</summary>
    public static bool IsElevated()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Ejecuta esta misma app con permisos de administrador (un único aviso de UAC) y espera a que termine.
    /// Devuelve su código de salida. Si el usuario cancela el aviso, <paramref name="cancelled"/> es true.
    /// </summary>
    public static int RunElevated(string args, out bool cancelled)
    {
        cancelled = false;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = AppContext.BaseDirectory,
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

    /// <summary>
    /// Lanza esta app con permisos de administrador sin esperar a que termine. Devuelve false si el usuario
    /// cancela el aviso de UAC (en ese caso no se lanza nada).
    /// </summary>
    public static bool StartElevated(string args) => StartElevated(Environment.ProcessPath!, args, AppContext.BaseDirectory);

    /// <summary>Igual, pero con otro ejecutable (por ejemplo, una copia temporal de la app).</summary>
    public static bool StartElevated(string exe, string args, string workingDirectory)
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = workingDirectory,
            })?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
