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
    int _sells, _buys, _pulses, _missedAims;
    bool _usedLaser;
    int _hopperId, _hopperTrips;
    int _toolBuys, _breaks;
    long _beltSold;
    bool _droneBought;
    readonly HashSet<Crystal> _unaimable = new();

    public override async void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        SaveSystem.SavePath = "user://bot_save.json";
        // o bot testa se a jornada inteira FUNCIONA (o ritmo é medido na simulação de balanceamento):
        // vendas valem mais aqui para caber nos minutos do teste
        Economy.SellValue = 0.75f * 3.6f; // compensa a vida x1,6 dos minérios (só o bot, para cobrir a jornada no tempo do teste)
        SaveSystem.Delete();
        await Frames(10);
        Main.StartGame(false);
        P.SimulateCapturedMouse = true;
        P.ResonancePulse += _ =>
        {
            _pulses++;
            if (!GS.ResonanceActive) { _errors++; Log("ERRO: pulso ressonante sem ferramenta a laser"); }
        };
        GS.BeltSold += (v, _) => _beltSold += v;
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
                await DismissCompletion();
                if (GS.DayOver) { await EndOfDay(); continue; }
                // uma volta antecipada pelo elevador (com confirmação) no 2º dia
                if (_days == 1 && _earlyHome == 0 && GS.Data.DayTimeSeconds > 120) { if (GS.InventoryCount > 0) await SellTrip(); await GoHomeAndSleep(); continue; }
                if (GS.InventoryCount >= GS.Capacity - 2 || (GS.ToolBroken && GS.CanWork))
                {
                    if (GS.ToolBroken) _breaks++;
                    if (GS.InventoryCount > 0) await SellTrip();
                    if (GS.CanWork) await SpendTrip();
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

        await Frames(120);
        await DismissCompletion();
        if (!GS.Data.GameCompleted)
        {
            _errors++;
            Log($"ERRO: jornada não foi marcada como concluída (coração descoberto={GS.Data.Discovered.Contains("heart")}, regiões={string.Join(",", GS.Data.UnlockedRegions)}, " +
                $"coração soltos={Mining.ShardManager.Instance?.Loose.Count(s => s.Mat.Id == "heart") ?? -1}, coração vivo={Main.World.Crystals.Any(c => c.Material.Id == "heart" && c.IsMineable)})");
        }

        // caminhada real do acampamento até o Núcleo (colisões, túneis e portões abertos)
        if (_r3 > 0) await WalkTest();

        // ---------- Renascimento pela interface e 2ª jornada ----------
        _firstR2 = _r2;
        await PrestigeViaUi();
        _r2 = _firstR2;

        await AdminPanelTest();

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
        GD.Print($"Vendas: {_sells} | Compras: {_buys} | Pulsos: {_pulses} (usou laser: {_usedLaser}) | Cristais: {GS.Data.CrystalsMined} | Total ganho: {GS.Data.TotalEarned}");
        GD.Print($"Renascimento: {(_prestiged ? "sim" : "não")} | Região 2 na 2ª jornada: {(_secondR2 > 0 ? $"{_secondR2:0.0} min" : "não alcançada")}");
        GD.Print($"Upgrades: {string.Join(", ", UpgradeIds.All.Select(id => $"{id}={GS.Data.Level(id)}"))}");
        GD.Print($"Save/Load após a partida: {(saveOk ? "OK" : "FALHOU")}");
        GD.Print($"Goblin: contratado={_hired} | taxas pagas {_feesPaid}, não pagas {_feesSkipped} | fragmentos do baú {_stashTaken}");
        GD.Print($"Goblin: melhorias compradas {_goblinUps} | cabana pronta={GS.GoblinBuilt("caves")}");
        // a cabana leva 1,5 dia: em partidas curtas pode haver poucas cobranças (e às vezes sem dinheiro para pagar)
        if (!_hired || (_feesPaid + _feesSkipped) < 2 || _stashTaken == 0 || _goblinUps == 0) { _errors++; GD.Print("ERRO: ciclo do goblin incompleto"); }
        GD.Print($"Construções: esteira={(_hopperId != 0 || _beltSold > 0)} | vendido pela esteira ◈ {_beltSold} | drone={_droneBought}");
        if (_beltSold <= 0) { _errors++; GD.Print("ERRO: a esteira não vendeu nada"); }
        if (!_droneBought) { _errors++; GD.Print("ERRO: drone não construído"); }
        GD.Print($"Ferramentas: {GS.CurrentTool.NamePt} | compradas {_toolBuys} | voltas por quebra {_breaks}");
        if (_toolBuys < 3) { _errors++; GD.Print("ERRO: o bot quase não comprou ferramentas"); }
        GD.Print($"Miras perdidas: {_missedAims}");
        if (_missedAims > 5) { _errors++; GD.Print("ERRO: muitos depósitos impossíveis de mirar"); }
        GD.Print($"Dias completos: {_days} (voltas antecipadas: {_earlyHome}) | Casa: {_houseBuys} compras | Habilidades: {_skillBuys} | Encomendas entregues: {_contracts} | Forja: {_forged} peças, {_repairs} consertos");
        bool ok = _days >= 2 && _houseBuys > 0 && _skillBuys > 0 && _errors == 0 && _r2 > 0 && _r3 > 0 && _r4 > 0 && _sawCompletion && _prestiged && _secondR2 > 0 && _sells > 0 && _buys > 0 && saveOk && (_pulses > 0 || !_usedLaser);
        GD.Print(ok ? "BOT: SUCESSO" : $"BOT: FALHA (erros={_errors})");
        SaveSystem.Delete();
        GameState.Instance.QuitClean(ok ? 0 : 1);
    }

    double GameMinutes => GS.Data.PlayTimeSeconds / 60.0;

    void Log(string msg) { var l = $"[{GameMinutes,6:0.0} min] {msg}"; _log.Add(l); GD.Print("BOT> " + l); }

    RegionDef BestRegion() => GameData.Regions.Last(r => GS.IsUnlocked(r.Id));

    Node3D RegionRoot(RegionDef r) => Main.World.RegionRoots[r.Index];

    // ---------------- Mineração ----------------
    async Task MineOne()
    {
        var region = BestRegion();
        var root = RegionRoot(region);
        // prefere o Coração quando o dano permite quebrá-lo em tempo razoável
        Crystal target = null;
        var heart = Main.World.Crystals.FirstOrDefault(c => c.Material.Id == "heart" && c.IsMineable);
        if (heart != null && GS.IsUnlocked("core") && heart.Hp / GS.DamagePerSecond < 120) target = heart; // Coração com vida x1,6: a broca de titânio já serve
        target ??= Main.World.Crystals
            .Where(c => c.IsMineable && !c.Material.Special && c.GetParent() == root && !_unaimable.Contains(c))
            .OrderBy(c => c.GlobalPosition.DistanceTo(P.GlobalPosition))
            .FirstOrDefault();
        if (target == null) { await Frames(30); return; }

        P.GlobalPosition = target.StandPoint + Vector3.Up * 0.05f;
        P.Velocity = Vector3.Zero;
        await Frames(2);
        AimAt(target.Center);
        await Frames(2);
        if (P.TargetCrystal != target)
        {
            // tenta mirar mais baixo (perto da base)
            AimAt(target.GlobalPosition + target.GlobalTransform.Basis.Y.Normalized() * 0.35f);
            await Frames(2);
            if (P.TargetCrystal != target) { Log($"Não conseguiu mirar {target.Material.Name}"); _missedAims++; _unaimable.Add(target); await Frames(5); return; }
        }
        bool wasHeart = target.Material.Id == "heart";
        if (GS.IsLaser) _usedLaser = true;
        // sem Mineração contínua o jogador clica a cada golpe (o bot clica ~6 vezes por segundo)
        Input.ActionPress("mine");
        int guard = 0;
        while (target.IsMineable && GS.CanWork && guard++ < 60 * 60)
        {
            await Frames(1);
            if (!GS.HoldToMine && guard % 10 == 0) { Input.ActionRelease("mine"); await Frames(1); Input.ActionPress("mine"); }
        }
        Input.ActionRelease("mine");
        if (!target.IsMineable && wasHeart && _heart < 0)
        {
            _heart = GameMinutes;
            Log("Coração Ancestral destruído!");
        }
        await Frames(50); // fragmentos chegam pelo ímã
    }

    bool _prestiged;
    double _firstR2 = -1;
    double _secondR2 = -1;

    async Task PrestigeViaUi()
    {
        if (!GS.CanPrestige) { _errors++; Log($"ERRO: Renascimento indisponível após concluir (pendente {GS.PendingEssence})"); return; }
        int pending = GS.PendingEssence;
        if (GS.DayOver) await EndOfDay();
        if (!await UseStation<ShopTerminal>(GameData.Regions[0])) return;
        var shop = Main.GetChildren().OfType<ShopScreen>().First();
        var btn = FindButtons(shop).FirstOrDefault(b => b.Text.StartsWith("Renascer"));
        if (btn == null || btn.Disabled) { _errors++; Log("ERRO: botão Renascer ausente/desabilitado"); return; }
        btn.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(3);
        var confirm = Main.GetChildren().OfType<ConfirmScreen>().First();
        var yes = FindButtons(confirm).First(b => b.Text == "Confirmar");
        yes.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(10);
        if (GS.Data.PrestigeCount != 1 || GS.Data.Essence != pending || GS.IsUnlocked("depths") || shop.IsOpen)
        { _errors++; Log("ERRO: Renascimento não aplicou corretamente"); return; }
        if (Main.World.Gates[0].IsOpen) { _errors++; Log("ERRO: portão não voltou a fechar após renascer"); }
        _prestiged = true;
        Log($"Renasceu pela interface: +{pending} Essência (bônus x{GS.EssenceMultiplier:0.0})");
        // 2ª jornada até a Região 2
        double t0 = GameMinutes;
        _r2 = -1;
        int guard = 0;
        while (!GS.IsUnlocked("depths") && GameMinutes - t0 < 30 && guard++ < 5000)
        {
            await DismissCompletion();
            if (GS.DayOver) await EndOfDay();
            else if (GS.InventoryCount >= GS.Capacity - 2 || (GS.ToolBroken && GS.CanWork)) { if (GS.InventoryCount > 0) await SellTrip(); if (GS.CanWork) await SpendTrip(); }
            else await MineOne();
        }
        _secondR2 = GS.IsUnlocked("depths") ? GameMinutes - t0 : -1;
        Log($"2ª jornada: Região 2 em {_secondR2:0.0} min");
    }

    bool _sawCompletion;

    async Task DismissCompletion()
    {
        var c = Main.GetChildren().OfType<CompletionScreen>().FirstOrDefault();
        if (c == null || !c.IsOpen) return;
        if (!_sawCompletion) { _sawCompletion = true; Log("Tela de conclusão exibida (Mestre Escavador)"); }
        PressAction("pause");
        await Frames(3);
        if (c.IsOpen) { _errors++; Log("ERRO: Esc não fechou a tela de conclusão"); }
    }

    void AimAt(Vector3 point)
    {
        var cam = P.Camera.GlobalPosition;
        var d = (point - cam).Normalized();
        float yaw = Mathf.Atan2(-d.X, -d.Z);
        float pitch = Mathf.Asin(Mathf.Clamp(d.Y, -1, 1));
        P.SetLook(yaw, pitch);
    }

    /// <summary>
    /// Caminhada real (física) do elevador até o Reservatório seguindo o caminho principal das cavernas:
    /// rampas, relevo, túneis com portões e o contorno da Câmara do Coração.
    /// </summary>
    async Task WalkTest()
    {
        var W = Main.World;
        var pts = new List<Vector3>();
        int last = GS.IsUnlocked("abyss") ? 3 : 2;
        for (int r = 0; r <= last; r++)
        {
            var L = W.Layouts[r];
            var path = L.PathPoints;
            int n = r == last ? Math.Min(3, path.Count) : path.Count;
            for (int i = 0; i < n; i++)
            {
                var q = path[i];
                if (L.Heart != null && new Vector2(q.X - L.Heart.A.X, q.Z - L.Heart.A.Y).Length() < 2f)
                {
                    // contorna o pedestal do Coração pela esquerda
                    foreach (float ang in new[] { 90f, 135f, 180f, 225f, 270f })
                    {
                        float a = Mathf.DegToRad(ang);
                        pts.Add(L.OnGround(L.Heart.A.X + Mathf.Cos(a) * 9.5f, L.Heart.A.Y + Mathf.Sin(a) * 9.5f));
                    }
                    continue;
                }
                pts.Add(L.OnGround(q.X, q.Z));
            }
        }
        P.GlobalPosition = W.SpawnPoint;
        P.Velocity = Vector3.Zero;
        await Frames(5);
        double t0 = GS.Data.PlayTimeSeconds;
        int idx = 0, stuck = 0, sidesteps = 0;
        float best = float.MaxValue;
        Input.ActionPress("sprint");
        for (int guard = 0; idx < pts.Count && guard < 60 * 240; guard++)
        {
            var target = pts[idx];
            var d = new Vector3(target.X - P.GlobalPosition.X, 0, target.Z - P.GlobalPosition.Z);
            float dist = d.Length();
            if (dist < 2.5f) { idx++; best = float.MaxValue; stuck = 0; continue; }
            P.SetLook(Mathf.Atan2(-d.X, -d.Z), 0);
            Input.ActionPress("move_forward");
            await Frames(1);
            if (dist < best - 0.02f) { best = dist; stuck = 0; }
            else if (++stuck > 90)
            {
                // obstáculo (depósito, pedra): desvia para o lado e pula
                sidesteps++;
                string side = sidesteps % 2 == 0 ? "move_left" : "move_right";
                Input.ActionPress(side);
                Input.ActionPress("jump");
                await Frames(25);
                Input.ActionRelease("jump");
                await Frames(15);
                Input.ActionRelease(side);
                stuck = 0; best = float.MaxValue;
                if (sidesteps > 40) break;
            }
        }
        Input.ActionRelease("move_forward");
        Input.ActionRelease("sprint");
        double secs = GS.Data.PlayTimeSeconds - t0;
        if (idx < pts.Count) { _errors++; Log($"ERRO: caminhada travou no ponto {idx}/{pts.Count} em {P.GlobalPosition}"); }
        else Log($"Caminhada real elevador → {GameData.Regions[last].Name}: {secs:0.0} s, {pts.Count} pontos, {sidesteps} desvios");
    }

    // ---------------- Estações ----------------
    async Task<bool> UseStation<T>(RegionDef region) where T : StationBase
    {
        var station = RegionRoot(region).GetChildren().OfType<T>().First();
        var front = station.GlobalTransform.Basis.Z.Normalized();
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

    int _days, _earlyHome;

    /// <summary>Fim do turno: vende o que tiver, pega o elevador, confere a tela de casa e dorme.</summary>
    async Task EndOfDay()
    {
        if (GS.InventoryCount > 0) await SellTrip();
        // fora do turno a mineração não pode funcionar
        var c = Main.World.Crystals.FirstOrDefault(x => x.IsMineable && !x.Material.Special && x.GetParent() == RegionRoot(GameData.Regions[0]));
        if (c != null && _days == 0)
        {
            P.GlobalPosition = c.GlobalPosition + new Vector3(0, 0.05f, 1.7f);
            P.Velocity = Vector3.Zero;
            await Frames(2);
            AimAt(c.GlobalPosition + Vector3.Up * 0.5f);
            float hp = c.Hp;
            Input.ActionPress("mine");
            await Frames(60);
            Input.ActionRelease("mine");
            if (c.Hp < hp) { _errors++; Log("ERRO: minerou depois do fim do turno"); }
        }
        await GoHomeAndSleep();
    }

    bool _checkedNightElevator;
    int _contracts, _forged, _repairs;

    /// <summary>Elevador da mina → casa (andando lá dentro) → painel → cama → elevador de volta à mina.</summary>
    async Task GoHomeAndSleep()
    {
        int day = GS.Data.Day;
        bool wasOver = GS.DayOver;
        if (!await UseStation<MineElevator>(GameData.Regions[0])) { await Frames(30); return; }
        if (!wasOver)
        {
            // antes do fim do turno o elevador pede confirmação
            var confirm = Main.GetChildren().OfType<ConfirmScreen>().First();
            if (!confirm.IsOpen) { _errors++; Log("ERRO: elevador não pediu confirmação antes do fim do turno"); return; }
            FindButtons(confirm).First(b => b.Text == "Confirmar").EmitSignal(BaseButton.SignalName.Pressed);
            _earlyHome++;
        }
        await Frames(3);
        // o goblin cobra a taxa antes de subir: na primeira vez o bot não paga (testa o perdão), depois paga
        var choice = Main.GetChildren().OfType<ChoiceScreen>().First();
        while (GS.DueGoblins().Count > 0)
        {
            string gid = GS.DueGoblins()[0].RegionId;
            if (!choice.IsOpen) { _errors++; Log("ERRO: o goblin não cobrou a taxa ao subir"); return; }
            bool pay = _feesSkipped >= 1;
            int strikes = GS.GoblinData(gid).Strikes;
            var btn = FindButtons(choice).First(b => pay ? b.Text.StartsWith("Pagar") : b.Text == "Não pagar");
            if (pay && btn.Disabled) btn = FindButtons(choice).First(b => b.Text == "Não pagar");
            bool paying = !btn.Text.StartsWith("Não");
            long money = GS.Data.Money, fee = GS.GoblinFee(gid), belt0 = _beltSold;
            btn.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(3);
            money += _beltSold - belt0; // a esteira continua vendendo sozinha nesses quadros
            // depois da cobrança o dia termina e pode entrar o "Bônus do turno" (habilidade)
            if (paying) { _feesPaid++; if (GS.GoblinData(gid).Strikes != 0 || GS.Data.Money != money - fee + GS.Data.DayBonus) { _errors++; Log($"ERRO: pagamento ao goblin não aplicou ({money} - {fee} + {GS.Data.DayBonus} ≠ {GS.Data.Money})"); } }
            else { _feesSkipped++; if (GS.GoblinData(gid).Strikes != strikes + 1) { _errors++; Log("ERRO: falta de pagamento não contou"); } else if (_feesSkipped == 1) Log("Não pagou o Grub (ele perdoou a 1ª vez)"); }
        }
        await Frames(60); // transição
        var house = Main.World.House;
        var town = Main.World.Town;
        if (!Town.Contains(P.GlobalPosition) || PlayerHouse.Contains(P.GlobalPosition) || !GS.DayOver || !P.InputEnabled) { _errors++; Log("ERRO: elevador não levou o jogador para a vila"); return; }
        if (Main.World.RegionRoots.Any(r => r.Visible) || !town.Visible || !house.Visible) { _errors++; Log("ERRO: visibilidade errada na vila"); }
        if (!GS.InTown) { _errors++; Log("ERRO: jogo não sabe que o jogador está na vila"); }

        // anda um pouco pela vila (chão e colisões)
        P.SetLook(Town.ArriveYaw, 0);
        Input.ActionPress("move_forward");
        await Frames(45);
        Input.ActionRelease("move_forward");
        if (!Town.Contains(P.GlobalPosition) || P.GlobalPosition.Y < -1f) { _errors++; Log("ERRO: jogador caiu ou saiu da vila"); return; }

        // de noite o elevador da vila não desce
        if (!_checkedNightElevator)
        {
            _checkedNightElevator = true;
            if (await UseStationNode(town.Elevator))
            {
                await Frames(60);
                if (!Town.Contains(P.GlobalPosition)) { _errors++; Log("ERRO: desceu para a mina sem dormir"); return; }
            }
        }

        // quadro de encomendas: entrega o que der
        GS.EnsureContracts();
        if (await UseStationNode(town.Board))
        {
            var ms = Main.GetChildren().OfType<MissionScreen>().First();
            if (!ms.IsOpen) { _errors++; Log("ERRO: quadro de encomendas não abriu"); return; }
            for (int k = 0; k < GS.Data.Contracts.Count; k++)
            {
                if (!GS.CanDeliver(k)) continue;
                long before = GS.Data.Money, reward = GS.Data.Contracts[k].Reward;
                var btn = FindButtons(ms).FirstOrDefault(b => b.HasMeta("contract") && (int)b.GetMeta("contract") == k);
                btn?.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
                if (!GS.Data.Contracts[k].Done || GS.Data.Money < before + reward) { _errors++; Log("ERRO: entrega da encomenda não pagou"); }
                else { _contracts++; if (_contracts <= 2) Log($"Entregou encomenda: {GS.Data.Contracts[k].Amount}x {GameData.Mat(GS.Data.Contracts[k].Mat).Name} (+{GameState.FormatMoney(reward)})"); }
            }
            PressAction("pause");
            await Frames(3);
            if (ms.IsOpen || !P.InputEnabled) { _errors++; Log("ERRO: Esc não fechou o quadro de encomendas"); return; }
        }

        // forja: forja o que der e conserta a ferramenta gasta
        bool canAny = GearData.Slots.Any(s => GS.CanForge(s)) || (GS.CanRepair && GS.ToolDurability01 < 0.6f);
        if (canAny && await UseStationNode(town.Anvil))
        {
            var fs = Main.GetChildren().OfType<ForgeScreen>().First();
            if (!fs.IsOpen) { _errors++; Log("ERRO: forja não abriu"); return; }
            foreach (var s in GearData.Slots)
            {
                if (!GS.CanForge(s)) continue;
                int tier = GS.GearTier(s);
                var b = FindButtons(fs).FirstOrDefault(x => x.HasMeta("forge") && (int)x.GetMeta("forge") == (int)s);
                b?.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
                if (GS.GearTier(s) != tier + 1) { _errors++; Log("ERRO: forjar pela tela não funcionou"); }
                else { _forged++; Log($"Forjou {GearData.Of(s, tier + 1).NamePt}"); }
            }
            if (GS.CanRepair && GS.ToolDurability01 < 0.6f)
            {
                var b = FindButtons(fs).FirstOrDefault(x => x.HasMeta("repair"));
                b?.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
                if (GS.Data.ToolWear > 0 || GS.ToolBroken) { _errors++; Log("ERRO: conserto na forja não funcionou"); }
                else { _repairs++; if (_repairs <= 2) Log($"Consertou {GS.CurrentTool.NamePt} na forja"); }
            }
            PressAction("pause");
            await Frames(3);
            if (fs.IsOpen || !P.InputEnabled) { _errors++; Log("ERRO: Esc não fechou a forja"); return; }
        }

        // história: quando o Velho Bram chama, conversa, vê o flashback e recebe a missão do portão
        if (GS.Story == StoryData.BramCalls && town.Bram != null && await UseStationNode(town.Bram))
        {
            var dlg = Main.GetChildren().OfType<DialogueScreen>().FirstOrDefault();
            for (int k = 0; k < 400 && GS.Story == StoryData.BramCalls; k++)
            {
                var fb = Main.GetChildren().OfType<IntroComic>().FirstOrDefault(c => c.Name == "Flashback");
                if (fb != null && fb.IsPlaying) fb.Advance();
                else dlg?.Advance();
                await Frames(4);
            }
            await Frames(10);
            if (GS.Story != StoryData.FindGate || !P.InputEnabled) { _errors++; Log("ERRO: conversa com o Velho Bram não avançou a história"); return; }
            Log("Ouviu a história do Velho Bram (missão: portão selado da Catedral)");
        }

        // porta: entra em casa
        if (!await UseStationNode(town.DoorOutside)) { _errors++; Log("ERRO: porta da casa (lado de fora) não funcionou"); return; }
        await Frames(60);
        if (!PlayerHouse.Contains(P.GlobalPosition)) { _errors++; Log("ERRO: a porta não levou para dentro de casa"); return; }

        // anda um pouco dentro de casa (colisões do cômodo)
        P.SetLook(0, 0);
        Input.ActionPress("move_forward");
        await Frames(60);
        Input.ActionRelease("move_forward");
        if (!PlayerHouse.Contains(P.GlobalPosition)) { _errors++; Log("ERRO: jogador atravessou a parede da casa"); return; }

        // painel da casa: melhorias e habilidades
        if (await UseStationNode(house.Panel))
        {
            var home = Main.GetChildren().OfType<HomeScreen>().First();
            if (!home.IsOpen) { _errors++; Log("ERRO: painel da casa não abriu"); return; }
            await BuyAtHome(home);
            PressAction("pause"); // Esc fecha o painel
            await Frames(3);
            if (home.IsOpen || !P.InputEnabled) { _errors++; Log("ERRO: Esc não fechou o painel da casa"); return; }
        }

        // cama
        if (!await UseStationNode(house.Bed)) return;
        await Frames(60);
        if (GS.DayOver || GS.Data.Day != day + 1 || GS.Data.DayTimeSeconds > 2 || !P.InputEnabled || !PlayerHouse.Contains(P.GlobalPosition))
        { _errors++; Log("ERRO: dormir não começou um novo dia corretamente"); return; }

        // sai de casa e pega o elevador da vila → mina
        if (!await UseStationNode(house.DoorInside)) { _errors++; Log("ERRO: porta da casa (lado de dentro) não funcionou"); return; }
        await Frames(60);
        if (!Town.Contains(P.GlobalPosition) || PlayerHouse.Contains(P.GlobalPosition)) { _errors++; Log("ERRO: a porta não levou para a vila"); return; }
        if (!await UseStationNode(town.Elevator)) return;
        await Frames(60);
        if (P.GlobalPosition.DistanceTo(Main.World.SpawnPoint) > 1.5f || !Main.World.RegionRoots[0].Visible || house.Visible || town.Visible)
        { _errors++; Log("ERRO: elevador da vila não levou o jogador à mina"); return; }
        _days++;
        if (_days <= 2 || _days % 3 == 0) Log($"Dia {GS.Data.Day} começou (saldo {GameState.FormatMoney(GS.Data.Money)})");
    }

    async Task<bool> UseStationNode(StationBase station)
    {
        var front = station.GlobalTransform.Basis.Z.Normalized();
        P.GlobalPosition = station.GlobalPosition + new Vector3(front.X, 0, front.Z).Normalized() * 1.8f + Vector3.Up * 0.05f;
        P.Velocity = Vector3.Zero;
        await Frames(2);
        AimAt(station.GlobalPosition + Vector3.Up * 0.7f);
        await Frames(3);
        if (P.TargetInteractable != station) { _errors++; Log($"ERRO: não mirou {station.Name} (alvo: {P.TargetInteractable})"); return false; }
        PressAction("interact");
        await Frames(3);
        return true;
    }

    int _houseBuys, _skillBuys;

    /// <summary>Em casa: compra melhorias da casa e habilidades baratas em relação ao saldo, pelos botões da tela.</summary>
    async Task BuyAtHome(HomeScreen home)
    {
        var next = GS.NextLockedRegion();
        if (next != null && next.Index >= GameData.JourneyRegions) next = null;
        bool Cheap(long cost) => cost <= GS.Data.Money * (next == null ? 0.6 : 0.2);
        home.ShowHouseTab();
        await Frames(2);
        for (int i = 0; i < 8; i++)
        {
            var opts = FindButtons(home).Where(b => !b.Disabled && b.IsVisibleInTree() && (b.Text.StartsWith("◈") || b.Text.StartsWith("Construir")))
                .Select(b => (b, cost: ParseCost(b.Text))).Where(x => Cheap(x.cost)).OrderBy(x => x.cost).ToList();
            if (opts.Count == 0) break;
            int before = GS.Data.House.Values.Sum();
            opts[0].b.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            if (GS.Data.House.Values.Sum() != before + 1) { _errors++; Log("ERRO: compra da casa não aplicou"); break; }
            _houseBuys++;
        }
        home.ShowSkillsTab();
        await Frames(2);
        foreach (var def in HomeData.Skills.OrderBy(s => s.Cost))
        {
            if (!GS.SkillAvailable(def.Id) || !Cheap(def.Cost)) continue;
            home.SelectSkill(def.Id);
            await Frames(2);
            float dmg = GS.Damage;
            var learn = FindButtons(home).FirstOrDefault(b => b.Text.StartsWith("Aprender") && !b.Disabled);
            if (learn == null) { _errors++; Log($"ERRO: botão Aprender indisponível para {def.Name}"); break; }
            learn.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(2);
            if (!GS.HasSkill(def.Id)) { _errors++; Log($"ERRO: habilidade {def.Name} não foi aprendida"); break; }
            if (def.Id == SkillIds.FirmStrike && GS.Damage < dmg * 1.14f) { _errors++; Log("ERRO: Golpe firme não aumentou o dano"); }
            _skillBuys++;
            Log($"Aprendeu {def.Name}");
        }
        home.ShowHouseTab();
    }

    int _feesPaid, _feesSkipped, _stashTaken;
    bool _hired;

    /// <summary>
    /// "Explora" até a câmara secreta onde o Grub se esconde (teleporte até perto dele), confere a descoberta
    /// e o contrata falando com ele (mira e E), quando sobra dinheiro.
    /// </summary>
    async Task TryHireGoblin()
    {
        var camp = Main.World.Goblins;
        if (_hired || GS.Goblin("caves") != GameState.GoblinMood.Available || GS.Data.Money < 1500 || !GS.CanWork) return;
        var g = camp.Grub;
        var L0 = Main.World.Layouts[0];
        Vector3 stand = g.GlobalPosition;
        foreach (var off in new[] { new Vector3(2f, 0, 0), new Vector3(-2f, 0, 0), new Vector3(0, 0, 2f), new Vector3(0, 0, -2f), new Vector3(1.5f, 0, 1.5f), new Vector3(-1.5f, 0, -1.5f) })
        {
            var q = g.GlobalPosition + off;
            if (L0.Reachable(q.X, q.Z) && L0.WallDist(q.X, q.Z) < -0.6f) { stand = q; break; }
        }
        P.GlobalPosition = L0.OnGround(stand.X, stand.Z, 0.1f);
        P.Velocity = Vector3.Zero;
        P.SetCrouch(true); // câmara secreta: pode ter teto baixo na entrada
        await Frames(70);
        if (!GS.GoblinFound("caves")) { _errors++; Log("ERRO: chegar ao esconderijo não descobriu o Grub"); return; }
        Log("Encontrou o Grub escondido numa câmara secreta");
        AimAt(g.GlobalPosition + Vector3.Up * 0.8f);
        await Frames(3);
        if (P.TargetInteractable != g) { _errors++; Log($"ERRO: não mirou o Grub (alvo {P.TargetInteractable})"); return; }
        PressAction("interact");
        await Frames(3);
        P.SetCrouch(false);
        if (GS.Goblin("caves") != GameState.GoblinMood.Working) { _errors++; Log("ERRO: contratar o Grub falhou"); return; }
        _hired = true;
        Log("Contratou o Grub, o goblin minerador");
    }

    int _goblinUps;

    /// <summary>Com a cabana pronta: conversa com o Grub e compra melhorias pela tela dele.</summary>
    async Task UpgradeGoblin()
    {
        var camp = Main.World.Goblins;
        if (!_hired || GS.Goblin("caves") != GameState.GoblinMood.Working || !GS.GoblinBuilt("caves") || !GS.CanWork) return;
        var cheapest = new[] { GameState.GoblinUpgrade.Hut, GameState.GoblinUpgrade.Carry, GameState.GoblinUpgrade.Speed }.Min(u => GS.GoblinUpgradeCost("caves", u));
        if (cheapest == long.MaxValue || GS.Data.Money < cheapest * 6) return;
        var g = camp.Grub;
        P.GlobalPosition = camp.GrubHome + (camp.HutPos - camp.GrubHome).Normalized() * -1.8f + Vector3.Up * 0.1f;
        g.GlobalPosition = camp.GrubHome;
        await Frames(3);
        camp.TalkToGrub();
        await Frames(4);
        var scr = Main.GetChildren().OfType<GoblinScreen>().First();
        if (!scr.IsOpen) { _errors++; Log("ERRO: a tela do goblin não abriu"); return; }
        int lv0 = GS.GoblinData("caves").HutLevel + GS.GoblinData("caves").CarryLevel + GS.GoblinData("caves").SpeedLevel;
        var btn = FindButtons(scr).Where(b => !b.Disabled && b.Text.StartsWith("◈")).OrderBy(b => ParseCost(b.Text)).FirstOrDefault();
        if (btn != null) { btn.EmitSignal(BaseButton.SignalName.Pressed); await Frames(3); }
        int lv1 = GS.GoblinData("caves").HutLevel + GS.GoblinData("caves").CarryLevel + GS.GoblinData("caves").SpeedLevel;
        if (btn != null && lv1 != lv0 + 1) { _errors++; Log("ERRO: melhoria do goblin não aplicou"); }
        else if (btn != null) { _goblinUps++; if (_goblinUps <= 3) Log($"Melhorou o Grub (cabana {GS.GoblinData("caves").HutLevel + 1}, mochila {GS.GoblinData("caves").CarryLevel + 1}, picareta {GS.GoblinData("caves").SpeedLevel + 1})"); }
        PressAction("pause");
        await Frames(3);
        if (scr.IsOpen) { _errors++; Log("ERRO: Esc não fechou a tela do goblin"); }
    }

    /// <summary>Pega o que o Grub guardou no baú.</summary>
    async Task TakeFromChest()
    {
        if (GS.GoblinStashCount("caves") < 6 || GS.FreeSpace < 6) return;
        int before = GS.InventoryCount;
        if (!await UseStationNode(Main.World.Goblins.Chest)) return;
        if (GS.InventoryCount <= before) { _errors++; Log("ERRO: baú do Grub não entregou fragmentos"); return; }
        _stashTaken += GS.InventoryCount - before;
    }

    /// <summary>Painel de admin: abre pelo atalho, usa botões e comandos digitados, confere o efeito e fecha.</summary>
    async Task AdminPanelTest()
    {
        if (!GS.CanWork) { GS.StartNewDay(); await Frames(3); }
        Main.CloseAllForTour();
        await Frames(3);
        PressAction("debug_panel");
        await Frames(3);
        var dbg = Main.GetChildren().OfType<DebugScreen>().First();
        if (!dbg.IsOpen || !GetTree().Paused) { _errors++; Log("ERRO: painel de admin não abriu"); return; }
        long m0 = GS.Data.Money;
        dbg.Press("+◈ 10 mi");
        bool money = GS.Data.Money == m0 + 10_000_000;
        dbg.Press("Ir: Núcleo");
        bool tele = WorldBuilder.RegionAt(P.GlobalPosition).Index == 2;
        dbg.Press("Encher mochila");
        bool full = GS.FreeSpace == 0;
        string r = Main.RunDebug("hora 17");
        bool hour = GS.Data.DayTimeSeconds > GS.DayLength * 0.9;
        Main.RunDebug("fimdia");
        bool ended = GS.DayOver;
        Main.RunDebug("novodia");
        bool unknown = Main.RunDebug("xyz").Contains("desconhecido");
        PressAction("debug_panel");
        await Frames(3);
        bool closed = !dbg.IsOpen && !GetTree().Paused && P.InputEnabled;
        Log($"Painel de admin: dinheiro={money} teleporte={tele} mochila={full} hora={hour} fimdia={ended} erro={unknown} fechou={closed}");
        if (!(money && tele && full && hour && ended && unknown && closed)) { _errors++; Log("ERRO: painel de admin falhou"); }
        GS.Data.Inventory.Clear();
        P.GlobalPosition = Main.World.SpawnPoint;
    }

    /// <summary>Construções: aprende o ramo Construção, entra no modo construção pelos botões, constrói uma esteira até a venda e um drone.</summary>
    async Task TryBuild()
    {
        var bm = Main.Buildings;
        if (_hopperId != 0 && GS.Building(_hopperId) == null) _hopperId = 0; // renascimento
        if (!GS.CanWork) return;
        if (_hopperId == 0 && GS.Data.Money >= 16000)
        {
            if (!GS.HasSkill(SkillIds.BuildBelt) && GS.TryBuySkill(SkillIds.BuildBelt)) Log("Aprendeu Esteira (ramo Construção)");
            var L0 = Main.World.Layouts[0];
            var at = L0.PathPoints[Math.Min(4, L0.PathPoints.Count - 1)];
            P.GlobalPosition = at + Vector3.Up * 0.1f; P.Velocity = Vector3.Zero;
            await Frames(3);
            PressAction("build");
            await Frames(3);
            if (!bm.Active) { _errors++; Log("ERRO: modo construção não abriu"); return; }
            int sel = bm.Selected;
            PressAction("build_next");
            await Frames(3);
            if (bm.Selected != (sel + 1) % bm.Available.Count) { _errors++; Log("ERRO: trocar de construção não funcionou"); }
            if (!bm.SelectId(BuildIds.Belt)) { _errors++; Log("ERRO: esteira não liberada após a habilidade"); }
            BuildingSave b = null;
            for (int r = 2; r < 18 && b == null; r++)
                for (int a = 0; a < 12 && b == null; a++)
                {
                    float ang = a * Mathf.Tau / 12f;
                    float x = at.X + Mathf.Cos(ang) * r, z = at.Z + Mathf.Sin(ang) * r;
                    if (!L0.InGrid(x, z)) continue;
                    var q = L0.OnGround(x, z);
                    if (bm.CanPlace(BuildIds.Belt, q, out _)) b = bm.PlaceBelt(q, bm.SellIntake);
                }
            PressAction("build");
            await Frames(3);
            if (bm.Active) { _errors++; Log("ERRO: modo construção não fechou"); }
            if (b == null) { _errors++; Log("ERRO: não conseguiu construir a esteira"); return; }
            _hopperId = b.Id;
            Log($"Construiu esteira de {bm.BeltOf(b.Id)?.Length ?? -1:0} m até a venda (◈ {b.Paid})");
        }
        if (!_droneBought && _hopperId != 0 && GS.Data.Money >= 100000)
        {
            foreach (var sk in new[] { SkillIds.BuildHopper, SkillIds.BuildDrone })
                if (!GS.HasSkill(sk)) GS.TryBuySkill(sk);
            var L0 = Main.World.Layouts[0];
            var at = L0.PathPoints[Math.Min(6, L0.PathPoints.Count - 1)];
            for (int r = 2; r < 18 && !_droneBought; r++)
                for (int a = 0; a < 12 && !_droneBought; a++)
                {
                    float ang = a * Mathf.Tau / 12f;
                    float x = at.X + Mathf.Cos(ang) * r, z = at.Z + Mathf.Sin(ang) * r;
                    if (!L0.InGrid(x, z)) continue;
                    var q = L0.OnGround(x, z);
                    if (bm.CanPlace(BuildIds.Drone, q, out _) && bm.Place(BuildIds.Drone, q, 0) != null) { _droneBought = true; Log("Construiu a base do drone de área"); }
                }
        }
    }

    async Task SellTrip()
    {
        var region = GameData.Regions[0]; // única Estação de Venda, no acampamento
        await TryHireGoblin();
        await UpgradeGoblin();
        await TryBuild();
        // de vez em quando esvazia a mochila na entrada da esteira em vez de vender no balcão
        if (_hopperId != 0 && GS.CanWork && ++_hopperTrips % 3 == 0 && Main.Buildings.NodeOf(_hopperId) is BeltIntake hop)
        {
            int carried = GS.InventoryCount;
            if (await UseStationNode(hop) && GS.InventoryCount == 0 && carried > 0) { _sells++; return; }
            _errors++; Log("ERRO: não conseguiu usar a entrada da esteira");
        }
        if (!await UseStation<SellStation>(region)) return;
        var sell = Main.GetChildren().OfType<SellScreen>().First();
        if (!sell.IsOpen) { _errors++; Log("ERRO: tela de venda não abriu"); return; }
        long before = GS.Data.Money;
        // como um jogador que guarda minério para a forja: separa o que as próximas peças pedem antes de vender
        var keep = new Dictionary<string, int>();
        // só guarda quando já dá para pagar a taxa e sem passar de 40% da mochila (senão não sobra o que vender)
        foreach (var s in GearData.Slots)
            if (GS.NextGear(s) is { } nd && GS.GearTierAvailable(nd) && GS.Data.Money >= nd.Fee)
                foreach (var c in nd.Costs) keep[c.Mat] = Math.Max(keep.GetValueOrDefault(c.Mat), c.Amount);
        int budget = (int)(GS.Capacity * 0.4f);
        foreach (var k in keep.Keys.ToList())
        {
            int have = GS.InventoryOf(k), held = Math.Min(Math.Min(have, keep[k]), budget);
            budget -= held;
            keep[k] = held;
            if (held > 0) { GS.Data.Inventory[k] = have - held; if (GS.Data.Inventory[k] == 0) GS.Data.Inventory.Remove(k); }
        }
        bool nothingToSell = GS.InventoryCount == 0; // tudo o que trouxe é para a forja
        var btn = FindButtons(sell).First(b => b.Text == "VENDER TUDO");
        btn.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        foreach (var kv in keep) if (kv.Value > 0) GS.Data.Inventory[kv.Key] = GS.InventoryOf(kv.Key) + kv.Value;
        if (keep.Values.Sum() > 0) GS.RaiseAll();
        long gained = GS.Data.Money - before;
        if (gained <= 0 && !nothingToSell) { _errors++; Log("ERRO: venda não rendeu"); }
        _sells++;
        // troca de aba Vender → Melhorias e volta
        var shopTab = FindButtons(sell).First(b => b.Text.Contains("Melhorias"));
        shopTab.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(2);
        var shopScr = Main.GetChildren().OfType<ShopScreen>().First();
        if (GS.DayOver)
        {
            if (shopScr.IsOpen || !sell.IsOpen) { _errors++; Log("ERRO: Melhorias abriu depois do fim do turno"); }
        }
        else if (!shopScr.IsOpen || sell.IsOpen) { _errors++; Log("ERRO: troca de aba para Melhorias falhou"); }
        PressAction("pause"); // fecha
        await Frames(3);
        if (sell.IsOpen || shopScr.IsOpen) { _errors++; Log("ERRO: Esc não fechou o terminal"); }
        if (_sells % 5 == 1) Log($"Venda #{_sells}: +{gained} (saldo {GS.Data.Money})");
        await TakeFromChest(); // o que o Grub minerou entra na mochila e é vendido na próxima viagem
    }

    async Task SpendTrip()
    {
        if (!GS.CanWork) return;
        var next = GS.NextLockedRegion();
        // o bot testa a jornada principal; as regiões de fim de jogo têm teste próprio
        if (next != null && next.Index >= GameData.JourneyRegions) next = null;
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
        // guarda dinheiro se a próxima região está perto (a não ser que a ferramenta tenha quebrado)
        if (next != null && GS.Data.Money > next.UnlockCost * 0.6 && !GS.ToolBroken) return;

        var region = BestRegion();
        if (!GS.CanWork) return;
        if (!await UseStation<ShopTerminal>(region)) return;
        var shop = Main.GetChildren().OfType<ShopScreen>().First();
        if (!shop.IsOpen && GS.DayOver) return; // o turno acabou no caminho
        if (!shop.IsOpen) { _errors++; Log("ERRO: loja não abriu"); return; }
        // terminal de melhorias não vende: só a Estação de Venda do acampamento
        if (FindButtons(shop).Any(b => b.IsVisibleInTree() && b.Text.Contains("Vender")))
        { _errors++; Log("ERRO: terminal de melhorias permite vender (deveria ser só na Estação de Venda)"); }
        // ferramenta: repõe a quebrada (a melhor que der) ou troca pela próxima melhor quando sobra dinheiro
        {
            Button Tool(int i) => FindButtons(shop).FirstOrDefault(b => b.HasMeta("uid") && (string)b.GetMeta("uid") == "tool_" + i && !b.Disabled);
            int target = -1;
            for (int ti = ToolData.All.Count - 1; ti >= 0; ti--)
            {
                if (!GS.ToolAvailable(ti) || !GS.CanAfford(ToolData.All[ti].Cost)) continue;
                if (GS.ToolBroken || (ti > GS.Data.Tool && ToolData.All[ti].Cost <= GS.Data.Money * 0.7)) target = ti;
                break;
            }
            if (target >= 0 && Tool(target) is { } tb)
            {
                bool wasBroken = GS.ToolBroken;
                tb.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(2);
                if (GS.Data.Tool != target || GS.ToolBroken) { _errors++; Log("ERRO: compra de ferramenta não aplicou"); }
                else { _toolBuys++; Log((wasBroken ? "Repôs a ferramenta: " : "Comprou ") + ToolData.All[target].NamePt); }
            }
            else if (GS.ToolBroken) Log("Ferramenta quebrada e sem dinheiro para outra (segue com as mãos)");
        }
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
            if (GS.DayOver) break; // o turno acabou durante as compras (a loja recusa, como deve)
            if (GS.Data.Upgrades.Values.Sum() != lv + 1) { _errors++; Log("ERRO: compra não aplicou"); break; }
            _buys++;
        }
        PressAction("pause");
        await Frames(3);
        if (shop.IsOpen) { _errors++; Log("ERRO: Esc não fechou a loja"); }
        if (_buys > 0 && _buys % 10 == 0) Log($"{_buys} compras; {GS.CurrentTool.NamePt} ({GS.ToolDurabilityLeft} golpes), dano {GS.Damage:0}, {GS.SwingsPerSecond:0.0}/s, mochila {GS.Capacity}");
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
