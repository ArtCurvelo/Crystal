using System.Collections.Generic;
using System.Diagnostics;

namespace CrystalExcavator.Core;

/// <summary>Medição simples de tempo por seção (diagnóstico com --perf).</summary>
public static class Prof
{
    public static bool On;
    static readonly Dictionary<string, (double max, double sum, int n)> _d = new();
    public static long Start() => On ? Stopwatch.GetTimestamp() : 0;
    public static void End(string name, long t0)
    {
        if (!On) return;
        double ms = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        _d.TryGetValue(name, out var v);
        _d[name] = (System.Math.Max(v.max, ms), v.sum + ms, v.n + 1);
    }
    public static string Report()
    {
        var parts = new List<string>();
        foreach (var kv in _d) parts.Add($"{kv.Key} máx {kv.Value.max:0.0} méd {kv.Value.sum / System.Math.Max(1, kv.Value.n):0.00}");
        _d.Clear();
        return string.Join(" | ", parts);
    }
}
