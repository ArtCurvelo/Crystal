using System;
using System.Linq;
using Godot;

namespace CrystalExcavator.Core.Rendering;

/// <summary>Detecta o hardware relevante (tudo local; nada é enviado para fora).</summary>
public static class HardwareInfo
{
    public static HardwareFingerprint Collect()
    {
        var hw = new HardwareFingerprint();
        try { hw.Os = OS.GetName() + " " + OS.GetVersion(); } catch { }
        try { hw.Cpu = OS.GetProcessorName(); } catch { }
        try { hw.Cores = OS.GetProcessorCount(); } catch { }
        try
        {
            var mem = OS.GetMemoryInfo();
            if (mem.ContainsKey("physical")) hw.RamGb = (int)Math.Round((long)mem["physical"] / 1073741824.0);
        }
        catch { }
        try
        {
            // nome e versão do driver de vídeo (Windows: registro; independe do renderizador em uso)
            var info = OS.GetVideoAdapterDriverInfo();
            hw.GpuDriver = string.Join(" ", info.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        catch { }
        try
        {
            if (DisplayServer.GetName() != "headless")
            {
                int s = DisplayServer.WindowGetCurrentScreen();
                var size = DisplayServer.ScreenGetSize(s);
                hw.ScreenW = size.X; hw.ScreenH = size.Y;
                double hz = DisplayServer.ScreenGetRefreshRate(s);
                hw.RefreshHz = hz > 0 && hz < 1000 ? (int)Math.Round(hz) : 0; // desconhecida em alguns drivers
            }
        }
        catch { }
        return hw;
    }

    /// <summary>Resumo legível para o log interno.</summary>
    public static string Describe(HardwareFingerprint hw) =>
        $"SO {hw.Os} · CPU {hw.Cpu} ({hw.Cores} núcleos) · RAM {hw.RamGb} GB · driver '{hw.GpuDriver}' · tela {hw.ScreenW}x{hw.ScreenH}@{hw.RefreshHz}Hz";
}
