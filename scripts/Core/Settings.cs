using System;
using System.Collections.Generic;
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
    /// <summary>Formato das configurações: 2 = padrões de alto FPS (sem VSync, limite 144, anti-aliasing).</summary>
    public int Version { get; set; }
    public bool VSync { get; set; }
    public float RenderScale { get; set; } = 1f;
    public bool ShowFps { get; set; }
    /// <summary>Ajusta a resolução 3D automaticamente para segurar o FPS alvo (120, ou o limite se for menor).</summary>
    public bool DynamicResolution { get; set; } = true;
    /// <summary>O jogador mudou a qualidade no menu: a escolha automática nunca a sobrescreve.</summary>
    public bool QualityManual { get; set; }
    /// <summary>Suavização de bordas: 0 desligada, 1 FXAA, 2 SMAA, 3 temporal (FSR 2).</summary>
    public int AntiAliasing { get; set; } = 2;
    /// <summary>Procurar atualizações ao iniciar o jogo.</summary>
    public bool CheckUpdates { get; set; } = true;
    public int MaxFps { get; set; } = 144;
    public int WindowSize { get; set; } = 0; // índice em Settings.WindowSizes
    // ---- geral
    public string Language { get; set; } = "pt";
    // ---- vídeo
    /// <summary>Brilho geral (exposição), 0,6 … 1,8.</summary>
    public float Brightness { get; set; } = 1f;
    /// <summary>Escala da interface (textos e painéis), 0,8 … 1,5.</summary>
    public float UiScale { get; set; } = 1f;
    // ---- controle
    public float PadLookSensitivity { get; set; } = 1f;
    public bool PadVibration { get; set; } = true;
    /// <summary>Teclas/mouse remapeados: ação → eventos ("k:código" ou "m:botão").</summary>
    public Dictionary<string, List<string>> KeyBinds { get; set; } = new();
    /// <summary>Botões do controle remapeados: ação → botões.</summary>
    public Dictionary<string, List<int>> PadBinds { get; set; } = new();
    // ---- acessibilidade
    /// <summary>0 nenhum, 1 protanopia, 2 deuteranopia, 3 tritanopia.</summary>
    public int Colorblind { get; set; }
    public float ColorblindStrength { get; set; } = 1f;
    public bool AudioCues { get; set; }
    public bool Narration { get; set; }
    public bool ReduceShake { get; set; }
    /// <summary>Acessibilidade: minerar segurando o botão desde o início (sem precisar clicar a cada golpe).</summary>
    public bool HoldToMine { get; set; }
    public bool HighContrastHud { get; set; }

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
        AntiAliasing = Math.Clamp(AntiAliasing, 0, 3);
        if (Array.IndexOf(Loc.Languages, Language) < 0) Language = "pt";
        Brightness = ok(Brightness, 0.6f, 1.8f, 1f);
        UiScale = ok(UiScale, 0.8f, 1.5f, 1f);
        PadLookSensitivity = ok(PadLookSensitivity, 0.3f, 3f, 1f);
        ColorblindStrength = ok(ColorblindStrength, 0f, 1f, 1f);
        Colorblind = Math.Clamp(Colorblind, 0, 3);
        KeyBinds ??= new();
        PadBinds ??= new();
    }
}

/// <summary>Carrega, salva e aplica configurações do usuário.</summary>
public static class Settings
{
    public const string Path = "user://settings.json";
    public static SettingsData Data { get; private set; } = new();
    public static event Action Changed;

    public static GraphicsQuality Quality => (GraphicsQuality)Data.Quality;

    /// <summary>
    /// Renderizador avançado (Forward+/Vulkan) ativo? Em GPUs sem Vulkan o Godot cai para OpenGL
    /// (Compatibility) e os efeitos exclusivos (névoa volumétrica, SSAO) ficam desligados.
    /// </summary>
    public static bool Advanced
    {
        get
        {
            try { return Rendering.RenderMethods.Current is Rendering.RenderMethods.ForwardPlus or Rendering.RenderMethods.Mobile; }
            catch { return false; }
        }
    }
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
                if (Data.Version < CurrentVersion) Migrate(Data);
            }
        }
        catch (Exception e)
        {
            GD.PushWarning($"Configurações inválidas, usando padrões: {e.Message}");
            Data = new SettingsData();
        }
        // --lang=en / --colorblind=1 na linha de comando (capturas de tela e testes)
        foreach (var a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--lang=")) Data.Language = a.Substring(7);
            else if (a.StartsWith("--colorblind=") && int.TryParse(a.Substring(13), out int cb)) Data.Colorblind = cb;
        }
        Data.Sanitize();
        // execuções headless (testes, exportação) não devem fixar configurações sem GPU real
        if (FirstRun && DisplayServer.GetName() != "headless") Save();
    }

    public const int CurrentVersion = 3;

    /// <summary>
    /// Configurações de versões antigas: o padrão era VSync + limite de 60 FPS (nunca passava de 60).
    /// Quem estava no padrão passa para o novo (sem VSync, até 144 FPS, resolução dinâmica segurando 120).
    /// Placas de vídeo fortes que estavam no padrão (Média) sobem para Alta.
    /// </summary>
    public static void Migrate(SettingsData d, string gpu = null)
    {
        if (d.Version < 2)
        {
            if (d.VSync && d.MaxFps == 60) { d.VSync = false; d.MaxFps = 144; }
            if (d.Quality == (int)GraphicsQuality.Medium && StrongGpu(gpu ?? AdapterName())) d.Quality = (int)GraphicsQuality.High;
            d.AntiAliasing = 2;
        }
        if (d.Version < 3)
        {
            // antes não havia como saber se a qualidade foi escolhida à mão; "Baixa" quase sempre é escolha do jogador
            d.QualityManual = d.Quality == (int)GraphicsQuality.Low;
        }
        d.Version = CurrentVersion;
    }

    static string AdapterName()
    {
        try { return RenderingServer.GetVideoAdapterName() ?? ""; } catch { return ""; }
    }

    /// <summary>Placas dedicadas modernas (RTX, RX 6000/7000/9000, Arc): aguentam a qualidade Alta.</summary>
    public static bool StrongGpu(string gpu)
    {
        string g = (gpu ?? "").ToLowerInvariant();
        return g.Contains("rtx") || g.Contains("rx 6") || g.Contains("rx 7") || g.Contains("rx 9") || g.Contains("arc a") || g.Contains("arc b");
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
        Data.Version = CurrentVersion;
        if (weak)
        {
            Data.Quality = (int)GraphicsQuality.Low;
            Data.RenderScale = 0.75f;
            Data.AntiAliasing = 1;
        }
        else if (StrongGpu(gpu)) Data.Quality = (int)GraphicsQuality.High;
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
            tree.Root.ContentScaleFactor = Data.UiScale;
            ApplyAntiAliasing(tree.Root);
        }
        InputSetup.ApplyBindings();
        Accessibility.Apply();
        Changed?.Invoke();
    }

    /// <summary>
    /// Suavização de bordas e reescala 3D. FSR 1 reescala barato quando a resolução dinâmica baixa;
    /// a opção temporal usa FSR 2 (também suaviza na resolução cheia). Sem Vulkan só há FXAA.
    /// </summary>
    public static void ApplyAntiAliasing(Viewport vp)
    {
        var f = Rendering.RenderFeatures.Current;
        int aa = Data.AntiAliasing;
        // a opção pedida, ou a melhor alternativa que este renderizador tem
        if (aa == 3 && !f.Fsr2) aa = 2;
        if (aa == 2 && !f.Smaa) aa = f.Msaa != Viewport.Msaa.Disabled ? 4 : 1;
        vp.ScreenSpaceAA = aa switch
        {
            1 when f.Fxaa || f.Smaa => Viewport.ScreenSpaceAAEnum.Fxaa,
            2 => Viewport.ScreenSpaceAAEnum.Smaa,
            _ => Viewport.ScreenSpaceAAEnum.Disabled,
        };
        vp.Msaa3D = aa == 4 || (aa == 1 && !f.Smaa) ? f.Msaa : Viewport.Msaa.Disabled;
        vp.Scaling3DMode = aa == 3 && f.Fsr2 ? Viewport.Scaling3DModeEnum.Fsr2 : f.Fsr1 ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        vp.FsrSharpness = 0.35f;
        // a única sombra dinâmica é a da lanterna: atlas de 2048 basta (o padrão 4096 custava memória e tempo de GPU)
        vp.PositionalShadowAtlasSize = 2048;
        vp.UseTaa = false;
        // cavernas escuras: gradientes suaves sem faixas
        vp.UseDebanding = f.Debanding;
    }

    static void SetBus(string name, float linear)
    {
        int idx = AudioServer.GetBusIndex(name);
        if (idx < 0) return;
        AudioServer.SetBusVolumeDb(idx, linear <= 0.001f ? -80f : Mathf.LinearToDb(linear));
        AudioServer.SetBusMute(idx, linear <= 0.001f);
    }
}
