using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Tests;

/// <summary>
/// Testes automatizados executados com:
///   godot --headless --path . -- --run-tests
/// Cobrem regras de economia, upgrades, regiões, save/load, dados inválidos,
/// integração de mineração na cena 3D e simulação de balanceamento.
/// </summary>
public partial class TestRunner : Node
{
    int _pass, _fail;
    readonly List<string> _failures = new();

    public override async void _Ready()
    {
        GD.Print("===== CRYSTAL EXCAVATOR — TESTES =====");
        string originalPath = SaveSystem.SavePath;
        SaveSystem.SavePath = "user://test_save.json";
        try
        {
            Run("Definições de dados", TestDefinitions);
            Run("Custos e efeitos progressivos", TestProgressiveCosts);
            Run("Capacidade do inventário", TestInventoryCapacity);
            Run("Venda com multiplicador", TestSell);
            Run("Compra de upgrades", TestBuyUpgrade);
            Run("Upgrades alteram atributos", TestUpgradeStats);
            Run("Desbloqueio de regiões", TestRegionUnlock);
            Run("Save/Load ida e volta", TestSaveRoundtrip);
            Run("Save corrompido usa backup", TestCorruptSave);
            Run("Save com dados inválidos é saneado", TestSanitize);
            Run("Compatibilidade com save antigo", TestOldSave);
            Run("Configurações inválidas", TestSettings);
            Run("Conclusão da jornada", TestCompletion);
            Run("Renascimento (prestígio)", TestPrestige);
            Run("Atualização automática", TestUpdater);
            Run("Proteção do save (regressão do bug da atualização)", TestSaveProtection);
            Run("Ciclo do dia (turno, fim do dia, novo dia)", TestDayCycle);
            Run("Goblin minerador: regras", TestGoblinRules);
            Run("Casa e árvore de habilidades", TestHomeAndSkills);
            Run("Áudio sem saturação", TestAudio);
            await RunAsync("Integração: minerar, coletar, renascer", TestMiningIntegration);
            await RunAsync("Integração: portões e estações", TestGatesIntegration);
            await RunAsync("Integração: pulso de ressonância", TestResonance);
            await RunAsync("Integração: eventos de região", TestRegionEvents);
            await RunAsync("Integração: marcador de objetivo", TestObjectiveMarker);
            await RunAsync("Integração: controle (gamepad)", TestGamepad);
            await RunAsync("Integração: cavernas, relevo e túneis secretos", TestWings);
            await RunAsync("Integração: coleta direta", TestDirectCollect);
            await RunAsync("Integração: goblin minerador e rebelião", TestGoblins);
            Run("Simulação de balanceamento", TestBalanceSimulation);
        }
        catch (Exception e)
        {
            Fail("Exceção não tratada: " + e);
        }
        SaveSystem.Delete();
        SaveSystem.SavePath = originalPath;
        GD.Print($"===== RESULTADO: {_pass} passaram, {_fail} falharam =====");
        foreach (var f in _failures) GD.Print("  FALHA: " + f);
        GameState.Instance.QuitClean(_fail == 0 ? 0 : 1);
    }

    void Run(string name, Action t)
    {
        int before = _fail;
        try { t(); }
        catch (Exception e) { Fail($"{name}: exceção {e.Message}\n{e.StackTrace}"); }
        GD.Print((_fail == before ? "[OK]   " : "[FALHA] ") + name);
    }

    async Task RunAsync(string name, Func<Task> t)
    {
        int before = _fail;
        try { await t(); }
        catch (Exception e) { Fail($"{name}: exceção {e.Message}\n{e.StackTrace}"); }
        GD.Print((_fail == before ? "[OK]   " : "[FALHA] ") + name);
    }

    void Check(bool cond, string msg) { if (cond) _pass++; else Fail(msg); }
    void Fail(string msg) { _fail++; _failures.Add(msg); }

    static GameState GS => GameState.Instance;

    void Fresh() { GS.NewGame(); }

    /// <summary>Põe o jogador no ponto de mineração do depósito, olhando para o centro dele.</summary>
    static void Face(PlayerController p, Crystal c, float yawOffset = 0f)
    {
        p.GlobalPosition = c.StandPoint;
        p.Velocity = Vector3.Zero;
        var dir = c.Center - (c.StandPoint + Vector3.Up * 1.55f);
        float yaw = Mathf.Atan2(-dir.X, -dir.Z);
        float pitch = Mathf.Atan2(dir.Y, new Vector2(dir.X, dir.Z).Length());
        p.SetLook(yaw + yawOffset, pitch);
    }

    // ---------------------------------------------------------------
    void TestHomeAndSkills()
    {
        Fresh();
        float dmg0 = GS.Damage, spd0 = GS.MoveSpeed, val0 = GS.ValueMultiplier, mag0 = GS.MagnetRadius, sw0 = GS.SwingsPerSecond;
        int cap0 = GS.Capacity;
        float day0 = GS.DayLength;
        Check(!GS.TryBuyHouse(HouseIds.Bed), "sem dinheiro não compra melhoria da casa");
        GS.AddMoney(50_000_000);
        // casa
        Check(GS.TryBuyHouse(HouseIds.Bed) && GS.TryBuyHouse(HouseIds.Bed), "compra a cama duas vezes");
        Check(Mathf.IsEqualApprox(GS.DayLength, day0 + 120), $"cama nível 2 = +2 min de turno ({GS.DayLength})");
        GS.TryBuyHouse(HouseIds.Workbench);
        Check(Mathf.IsEqualApprox(GS.Damage, dmg0 * 1.06f), "bancada +6% de dano");
        GS.TryBuyHouse(HouseIds.Wardrobe);
        Check(GS.Capacity == (int)(cap0 * 1.10f), "armário +10% de mochila");
        GS.TryBuyHouse(HouseIds.Kitchen);
        Check(Mathf.IsEqualApprox(GS.MoveSpeed, spd0 * 1.05f), "cozinha +5% de velocidade");
        GS.TryBuyHouse(HouseIds.Trophies);
        Check(Mathf.IsEqualApprox(GS.ValueMultiplier, val0 * 1.04f), "troféus +4% de venda");
        for (int i = 0; i < 10; i++) GS.TryBuyHouse(HouseIds.Bed);
        Check(GS.HouseLevel(HouseIds.Bed) == 3 && GS.HouseMaxed(HouseIds.Bed) && GS.HouseCost(HouseIds.Bed) == long.MaxValue, "cama para no nível máximo");
        GS.EndDay(early: true);
        Check(GS.TryBuyHouse(HouseIds.Kitchen), "em casa (fora do turno) as melhorias da casa podem ser compradas");
        GS.StartNewDay();
        // habilidades: pré-requisitos
        Check(!GS.TryBuySkill(SkillIds.DirectCollect), "habilidade de nível 2 exige a anterior");
        Check(GS.TryBuySkill(SkillIds.Magnet) && Mathf.IsEqualApprox(GS.MagnetRadius, mag0 * 1.3f), "Ímã ampliado +30%");
        Check(!GS.TryBuySkill(SkillIds.Magnet), "não compra a mesma habilidade duas vezes");
        Check(GS.TryBuySkill(SkillIds.DirectCollect) && GS.DirectCollect, "Coleta direta liberada após o Ímã");
        float dmg1 = GS.Damage;
        GS.TryBuySkill(SkillIds.FirmStrike);
        Check(Mathf.IsEqualApprox(GS.Damage, dmg1 * 1.15f), "Golpe firme +15%");
        GS.TryBuySkill(SkillIds.Cadence);
        Check(Mathf.IsEqualApprox(GS.SwingsPerSecond, sw0 * 1.10f), "Cadência +10%");
        Check(GS.CritChance == 0, "sem crítico antes da habilidade");
        GS.TryBuySkill(SkillIds.Critical);
        Check(GS.CritChance > 0.1f, "Golpe crítico ativo");
        float day1 = GS.DayLength;
        GS.TryBuySkill(SkillIds.Early); GS.TryBuySkill(SkillIds.Overtime);
        Check(Mathf.IsEqualApprox(GS.DayLength, day1 + 240), "Madrugador + Hora extra = +4 min");
        var rare = GameData.Mat("sunstone");
        long rp = GS.SellPrice(rare);
        GS.TryBuySkill(SkillIds.Haggle); GS.TryBuySkill(SkillIds.RareNose);
        Check(GS.RareChanceMultiplier > 1.4f, "Faro para raros aumenta a chance");
        long rp2 = GS.SellPrice(rare);
        GS.TryBuySkill(SkillIds.Jeweler);
        Check(GS.SellPrice(rare) >= (long)(rp2 * 1.49) && rp2 > rp, "Joalheiro valoriza raros");
        // bônus do turno: só com a habilidade, uma vez por dia
        GS.Data.DayEarned = 0;
        GS.AddItem("quartz", 10);
        long sold = GS.SellAll();
        Check(GS.PayShiftBonus() == 0, "sem a habilidade não há bônus do turno");
        GS.TryBuySkill(SkillIds.Compactor); GS.TryBuySkill(SkillIds.ShiftBonus);
        long bonus = GS.PayShiftBonus();
        Check(bonus == Math.Max(1, sold / 10) && GS.PayShiftBonus() == 0, "bônus do turno = 10% do dia, pago uma vez");
        // save e renascimento
        GS.SaveGame(true);
        int houseSum = GS.Data.House.Values.Sum();
        GS.LoadGame();
        Check(GS.Data.House.Values.Sum() == houseSum && GS.Data.Skills.Count == HomeData.Skills.Count, "casa e habilidades são salvas");
        GS.Data.GameCompleted = true;
        GS.Data.RunEarned = 10_000_000;
        GS.Prestige();
        Check(GS.Data.Skills.Count == 0 && GS.Data.House.Values.Sum() == houseSum, "renascer zera a árvore e mantém a casa");
        var bad = new SaveData { House = new() { ["bed"] = 99, ["nada"] = 2 }, Skills = new() { "magnet", "magnet", "inexistente" } };
        bad.Sanitize();
        Check(bad.House["bed"] == 3 && !bad.House.ContainsKey("nada") && bad.Skills.Count == 1, "casa/habilidades inválidas são saneadas");
        Fresh();
    }

    // ---------------------------------------------------------------
    void TestGoblinRules()
    {
        Fresh();
        var d = GS.Data;
        Check(GS.Goblin == GameState.GoblinMood.Available && !GS.TryHireGoblin(), "sem dinheiro não contrata o goblin");
        GS.AddMoney(1000);
        Check(GS.TryHireGoblin() && GS.Goblin == GameState.GoblinMood.Working && d.Money == 500, "contratar custa ◈ 500");
        var q = GameData.Mat("quartz");
        GS.GoblinMined(q, 40);
        long val = GS.SellPrice(q) * 40;
        Check(GS.GoblinStashCount == 40 && d.GoblinDayValue == val, "o que ele minera vai para o baú e conta no dia");
        Check(GS.GoblinFee == Math.Max(30, val / 4), "taxa = 1/4 do dia (mínimo 30)");
        int cap = GS.FreeSpace;
        int got = GS.CollectGoblinStash();
        Check(got == Math.Min(40, cap) && GS.InventoryCount == got, "pegar do baú enche a mochila");
        long money = d.Money, fee = GS.GoblinFee;
        Check(GS.GoblinFeeDue && GS.SettleGoblinFee(true) == GameState.FeeOutcome.Paid && d.Money == money - fee && !GS.GoblinFeeDue, "pagar a taxa");
        Check(GS.SettleGoblinFee(true) == GameState.FeeOutcome.NoFee, "só cobra uma vez por dia");
        GS.StartNewDay();
        Check(GS.SettleGoblinFee(false) == GameState.FeeOutcome.Forgiven, "1ª falta: perdoa");
        GS.StartNewDay();
        Check(GS.SettleGoblinFee(false) == GameState.FeeOutcome.Warned, "2ª falta: avisa");
        GS.StartNewDay();
        Check(GS.SettleGoblinFee(false, forceRebel: true) == GameState.FeeOutcome.Rebel && GS.Goblin == GameState.GoblinMood.Rebel, "3ª falta: rebelião");
        GS.EndGoblinRebellion(true);
        Check(GS.Goblin == GameState.GoblinMood.Working && d.GoblinStrikes == 0, "rebelião vencida: Grub volta ao trabalho");
        GS.StartNewDay();
        GS.SettleGoblinFee(true);
        Check(d.GoblinStrikes == 0, "pagar zera as faltas");
        for (int i = 0; i < 2; i++) { GS.StartNewDay(); GS.SettleGoblinFee(false); }
        GS.StartNewDay();
        Check(GS.SettleGoblinFee(false, forceRebel: false) == GameState.FeeOutcome.Fled && GS.Goblin == GameState.GoblinMood.Fled, "ou ele foge");
        Check(!GS.CanRehireGoblin && !GS.TryHireGoblin(), "não volta logo depois de fugir");
        for (int i = 0; i < GameState.GoblinRehireDays; i++) GS.StartNewDay();
        GS.AddMoney(5000);
        long before = d.Money;
        Check(GS.CanRehireGoblin && GS.TryHireGoblin() && before - d.Money == GameState.GoblinRehireCost, "volta dias depois, por ◈ 2.000");
        GS.Data.Inventory.Clear();
        GS.AddItem("quartz", 20);
        int stolen = GS.StealFromInventory(0.25f);
        Check(stolen == 5 && GS.InventoryCount == 15, "desmaio: goblins levam 25% da mochila");
        GS.SaveGame(true);
        GS.LoadGame();
        Check(GS.Goblin == GameState.GoblinMood.Working, "estado do goblin é salvo");
        var bad = new SaveData { GoblinState = 9, GoblinStrikes = -3, GoblinStash = new() { ["nada"] = 4, ["quartz"] = -1 } };
        bad.Sanitize();
        Check(bad.GoblinState == 3 && bad.GoblinStrikes == 0 && bad.GoblinStash.Count == 0, "dados do goblin saneados");
        Fresh();
    }

    async Task TestGoblins()
    {
        Fresh();
        var root = new Node3D();
        AddChild(root);
        root.AddChild(new Effects());
        root.AddChild(new ShardManager());
        var world = new WorldBuilder();
        root.AddChild(world);
        var player = new PlayerController();
        root.AddChild(player);
        player.SimulateCapturedMouse = true;
        player.GlobalPosition = world.SpawnPoint;
        await Frames(3);
        var camp = world.Goblins;
        Check(camp != null && camp.Grub != null && camp.Chest != null, "acampamento do goblin com barraca, baú e Grub");
        camp.PlayerPos = () => player.GlobalPosition;
        camp.DamagePlayer = (dmg, from) => player.TakeDamage(dmg, from);
        camp.PlayerInMine = () => true;
        Check(world.Layouts[0].Reachable(camp.GrubHome.X, camp.GrubHome.Z) && world.Layouts[0].Reachable(camp.ChestPos.X, camp.ChestPos.Z + 1f) || world.Layouts[0].NearestReach(camp.ChestPos.X, camp.ChestPos.Z) >= 0, "Grub e o baú ficam em lugar alcançável");
        GS.AddMoney(1000);
        GS.TryHireGoblin();
        camp.SyncState();
        GS.IsPlaying = true;
        Engine.TimeScale = 6;
        var grubStart = camp.Grub.GlobalPosition;
        for (int i = 0; i < 1500 && GS.GoblinStashCount == 0; i++) await Frames(1);
        Check(camp.Grub.GlobalPosition.DistanceTo(grubStart) > 1f, "Grub anda até um depósito");
        Check(GS.GoblinStashCount > 0 && GS.Data.GoblinDayValue > 0, $"Grub minera e guarda no baú ({GS.GoblinStashCount} fragmentos)");
        Check(GS.Data.CrystalsMined == 0, "o que o goblin quebra não conta como do jogador");
        Engine.TimeScale = 1;

        // rebelião: perseguem e atacam; o feixe atordoa
        GS.Data.GoblinState = (int)GameState.GoblinMood.Rebel;
        camp.StartRebellion();
        Check(camp.Rebels.Count == 3 && camp.Rebels.All(g => g.Hostile), "rebelião: Grub e 2 comparsas hostis");
        player.GlobalPosition = world.Layouts[0].OnGround(camp.GrubHome.X - 4f, camp.GrubHome.Z, 0.1f);
        float hp0 = player.Hp;
        for (int i = 0; i < 600 && player.Hp >= hp0; i++) await Frames(1);
        Check(player.Hp < hp0, $"goblins rebelados atacam o jogador (vida {player.Hp:0})");
        // feixe real contra o goblin mais próximo
        var g0 = camp.Rebels.OrderBy(g => g.GlobalPosition.DistanceTo(player.GlobalPosition)).First();
        int h0 = g0.HitsLeft;
        for (int tries = 0; tries < 20 && g0.HitsLeft == h0; tries++)
        {
            var eye = player.GlobalPosition + Vector3.Up * 1.55f;
            var dir = g0.GlobalPosition + Vector3.Up * 0.7f - eye;
            player.SetLook(Mathf.Atan2(-dir.X, -dir.Z), Mathf.Atan2(dir.Y, new Vector2(dir.X, dir.Z).Length()));
            Input.ActionPress("mine");
            await Frames(6);
            Input.ActionRelease("mine");
            await Frames(2);
        }
        Check(g0.HitsLeft < h0, "o feixe acerta e atordoa o goblin rebelado");
        bool won = false;
        camp.RebellionWon += () => won = true;
        foreach (var g in camp.Rebels.ToList()) while (g.State != GoblinNpc.Mode.Defeated) g.TakeHit(player.GlobalPosition, 1);
        await Frames(3);
        Check(won && GS.Goblin == GameState.GoblinMood.Working && camp.Rebels.Count == 0 && !camp.Grub.Hostile, "todos atordoados: rebelião vencida, Grub volta ao trabalho");
        GS.IsPlaying = false;
        root.QueueFree();
        await Frames(2);
        Fresh();
    }

    // ---------------------------------------------------------------
    void TestDayCycle()
    {
        Fresh();
        var d = GS.Data;
        Check(d.Day == 1 && !GS.DayOver && GS.CanWork, "novo jogo começa no Dia 1 com o turno aberto");
        Check(GS.ClockText == "06:00", $"relógio começa às 06:00 (está {GS.ClockText})");
        d.DayTimeSeconds = GS.DayLength / 2;
        Check(GS.ClockText == "12:00", $"metade do turno = 12:00 (está {GS.ClockText})");
        int warns = 0, ended = 0, started = 0;
        void OnN(string t, string b, NotifyKind k) { if (t.Contains("acabando")) warns++; }
        GS.Notified += OnN;
        GS.DayEnded += () => ended++;
        d.DayTimeSeconds = GS.DayLength - GameState.DayWarningSeconds - 1;
        GS.TickDay(2);
        Check(warns == 1 && !GS.DayOver, "aviso de 1 minuto antes do fim do turno");
        GS.TickDay(GameState.DayWarningSeconds + 5);
        GS.Notified -= OnN;
        Check(GS.DayOver && !GS.CanWork && ended == 1, "turno termina ao fim do tempo");
        Check(GS.ClockText == "18:00", $"relógio para às 18:00 (está {GS.ClockText})");
        double t = d.DayTimeSeconds;
        GS.TickDay(30);
        Check(d.DayTimeSeconds == t, "tempo não corre depois do fim do turno");
        // fora do turno: só vender
        GS.AddMoney(10_000_000);
        Check(!GS.TryBuyUpgrade(UpgradeIds.Damage), "não compra melhorias fora do turno");
        Check(!GS.TryUnlockRegion("depths"), "não desbloqueia regiões fora do turno");
        GS.AddItem("quartz", 5);
        long gained = GS.SellAll();
        Check(gained > 0 && d.DayEarned == gained, "venda continua funcionando e soma no ganho do dia");
        // novo dia
        GS.DayStarted += () => started++;
        GS.StartNewDay();
        Check(d.Day == 2 && !GS.DayOver && d.DayTimeSeconds == 0 && d.DayEarned == 0 && started == 1, "dormir começa o Dia 2 zerado");
        Check(GS.TryBuyUpgrade(UpgradeIds.Damage), "no novo dia volta a comprar");
        // encerrar mais cedo (elevador) não mostra a mensagem de fim de turno
        int big = 0;
        void OnBig(string m) => big++;
        GS.BigMessage += OnBig;
        d.DayTimeSeconds = 100;
        GS.EndDay(early: true);
        GS.BigMessage -= OnBig;
        Check(GS.DayOver && big == 0 && d.DayTimeSeconds == 100, "volta antecipada encerra o dia sem alarde");
        // persistência e saneamento
        d.Day = 5;
        GS.SaveGame(true);
        GS.LoadGame();
        Check(GS.Data.Day == 5 && GS.Data.DayOver, "dia e estado do turno são salvos");
        var bad = new SaveData { Day = -3, DayTimeSeconds = double.NaN, DayEarned = -5 };
        bad.Sanitize();
        Check(bad.Day == 1 && bad.DayTimeSeconds == 0 && bad.DayEarned == 0, "valores inválidos do dia são corrigidos");
        Fresh();
    }

    // ---------------------------------------------------------------
    void TestDefinitions()
    {
        Check(GameData.Regions.Count >= 3, "deve haver ao menos 3 regiões");
        Check(GameData.Upgrades.Count == 7, "devem existir 7 upgrades");
        foreach (var r in GameData.Regions)
        {
            Check(r.Spawns.All(s => GameData.Mat(s.MaterialId) != null), $"região {r.Id} referencia material inexistente");
            Check(GameData.Mat(r.RareMaterialId)?.Rare == true, $"região {r.Id} sem material raro válido");
        }
        for (int i = 1; i < GameData.Regions.Count; i++)
            Check(GameData.Regions[i].UnlockCost > GameData.Regions[i - 1].UnlockCost, "custos de região devem crescer");
        // regiões mais fundas devem render mais por segundo de mineração
        double prev = 0;
        foreach (var r in GameData.Regions)
        {
            var m = GameData.Mat(r.Spawns[0].MaterialId);
            double vph = m.Value * (m.ShardsMin + m.ShardsMax) / 2.0;
            Check(vph > prev, $"valor por cristal em {r.Id} deve superar a região anterior");
            prev = vph;
        }
    }

    void TestProgressiveCosts()
    {
        foreach (var u in GameData.Upgrades.Values)
        {
            for (int l = 0; l < u.MaxLevel - 1; l++)
            {
                if (u.CostAt(l + 1) <= u.CostAt(l)) { Fail($"{u.Id}: custo não cresce no nível {l}"); return; }
                if (u.ValueAt(l + 1) <= u.ValueAt(l)) { Fail($"{u.Id}: efeito não cresce no nível {l}"); return; }
            }
            Check(u.CostAt(u.MaxLevel) == long.MaxValue, $"{u.Id}: nível máximo deve ser incomprável");
        }
    }

    void TestInventoryCapacity()
    {
        Fresh();
        int cap = GS.Capacity;
        Check(cap == 30, $"capacidade inicial esperada 30, obtida {cap}");
        int added = GS.AddItem("quartz", cap + 10);
        Check(added == cap, $"deveria aceitar apenas {cap}, aceitou {added}");
        Check(GS.InventoryFull, "inventário deveria estar cheio");
        Check(GS.AddItem("amethyst", 1) == 0, "não deve aceitar itens com inventário cheio");
        Check(GS.AddItem("material_inexistente", 1) == 0, "material inválido deve ser rejeitado");
    }

    void TestSell()
    {
        Fresh();
        GS.AddItem("quartz", 10);
        GS.AddItem("amethyst", 5);
        long expected = 10 * 3 + 5 * 8;
        Check(GS.InventoryValue() == expected, $"valor do inventário esperado {expected}, obtido {GS.InventoryValue()}");
        long got = GS.SellAll();
        Check(got == expected, "venda deve pagar o valor do inventário");
        Check(GS.Data.Money == expected, "dinheiro deve aumentar");
        Check(GS.InventoryCount == 0, "inventário deve esvaziar");
        Check(GS.SellAll() == 0, "vender vazio não deve dar dinheiro");
        GS.Data.Upgrades[UpgradeIds.Value] = 5;
        GS.AddItem("quartz", 10);
        long withMult = GS.SellAll();
        Check(withMult > 30, $"multiplicador deve aumentar a venda (obtido {withMult})");
    }

    void TestBuyUpgrade()
    {
        Fresh();
        Check(!GS.TryBuyUpgrade(UpgradeIds.Damage), "não deve comprar sem dinheiro");
        long cost = GS.UpgradeCost(UpgradeIds.Damage);
        GS.AddMoney(cost);
        Check(GS.TryBuyUpgrade(UpgradeIds.Damage), "deve comprar com dinheiro suficiente");
        Check(GS.Data.Money == 0, "deve descontar o custo exato");
        Check(GS.Data.Level(UpgradeIds.Damage) == 1, "nível deve ser 1");
        Check(GS.UpgradeCost(UpgradeIds.Damage) > cost, "próximo nível deve ser mais caro");
        GS.Data.Upgrades[UpgradeIds.Move] = GameData.Upgrades[UpgradeIds.Move].MaxLevel;
        GS.AddMoney(1_000_000_000);
        Check(!GS.TryBuyUpgrade(UpgradeIds.Move), "não deve passar do nível máximo");
        Check(!GS.TryBuyUpgrade("inexistente"), "upgrade inexistente deve falhar");
    }

    void TestUpgradeStats()
    {
        Fresh();
        float d0 = GS.Damage, s0 = GS.SwingsPerSecond, m0 = GS.MoveSpeed, r0 = GS.MagnetRadius, reach0 = GS.MiningReach;
        int c0 = GS.Capacity;
        float v0 = GS.ValueMultiplier;
        foreach (var id in UpgradeIds.All) GS.Data.Upgrades[id] = 5;
        Check(GS.Damage > d0 * 2, "dano deve crescer significativamente");
        Check(GS.SwingsPerSecond > s0 * 1.4f, "velocidade de golpe deve crescer");
        Check(GS.MoveSpeed > m0, "velocidade de movimento deve crescer");
        Check(GS.MagnetRadius > r0 && GS.MiningReach > reach0, "alcance deve crescer");
        Check(GS.Capacity > c0, "capacidade deve crescer");
        Check(GS.ValueMultiplier > v0, "valor deve crescer");
        // quartzo: vários segundos no início, quase instantâneo depois
        var q = GameData.Mat("quartz");
        Fresh();
        float tStart = q.Hp / GS.DamagePerSecond;
        foreach (var id in UpgradeIds.All) GS.Data.Upgrades[id] = 12;
        float tLate = q.Hp / GS.DamagePerSecond;
        GD.Print($"      quartzo: {tStart:0.00}s no início, {tLate:0.000}s com upgrades nível 12");
        Check(tStart >= 2.5f && tLate < 0.3f, "progressão de poder deve ser perceptível");
    }

    void TestRegionUnlock()
    {
        Fresh();
        var r2 = GameData.Regions[1]; var r3 = GameData.Regions[2];
        Check(!GS.TryUnlockRegion(r2.Id), "não deve desbloquear sem dinheiro");
        GS.AddMoney(r3.UnlockCost * 2);
        Check(!GS.TryUnlockRegion(r3.Id), "não deve pular a região 2");
        Check(GS.TryUnlockRegion(r2.Id), "deve desbloquear a região 2");
        Check(GS.TryUnlockRegion(r3.Id), "deve desbloquear a região 3 depois da 2");
        Check(!GS.TryUnlockRegion(r3.Id), "não deve desbloquear duas vezes");
        foreach (var extra in GameData.Regions.Skip(3))
        {
            GS.AddMoney(extra.UnlockCost);
            Check(GS.TryUnlockRegion(extra.Id), $"deve desbloquear {extra.Name}");
        }
        Check(GS.NextLockedRegion() == null, "não deve haver regiões bloqueadas");
    }

    void TestSaveRoundtrip()
    {
        Fresh();
        SaveSystem.Delete();
        GS.AddMoney(12345);
        GS.Data.Upgrades[UpgradeIds.Damage] = 7;
        GS.Data.Upgrades[UpgradeIds.Capacity] = 3;
        GS.Data.UnlockedRegions.Add("depths");
        GS.AddItem("sapphire", 4);
        GS.Data.PlayerX = 1.5f; GS.Data.PlayerZ = -40f; GS.Data.HasPosition = true;
        Check(SaveSystem.Save(GS.Data), "salvar deve funcionar");
        Fresh();
        Check(GS.Data.Money == 0, "novo jogo deve zerar");
        Check(GS.LoadGame(), "carregar deve funcionar");
        var d = GS.Data;
        Check(d.Money == 12345, $"dinheiro persistido (obtido {d.Money})");
        Check(d.Level(UpgradeIds.Damage) == 7 && d.Level(UpgradeIds.Capacity) == 3, "upgrades persistidos");
        Check(d.UnlockedRegions.Contains("depths"), "desbloqueios persistidos");
        Check(d.Inventory.TryGetValue("sapphire", out int s) && s == 4, "inventário persistido");
        Check(d.HasPosition && Mathf.IsEqualApprox(d.PlayerZ, -40f), "posição persistida");
        Check(d.Discovered.Contains("sapphire"), "descobertas persistidas");
    }

    void TestCorruptSave()
    {
        SaveSystem.Delete();
        Fresh();
        GS.AddMoney(500);
        SaveSystem.Save(GS.Data);           // save válido
        GS.AddMoney(100);
        SaveSystem.Save(GS.Data);           // promove o anterior a backup
        using (var f = FileAccess.Open(SaveSystem.SavePath, FileAccess.ModeFlags.Write)) f.StoreString("{ isto não é json válido ");
        var loaded = SaveSystem.Load(out string msg);
        Check(loaded != null, "deve restaurar o backup");
        Check(loaded?.Money == 500, $"backup deve conter 500 (obtido {loaded?.Money})");
        Check(msg.Contains("Backup"), "mensagem deve informar restauração");
        using (var f = FileAccess.Open(SaveSystem.BackupPath, FileAccess.ModeFlags.Write)) f.StoreString("lixo");
        var none = SaveSystem.Load(out _);
        Check(none == null, "sem nada válido deve retornar null");
        Check(!GS.LoadGame() && GS.Data.Money == 0, "LoadGame inválido deve iniciar novo jogo");
        SaveSystem.Delete();
        Check(SaveSystem.Load(out _) == null, "sem arquivos deve retornar null");
    }

    void TestSanitize()
    {
        string json = @"{
            ""Money"": -999,
            ""Upgrades"": { ""damage"": 9999, ""hack"": 5, ""reach"": -3 },
            ""UnlockedRegions"": [""core"", ""core"", ""nowhere"", null],
            ""Inventory"": { ""quartz"": 5, ""fake"": 10, ""ruby"": -4 },
            ""PlayerX"": ""NaN"", ""HasPosition"": true
        }";
        bool ok = SaveSystem.TryParse(json, out var d);
        Check(ok, "deve aceitar e corrigir dados estranhos");
        if (!ok) return;
        Check(d.Money == 0, "dinheiro negativo deve virar 0");
        Check(d.Level("damage") == GameData.Upgrades["damage"].MaxLevel, "nível acima do máximo deve ser limitado");
        Check(!d.Upgrades.ContainsKey("hack"), "upgrade desconhecido removido");
        Check(d.Level("reach") == 0, "nível negativo vira 0");
        Check(d.UnlockedRegions.Contains("caves") && d.UnlockedRegions.Count(r => r == "core") == 1 && !d.UnlockedRegions.Contains("nowhere"), "regiões saneadas");
        Check(d.Inventory.Count == 1 && d.Inventory["quartz"] == 5, "inventário saneado");
        Check(!d.HasPosition, "posição inválida descartada");
        Check(!SaveSystem.TryParse("", out _) && !SaveSystem.TryParse("null", out _) && !SaveSystem.TryParse("[1,2]", out _), "entradas vazias ou erradas rejeitadas");
    }

    void TestOldSave()
    {
        // simula save de versão anterior com poucos campos
        bool ok = SaveSystem.TryParse(@"{ ""Version"": 0, ""Money"": 42 }", out var d);
        Check(ok && d.Money == 42 && d.Version == SaveData.CurrentVersion, "save antigo deve carregar com padrões");
        Check(ok && d.UnlockedRegions.Contains("caves") && d.Upgrades != null && d.Inventory != null, "campos ausentes recebem padrões");
        bool ok2 = SaveSystem.TryParse(@"{ ""Version"": 99, ""Money"": 7, ""CampoFuturo"": 123 }", out var d2);
        Check(ok2 && d2.Money == 7, "save de versão futura com campos extras deve carregar");
    }

    void TestSettings()
    {
        var s = new SettingsData { MouseSensitivity = float.NaN, Fov = 500, MasterVolume = -3, Quality = 9, RenderScale = 0, WindowSize = 42 };
        s.Sanitize();
        Check(s.WindowSize == Settings.WindowSizes.Length - 1, "resolução inválida deve ser limitada");
        Check(s.MouseSensitivity > 0 && s.Fov <= 100 && s.MasterVolume == 0 && s.Quality == 2 && s.RenderScale >= 0.5f, "configurações saneadas");
    }

    void TestCompletion()
    {
        Fresh();
        int fired = 0;
        void H() => fired++;
        GS.Completed += H;
        GS.CheckCompletion();
        Check(fired == 0 && !GS.Data.GameCompleted, "não conclui no início");
        foreach (var r in GameData.Regions) if (!GS.Data.UnlockedRegions.Contains(r.Id)) GS.Data.UnlockedRegions.Add(r.Id);
        GS.CheckCompletion();
        Check(fired == 0, "sem o Coração não conclui");
        GS.AddItem("heart", 1);
        GS.CheckCompletion();
        Check(fired == 1 && GS.Data.GameCompleted, "regiões + Coração concluem a jornada");
        GS.CheckCompletion();
        Check(fired == 1, "conclusão dispara uma única vez");
        GS.Completed -= H;
        Check(SaveSystem.TryParse(System.Text.Json.JsonSerializer.Serialize(GS.Data), out var d) && d.GameCompleted, "conclusão persiste no save");
        Fresh();
    }

    void TestPrestige()
    {
        Fresh();
        Check(!GS.CanPrestige && !GS.Prestige(), "não renasce sem concluir");
        GS.AddMoney(4_000_000);
        GS.Data.Upgrades[UpgradeIds.Damage] = 20;
        foreach (var r in GameData.Regions) if (!GS.Data.UnlockedRegions.Contains(r.Id)) GS.Data.UnlockedRegions.Add(r.Id);
        GS.AddItem("heart", 1);
        GS.CheckCompletion();
        Check(GS.CanPrestige, "concluído com ganhos permite renascer");
        int pending = GS.PendingEssence;
        Check(pending == 4, $"4 mi de ganhos devem render 4 de Essência (obtido {pending})");
        float dmgBase0 = GameData.Upgrades[UpgradeIds.Damage].ValueAt(0);
        long totalBefore = GS.Data.TotalEarned;
        Check(GS.Prestige(), "renascimento deve ocorrer");
        var d = GS.Data;
        Check(d.Essence == 4 && d.PrestigeCount == 1, "Essência e contagem registradas");
        Check(d.Money == 0 && d.Upgrades.Count == 0 && d.Inventory.Count == 0, "progresso da partida zerado");
        Check(d.UnlockedRegions.Count == 1 && d.UnlockedRegions[0] == "caves", "regiões voltam ao início");
        Check(!d.GameCompleted && d.RunEarned == 0, "nova jornada começa");
        Check(d.Discovered.Contains("heart") && d.TotalEarned == totalBefore, "Coleção e estatísticas mantidas");
        Check(Mathf.IsEqualApprox(GS.Damage, dmgBase0 * 1.4f), $"bônus de dano x1,4 (obtido {GS.Damage / dmgBase0:0.00})");
        Check(Mathf.IsEqualApprox(GS.ValueMultiplier, 1.4f), "bônus de venda x1,4");
        Check(!GS.CanPrestige, "não renasce de novo sem nova conclusão");
        Check(SaveSystem.TryParse(System.Text.Json.JsonSerializer.Serialize(d), out var d2) && d2.Essence == 4 && d2.PrestigeCount == 1, "Essência persiste no save");
        // migração: save antigo concluído, sem RunEarned
        Check(SaveSystem.TryParse(@"{ ""TotalEarned"": 1000000, ""GameCompleted"": true }", out var old) && old.RunEarned == 1000000, "save antigo usa o total ganho como partida atual");
        Fresh();
    }

    void TestSaveProtection()
    {
        SaveSystem.Delete();
        foreach (var f in System.IO.Directory.Exists(ProjectSettings.GlobalizePath(SaveSystem.BackupDir)) ? System.IO.Directory.GetFiles(ProjectSettings.GlobalizePath(SaveSystem.BackupDir)) : new string[0]) System.IO.File.Delete(f);
        Fresh();
        GS.AddMoney(777_000);
        GS.Data.PlayTimeSeconds = 3600;
        Check(GS.SaveGame(true), "partida carregada salva normalmente");
        // jogo recém-aberto (menu): dados em memória vazios e nenhuma partida carregada
        GS.ResetToBootStateForTests();
        Check(!GS.SaveGame(true), "sem partida carregada, salvar deve ser recusado");
        var onDisk = SaveSystem.Load(out _);
        Check(onDisk != null && onDisk.Money == 777_000, $"save do jogador intacto (dinheiro {onDisk?.Money})");

        // backups
        var b1 = SaveSystem.Snapshot("teste");
        Check(b1 != null && SaveSystem.ListSnapshots().Count == 1, "backup criado");
        Check(SaveSystem.Snapshot("teste") == b1 && SaveSystem.ListSnapshots().Count == 1, "backup idêntico não é duplicado");
        GS.LoadGame();
        GS.AddMoney(1);
        GS.SaveGame(true);
        for (int i = 0; i < 14; i++) { GS.AddMoney(1); GS.SaveGame(true); SaveSystem.Snapshot("rot" + i); }
        Check(SaveSystem.ListSnapshots().Count == SaveSystem.MaxSnapshots, $"mantém só os {SaveSystem.MaxSnapshots} backups mais recentes");
        // novo jogo arquiva em vez de apagar
        long before = SaveSystem.Load(out _).Money;
        SaveSystem.ArchiveAndDelete();
        Check(!SaveSystem.HasSave(), "novo jogo remove o save ativo");
        var latest = SaveSystem.ListSnapshots()[0];
        Check(latest.Data.Money == before, "save anterior ao novo jogo fica nos backups");
        Check(SaveSystem.Restore(latest.Path) && SaveSystem.Load(out _).Money == before, "restaurar backup recupera o progresso");
        // saves vazios não viram backup
        Fresh();
        SaveSystem.Save(GS.Data);
        int n = SaveSystem.ListSnapshots().Count;
        SaveSystem.Snapshot("vazio");
        Check(SaveSystem.ListSnapshots().Count == n, "save sem progresso não gera backup");
        Check(SaveSystem.BackupDir != "user://backups", "testes usam pasta de backup separada da do jogador");
        foreach (var f in System.IO.Directory.GetFiles(ProjectSettings.GlobalizePath(SaveSystem.BackupDir))) System.IO.File.Delete(f);
        SaveSystem.Delete();
        Fresh();
    }

    void TestUpdater()
    {
        Check(UpdateManager.IsNewer("0.2.1", "0.2.0") && UpdateManager.IsNewer("0.10.0", "0.9.9") && UpdateManager.IsNewer("1.0.0", "0.99.99"), "versões mais novas detectadas");
        Check(!UpdateManager.IsNewer("0.2.0", "0.2.0") && !UpdateManager.IsNewer("0.1.9", "0.2.0") && !UpdateManager.IsNewer("lixo", "0.2.0"), "versões iguais/antigas/inválidas ignoradas");
        string h = new string('a', 64);
        string ok = $@"{{""version"":""0.3.0"",""engine"":""4.5.1"",""files"":[{{""path"":""CrystalExcavator.pck"",""url"":""https://x/y.pck"",""sha256"":""{h}"",""size"":10}}]}}";
        var m = UpdateManager.ParseManifest(ok);
        Check(m != null && m.Version == "0.3.0" && m.Files.Count == 1, "manifesto válido aceito");
        Check(UpdateManager.ParseManifest(ok.Replace("CrystalExcavator.pck", "../../Windows/x.dll")) == null, "caminho fora da pasta do jogo recusado");
        Check(UpdateManager.ParseManifest(ok.Replace("CrystalExcavator.pck", "C:/x.pck")) == null, "caminho absoluto recusado");
        Check(UpdateManager.ParseManifest(ok.Replace("https://", "http://")) == null, "URL sem HTTPS recusada");
        Check(UpdateManager.ParseManifest(ok.Replace(h, "abc")) == null, "hash inválido recusado");
        Check(UpdateManager.ParseManifest("{ quebrado") == null && UpdateManager.ParseManifest(@"{""version"":""1.0.0"",""files"":[]}") == null, "manifesto inválido/vazio recusado");
        // SHA-256 e script de aplicação
        string dir = ProjectSettings.GlobalizePath("user://test_update");
        System.IO.Directory.CreateDirectory(dir);
        string f = System.IO.Path.Combine(dir, "a.bin");
        System.IO.File.WriteAllText(f, "abc");
        Check(UpdateManager.Sha256Of(f) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA-256 correto");
        var files = new List<UpdateFile> { new() { Path = "CrystalExcavator.pck" }, new() { Path = "data_CrystalExcavator_windows_x86_64/CrystalExcavator.dll" } };
        string bat = System.IO.File.ReadAllText(UpdateManager.WriteApplyScript(dir, @"C:\Jogo", @"C:\Jogo\CrystalExcavator.exe", files, 1234));
        Check(bat.Contains("PID eq 1234") && bat.Contains(@"C:\Jogo\data_CrystalExcavator_windows_x86_64\CrystalExcavator.dll") && bat.Contains("start \"\" \"C:\\Jogo\\CrystalExcavator.exe\"") && bat.Contains("\r\n"),
            "script de aplicação espera o jogo fechar, copia os arquivos e reabre");
        Check(!UpdateManager.Supported, "verificação desativada fora do executável Windows");
        System.IO.Directory.Delete(dir, true);
    }

    void TestAudio()
    {
        // feixe: liga e desliga sem erro
        Audio.AudioManager.Instance.SetBeam(true, 0.5f);
        Audio.AudioManager.Instance.SetBeam(false);
        var peaks = Audio.AudioManager.Instance.PeakLevels();
        Check(peaks.Count >= 15, "efeitos sonoros sintetizados");
        foreach (var kv in peaks)
        {
            GD.Print($"      som '{kv.Key}': pico {kv.Value:0.00}");
            Check(kv.Value < 0.99f, $"som '{kv.Key}' satura (pico {kv.Value:0.00})");
            Check(kv.Value > 0.05f, $"som '{kv.Key}' quase inaudível (pico {kv.Value:0.00})");
        }
    }

    // ---------------------------------------------------------------
    async Task TestMiningIntegration()
    {
        Fresh();
        var root = new Node3D();
        AddChild(root);
        root.AddChild(new Effects());
        var shards = new ShardManager();
        root.AddChild(shards);
        var world = new WorldBuilder();
        root.AddChild(world);
        var player = new PlayerController();
        root.AddChild(player);
        player.GlobalPosition = world.SpawnPoint;
        shards.PlayerPosition = () => player.BodyCenter;
        await Frames(5);

        Check(world.Crystals.Count >= 60, $"mundo deve ter cristais suficientes ({world.Crystals.Count})");
        Check(world.Gates.Count == GameData.Regions.Count - 1, "deve existir um portão por região desbloqueável");
        Check(world.Crystals.Any(c => c.Material.Special), "deve existir a formação especial");

        var crystal = world.Crystals.Where(c => c.Material.Id == "quartz" && c.GlobalTransform.Basis.Y.Normalized().Y > 0.95f).OrderBy(c => c.GlobalPosition.DistanceTo(world.SpawnPoint)).First();
        // posiciona o jogador ao lado e olhando para o cristal
        var cpos = crystal.GlobalPosition;
        Face(player, crystal);
        await Frames(4);
        Check(player.TargetCrystal == crystal, "raycast do jogador deve mirar o cristal à frente");

        int hits = 0;
        float hp0 = crystal.Hp;
        while (crystal.IsMineable && hits < 100)
        {
            crystal.Hit(GS.Damage, cpos + Vector3.Up, Vector3.Back);
            hits++;
        }
        int expectedHits = (int)Math.Ceiling(hp0 / GS.Damage);
        Check(hits == expectedHits, $"golpes para quebrar: esperado {expectedHits}, obtido {hits}");
        Check(crystal.State == Crystal.CrystalState.Broken, "cristal deve quebrar");
        Check(shards.ActiveCount > 0, "fragmentos devem surgir");
        await Frames(150);
        Check(GS.InventoryCount > 0, $"fragmentos devem ser coletados pelo ímã (inventário {GS.InventoryCount}, soltos {shards.ActiveCount})");
        Check(GS.Data.CrystalsMined == 1, "estatística de cristais minerados");
        crystal.DebugRespawnNow();
        await Frames(100);
        Check(crystal.State == Crystal.CrystalState.Active && crystal.Hp == crystal.MaxHp, "cristal deve renascer com vida cheia");

        // inventário cheio: fragmentos não são coletados
        GS.AddItem("quartz", 1000);
        int before = GS.InventoryCount;
        shards.SpawnShards(GameData.Mat("amethyst"), player.BodyCenter + Vector3.Forward, 3);
        await Frames(90);
        Check(GS.InventoryCount == before && shards.ActiveCount >= 3, "mochila cheia deve bloquear a coleta");

        // mineração real via entrada simulada
        GS.Data.Inventory.Clear();
        var c2 = crystal;
        Face(player, c2);
        player.SimulateCapturedMouse = true;
        Input.ActionPress("mine");
        float hpBefore = c2.Hp;
        await Frames(60);
        Input.ActionRelease("mine");
        Check(c2.Hp < hpBefore || !c2.IsMineable, $"segurar o botão de mineração deve causar dano (hp {hpBefore} → {c2.Hp})");
        root.QueueFree();
        await Frames(2);
    }

    async Task TestGatesIntegration()
    {
        Fresh();
        var root = new Node3D();
        AddChild(root);
        root.AddChild(new Effects());
        root.AddChild(new ShardManager());
        var world = new WorldBuilder();
        root.AddChild(world);
        await Frames(3);
        var gate = world.Gates[0];
        Check(!gate.IsOpen, "portão começa fechado");
        Check(gate.GetPrompt().Contains("faltam"), "prompt deve indicar quanto falta");
        GS.AddMoney(gate.Region.UnlockCost);
        Check(gate.GetPrompt().Contains("Desbloquear"), "prompt deve oferecer desbloqueio");
        gate.Interact(null);
        await Frames(3);
        Check(gate.IsOpen && GS.IsUnlocked(gate.Region.Id), "interagir deve abrir o portão");
        Check(GS.Data.Money == 0, "desbloqueio deve cobrar");
        // novo jogo deve fechar o portão novamente
        GS.NewGame();
        await Frames(3);
        Check(!gate.IsOpen, "novo jogo deve restaurar o portão bloqueado");
        GS.Data.UnlockedRegions.Add(gate.Region.Id);
        GS.RaiseAll();
        await Frames(3);
        Check(gate.IsOpen, "carregar save com região liberada deve abrir o portão");
        foreach (var rr in GameData.Regions)
        {
            Check(WorldBuilder.RegionAt(new Vector3(0, 0, rr.CenterZ)).Id == rr.Id, $"detecção da região {rr.Name} pelo centro");
            Check(WorldBuilder.RegionAt(new Vector3(0, 0, rr.CenterZ + rr.HalfZ - 1f)).Id == rr.Id, $"detecção da região {rr.Name} na entrada");
        }
        root.QueueFree();
        await Frames(2);
    }

    async Task TestResonance()
    {
        Fresh();
        GS.Data.Upgrades[UpgradeIds.Resonance] = 6;
        var root = new Node3D();
        AddChild(root);
        root.AddChild(new Effects());
        var shards = new ShardManager();
        root.AddChild(shards);
        var world = new WorldBuilder();
        root.AddChild(world);
        var player = new PlayerController();
        root.AddChild(player);
        await Frames(3);
        float radius = GS.ResonanceRadius;
        // cristal resistente com um vizinho dentro do raio
        var target = world.Crystals.Where(c => c.Material.Id == "sapphire")
            .FirstOrDefault(c => world.Crystals.Any(o => o != c && o.GlobalPosition.DistanceTo(c.GlobalPosition) < radius - 0.5f));
        Check(target != null, "deve existir safira com vizinho no raio");
        if (target == null) { root.QueueFree(); return; }
        var neighbors = world.Crystals.Where(o => o != target && o.GlobalPosition.DistanceTo(target.GlobalPosition) < radius - 0.5f).ToList();
        Face(player, target);
        player.SimulateCapturedMouse = true;
        int pulses = 0, hitCount = 0;
        player.ResonancePulse += n => { pulses++; hitCount = n; };
        await Frames(3);
        Check(player.TargetCrystal == target, "deve mirar a safira");
        Input.ActionPress("mine");
        for (int i = 0; i < 60 * 9 && pulses == 0; i++) await Frames(1);
        Input.ActionRelease("mine");
        Check(pulses >= 1, "segurar o ritmo deve disparar um pulso");
        Check(hitCount >= 2, $"pulso deve atingir o alvo e vizinhos (atingiu {hitCount})");
        Check(neighbors.Any(n => n.Hp < n.MaxHp), "vizinho deve receber dano do pulso");
        root.QueueFree();
        await Frames(2);
    }

    async Task TestGamepad()
    {
        Fresh();
        foreach (var a in new[] { "move_forward", "look_left", "mine", "interact", "jump", "sprint", "pause" })
            Check(InputMap.ActionGetEvents(a).Any(e => e is InputEventJoypadButton || e is InputEventJoypadMotion), $"ação '{a}' deve ter mapeamento de controle");

        var root = new Node3D();
        AddChild(root);
        root.AddChild(new Effects());
        var shards = new ShardManager();
        root.AddChild(shards);
        var world = new WorldBuilder();
        root.AddChild(world);
        var player = new PlayerController();
        root.AddChild(player);
        player.SimulateCapturedMouse = true;
        await Frames(3);

        // analógico direito gira a câmera
        var crystal = world.Crystals.First(c => c.Material.Id == "quartz" && c.GlobalTransform.Basis.Y.Normalized().Y > 0.95f);
        Face(player, crystal, 0.6f);
        float yaw0 = player.Yaw;
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = -1f });
        await Frames(20);
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = 0f });
        await Frames(2);
        Check(player.Yaw > yaw0 + 0.3f, $"analógico direito deve girar a câmera (yaw {yaw0:0.00} → {player.Yaw:0.00})");
        Check(InputHints.UsingPad && InputHints.Interact == "[X]", "dicas devem mudar para o controle");

        // gatilho direito minera
        Face(player, crystal);
        await Frames(3);
        float hp0 = crystal.Hp;
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 1f });
        await Frames(60);
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 0f });
        Check(crystal.Hp < hp0 || !crystal.IsMineable, "gatilho direito (RT) deve minerar");

        // analógico esquerdo move
        player.GlobalPosition = world.SpawnPoint;
        await Frames(2);
        var p0 = player.GlobalPosition;
        player.SetLook(0, 0); // do acampamento para o centro da caverna (área sempre livre)
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = -1f });
        await Frames(30);
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = 0f });
        Check(player.GlobalPosition.DistanceTo(p0) > 1f, "analógico esquerdo deve mover o jogador");

        // volta ao teclado
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Godot.Key.Shift, Keycode = Godot.Key.Shift, Pressed = true });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = Godot.Key.Shift, Keycode = Godot.Key.Shift, Pressed = false });
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(!InputHints.UsingPad && InputHints.Interact == "[E]", "dicas voltam ao teclado");
        root.QueueFree();
        await Frames(2);
    }

    /// <summary>Cavernas: tamanho, relevo, caminhos, depósitos alcançáveis (chão e parede), túneis secretos e casa.</summary>
    async Task TestWings()
    {
        Fresh();
        var root = new Node3D();
        AddChild(root);
        root.AddChild(new Effects());
        root.AddChild(new ShardManager());
        var world = new WorldBuilder();
        root.AddChild(world);
        var player = new PlayerController();
        root.AddChild(player);
        player.SimulateCapturedMouse = true;
        await Frames(3);
        float reach = GS.MiningReach + 0.6f;
        foreach (var r in GameData.Regions)
        {
            var L = world.Layouts[r.Index];
            float area = L.ReachableArea();
            Check(area > 2500, $"{r.Id}: área caminhável grande ({area:0} m²)");
            float lo = 99, hi = -99;
            for (int k = 0; k < L.Reach.Length; k++) if (L.Reach[k]) { lo = Mathf.Min(lo, L.F[k]); hi = Mathf.Max(hi, L.F[k]); }
            Check(hi - lo >= 3.5f, $"{r.Id}: o chão tem relevo ({lo:0.0} → {hi:0.0} m)");
            if (r.Index + 1 < GameData.Regions.Count) Check(L.Reachable(L.Exit.X, L.Exit.Z), $"{r.Id}: a saída para a próxima região é alcançável");
            Check(L.Chambers.Count(c => c.Wing != null) == r.Wings.Count, $"{r.Id}: câmaras de minério em destaque criadas");
            Check(L.Secrets.Count >= 1 && L.Crawls.Count == L.Secrets.Count, $"{r.Id}: há câmara secreta com túnel baixo");
            foreach (var s2 in L.Secrets) Check(L.Reachable(s2.A.X, s2.A.Y), $"{r.Id}: câmara secreta alcançável (agachado)");
            var crystals = world.Crystals.Where(c => WorldBuilder.RegionAt(c.GlobalPosition).Index == r.Index && !c.Material.Special).ToList();
            int onWall = crystals.Count(c => c.GlobalTransform.Basis.Y.Normalized().Y < 0.9f);
            Check(crystals.Count >= r.CrystalSlots, $"{r.Id}: depósitos suficientes ({crystals.Count})");
            Check(onWall >= crystals.Count / 6, $"{r.Id}: depósitos nas paredes ({onWall}/{crystals.Count})");
            int bad = 0;
            foreach (var c in crystals)
            {
                var eye = c.StandPoint + Vector3.Up * 1.45f;
                if (!L.Reachable(c.StandPoint.X, c.StandPoint.Z) || eye.DistanceTo(c.Center) > reach + 0.8f) bad++;
            }
            Check(bad == 0, $"{r.Id}: todo depósito tem um ponto alcançável para minerar ({bad} sem)");
            // mira real: raio do olho (no ponto de mineração) até o centro precisa acertar o próprio depósito
            var space = world.GetWorld3D().DirectSpaceState;
            int blocked = 0;
            foreach (var c in crystals)
            {
                var eye = c.StandPoint + Vector3.Up * 1.45f;
                var dirc = (c.Center - eye).Normalized();
                var q = PhysicsRayQueryParameters3D.Create(eye, eye + dirc * reach, PlayerController.WorldLayer | PlayerController.CrystalLayer);
                var hit = space.IntersectRay(q);
                if (hit.Count == 0 || hit["collider"].AsGodotObject() != c)
                {
                    blocked++;
                    if (blocked <= 3) GD.Print($"      sem mira: {c.Material.Id} em {c.GlobalPosition} parede={c.GlobalTransform.Basis.Y.Normalized().Y < 0.9f} dist={eye.DistanceTo(c.Center):0.00} acertou={(hit.Count == 0 ? "nada" : hit["collider"].AsGodotObject())}");
                }
            }
            Check(blocked <= crystals.Count / 50, $"{r.Id}: depósitos miráveis do ponto de mineração ({blocked} bloqueados de {crystals.Count})");
        }

        // depósitos que renascem como outro formato (ex.: pérolas, mais baixas) continuam miráveis
        {
            var space2 = world.GetWorld3D().DirectSpaceState;
            var sample = world.Crystals.Where(c => !c.Material.Special).Where((c, i) => i % 7 == 0).Take(36).ToList();
            var forms = new[] { "voidpearl", "blackpearl", "opal", "coralprism", "obsidian", "magmite" };
            int k = 0;
            foreach (var c in sample)
            {
                var m = GameData.Mat(forms[k++ % forms.Length]);
                c.PickMaterial = _ => m;
                c.Hit(1e9f, c.GlobalPosition, Vector3.Up, true);
                c.DebugRespawnNow();
            }
            await Frames(120);
            int miss = 0;
            foreach (var c in sample)
            {
                var eye = c.StandPoint + Vector3.Up * 1.45f;
                var q = PhysicsRayQueryParameters3D.Create(eye, eye + (c.Center - eye).Normalized() * reach, PlayerController.WorldLayer | PlayerController.CrystalLayer);
                var hit = space2.IntersectRay(q);
                if (hit.Count == 0 || hit["collider"].AsGodotObject() != c) { miss++; if (miss <= 3) GD.Print($"      sem mira após renascer: {c.Material.Id} em {c.GlobalPosition}"); }
            }
            Check(miss <= 1, $"depósitos renascidos com outro formato continuam miráveis ({miss} de {sample.Count} sem mira)");
        }

        // túnel secreto: em pé não passa, agachado passa
        var L1 = world.Layouts[1];
        var crawl = L1.Crawls[0];
        var a = new Vector3(crawl.A.X, 0, crawl.A.Y);
        var b = new Vector3(crawl.B.X, 0, crawl.B.Y);
        var dir = (b - a).Normalized();
        var start = a - dir * 2.5f;
        start.Y = L1.Ground(start.X, start.Z) + 0.1f;
        float yaw = Mathf.Atan2(-dir.X, -dir.Z);
        async Task<float> Walk(bool crouch)
        {
            player.GlobalPosition = start;
            player.Velocity = Vector3.Zero;
            player.SetLook(yaw, 0);
            player.SetCrouch(crouch);
            await Frames(10);
            Input.ActionPress("move_forward");
            await Frames(crouch ? 900 : 400);
            Input.ActionRelease("move_forward");
            return (player.GlobalPosition - start).Dot(dir);
        }
        // trecho de teto baixo ao longo do túnel (medido a partir do ponto de partida)
        float lowStart = -1, lowEnd = -1, total = a.DistanceTo(b);
        for (float t = 0; t <= total; t += 0.25f)
        {
            var q = a + dir * t;
            if (L1.Ceiling(q.X, q.Z) - L1.Ground(q.X, q.Z) < 2.0f) { if (lowStart < 0) lowStart = t; lowEnd = t; }
        }
        Check(lowStart >= 0, "túnel secreto tem teto baixo");
        float standing = await Walk(false);
        Check(standing < lowStart + 2.5f + 0.6f, $"túnel secreto bloqueia quem está em pé (avançou {standing:0.0} m; teto baixo a partir de {lowStart + 2.5f:0.0} m)");
        float crouched = await Walk(true);
        float need = lowEnd + 2.5f + 1f;
        Check(crouched > need, $"agachado atravessa o túnel secreto (avançou {crouched:0.0} de {need:0.0} m)");
        var mid = a + dir * ((lowStart + lowEnd) * 0.5f);
        mid.Y = L1.Ground(mid.X, mid.Z) + 0.1f;
        player.GlobalPosition = mid;
        player.SetCrouch(true);
        await Frames(10);
        player.SetCrouch(false);
        await Frames(30);
        Check(player.Crouched, "dentro do túnel baixo o jogador não consegue levantar");
        player.GlobalPosition = world.SpawnPoint;
        await Frames(30);
        Check(!player.Crouched, "fora do túnel ele levanta");

        // renascimento dinâmico: região esgotada → minérios voltam bem mais rápido
        Check(Mathf.IsEqualApprox(WorldBuilder.RespawnRateFor(0), 1f) && WorldBuilder.RespawnRateFor(1) >= 5.9f, "taxa de renascimento: 1x cheia, ~6x esgotada");
        var depths = world.Crystals.Where(c => WorldBuilder.RegionAt(c.GlobalPosition).Index == 1).ToList();
        foreach (var c in depths) c.Hit(1e9f, c.GlobalPosition, Vector3.Up, true);
        world.UpdateRespawnRates();
        Check(depths.All(c => c.RespawnRate > 5.5f), "Profundezas esgotadas: renascimento acelerado");
        Check(world.Crystals.Where(c => WorldBuilder.RegionAt(c.GlobalPosition).Index == 0).All(c => c.RespawnRate < 1.5f), "outras regiões não são afetadas");
        // casa onde se entra andando
        var house = world.House;
        Check(house != null && house.Bed != null && house.Panel != null && house.ElevatorDown != null, "casa com cama, painel e elevador");
        Check(PlayerHouse.Contains(house.ArrivePoint) && PlayerHouse.Contains(house.WakePoint) && !PlayerHouse.Contains(world.SpawnPoint), "pontos da casa ficam dentro dela");
        player.GlobalPosition = house.ArrivePoint;
        player.Velocity = Vector3.Zero;
        player.SetLook(0, 0);
        await Frames(2);
        Input.ActionPress("move_forward");
        await Frames(240);
        Input.ActionRelease("move_forward");
        Check(PlayerHouse.Contains(player.GlobalPosition), "paredes da casa seguram o jogador");
        Check(world.IsSafeSpot(world.SpawnPoint) && !world.IsSafeSpot(new Vector3(world.Layouts[0].X0 + 1, 0, 0)), "validação de posição salva (dentro da rocha é inválida)");
        root.QueueFree();
        await Frames(2);
    }

    async Task TestDirectCollect()
    {
        Fresh();
        var root = new Node3D();
        AddChild(root);
        var shards = new ShardManager();
        root.AddChild(shards);
        shards.PlayerPosition = () => new Vector3(0, 1, 0);
        var q = GameData.Mat("quartz");
        shards.SpawnShards(q, new Vector3(40, 1, 0), 4);
        await Frames(150);
        Check(GS.InventoryCount == 0, "sem a habilidade, fragmentos a 40 m não vêm sozinhos");
        shards.ClearAll();
        GS.AddMoney(1_000_000);
        GS.TryBuySkill(SkillIds.Magnet);
        GS.TryBuySkill(SkillIds.DirectCollect);
        shards.SpawnShards(q, new Vector3(40, 1, 0), 4);
        for (int i = 0; i < 400 && GS.InventoryCount < 4; i++) await Frames(1);
        Check(GS.InventoryCount == 4, $"Coleta direta: fragmentos a 40 m vão para a mochila ({GS.InventoryCount}/4)");
        root.QueueFree();
        await Frames(2);
        Fresh();
    }

    async Task TestObjectiveMarker()
    {
        Fresh();
        var root = new Node3D();
        AddChild(root);
        root.AddChild(new Effects());
        root.AddChild(new ShardManager());
        var world = new WorldBuilder();
        root.AddChild(world);
        var pos = world.SpawnPoint;
        var curRegion = GameData.Regions[0];
        var marker = new ObjectiveMarker { World = world, PlayerPos = () => pos, CurrentRegion = () => curRegion };
        root.AddChild(marker);
        GS.IsPlaying = true;
        await Frames(40);
        Check(marker.TargetPosition is Vector3 t0 && world.Crystals.Any(c => c.GlobalPosition == t0), "início: marcador aponta para um cristal");
        GS.Data.CrystalsMined = 1;
        GS.AddItem("quartz", 30);
        await Frames(40);
        var sell = world.RegionRoots[0].GetChildren().OfType<SellStation>().First();
        Check(marker.TargetPosition == sell.GlobalPosition, "mochila cheia: marcador aponta para a venda");
        GS.SellAll();
        await Frames(40);
        var shop = world.RegionRoots[0].GetChildren().OfType<ShopTerminal>().First();
        Check(marker.TargetPosition == shop.GlobalPosition, "após vender: marcador aponta para o terminal");
        GS.Data.Upgrades[UpgradeIds.Damage] = 1;
        GS.AddMoney(GameData.Regions[1].UnlockCost);
        await Frames(40);
        Check(marker.TargetPosition == world.Gates[0].GlobalPosition, "com dinheiro: marcador aponta para o portão");
        // estação de venda única e elevador
        int sellCount = world.RegionRoots.Sum(r => r.GetChildren().OfType<SellStation>().Count());
        Check(sellCount == 1 && world.SellStation != null, $"deve haver exatamente 1 Estação de Venda no mapa (há {sellCount})");
        Check(world.Elevator != null && world.Elevator.GetParent() == world.RegionRoots[0], "elevador fica no acampamento");
        Check(world.Elevator.GlobalPosition.DistanceTo(world.SpawnPoint) < 4f, "jogador começa o dia na frente do elevador");
        GS.AddItem("quartz", 3);
        GS.EndDay();
        await Frames(40);
        Check(marker.TargetPosition == sell.GlobalPosition, "fim do turno com minérios: marcador aponta para a venda");
        GS.SellAll();
        await Frames(40);
        Check(marker.TargetPosition == world.Elevator.GlobalPosition, "fim do turno sem minérios: marcador aponta para o elevador");
        curRegion = GameData.Regions[2];
        await Frames(40);
        Check(marker.TargetPosition == world.Gates[1].GlobalPosition, "em região funda: marcador mostra o caminho de volta (portão)");
        curRegion = GameData.Regions[0];
        GS.StartNewDay();
        GS.IsPlaying = false;
        root.QueueFree();
        await Frames(2);
    }

    async Task TestRegionEvents()
    {
        Fresh();
        var root = new Node3D();
        AddChild(root);
        root.AddChild(new Effects());
        root.AddChild(new ShardManager());
        var env = new EnvironmentController();
        root.AddChild(env);
        var world = new WorldBuilder();
        root.AddChild(world);
        RegionDef region = GameData.Regions[1];
        var ev = new RegionEvents { Env = env, World = world, CurrentRegion = () => region };
        root.AddChild(ev);
        await Frames(3);
        float baseDmg = GS.Damage;

        ev.Begin("depths");
        Check(ev.Active != null && GS.EventShardMultiplier == 2f, "Maré Gélida deve dobrar fragmentos");
        var sapph = world.Crystals.First(c => c.Material.Id == "sapphire" || c.Material.Id == "emerald");
        int before = ShardManager.Instance.ActiveCount;
        while (sapph.IsMineable) sapph.Hit(1e6f, sapph.GlobalPosition, Vector3.Up);
        int spawned = ShardManager.Instance.ActiveCount - before;
        // com o evento a quantidade sorteada é dobrada: no mínimo 2x o menor sorteio possível
        int minDoubled = 2 * Math.Max(1, (int)MathF.Round(sapph.Material.ShardsMin * sapph.SizeScale));
        Check(spawned >= minDoubled && spawned % 2 == 0, $"fragmentos com evento ({spawned}) devem ser o dobro do sorteio (mínimo {minDoubled})");
        GS.IsPlaying = true;
        region = GameData.Regions[0]; // jogador sai da região → evento termina
        for (int i = 0; i < 60 && ev.Active != null; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(ev.Active == null && GS.EventShardMultiplier == 1f, "evento deve terminar ao sair da região");

        region = GameData.Regions[2];
        ev.Begin("core");
        Check(Mathf.IsEqualApprox(GS.Damage, baseDmg * 1.5f), "Surto do Núcleo deve dar +50% de dano");
        ev.Stop();
        Check(Mathf.IsEqualApprox(GS.Damage, baseDmg), "dano volta ao normal após o evento");

        var cave = world.Crystals.First(c => c.GetParent() == world.RegionRoots[0]);
        while (cave.IsMineable) cave.Hit(1e6f, cave.GlobalPosition, Vector3.Up);
        Check(cave.State == Crystal.CrystalState.Broken, "cristal da caverna quebrado");
        region = GameData.Regions[0];
        ev.Begin("caves");
        await Frames(100);
        Check(cave.State == Crystal.CrystalState.Active, "Eco Luminoso deve fazer o cristal renascer");
        ev.Stop();
        GS.IsPlaying = false;
        root.QueueFree();
        await Frames(2);
    }

    async Task Frames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    // ---------------------------------------------------------------
    /// <summary>
    /// Simula um jogador eficiente: minera na melhor região liberada, vende quando enche
    /// e compra o upgrade mais barato com retorno. Mede o tempo até cada marco.
    /// </summary>
    void TestBalanceSimulation()
    {
        Fresh();
        var d = GS.Data;
        double t = 0;
        double unlock2 = -1, unlock3 = -1, heart = -1;
        const double walkPerCrystal = 2.2;    // deslocamento entre cristais
        const double sellTrip = 14;           // ida e volta até a estação
        var rng = new Random(5);
        int guard = 0;
        while (t < 6 * 3600 && guard++ < 200000)
        {
            var region = GameData.Regions.Last(r => GS.IsUnlocked(r.Id));
            // escolhe a região que mais rende por segundo entre as liberadas
            double bestRate = 0; RegionDef best = region;
            foreach (var r in GameData.Regions.Where(r => GS.IsUnlocked(r.Id)))
            {
                double rate = RegionRate(r, walkPerCrystal);
                if (rate > bestRate) { bestRate = rate; best = r; }
            }
            var mat = WorldBuilder.PickFor(best, rng);
            double time = mat.Hp / GS.DamagePerSecond + walkPerCrystal;
            int shards = (mat.ShardsMin + mat.ShardsMax) / 2;
            t += time;
            GS.AddItem(mat.Id, shards);
            if (GS.InventoryFull)
            {
                t += sellTrip;
                GS.SellAll();
                // compra regiões quando possível, senão upgrades baratos
                var next = GS.NextLockedRegion();
                if (next != null && GS.CanUnlock(next))
                {
                    GS.TryUnlockRegion(next.Id);
                    if (next.Index == 1) unlock2 = t;
                    if (next.Index == 2) unlock3 = t;
                }
                bool bought = true;
                while (bought)
                {
                    bought = false;
                    var cheapest = UpgradeIds.All.Where(id => !GS.IsMaxed(id) && id != UpgradeIds.Move)
                        .OrderBy(id => GS.UpgradeCost(id)).FirstOrDefault();
                    // guarda dinheiro para a próxima região se estiver perto
                    if (next != null && !GS.IsUnlocked(next.Id) && d.Money > next.UnlockCost * 0.6) break;
                    if (cheapest != null && GS.TryBuyUpgrade(cheapest)) bought = true;
                }
            }
            if (heart < 0 && unlock3 > 0 && GS.DamagePerSecond > 0)
            {
                double heartTime = GameData.Mat("heart").Hp / GS.DamagePerSecond;
                if (heartTime < 60) heart = t;
            }
            if (unlock3 > 0 && heart > 0 && UpgradeIds.All.All(GS.IsMaxed)) break;
        }
        string Fmt(double s) => s < 0 ? "não alcançado" : $"{s / 60:0.0} min";
        GD.Print($"      Região 2 em {Fmt(unlock2)}, Região 3 em {Fmt(unlock3)}, Coração viável em {Fmt(heart)}");
        GD.Print($"      Após {Fmt(t)}: dano {GS.Damage:0}, {GS.SwingsPerSecond:0.0} golpes/s, cap {GS.Capacity}, valor x{GS.ValueMultiplier:0.00}");
        Check(unlock2 > 4 * 60 && unlock2 < 25 * 60, $"Região 2 deveria levar 4–25 min (simulado {Fmt(unlock2)})");
        Check(unlock3 > unlock2 + 10 * 60 && unlock3 < 120 * 60, $"Região 3 deveria levar 10–120 min após a 2 (simulado {Fmt(unlock3)})");
        Fresh();
    }

    double RegionRate(RegionDef r, double walk)
    {
        double total = r.Spawns.Sum(s => s.Weight);
        double rate = 0;
        foreach (var s in r.Spawns)
        {
            var m = GameData.Mat(s.MaterialId);
            double time = m.Hp / GS.DamagePerSecond + walk;
            rate += s.Weight / total * (GS.SellPrice(m) * (m.ShardsMin + m.ShardsMax) / 2.0) / time;
        }
        return rate;
    }
}
