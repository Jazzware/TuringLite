using System.Text;
using HidSharp;
using HidSharp.Reports;

namespace TuringLite;

public enum BatteryKind { Unknown, Mouse, Keyboard, Headset, Other }

/// <summary>Una lectura de batería. Percent es null si el dispositivo está conectado pero apagado o fuera de alcance.</summary>
public sealed record BatteryInfo(string Id, string Name, BatteryKind Kind, int? Percent, bool Charging, string Source);

/// <summary>
/// Baterías de periféricos inalámbricos, leídas directamente por USB HID (sin G HUB ni administrador):
/// - Logitech LIGHTSPEED/Unifying/Bolt y dispositivos directos: protocolo HID++ 2.0
///   (características 0x1004 Unified Battery, 0x1000 Battery Status y 0x1001 Battery Voltage).
/// - ASTRO A50 Gen 5 (base 046D:0B1C): protocolo propio de la base, documentado por HeadsetControl
///   (https://github.com/Sapd/HeadsetControl, GPL-3.0).
/// Solo se envían peticiones de lectura.
/// </summary>
public static class BatteryReader
{
    const int Logitech = 0x046D;

    public static List<BatteryInfo> ReadAll(Action<string>? log = null)
    {
        var result = new List<BatteryInfo>();
        foreach (var read in new Func<Action<string>?, IEnumerable<BatteryInfo>>[] { Hidpp.Read, AstroA50.Read })
        {
            try { result.AddRange(read(log)); }
            catch (Exception ex) { log?.Invoke("Error: " + ex.Message); }
        }
        return result;
    }

    /// <summary>Colecciones HID de un VID/PID con una página de uso concreta (cada colección es una ruta distinta en Windows).</summary>
    internal static IEnumerable<HidDevice> Collections(int vid, int? pid, uint usagePage, uint? usage = null)
    {
        foreach (var d in DeviceList.Local.GetHidDevices(vid))
        {
            if (pid != null && d.ProductID != pid) continue;
            uint[] usages;
            try
            {
                usages = d.GetReportDescriptor().DeviceItems.SelectMany(i => i.Usages.GetAllValues()).ToArray();
            }
            catch (Exception) { continue; }
            if (usages.Any(u => u >> 16 == usagePage && (usage == null || (u & 0xFFFF) == usage))) yield return d;
        }
    }

    internal static string Hex(byte[] b, int n) => string.Join(" ", b.Take(n).Select(x => x.ToString("X2")));
}

/// <summary>Dispositivos que muestra un componente de baterías según su configuración.</summary>
public static class BatteryFilter
{
    public static List<BatteryInfo> Apply(IReadOnlyList<BatteryInfo> all, WidgetConfig c) =>
        all.Where(b => (c.Devices.Count == 0 || c.Devices.Contains(b.Id)) && !(c.HideOffline && b.Percent == null)).ToList();
}

/// <summary>
/// Últimas lecturas de batería, compartidas por todos los componentes. Lee en segundo plano cada minuto,
/// y solo desde que algún componente lo pide: sin componente de baterías no se habla con los dispositivos.
/// </summary>
public static class BatteryCache
{
    static readonly TimeSpan Every = TimeSpan.FromSeconds(60);
    static readonly object Gate = new();
    static System.Threading.Timer? _timer;
    static int _busy;
    static IReadOnlyList<BatteryInfo> _latest = Array.Empty<BatteryInfo>();

    /// <summary>Lecturas más recientes (vacío hasta que termina la primera lectura).</summary>
    public static IReadOnlyList<BatteryInfo> Latest { get { Start(); return _latest; } }

    /// <summary>Ya se completó al menos una lectura.</summary>
    public static bool Ready { get; private set; }

    static void Start()
    {
        if (_timer != null) return;
        lock (Gate) _timer ??= new System.Threading.Timer(_ => Poll(), null, TimeSpan.Zero, Every);
    }

    static void Poll()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return; // la anterior aún no terminó
        try
        {
            _latest = BatteryReader.ReadAll();
            Ready = true;
        }
        catch (Exception) { /* conserva la última lectura */ }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }
}

/// <summary>HID++ 2.0 de Logitech sobre la colección de informes largos (ID 0x11, 20 bytes; página 0xFF00, uso 0x0002).</summary>
static class Hidpp
{
    const byte LongReport = 0x11;
    const byte SoftwareId = 0x0B;           // identifica nuestras peticiones; G HUB usa otros
    const int ReplyTimeoutMs = 600;
    const int PingTimeoutMs = 250;

    public static IEnumerable<BatteryInfo> Read(Action<string>? log)
    {
        var list = new List<BatteryInfo>();
        foreach (var dev in BatteryReader.Collections(0x046D, null, 0xFF00, 0x0002))
        {
            if (!dev.TryOpen(out HidStream stream)) { log?.Invoke($"HID++ {dev.ProductID:X4}: no se pudo abrir"); continue; }
            using (stream)
            {
                stream.ReadTimeout = ReplyTimeoutMs;
                stream.WriteTimeout = ReplyTimeoutMs;
                int outLen = dev.GetMaxOutputReportLength();
                log?.Invoke($"HID++ {dev.ProductID:X4}: abierto (informe de salida {outLen} bytes)");
                // Receptor: dispositivos en los índices 1-6. Dispositivo con cable o dongle directo: índice 0xFF.
                foreach (byte index in new byte[] { 1, 2, 3, 4, 5, 6, 0xFF })
                {
                    var info = ReadDevice(stream, outLen, dev.ProductID, index, log);
                    if (info != null) list.Add(info);
                }
            }
        }
        return list;
    }

    static BatteryInfo? ReadDevice(HidStream s, int outLen, int pid, byte index, Action<string>? log)
    {
        // Ping (raíz, función 1): solo los dispositivos HID++ 2.0 encendidos responden con la versión.
        // Espera corta: en un receptor, los índices vacíos no responden por esta colección.
        var ping = Request(s, outLen, index, 0x00, 1, log, PingTimeoutMs, 0, 0, 0x5A);
        if (ping == null) return null;
        string id = $"logi:{pid:X4}:{index:X2}";
        string name = Name(s, outLen, index, log) ?? $"Logitech {pid:X4} #{index}";
        var kind = Kind(s, outLen, index, log);

        int? percent = null;
        bool charging = false;
        if (Feature(s, outLen, index, 0x1004, log) is byte unified and > 0
            && Request(s, outLen, index, unified, 1, log) is { } u)
        {
            percent = u[0];
            charging = u[2] is 1 or 2 or 3;
        }
        else if (Feature(s, outLen, index, 0x1000, log) is byte status and > 0
                 && Request(s, outLen, index, status, 0, log) is { } b)
        {
            percent = b[0];
            charging = b[2] is 1 or 2 or 3;
        }
        else if (Feature(s, outLen, index, 0x1001, log) is byte voltage and > 0
                 && Request(s, outLen, index, voltage, 0, log) is { } v)
        {
            int mv = (v[0] << 8) | v[1];
            percent = FromMillivolts(mv);
            charging = (v[2] & 0x80) != 0;
            log?.Invoke($"  {name}: {mv} mV");
        }
        log?.Invoke($"  índice {index:X2}: {name} ({kind}) → {(percent?.ToString() ?? "--")} %{(charging ? " cargando" : "")}");
        return new BatteryInfo(id, name, kind, percent, charging, "HID++");
    }

    /// <summary>Índice de una característica (raíz, función 0). 0 = no la tiene.</summary>
    static byte? Feature(HidStream s, int outLen, byte index, ushort feature, Action<string>? log) =>
        Request(s, outLen, index, 0x00, 0, log, (byte)(feature >> 8), (byte)feature) is { } r ? r[0] : null;

    static string? Name(HidStream s, int outLen, byte index, Action<string>? log)
    {
        if (Feature(s, outLen, index, 0x0005, log) is not byte f || f == 0) return null;
        if (Request(s, outLen, index, f, 0, log) is not { } count) return null;
        var sb = new StringBuilder();
        while (sb.Length < count[0])
        {
            if (Request(s, outLen, index, f, 1, log, (byte)sb.Length) is not { } part) break;
            int before = sb.Length;
            foreach (var c in part) { if (c == 0 || sb.Length >= count[0]) break; sb.Append((char)c); }
            if (sb.Length == before) break;
        }
        return sb.Length > 0 ? sb.ToString().Trim() : null;
    }

    static BatteryKind Kind(HidStream s, int outLen, byte index, Action<string>? log)
    {
        if (Feature(s, outLen, index, 0x0005, log) is not byte f || f == 0) return BatteryKind.Unknown;
        if (Request(s, outLen, index, f, 2, log) is not { } t) return BatteryKind.Unknown;
        return t[0] switch
        {
            0 or 2 => BatteryKind.Keyboard,
            3 or 4 or 5 => BatteryKind.Mouse,
            8 => BatteryKind.Headset,
            _ => BatteryKind.Other,
        };
    }

    /// <summary>Envía una petición HID++ 2.0 y devuelve los 16 bytes de parámetros de la respuesta, o null.</summary>
    static byte[]? Request(HidStream s, int outLen, byte index, byte feature, byte function, Action<string>? log, params byte[] args) =>
        Request(s, outLen, index, feature, function, log, ReplyTimeoutMs, args);

    static byte[]? Request(HidStream s, int outLen, byte index, byte feature, byte function, Action<string>? log, int timeoutMs, params byte[] args)
    {
        var buf = new byte[Math.Max(outLen, 20)];
        buf[0] = LongReport;
        buf[1] = index;
        buf[2] = feature;
        buf[3] = (byte)((function << 4) | SoftwareId);
        Array.Copy(args, 0, buf, 4, Math.Min(args.Length, 16));
        try { s.Write(buf, 0, outLen); }
        catch (Exception) { return null; }

        var reply = new byte[64];
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            int n;
            s.ReadTimeout = Math.Max(1, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
            try { n = s.Read(reply, 0, reply.Length); }
            catch (TimeoutException) { return null; }
            catch (IOException) { return null; }
            if (n < 7 || reply[1] != index) continue;
            // Error HID++ 2.0 (0xFF) o HID++ 1.0 (0x8F): el dispositivo no está o no entiende la petición.
            if ((reply[2] == 0xFF || reply[2] == 0x8F) && reply[3] == feature) return null;
            if (reply[0] != LongReport || reply[2] != feature || reply[3] != buf[3]) continue; // notificación u otra app
            var p = new byte[16];
            Array.Copy(reply, 4, p, 0, Math.Min(16, n - 4));
            return p;
        }
        return null;
    }

    /// <summary>Curva de descarga típica de una celda Li-ion (mV → %), la misma aproximación que usan Solaar y otros.</summary>
    static readonly (int mV, int Pct)[] Curve =
    {
        (4186, 100), (4067, 90), (3989, 80), (3922, 70), (3859, 60), (3811, 50),
        (3778, 40), (3751, 30), (3717, 20), (3671, 10), (3646, 5), (3579, 2), (3500, 0),
    };

    static int FromMillivolts(int mv)
    {
        if (mv >= Curve[0].mV) return 100;
        for (int i = 1; i < Curve.Length; i++)
        {
            var (hiV, hiP) = Curve[i - 1];
            var (loV, loP) = Curve[i];
            if (mv >= loV) return loP + (hiP - loP) * (mv - loV) / (hiV - loV);
        }
        return 0;
    }
}

/// <summary>
/// ASTRO A50 Gen 5: base USB 046D:0B1C, interfaz HID de fabricante (página 0xFF32). Tramas de 64 bytes con ID 0x02.
/// Petición de batería: 02 0C 03 00 06 0C. Respuesta: 02 0C 06 00 06 00 [nivel %] [nivel2] [en base/cargando].
/// Protocolo según HeadsetControl (lib/devices/logitech_astro_a50.hpp, GPL-3.0).
/// </summary>
static class AstroA50
{
    const int Pid = 0x0B1C;

    public static IEnumerable<BatteryInfo> Read(Action<string>? log)
    {
        var list = new List<BatteryInfo>();
        foreach (var dev in BatteryReader.Collections(0x046D, Pid, 0xFF32))
        {
            if (!dev.TryOpen(out HidStream s)) { log?.Invoke("ASTRO A50: no se pudo abrir la base"); continue; }
            using (s)
            {
                s.ReadTimeout = 800;
                s.WriteTimeout = 800;
                int outLen = dev.GetMaxOutputReportLength();
                var req = new byte[outLen];
                new byte[] { 0x02, 0x0C, 0x03, 0x00, 0x06, 0x0C }.CopyTo(req, 0);
                log?.Invoke($"ASTRO A50: abierta (informe de salida {outLen} bytes)");
                try { s.Write(req, 0, outLen); }
                catch (Exception ex) { log?.Invoke("ASTRO A50: fallo al escribir: " + ex.Message); continue; }

                var reply = new byte[Math.Max(dev.GetMaxInputReportLength(), 64)];
                var deadline = DateTime.UtcNow.AddMilliseconds(1500);
                int? percent = null;
                bool docked = false;
                while (DateTime.UtcNow < deadline)
                {
                    int n;
                    try { n = s.Read(reply, 0, reply.Length); }
                    catch (TimeoutException) { break; }
                    catch (IOException) { break; }
                    log?.Invoke("  recibido: " + BatteryReader.Hex(reply, Math.Min(n, 12)));
                    if (n >= 9 && reply[0] == 0x02 && reply[4] == 0x06)
                    {
                        percent = reply[6] <= 100 ? reply[6] : null;
                        docked = reply[8] == 1;
                        break;
                    }
                }
                list.Add(new BatteryInfo("astro:0B1C", "ASTRO A50", BatteryKind.Headset, percent, docked, "ASTRO"));
            }
        }
        return list;
    }
}
