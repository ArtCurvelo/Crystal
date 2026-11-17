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
        p.GlobalPosition = new Vector3(2.4f, 0.1f, 15.6f);
        p.SetLook(-1.05f, -0.12f);
        await Wait(0.4f);
        await Shot("02c_terminal");
        p.GlobalPosition = new Vector3(-1.5f, 0.1f, 16.2f);
        p.SetLook(1.0f, -0.12f);
        await Wait(0.4f);
        await Shot("02d_sell");
        p.GlobalPosition = new Vector3(-9f, 0.1f, 1.5f);
        p.SetLook(1.9f, -0.05f);
        await Wait(0.4f);
        await Shot("02e_props");
        p.GlobalPosition = Main.World.SpawnPoint;
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
        // as 6 ferramentas disparando contra um cristal
        int[] tierLevels = { 0, 3, 7, 12, 18, 24 };
        var tc = Main.World.Crystals.Where(x => x.IsMineable && x.Material.Id == "amethyst")
            .OrderBy(x => x.GlobalPosition.DistanceTo(p.GlobalPosition)).First();
        p.GlobalPosition = tc.GlobalPosition + new Vector3(0, 0, 2.4f);
        p.SetLook(0, -0.3f);
        for (int t = 0; t < tierLevels.Length; t++)
        {
            gs.Data.Upgrades[UpgradeIds.Damage] = tierLevels[t];
            gs.RaiseAll();
            Input.ActionPress("mine");
            await Wait(0.35f);
            await Shot($"tool_{t}");
            Input.ActionRelease("mine");
        }
        gs.Data.Upgrades.Remove(UpgradeIds.Damage);
        gs.RaiseAll();
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
        p.GlobalPosition = new Vector3(0, 0.1f, -15f);
        await Wait(0.5f);
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
        gs.AddMoney(GameData.Regions[3].UnlockCost);
        gs.TryUnlockRegion("abyss");
        p.GlobalPosition = new Vector3(0, 0.1f, -168f);
        p.SetLook(0, 0.05f);
        await Wait(3f);
        await Shot("10b_region4");
        p.GlobalPosition = new Vector3(6f, 0.1f, -178f);
        p.SetLook(0.6f, 0.25f);
        await Wait(0.6f);
        await Shot("10c_region4b");
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
        Input.ParseInputEvent(new InputEventAction { Action = "pause", Pressed = true });
        await Wait(0.3f);
        Main.OpenSettingsForTour();
        await Wait(0.5f);
        await Shot("14_settings");
        Input.ParseInputEvent(new InputEventAction { Action = "pause", Pressed = true });
        await Wait(0.3f);
        Main.OpenCompletionForTour();
        await Wait(0.6f);
        await Shot("15_completion");

        await OreGallery(p);

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

    /// <summary>Galeria: todos os minérios lado a lado (grupos de 6), para revisão visual das texturas.</summary>
    async Task OreGallery(Player.PlayerController p)
    {
        Main.CloseAllForTour();
        await Wait(0.3f);
        var mats = GameData.Materials.Values.ToList();
        // galeria isolada ao sul do mapa (fundo escuro, sem outros cristais)
        var root = new Node3D { Position = new Vector3(0, 0, 70) };
        Main.World.GetParent().AddChild(root);
        var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(30, 12) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.11f, 0.14f) } };
        root.AddChild(floor);
        var body = new StaticBody3D { CollisionLayer = 1 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(30, 1, 30) }, Position = new Vector3(0, -0.5f, 5) });
        root.AddChild(body);
        int perShot = 6;
        for (int g = 0; g * perShot < mats.Count; g++)
        {
            foreach (Node c in root.GetChildren()) if (c != floor && c != body) c.QueueFree();
            await Wait(0.1f);
            var group = mats.Skip(g * perShot).Take(perShot).ToList();
            for (int i = 0; i < group.Count; i++)
            {
                var c = new Mining.Crystal();
                c.Init(group[i], 100 + i, group[i].Special ? 0.45f : 1f);
                root.AddChild(c);
                c.Position = new Vector3(-6.25f + i * 2.5f, 0.02f, 0f);
                if (group[i].Special) c.Scale = Vector3.One * 0.4f;
                var label = new Label3D { Text = group[i].Name, FontSize = 36, OutlineSize = 8, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.1f, 0), NoDepthTest = true };
                c.AddChild(label);
            }
            p.GlobalPosition = new Vector3(0, 0.1f, 76.5f);
            p.Velocity = Vector3.Zero;
            p.SetLook(0, -0.22f);
            await Wait(0.8f);
            await Shot($"16_ores_{g}");
        }
        root.QueueFree();
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
