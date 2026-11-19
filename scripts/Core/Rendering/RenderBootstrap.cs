using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CrystalExcavator.UI;
using Godot;

namespace CrystalExcavator.Core.Rendering;

/// <summary>
/// Seleção automática do renderizador, antes de montar o mundo:
///  1. confere se a última inicialização caiu antes do menu (2 seguidas → troca para o próximo renderizador validado);
///  2. se a avaliação salva vale para este computador, garante que o jogo está no renderizador dela (senão reinicia nele);
///  3. senão, testa os renderizadores em processos separados fora da tela (Forward+ → Mobile → Compatibility,
///     parando no primeiro aprovado), escolhe, salva o perfil e reinicia no escolhido se for preciso.
/// O jogador só vê a tela de carregamento do jogo. Tudo fica registrado em user://logs/render.log.
/// </summary>
public partial class RenderBootstrap : Node
{
    public Action OnContinue;
    /// <summary>Tempo máximo de cada teste (o processo é encerrado se passar disso).</summary>
    public const double ProbeTimeout = 50;
    const string RestartedArg = "--render-restarted";

    /// <summary>Roda no jogo exportado (ou com --render-select para testar); nunca nos testes e ferramentas.</summary>
    public static bool ShouldRun(string[] args)
    {
        if (args.Any(a => a.StartsWith("--render-probe") || a == "--run-tests" || a.StartsWith("--shots") || a.StartsWith("--bench") || a.StartsWith("--bot") || a.StartsWith("--render-matrix") || a == "--no-render-select"))
            return false;
        if (DisplayServer.GetName() == "headless") return false;
        return args.Contains("--render-select") || (OS.HasFeature("template") && !OS.HasFeature("editor"));
    }

    Label _status;
    ProgressBar _bar;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        try { await Run(); }
        catch (Exception e)
        {
            // nunca impede o jogo de abrir: registra e segue com o renderizador atual
            RenderStore.Log("ERRO no bootstrap: " + e);
            Continue();
        }
    }

    async Task Run()
    {
        string current = RenderMethods.Current;
        string engine = UpdateManager.EngineVersion;
        var args = OS.GetCmdlineUserArgs();
        var hw = HardwareInfo.Collect();
        var profile = RenderStore.LoadProfile();
        var guard = RenderStore.LoadGuard();
        bool restarted = args.Contains(RestartedArg);
        RenderStore.Log($"início: renderizador atual {current} ({RenderingServer.GetCurrentRenderingDriverName()}), override '{RenderStore.ReadOverrideMethod()}', reiniciado={restarted}");

        // ---- 1. recuperação: a última inicialização neste renderizador não chegou ao menu?
        if (guard.Pending && guard.Method == current) guard.Failures++;
        else if (guard.Method != current) guard.Failures = 0;
        if (!restarted) guard.Switches = 0;
        if (guard.Failures >= 2 && !string.IsNullOrEmpty(current))
        {
            // não volta a usar este renderizador nesta máquina; a reavaliação abaixo só considera os outros
            // (com Compatibility também falhando, segue nele mesmo: não há para onde ir e não entra em ciclo)
            RenderStore.Log($"{current} falhou {guard.Failures}x seguidas antes do menu: marcado como falho, reavaliando sem ele");
            profile ??= new RenderProfile();
            if (!profile.FailedMethods.Contains(current)) profile.FailedMethods.Add(current);
            RenderStore.SaveProfile(profile);
            guard.Failures = 0;
            guard.Pending = false;
            RenderStore.SaveGuard(guard);
        }

        // ---- 2. avaliação salva ainda vale?
        if (RendererSelector.IsValid(profile, hw, engine))
        {
            if (profile.Method != current && RenderMethods.IsKnown(profile.Method) && !restarted && !string.IsNullOrEmpty(current))
            {
                RenderStore.Log($"perfil pede {profile.Method}, jogo abriu em {current}: reiniciando");
                RenderStore.WriteOverride(profile.Method);
                Restart(profile.Method);
                return;
            }
            if (profile.Method != current)
                RenderStore.Log($"aviso: perfil pede {profile.Method} mas o motor usou {current} (queda automática do driver)");
            BeginGuard(guard, current);
            Continue();
            return;
        }

        // ---- 3. avaliação (primeira vez, hardware/driver/motor mudou ou regras novas)
        // outra janela do jogo já está avaliando (duplo clique no atalho): esta abre normal, sem mexer em nada
        if (!TryLock())
        {
            RenderStore.Log("outra instância está avaliando; esta segue sem avaliar");
            Continue();
            return;
        }
        RenderStore.Log("avaliação necessária: " + HardwareInfo.Describe(hw) + $" · motor {engine} · regras v{RendererSelector.EvalVersion}");
        BuildOverlay();
        ulong t0 = Time.GetTicksMsec();
        var results = new List<ProbeResult>();
        var caps = await CheckGpu();
        RenderStore.Log($"APIs gráficas: Vulkan {(caps.Vulkan ? "sim" : "não")} ({caps.VulkanDevices} disp., '{caps.VulkanDevice}') · Direct3D 12 {(caps.D3D12 ? "sim" : "não")}{(string.IsNullOrEmpty(caps.Note) ? "" : " · " + caps.Note.Trim())}");
        var skip = profile?.FailedMethods ?? new List<string>();
        if (skip.Count >= RenderMethods.Preference.Length) skip = skip.Where(x => x != RenderMethods.Compatibility).ToList(); // nunca fica sem nenhum
        int step = 0;
        string m;
        while ((m = RendererSelector.NextToProbe(results, skip)) != null)
        {
            step++;
            SetProgress(step / 4f);
            // sem Vulkan nem Direct3D 12 Forward+/Mobile não iniciam: nem abre o processo
            // (evita a caixa de erro do motor numa máquina antiga)
            var r = !caps.RenderingDevice && m != RenderMethods.Compatibility
                ? new ProbeResult { Method = m, Note = "sem Vulkan nem Direct3D 12 utilizável" }
                : await Probe(m);
            r.Status = RendererSelector.Classify(r);
            results.Add(r);
            RenderStore.Log($"teste {m}: {r.Status} · real {r.ActualMethod}/{r.Driver} · '{r.Adapter}' · Média {r.FpsMedium:0.0} FPS (média {r.AvgMsMedium:0.0} ms, p99 {r.P99MsMedium:0.0} ms)" +
                            (r.FpsLow > 0 ? $" · Baixa {r.FpsLow:0.0} FPS (p99 {r.P99MsLow:0.0})" : "") +
                            $" · CPU {r.CpuMs:0.0} ms · GPU {r.GpuMs:0.0} ms · erros {r.Errors} (críticos {r.CriticalErrors}) · {r.Seconds:0.0} s" + (string.IsNullOrEmpty(r.Note) ? "" : " · " + r.Note));
        }
        var decision = RendererSelector.Choose(results, skip);
        SetProgress(1f);
        RenderStore.Log($"escolha: {decision.Method} · qualidade {decision.Quality} · escala {decision.RenderScale:0.00}{(decision.Recovery ? " · RECUPERAÇÃO" : "")} · {decision.Reason} · avaliação em {(Time.GetTicksMsec() - t0) / 1000.0:0.0} s");

        profile = new RenderProfile
        {
            EvalVersion = RendererSelector.EvalVersion, Engine = engine, GameVersion = UpdateManager.CurrentVersion,
            EvaluatedAt = Time.GetDatetimeStringFromSystem(), Hardware = hw, Method = decision.Method,
            Quality = decision.Quality, RenderScale = decision.RenderScale, Recovery = decision.Recovery,
            Results = results, FailedMethods = new List<string>(skip), Caps = caps,
        };
        RenderStore.SaveProfile(profile);
        RenderStore.WriteOverride(decision.Method);
        ApplyInitialQuality(decision);

        Unlock();
        if (decision.Method != current && !restarted && !string.IsNullOrEmpty(current))
        {
            Restart(decision.Method);
            return;
        }
        BeginGuard(guard, current);
        _overlay?.QueueFree();
        Continue();
    }

    /// <summary>Perfil inicial sugerido pela avaliação — só se o jogador ainda não escolheu a qualidade.</summary>
    public static bool ApplyInitialQuality(RenderDecision d, SettingsData s = null)
    {
        s ??= Settings.Data;
        if (s.QualityManual) { RenderStore.Log("qualidade escolhida pelo jogador: mantida"); return false; }
        s.Quality = d.Quality;
        s.RenderScale = d.RenderScale;
        s.DynamicResolution = true;
        if (ReferenceEquals(s, Settings.Data)) Settings.Save();
        return true;
    }

    const string LockPath = "user://render_eval.lock";

    /// <summary>Uma avaliação por vez (trava com o PID; trava de processo que não existe mais é ignorada).</summary>
    static bool TryLock()
    {
        string abs = ProjectSettings.GlobalizePath(LockPath);
        try
        {
            if (System.IO.File.Exists(abs) && int.TryParse(System.IO.File.ReadAllText(abs).Trim(), out int other) && other != OS.GetProcessId())
            {
                bool alive;
                try { using var p = System.Diagnostics.Process.GetProcessById(other); alive = !p.HasExited; } catch { alive = false; }
                bool fresh = (DateTime.Now - System.IO.File.GetLastWriteTime(abs)).TotalMinutes < 5;
                if (alive && fresh) return false;
            }
            System.IO.File.WriteAllText(abs, OS.GetProcessId().ToString());
            return true;
        }
        catch { return true; }
    }

    static void Unlock()
    {
        try { System.IO.File.Delete(ProjectSettings.GlobalizePath(LockPath)); } catch { }
    }

    static void BeginGuard(BootGuard g, string method)
    {
        g.Method = method;
        g.Pending = true;
        RenderStore.SaveGuard(g);
    }

    /// <summary>Chegou ao menu: esta inicialização deu certo (zera o controle de falhas).</summary>
    public static async void MarkBootOk(Node from)
    {
        if (!FileAccess.FileExists(RenderStore.GuardPath)) return;
        // alguns segundos de menu desenhando sem cair
        var t = from.GetTree().CreateTimer(3.0, true, false, true);
        await from.ToSignal(t, SceneTreeTimer.SignalName.Timeout);
        var g = RenderStore.LoadGuard();
        if (!g.Pending && g.Failures == 0) return;
        g.Pending = false;
        g.Failures = 0;
        g.Switches = 0;
        RenderStore.SaveGuard(g);
        RenderStore.Log($"inicialização ok em {RenderMethods.Current}");
    }

    void Continue()
    {
        var c = OnContinue;
        OnContinue = null;
        CallDeferred(MethodName.Finish);
        c?.Invoke();
    }

    void Finish() => QueueFree();

    // ------------------------------------------------------------ processos
    /// <summary>Argumentos para reabrir o próprio executável (no jogo exportado: nenhum; em desenvolvimento: --path).</summary>
    static List<string> BaseArgs()
    {
        var list = new List<string>();
        var a = OS.GetCmdlineArgs();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == "--rendering-method" || a[i] == "--rendering-driver" || a[i] == "--position" || a[i] == "--resolution") { i++; continue; }
            if (a[i].EndsWith(".tscn") || a[i] == "--") continue;
            list.Add(a[i]);
        }
        return list;
    }

    static void Restart(string method)
    {
        var args = BaseArgs();
        args.Add("--rendering-method"); args.Add(method);
        args.Add("--");
        args.AddRange(OS.GetCmdlineUserArgs().Where(a => a != RestartedArg));
        args.Add(RestartedArg);
        RenderStore.Log($"reiniciando em {method}: {string.Join(' ', args)}");
        int pid = OS.CreateProcess(OS.GetExecutablePath(), args.ToArray());
        if (pid <= 0) { RenderStore.Log("falha ao reiniciar; seguindo no atual"); return; }
        (Engine.GetMainLoop() as SceneTree)?.Quit(0);
    }

    /// <summary>Roda um teste num processo separado, fora da área visível, e espera o resultado (com tempo limite).</summary>
    async Task<ProbeResult> Probe(string method)
    {
        string outRel = $"user://probe_{method}.json";
        string outAbs = ProjectSettings.GlobalizePath(outRel);
        if (System.IO.File.Exists(outAbs)) System.IO.File.Delete(outAbs);
        if (System.IO.File.Exists(outAbs + ".alive")) System.IO.File.Delete(outAbs + ".alive");
        var size = ProbeResolution();
        var args = BaseArgs();
        args.AddRange(new[] { "--rendering-method", method, "--windowed", "--resolution", $"{size.X}x{size.Y}", "--position", OffscreenPosition(size) });
        args.Add("--");
        args.Add("--render-probe=" + method);
        args.Add("--probe-out=" + outRel);
        args.Add("--probe-parent=" + OS.GetProcessId());
        // simulação de falhas para os testes automáticos: --x-probe-<modo>=<renderizador>
        foreach (var mode in new[] { "crash", "hang", "black", "slow", "fast", "nostart" })
            if (OS.GetCmdlineUserArgs().Any(a => a == $"--x-probe-{mode}={method}")) args.Add("--probe-simulate=" + mode);
        ulong t0 = Time.GetTicksMsec();
        int pid = OS.CreateProcess(OS.GetExecutablePath(), args.ToArray());
        var fail = new ProbeResult { Method = method };
        if (pid <= 0) { fail.Note = "não foi possível abrir o processo de teste"; return fail; }
        while (true)
        {
            await ToSignal(GetTree().CreateTimer(0.25, true, false, true), SceneTreeTimer.SignalName.Timeout);
            if (System.IO.File.Exists(outAbs))
            {
                // espera o processo terminar de gravar/fechar (curto)
                for (int i = 0; i < 20 && OS.IsProcessRunning(pid); i++) await ToSignal(GetTree().CreateTimer(0.1, true, false, true), SceneTreeTimer.SignalName.Timeout);
                try
                {
                    var r = JsonSerializer.Deserialize<ProbeResult>(System.IO.File.ReadAllText(outAbs));
                    System.IO.File.Delete(outAbs);
                    if (System.IO.File.Exists(outAbs + ".alive")) System.IO.File.Delete(outAbs + ".alive");
                    if (OS.IsProcessRunning(pid)) OS.Kill(pid); // alguns drivers demoram a fechar: o resultado já chegou
                    if (r != null) return r;
                }
                catch (Exception e) { fail.Note = "resultado ilegível: " + e.Message; return fail; }
            }
            if (!OS.IsProcessRunning(pid)) { fail.Note = "o processo de teste fechou sem resultado (falha ao iniciar ou travamento)"; return fail; }
            // não chegou nem a iniciar o jogo: o driver travou ou o motor abriu uma caixa de erro → encerra logo
            if (!System.IO.File.Exists(outAbs + ".alive") && (Time.GetTicksMsec() - t0) / 1000.0 > StartTimeout())
            {
                OS.Kill(pid);
                fail.Note = $"o driver não iniciou em {StartTimeout():0} s";
                return fail;
            }
            if ((Time.GetTicksMsec() - t0) / 1000.0 > Timeout())
            {
                OS.Kill(pid);
                fail.Note = $"sem resposta em {Timeout():0} s";
                return fail;
            }
        }
    }

    /// <summary>Tempo limite (os testes automáticos em CPU sem GPU usam --x-probe-timeout=).</summary>
    static double Timeout()
    {
        var a = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--x-probe-timeout="));
        return a != null && double.TryParse(a.Split('=')[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t) ? t : ProbeTimeout;
    }

    /// <summary>Resolução em que o jogador vai jogar (janela configurada ou tela cheia), limitada a 1080p.</summary>
    static Vector2I ProbeResolution()
    {
        var d = Settings.Data;
        Vector2I size = d.Fullscreen ? DisplayServer.ScreenGetSize() : Settings.WindowSizes[Math.Clamp(d.WindowSize, 0, Settings.WindowSizes.Length - 1)];
        if (size.X <= 0 || size.Y <= 0) size = new Vector2I(1280, 720);
        if (size.Y > 1080) size = new Vector2I(size.X * 1080 / size.Y, 1080);
        return size;
    }

    /// <summary>
    /// No canto inferior direito da tela principal, quase toda fora dela (sobra 1 pixel, atrás da barra de tarefas).
    /// A janela continua "num monitor": totalmente fora das telas alguns drivers OpenGL não criam o contexto.
    /// </summary>
    static string OffscreenPosition(Vector2I size)
    {
        try
        {
            int s = DisplayServer.GetPrimaryScreen();
            var p = DisplayServer.ScreenGetPosition(s); var sz = DisplayServer.ScreenGetSize(s);
            return $"{p.X + sz.X - 1},{p.Y + sz.Y - 1}";
        }
        catch { return "0,0"; }
    }

    static double StartTimeout()
    {
        var a = OS.GetCmdlineUserArgs().FirstOrDefault(x => x.StartsWith("--x-probe-start-timeout="));
        return a != null && double.TryParse(a.Split('=')[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double t) ? t : 20;
    }

    /// <summary>Vulkan/Direct3D 12 disponíveis? Consulta num processo sem janela (driver com defeito não afeta o jogo).</summary>
    async Task<GpuCaps> CheckGpu()
    {
        string rel = "user://gpu_caps.json", abs = ProjectSettings.GlobalizePath(rel);
        if (System.IO.File.Exists(abs)) System.IO.File.Delete(abs);
        var args = BaseArgs();
        args.Add("--headless");
        args.Add("--");
        args.Add("--gpu-check=" + rel);
        if (OS.GetCmdlineUserArgs().Contains("--x-gpu-allow-cpu")) args.Add("--x-gpu-allow-cpu");
        int pid = OS.CreateProcess(OS.GetExecutablePath(), args.ToArray());
        ulong t0 = Time.GetTicksMsec();
        while (pid > 0 && (Time.GetTicksMsec() - t0) < 20000)
        {
            await ToSignal(GetTree().CreateTimer(0.2, true, false, true), SceneTreeTimer.SignalName.Timeout);
            if (System.IO.File.Exists(abs))
            {
                try
                {
                    await ToSignal(GetTree().CreateTimer(0.1, true, false, true), SceneTreeTimer.SignalName.Timeout);
                    var caps = JsonSerializer.Deserialize<GpuCaps>(System.IO.File.ReadAllText(abs));
                    System.IO.File.Delete(abs);
                    if (OS.IsProcessRunning(pid)) OS.Kill(pid);
                    if (caps != null) return caps;
                }
                catch { }
                break;
            }
            if (!OS.IsProcessRunning(pid)) break;
        }
        if (pid > 0 && OS.IsProcessRunning(pid)) OS.Kill(pid);
        // a própria consulta falhou (driver com defeito): por segurança, só o Compatibility
        return new GpuCaps { Checked = false, Note = "consulta das APIs gráficas falhou" };
    }

    // ------------------------------------------------------------ tela (a mesma da carga do jogo, sem detalhes técnicos)
    CanvasLayer _overlay;

    void BuildOverlay()
    {
        _overlay = new CanvasLayer { Layer = 80 };
        AddChild(_overlay);
        var bg = new ColorRect { Color = new Color(0.02f, 0.03f, 0.06f, 1f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(bg);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(center);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        center.AddChild(v);
        var icon = HudIcon.Make(HudIcon.Kind.Gem, UiTheme.Cyan, 64);
        icon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        v.AddChild(icon);
        v.AddChild(UiTheme.Label("CRYSTAL EXCAVATOR", 34, UiTheme.Cyan, true, HorizontalAlignment.Center));
        _status = UiTheme.Label(Loc.T("Preparando o jogo para o seu computador…"), 17, UiTheme.Muted, false, HorizontalAlignment.Center);
        v.AddChild(_status);
        _bar = new ProgressBar { MinValue = 0, MaxValue = 1, Step = 0.001, ShowPercentage = false, CustomMinimumSize = new Vector2(360, 10) };
        _bar.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        v.AddChild(_bar);
        v.AddChild(UiTheme.Label(Loc.T("Isso só acontece na primeira vez."), 14, UiTheme.Muted, false, HorizontalAlignment.Center));
    }

    void SetProgress(float f)
    {
        if (_bar == null) return;
        var tw = CreateTween();
        tw.TweenProperty(_bar, "value", (double)f, 0.4);
    }
}
