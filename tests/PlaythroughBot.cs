using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using CrystalExcavator.UI;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Tests;

/// <summary>
/// Bot de QA que joga o jogo real usando as mesmas entradas e telas do jogador:
/// mira, segura o botão de mineração, interage (E) com estações e portões
/// e pressiona os botões das telas de venda e loja.
/// Teleporta entre alvos (não testa navegação), portanto o tempo é otimista.
///   godot --headless --fixed-fps 60 --path . -- --bot[=minutos]
/// </summary>
public partial class PlaythroughBot : Node
{
    public Main Main;
    public float MaxGameMinutes = 90;

    PlayerController P => Main.Player;
    GameState GS => GameState.Instance;
    readonly List<string> _log = new();
    int _errors;
    double _r2 = -1, _r3 = -1, _r4 = -1, _heart = -1;
    int _sells, _buys, _pulses;

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SaveSystem.SavePath = "user://bot_save.json";
        SaveSystem.Delete();
        await Frames(10);
        Main.StartGame(false);
        P.SimulateCapturedMouse = true;
        P.ResonancePulse += _ => _pulses++;
        await Frames(10);
        Log("Início do jogo");
        await Frames(60);
        double nodes0 = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        double objs0 = Performance.GetMonitor(Performance.Monitor.ObjectCount);
        double mem0 = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0;

        try
        {
            while (GameMinutes < MaxGameMinutes)
            {
                if (GS.InventoryCount >= GS.Capacity - 2)
                {
                    await SellTrip();
                    await SpendTrip();
                    if (_r3 > 0 && _heart > 0 && _r4 > 0) break;
                    continue;
                }
                await MineOne();
            }
        }
        catch (Exception e)
        {
            _errors++;
            Log("EXCEÇÃO: " + e);
        }

        // coleta o lixo gerenciado antes de medir (separa vazamento real de coleta atrasada)
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        await Frames(5);
        System.GC.Collect();
        await Frames(5);
        double nodes1 = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
        double objs1 = Performance.GetMonitor(Performance.Monitor.ObjectCount);
        double mem1 = Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0;
        double orphans = Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
        Log($"Estabilidade: nós {nodes0:0} → {nodes1:0}, objetos {objs0:0} → {objs1:0}, memória {mem0:0.0} → {mem1:0.0} MB, nós órfãos {orphans:0}");
        if (nodes1 > nodes0 * 1.25 + 200) { _errors++; Log("ERRO: crescimento anormal de nós (possível vazamento)"); }
        if (objs1 > objs0 * 2 + 3000) { _errors++; Log("ERRO: crescimento anormal de objetos após GC (possível vazamento)"); }

        // caminhada real do acampamento até o Núcleo (colisões, túneis e portões abertos)
        if (_r3 > 0) await WalkTest();

        // verificação final de salvamento/carregamento do estado real
        GS.SaveGame(true);
        long money = GS.Data.Money;
        int lvl = GS.Data.Upgrades.Values.Sum();
        GS.NewGame();
        GS.LoadGame();
        bool saveOk = GS.Data.Money == money && GS.Data.Upgrades.Values.Sum() == lvl;

        string Fmt(double m) => m < 0 ? "não alcançado" : $"{m:0.0} min";
        GD.Print("===== BOT DE JOGO — RESUMO =====");
        foreach (var l in _log) GD.Print(l);
        GD.Print($"Tempo de jogo: {GameMinutes:0.0} min | Região 2: {Fmt(_r2)} | Região 3: {Fmt(_r3)} | Coração quebrado: {Fmt(_heart)} | Reservatório: {Fmt(_r4)}");
        GD.Print($"Vendas: {_sells} | Compras: {_buys} | Pulsos: {_pulses} | Cristais: {GS.Data.CrystalsMined} | Total ganho: {GS.Data.TotalEarned}");
        GD.Print($"Upgrades: {string.Join(", ", UpgradeIds.All.Select(id => $"{id}={GS.Data.Level(id)}"))}");
        GD.Print($"Save/Load após a partida: {(saveOk ? "OK" : "FALHOU")}");
        bool ok = _errors == 0 && _r2 > 0 && _r3 > 0 && _r4 > 0 && _sells > 0 && _buys > 0 && saveOk && _pulses > 0;
        GD.Print(ok ? "BOT: SUCESSO" : $"BOT: FALHA (erros={_errors})");
        SaveSystem.Delete();
        GameState.Instance.QuitClean(ok ? 0 : 1);
    }

    double GameMinutes => GS.Data.PlayTimeSeconds / 60.0;

    void Log(string msg) => _log.Add($"[{GameMinutes,6:0.0} min] {msg}");

    RegionDef BestRegion() => GameData.Regions.Last(r => GS.IsUnlocked(r.Id));

    Node3D RegionRoot(RegionDef r) => Main.World.RegionRoots[r.Index];

    // ---------------- Mineração ----------------
    async Task MineOne()
    {
        var region = BestRegion();
        var root = RegionRoot(region);
        // prefere o Coração quando o dano permite quebrá-lo em tempo razoável
        Crystal target = null;
        var heart = Main.World.Crystals.FirstOrDefault(c => c.Material.Special && c.IsMineable);
        if (heart != null && GS.IsUnlocked("core") && heart.Hp / GS.DamagePerSecond < 45) target = heart;
        target ??= Main.World.Crystals
            .Where(c => c.IsMineable && !c.Material.Special && c.GetParent() == root)
            .OrderBy(c => c.GlobalPosition.DistanceTo(P.GlobalPosition))
            .FirstOrDefault();
        if (target == null) { await Frames(30); return; }

        float back = target.Material.Special ? 4.2f : 1.7f;
        var tp = target.GlobalPosition;
        var dirFlat = new Vector3(P.GlobalPosition.X - tp.X, 0, P.GlobalPosition.Z - tp.Z);
        if (dirFlat.LengthSquared() < 0.01f) dirFlat = Vector3.Back;
        P.GlobalPosition = tp + dirFlat.Normalized() * back + Vector3.Up * 0.05f;
        P.Velocity = Vector3.Zero;
        await Frames(2);
        AimAt(tp + Vector3.Up * (target.Material.Special ? 2.0f : 0.6f * target.SizeScale));
        await Frames(2);
        if (P.TargetCrystal != target)
        {
            // tenta mirar mais baixo
            AimAt(tp + Vector3.Up * 0.35f);
            await Frames(2);
            if (P.TargetCrystal != target) { Log($"Não conseguiu mirar {target.Material.Name}"); await Frames(5); return; }
        }
        bool wasHeart = target.Material.Special;
        Input.ActionPress("mine");
        int guard = 0;
        while (target.IsMineable && guard++ < 60 * 60) await Frames(1);
        Input.ActionRelease("mine");
        if (!target.IsMineable && wasHeart && _heart < 0)
        {
            _heart = GameMinutes;
            Log("Coração Ancestral destruído!");
        }
        await Frames(50); // fragmentos chegam pelo ímã
    }

    void AimAt(Vector3 point)
    {
        var cam = P.Camera.GlobalPosition;
        var d = (point - cam).Normalized();
        float yaw = Mathf.Atan2(-d.X, -d.Z);
        float pitch = Mathf.Asin(Mathf.Clamp(d.Y, -1, 1));
        P.SetLook(yaw, pitch);
    }

    async Task WalkTest()
    {
        P.GlobalPosition = Main.World.SpawnPoint;
        P.Velocity = Vector3.Zero;
        P.SetLook(0, 0);
        await Frames(5);
        double t0 = GS.Data.PlayTimeSeconds;
        // pelo corredor central até o Núcleo; se o Reservatório estiver aberto, desvia do pedestal e segue até ele
        float targetZ = GameData.Regions[2].CenterZ + 12f;
        Input.ActionPress("move_forward");
        Input.ActionPress("sprint");
        int guard = 0;
        float lastZ = P.GlobalPosition.Z; int stuck = 0;
        while (P.GlobalPosition.Z > targetZ && guard++ < 60 * 120)
        {
            await Frames(10);
            if (lastZ - P.GlobalPosition.Z < 0.05f) { if (++stuck > 12) break; } else stuck = 0;
            lastZ = P.GlobalPosition.Z;
        }
        Input.ActionRelease("move_forward");
        Input.ActionRelease("sprint");
        if (P.GlobalPosition.Z <= targetZ && GS.IsUnlocked("abyss"))
        {
            // contorna o anel de pilares do Coração (raio ~6 m) pela lateral esquerda
            float heartZ = GameData.Regions[2].CenterZ - 4f;
            async Task WalkUntil(float yaw, Func<bool> done)
            {
                P.SetLook(yaw, 0);
                Input.ActionPress("move_forward");
                for (int i = 0; i < 600 && !done(); i++) await Frames(1);
                Input.ActionRelease("move_forward");
            }
            await WalkUntil(Mathf.Pi / 2, () => P.GlobalPosition.X < -8.5f);          // esquerda (-X)
            float sideX = P.GlobalPosition.X;
            await WalkUntil(0, () => P.GlobalPosition.Z < heartZ - 8.5f);             // frente (-Z)
            await WalkUntil(-Mathf.Pi / 2, () => P.GlobalPosition.X > -0.3f);         // volta ao centro (+X)
            P.SetLook(0, 0);
            targetZ = GameData.Regions[3].CenterZ + 10f;
            Input.ActionPress("move_forward");
            Input.ActionPress("sprint");
            guard = 0; stuck = 0; lastZ = P.GlobalPosition.Z;
            while (P.GlobalPosition.Z > targetZ && guard++ < 60 * 120)
            {
                await Frames(10);
                if (lastZ - P.GlobalPosition.Z < 0.05f) { if (++stuck > 12) break; } else stuck = 0;
                lastZ = P.GlobalPosition.Z;
            }
            Input.ActionRelease("move_forward");
            Input.ActionRelease("sprint");
            Log($"Desvio do pedestal (x={sideX:0.0}) e caminhada até o Reservatório");
        }
        double secs = GS.Data.PlayTimeSeconds - t0;
        bool ok = P.GlobalPosition.Z <= targetZ;
        if (!ok) { _errors++; Log($"ERRO: caminhada travou em {P.GlobalPosition}"); }
        else Log($"Caminhada acampamento → destino: {secs:0.0} s, sem travar (z final {P.GlobalPosition.Z:0.0})");
    }

    // ---------------- Estações ----------------
    async Task<bool> UseStation<T>(RegionDef region) where T : StationBase
    {
        var station = RegionRoot(region).GetChildren().OfType<T>().First();
        var front = station.GlobalTransform.Basis.Z.Normalized();
        if (station is SellStation) front = (new Vector3(0, 0, region.CenterZ) - station.GlobalPosition).Normalized();
        P.GlobalPosition = station.GlobalPosition + new Vector3(front.X, 0, front.Z).Normalized() * 2.2f + Vector3.Up * 0.05f;
        P.Velocity = Vector3.Zero;
        await Frames(2);
        AimAt(station.GlobalPosition + Vector3.Up * 0.9f);
        await Frames(3);
        if (P.TargetInteractable is not T) { _errors++; Log($"ERRO: não mirou {typeof(T).Name} (alvo: {P.TargetInteractable})"); return false; }
        PressAction("interact");
        await Frames(3);
        return true;
    }

    async Task SellTrip()
    {
        var region = BestRegion();
        if (!await UseStation<SellStation>(region)) return;
        var sell = Main.GetChildren().OfType<SellScreen>().First();
        if (!sell.IsOpen) { _errors++; Log("ERRO: tela de venda não abriu"); return; }
        long before = GS.Data.Money;
        var btn = FindButtons(sell).First(b => b.Text == "VENDER TUDO");
        btn.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        long gained = GS.Data.Money - before;
        if (gained <= 0) { _errors++; Log("ERRO: venda não rendeu"); }
        _sells++;
        // troca de aba Vender → Melhorias e volta
        var shopTab = FindButtons(sell).First(b => b.Text.Contains("Melhorias"));
        shopTab.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var shopScr = Main.GetChildren().OfType<ShopScreen>().First();
        if (!shopScr.IsOpen || sell.IsOpen) { _errors++; Log("ERRO: troca de aba para Melhorias falhou"); }
        PressAction("pause"); // fecha
        await Frames(3);
        if (sell.IsOpen || shopScr.IsOpen) { _errors++; Log("ERRO: Esc não fechou o terminal"); }
        if (_sells % 5 == 1) Log($"Venda #{_sells}: +{gained} (saldo {GS.Data.Money})");
    }

    async Task SpendTrip()
    {
        var next = GS.NextLockedRegion();
        if (next != null && GS.CanUnlock(next))
        {
            var gate = Main.World.Gates.First(g => g.Region == next);
            P.GlobalPosition = gate.GlobalPosition + new Vector3(0, 0.05f, 2.4f);
            P.Velocity = Vector3.Zero;
            await Frames(2);
            AimAt(gate.GlobalPosition + Vector3.Up * 1.6f);
            await Frames(3);
            if (P.TargetInteractable != gate) { _errors++; Log("ERRO: não mirou o portão"); return; }
            PressAction("interact");
            await Frames(5);
            if (!GS.IsUnlocked(next.Id) || !gate.IsOpen) { _errors++; Log("ERRO: portão não abriu"); return; }
            if (next.Index == 1) _r2 = GameMinutes;
            if (next.Index == 2) _r3 = GameMinutes;
            if (next.Index == 3) _r4 = GameMinutes;
            Log($"Desbloqueou {next.Name}");
            // atravessa o portão (testa colisão aberta)
            var start = gate.GlobalPosition + new Vector3(0, 0.05f, 1.5f);
            P.GlobalPosition = start;
            P.SetLook(0, 0);
            Input.ActionPress("move_forward");
            await Frames(90);
            Input.ActionRelease("move_forward");
            if (P.GlobalPosition.Z > gate.GlobalPosition.Z - 1f) { _errors++; Log("ERRO: jogador não atravessou o portão aberto"); }
            return;
        }
        // guarda dinheiro se a próxima região está perto
        if (next != null && GS.Data.Money > next.UnlockCost * 0.6) return;

        var region = BestRegion();
        if (!await UseStation<ShopTerminal>(region)) return;
        var shop = Main.GetChildren().OfType<ShopScreen>().First();
        if (!shop.IsOpen) { _errors++; Log("ERRO: loja não abriu"); return; }
        for (int i = 0; i < 40; i++)
        {
            await Frames(1);
            // botões de upgrade habilitados (texto com preço), do mais barato ao mais caro
            var buttons = FindButtons(shop).Where(b => !b.Disabled && b.Text.StartsWith("◈") && b.GetParent()?.GetParent() is PanelContainer card && card.GetParent() is GridContainer)
                .Select(b => (b, cost: ParseCost(b.Text))).OrderBy(x => x.cost).ToList();
            if (buttons.Count == 0) break;
            var (btn, cost) = buttons[0];
            if (next != null && GS.Data.Money - cost < next.UnlockCost * 0.25 && GS.Data.Money > next.UnlockCost * 0.45) break;
            int lv = GS.Data.Upgrades.Values.Sum();
            btn.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            if (GS.Data.Upgrades.Values.Sum() != lv + 1) { _errors++; Log("ERRO: compra não aplicou"); break; }
            _buys++;
        }
        PressAction("pause");
        await Frames(3);
        if (shop.IsOpen) { _errors++; Log("ERRO: Esc não fechou a loja"); }
        if (_buys > 0 && _buys % 10 == 0) Log($"{_buys} compras; dano {GS.Damage:0}, {GS.SwingsPerSecond:0.0}/s, mochila {GS.Capacity}");
    }

    static long ParseCost(string text)
    {
        var digits = new string(text.Where(char.IsDigit).ToArray());
        double mult = text.EndsWith("K") ? 1e3 : text.EndsWith("M") ? 1e6 : text.EndsWith("B") ? 1e9 : 1;
        return long.TryParse(digits, out long v) ? (long)(v * mult) : long.MaxValue;
    }

    static IEnumerable<Button> FindButtons(Node root)
    {
        foreach (Node c in root.GetChildren())
        {
            if (c is Button b) yield return b;
            foreach (var x in FindButtons(c)) yield return x;
        }
    }

    static void PressAction(string action)
    {
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = true });
        Input.ParseInputEvent(new InputEventAction { Action = action, Pressed = false });
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
