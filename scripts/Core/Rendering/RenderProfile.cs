using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace CrystalExcavator.Core.Rendering;

public enum ProbeStatus { Incompatible = 0, Limited = 1, Approved = 2 }

/// <summary>Resultado do teste de um renderizador (rodado num processo separado, fora da tela).</summary>
public sealed class ProbeResult
{
    public string Method { get; set; } = "";
    /// <summary>Renderizador/driver que o motor realmente usou (a Godot pode cair para OpenGL sozinha).</summary>
    public string ActualMethod { get; set; } = "";
    public string Driver { get; set; } = "";
    public string Adapter { get; set; } = "";
    public string Api { get; set; } = "";
    public bool Started { get; set; }
    /// <summary>A imagem renderizada tinha conteúdo (não ficou preta/vazia).</summary>
    public bool Rendered { get; set; }
    public double FpsMedium { get; set; }
    public double AvgMsMedium { get; set; }
    public double P99MsMedium { get; set; }
    public double FpsLow { get; set; }
    public double P99MsLow { get; set; }
    public double CpuMs { get; set; }
    public double GpuMs { get; set; }
    public int Errors { get; set; }
    public int CriticalErrors { get; set; }
    public double Seconds { get; set; }
    /// <summary>Motivo curto quando falhou (uso interno, vai para o log).</summary>
    public string Note { get; set; } = "";
    public ProbeStatus Status { get; set; }
}

/// <summary>Identificação do computador usada para saber se a avaliação ainda vale.</summary>
public sealed class HardwareFingerprint
{
    public string Os { get; set; } = "";
    public string Cpu { get; set; } = "";
    public int Cores { get; set; }
    public int RamGb { get; set; }
    public string GpuDriver { get; set; } = "";
    public int ScreenW { get; set; }
    public int ScreenH { get; set; }
    public int RefreshHz { get; set; }

    /// <summary>Campos que, se mudarem, pedem nova avaliação (a taxa de atualização não pesa no desempenho).</summary>
    public string Key => $"{Os}|{Cpu}|{Cores}|{RamGb}|{GpuDriver}|{ScreenW}x{ScreenH}";
}

/// <summary>Perfil gráfico salvo localmente (nunca é enviado para fora).</summary>
public sealed class RenderProfile
{
    public int EvalVersion { get; set; }
    public string Engine { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string EvaluatedAt { get; set; } = "";
    public HardwareFingerprint Hardware { get; set; } = new();
    /// <summary>Renderizador aprovado (o jogo inicia com ele).</summary>
    public string Method { get; set; } = RenderMethods.Compatibility;
    /// <summary>Perfil inicial sugerido (0 Baixa, 1 Média, 2 Alta).</summary>
    public int Quality { get; set; } = 1;
    public float RenderScale { get; set; } = 1f;
    /// <summary>Nenhum renderizador atingiu a meta: perfil de recuperação (Baixa + resolução dinâmica).</summary>
    public bool Recovery { get; set; }
    public List<ProbeResult> Results { get; set; } = new();
    /// <summary>Renderizadores que falharam ao iniciar depois de escolhidos (não voltam a ser usados nesta máquina).</summary>
    public List<string> FailedMethods { get; set; } = new();
    /// <summary>APIs gráficas encontradas antes dos testes.</summary>
    public GpuCaps Caps { get; set; }
}

/// <summary>Controle de inicializações: detecta quando o jogo cai antes de chegar ao menu.</summary>
public sealed class BootGuard
{
    public string Method { get; set; } = "";
    public bool Pending { get; set; }
    public int Failures { get; set; }
    /// <summary>Quantas trocas automáticas já foram feitas seguidas (corta qualquer ciclo).</summary>
    public int Switches { get; set; }
}

/// <summary>Arquivos locais do sistema de renderização (pasta do usuário).</summary>
public static class RenderStore
{
    public const string ProfilePath = "user://graphics_profile.json";
    /// <summary>Lido pela Godot ao iniciar (application/config/project_settings_override em project.godot).</summary>
    public const string OverridePath = "user://graphics_override_v2.cfg";
    public const string GuardPath = "user://render_boot.json";
    public const string LogPath = "user://logs/render.log";

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static RenderProfile LoadProfile() => Read<RenderProfile>(ProfilePath);
    public static void SaveProfile(RenderProfile p) => Write(ProfilePath, JsonSerializer.Serialize(p, Json));
    public static BootGuard LoadGuard() => Read<BootGuard>(GuardPath) ?? new BootGuard();
    public static void SaveGuard(BootGuard g) => Write(GuardPath, JsonSerializer.Serialize(g, Json));

    /// <summary>Grava o renderizador que a Godot deve usar nas próximas inicializações.</summary>
    public static void WriteOverride(string method)
    {
        if (!RenderMethods.IsKnown(method)) method = RenderMethods.Compatibility;
        Write(OverridePath, OverrideText(method));
    }

    public static string OverrideText(string method) =>
        "; Gerado pelo Crystal Excavator: renderizador escolhido automaticamente para este computador.\n" +
        "[rendering]\n" +
        $"renderer/rendering_method=\"{method}\"\n";

    /// <summary>Renderizador que está no arquivo de override ("" se não existe).</summary>
    public static string ReadOverrideMethod()
    {
        if (!FileAccess.FileExists(OverridePath)) return "";
        var text = FileAccess.GetFileAsString(OverridePath) ?? "";
        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("renderer/rendering_method=")) return t.Substring(t.IndexOf('=') + 1).Trim().Trim('"');
        }
        return "";
    }

    public static void Log(string msg)
    {
        try
        {
            DirAccess.MakeDirRecursiveAbsolute("user://logs");
            string line = $"{Time.GetDatetimeStringFromSystem()} {msg}\n";
            // arquivo pequeno: recomeça quando passa de 200 KB
            bool append = FileAccess.FileExists(LogPath) && FileAccess.GetFileAsString(LogPath).Length < 200_000;
            using var f = append ? FileAccess.Open(LogPath, FileAccess.ModeFlags.ReadWrite) : FileAccess.Open(LogPath, FileAccess.ModeFlags.Write);
            if (f == null) return;
            f.SeekEnd();
            f.StoreString(line);
        }
        catch { }
    }

    static T Read<T>(string path) where T : class
    {
        try
        {
            if (!FileAccess.FileExists(path)) return null;
            return JsonSerializer.Deserialize<T>(FileAccess.GetFileAsString(path));
        }
        catch { return null; }
    }

    static void Write(string path, string text)
    {
        try
        {
            using var f = FileAccess.Open(path, FileAccess.ModeFlags.Write);
            f?.StoreString(text);
        }
        catch (Exception e) { GD.PushWarning($"[Render] falha ao gravar {path}: {e.Message}"); }
    }
}
