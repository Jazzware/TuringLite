using System.Diagnostics;
using System.Drawing;
using System.Text;
using Microsoft.Win32;

namespace TuringLite;

/// <summary>Opciones de desinstalación elegidas por el usuario (o por la línea de comandos).</summary>
public sealed record UninstallOptions(bool KeepConfig, bool KeepService, bool CleanSystemTraces = true);

/// <summary>
/// Desinstala TuringLite y deja el sistema como estaba antes de instalarlo. Solo borra lo que instaló el instalador:
/// comprueba un archivo marcador (install.marker) en la carpeta, la ruta de la tarea de inicio y el valor
/// InstallLocation de su entrada de Windows. Así no toca una copia de desarrollo ni otra instalación.
/// </summary>
public static class Uninstaller
{
    public const string Marker = "install.marker";
    public const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TuringLite";

    static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "TuringLite.lnk");
    public static string BackupDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TuringLite");

    static string AppDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

    public static bool IsInstalledCopy() => File.Exists(Path.Combine(AppDir, Marker));

    /// <summary>Instancias de TuringLite de esta misma carpeta que siguen abiertas (la del desinstalador no cuenta).</summary>
    public static bool AppIsRunning()
    {
        var self = Environment.ProcessId;
        var exe = Path.Combine(AppDir, "TuringLite.exe");
        foreach (var p in Process.GetProcessesByName("TuringLite"))
        {
            try
            {
                if (p.Id != self && string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception)
            {
                // Proceso de otro usuario o sin acceso: no es de esta instalación.
            }
            finally { p.Dispose(); }
        }
        return false;
    }

    /// <summary>Desinstala. Devuelve un resumen de lo hecho, para mostrarlo o registrarlo.</summary>
    public static List<string> Run(UninstallOptions o)
    {
        var log = new List<string>();
        if (!IsInstalledCopy())
        {
            log.Add("Esta copia no es una instalación del instalador: no se ha borrado nada.");
            return log;
        }
        var exe = Path.Combine(AppDir, "TuringLite.exe");

        // 1. Inicio automático: solo el que apunta a esta copia.
        log.Add(AutoStart.RemoveIfOwned(exe) switch
        {
            "removed" => "Inicio automático quitado.",
            "other" => "El inicio automático es de otra copia: se conserva.",
            _ => "No había inicio automático.",
        });

        // 2. Servicio de sensores y huellas del sistema (Prefetch): requieren administrador. Van en un único proceso
        //    elevado, lanzado sin esperarlo: espera a que Windows termine de escribir sus registros antes de borrar Prefetch.
        bool removeService = !o.KeepService && ServiceInstaller.IsInstalled();
        if (o.KeepService) log.Add("Servicio de sensores conservado (opción elegida).");
        else if (!removeService) log.Add("No había servicio de sensores.");
        if (removeService || o.CleanSystemTraces)
        {
            var args = "--elevated-cleanup --quiet --delay 120" + (removeService ? " --remove-service" : "") + (o.CleanSystemTraces ? " --prefetch" : "");
            // Corre desde una copia temporal: desde la carpeta de la app la tendría bloqueada mientras reintenta
            // Prefetch (varios minutos) y el borrado de la carpeta podría rendirse antes de que termine.
            var copy = TempCopy.Create(AppDir, "turinglite-elevado-");
            bool started = copy != null
                ? Elevation.StartElevated(Path.Combine(copy, "TuringLite.exe"), args + $" --temp-copy \"{copy}\"", copy)
                : Elevation.StartElevated(args);
            if (!started && copy != null) TempCopy.TryDelete(copy);
            log.Add(started
                ? "Servicio y huellas del sistema: se quitan en segundo plano con permiso de administrador."
                : "Permiso de administrador cancelado: el servicio y las huellas del sistema no se han quitado.");
        }

        // 3. Acceso directo del menú Inicio, huellas de Windows del usuario y entrada de "Aplicaciones instaladas",
        //    solo si son de esta copia.
        DeleteIfExists(ShortcutPath, "Acceso directo del menú Inicio", log);
        CleanUserTraces(Registry.CurrentUser, AppDir, ShortcutPath, log);
        // Windows y el Explorador siguen escribiendo estas huellas un rato después de cerrar la app: se repite más tarde.
        DelayedTraces.Schedule(AppDir, ShortcutPath, log);
        if (OwnsUninstallEntry()) { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false); log.Add("Entrada de Aplicaciones instaladas quitada."); }

        // 4. Configuración: se borra, o se guarda en Documentos\TuringLite si se eligió conservarla.
        foreach (var name in new[] { "config.json", "status.txt" })
        {
            var file = Path.Combine(AppDir, name);
            if (!File.Exists(file)) continue;
            if (o.KeepConfig)
            {
                Directory.CreateDirectory(BackupDir);
                File.Copy(file, Path.Combine(BackupDir, name), overwrite: true);
            }
        }
        log.Add(o.KeepConfig ? $"Configuración guardada en {BackupDir}." : "Configuración borrada.");

        // 5. Restos temporales de la instalación.
        foreach (var tmp in Directory.GetFiles(Path.GetTempPath(), "turinglite-*"))
            DeleteIfExists(tmp, "Temporal", log);

        // 6. La carpeta de la app se borra cuando este proceso ya no tiene archivos abiertos.
        ScheduleFolderRemoval(AppDir);
        log.Add("Carpeta de la aplicación: se borra al cerrar el desinstalador.");
        return log;
    }

    /// <summary>
    /// Quita las huellas que Windows guarda del usuario sobre esta copia: historial de ejecución, caché de nombres,
    /// uso del acceso directo, mosaicos del menú Inicio y metadatos en la nube. No necesita administrador y solo toca
    /// entradas que apuntan a esta instalación. <paramref name="root"/> es HKCU en producción (la prueba usa un árbol aparte).
    /// </summary>
    public static int CleanUserTraces(RegistryKey root, string appDir, string shortcut, List<string> log)
    {
        var exe = Path.Combine(appDir, "TuringLite.exe");
        int n = 0;
        // Caché de nombres (MuiCache): valores "<ruta>.FriendlyAppName", "<ruta>.ApplicationCompany"...
        n += DropValues(root, @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache",
            name => name.StartsWith(exe, StringComparison.OrdinalIgnoreCase));
        // Historial de uso: el nombre del valor es la ruta del ejecutable.
        n += DropValues(root, @"Software\Microsoft\Windows\CurrentVersion\Explorer\FeatureUsage\AppSwitched",
            name => string.Equals(name, exe, StringComparison.OrdinalIgnoreCase));
        // Compatibilidad: el nombre del valor es la ruta del ejecutable.
        n += DropValues(root, @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store",
            name => name.StartsWith(exe, StringComparison.OrdinalIgnoreCase));
        // Uso del acceso directo del menú Inicio: el dato contiene la ruta del .lnk.
        n += DropValues(root, @"Software\Microsoft\Windows\CurrentVersion\UFH\SHC",
            null, data => data.Contains(shortcut, StringComparison.OrdinalIgnoreCase));
        // Mosaicos del menú Inicio: la ruta está repartida en subclaves anidadas (W~C: → Users → ... → TuringLite).
        n += DropTileTree(root, @"Software\Microsoft\Windows\CurrentVersion\Start\TileProperties", appDir);
        // Metadatos de la app en la nube de Windows (CloudStore): la cuenta cambia de un equipo a otro, así que se recorre.
        n += DropCloudStore(root, appDir);
        log.Add(n == 0 ? "Huellas de Windows del usuario: no había." : $"Huellas de Windows del usuario quitadas: {n}.");
        return n;
    }

    static int DropValues(RegistryKey root, string path, Func<string, bool>? nameMatches, Func<string, bool>? dataMatches = null)
    {
        using var key = root.OpenSubKey(path, writable: true);
        if (key == null) return 0;
        int n = 0;
        foreach (var name in key.GetValueNames())
        {
            if (name.Length == 0) continue; // valor predeterminado: no es una huella
            var data = ValueText(key.GetValue(name));
            if ((nameMatches?.Invoke(name) ?? false) || (dataMatches?.Invoke(data) ?? false))
            {
                key.DeleteValue(name, throwOnMissingValue: false);
                n++;
            }
        }
        return n;
    }

    /// <summary>
    /// Texto de un valor del registro. Los valores REG_MULTI_SZ (como UFH\SHC) llegan como string[]: su ToString()
    /// sería "System.String[]", así que hay que unir los elementos.
    /// </summary>
    static string ValueText(object? raw) => raw switch
    {
        null => "",
        string s => s,
        string[] parts => string.Join(" ", parts),
        _ => raw.ToString() ?? "",
    };

    static int DropTileTree(RegistryKey root, string path, string appDir)
    {
        using var key = root.OpenSubKey(path, writable: true);
        return key == null ? 0 : WalkTiles(key, "", appDir);
    }

    /// <summary>
    /// Recorre las subclaves como una ruta. Borra la rama que contiene la carpeta de la app y baja solo por las ramas
    /// que pueden llevar a ella (así no se recorre todo el menú Inicio).
    /// </summary>
    static int WalkTiles(RegistryKey key, string prefix, string appDir)
    {
        int n = 0;
        foreach (var child in key.GetSubKeyNames())
        {
            var full = prefix.Length == 0 ? child : prefix + "\\" + child;
            if (full.Contains(appDir, StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteSubKeyTree(child, throwOnMissingSubKey: false);
                n++;
                continue;
            }
            var plain = full.StartsWith("W~", StringComparison.Ordinal) ? full[2..] : full;
            if (!appDir.StartsWith(plain, StringComparison.OrdinalIgnoreCase)) continue;
            var sub = key.OpenSubKey(child, writable: true);
            if (sub == null) continue;
            try
            {
                n += WalkTiles(sub, full, appDir);
                // Un nodo de la ruta de la app que se queda sin nada se borra (si tiene datos, se deja).
                if (sub.SubKeyCount == 0 && sub.ValueCount == 0)
                {
                    sub.Close();
                    key.DeleteSubKey(child, throwOnMissingSubKey: false);
                }
            }
            finally { sub.Close(); }
        }
        return n;
    }

    static int DropCloudStore(RegistryKey root, string appDir)
    {
        const string store = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount";
        var wanted = Normalize(appDir);
        int n = 0;
        foreach (var section in new[] { "Cloud", "Current" })
        {
            using var sec = root.OpenSubKey($@"{store}\{section}", writable: false);
            if (sec == null) continue;
            foreach (var list in sec.GetSubKeyNames())
            {
                bool apps = list.Contains("windows.data.apps.appmetadata$appmetadatalist", StringComparison.OrdinalIgnoreCase);
                bool tiles = list.Contains("windows.data.apps.appleveltileinfo$appleveltilelist", StringComparison.OrdinalIgnoreCase);
                if (!apps && !tiles) continue;
                using var listKey = sec.OpenSubKey(list, writable: true);
                if (listKey == null) continue;
                foreach (var item in listKey.GetSubKeyNames())
                {
                    bool mine = apps
                        ? item.EndsWith("$turinglite", StringComparison.OrdinalIgnoreCase)
                        : Normalize(item).Contains(wanted, StringComparison.Ordinal);
                    if (!mine) continue;
                    listKey.DeleteSubKeyTree(item, throwOnMissingSubKey: false);
                    n++;
                }
            }
        }
        return n;
    }

    /// <summary>Solo letras y cifras en minúscula: así se comparan rutas con los nombres que escribe Windows.</summary>
    static string Normalize(string s) => new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    static bool OwnsUninstallEntry()
    {
        using var key = Registry.CurrentUser.OpenSubKey(UninstallKey);
        return key != null && string.Equals(key.GetValue("InstallLocation") as string, AppDir, StringComparison.OrdinalIgnoreCase);
    }

    static void DeleteIfExists(string path, string what, List<string> log)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); log.Add($"{what} borrado."); }
        catch (IOException) { log.Add($"{what} en uso: no se pudo borrar ({path})."); }
    }

    /// <summary>
    /// Un proceso no puede borrar su propia carpeta. Un script temporal espera a que el proceso termine,
    /// borra la carpeta (con reintentos) y se borra a sí mismo.
    /// </summary>
    static void ScheduleFolderRemoval(string dir)
    {
        var script = Path.Combine(Path.GetTempPath(), $"turinglite-cleanup-{Guid.NewGuid():N}.cmd");
        var text = new StringBuilder()
            .AppendLine("@echo off")
            .AppendLine("chcp 65001 >nul")
            .AppendLine("set /a n=0")
            .AppendLine(":retry")
            .AppendLine("timeout /t 1 /nobreak >nul")
            .AppendLine($"rd /s /q \"{dir}\" 2>nul")
            .AppendLine($"if exist \"{dir}\" (")
            .AppendLine("  set /a n+=1")
            .AppendLine("  if %n% lss 300 goto retry")
            .AppendLine(")")
            .AppendLine("del \"%~f0\"")
            .ToString();
        File.WriteAllText(script, text, new UTF8Encoding(false));
        // El directorio de trabajo no puede ser la carpeta que se borra: si lo fuera, rd no la eliminaría nunca.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        });
    }

    /// <summary>Punto de entrada de --uninstall: con interfaz, o en silencio con --silent.</summary>
    public static void Entry(string[] args)
    {
        var keepConfig = args.Contains("--keep-config");
        var keepService = args.Contains("--keep-service");
        if (args.Contains("--silent"))
        {
            if (AppIsRunning()) { Environment.Exit(2); return; }
            Run(new UninstallOptions(keepConfig, keepService, !args.Contains("--keep-traces")));
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new UninstallForm(keepConfig));
    }
}

/// <summary>
/// Segunda pasada de limpieza de huellas, unos minutos después de desinstalar: el Explorador y el servicio de compatibilidad
/// escriben sus cachés con retraso. Corre desde una copia temporal de la app, porque la carpeta original ya no existirá.
/// </summary>
public static class DelayedTraces
{
    public const string Flag = "--trazas-diferidas";
    const int DelaySeconds = 180;

    public static void Schedule(string appDir, string shortcut, List<string> log)
    {
        try
        {
            var temp = TempCopy.Create(appDir, "turinglite-trazas-") ?? throw new IOException("sin copia temporal");
            var psi = new ProcessStartInfo(Path.Combine(temp, "TuringLite.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = temp,
            };
            psi.ArgumentList.Add(Flag);
            psi.ArgumentList.Add(appDir);
            psi.ArgumentList.Add(shortcut);
            psi.ArgumentList.Add(temp);
            psi.ArgumentList.Add(DelaySeconds.ToString());
            Process.Start(psi)?.Dispose();
        }
        catch (Exception)
        {
            log.Add("La segunda pasada de limpieza no se pudo programar.");
        }
    }

    /// <summary>Argumentos: --trazas-diferidas, carpeta de la app, acceso directo, carpeta temporal, segundos.</summary>
    public static void Run(string[] args)
    {
        int i = Array.IndexOf(args, Flag);
        if (i < 0 || i + 4 >= args.Length) return;
        var appDir = args[i + 1];
        var shortcut = args[i + 2];
        var temp = args[i + 3];
        int seconds = int.TryParse(args[i + 4], out var s) ? Math.Clamp(s, 0, 600) : DelaySeconds;
        Thread.Sleep(TimeSpan.FromSeconds(seconds));
        Uninstaller.CleanUserTraces(Registry.CurrentUser, appDir, shortcut, new List<string>());
        TempCopy.RemoveSelfLater(temp);
    }
}

/// <summary>
/// Copias temporales de la app para el trabajo que sigue después de desinstalar (segunda pasada y limpieza elevada):
/// así no se bloquea la carpeta de instalación, que se borra mientras tanto.
/// </summary>
public static class TempCopy
{
    /// <summary>Copia la carpeta de la app a %TEMP%\&lt;prefijo&gt;&lt;guid&gt;. Devuelve la ruta, o null si no se pudo.</summary>
    public static string? Create(string source, string prefix)
    {
        var dest = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(source, dest);
            return dest;
        }
        catch (Exception)
        {
            TryDelete(dest);
            return null;
        }
    }

    public static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    public static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception) { /* queda en %TEMP%; Windows lo limpia */ }
    }

    /// <summary>
    /// Borra, poco después de que termine este proceso, la carpeta desde la que se está ejecutando. Solo esa: aunque
    /// la ruta llegue por argumentos, nunca se borra otra (importante cuando el proceso corre como administrador).
    /// </summary>
    public static bool RemoveSelfLater(string dir)
    {
        if (!IsOwnFolder(dir, AppContext.BaseDirectory)) return false;
        // Sin WorkingDirectory, cmd heredaría esta carpeta como directorio actual y rd no podría borrarla.
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 4 127.0.0.1 >nul & rd /s /q \"{Path.GetFullPath(dir)}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath(),
        })?.Dispose();
        return true;
    }

    /// <summary>true si <paramref name="dir"/> es exactamente <paramref name="baseDir"/> y está dentro de %TEMP%.</summary>
    public static bool IsOwnFolder(string dir, string baseDir)
    {
        try
        {
            var a = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
            var b = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar);
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                   && a.StartsWith(temp, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// Trabajo que necesita administrador, ejecutado por el propio desinstalador elevado (--elevated-cleanup):
/// borra los Prefetch de TuringLite y, si se pide, el servicio de sensores.
/// </summary>
public static class SystemCleanup
{
    public static int Run(string[] args)
    {
        if (!Elevation.IsElevated()) return 5;
        int code = 0;
        if (args.Contains("--remove-service")) code = ServiceInstaller.Uninstall(out _);
        if (args.Contains("--prefetch"))
        {
            // Windows escribe el Prefetch con retraso al cerrar la app, y la copia de la segunda pasada (que corre
            // unos 3 minutos) escribe el suyo al terminar: se reintenta un rato hasta que no quede ningún TURINGLITE*.pf.
            Thread.Sleep(TimeSpan.FromSeconds(DelaySeconds(args, 120)));
            int left = PrefetchUntilGone(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch"),
                attempts: 12, pause: TimeSpan.FromSeconds(30));
            if (left > 0 && code == 0) code = 3;
        }
        // Corre desde una copia en %TEMP% (ver Uninstaller.Run): se borra al terminar.
        int t = Array.IndexOf(args, "--temp-copy");
        if (t >= 0 && t + 1 < args.Length) TempCopy.RemoveSelfLater(args[t + 1]);
        return code;
    }

    /// <summary>Repite el borrado hasta que no quede ningún TURINGLITE*.pf o se agoten los intentos. Devuelve cuántos quedan.</summary>
    public static int PrefetchUntilGone(string dir, int attempts, TimeSpan pause)
    {
        for (int i = 0; ; i++)
        {
            DeletePrefetch(dir);
            int left = Directory.Exists(dir) ? Directory.GetFiles(dir, "TURINGLITE*.pf").Length : 0;
            if (left == 0 || i + 1 >= attempts) return left;
            Thread.Sleep(pause);
        }
    }

    static int DelaySeconds(string[] args, int fallback)
    {
        int i = Array.IndexOf(args, "--delay");
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var s) ? Math.Clamp(s, 0, 600) : fallback;
    }

    /// <summary>Borra TURINGLITE*.pf: huellas de ejecución que Windows crea cada vez que se abre un programa. Devuelve cuántas no pudo borrar.</summary>
    public static int DeletePrefetch(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        int failed = 0;
        foreach (var file in Directory.GetFiles(dir, "TURINGLITE*.pf"))
        {
            try { File.Delete(file); }
            catch (IOException) { failed++; }
            catch (UnauthorizedAccessException) { failed++; }
        }
        return failed;
    }
}

/// <summary>Ventana de desinstalación: qué se quita y qué se conserva, con una sola confirmación.</summary>
sealed class UninstallForm : Form
{
    readonly CheckBox _service = new() { Text = "Quitar el servicio de sensores (pide permiso de administrador)", AutoSize = true };
    readonly CheckBox _config = new() { Text = "Conservar mi configuración en Documentos\\TuringLite", AutoSize = true };
    readonly CheckBox _traces = new() { Text = "Borrar también las huellas de Windows (historial y Prefetch; Prefetch pide administrador)", AutoSize = true };

    public UninstallForm(bool keepConfig)
    {
        Text = "Desinstalar TuringLite";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(500, 250);

        Controls.Add(Ui.MakeLabel("Desinstalar TuringLite", 20, 16, font: Ui.TitleFont));
        Controls.Add(Ui.MakeLabel("Se quitan el programa, su acceso directo, el inicio automático y la entrada de " +
                                  "Aplicaciones instaladas. Se quitan también todos los archivos que creó la instalación.",
            20, 52, dim: true));
        bool serviceInstalled = ServiceInstaller.IsInstalled();
        _service.Location = new Point(20, 110);
        _service.Checked = serviceInstalled;
        _service.Enabled = serviceInstalled;
        if (!serviceInstalled) _service.Text += " (no está instalado)";
        _config.Location = new Point(20, 140);
        _config.Checked = keepConfig;
        _traces.Location = new Point(20, 170);
        _traces.Checked = true;
        Controls.Add(_service);
        Controls.Add(_config);
        Controls.Add(_traces);

        var ok = Ui.MakeButton("Desinstalar", 300, 206, 180, 30, true, Uninstall);
        var cancel = Ui.MakeButton("Cancelar", 200, 206, 90, 30, false, Close);
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        Ui.Dark(this);
    }

    void Uninstall()
    {
        if (Uninstaller.AppIsRunning())
        {
            MessageBox.Show(this, "TuringLite sigue abierto. Ciérralo desde la bandeja del sistema (Salir) y vuelve a intentarlo.",
                "Desinstalar TuringLite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this, "¿Seguro que quieres desinstalar TuringLite?", "Desinstalar TuringLite",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        var log = Uninstaller.Run(new UninstallOptions(_config.Checked, !_service.Checked, _traces.Checked));
        MessageBox.Show(this, string.Join("\n", log) + "\n\nTuringLite se ha desinstalado.",
            "Desinstalar TuringLite", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Close();
        Application.Exit();
    }
}
