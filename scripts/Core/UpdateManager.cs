using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace CrystalExcavator.Core;

/// <summary>Manifesto publicado no ramo "updates" do repositório.</summary>
public sealed class UpdateManifest
{
    public string Version { get; set; } = "0.0.0";
    public string Engine { get; set; } = "";
    public string Notes { get; set; } = "";
    public string SetupUrl { get; set; } = "";
    public List<UpdateFile> Files { get; set; } = new();
}

public sealed class UpdateFile
{
    /// <summary>Caminho relativo à pasta do executável (ex.: "CrystalExcavator.pck").</summary>
    public string Path { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

/// <summary>
/// Atualização automática: ao iniciar, consulta o manifesto publicado; se houver versão nova,
/// baixa só os arquivos do jogo (.pck + código C#), confere SHA-256, fecha o jogo e um script
/// aplica os arquivos e reabre o jogo. Só atua no jogo exportado para Windows.
/// </summary>
public partial class UpdateManager : Node
{
    public const string ManifestUrl = "https://raw.githubusercontent.com/ArtCurvelo/Crystal/updates/update.json";

    public static string CurrentVersion => (string)ProjectSettings.GetSetting("application/config/version", "0.0.0");
    public static string EngineVersion => $"{Engine.GetVersionInfo()["major"]}.{Engine.GetVersionInfo()["minor"]}.{Engine.GetVersionInfo()["patch"]}";

    /// <summary>Atualização automática só faz sentido no executável exportado para Windows.</summary>
    public static bool Supported => OS.HasFeature("windows") && OS.HasFeature("template") && !OS.HasFeature("editor");

    public UpdateManifest Available { get; private set; }
    public bool NeedsFullInstaller { get; private set; }
    public event Action<UpdateManifest> UpdateFound;
    public event Action<float, string> Progress;   // (0..1, texto)
    public event Action<string> Failed;
    public event Action<string> Status;   // mensagens curtas para o menu

    HttpRequest _http;

    public override void _Ready()
    {
        _http = new HttpRequest { Timeout = 20, UseThreads = true };
        AddChild(_http);
    }

    // ------------------------------------------------------------ verificação
    bool _busy;
    /// <summary>Ao reabrir depois da atualização, volta direto para a partida salva.</summary>
    public bool ResumeAfterUpdate;

    public async void CheckForUpdates()
    {
        if (!Supported || !Settings.Data.CheckUpdates || _busy) return;
        // já achou uma versão nova: só mostra de novo (sem baixar o manifesto outra vez)
        if (Available != null) { UpdateFound?.Invoke(Available); return; }
        _busy = true;
        try { await Check(); } finally { _busy = false; }
    }

    async Task Check()
    {
        Status?.Invoke(Loc.T("Procurando atualizações…"));
        var bytes = await Fetch(ManifestUrl + "?t=" + Time.GetUnixTimeFromSystem());
        if (bytes == null) { Status?.Invoke(Loc.T("Sem conexão para verificar atualizações")); return; }
        var m = ParseManifest(System.Text.Encoding.UTF8.GetString(bytes));
        if (m == null || !IsNewer(m.Version, CurrentVersion)) { Status?.Invoke(Loc.T("✓ Jogo atualizado")); return; }
        Status?.Invoke(Loc.F("Nova versão v{0} disponível", m.Version));
        Available = m;
        NeedsFullInstaller = !string.IsNullOrEmpty(m.Engine) && m.Engine != EngineVersion;
        UpdateFound?.Invoke(m);
    }

    public static UpdateManifest ParseManifest(string json)
    {
        try
        {
            var m = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (m == null || string.IsNullOrWhiteSpace(m.Version) || m.Files == null || m.Files.Count == 0) return null;
            foreach (var f in m.Files)
            {
                // segurança: apenas os arquivos do jogo conhecidos (lista fechada), em qualquer sistema
                if (!AllowedPath(f.Path)) return null;
                if (string.IsNullOrWhiteSpace(f.Url) || !f.Url.StartsWith("https://")) return null;
                if (f.Sha256 == null || f.Sha256.Length != 64) return null;
            }
            return m;
        }
        catch { return null; }
    }

    static readonly System.Text.RegularExpressions.Regex Allowed = new(
        @"^(CrystalExcavator\.pck|data_CrystalExcavator_windows_x86_64/CrystalExcavator\.(dll|deps\.json|runtimeconfig\.json))$");

    public static bool AllowedPath(string p) => !string.IsNullOrEmpty(p) && Allowed.IsMatch(p);

    /// <summary>Compara versões "a.b.c" numericamente.</summary>
    public static bool IsNewer(string candidate, string current)
    {
        int[] P(string v)
        {
            var parts = (v ?? "0").Split('.');
            var r = new int[3];
            for (int i = 0; i < 3 && i < parts.Length; i++) int.TryParse(parts[i], out r[i]);
            return r;
        }
        var a = P(candidate); var b = P(current);
        for (int i = 0; i < 3; i++) if (a[i] != b[i]) return a[i] > b[i];
        return false;
    }

    public static string Sha256Of(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    // ------------------------------------------------------------ download + aplicação
    public async void DownloadAndApply()
    {
        var m = Available;
        if (m == null || _busy) return;
        _busy = true;
        string staging = ProjectSettings.GlobalizePath("user://update_staging");
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);
            for (int i = 0; i < m.Files.Count; i++)
            {
                var f = m.Files[i];
                Progress?.Invoke((float)i / m.Files.Count, Loc.F("Baixando {0}…", System.IO.Path.GetFileName(f.Path)));
                var data = await Fetch(f.Url);
                if (data == null) { Failed?.Invoke(Loc.T("Falha no download. Verifique a conexão e tente de novo.")); return; }
                string dst = System.IO.Path.Combine(staging, f.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dst));
                File.WriteAllBytes(dst, data);
                if (!string.Equals(Sha256Of(dst), f.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Failed?.Invoke(Loc.F("Arquivo corrompido no download ({0}). Tente de novo.", f.Path));
                    return;
                }
            }
            Progress?.Invoke(1f, Loc.T("Aplicando atualização…"));
            string gameDir = System.IO.Path.GetDirectoryName(OS.GetExecutablePath());
            string script = WriteApplyScript(staging, gameDir, OS.GetExecutablePath(), m.Files, OS.GetProcessId(), ResumeAfterUpdate && SaveSystem.HasSave());
            // só salva se houver partida em andamento; e guarda um backup antes de atualizar
            if (GameState.Instance != null && GameState.Instance.IsPlaying) GameState.Instance.SaveGame(true);
            SaveSystem.Snapshot("antes-da-v" + m.Version);
            OS.CreateProcess("cmd.exe", new[] { "/c", script });
            GameState.Instance?.QuitClean();
        }
        catch (Exception e)
        {
            Failed?.Invoke(Loc.T("Erro ao atualizar: ") + e.Message);
        }
        finally { _busy = false; }
    }

    /// <summary>Script .bat: espera o jogo fechar, copia os arquivos baixados e reabre o jogo.</summary>
    public static string WriteApplyScript(string staging, string gameDir, string exePath, List<UpdateFile> files, int pid, bool resume = false)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("chcp 65001 >nul");
        sb.AppendLine("title Atualizando Crystal Excavator");
        sb.AppendLine(":wait");
        sb.AppendLine(string.Format("tasklist /FI \"PID eq {0}\" 2>nul | find \"{1}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)", pid, pid));
        foreach (var f in files)
        {
            string rel = f.Path.Replace('/', '\\');
            sb.AppendLine(string.Format("copy /Y \"{0}\\{1}\" \"{2}\\{3}\" >nul", staging, rel, gameDir, rel));
        }
        sb.AppendLine(string.Format("start \"\" \"{0}\"", exePath) + (resume ? " -- --continue" : ""));
        string path = System.IO.Path.Combine(staging, "aplicar_atualizacao.bat");
        File.WriteAllText(path, sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));
        return path;
    }

    async Task<byte[]> Fetch(string url)
    {
        if (_http.GetHttpClientStatus() != HttpClient.Status.Disconnected) _http.CancelRequest();
        if (_http.Request(url) != Error.Ok) return null;
        var r = await ToSignal(_http, HttpRequest.SignalName.RequestCompleted);
        long result = (long)r[0];
        long code = (long)r[1];
        if (result != (long)HttpRequest.Result.Success || code != 200) return null;
        return r[3].AsByteArray();
    }
}
