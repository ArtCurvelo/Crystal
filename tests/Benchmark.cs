using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.Tests;

/// <summary>
/// Benchmark de desempenho com vistas fixas: mede o tempo médio de quadro em cada região.
///   godot --path . --resolution 1280x720 -- --bench [--bench-quality=0..2]
/// Imprime uma linha "[Bench] ..." por vista e a média geral.
/// </summary>
public partial class Benchmark : Node
{
    public Main Main;
    public int Quality = 0;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SaveSystem.SavePath = "user://bench_save.json";
        Settings.Data.Quality = Quality;
        Settings.Data.VSync = false;
        Settings.Data.MaxFps = 0;
        var aa = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--bench-aa="));
        if (aa != null) Settings.Data.AntiAliasing = int.Parse(aa.Split('=')[1]);
        Settings.Data.ShowFps = false;
        Settings.Data.DynamicResolution = false; // mede com escala fixa
        var rs = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--bench-scale="));
        Settings.Data.RenderScale = rs != null ? float.Parse(rs.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture) : (Quality == 0 ? 0.75f : 1f);
        Settings.Apply(GetTree());
        await Wait(0.5f);
        Main.StartGame(false);
        var gs = GameState.Instance;
        foreach (var r in GameData.Regions) if (!gs.IsUnlocked(r.Id)) gs.Data.UnlockedRegions.Add(r.Id);
        gs.RaiseAll();
        var p = Main.Player;
        p.SimulateCapturedMouse = true;
        ApplyExperiments();
        GetTree().Root.AddChild(new ProcessStopwatch { ProcessPriority = int.MinValue, ProcessPhysicsPriority = int.MinValue });
        GetTree().Root.AddChild(new ProcessStopwatch { IsEnd = true, ProcessPriority = int.MaxValue, ProcessPhysicsPriority = int.MaxValue });

        // vistas fixas no mapa de cavernas: ao longo do caminho principal de cada região
        var W = Main.World;
        (string, Vector3, float, float) Along(string name, int region, int node)
        {
            var L = W.Layouts[region];
            var a = L.PathPoints[Math.Min(node, L.PathPoints.Count - 1)];
            var b = L.PathPoints[Math.Min(node + 1, L.PathPoints.Count - 1)];
            var pos = L.OnGround(a.X, a.Z, 0.1f);
            var d = new Vector3(b.X - a.X, 0, b.Z - a.Z);
            return (name, pos, Mathf.Atan2(-d.X, -d.Z), 0f);
        }
        var crystal = W.Crystals.First(c => c.Material.Id == "quartz" && c.GlobalTransform.Basis.Y.Normalized().Y > 0.95f);
        var L2 = W.Layouts[2];
        var views = new List<(string name, Vector3 pos, float yaw, float pitch)>
        {
            ("R1 acampamento", W.SpawnPoint, 0f, -0.05f),
            ("R1 mineração", crystal.StandPoint, Mathf.Atan2(-(crystal.Center.X - crystal.StandPoint.X), -(crystal.Center.Z - crystal.StandPoint.Z)), -0.35f),
            Along("R2 caminho", 1, 1),
            ("R3 coração", L2.OnGround(L2.Heart.A.X, L2.Heart.A.Y + 15f, 0.1f), 0f, 0.08f),
            Along("R4 caminho", 3, 1),
            ("Vila praça", W.Town.ArrivePoint, CrystalExcavator.World.Town.ArriveYaw, 0f),
            ("Deep Galerias", W.DeepArrivals.Values.First(), 0f, 0f),
        };

        var results = new List<double>();
        foreach (var (name, pos, yaw, pitch) in views)
        {
            p.GlobalPosition = pos;
            p.Velocity = Vector3.Zero;
            p.SetLook(yaw, pitch);
            bool mining = name.Contains("mineração");
            if (mining) Input.ActionPress("mine");
            await Wait(1.2f); // estabiliza (compilação, streaming)
            ulong t0 = Time.GetTicksUsec();
            int frames = 0;
            Engine.MaxFps = 0; GD.Print($"[Bench] maxfps={Engine.MaxFps} physticks={Engine.PhysicsTicksPerSecond} lowproc={OS.LowProcessorUsageMode} vsync={DisplayServer.WindowGetVsyncMode()}");
            double procSum = 0, procMax = 0, physSum = 0;
            var frameMs = new List<double>();
            ProcessStopwatch.Reset();
            ulong last = Time.GetTicksUsec();
            while (Time.GetTicksUsec() - t0 < 3_000_000)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frames++;
                ulong now = Time.GetTicksUsec();
                frameMs.Add((now - last) / 1000.0);
                last = now;
                double pr = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
                procSum += pr; procMax = System.Math.Max(procMax, pr);
                physSum += Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
            }
            frameMs.Sort();
            double p95 = frameMs[(int)(frameMs.Count * 0.95)], fmax = frameMs[^1];
            if (mining) Input.ActionRelease("mine");
            if (OS.GetCmdlineUserArgs().Contains("--bench-nodes") && name.Contains("acampamento")) PrintProcessing();
            // --bench-shots=DIR: foto de cada vista (para comparar o visual entre qualidades)
            var shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--bench-shots="));
            if (shots != null) GetViewport().GetTexture().GetImage().SavePng($"{shots.Substring(14)}/{name.Replace(' ', '_')}_q{Quality}.png");
            double ms = (Time.GetTicksUsec() - t0) / 1000.0 / frames;
            results.Add(ms);
            var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            var objs = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
            var prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
            var proc = procSum / frames;
            var phys = physSum / frames;
            GD.Print($"[Bench] {name,-16} {1000.0 / ms,6:0.0} FPS  ({ms:0.0} ms)  draw calls {draws:0}  objetos {objs:0}  triângulos {prims / 1000:0}k  p95 {p95:0.0} ms  pior {fmax:0.0} ms  scripts {ProcessStopwatch.Accum / 1000.0 / System.Math.Max(1, ProcessStopwatch.Frames):0.00} ms/quadro  física {ProcessStopwatch.PhysAccum / 1000.0 / System.Math.Max(1, ProcessStopwatch.Frames):0.00} ms/quadro");
        }
        double avg = results.Average();
        GD.Print($"[Bench] MÉDIA qualidade={(GraphicsQuality)Quality} escala3D={Settings.Data.RenderScale:0.00} AA={Settings.Data.AntiAliasing}: {1000.0 / avg:0.0} FPS ({avg:0.0} ms)");
        SaveSystem.Delete();
        GameState.Instance.QuitClean();
    }

    /// <summary>Experimentos de desempenho controlados por argumentos (--x-nolights, --x-litecrystal, --x-nofog, --x-noparticles, --x-scale=0.7).</summary>
    void ApplyExperiments()
    {
        var args = OS.GetCmdlineUserArgs();
        var root = GetTree().Root;
        if (args.Contains("--x-nolights"))
            foreach (var n in All<OmniLight3D>(root)) n.Visible = false;
        if (args.Contains("--x-noparticles"))
            foreach (var n in All<CpuParticles3D>(root)) n.Visible = false;
        if (args.Contains("--x-nofog"))
            foreach (var n in All<WorldEnvironment>(root)) n.Environment.FogEnabled = false;
        if (args.Contains("--x-litecrystal"))
            foreach (var n in All<MeshInstance3D>(root))
                if (n.MaterialOverride is ShaderMaterial sm && sm.Shader.ResourcePath.Contains("crystal"))
                    n.MaterialOverride = new StandardMaterial3D { AlbedoColor = (Color)sm.GetShaderParameter("base_color"), EmissionEnabled = true, Emission = (Color)sm.GetShaderParameter("glow_color"), VertexColorUseAsAlbedo = true };
        if (args.Contains("--x-liteore"))
            foreach (var n in All<MeshInstance3D>(root))
                if (n.MaterialOverride is ShaderMaterial sm && sm.Shader.ResourcePath.Contains("ore.gdshader"))
                    n.MaterialOverride = new StandardMaterial3D { AlbedoColor = (Color)sm.GetShaderParameter("base_color"), VertexColorUseAsAlbedo = true };
        if (args.Contains("--x-noprocess-hidden"))
        {
            Main.World.Town.ProcessMode = ProcessModeEnum.Disabled;
            Main.World.House.ProcessMode = ProcessModeEnum.Disabled;
        }
        if (args.Contains("--x-noprocess-regions"))
            for (int i = 1; i < Main.World.RegionRoots.Count; i++) Main.World.RegionRoots[i].ProcessMode = ProcessModeEnum.Disabled;
        if (args.Contains("--x-noenemies") && Main.World.Enemies != null) Main.World.Enemies.ProcessMode = ProcessModeEnum.Disabled;
        if (args.Contains("--x-noglow"))
            foreach (var n in All<WorldEnvironment>(root)) n.Environment.GlowEnabled = false;
        if (args.Contains("--x-glowcheap"))
        {
            RenderingServer.EnvironmentGlowSetUseBicubicUpscale(false);
            foreach (var n in All<WorldEnvironment>(root)) { var e = n.Environment; for (int i = 0; i < 7; i++) e.SetGlowLevel(i, 0f); e.SetGlowLevel(4, 1.4f); }
        }
        if (args.Contains("--x-nobicubic")) RenderingServer.EnvironmentGlowSetUseBicubicUpscale(false);
        if (args.Contains("--x-nodecor"))
            foreach (var n in All<MultiMeshInstance3D>(root)) n.Visible = false;
        if (args.Contains("--x-nolite")) RenderingServer.GlobalShaderParameterSet("cave_lite", 0f);
        var sc = args.FirstOrDefault(a => a.StartsWith("--x-scale="));
        if (sc != null) GetViewport().Scaling3DScale = float.Parse(sc.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);
        if (args.Any(a => a.StartsWith("--x-"))) GD.Print("[Bench] experimentos: " + string.Join(" ", args.Where(a => a.StartsWith("--x-"))));
        if (false)
        {
            // quantos nós rodam código todo quadro (_Process / _PhysicsProcess), por tipo
            var proc = new Dictionary<string, int>(); var phys = new Dictionary<string, int>(); int total = 0;
            foreach (var n in All<Node>(root))
            {
                total++;
                string k = n.GetType().Name;
                if (n.IsProcessing()) proc[k] = proc.GetValueOrDefault(k) + 1;
                if (n.IsPhysicsProcessing()) phys[k] = phys.GetValueOrDefault(k) + 1;
            }
            GD.Print($"[Bench] nós no total: {total}");
            foreach (var kv in proc.OrderByDescending(x => x.Value).Take(25)) GD.Print($"[Bench] process  {kv.Value,6}  {kv.Key}");
            foreach (var kv in phys.OrderByDescending(x => x.Value).Take(15)) GD.Print($"[Bench] physics  {kv.Value,6}  {kv.Key}");
        }
    }

    void PrintProcessing()
    {
        var proc = new Dictionary<string, int>(); var phys = new Dictionary<string, int>(); int total = 0;
        foreach (var n in All<Node>(GetTree().Root))
        {
            total++;
            string k = n.GetType().Name;
            if (n.IsProcessing()) proc[k] = proc.GetValueOrDefault(k) + 1;
            if (n.IsPhysicsProcessing()) phys[k] = phys.GetValueOrDefault(k) + 1;
        }
        GD.Print($"[Bench] nós no total: {total}");
        foreach (var kv in proc.OrderByDescending(x => x.Value).Take(25)) GD.Print($"[Bench] process  {kv.Value,6}  {kv.Key}");
        foreach (var kv in phys.OrderByDescending(x => x.Value).Take(15)) GD.Print($"[Bench] physics  {kv.Value,6}  {kv.Key}");
    }

    static IEnumerable<T> All<T>(Node n) where T : Node
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (var x in All<T>(c)) yield return x;
        }
    }

    async Task Wait(float s)
    {
        ulong t0 = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - t0 < s * 1000) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}

/// <summary>Cronômetro da etapa de processo: um nó roda primeiro e outro por último em cada quadro (mede só o tempo de CPU dos scripts).</summary>
public partial class ProcessStopwatch : Node
{
    public static ulong Start, Accum, PhysStart, PhysAccum;
    public static int Frames;
    public bool IsEnd;
    public override void _Ready() { ProcessMode = ProcessModeEnum.Always; }
    public override void _Process(double delta)
    {
        if (!IsEnd) Start = Time.GetTicksUsec();
        else { Accum += Time.GetTicksUsec() - Start; Frames++; }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (!IsEnd) PhysStart = Time.GetTicksUsec();
        else PhysAccum += Time.GetTicksUsec() - PhysStart;
    }
    public static void Reset() { Accum = 0; PhysAccum = 0; Frames = 0; }
}
