using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace TuringLite;

/// <summary>
/// Inicio con Windows mediante una entrada de HKCU\...\Run: es del usuario y no necesita administrador. La app arranca
/// sin privilegios, así que nada de esto requiere elevación. Las tareas programadas de versiones anteriores se quitan
/// cuando Windows lo permite (las creadas con privilegios altos solo las puede borrar un administrador).
/// </summary>
static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "TuringLite";
    const string LegacyTaskName = "TuringLite";

    static string Command(string exe) => $"\"{exe}\" --tray";

    public static bool IsEnabled() => Registry.CurrentUser.OpenSubKey(RunKey)?.GetValue(ValueName) is string;

    public static bool Enable()
    {
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Ruta del ejecutable desconocida.");
        using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            key.SetValue(ValueName, Command(exe), RegistryValueKind.String);
        RemoveLegacyTaskIfPossible();
        return true;
    }

    public static bool Disable()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        RemoveLegacyTaskIfPossible();
        return true;
    }

    /// <summary>
    /// Quita el inicio solo si es de este ejecutable. Devuelve "none" (no había), "removed" o "other" (es de otra copia
    /// y se conserva: desinstalar esta no debe quitarle el inicio a la otra).
    /// </summary>
    public static string RemoveIfOwned(string exe)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(ValueName) is not string command) return "none";
        if (!command.Contains(exe, StringComparison.OrdinalIgnoreCase)) return "other";
        key.DeleteValue(ValueName, throwOnMissingValue: false);
        return "removed";
    }

    /// <summary>Tarea de versiones anteriores, solo si apunta a este ejecutable. Sin permiso, se deja como está.</summary>
    static void RemoveLegacyTaskIfPossible()
    {
        try
        {
            var exe = Environment.ProcessPath ?? "";
            if (Schtasks($"/Query /TN {LegacyTaskName} /XML", out var xml) != 0) return;
            if (exe.Length > 0 && xml.Contains(SecurityElement.Escape(exe), StringComparison.OrdinalIgnoreCase))
                Schtasks($"/Delete /TN {LegacyTaskName} /F");
        }
        catch (Exception)
        {
            // Sin permiso o sin schtasks: la tarea antigua se queda; no impide el inicio nuevo.
        }
    }

    static int Schtasks(string args, out string stdout)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        stdout = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit();
        return p.ExitCode;
    }

    static int Schtasks(string args) => Schtasks(args, out _);
}
