using System.IO.Ports;
using Microsoft.Win32;

namespace TuringLite;

public enum Orientation { Portrait = 0, ReversePortrait = 1, Landscape = 2, ReverseLandscape = 3 }

/// <summary>
/// Cliente del protocolo serie "Rev. A" (Turing Smart Screen 3.5", VID 1A86 / PID 5722).
/// Protocolo tomado del proyecto open source turing-smart-screen-python (mathoudebine), GPL-3.0:
/// https://github.com/mathoudebine/turing-smart-screen-python. TuringLite se publica con la misma licencia.
/// </summary>
public sealed class RevADisplay : IDisposable
{
    const int NativeW = 320, NativeH = 480;
    const byte CmdClear = 102, CmdScreenOff = 108, CmdScreenOn = 109, CmdBrightness = 110, CmdOrientation = 121, CmdBitmap = 197;

    readonly object _lock = new();
    readonly string _requestedPort;
    SerialPort? _port;

    public Orientation Orientation { get; private set; } = Orientation.Portrait;
    public int Width => IsLandscape ? NativeH : NativeW;
    public int Height => IsLandscape ? NativeW : NativeH;
    bool IsLandscape => Orientation is Orientation.Landscape or Orientation.ReverseLandscape;

    public int MaxRegionWidth { get; set; }
    public string? PortName => _port?.PortName;
    byte[] _tile = Array.Empty<byte>();

    public RevADisplay(string port = "AUTO") => _requestedPort = port;

    /// <summary>Busca el COM de la pantalla en el registro (sin dependencias extra).</summary>
    public static string? AutoDetect()
    {
        var present = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase);
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\VID_1A86&PID_5722");
        if (usb == null) return null;
        foreach (var instance in usb.GetSubKeyNames())
        {
            using var p = usb.OpenSubKey(instance + @"\Device Parameters");
            if (p?.GetValue("PortName") is string name && present.Contains(name)) return name;
        }
        return null;
    }

    public void Open()
    {
        lock (_lock)
        {
            Close();
            var name = _requestedPort.Equals("AUTO", StringComparison.OrdinalIgnoreCase)
                ? AutoDetect() ?? throw new IOException("No se encontró la pantalla (VID_1A86 PID_5722).")
                : _requestedPort;
            _port = new SerialPort(name, 115200)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                Handshake = Handshake.RequestToSend, // rtscts=True en el original
            };
            _port.Open();
        }
    }

    public void Close()
    {
        lock (_lock)
        {
            try { _port?.Close(); } catch { }
            _port?.Dispose();
            _port = null;
        }
    }

    void SendCommand(byte cmd, int x = 0, int y = 0, int ex = 0, int ey = 0)
    {
        var b = new byte[6];
        b[0] = (byte)(x >> 2);
        b[1] = (byte)(((x & 3) << 6) + (y >> 4));
        b[2] = (byte)(((y & 15) << 4) + (ex >> 6));
        b[3] = (byte)(((ex & 63) << 2) + (ey >> 8));
        b[4] = (byte)(ey & 255);
        b[5] = cmd;
        Write(b, 0, b.Length);
    }

    void Write(byte[] data, int offset, int count)
    {
        if (_port is not { IsOpen: true }) throw new IOException("Puerto cerrado.");
        _port.Write(data, offset, count);
    }

    public void ScreenOff() { lock (_lock) SendCommand(CmdScreenOff); }
    public void ScreenOn() { lock (_lock) SendCommand(CmdScreenOn); }

    /// <summary>0 = apagado visual, 100 = máximo. El hardware usa el valor invertido.</summary>
    public void SetBrightness(int level)
    {
        level = Math.Clamp(level, 0, 100);
        lock (_lock) SendCommand(CmdBrightness, (int)(255 - level / 100.0 * 255));
    }

    public void SetOrientation(Orientation o)
    {
        lock (_lock)
        {
            Orientation = o;
            var b = new byte[16];
            b[5] = CmdOrientation;
            b[6] = (byte)((int)o + 100);
            b[7] = (byte)(Width >> 8); b[8] = (byte)(Width & 255);
            b[9] = (byte)(Height >> 8); b[10] = (byte)(Height & 255);
            Write(b, 0, b.Length);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            // El original exige orientación vertical antes de limpiar.
            var keep = Orientation;
            SetOrientation(Orientation.Portrait);
            SendCommand(CmdClear);
            if (keep != Orientation.Portrait) SetOrientation(keep);
        }
    }

    /// <summary>Envía solo una región. Datos en RGB565 little-endian, w*h*2 bytes.</summary>
    public void DrawRegion(int x, int y, int w, int h, byte[] rgb565, int length)
    {
        if (x < 0 || y < 0 || w <= 0 || h <= 0 || x + w > Width || y + h > Height)
            throw new ArgumentOutOfRangeException(nameof(x), "Región fuera de la pantalla.");
        var widths = StripWidths(w, MaxRegionWidth);
        if (widths.Count == 1) { SendRegion(x, y, w, h, rgb565, length); return; }

        // Divide en franjas verticales copiando las columnas correspondientes de cada fila.
        int tx = 0;
        foreach (int tw in widths)
        {
            int need = tw * h * 2;
            if (_tile.Length < need) _tile = new byte[need];
            for (int row = 0; row < h; row++)
                Buffer.BlockCopy(rgb565, (row * w + tx) * 2, _tile, row * tw * 2, tw * 2);
            SendRegion(x + tx, y, tw, h, _tile, need);
            tx += tw;
        }
    }

    /// <summary>
    /// Anchos de las franjas en que se envía una región. Además del límite MaxRegionWidth,
    /// evita anchos con residuo 2 al dividir entre 8 (50, 250 px...): el firmware los corrompe.
    /// </summary>
    public static List<int> StripWidths(int w, int max)
    {
        var list = new List<int>();
        for (int rest = w; rest > 0;)
        {
            int tw = max > 0 ? Math.Min(max, rest) : rest;
            if (tw % 8 == 2) tw = tw >= 10 ? tw - 6 : 1; // p. ej. 50 -> 44 + 6
            list.Add(tw);
            rest -= tw;
        }
        return list;
    }

    void SendRegion(int x, int y, int w, int h, byte[] rgb565, int length)
    {
        lock (_lock)
        {
            SendCommand(CmdBitmap, x, y, x + w - 1, y + h - 1);
            int chunk = w * 8; // 4 filas por escritura, igual que el original
            for (int off = 0; off < length; off += chunk)
                Write(rgb565, off, Math.Min(chunk, length - off));
        }
    }

    public void Dispose() => Close();
}
