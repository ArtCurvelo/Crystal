using System.Linq;
using System.Threading.Tasks;
using CrystalExcavator.Core;
using CrystalExcavator.Core.Rendering;
using Godot;

namespace CrystalExcavator.Tests;

/// <summary>
/// Verificação dos perfis Baixa/Média/Alta no renderizador em uso (contexto gráfico real):
///   godot --path . --rendering-method mobile -- --render-matrix=pasta
/// Para cada qualidade: aplica, desenha a cena de mineração, salva a imagem, confere que tem conteúdo
/// e conta erros do motor (shader/driver). Imprime "[Matrix] ..." e "[Matrix] RESULTADO ok|falha".
/// </summary>
public partial class RenderMatrix : Node
{
    public Main Main;
    public string Dir;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var log = new ProbeLogger();
        OS.AddLogger(log);
        AudioServer.SetBusMute(0, true);
        SaveSystem.SavePath = "user://matrix_save.json";
        Settings.Data.DynamicResolution = false;
        Settings.Data.RenderScale = 1f;
        Main.StartGame(false);
        await Wait(1.0);
        var p = Main.Player;
        var c = Main.World.Crystals.First(x => x.Material.Id == "quartz" && x.GlobalTransform.Basis.Y.Normalized().Y > 0.95f);
        p.SimulateCapturedMouse = true;
        p.GlobalPosition = c.StandPoint;
        var d = c.Center - c.StandPoint;
        p.SetLook(Mathf.Atan2(-d.X, -d.Z), -0.3f);
        string method = RenderMethods.Current;
        bool ok = true;
        foreach (var q in new[] { GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High })
        {
            int e0 = log.Errors, c0 = log.Critical;
            Settings.Data.Quality = (int)q;
            Settings.Apply(GetTree());
            Input.ActionPress("mine");
            await Wait(1.5);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng($"{Dir}/{method}_{q}.png");
            Input.ActionRelease("mine");
            double mean = 0, var = 0; int n = 0;
            for (int y = 0; y < img.GetHeight(); y += 12) for (int x = 0; x < img.GetWidth(); x += 12)
            { var px = img.GetPixel(x, y); double l = px.R * 0.2126 + px.G * 0.7152 + px.B * 0.0722; mean += l; var += l * l; n++; }
            mean /= n; double std = System.Math.Sqrt(System.Math.Max(0, var / n - mean * mean));
            var f = RenderFeatures.Current;
            var env = GetTree().Root.FindChild("Environment", true, false) as WorldEnvironment;
            var e = env?.Environment;
            bool content = mean > 0.01 && std > 0.01;
            bool applied = e != null && e.SsaoEnabled == f.Ssao && e.SsrEnabled == f.Ssr && e.SsilEnabled == f.Ssil && e.VolumetricFogEnabled == f.VolumetricFog && e.GlowEnabled == f.Glow
                           && p.Lamp.ShadowEnabled == f.LampShadows;
            bool pass = content && applied && log.Critical == c0;
            ok &= pass;
            GD.Print($"[Matrix] {method} {q}: imagem {(content ? "ok" : "VAZIA")} (lum {mean:0.000}, var {std:0.000}) · perfil aplicado {(applied ? "ok" : "DIFERENTE")} · SSAO {e?.SsaoEnabled} SSR {e?.SsrEnabled} SSIL {e?.SsilEnabled} névoa vol. {e?.VolumetricFogEnabled} sombra lanterna {p.Lamp.ShadowEnabled} AA {GetViewport().ScreenSpaceAA}/{GetViewport().Msaa3D} · erros {log.Errors - e0} (críticos {log.Critical - c0}) → {(pass ? "OK" : "FALHA")}");
        }
        GD.Print($"[Matrix] RESULTADO {method}: {(ok ? "ok" : "falha")}");
        SaveSystem.Delete();
        OS.RemoveLogger(log);
        new System.Threading.Thread(() => { System.Threading.Thread.Sleep(3000); OS.Kill(OS.GetProcessId()); }) { IsBackground = true }.Start();
        GetTree().Quit(ok ? 0 : 1);
    }

    async Task Wait(double s)
    {
        ulong t0 = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - t0 < s * 1000) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
