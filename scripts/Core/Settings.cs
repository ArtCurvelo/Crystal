using System;
using System.Text.Json;
using Godot;

namespace CrystalExcavator.Core;

public enum GraphicsQuality { Low = 0, Medium = 1, High = 2 }

public sealed class SettingsData
{
    public float MouseSensitivity { get; set; } = 0.22f;
    public bool InvertY { get; set; }
    public float Fov { get; set; } = 75f;
    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.6f;
    public float SfxVolume { get; set; } = 0.9f;
    public int Quality { get; set; } = (int)GraphicsQuality.Medium;
    public bool Fullscreen { get; set; }
    public bool VSync { get; set; } = true;
    public float RenderScale { get; set; } = 1f;
    public bool ShowFps { get; set; }
    /// <summary>Ajusta a resolução 3D automaticamente para manter ~30 FPS.</summary>
    public bool DynamicResolution { get; set; } = true;
    /// <summary>Procurar atualizações ao iniciar o jogo.</summary>
    public bool CheckUpdates { get; set; } = true;
    public int MaxFps { get; set; } = 60;
    public int WindowSize { get; set; } = 0; // índice em Settings.WindowSizes

    public void Sanitize()
    {
        static float ok(float v, float min, float max, float def) =>
            float.IsNaN(v) || float.IsInfinity(v) ? def : Math.Clamp(v, min, max);
        MouseSensitivity = ok(MouseSensitivity, 0.02f, 1.5f, 0.22f);
        Fov = ok(Fov, 55f, 100f, 75f);
        MasterVolume = ok(MasterVolume, 0f, 1f, 0.8f);
        MusicVolume = ok(MusicVolume, 0f, 1f, 0.6f);
        SfxVolume = ok(SfxVolume, 0f, 1f, 0.9f);
        RenderScale = ok(RenderScale, 0.5f, 1f, 1f);
        Quality = Math.Clamp(Quality, 0, 2);
        WindowSize = Math.Clamp(WindowSize, 0, Settings.WindowSizes.Length - 1);
        if (MaxFps != 0) MaxFps = Math.Clamp(MaxFps, 30, 240);
    }
}

/// <summary>Carrega, salva e aplica configurações do usuário.</summary>
public static class Settings
{
    public const string Path = "user://settings.json";
    public static SettingsData Data { get; private set; } = new();
    public static event Action Changed;

    public static GraphicsQuality Quality => (GraphicsQuality)Data.Quality;
    public static readonly Vector2I[] WindowSizes = { new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080) };

    /// <summary>True quando as configurações foram criadas agora (primeira execução).</summary>
    public static bool FirstRun { get; private set; }

    public static void Load()
    {
        try
        {
            FirstRun = !FileAccess.FileExists(Path);
            if (FirstRun) AutoDetect();
            else
            {
                var d = JsonSerializer.Deserialize<SettingsData>(FileAccess.GetFileAsString(Path));
                if (d != null) Data = d;
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"Configurações inválidas, usando padrões: {e.Message}");
            Data = new SettingsData();
        }
        Data.Sanitize();
        // execuções headless (testes, exportação) não devem fixar configurações sem GPU real
        if (FirstRun && DisplayServer.GetName() != "headless") Save();
    }

    /// <summary>Na primeira execução escolhe preset conforme a GPU (integradas Intel antigas → Baixa).</summary>
    static void AutoDetect()
    {
        Data = new SettingsData();
        string gpu = "";
        try { gpu = RenderingServer.GetVideoAdapterName() ?? ""; } catch { }
        string g = gpu.ToLowerInvariant();
        bool weak = g.Contains("intel") && (g.Contains("hd graphics") || g.Contains("uhd") || g.Contains("haswell") || g.Contains("ivybridge"))
                    || g.Contains("llvmpipe") || g.Contains("swiftshader");
        if (weak)
        {
            Data.Quality = (int)GraphicsQuality.Low;
            Data.RenderScale = 0.75f;
        }
        GD.Print($"[Configurações] GPU: '{gpu}' → qualidade {(GraphicsQuality)Data.Quality}");
    }

    public static void Save()
    {
        Data.Sanitize();
        try
        {
            using var f = FileAccess.Open(Path, FileAccess.ModeFlags.Write);
            f?.StoreString(JsonSerializer.Serialize(Data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { GD.PushError($"Falha ao salvar configurações: {e.Message}"); }
    }

    /// <summary>Aplica configurações globais (janela, áudio, limites). Elementos de cena reagem ao evento Changed.</summary>
    public static void Apply(SceneTree tree)
    {
        Data.Sanitize();
        if (DisplayServer.GetName() != "headless")
        {
            var mode = Data.Fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
            if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
            if (!Data.Fullscreen)
            {
                var size = WindowSizes[Data.WindowSize];
                var screen = DisplayServer.ScreenGetUsableRect();
                // não maior que a tela disponível
                size = new Vector2I(Math.Min(size.X, screen.Size.X), Math.Min(size.Y, screen.Size.Y));
                if (DisplayServer.WindowGetSize() != size)
                {
                    DisplayServer.WindowSetSize(size);
                    DisplayServer.WindowSetPosition(screen.Position + (screen.Size - size) / 2);
                }
            }
            DisplayServer.WindowSetVsyncMode(Data.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        }
        Engine.MaxFps = Data.MaxFps;
        SetBus("Master", Data.MasterVolume);
        SetBus("Music", Data.MusicVolume);
        SetBus("SFX", Data.SfxVolume);
        if (tree?.Root != null)
        {
            tree.Root.Scaling3DScale = Data.RenderScale;
        }
        Changed?.Invoke();
    }

    static void SetBus(string name, float linear)
    {
        int idx = AudioServer.GetBusIndex(name);
        if (idx < 0) return;
        AudioServer.SetBusVolumeDb(idx, linear <= 0.001f ? -80f : Mathf.LinearToDb(linear));
        AudioServer.SetBusMute(idx, linear <= 0.001f);
    }
}
