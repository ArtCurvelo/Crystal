using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.Core.Rendering;

/// <summary>Conta erros do motor durante o teste (erros de shader e do driver são críticos).</summary>
public partial class ProbeLogger : Logger
{
    int _errors, _critical;
    public int Errors => _errors;
    public int Critical => _critical;
    public string LastCritical = "";

    public override void _LogError(string function, string file, int line, string code, string rationale, bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
    {
        if (errorType == (int)ErrorType.Warning) return;
        Interlocked.Increment(ref _errors);
        string text = (code + " " + rationale + " " + file).ToLowerInvariant();
        bool critical = errorType == (int)ErrorType.Shader || text.Contains("shader") || text.Contains("device lost") || text.Contains("vk_error")
                        || text.Contains("out of memory") || text.Contains("out_of_memory") || text.Contains("pipeline");
        if (critical) { Interlocked.Increment(ref _critical); LastCritical = (code + " " + rationale).Trim(); }
    }

    public override void _LogMessage(string message, bool error) { }
}

/// <summary>
/// Teste de um renderizador, num processo separado aberto fora da tela pelo <see cref="RenderBootstrap"/>.
/// Usa a cena real do jogo: mineração com partículas e luzes, deslocamento pelo acampamento e caverna.
/// Mede FPS médio, tempo médio e percentil 99 por quadro, CPU e GPU, erros do motor e confere se a imagem
/// tem conteúdo. Se não bater a meta na Média, mede também na Baixa. Grava um JSON e fecha.
/// Nunca toca no save nem nas configurações do jogador.
/// </summary>
public partial class RenderProbe : Node
{
    public Main Main;
    public string Method = "";
    public string OutPath = "user://probe.json";

    readonly ProbeLogger _log = new();
    readonly ProbeResult _r = new();
    readonly List<double> _frames = new();
    bool _recording;
    ulong _last;
    double _cpuSum, _gpuSum; int _samples;
    ulong _t0;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _t0 = Time.GetTicksMsec();
        OS.AddLogger(_log);
        _r.Method = Method;
        _r.ActualMethod = RenderMethods.Current;
        try { _r.Driver = RenderingServer.GetCurrentRenderingDriverName(); _r.Adapter = RenderingServer.GetVideoAdapterName(); _r.Api = RenderingServer.GetVideoAdapterApiVersion(); } catch { }
        string sim = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--probe-simulate="))?.Split('=')[1] ?? "";
        if (sim == "crash") { OS.Kill(OS.GetProcessId()); return; }       // processo morre sem resultado
        if (sim == "hang") return;                                          // nunca responde (o tempo limite age)
        // a Godot caiu para outro renderizador (ex.: sem Vulkan): não adianta medir
        if (_r.ActualMethod != Method && sim != "fast") { _r.Started = true; _r.Note = $"o motor abriu em {_r.ActualMethod}"; Finish(); return; }

        // vigia: nunca passa de 45 s, mesmo que algo trave
        var watchdog = GetTree().CreateTimer(45, true, false, true);
        watchdog.Timeout += () => { _r.Note = "tempo esgotado dentro do teste"; Finish(); };
        // se o jogo que pediu o teste fechar, este processo também fecha (nada fica rodando escondido)
        var parent = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--probe-parent="));
        if (parent != null && int.TryParse(parent.Split('=')[1], out int ppid))
        {
            var t = new Godot.Timer { WaitTime = 1, Autostart = true, ProcessMode = ProcessModeEnum.Always };
            AddChild(t);
            // OS.IsProcessRunning só conhece processos criados por este: usa a consulta do .NET
            t.Timeout += () => { if (!ParentAlive(ppid)) OS.Kill(OS.GetProcessId()); };
        }

        // ambiente do teste: sem som, sem save do jogador, configurações só em memória
        AudioServer.SetBusMute(0, true);
        SaveSystem.SavePath = "user://probe_save.json";
        Settings.Data.Quality = (int)GraphicsQuality.Medium;
        Settings.Data.RenderScale = 1f;
        Settings.Data.DynamicResolution = false;
        Settings.Data.VSync = false;
        Settings.Data.MaxFps = 0;
        Settings.Data.ShowFps = false;
        Settings.Apply(GetTree());
        var vp = GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vp, true);
        _r.Started = true;

        await Wait(0.3);
        Main.StartGame(false);
        await Wait(0.8);
        var p = Main.Player;
        var W = Main.World;
        if (p == null || W == null) { _r.Note = "cena não montou"; Finish(); return; }
        p.SimulateCapturedMouse = true;

        // vista 1: mineração (partículas, luz do feixe, minério brilhando)
        var crystal = W.Crystals.FirstOrDefault(c => c.Material.Id == "quartz" && c.GlobalTransform.Basis.Y.Normalized().Y > 0.95f)
                      ?? W.Crystals.FirstOrDefault(c => !c.Material.Special);
        if (crystal != null)
        {
            p.GlobalPosition = crystal.StandPoint;
            var d = crystal.Center - crystal.StandPoint;
            p.SetLook(Mathf.Atan2(-d.X, -d.Z), -0.35f);
        }
        p.Velocity = Vector3.Zero;
        Input.ActionPress("mine");
        await Wait(1.2); // aquece (compilação de shaders, streaming)
        _r.Rendered = await ImageHasContent();
        if (sim == "black") _r.Rendered = false;
        await Measure(2.6);
        Input.ActionRelease("mine");
        // vista 2: deslocamento pelo caminho principal (muita geometria e luzes à frente)
        var L = W.Layouts[0];
        var a = L.PathPoints[Math.Min(2, L.PathPoints.Count - 1)];
        var b = L.PathPoints[Math.Min(4, L.PathPoints.Count - 1)];
        p.GlobalPosition = L.OnGround(a.X, a.Z, 0.1f);
        p.SetLook(Mathf.Atan2(-(b.X - a.X), -(b.Z - a.Z)), 0f);
        Input.ActionPress("move_forward");
        await Wait(0.4);
        await Measure(1.8, append: true);
        Input.ActionRelease("move_forward");
        Summ(out double fps, out double avg, out double p99);
        if (sim == "slow") { fps = 30; avg = 33.3; p99 = 50; }
        if (sim == "fast") { fps = 130; avg = 7.7; p99 = 11; _r.ActualMethod = Method; }
        _r.FpsMedium = fps; _r.AvgMsMedium = avg; _r.P99MsMedium = p99;
        _r.CpuMs = _samples > 0 ? _cpuSum / _samples : 0;
        _r.GpuMs = _samples > 0 ? _gpuSum / _samples : 0;

        // não bateu a meta na Média: mede a Baixa (corta primeiro os efeitos mais caros)
        if (!(fps >= RendererSelector.TargetFps && p99 <= RendererSelector.MaxP99Ms))
        {
            Settings.Data.Quality = (int)GraphicsQuality.Low;
            Settings.Apply(GetTree());
            if (crystal != null) { p.GlobalPosition = crystal.StandPoint; var d = crystal.Center - crystal.StandPoint; p.SetLook(Mathf.Atan2(-d.X, -d.Z), -0.35f); }
            Input.ActionPress("mine");
            await Wait(0.8);
            _frames.Clear();
            await Measure(2.2);
            Input.ActionRelease("mine");
            Summ(out double fl, out _, out double pl);
            if (sim == "slow") { fl = 40; pl = 40; }
            _r.FpsLow = fl; _r.P99MsLow = pl;
        }
        Finish();
    }

    static bool ParentAlive(int pid)
    {
        try { using var p = System.Diagnostics.Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }

    async Task Measure(double seconds, bool append = false)
    {
        if (!append) _frames.Clear();
        _last = Time.GetTicksUsec();
        _recording = true;
        await Wait(seconds);
        _recording = false;
    }

    public override void _Process(double delta)
    {
        if (!_recording) return;
        ulong now = Time.GetTicksUsec();
        _frames.Add((now - _last) / 1000.0);
        _last = now;
        _cpuSum += Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        _gpuSum += RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid());
        _samples++;
    }

    void Summ(out double fps, out double avg, out double p99)
    {
        fps = avg = p99 = 0;
        if (_frames.Count < 5) return;
        var f = _frames.Skip(1).ToList();
        avg = f.Average();
        fps = 1000.0 / avg;
        var sorted = f.OrderBy(x => x).ToList();
        p99 = sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(sorted.Count * 0.99) - 1)];
    }

    /// <summary>A imagem tem conteúdo de verdade (não preta, não uniforme)?</summary>
    async Task<bool> ImageHasContent()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture()?.GetImage();
        if (img == null || img.IsEmpty()) return false;
        int w = img.GetWidth(), h = img.GetHeight();
        double sum = 0, sum2 = 0; int n = 0;
        for (int y = 0; y < h; y += Math.Max(1, h / 48))
            for (int x = 0; x < w; x += Math.Max(1, w / 64))
            {
                var c = img.GetPixel(x, y);
                double l = c.R * 0.2126 + c.G * 0.7152 + c.B * 0.0722;
                sum += l; sum2 += l * l; n++;
            }
        double mean = sum / n, std = Math.Sqrt(Math.Max(0, sum2 / n - mean * mean));
        return mean > 0.01 && std > 0.01;
    }

    bool _done;
    void Finish()
    {
        if (_done) return;
        _done = true;
        _r.Errors = _log.Errors;
        _r.CriticalErrors = _log.Critical;
        if (_log.Critical > 0 && string.IsNullOrEmpty(_r.Note)) _r.Note = "erro crítico: " + _log.LastCritical;
        _r.Seconds = (Time.GetTicksMsec() - _t0) / 1000.0;
        _r.Status = RendererSelector.Classify(_r);
        try
        {
            using var f = FileAccess.Open(OutPath, FileAccess.ModeFlags.Write);
            f?.StoreString(JsonSerializer.Serialize(_r));
        }
        catch { }
        SaveSystem.Delete();
        OS.RemoveLogger(_log);
        // alguns drivers Vulkan demoram (ou travam) para fechar: força a saída se não fechar logo
        new System.Threading.Thread(() => { System.Threading.Thread.Sleep(3000); OS.Kill(OS.GetProcessId()); }) { IsBackground = true }.Start();
        GetTree().Quit(0);
    }

    async Task Wait(double s)
    {
        ulong t0 = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - t0 < s * 1000) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
