using System.Linq;
using System.Threading.Tasks;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using Godot;

namespace CrystalExcavator.Tests;

/// <summary>
/// Tour automático de capturas de tela para QA visual e medição de FPS:
///   godot --path . -- --shots=&lt;pasta&gt;
/// Percorre menu, gameplay, mineração, loja, venda, regiões e pausa.
/// </summary>
public partial class ScreenshotTour : Node
{
    public Main Main;
    public string Dir;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        DirAccess.MakeDirRecursiveAbsolute(Dir);
        SaveSystem.SavePath = "user://shots_save.json";
        await Wait(1.5f);
        await Shot("01_menu");
        Main.OpenHelpForTour();
        await Wait(0.5f);
        await Shot("01b_help");
        Input.ParseInputEvent(new InputEventAction { Action = "pause", Pressed = true });
        await Wait(0.3f);

        Main.StartGame(false);
        var gs = GameState.Instance;
        var p = Main.Player;
        p.SimulateCapturedMouse = true;
        await Wait(1.2f);
        await Shot("02_spawn");
        p.SetLook(1.35f, 0.02f);
        await Wait(0.4f);
        await Shot("02b_outpost");
        p.SetLook(0, 0);

        // mira em um cristal e minera
        var c = Main.World.Crystals.Where(x => x.Material.Id == "quartz")
            .OrderBy(x => x.GlobalPosition.DistanceTo(p.GlobalPosition)).First();
        p.GlobalPosition = c.GlobalPosition + new Vector3(0, 0, 1.8f);
        p.SetLook(0, -0.38f);
        await Wait(0.6f);
        Input.ActionPress("mine");
        await Wait(1.2f);
        await Shot("03_mining");
        await Wait(2.4f);
        Input.ActionRelease("mine");
        await Wait(0.3f);
        await Shot("04_shards");
        await Wait(1.5f);

        // vista geral da região 1 com FPS
        Settings.Data.ShowFps = true;
        p.GlobalPosition = new Vector3(0, 0.1f, 12);
        p.SetLook(0, -0.05f);
        await Wait(2f);
        await Shot("05_region1");
        float fps1 = (float)Engine.GetFramesPerSecond();
        string dc1 = DrawStats();

        // loja e venda
        gs.AddMoney(5000);
        gs.AddItem("amethyst", 12);
        UiBus.RequestShop();
        await Wait(0.6f);
        await Shot("06_shop");
        Input.ParseInputEvent(new InputEventAction { Action = "pause", Pressed = true });
        await Wait(0.3f);
        UiBus.RequestSell();
        await Wait(0.6f);
        await Shot("07_sell");
        Input.ParseInputEvent(new InputEventAction { Action = "pause", Pressed = true });
        await Wait(0.3f);

        // portão
        p.GlobalPosition = new Vector3(0, 0.1f, -17);
        p.SetLook(0, 0.05f);
        await Wait(1f);
        await Shot("08_gate");

        // região 2
        gs.AddMoney(GameData.Regions[2].UnlockCost + GameData.Regions[1].UnlockCost);
        gs.TryUnlockRegion("depths");
        gs.TryUnlockRegion("core");
        foreach (var id in UpgradeIds.All) gs.Data.Upgrades[id] = 10;
        gs.Data.Upgrades[UpgradeIds.Damage] = 18;
        gs.RaiseAll();
        p.GlobalPosition = new Vector3(0, 0.1f, -36);
        p.SetLook(0, 0);
        await Wait(3f);
        await Shot("09_region2");
        Main.Events.Begin("depths");
        await Wait(2.5f);
        await Shot("09b_event_depths");
        Main.Events.Stop();
        float fps2 = (float)Engine.GetFramesPerSecond();
        string dc2 = DrawStats();

        // região 3 e o Coração Ancestral
        p.GlobalPosition = new Vector3(0, 0.1f, -100);
        p.SetLook(0, 0.08f);
        await Wait(3f);
        await Shot("10_region3");
        float fps3 = (float)Engine.GetFramesPerSecond();
        string dc3 = DrawStats();

        var heart = Main.World.Crystals.First(x => x.Material.Special);
        p.GlobalPosition = heart.GlobalPosition + new Vector3(0, 0, 4.2f);
        p.SetLook(0, -0.05f);
        await Wait(0.5f);
        Input.ActionPress("mine");
        await Wait(1.5f);
        await Shot("11_heart");
        Input.ActionRelease("mine");

        // pausa
        Input.ParseInputEvent(new InputEventAction { Action = "pause", Pressed = true });
        await Wait(0.5f);
        await Shot("12_pause");
        Main.GetNode<Node>(".").Call("OpenCodexForTour");
        await Wait(0.5f);
        await Shot("13_codex");

        GD.Print($"[Tour] FPS (software, Xvfb): R1={fps1:0} R2={fps2:0} R3={fps3:0}");
        GD.Print($"[Tour] Qualidade {Settings.Quality} | R1: {dc1} | R2: {dc2} | R3: {dc3}");
        SaveSystem.Delete();
        GetTree().Quit();
    }

    static string DrawStats()
    {
        var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var objs = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        var prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        return $"{draws:0} draw calls, {objs:0} objetos, {prims / 1000:0}k primitivas";
    }

    async Task Wait(float s)
    {
        float t = 0;
        while (t < s)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            t += (float)GetProcessDeltaTime();
        }
    }

    async Task Shot(string name)
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        var img = GetViewport().GetTexture().GetImage();
        string path = $"{Dir}/{name}.png";
        img.SavePng(path);
        GD.Print($"[Tour] {path}");
    }
}
