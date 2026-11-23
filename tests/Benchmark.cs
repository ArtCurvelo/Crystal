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
            while (Time.GetTicksUsec() - t0 < 3_000_000)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                frames++;
            }
            if (mining) Input.ActionRelease("mine");
            // --bench-shots=DIR: foto de cada vista (para comparar o visual entre qualidades)
            var shots = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--bench-shots="));
            if (shots != null) GetViewport().GetTexture().GetImage().SavePng($"{shots.Substring(14)}/{name.Replace(' ', '_')}_q{Quality}.png");
            double ms = (Time.GetTicksUsec() - t0) / 1000.0 / frames;
            results.Add(ms);
            var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
            var objs = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
            var prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
            var proc = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
            var phys = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
            GD.Print($"[Bench] {name,-16} {1000.0 / ms,6:0.0} FPS  ({ms:0.0} ms)  draw calls {draws:0}  objetos {objs:0}  triângulos {prims / 1000:0}k  CPU process {proc:0.00} ms  física {phys:0.00} ms");
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
        if (args.Contains("--x-nolite")) RenderingServer.GlobalShaderParameterSet("cave_lite", 0f);
        var sc = args.FirstOrDefault(a => a.StartsWith("--x-scale="));
        if (sc != null) GetViewport().Scaling3DScale = float.Parse(sc.Split('=')[1], System.Globalization.CultureInfo.InvariantCulture);
        if (args.Any(a => a.StartsWith("--x-"))) GD.Print("[Bench] experimentos: " + string.Join(" ", args.Where(a => a.StartsWith("--x-"))));
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
