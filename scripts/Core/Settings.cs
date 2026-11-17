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
    public int MaxFps { get; set; } = 60;

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

    public static void Load()
    {
        try
        {
            if (FileAccess.FileExists(Path))
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
