using System.Collections.Concurrent;

namespace TuringLite;

/// <summary>
/// Sensor calculado "pc.power": potencia de la CPU + la de la GPU dedicada, medidas por sus propios sensores.
/// No incluye placa, RAM, discos, ventiladores ni pérdidas de la fuente: ningún sensor del PC las informa.
/// Cada componente puede sumar unos watts fijos y dividir por la eficiencia de la fuente para estimar el total
/// desde el enchufe; entonces el valor se muestra con "~" porque es una estimación.
/// </summary>
public static class TotalPower
{
    public const string Key = "pc.power";
    public const string Display = "PC · Potencia total (CPU + GPU)";

    /// <summary>GPU integrada por identificador: su consumo ya va dentro del de la CPU y no se suma dos veces.</summary>
    static readonly ConcurrentDictionary<string, bool> Integrated = new();

    public static bool Is(string key) => key == Key;

    /// <summary>Hay algo que sumar si existe al menos la potencia de la CPU o la de la GPU.</summary>
    public static bool Available(ISensorSource s) =>
        s.Canonical("cpu.power") != null || s.Canonical("gpu.power") != null;

    public static bool Track(ISensorSource s)
    {
        bool cpu = s.Track("cpu.power");
        bool gpu = s.Track("gpu.power");
        return cpu || gpu;
    }

    /// <summary>CPU + GPU dedicada en watts, o null si no hay ninguna de las dos lecturas.</summary>
    public static float? Measured(ISensorSource s)
    {
        float? cpu = s.Get("cpu.power");
        float? gpu = null;
        if (s.Canonical("gpu.power") is { } id && !IsIntegrated(s, id)) gpu = s.Get("gpu.power");
        if (cpu == null && gpu == null) return null;
        return (cpu ?? 0) + (gpu ?? 0);
    }

    static bool IsIntegrated(ISensorSource s, string id) =>
        Integrated.GetOrAdd(id, key =>
        {
            var info = s.Catalog().FirstOrDefault(i => i.Key == key);
            // Las integradas suelen llamarse "AMD Radeon(TM) Graphics", "Intel(R) UHD Graphics"...
            return info != null && info.HardwareName.TrimEnd().EndsWith("Graphics", StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>El componente pide una estimación (watts extra o eficiencia de la fuente) y no solo lo medido.</summary>
    public static bool Estimated(WidgetConfig c) => c.ExtraWatts > 0 || c.PsuEfficiency is > 0 and < 100;

    /// <summary>Aplica la estimación del componente: (medido + extra) / eficiencia.</summary>
    public static float Adjust(float measured, WidgetConfig c)
    {
        float v = measured + c.ExtraWatts;
        if (c.PsuEfficiency is > 0 and < 100) v /= c.PsuEfficiency / 100f;
        return v;
    }

    /// <summary>Valor que muestra un componente de tipo sensor (con la estimación si es el total).</summary>
    public static float? ValueFor(WidgetConfig c, ISensorSource? s)
    {
        var v = s?.Get(c.Sensor);
        return v is float f && Is(c.Sensor) ? Adjust(f, c) : v;
    }

    /// <summary>Prefijo del valor: "~" cuando es una estimación.</summary>
    public static string Prefix(WidgetConfig c) => Is(c.Sensor) && Estimated(c) ? "~" : "";
}
