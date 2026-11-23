using System.Linq;
using System.Threading.Tasks;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.World;
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
        // capturas na qualidade alvo (PC com GPU dedicada); --shots-quality=0..2 para comparar
        var qa = System.Linq.Enumerable.FirstOrDefault(OS.GetCmdlineUserArgs(), a => a.StartsWith("--shots-quality="));
        Settings.Data.Quality = qa != null ? int.Parse(qa.Split('=')[1]) : 2;
        Settings.Data.RenderScale = 1f;
        Settings.Data.DynamicResolution = false;
        Settings.Apply(GetTree());
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
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-regions"))
        {
            await NewRegionsTour(p, gs);
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-giants"))
        {
            foreach (var r in GameData.Regions) if (!gs.IsUnlocked(r.Id)) gs.Data.UnlockedRegions.Add(r.Id);
            gs.Data.DayTimeSeconds = 30;
            foreach (var r in GameData.Regions.Where(r => r.GiantMaterialId != null))
            {
                var giantC = Main.World.Crystals.First(x => x.Material.Id == r.GiantMaterialId);
                var L = Main.World.Layouts[r.Index];
                var dir = (giantC.StandPoint - giantC.GlobalPosition); dir.Y = 0; dir = dir.Normalized();
                var from = giantC.GlobalPosition + dir * 9f;
                from = L.InGrid(from.X, from.Z) ? L.OnGround(from.X, from.Z, 0.1f) : giantC.StandPoint;
                LookAtPoint(p, from, giantC.Center + Vector3.Up * 1.5f);
                await Wait(1.2f);
                await Shot($"27_giant_{r.Id}");
            }
            await OreGallery(p);
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-decor"))
        {
            Main.CloseAllForTour();
            foreach (var r in GameData.Regions) if (!gs.IsUnlocked(r.Id)) gs.Data.UnlockedRegions.Add(r.Id);
            gs.Data.DayTimeSeconds = 30;
            p.Tool.Visible = false;
            // fotografa de perto instâncias reais de cada malha nova da decoração
            var targets = new (Mesh mesh, string name, float dist, float lift)[]
            {
                (World.MeshFactory.Rock(5, 0.35f), "boulder", 3.2f, 0.3f),
                (World.DecorMeshes.MushroomStem(), "mushrooms", 2.2f, 0.3f),
                (World.DecorMeshes.Frond(0), "ferns", 2.6f, 0.4f),
                (World.DecorMeshes.Dripstone(3), "stalagmite", 4.5f, 1.0f),
                (World.DecorMeshes.Dripstone(0), "stalactite", 5f, 3.0f),
            };
            var all = new System.Collections.Generic.List<MultiMeshInstance3D>();
            void Collect(Node n) { foreach (Node c in n.GetChildren()) { if (c is MultiMeshInstance3D mm) all.Add(mm); Collect(c); } }
            Collect(Main.World);
            // estação de venda (balcão e caixotes)
            var sells = new System.Collections.Generic.List<World.SellStation>();
            void FindSell(Node n) { foreach (Node c in n.GetChildren()) { if (c is World.SellStation ss) sells.Add(ss); FindSell(c); } }
            FindSell(Main.World);
            if (sells.Count > 0)
            {
                var ss = sells[0];
                var f = ss.GlobalTransform.Basis.Z; var sd = ss.GlobalTransform.Basis.X;
                LookAtPoint(p, ss.GlobalPosition + f * 3.6f, ss.GlobalPosition + Vector3.Up * 1.0f);
                await Wait(0.9f);
                await Shot("35_decor_sell_0");
                LookAtPoint(p, ss.GlobalPosition + f * 2.6f - sd * 2.6f, ss.GlobalPosition + Vector3.Up * 0.8f - sd * 0.8f);
                await Wait(0.9f);
                await Shot("35_decor_sell_1");
                // posto avançado inteiro, de longe (fica atrás da venda)
                LookAtPoint(p, ss.GlobalPosition + f * 8.5f + sd * 2.5f, ss.GlobalPosition - f * 2.5f + Vector3.Up * 1.4f);
                await Wait(0.9f);
                await Shot("35_decor_outpost_0");
            }
            var terms = new System.Collections.Generic.List<World.ShopTerminal>();
            void FindTerm(Node n) { foreach (Node c in n.GetChildren()) { if (c is World.ShopTerminal tt) terms.Add(tt); FindTerm(c); } }
            FindTerm(Main.World);
            if (terms.Count > 0)
            {
                var tt = terms[0];
                var f = tt.GlobalTransform.Basis.Z; var sd = tt.GlobalTransform.Basis.X;
                LookAtPoint(p, tt.GlobalPosition + f * 3.2f - sd * 1.2f, tt.GlobalPosition + Vector3.Up * 1.4f);
                await Wait(0.9f);
                await Shot("35_decor_terminal_0");

            }
            // lago do Reservatório Abissal e um depósito comum da região 2
            {
                var La = Main.World.Layouts[GameData.Regions.FindIndex(r => r.Id == "abyss")];
                if (La.Lake != null)
                {
                    LookAtPoint(p, La.OnGround(La.Lake.A.X + La.Lake.RA * 0.7f, La.Lake.A.Y, 0.1f), new Vector3(La.Lake.A.X, La.Lake.Level + 4f, La.Lake.A.Y));
                    await Wait(1.0f);
                    await Shot("35_decor_lake_0");
                }
                var dep = Main.World.Crystals.FirstOrDefault(x => x.IsMineable && x.Material.Archetype == OreArchetype.Crystal && World.WorldBuilder.RegionAt(x.GlobalPosition).Id == "depths");
                if (dep != null)
                {
                    LookAtPoint(p, dep.StandPoint + Vector3.Up * 0.3f, dep.GlobalPosition + Vector3.Up * 0.2f);
                    await Wait(0.9f);
                    await Shot("35_decor_deposit_0");
                }
            }
            // portão de região (campo de força)
            if (Main.World.Gates.Count > 0)
            {
                var gt = Main.World.Gates[0];
                var gf = gt.GlobalTransform.Basis.Z;
                LookAtPoint(p, gt.GlobalPosition + gf * 7.5f, gt.GlobalPosition + Vector3.Up * 2.2f);
                await Wait(0.9f);
                await Shot("35_decor_gate_0");
            }
            // Catedral: colunas colossais
            for (int ci = 0; ci < Mathf.Min(2, Main.World.CathedralColumns.Count); ci++)
            {
                var col = Main.World.CathedralColumns[ci];
                var Lc = Main.World.Layouts[GameData.Regions.FindIndex(r => r.Id == "cathedral")];
                Vector3? st = null;
                for (int d = 0; d < 8 && st == null; d++)
                {
                    var q = col + new Vector3(Mathf.Cos(d * 0.785f), 0, Mathf.Sin(d * 0.785f)) * 11f;
                    if (Lc.WallDist(q.X, q.Z) < -1.5f) st = Lc.OnGround(q.X, q.Z, 0.1f);
                }
                if (st == null) continue;
                LookAtPoint(p, st.Value, col + Vector3.Up * 5f);
                await Wait(1.0f);
                await Shot($"35_decor_cathedral_{ci}");
            }
            // cogumelo gigante (Jardim Fúngico): instância de caule mais alta
            {
                MultiMeshInstance3D best = null; int bi = -1; float bh = 0;
                void FindStem(Node n) { foreach (Node c in n.GetChildren()) { if (c is MultiMeshInstance3D mm && mm.Multimesh?.Mesh == World.DecorMeshes.MushroomStem()) for (int i = 0; i < mm.Multimesh.InstanceCount; i++) { float h = mm.Multimesh.GetInstanceTransform(i).Basis.Y.Length(); if (h > bh) { bh = h; best = mm; bi = i; } } FindStem(c); } }
                FindStem(Main.World);
                if (best != null && bh > 3f)
                {
                    var bp = best.GlobalTransform * best.Multimesh.GetInstanceTransform(bi).Origin;
                    var Lr = Main.World.Layouts[GameData.Regions.IndexOf(World.WorldBuilder.RegionAt(bp))];
                    Vector3? st = null;
                    for (int d = 0; d < 8 && st == null; d++)
                    {
                        var q = bp + new Vector3(Mathf.Cos(d * 0.785f), 0, Mathf.Sin(d * 0.785f)) * 9f;
                        if (Lr.WallDist(q.X, q.Z) < -1.2f) st = Lr.OnGround(q.X, q.Z, 0.1f);
                    }
                    if (st != null)
                    {
                        LookAtPoint(p, st.Value, bp + Vector3.Up * bh * 0.7f);
                        await Wait(1.0f);
                        await Shot("35_decor_giant_mushroom");
                    }
                }
            }
            // Vila dos Ferreiros: noite (depois do turno) e manhã
            {
                var tw = Main.World.Town;
                gs.EndDay();
                gs.EnsureContracts();
                var c0 = World.Town.PlazaCenter;
                var views = new (Vector3 from, Vector3 at, string name)[]
                {
                    (tw.ArrivePoint, c0 + new Vector3(4f, 1.5f, 0), "town_arrive"),
                    (c0 + new Vector3(2f, 0.1f, -2f), c0 + new Vector3(0, 1.8f, -11f), "town_smith"),
                    (c0 + new Vector3(4f, 0.1f, 6f), World.PlayerHouse.Origin + new Vector3(0, 2.5f, 0), "town_house"),
                    (c0 + new Vector3(-4f, 0.1f, 2f), c0 + new Vector3(3f, 1.6f, 9f), "town_board"),
                    (c0 + new Vector3(10f, 0.1f, 8f), c0 + new Vector3(-10f, 3f, -2f), "town_wide"),
                };
                foreach (var (from, at, name) in views)
                {
                    LookAtPoint(p, from, at);
                    await Wait(1.0f);
                    await Shot($"36_{name}_night");
                }
                gs.StartNewDay();
                LookAtPoint(p, views[4].from, views[4].at);
                await Wait(1.2f);
                await Shot("36_town_wide_day");
                LookAtPoint(p, World.Town.PlazaCenter + new Vector3(-6f, 0.1f, 0), World.Town.PlazaCenter + new Vector3(-11.5f, 4f, 0));
                await Wait(0.6f);
                await Shot("36_town_station_day");
                UiBus.RequestMissions();
                await Wait(0.6f);
                await Shot("36_town_missions_ui");
                Main.CloseAllForTour();
                await Wait(0.3f);
                // forja: bigorna e tela (com minérios para as luvas e as botas)
                foreach (var gsl in new[] { GearSlot.Gloves, GearSlot.Boots })
                    foreach (var fc in GearData.Of(gsl, 0).Costs) gs.Data.Inventory[fc.Mat] = gs.InventoryOf(fc.Mat) + fc.Amount;
                gs.AddMoney(5000);
                gs.RaiseAll();
                LookAtPoint(p, c0 + new Vector3(0.6f, 0.1f, -6.4f), c0 + new Vector3(0.6f, 1.0f, -9.4f));
                await Wait(0.8f);
                await Shot("36_town_anvil_day");
                UiBus.RequestForge();
                await Wait(0.6f);
                await Shot("36_town_forge_ui");
                Main.CloseAllForTour();
            }
            // elevador da mina (torre de içamento)
            {
                var els = new System.Collections.Generic.List<World.MineElevator>();
                void FindEl(Node n) { foreach (Node c in n.GetChildren()) { if (c is World.MineElevator me && !me.GoesDown) els.Add(me); FindEl(c); } }
                FindEl(Main.World);
                if (els.Count > 0)
                {
                    var el = els[0];
                    var ef = el.GlobalTransform.Basis.Z;
                    LookAtPoint(p, el.GlobalPosition + ef * 7.5f, el.GlobalPosition + Vector3.Up * 3.2f);
                    await Wait(0.9f);
                    await Shot("35_decor_elevator_0");
                }
            }
            // geodos: um comum e um especial (colossal)
            int gshot = 0;
            foreach (bool special in new[] { false, true })
            {
                var g = Main.World.Crystals.FirstOrDefault(x => x.IsMineable && x.Material.Archetype == OreArchetype.Geode && x.Material.Special == special);
                if (g == null) continue;
                float dist = special ? 7f : 2.4f;
                var from = g.StandPoint;
                var dir = (from - g.GlobalPosition); dir.Y = 0;
                if (dir.LengthSquared() < 0.01f) dir = Vector3.Back;
                from = g.GlobalPosition + dir.Normalized() * dist;
                var L = Main.World.Layouts[GameData.Regions.IndexOf(World.WorldBuilder.RegionAt(from))];
                from = L.OnGround(from.X, from.Z, 0.1f) + Vector3.Up * (special ? 2.5f : 0.6f);
                LookAtPoint(p, from, g.GlobalPosition + Vector3.Up * (special ? 1.5f : 0.2f));
                await Wait(0.9f);
                await Shot($"35_decor_geode_{gshot++}");
            }
            // scanner de minério (tripé)
            var scanners = new System.Collections.Generic.List<World.ScannerNode>();
            void FindScanners(Node n) { foreach (Node c in n.GetChildren()) { if (c is World.ScannerNode sn) scanners.Add(sn); FindScanners(c); } }
            FindScanners(Main.World);
            if (scanners.Count > 0)
            {
                var sc = scanners[0];
                var fwd = -sc.GlobalTransform.Basis.Z;
                var side = sc.GlobalTransform.Basis.X;
                LookAtPoint(p, sc.GlobalPosition + side * 2.2f + fwd * 0.6f, sc.GlobalPosition + Vector3.Up * 1.1f + fwd * 0.4f);
                await Wait(0.9f);
                await Shot("35_decor_scanner_0");
                LookAtPoint(p, sc.GlobalPosition - fwd * 2.0f + side * 0.8f, sc.GlobalPosition + Vector3.Up * 1.0f + fwd * 1.5f);
                await Wait(0.9f);
                await Shot("35_decor_scanner_1");
            }
            foreach (var (mesh, name, dist, lift) in targets)
            {
                int shot = 0;
                foreach (var mm in all.Where(m => m.Multimesh?.Mesh == mesh))
                {
                    var count = mm.Multimesh.VisibleInstanceCount < 0 ? mm.Multimesh.InstanceCount : mm.Multimesh.VisibleInstanceCount;
                    for (int k = 0; k < count && shot < 2; k += System.Math.Max(1, count / 3))
                    {
                        var at = mm.GlobalTransform * mm.Multimesh.GetInstanceTransform(k).Origin;
                        if (name == "stalactite") at += Vector3.Down * 1.5f;
                        // de que lado há chão livre: tenta 8 direções
                        Vector3? stand = null;
                        for (int d = 0; d < 8 && stand == null; d++)
                        {
                            var dir = new Vector3(Mathf.Cos(d * 0.785f), 0, Mathf.Sin(d * 0.785f));
                            var q = at + dir * dist;
                            var L = Main.World.Layouts[GameData.Regions.IndexOf(World.WorldBuilder.RegionAt(q))];
                            if (L.WallDist(q.X, q.Z) < -1.2f) stand = L.OnGround(q.X, q.Z, 0.1f);
                        }
                        if (stand == null) continue;
                        LookAtPoint(p, stand.Value, at + Vector3.Up * lift);
                        await Wait(0.7f);
                        await Shot($"35_decor_{name}_{shot++}");
                    }
                    if (shot >= 2) break;
                }
            }
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-deep"))
        {
            foreach (var reg in GameData.Regions) if (!gs.IsUnlocked(reg.Id)) gs.Data.UnlockedRegions.Add(reg.Id);
            gs.Data.Discovered.Add("heart"); gs.Data.GameCompleted = true;
            gs.Data.StoryStage = CrystalExcavator.Data.StoryData.GateOpen;
            gs.Data.WeaponKind = 0; gs.Data.WeaponOres = new() { "runestone", "lumenite" };
            gs.RaiseAll();
            await Wait(0.5f);
            Main.CloseAllForTour();
            var deep = GameData.Deep.First();
            var at = Main.World.DeepArrivals[deep.Id];
            p.GlobalPosition = at; p.SetLook(0, 0);
            p.SetWeaponMode(true);
            await Wait(1.5f);
            await Shot("43_deep_arrival");
            var ens = Main.World.Enemies.Enemies;
            foreach (var reg in GameData.Deep)
            {
                var regAt = Main.World.DeepArrivals[reg.Id];
                p.GlobalPosition = regAt; p.SetLook(0, 0);
                await Wait(0.6f);
                foreach (var def in ens.Where(x => x.Def.Region == reg.Id).Select(x => x.Def).Distinct().ToList())
                {
                    var e = ens.Where(x => x.Def == def).OrderBy(x => x.Home.DistanceTo(regAt)).First();
                    var L = Main.World.LayoutAt(e.Home);
                    float dist = def.Guardian ? 9f : 6f;
                    for (int k = 0; k < 16; k++)
                    {
                        float a2 = k * Mathf.Tau / 16f;
                        var q = e.Home + new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2)) * dist;
                        if (L.WallDist(q.X, q.Z) < -1.5f && L.Reachable(q.X, q.Z)) { LookAtPoint(p, L.OnGround(q.X, q.Z, 0.1f), e.GlobalPosition + Vector3.Up * 1.0f * e.Def.Scale); break; }
                    }
                    p.InputEnabled = false;
                    await Wait(1.6f);
                    var look = e.GlobalPosition + Vector3.Up * (def.Flying ? 0f : 0.8f * e.Def.Scale);
                    var from = p.GlobalPosition;
                    var flat = new Vector2(look.X - from.X, look.Z - from.Z).Length();
                    p.SetLook(Mathf.Atan2(-(look.X - from.X), -(look.Z - from.Z)), Mathf.Atan2(look.Y - (from.Y + 1.6f), flat));
                    p.WeaponView.Swing(0.5f);
                    await Wait(0.12f);
                    await Shot($"44_{reg.Id}_{def.Id}");
                    p.RestoreHp();
                }
            }
            foreach (var id in new[] { "runestone", "lumenite", "ancientglass", "diamond", "meteorite" }) { gs.Data.Discovered.Add(id); gs.Data.Inventory[id] = 25; }
            gs.AddMoney(900_000_000);
            Main.CloseAllForTour();
            p.GlobalPosition = Main.World.Town.ArrivePoint;
            await Wait(0.5f);
            UiBus.RequestForge();
            await Wait(0.8f);
            var fs = Main.GetChildren().OfType<CrystalExcavator.UI.ForgeScreen>().First();
            fs.SelectForTour(0, new[] { "runestone", "lumenite" });
            await Wait(0.5f);
            await Shot("43_forge_weapons");
            fs.SelectForTour(2, new[] { "runestone", "lumenite", "ancientglass", "diamond" });
            await Wait(0.5f);
            await Shot("43_forge_armor");
            Main.CloseAllForTour();
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-story"))
        {
            foreach (var reg in GameData.Regions) if (!gs.IsUnlocked(reg.Id)) gs.Data.UnlockedRegions.Add(reg.Id);
            gs.Data.Discovered.Add("heart");
            gs.Data.GameCompleted = true;
            gs.RaiseAll();
            await Wait(0.5f);
            Main.CloseAllForTour();
            var bram = Main.World.Town.Bram;
            LookAtPoint(p, bram.GlobalPosition + new Vector3(-1.2f, 0.1f, 1.7f), bram.GlobalPosition + Vector3.Up * 0.8f);
            await Wait(1.5f);
            await Shot("42_bram");
            UiBus.RequestTalk("bram");
            await Wait(1.2f);
            await Shot("42_dialogue_bram");
            var dlg = Main.GetChildren().OfType<CrystalExcavator.UI.DialogueScreen>().FirstOrDefault();
            for (int k = 0; k < 3 && dlg != null; k++) { dlg.Advance(); await Wait(0.2f); dlg.Advance(); await Wait(0.3f); }
            await Wait(0.8f);
            await Shot("42_dialogue_hero");
            for (int k = 0; k < 12 && dlg != null && dlg.IsPlaying; k++) { dlg.Advance(); await Wait(0.15f); }
            await Wait(1.0f);
            await Shot("42_flashback");
            var fb = Main.GetChildren().OfType<CrystalExcavator.UI.IntroComic>().FirstOrDefault(c => c.Name == "Flashback");
            fb?.Skip();
            await Wait(1.0f);
            for (int k = 0; k < 12 && dlg != null && dlg.IsPlaying; k++) { dlg.Advance(); await Wait(0.15f); }
            await Wait(0.8f);
            GD.Print($"[Tour] etapa da história: {gs.Story}");
            var gate = Main.World.SealedGate;
            var fwd = gate.GlobalTransform.Basis.Z;
            LookAtPoint(p, gate.GlobalPosition + fwd * 11f + Vector3.Up * 0.2f, gate.GlobalPosition + Vector3.Up * 3.5f);
            await Wait(1.5f);
            await Shot("42_gate_sealed");
            gs.SetStory(CrystalExcavator.Data.StoryData.OpenGate);
            UiBus.RequestGate();
            await Wait(3.5f);
            await Shot("42_gate_open");
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-diary"))
        {
            foreach (var reg in GameData.Regions) if (!gs.IsUnlocked(reg.Id)) gs.Data.UnlockedRegions.Add(reg.Id);
            gs.RaiseAll();
            for (int di = 0; di < 3; di++)
            {
                var dn = Main.World.Diaries[di * 4];
                var fwd = new Vector3(0, 0, 1).Rotated(Vector3.Up, dn.Rotation.Y);
                LookAtPoint(p, dn.GlobalPosition + fwd * 2.4f + Vector3.Up * 0.1f, dn.GlobalPosition + Vector3.Up * 0.5f);
                await Wait(1.2f);
                await Shot($"41_diary_{di}");
            }
            UiBus.RequestDiary(Main.World.Diaries[4].DiaryId);
            await Wait(0.6f);
            await Shot("41_diary_ui");
            Main.CloseAllForTour();
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-intro"))
        {
            var ic = new CrystalExcavator.UI.IntroComic();
            AddChild(ic);
            await Wait(0.2f);
            ic.Play();
            for (int i = 0; i < CrystalExcavator.UI.IntroComic.Captions.Length; i++)
            {
                await Wait(i == 0 ? 0.6f : 0.4f);
                ic.Advance(); // completa a fala
                await Wait(0.5f);
                await Shot($"40_intro_{i + 1}");
                ic.Advance();
            }
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-house"))
        {
            gs.AddMoney(1_000_000_000);
            gs.EndDay();
            UiBus.RequestHome();
            await Wait(1.8f);
            Main.CloseAllForTour();
            p.Tool.Visible = false;
            // 5 patamares: compra as melhorias aos poucos e fotografa o cômodo
            var ids = new[] { "bed", "workbench", "wardrobe", "kitchen", "trophies" };
            for (int tier = 0; tier <= 4; tier++)
            {
                while (World.HouseDiorama.HouseTier(gs) < tier)
                    foreach (var id in ids) { gs.TryBuyHouse(id); if (World.HouseDiorama.HouseTier(gs) >= tier) break; }
                Main.World.House.Room.Rebuild();
                p.GlobalPosition = World.PlayerHouse.Origin + new Vector3(2.3f, 0.1f, 2.0f);
                p.SetLook(Mathf.Atan2(2.3f + 0.4f, 2.0f + 0.6f), -0.12f);
                p.Camera.Fov = 95;
                await Wait(0.8f);
                await Shot($"26_house_tier_{tier}");
            }
            // melhorias novas no máximo: suporte, baú, lampiões e carregador
            foreach (var id in new[] { "tool_rack", "chest", "lanterns", "charger" })
                while (gs.TryBuyHouse(id)) { }
            Main.World.House.Room.Rebuild();
            p.GlobalPosition = World.PlayerHouse.Origin + new Vector3(-0.4f, 0.1f, 1.0f);
            p.SetLook(0f, -0.05f);
            await Wait(0.8f);
            await Shot("27_house_back");
            p.GlobalPosition = World.PlayerHouse.Origin + new Vector3(-0.3f, 0.1f, -0.9f);
            p.SetLook(Mathf.Pi, -0.25f);
            await Wait(0.8f);
            await Shot("27_house_front");
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-goblin"))
        {
            var camp = Main.World.Goblins;
            var g = gs.GoblinData("caves");
            g.Found = true;
            gs.AddMoney(10_000_000);
            gs.TryHireGoblin("caves");
            var view = camp.HutPos + new Vector3(4.5f, 0.1f, -4.5f);
            var Lg = Main.World.Layouts[0];
            view = Lg.OnGround(view.X, view.Z, 0.1f);
            int k = 0;
            foreach (float prog in new[] { 0.15f, 0.5f, 0.85f })
            {
                g.HireDay = gs.Data.Day;
                gs.Data.DayTimeSeconds = gs.DayLength * prog * GameState.GoblinBuildDays;
                if (prog * GameState.GoblinBuildDays >= 1f) { g.HireDay = gs.Data.Day - 1; gs.Data.DayTimeSeconds = gs.DayLength * (prog * GameState.GoblinBuildDays - 1f); }
                camp.SyncState();
                LookAtPoint(p, view, camp.HutPos + Vector3.Up * 1f);
                await Wait(1.2f);
                await Shot($"25_build_{k++}");
            }
            g.HutBuilt = true;
            gs.Data.DayTimeSeconds = 20;
            for (int lv = 0; lv <= 4; lv++)
            {
                g.HutLevel = lv; g.CarryLevel = lv; g.SpeedLevel = lv;
                camp.SyncState();
                camp.Grub.GlobalPosition = camp.GrubHome;
                LookAtPoint(p, view, camp.HutPos + Vector3.Up * 1.2f);
                await Wait(1.0f);
                await Shot($"25_hut_level_{lv}");
            }
            UiBus.RequestGoblin(camp);
            await Wait(0.6f);
            await Shot("25_goblin_panel");
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-ropes"))
        {
            var L = Main.World.Layouts[0];
            var pit = L.Pits[0];
            var rope = Main.World.PitRopes.First(r => r.Top.DistanceTo(pit.RopeTop) < 0.1f);
            var c3 = new Vector3(pit.Center.X, pit.RopeTop.Y, pit.Center.Y);
            var back = (pit.TopExit - c3); back.Y = 0; back = back.Normalized();
            LookAtPoint(p, pit.TopExit + back * 2f + Vector3.Up * 0.1f, new Vector3(pit.Center.X, pit.Floor, pit.Center.Y));
            await Wait(1f);
            await Shot("24_pit_edge");
            LookAtPoint(p, pit.TopExit + Vector3.Up * 0.1f, rope.Top + Vector3.Up * 0.3f);
            await Wait(0.6f);
            await Shot("24_rope_top");
            rope.Interact(p);
            Input.ActionPress("move_back");
            await Wait(1.1f);
            p.SetLook(p.Yaw, -0.2f);
            await Wait(0.3f);
            GD.Print($"[Tour] descendo: climbing={p.Climbing} pos={p.GlobalPosition}");
            await Shot("24_climbing_down");
            for (int i = 0; i < 400 && p.Climbing; i++) await Wait(0.05f);
            Input.ActionRelease("move_back");
            await Wait(0.5f);
            p.SetLook(p.Yaw + Mathf.Pi, 0.9f);
            await Wait(0.6f);
            await Shot("24_pit_bottom_up");
            p.SetLook(p.Yaw, 0.1f);
            await Wait(0.4f);
            await Shot("24_pit_bottom");
            rope.Interact(p);
            Input.ActionPress("move_forward");
            await Wait(1.6f);
            p.SetLook(p.Yaw, 0.6f);
            await Wait(0.3f);
            GD.Print($"[Tour] subindo: climbing={p.Climbing} pos={p.GlobalPosition}");
            await Shot("24_climbing_up");
            p.SetLook(p.Yaw, -0.1f);
            await Wait(0.2f);
            await Shot("24_climbing_up_level");
            for (int i = 0; i < 400 && p.Climbing; i++) await Wait(0.05f);
            Input.ActionRelease("move_forward");
            await Wait(0.5f);
            // vista ampla de uma câmara grande (mapa maior)
            var big = L.Chambers.Where(c => c.Wing == null && c != L.Camp).OrderByDescending(c => c.RA).First();
            LookAtPoint(p, L.OnGround(big.A.X + big.RA * 0.6f, big.A.Y, 0.1f), L.OnGround(big.A.X - big.RA, big.A.Y, 3f));
            await Wait(1f);
            await Shot("24_big_chamber");
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-water"))
        {
            foreach (var r in GameData.Regions) if (!gs.IsUnlocked(r.Id)) gs.Data.UnlockedRegions.Add(r.Id);
            int k = 0;
            foreach (var w in World.WorldBuilder.WaterSpots.Where((_, i) => i % 3 == 0).Take(8))
            {
                var L = Main.World.LayoutAt(w);
                var from = w + new Vector3(4f, 0, 4f);
                if (L.InGrid(from.X, from.Z)) from.Y = L.Ground(from.X, from.Z) + 0.1f;
                LookAtPoint(p, from, w + Vector3.Up * 0.3f);
                await Wait(1.0f);
                await Shot($"23_water_{k++}");
            }
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-ores"))
        {
            await OresTour(p, gs);
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-tools"))
        {
            await ToolsTour(p, gs);
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        if (System.Linq.Enumerable.Contains(OS.GetCmdlineUserArgs(), "--shots-build"))
        {
            // só a parte das construções (iteração rápida)
            await BuildingsTour(p, gs);
            SaveSystem.Delete();
            GameState.Instance.QuitClean();
            return;
        }
        var L0 = Main.World.Layouts[0];
        var cc = L0.OnGround(L0.Camp.A.X, L0.Camp.A.Y);
        LookAtPoint(p, L0.OnGround(cc.X + 2f, cc.Z + 6f, 0.1f), cc + new Vector3(-9.5f, 1.5f, 0f));
        await Wait(0.4f);
        await Shot("02b_outpost");
        LookAtPoint(p, L0.OnGround(cc.X + 3f, cc.Z + 7f, 0.1f), Main.World.Layouts[0].OnGround(cc.X + 6.5f, cc.Z + 4f, 1.2f));
        await Wait(0.4f);
        await Shot("02c_terminal");
        LookAtPoint(p, L0.OnGround(cc.X - 2.5f, cc.Z + 7.5f, 0.1f), Main.World.SellStation.GlobalPosition + Vector3.Up * 1.2f);
        await Wait(0.4f);
        await Shot("02d_sell");
        LookAtPoint(p, PathAt(0, 0.35f), PathAt(0, 0.55f) + Vector3.Up * 2f);
        await Wait(0.6f);
        await Shot("02e_cave");
        LookAtPoint(p, L0.OnGround(cc.X + 2.5f, cc.Z + 4f, 0.1f), Main.World.Elevator.GlobalPosition + Vector3.Up * 2.5f);
        await Wait(0.4f);
        await Shot("02f_elevator");
        // cada goblin escondido na sua câmara secreta
        int gi = 0;
        foreach (var gcamp in Main.World.GoblinCamps)
        {
            var Lg = Main.World.Layouts[gcamp.RegionIndex];
            var hp = gcamp.HideoutPos;
            Vector3 from = hp + new Vector3(3f, 0, 0);
            foreach (var off in new[] { new Vector3(3f, 0, 0), new Vector3(-3f, 0, 0), new Vector3(0, 0, 3f), new Vector3(0, 0, -3f) })
                if (Lg.Reachable(hp.X + off.X, hp.Z + off.Z)) { from = hp + off; break; }
            LookAtPoint(p, Lg.OnGround(from.X, from.Z, 0.1f), hp + Vector3.Up * 0.9f);
            await Wait(0.9f);
            await Shot($"02g_goblin_{gi++}_{gcamp.Def.Name}");
        }
        p.GlobalPosition = Main.World.SpawnPoint;
        p.GlobalPosition = Main.World.SpawnPoint;
        p.SetLook(0, 0);

        // mira em um cristal e minera
        var c = Main.World.Crystals.Where(x => x.Material.Id == "quartz")
            .OrderBy(x => x.GlobalPosition.DistanceTo(p.GlobalPosition)).First();
        LookAtPoint(p, c.StandPoint, c.Center);
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
        LookAtPoint(p, tc.StandPoint, tc.Center);
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
        LookAtPoint(p, PathAt(0, 0.12f), PathAt(0, 0.4f) + Vector3.Up * 2f);
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
        var g0 = Main.World.Gates[0].GlobalPosition;
        LookAtPoint(p, PathAt(0, 0.93f), g0 + Vector3.Up * 2.5f);
        await Wait(1f);
        await Wait(0.5f);
        await Shot("08_gate");

        // região 2
        gs.AddMoney(GameData.Regions[2].UnlockCost + GameData.Regions[1].UnlockCost);
        gs.TryUnlockRegion("depths");
        gs.TryUnlockRegion("core");
        foreach (var id in UpgradeIds.All) gs.Data.Upgrades[id] = 10;
        gs.Data.Upgrades[UpgradeIds.Damage] = 18;
        gs.RaiseAll();
        LookAtPoint(p, PathAt(1, 0.2f), PathAt(1, 0.45f) + Vector3.Up * 2f);
        await Wait(3f);
        await Shot("09_region2");
        Main.Events.Begin("depths");
        await Wait(2.5f);
        await Shot("09b_event_depths");
        Main.Events.Stop();
        float fps2 = (float)Engine.GetFramesPerSecond();
        string dc2 = DrawStats();

        // região 3 e o Coração Ancestral
        var L2 = Main.World.Layouts[2];
        var hcen = L2.OnGround(L2.Heart.A.X, L2.Heart.A.Y);
        LookAtPoint(p, L2.OnGround(hcen.X + 3f, hcen.Z + 15f, 0.1f), hcen + Vector3.Up * 4f);
        await Wait(3f);
        await Shot("10_region3");
        gs.AddMoney(GameData.Regions[3].UnlockCost);
        gs.TryUnlockRegion("abyss");
        LookAtPoint(p, PathAt(3, 0.15f), PathAt(3, 0.4f) + Vector3.Up * 2f);
        await Wait(3f);
        await Shot("10b_region4");
        var L3 = Main.World.Layouts[3];
        if (L3.Lake != null) LookAtPoint(p, L3.OnGround(L3.Lake.A.X + L3.Lake.RA * 0.7f, L3.Lake.A.Y, 0.1f), new Vector3(L3.Lake.A.X, L3.Lake.Level + 4f, L3.Lake.A.Y));
        await Wait(0.6f);
        await Shot("10c_region4b");
        // câmaras com minério em destaque e túneis secretos de cada região
        {
            int wi = 0;
            foreach (var r in GameData.Regions)
            {
                var L = Main.World.Layouts[r.Index];
                foreach (var ch in L.Chambers)
                {
                    if (ch.Wing == null) continue;
                    var dirc = new Vector3(ch.A.X, 0, ch.A.Y).Normalized();
                    var pos = L.OnGround(ch.A.X - dirc.X * ch.RA * 0.6f, ch.A.Y - dirc.Z * ch.RA * 0.6f, 0.1f);
                    p.GlobalPosition = pos;
                    p.SetLook(Mathf.Atan2(-(ch.A.X - pos.X), -(ch.A.Y - pos.Z)), 0.05f);
                    await Wait(0.8f);
                    await Shot($"10w_{wi++}_{ch.Id}");
                }
                if (L.Crawls.Count > 0)
                {
                    var cr = L.Crawls[0];
                    var d = new Vector3(cr.B.X - cr.A.X, 0, cr.B.Y - cr.A.Y).Normalized();
                    var pos = L.OnGround(cr.A.X - d.X * 4f, cr.A.Y - d.Z * 4f, 0.1f);
                    p.GlobalPosition = pos;
                    p.SetLook(Mathf.Atan2(-d.X, -d.Z), -0.1f);
                    await Wait(0.8f);
                    await Shot($"10s_{r.Id}_secret");
                }
            }
        }
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

        // fim do turno e casa
        Main.CloseAllForTour();
        LookAtPoint(p, L0.OnGround(cc.X + 2f, cc.Z + 1f, 0.1f), Main.World.SellStation.GlobalPosition + Vector3.Up);
        gs.AddItem("quartz", 12);
        gs.Data.DayTimeSeconds = gs.DayLength - 50;
        await Wait(0.8f);
        await Shot("15a_clock_late");
        gs.EndDay();
        await Wait(1.0f);
        await Shot("15b_day_over");
        gs.SellAll();
        gs.AddMoney(600_000);
        foreach (var (id, n) in new[] { ("bed", 2), ("workbench", 3), ("wardrobe", 2), ("kitchen", 3), ("trophies", 3) })
            for (int i = 0; i < n; i++) gs.TryBuyHouse(id);
        foreach (var id in new[] { "magnet", "firm_strike", "cadence", "haggle" }) gs.TryBuySkill(id);
        UiBus.RequestHome();
        await Wait(1.6f);
        await Shot("15c_house");
        p.GlobalPosition = Main.World.House.WakePoint + new Vector3(0.4f, 0, 0.9f);
        p.SetLook(2.4f, -0.1f);
        await Wait(0.5f);
        await Shot("15c_house2");
        UiBus.RequestHomePanel();
        await Wait(0.8f);
        await Shot("15c_home");
        var home = Main.GetChildren().OfType<UI.HomeScreen>().First();
        home.ShowSkillsTab();
        home.SelectSkill("direct_collect");
        await Wait(0.4f);
        await Shot("15d_skills");
        Main.CloseAllForTour();
        gs.StartNewDay();

        // rebelião goblin
        Main.CloseAllForTour();
        gs.GoblinData("caves").State = (int)GameState.GoblinMood.Rebel;
        gs.GoblinData("caves").Found = true;
        var gc = Main.World.Goblins;
        gc.StartRebellion();
        LookAtPoint(p, L0.OnGround(gc.GrubHome.X - 5f, gc.GrubHome.Z, 0.1f), gc.GrubHome + Vector3.Up * 0.8f);
        await Wait(1.6f);
        await Shot("15g_rebellion");
        gc.EndRebellion(true);

        await BuildingsTour(p, gs);
        await OreGallery(p);

        GD.Print($"[Tour] FPS (software, Xvfb): R1={fps1:0} R2={fps2:0} R3={fps3:0}");
        GD.Print($"[Tour] Qualidade {Settings.Quality} | R1: {dc1} | R2: {dc2} | R3: {dc3}");
        SaveSystem.Delete();
        GameState.Instance.QuitClean();
    }

    /// <summary>Construções: postes, funil com esteira até a Estação de Venda, drone e modo construção.</summary>
    async Task BuildingsTour(Player.PlayerController p, GameState gs)
    {
        Main.CloseAllForTour();
        gs.AddMoney(5_000_000);
        gs.Data.DayTimeSeconds = 30;
        var bm = Main.Buildings;
        var L0 = Main.World.Layouts[0];
        Vector3 Spot(string type, Vector3 near)
        {
            for (int r = 0; r < 16; r++)
                for (int a = 0; a < 12; a++)
                {
                    float ang = a * Mathf.Tau / 12f;
                    float x = near.X + Mathf.Cos(ang) * r * 1.5f, z = near.Z + Mathf.Sin(ang) * r * 1.5f;
                    if (!L0.InGrid(x, z)) continue;
                    var q = L0.OnGround(x, z);
                    if (bm.CanPlace(type, q, out _)) return q;
                }
            return near;
        }
        foreach (var sk in new[] { Data.SkillIds.BuildBelt, Data.SkillIds.BuildHopper, Data.SkillIds.BuildDrone, Data.SkillIds.BuildRobot })
            if (!gs.HasSkill(sk)) gs.Data.Skills.Add(sk);
        var bs = Spot(Data.BuildIds.Belt, PathAt(0, 0.12f));
        var beltB = bm.PlaceBelt(bs, bm.SellIntake);
        var belt = beltB != null ? bm.BeltOf(beltB.Id) : null;
        // funil ao lado da esteira
        Vector3 hp = bs;
        BuildingSave hop = null;
        if (belt != null)
            for (float s0 = 4f; s0 < belt.Length - 4f && hop == null; s0 += 1f)
            {
                belt.Sample(s0, out var bp0, out var d0);
                var side0 = d0.Cross(Vector3.Up).Normalized();
                foreach (var sg in new[] { 1.8f, -1.8f })
                {
                    var q = L0.OnGround((bp0 + side0 * sg).X, (bp0 + side0 * sg).Z);
                    if (bm.CanPlace(Data.BuildIds.Hopper, q, out _) && bm.HopperSpot(q, out _, out _, out _)) { hop = bm.Place(Data.BuildIds.Hopper, q, 0); hp = q; break; }
                }
            }
        // postes ao longo da esteira
        if (belt != null)
            for (float s = 6f; s < belt.Length - 6f; s += 14f)
            {
                belt.Sample(s, out var bp, out var dir);
                var side = dir.Cross(Vector3.Up).Normalized() * 2.2f;
                bm.Place(Data.BuildIds.Light, Spot(Data.BuildIds.Light, bp + side), 0);
            }
        var dockPos = Spot(Data.BuildIds.Drone, hp + new Vector3(4f, 0, 4f));
        bm.Place(Data.BuildIds.Drone, dockPos, 0);
        if (beltB != null) { gs.AddItem("amethyst", 40); gs.AddItem("quartz", 60); gs.DepositInventoryInHopper(beltB.Id); }
        await Wait(4f);
        LookAtPoint(p, hp + (Main.World.SellStation.GlobalPosition - hp).Normalized() * 5f + new Vector3(2.5f, 0.1f, 0), hp + Vector3.Up * 0.8f);
        await Wait(0.8f);
        await Shot("18a_hopper");
        var st = Main.World.SellStation.GlobalPosition;
        if (belt != null)
        {
            // fim da esteira chegando na Estação de Venda, visto de trás e do alto
            belt.Sample(belt.Length - 7f, out var near, out var nd);
            LookAtPoint(p, near - nd * 2f + nd.Cross(Vector3.Up).Normalized() * 2.5f + Vector3.Up * 0.1f, st + Vector3.Up * 0.8f);
            await Wait(0.8f);
            await Shot("18b_belt_station");
            belt.Sample(belt.Length * 0.45f, out var mid, out var md);
            LookAtPoint(p, mid - md * 4f + md.Cross(Vector3.Up).Normalized() * 1.6f + Vector3.Up * 0.1f, mid + md * 4f);
            await Wait(0.8f);
            await Shot("18c_belt_close");
            p.SetLamp(false);
            LookAtPoint(p, mid - md * 9f + md.Cross(Vector3.Up).Normalized() * 3f + Vector3.Up * 0.1f, mid + md * 10f + Vector3.Up * 1.5f);
            await Wait(0.8f);
            await Shot("18c_posts_lamp_off");
            p.SetLamp(true);
        }
        // drone de área buscando fragmentos
        gs.Data.Inventory.Clear();
        var shardAt = dockPos + new Vector3(5f, 0, 2f);
        Mining.ShardManager.Instance?.SpawnShards(GameData.Mat("amethyst"), L0.OnGround(shardAt.X, shardAt.Z, 0.6f), 5);
        p.GlobalPosition = L0.OnGround(PathAt(0, 0.9f).X, PathAt(0, 0.9f).Z);
        await Wait(1.6f);
        if (bm.Drone != null)
        {
            var dp = bm.Drone.GlobalPosition;
            var away = (hp - dockPos); away.Y = 0;
            var camFrom = dp + (away.LengthSquared() > 0.01f ? away.Normalized() : Vector3.Right) * 3.5f;
            LookAtPoint(p, new Vector3(camFrom.X, L0.Ground(camFrom.X, camFrom.Z) + 0.1f, camFrom.Z), dp);
        }
        await Wait(0.15f);
        await Shot("18d_drone");
        // robô minerador perto de um depósito
        var crystal = Main.World.Crystals.Where(c => c.IsMineable && !c.Material.Special && WorldBuilder.RegionAt(c.GlobalPosition).Index == 0 && c.GlobalTransform.Basis.Y.Normalized().Y > 0.9f)
                                         .OrderBy(c => c.StandPoint.DistanceTo(PathAt(0, 0.4f))).FirstOrDefault();
        if (crystal != null)
        {
            var rp = Spot(Data.BuildIds.Robot, crystal.StandPoint + (crystal.StandPoint - crystal.GlobalPosition).Normalized() * 3f);
            var rb = bm.Place(Data.BuildIds.Robot, rp, 0);
            await Wait(6f);
            if (rb != null && bm.NodeOf(rb.Id) is Dock rd && rd.Unit != null)
            {
                var u = rd.Unit.GlobalPosition;
                var toC = (crystal.GlobalPosition - u); toC.Y = 0;
                var side = toC.Normalized().Cross(Vector3.Up);
                LookAtPoint(p, u - toC.Normalized() * 2.2f + side * 1.6f + Vector3.Up * 0.1f, u + Vector3.Up * 0.4f);
                await Wait(0.4f);
                await Shot("18g_robot");
                bm.SetActive(true);
                LookAtPoint(p, rp + new Vector3(0, 0.1f, 0) - toC.Normalized() * 6f, rp);
                await Wait(0.6f);
                await Shot("18h_robot_area");
                bm.SetActive(false);
            }
        }
        // modo construção: marcando a esteira (prévia do trajeto até a venda)
        var startB = Spot(Data.BuildIds.Belt, PathAt(0, 0.3f));
        LookAtPoint(p, startB + Vector3.Up * 0.1f + (st - startB).Normalized() * -2f, startB);
        bm.SetActive(true);
        bm.SelectId(Data.BuildIds.Belt);
        await Wait(0.4f);
        await Shot("18e_build_mode");
        await Wait(0.05f); // o aperto precisa cair no começo de um quadro
        Input.ActionPress("mine");
        await Wait(0.2f);
        LookAtPoint(p, st + (startB - st).Normalized() * 5f + Vector3.Up * 0.1f, bm.SellIntake);
        await Wait(1.0f);
        await Shot("18f_build_belt_drag");
        bm.SetActive(false);
        Input.ActionRelease("mine");
        await Wait(0.2f);
        // árvore de habilidades com o ramo Construção
        gs.Data.Skills.Remove(Data.SkillIds.BuildRobot);
        gs.Data.Skills.Remove(Data.SkillIds.BuildDrone);
        UiBus.RequestHomePanel();
        await Wait(0.6f);
        var homeS = Main.GetChildren().OfType<UI.HomeScreen>().First();
        homeS.ShowSkillsTab();
        homeS.SelectSkill(Data.SkillIds.BuildDrone);
        await Wait(0.4f);
        await Shot("18z_skills_build");
        Main.CloseAllForTour();
        // painel de admin
        Input.ParseInputEvent(new InputEventAction { Action = "debug_panel", Pressed = true });
        await Wait(0.6f);
        await Shot("19_admin");
        Input.ParseInputEvent(new InputEventAction { Action = "debug_panel", Pressed = true });
        await Wait(0.3f);
    }

    /// <summary>Regiões de fim de jogo: vistas do caminho, das câmaras com minério em destaque e do goblin.</summary>
    async Task NewRegionsTour(Player.PlayerController p, GameState gs)
    {
        foreach (var r in GameData.Regions) if (!gs.IsUnlocked(r.Id)) gs.Data.UnlockedRegions.Add(r.Id);
        gs.Data.DayTimeSeconds = 30;
        for (int ri = GameData.JourneyRegions; ri < GameData.Regions.Count; ri++)
        {
            var L = Main.World.Layouts[ri];
            string id = GameData.Regions[ri].Id;
            foreach (var t in new[] { 0.2f, 0.45f, 0.75f })
            {
                LookAtPoint(p, PathAt(ri, t) + Vector3.Up * 0.1f, PathAt(ri, t + 0.08f) + Vector3.Up * 2.5f);
                await Wait(1.0f);
                await Shot($"20_{id}_{t * 100:0}");
            }
            foreach (var c in L.Chambers)
            {
                if (c.Wing == null) continue;
                var from = L.OnGround(c.A.X + (c.B.X - c.A.X) * 0.1f, c.A.Y);
                var toward = L.Inward(from.X, from.Z);
                LookAtPoint(p, from + Vector3.Up * 0.1f - toward * 2f, from + toward * 8f + Vector3.Up * 2f);
                await Wait(0.8f);
                await Shot($"21_{id}_{c.Wing.Id}");
            }
            var camp = Main.World.GoblinCamps.FirstOrDefault(gc => gc.RegionIndex == ri);
            if (camp != null)
            {
                LookAtPoint(p, camp.HideoutPos + new Vector3(3f, 0.1f, 0), camp.HideoutPos + Vector3.Up * 0.8f);
                await Wait(0.8f);
                await Shot($"22_{id}_goblin");
            }
        }
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
        var root = new Node3D { Position = new Vector3(0, 0, 125) };
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
            p.GlobalPosition = new Vector3(0, 0.1f, 131.5f);
            p.Velocity = Vector3.Zero;
            p.SetLook(0, -0.22f);
            await Wait(0.8f);
            await Shot($"16_ores_{g}");
        }
        // as 6 ferramentas em espaço limpo, disparando contra um quartzo
        foreach (Node c in root.GetChildren()) if (c != floor && c != body) c.QueueFree();
        await Wait(0.1f);
        var tq = new Mining.Crystal();
        tq.Init(GameData.Mat("abyssaltear"), 7, 1.2f); // resistente: não quebra durante as fotos
        root.AddChild(tq);
        tq.Position = new Vector3(1.2f, 0.02f, 0f);
        var gs = GameState.Instance;
        int[] tierLevels = { 0, 3, 7, 12, 18, 24 };
        for (int t = 0; t < tierLevels.Length; t++)
        {
            gs.Data.Upgrades[UpgradeIds.Damage] = tierLevels[t];
            gs.RaiseAll();
            p.GlobalPosition = new Vector3(0.3f, 0.1f, 127.6f);
            p.Velocity = Vector3.Zero;
            p.SetLook(0.28f, -0.3f);
            await Wait(0.35f);
            await Shot($"17_gun_idle_{t}");
            Input.ActionPress("mine");
            await Wait(0.45f);
            await Shot($"17_gun_{t}");
            Input.ActionRelease("mine");
            await Wait(0.2f);
        }
        gs.Data.Upgrades.Remove(UpgradeIds.Damage);
        gs.RaiseAll();
        root.QueueFree();
    }

    /// <summary>Identidade dos minérios: folha com todos os ícones, fragmentos no chão e a coleção.</summary>
    async Task OresTour(Player.PlayerController p, GameState gs)
    {
        Main.CloseAllForTour();
        gs.Data.DayTimeSeconds = 30;
        var mats = GameData.Materials.Values.Where(m => !m.Special).ToList();
        for (int k = 0; k < 600 && UI.OreIcons.ReadyCount < GameData.Materials.Count; k++) await Wait(0.05f);
        // folha de contato dos ícones (8 por linha)
        int cols = 8, cell = 96;
        var sheet = Image.CreateEmpty(cols * cell, ((mats.Count + cols - 1) / cols) * cell, false, Image.Format.Rgba8);
        sheet.Fill(new Color(0.05f, 0.06f, 0.1f));
        for (int i = 0; i < mats.Count; i++)
        {
            var tex = UI.OreIcons.Get(mats[i]);
            if (tex == null) continue;
            var img = tex.GetImage();
            img.Convert(Image.Format.Rgba8);
            sheet.BlendRect(img, new Rect2I(0, 0, img.GetWidth(), img.GetHeight()), new Vector2I((i % cols) * cell, (i / cols) * cell));
        }
        sheet.SavePng($"{Dir}/40_ore_icons.png");
        GD.Print($"[Tour] ícones prontos: {UI.OreIcons.ReadyCount}/{GameData.Materials.Count}");
        // fragmentos de vários minérios no chão, à frente
        var L0 = Main.World.Layouts[0];
        var at = L0.OnGround(Main.World.SpawnPoint.X, Main.World.SpawnPoint.Z - 6f);
        LookAtPoint(p, at + new Vector3(0, 0.1f, 2.2f), at);
        var pick = new[] { "quartz", "copper", "amethyst", "hematite", "sapphire", "frostice", "ruby", "magmite", "malachite", "opal", "coralprism", "blackpearl" };
        for (int i = 0; i < pick.Length; i++)
            if (GameData.Mat(pick[i]) is { } m) Mining.ShardManager.Instance?.SpawnShards(m, at + new Vector3((i % 4 - 1.5f) * 0.5f, 0.6f, (i / 4 - 1) * 0.5f), 1);
        await Wait(1.6f);
        await Shot("41_ore_drops");
        foreach (var m in GameData.Materials.Values) if (!gs.Data.Discovered.Contains(m.Id)) gs.Data.Discovered.Add(m.Id);
    }

    /// <summary>Ferramentas em primeira pessoa (picaretas, broca, laser, mãos) e a seção do Terminal.</summary>
    async Task ToolsTour(Player.PlayerController p, GameState gs)
    {
        Main.CloseAllForTour();
        foreach (var r in GameData.Regions) if (!gs.IsUnlocked(r.Id)) gs.Data.UnlockedRegions.Add(r.Id);
        gs.Data.DayTimeSeconds = 30;
        var c = Main.World.Crystals.First(x => x.Material.Id == "quartz" && x.GlobalTransform.Basis.Y.Normalized().Y > 0.95f);
        LookAtPoint(p, c.StandPoint + Vector3.Up * 0.05f, c.Center);
        foreach (int ti in new[] { 0, 1, 3, 5, 7, 8, 10 })
        {
            gs.DebugSetTool(ti);
            await Wait(0.5f);
            await Shot($"30_tool_{ti}_{ToolData.All[ti].Id}");
            // fases do golpe congeladas: antecipação, impacto e volta
            if (ti <= 7)
                foreach (var (ph, name) in new[] { (0.22f, "a_windup"), (0.42f, "b_impact"), (0.6f, "c_recover") })
                {
                    p.Tool.DebugPhase = ph;
                    await Wait(0.25f);
                    await Shot($"30_tool_{ti}_{name}");
                }
            p.Tool.DebugPhase = -1f;
            await Wait(0.3f);
        }
        gs.DebugSetTool(2, true);
        await Wait(0.5f);
        await Shot("31_hands_broken");
        gs.AddMoney(5000);
        gs.Data.UnlockedRegions.RemoveAll(r => r != "caves" && r != "depths");
        UiBus.RequestShop();
        await Wait(0.8f);
        await Shot("32_terminal_tools");
        Main.CloseAllForTour();
        // árvore de habilidades: alguns aprendidos, outros disponíveis e bloqueados
        gs.AddMoney(2_000_000);
        foreach (var id in new[] { "magnet", "direct_collect", "early_bird", "firm_strike", "cadence", "haggle", Data.SkillIds.ToolGrip, Data.SkillIds.Temper, Data.SkillIds.BrightLamp, Data.SkillIds.BuildBelt })
            if (!gs.HasSkill(id)) gs.Data.Skills.Add(id);
        UiBus.RequestHomePanel();
        await Wait(0.6f);
        var home = Main.GetChildren().OfType<UI.HomeScreen>().First();
        await Shot("33a_home_upgrades");
        home.ShowSkillsTab();
        home.SelectSkill(Data.SkillIds.Hone);
        await Wait(0.5f);
        await Shot("33_skill_tree");
        Main.CloseAllForTour();
    }

    static void LookAtPoint(Player.PlayerController p, Vector3 from, Vector3 target)
    {
        p.GlobalPosition = from;
        p.Velocity = Vector3.Zero;
        var dir = target - (from + Vector3.Up * 1.55f);
        p.SetLook(Mathf.Atan2(-dir.X, -dir.Z), Mathf.Atan2(dir.Y, new Vector2(dir.X, dir.Z).Length()));
    }

    /// <summary>Ponto (no chão) a uma fração do caminho principal da região.</summary>
    Vector3 PathAt(int region, float t)
    {
        var L = Main.World.Layouts[region];
        var pts = L.PathPoints;
        float total = 0;
        for (int i = 1; i < pts.Count; i++) total += pts[i].DistanceTo(pts[i - 1]);
        float want = t * total;
        for (int i = 1; i < pts.Count; i++)
        {
            float seg = pts[i].DistanceTo(pts[i - 1]);
            if (want <= seg) { var q = pts[i - 1].Lerp(pts[i], want / seg); return L.OnGround(q.X, q.Z, 0.1f); }
            want -= seg;
        }
        return L.OnGround(pts[^1].X, pts[^1].Z, 0.1f);
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
