using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrystalExcavator.Core;
using CrystalExcavator.Core.Rendering;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using CrystalExcavator.UI;
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
            Run("Desempenho: padrões de alto FPS, migração e resolução dinâmica", TestFpsSettings);
            Run("Renderizador: perfis, testes, escolha, validade e recuperação", TestRendererSelection);
            Run("Ferramentas: picaretas, durabilidade, quebra e compra", TestTools);
            Run("Conclusão da jornada", TestCompletion);
            Run("Renascimento (prestígio)", TestPrestige);
            Run("Atualização automática", TestUpdater);
            Run("Proteção do save (regressão do bug da atualização)", TestSaveProtection);
            Run("Ciclo do dia (turno, fim do dia, novo dia)", TestDayCycle);
            Run("Goblin minerador: regras", TestGoblinRules);
            Run("Casa e árvore de habilidades", TestHomeAndSkills);
            Run("Áudio sem saturação", TestAudio);
            Run("Idiomas, remapeamento e acessibilidade", TestLocAndBindings);
            Run("Detecção de controle (teclado ↔ controle)", TestPadDetection);
            Run("Regiões de fim de jogo: balanceamento", TestEndgameRegions);
            await RunAsync("Integração: minerar, coletar, renascer", TestMiningIntegration);
            await RunAsync("Integração: portões e estações", TestGatesIntegration);
            await RunAsync("Integração: pulso de ressonância", TestResonance);
            await RunAsync("Integração: eventos de região", TestRegionEvents);
            await RunAsync("Integração: marcador de objetivo", TestObjectiveMarker);
            await RunAsync("Integração: controle (gamepad)", TestGamepad);
            await RunAsync("Menus só com o controle", TestGamepadMenus);
            await RunAsync("Loja: comprar não rola a tela", TestShopScroll);
            await RunAsync("Integração: cavernas, relevo e túneis secretos", TestWings);
            await RunAsync("Integração: coleta direta", TestDirectCollect);
            await RunAsync("Integração: goblin minerador e rebelião", TestGoblins);
            await RunAsync("Integração: construções (poste, esteira, funil, drone, robô)", TestBuildings);
            await RunAsync("Exploração: minérios raros só no fundo das cavernas", TestRarityByDepth);
            await RunAsync("Integração: cordas (poços, subir e descer, corda do jogador)", TestRopes);
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

    /// <summary>--only=texto roda só os testes cujo nome contém o texto (depuração rápida).</summary>
    static readonly string Only = System.Linq.Enumerable.FirstOrDefault(OS.GetCmdlineUserArgs(), a => a.StartsWith("--only="))?.Substring(7);
    static bool Skip(string name) => Only != null && !name.Contains(Only, StringComparison.OrdinalIgnoreCase);

    void Run(string name, Action t)
    {
        if (Skip(name)) return;
        int before = _fail;
        try { t(); }
        catch (Exception e) { Fail($"{name}: exceção {e.Message}\n{e.StackTrace}"); }
        GD.Print((_fail == before ? "[OK]   " : "[FALHA] ") + name);
    }

    async Task RunAsync(string name, Func<Task> t)
    {
        if (Skip(name)) return;
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
        int skillCount = GS.Data.Skills.Count;
        GS.LoadGame();
        Check(GS.Data.House.Values.Sum() == houseSum && skillCount == HomeData.Skills.Count(sk => sk.Branch < 4) && GS.Data.Skills.Count == skillCount, "casa e habilidades são salvas");
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
        const string id = "caves";
        Check(GoblinData.All.Count == GameData.Regions.Count && GameData.Regions.All(r => GoblinData.Of(r.Id) != null), "um goblin por região");
        for (int i = 1; i < GoblinData.All.Count; i++)
            Check(GoblinData.All[i].HireCost > GoblinData.All[i - 1].HireCost && GoblinData.All[i].MineDps > GoblinData.All[i - 1].MineDps, $"{GoblinData.All[i].Name}: mais caro e mais forte que o anterior");
        GS.AddMoney(1000);
        Check(!GS.GoblinFound(id) && !GS.TryHireGoblin(id), "sem encontrar o goblin não dá para contratar");
        Check(GS.MarkGoblinFound(id) && !GS.MarkGoblinFound(id), "encontrar marca uma vez só");
        Check(GS.TryHireGoblin(id) && GS.Goblin(id) == GameState.GoblinMood.Working && d.Money == 500, "contratar o Grub custa ◈ 500");
        Check(GS.Goblin("depths") == GameState.GoblinMood.Available && !GS.TryHireGoblin("depths"), "cada goblin é independente");
        // primeiro ele monta a cabana: não trabalha nem cobra
        Check(!GS.GoblinBuilt(id) && !GS.GoblinOnShift(id) && GS.GoblinFee(id) == 0 && GS.DueGoblins().Count == 0, "recém-contratado: montando a cabana, sem trabalhar nem cobrar");
        Check(!GS.TryUpgradeGoblin(id, GameState.GoblinUpgrade.Hut), "sem cabana pronta não dá para melhorar");
        d.DayTimeSeconds = GS.DayLength * 0.9;
        Check(!GS.GoblinBuilt(id) && GS.GoblinBuildProgress(id) > 0.5f, $"no fim do 1º dia ainda está montando ({GS.GoblinBuildProgress(id) * 100:0}%)");
        GS.StartNewDay();
        d.DayTimeSeconds = GS.DayLength * 0.55;
        Check(GS.GoblinBuilt(id) && GS.GoblinData(id).HutBuilt, "no meio do 2º dia a cabana fica pronta");
        d.DayTimeSeconds = GS.DayLength * 0.1;
        Check(GS.GoblinOnShift(id), "trabalha no começo do turno");
        d.DayTimeSeconds = GS.DayLength * 0.5;
        Check(!GS.GoblinOnShift(id) && Math.Abs(GS.GoblinWorkFraction(id) - 0.3f) < 0.01f, "cabana básica: trabalha só 30% do turno (3 de 10 min)");
        // melhorias
        GS.AddMoney(1_000_000);
        long c0 = GS.GoblinUpgradeCost(id, GameState.GoblinUpgrade.Hut);
        float dps0 = GS.GoblinDps(id); int carry0 = GS.GoblinCarryMax(id);
        d.DayTimeSeconds = GS.DayLength * 0.4;
        Check(!GS.GoblinOnShift(id) && GS.TryUpgradeGoblin(id, GameState.GoblinUpgrade.Hut) && GS.GoblinWorkFraction(id) > 0.3f && GS.GoblinOnShift(id), "melhorar a cabana aumenta o tempo de trabalho");
        Check(GS.GoblinUpgradeCost(id, GameState.GoblinUpgrade.Hut) > c0, "a próxima melhoria custa mais");
        Check(GS.TryUpgradeGoblin(id, GameState.GoblinUpgrade.Speed) && GS.GoblinDps(id) > dps0, "picareta melhor: quebra mais rápido");
        Check(GS.TryUpgradeGoblin(id, GameState.GoblinUpgrade.Carry) && GS.GoblinCarryMax(id) > carry0, "mochila melhor: carrega mais");
        for (int k = 0; k < 6; k++) GS.TryUpgradeGoblin(id, GameState.GoblinUpgrade.Hut);
        Check(GS.GoblinData(id).HutLevel == GameState.GoblinMaxLevel && Math.Abs(GS.GoblinWorkFraction(id) - 1f) < 0.01f && GS.GoblinUpgradeCost(id, GameState.GoblinUpgrade.Hut) == long.MaxValue, "cabana no máximo: trabalha o turno inteiro");
        var json = System.Text.Json.JsonSerializer.Serialize(GS.Data);
        Check(SaveSystem.TryParse(json, out var dd) && dd.Goblins[id].HutBuilt && dd.Goblins[id].HutLevel == GameState.GoblinMaxLevel && dd.Goblins[id].SpeedLevel == 1, "cabana e melhorias salvas");
        d.DayTimeSeconds = 0;
        d.Money = 500;
        var q = GameData.Mat("quartz");
        GS.GoblinMined(id, q, 40);
        long val = GS.SellPrice(q) * 40;
        Check(GS.GoblinStashCount(id) == 40 && GS.GoblinData(id).DayValue == val, "o que ele minera vai para o baú dele");
        Check(GS.GoblinFee(id) == Math.Max(GoblinData.Of(id).MinFee, val / 4), "taxa = 1/4 do dia (com mínimo)");
        int cap = GS.FreeSpace;
        int got = GS.CollectGoblinStash(id);
        Check(got == Math.Min(40, cap) && GS.InventoryCount == got, "pegar do baú enche a mochila");
        long money = d.Money, fee = GS.GoblinFee(id);
        Check(GS.DueGoblins().Count == 1 && GS.SettleGoblinFee(id, true) == GameState.FeeOutcome.Paid && d.Money == money - fee && GS.DueGoblins().Count == 0, "pagar a taxa");
        Check(GS.SettleGoblinFee(id, true) == GameState.FeeOutcome.NoFee, "só cobra uma vez por dia");
        GS.StartNewDay();
        Check(GS.SettleGoblinFee(id, false) == GameState.FeeOutcome.Forgiven, "1ª falta: perdoa");
        GS.StartNewDay();
        Check(GS.SettleGoblinFee(id, false) == GameState.FeeOutcome.Warned, "2ª falta: avisa");
        GS.StartNewDay();
        Check(GS.SettleGoblinFee(id, false, forceRebel: true) == GameState.FeeOutcome.Rebel && GS.Goblin(id) == GameState.GoblinMood.Rebel, "3ª falta: rebelião");
        GS.EndGoblinRebellion(id, true);
        Check(GS.Goblin(id) == GameState.GoblinMood.Working && GS.GoblinData(id).Strikes == 0, "rebelião vencida: volta ao trabalho");
        for (int i = 0; i < 2; i++) { GS.StartNewDay(); GS.SettleGoblinFee(id, false); }
        GS.StartNewDay();
        Check(GS.SettleGoblinFee(id, false, forceRebel: false) == GameState.FeeOutcome.Fled && GS.Goblin(id) == GameState.GoblinMood.Fled, "ou ele foge");
        Check(!GS.CanRehireGoblin(id) && !GS.TryHireGoblin(id), "não volta logo depois de fugir");
        for (int i = 0; i < GameState.GoblinRehireDays; i++) GS.StartNewDay();
        GS.AddMoney(5000);
        long before = d.Money;
        Check(GS.CanRehireGoblin(id) && GS.TryHireGoblin(id) && before - d.Money == GoblinData.Of(id).RehireCost, "volta dias depois, mais caro");
        // goblin de outra região: mais caro
        GS.MarkGoblinFound("depths");
        GS.AddMoney(10000);
        long m0 = d.Money;
        Check(GS.TryHireGoblin("depths") && m0 - d.Money == GoblinData.Of("depths").HireCost, "o goblin das Profundezas custa mais");
        GS.Data.Inventory.Clear();
        GS.AddItem("quartz", 20);
        int stolen = GS.StealFromInventory(0.25f);
        Check(stolen == 5 && GS.InventoryCount == 15, "desmaio: goblins levam 25% da mochila");
        GS.SaveGame(true);
        GS.LoadGame();
        Check(GS.Goblin(id) == GameState.GoblinMood.Working && GS.Goblin("depths") == GameState.GoblinMood.Working, "estado dos goblins é salvo");
        // save antigo (v0.3.x): um único goblin nos campos antigos → migra para as Cavernas, já encontrado
        var old = new SaveData { GoblinState = 1, GoblinStrikes = 1, GoblinStash = new() { ["quartz"] = 7 } };
        old.Sanitize();
        Check(old.Goblins.TryGetValue("caves", out var mg) && mg.State == 1 && mg.Found && mg.Stash["quartz"] == 7 && old.GoblinState == 0, "migração do Grub de saves antigos");
        var bad = new SaveData { Goblins = new() { ["caves"] = new GoblinSave { State = 9, Strikes = -3, Stash = new() { ["nada"] = 4 } }, ["xx"] = new GoblinSave() } };
        bad.Sanitize();
        Check(bad.Goblins["caves"].State == 3 && bad.Goblins["caves"].Strikes == 0 && bad.Goblins["caves"].Stash.Count == 0 && !bad.Goblins.ContainsKey("xx"), "dados dos goblins saneados");
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
        Check(world.GoblinCamps.Count == GameData.Regions.Count, $"um acampamento de goblin por região ({world.GoblinCamps.Count})");
        foreach (var c in world.GoblinCamps)
        {
            var Lc = world.Layouts[c.RegionIndex];
            var secret = Lc.SecretAt(c.HideoutPos + Vector3.Up * 0.1f);
            Check(secret != null, $"{c.Def.Name} se esconde numa câmara secreta");
            Check(Lc.Reachable(c.HideoutPos.X, c.HideoutPos.Z), $"esconderijo de {c.Def.Name} é alcançável (agachado)");
            Check(c.Grub.Visible && !c.Chest.Visible, $"{c.Def.Name}: acampamento escondido até ser contratado");
            Check(c.Grub.GlobalPosition.DistanceTo(c.HideoutPos) < 0.5f, $"{c.Def.Name} começa no esconderijo");
        }
        var camp = world.Goblins;
        camp.PlayerPos = () => player.GlobalPosition;
        camp.DamagePlayer = (dmg, from) => player.TakeDamage(dmg, from);
        camp.PlayerInMine = () => true;
        GS.IsPlaying = true;
        // longe não descobre; perto do esconderijo descobre
        await Frames(40);
        Check(!GS.GoblinFound("caves"), "não é descoberto de longe");
        player.GlobalPosition = camp.HideoutPos + new Vector3(2f, 0.1f, 0);
        bool found = false;
        camp.Discovered += _ => found = true;
        for (int i = 0; i < 60 && !found; i++) await Frames(1);
        Check(found && GS.GoblinFound("caves"), "chegar perto do esconderijo descobre o goblin");
        GS.AddMoney(1000);
        camp.TalkToGrub();
        await Frames(3);
        Check(GS.Goblin("caves") == GameState.GoblinMood.Working && !camp.Chest.Visible && !GS.GoblinBuilt("caves"), "contratado: começa a montar a cabana (sem baú ainda)");
        await Frames(40);
        Check(camp.Grub.State == GoblinNpc.Mode.Building && camp.Grub.GlobalPosition.DistanceTo(camp.HideoutPos) > 3f, "vai até o terreno e trabalha na cabana");
        // a montagem termina com o passar do tempo (simulado: meio do dia seguinte)
        GS.StartNewDay();
        GS.Data.DayTimeSeconds = GS.DayLength * 0.55;
        await Frames(40);
        GS.Data.DayTimeSeconds = 0;
        await Frames(40);
        Check(GS.GoblinBuilt("caves") && camp.Chest.Visible, "cabana pronta: aparece o baú");
        Engine.TimeScale = 6;
        var grubStart = camp.Grub.GlobalPosition;
        for (int i = 0; i < 1800 && GS.GoblinStashCount("caves") == 0; i++) await Frames(1);
        Check(camp.Grub.GlobalPosition.DistanceTo(grubStart) > 1f, "Grub anda até um depósito");
        Check(GS.GoblinStashCount("caves") > 0 && GS.GoblinData("caves").DayValue > 0, $"Grub minera e guarda no baú ({GS.GoblinStashCount("caves")} fragmentos)");
        Check(GS.Data.CrystalsMined == 0, "o que o goblin quebra não conta como do jogador");
        Engine.TimeScale = 1;

        // rebelião: perseguem e atacam; o feixe atordoa
        GS.GoblinData("caves").State = (int)GameState.GoblinMood.Rebel;
        camp.StartRebellion();
        Check(camp.Rebels.Count == 3 && camp.Rebels.All(g => g.Hostile), "rebelião: goblin e 2 comparsas hostis");
        player.GlobalPosition = world.Layouts[0].OnGround(camp.GrubHome.X - 4f, camp.GrubHome.Z, 0.1f);
        float hp0 = player.Hp;
        for (int i = 0; i < 600 && player.Hp >= hp0; i++) await Frames(1);
        Check(player.Hp < hp0, $"goblins rebelados atacam o jogador (vida {player.Hp:0})");
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
        Check(won && GS.Goblin("caves") == GameState.GoblinMood.Working && camp.Rebels.Count == 0 && !camp.Grub.Hostile, "todos atordoados: rebelião vencida, volta ao trabalho");
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
        GS.DebugSetTool(5);
        Check(GS.Damage > d0 * 2, "dano deve crescer significativamente (ferramenta melhor)");
        Check(GS.SwingsPerSecond > s0 * 1.4f, "velocidade de golpe deve crescer (ferramenta melhor)");
        Check(GS.MoveSpeed > m0, "velocidade de movimento deve crescer");
        Check(GS.MagnetRadius > r0 && GS.MiningReach > reach0, "alcance deve crescer");
        Check(GS.Capacity > c0, "capacidade deve crescer");
        Check(GS.ValueMultiplier > v0, "valor deve crescer");
        // quartzo: vários segundos no início, quase instantâneo depois
        var q = GameData.Mat("quartz");
        Fresh();
        float tStart = q.Hp / GS.DamagePerSecond;
        foreach (var id in UpgradeIds.All) GS.Data.Upgrades[id] = 12;
        GS.DebugSetTool(7);
        float tLate = q.Hp / GS.DamagePerSecond;
        GD.Print($"      quartzo: {tStart:0.00}s com a picareta de madeira, {tLate:0.000}s com a de rubi");
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

    void TestLocAndBindings()
    {
        var d = Settings.Data;
        string lang = d.Language;
        // traduções: mesmo número de marcadores {n} em todos os idiomas
        var rx = new System.Text.RegularExpressions.Regex(@"\{(\d+)[^}]*\}");
        string Marks(string t) => string.Join(",", rx.Matches(t).Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x));
        int bad = 0;
        foreach (var kv in LocTables.En)
        {
            string es = LocTables.Es[kv.Key];
            if (Marks(kv.Key) != Marks(kv.Value) || Marks(kv.Key) != Marks(es)) { bad++; if (bad < 4) Fail("marcadores diferentes: " + kv.Key); }
        }
        Check(bad == 0 && LocTables.En.Count > 400, $"tabelas de tradução consistentes ({LocTables.En.Count} textos)");
        d.Language = "en";
        Check(Loc.T("Novo jogo") == "New game" && Loc.F("DIA {0}", 3) == "DAY 3", "inglês traduz textos e modelos");
        Check(GameData.Regions[0].Name != "" && GameData.Regions[0].Name != GameData.Regions[0].NamePt, "nomes de regiões traduzidos");
        d.Language = "es";
        Check(Loc.T("Novo jogo") == "Nueva partida", "espanhol traduz");
        Check(Loc.T("texto sem tradução xyz") == "texto sem tradução xyz", "texto sem tradução volta em português");
        d.Language = "pt";
        Check(Loc.T("Novo jogo") == "Novo jogo", "português usa o texto original");
        d.Language = "xx"; d.Sanitize();
        Check(d.Language == "pt", "idioma inválido volta para português");
        d.Language = lang;
        // remapeamento
        InputSetup.BindKey("jump", new InputEventKey { PhysicalKeycode = Key.J });
        Check(InputMap.ActionGetEvents("jump").Any(e => e is InputEventKey k && k.PhysicalKeycode == Key.J)
              && !InputMap.ActionGetEvents("jump").Any(e => e is InputEventKey k && k.PhysicalKeycode == Key.Space), "tecla remapeada substitui a padrão");
        Check(InputMap.ActionGetEvents("jump").Any(e => e is InputEventJoypadButton), "remapear teclado mantém o botão do controle");
        InputSetup.BindKey("mine", new InputEventMouseButton { ButtonIndex = MouseButton.Right });
        Check(InputMap.ActionGetEvents("mine").Any(e => e is InputEventMouseButton m && m.ButtonIndex == MouseButton.Right), "ação pode usar botão do mouse");
        InputSetup.BindPad("jump", JoyButton.Y);
        Check(InputMap.ActionGetEvents("jump").Any(e => e is InputEventJoypadButton b && b.ButtonIndex == JoyButton.Y)
              && !InputMap.ActionGetEvents("jump").Any(e => e is InputEventJoypadButton b && b.ButtonIndex == JoyButton.A), "botão do controle remapeado");
        var json = System.Text.Json.JsonSerializer.Serialize(d);
        var back = System.Text.Json.JsonSerializer.Deserialize<SettingsData>(json);
        Check(back.KeyBinds.ContainsKey("jump") && back.PadBinds["jump"][0] == (int)JoyButton.Y, "remapeamento salvo nas configurações");
        InputSetup.ResetBindings();
        Check(InputMap.ActionGetEvents("jump").Any(e => e is InputEventKey k && k.PhysicalKeycode == Key.Space)
              && InputMap.ActionGetEvents("jump").Any(e => e is InputEventJoypadButton b && b.ButtonIndex == JoyButton.A)
              && d.KeyBinds.Count == 0, "restaurar padrões");
        // acessibilidade
        d.Colorblind = 2; Accessibility.Apply();
        var layer = GetTree().Root.GetNodeOrNull<CanvasLayer>("ColorblindFilter");
        d.Colorblind = 0; Accessibility.Apply();
        Check(true, "filtro de daltonismo aplicado sem erro");
        d.Narration = true; Accessibility.Speak("teste"); d.Narration = false;
        Check(true, "narração não falha sem voz no sistema");
    }

    void TestEndgameRegions()
    {
        Check(GameData.Regions.Count >= 6 && GameData.JourneyRegions == 4, $"6 regiões, jornada = 4 primeiras ({GameData.Regions.Count})");
        // jornada concluída sem as regiões novas
        Fresh();
        for (int i = 0; i < GameData.JourneyRegions; i++) if (!GS.IsUnlocked(GameData.Regions[i].Id)) GS.Data.UnlockedRegions.Add(GameData.Regions[i].Id);
        GS.AddItem("heart", 1);
        Check(GS.JourneyComplete, "jornada conclui sem as regiões de fim de jogo");
        var dmg = GameData.Upgrades[UpgradeIds.Damage];
        float maxDmg = dmg.ValueAt(dmg.MaxLevel), midDmg = dmg.ValueAt(20);
        for (int i = GameData.JourneyRegions; i < GameData.Regions.Count; i++)
        {
            var r = GameData.Regions[i];
            Check(r.UnlockCost > GameData.Regions[i - 1].UnlockCost, $"{r.NamePt} custa mais que a anterior");
            Check(GoblinData.Of(r.Id) != null, $"{r.NamePt} tem goblin");
            Check(RegionEvents.For(r.Id) != null, $"{r.NamePt} tem evento");
            float total = r.Spawns.Sum(sp => sp.Weight);
            Check(Math.Abs(total - 1f) < 0.02f, $"{r.NamePt}: chances de minério somam 1 ({total:0.00})");
            foreach (var sp in r.Spawns.Select(x => GameData.Mat(x.MaterialId)).Append(GameData.Mat(r.RareMaterialId)))
            {
                float hitsMax = sp.Hp / maxDmg, hitsMid = sp.Hp / midDmg;
                Check(hitsMax <= 25f && hitsMid >= 3f, $"{sp.NamePt}: {hitsMid:0} golpes no nível 20, {hitsMax:0.0} no máximo");
                Check(sp.Value / (float)sp.Hp is > 0.05f and < 0.4f, $"{sp.NamePt}: valor por resistência coerente");
            }
        }
        Fresh();
    }

    void TestPadDetection()
    {
        // começa no teclado
        InputHints.Observe(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true });
        Check(!InputHints.UsingPad, "tecla: modo teclado");
        // gatilho solto que fica em -1 (comum no Windows) não pode trocar para controle
        for (int i = 0; i < 20; i++) InputHints.Observe(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.TriggerLeft, AxisValue = -1f });
        Check(!InputHints.UsingPad, "gatilho parado em -1 não troca as dicas");
        // apertar o gatilho de verdade troca
        InputHints.Observe(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.TriggerLeft, AxisValue = 0.8f });
        Check(InputHints.UsingPad, "apertar o gatilho troca para controle");
        // um esbarrão no mouse não volta para teclado
        InputHints.Observe(new InputEventMouseMotion { Relative = new Vector2(3, 2) });
        InputHints.Observe(new InputEventMouseMotion { Relative = new Vector2(2, 1) });
        Check(InputHints.UsingPad, "esbarrão no mouse não troca para teclado");
        // movimento contínuo do mouse troca
        for (int i = 0; i < 6; i++) InputHints.Observe(new InputEventMouseMotion { Relative = new Vector2(6, 3) });
        Check(!InputHints.UsingPad, "mexer o mouse de verdade volta para teclado");
        // analógico com leve folga (drift) não troca; empurrado troca
        InputHints.Observe(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0.18f });
        Check(!InputHints.UsingPad, "folga do analógico não troca");
        InputHints.Observe(new InputEventJoypadMotion { Device = 3, Axis = JoyAxis.LeftX, AxisValue = 0.9f });
        Check(InputHints.UsingPad, "analógico empurrado troca para controle");
        // soltar um botão não conta (só apertar)
        InputHints.Observe(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true });
        InputHints.Observe(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = false });
        Check(!InputHints.UsingPad, "soltar botão do controle não troca");
        // conectar o controle troca na hora; desconectar (sem outros) volta para teclado
        bool? ev = null;
        void H(bool c, string n) => ev = c;
        InputHints.PadConnection += H;
        InputHints.OnJoyConnection(5, true);
        Check(InputHints.UsingPad && ev == true, "conectar controle: dicas de controle na hora");
        InputHints.OnJoyConnection(5, false);
        Check(!InputHints.UsingPad && ev == false, "desconectar: volta para teclado e avisa");
        InputHints.PadConnection -= H;
    }

    void TestTools()
    {
        Fresh();
        GS.NewGame();
        GS.IsPlaying = true;
        GS.Data.DayOver = false;
        Check(GS.Data.Tool == 0 && GS.CurrentTool.Id == "wood" && !GS.ToolBroken, "começa com a picareta de madeira");
        Check(GS.Damage <= 4f, $"picareta de madeira tem dano muito baixo ({GS.Damage})");
        Check(!UpgradeIds.All.Contains(UpgradeIds.Damage) && !UpgradeIds.All.Contains(UpgradeIds.Speed), "dano e velocidade saíram da loja de melhorias");
        // ordem: cada ferramenta é melhor que a anterior; lasers só no fim
        bool increasing = true;
        for (int i = 1; i < ToolData.All.Count; i++)
            increasing &= ToolData.All[i].DamagePerSecond > ToolData.All[i - 1].DamagePerSecond && ToolData.All[i].Cost > ToolData.All[i - 1].Cost && ToolData.All[i].Region >= ToolData.All[i - 1].Region;
        Check(increasing, "cada ferramenta é melhor, mais cara e de região igual ou mais funda");
        Check(ToolData.All.Where(t => t.Kind == ToolKind.Laser).All(t => t.Region >= GameData.Regions.Count - 1), "armas laser só no final");
        Check(ToolData.All.Skip(1).All(t => t.Ore != null && GameData.Mat(t.Ore) != null), "ferramentas feitas dos minérios do jogo");

        // região bloqueada
        int silver = ToolData.All.FindIndex(t => t.Id == "silver");
        GS.AddMoney(10_000_000);
        Check(!GS.ToolAvailable(silver) && !GS.TryBuyTool(silver), "picareta de prata exige as Profundezas liberadas");
        GS.Data.UnlockedRegions.Add("depths");
        Check(GS.ToolAvailable(silver), "liberou a região: dá para comprar");

        // compra e desgaste
        long m0 = GS.Data.Money;
        Check(GS.TryBuyTool(1) && GS.Data.Tool == 1 && GS.Data.Money == m0 - ToolData.All[1].Cost, "compra a picareta de cobre pelo preço");
        float dmgCopper = GS.Damage;
        int dur = GS.CurrentTool.Durability;
        GS.WearTool(dur - 1);
        Check(!GS.ToolBroken && GS.ToolDurabilityLeft == 1, "desgasta a cada golpe");
        GS.WearTool();
        Check(GS.ToolBroken && GS.ToolDurabilityLeft == 0, "no último golpe a ferramenta quebra");
        Check(GS.Damage < dmgCopper && Mathf.IsEqualApprox(GS.BaseDamage, ToolData.HandsDamage), "quebrada: minera com as mãos (dano mínimo)");
        Check(GS.ToolLabel.Contains(Loc.T("quebrada").Trim('(', ')')) || GS.ToolLabel.Contains("quebrada"), "nome mostra que quebrou");
        // compra outra (a mesma)
        Check(GS.TryBuyTool(1) && !GS.ToolBroken && GS.ToolDurabilityLeft == dur, "compra outra igual: nova, durabilidade cheia");
        // turno encerrado: não compra
        GS.Data.DayOver = true;
        Check(!GS.TryBuyTool(2), "fora do turno não compra");
        GS.Data.DayOver = false;
        // salva e carrega
        GS.WearTool(10);
        var json = System.Text.Json.JsonSerializer.Serialize(GS.Data);
        Check(SaveSystem.TryParse(json, out var back) && back.Tool == 1 && back.ToolWear == 10 && !back.ToolBroken, "ferramenta e desgaste salvos");

        // save antigo: dano vinha da melhoria → recebe a ferramenta equivalente
        var old = new SaveData();
        old.UnlockedRegions.Add("depths");
        old.Upgrades[UpgradeIds.Damage] = 12; // ~ 4·1,32^12 + 24 ≈ 87 de dano
        GS.UseDataForTests(old);
        GS.MigrateTools();
        Check(old.ToolsMigrated && ToolData.All[old.Tool].Damage <= GameData.Upgrades[UpgradeIds.Damage].ValueAt(12) * 1.05f && old.Tool >= 3, $"save antigo recebe a ferramenta equivalente ({ToolData.All[old.Tool].NamePt})");
        int before = old.Tool;
        GS.MigrateTools();
        Check(old.Tool == before, "conversão só acontece uma vez");
        // laser: não desgasta, gasta células de energia
        Fresh();
        GS.NewGame();
        GS.IsPlaying = true;
        GS.Data.DayOver = false;
        foreach (var r in GameData.Regions) if (!GS.IsUnlocked(r.Id)) GS.Data.UnlockedRegions.Add(r.Id);
        int laser = ToolData.All.FindIndex(t => t.Kind == ToolKind.Laser);
        GS.AddMoney(ToolData.All[laser].Cost + ToolData.AmmoPackCost * 2);
        Check(GS.TryBuyTool(laser) && GS.IsLaser && GS.Data.Ammo == ToolData.AmmoWithLaser, "laser novo vem com algumas células");
        float laserDmg = GS.Damage;
        GS.WearTool(10);
        Check(GS.Data.Ammo == ToolData.AmmoWithLaser - 10 && GS.Data.ToolWear == 0 && !GS.ToolBroken, "cada disparo gasta 1 célula (o laser não desgasta)");
        GS.WearTool(GS.Data.Ammo);
        Check(GS.OutOfAmmo && GS.UsingHands && GS.Damage < laserDmg && Mathf.IsEqualApprox(GS.BaseDamage, ToolData.HandsDamage), "sem munição: minera com as mãos (não é infinito)");
        Check(GS.ToolLabel.Contains(Loc.T("sem munição")) || GS.ToolLabel.Contains("sem munição"), "nome mostra que está sem munição");
        long mA = GS.Data.Money;
        Check(GS.TryBuyAmmo() && GS.Data.Ammo == ToolData.AmmoPack && GS.Data.Money == mA - ToolData.AmmoPackCost && !GS.OutOfAmmo && Mathf.IsEqualApprox(GS.Damage, laserDmg), "compra pacote de células e volta a disparar");
        Fresh();
        GS.NewGame();
        GS.AddMoney(ToolData.AmmoPackCost * 2);
        Check(!GS.AnyLaserAvailable && !GS.TryBuyAmmo(), "munição só aparece quando há laser liberado");
        // renascimento volta à madeira
        Fresh();
        GS.DebugSetTool(6);
        GS.Data.GameCompleted = true;
        GS.Data.RunEarned = 10_000_000;
        GS.Prestige();
        Check(GS.Data.Tool == 0 && !GS.ToolBroken, "renascimento recomeça com a picareta de madeira");
        Fresh();
    }

    void TestRendererSelection()
    {
        // ---- os 9 perfis (3 renderizadores × Baixa/Média/Alta)
        foreach (var m in RenderMethods.Preference)
            foreach (var q in new[] { GraphicsQuality.Low, GraphicsQuality.Medium, GraphicsQuality.High })
            {
                var f = RenderFeatures.For(m, q);
                Check(f.Method == m && f.Quality == q && f.Glow, $"perfil {m}/{q} existe e mantém o brilho (identidade visual)");
                if (m != RenderMethods.ForwardPlus)
                    Check(!f.Ssao && !f.Ssr && !f.Ssil && !f.VolumetricFog && !f.Fsr2, $"{m}/{q} não liga efeitos que esse renderizador não tem");
                if (m == RenderMethods.Compatibility)
                    Check(!f.Smaa && !f.Fsr1 && (q == GraphicsQuality.Low || f.Msaa != Viewport.Msaa.Disabled), $"{m}/{q}: sem SMAA/FSR, usa MSAA como alternativa");
            }
        int Cost(RenderFeatures f) => (f.Ssao ? 1 : 0) + (f.Ssr ? 1 : 0) + (f.Ssil ? 1 : 0) + (f.VolumetricFog ? 1 : 0) + (f.LampShadows ? 1 : 0);
        foreach (var m in RenderMethods.Preference)
            Check(Cost(RenderFeatures.For(m, GraphicsQuality.Low)) <= Cost(RenderFeatures.For(m, GraphicsQuality.Medium)) && Cost(RenderFeatures.For(m, GraphicsQuality.Medium)) <= Cost(RenderFeatures.For(m, GraphicsQuality.High)), $"{m}: Baixa ≤ Média ≤ Alta em custo");
        var fh = RenderFeatures.For(RenderMethods.ForwardPlus, GraphicsQuality.High);
        Check(fh.Ssao && fh.Ssr && fh.Ssil && fh.VolumetricFog && fh.LampShadows, "Forward+ Alta usa os recursos avançados");

        // ---- classificação
        ProbeResult R(string m, double fps, double p99, double fl = 0, double pl = 0, bool rendered = true, string actual = null, int crit = 0, bool started = true)
        {
            var r = new ProbeResult { Method = m, ActualMethod = actual ?? m, Started = started, Rendered = rendered, FpsMedium = fps, P99MsMedium = p99, FpsLow = fl, P99MsLow = pl, CriticalErrors = crit };
            r.Status = RendererSelector.Classify(r);
            return r;
        }
        Check(R("forward_plus", 140, 12).Status == ProbeStatus.Approved, "rápido e estável: APROVADO");
        Check(R("forward_plus", 60, 40).Status == ProbeStatus.Limited, "média boa mas com travadas (p99 alto): LIMITADO");
        Check(R("forward_plus", 35, 30).Status == ProbeStatus.Limited, "35 FPS: LIMITADO");
        Check(R("forward_plus", 140, 12, actual: "gl_compatibility").Status == ProbeStatus.Incompatible, "motor caiu para outro renderizador: INCOMPATÍVEL (não basta ter Vulkan)");
        Check(R("mobile", 140, 12, rendered: false).Status == ProbeStatus.Incompatible, "imagem vazia/preta: INCOMPATÍVEL");
        Check(R("mobile", 140, 12, crit: 1).Status == ProbeStatus.Incompatible, "erro de shader/driver: INCOMPATÍVEL");
        Check(R("mobile", 0, 0, started: false).Status == ProbeStatus.Incompatible, "não iniciou: INCOMPATÍVEL");

        // ---- ordem dos testes: para no primeiro preferido aprovado
        var done = new List<ProbeResult>();
        Check(RendererSelector.NextToProbe(done) == "forward_plus", "primeiro testa Forward+");
        done.Add(R("forward_plus", 140, 12));
        Check(RendererSelector.NextToProbe(done) == null, "Forward+ aprovado: termina na hora (sem testar os outros)");
        done = new List<ProbeResult> { R("forward_plus", 35, 30) };
        Check(RendererSelector.NextToProbe(done) == "mobile", "Forward+ limitado: testa Mobile");
        done.Add(R("mobile", 60, 20));
        Check(RendererSelector.NextToProbe(done) == null, "Mobile aprovado: termina");
        Check(RendererSelector.NextToProbe(new List<ProbeResult>(), new[] { "forward_plus" }) == "mobile", "renderizador que falhou antes não é testado de novo");

        // ---- escolha
        var d1 = RendererSelector.Choose(new List<ProbeResult> { R("forward_plus", 35, 30), R("mobile", 60, 20) });
        Check(d1.Method == "mobile" && d1.Quality == 1 && !d1.Recovery, "exemplo do pedido: Forward+ 35 FPS e Mobile 60 FPS → Mobile");
        var d2 = RendererSelector.Choose(new List<ProbeResult> { R("forward_plus", 136, 10), R("mobile", 200, 8) });
        Check(d2.Method == "forward_plus" && d2.Quality == 2, "ambos aprovados: Forward+ (Alta com folga, como a RTX 3050 a ~136 FPS)");
        var d3 = RendererSelector.Choose(new List<ProbeResult> { R("forward_plus", 34, 40, 50, 30), R("mobile", 40, 30, 58, 25), R("gl_compatibility", 62, 22) });
        Check(d3.Method == "gl_compatibility" && d3.Quality == 1, "integrada (~35 FPS no Forward+): Compatibility na Média, que segura 60");
        var d4 = RendererSelector.Choose(new List<ProbeResult> { R("forward_plus", 34, 40, 50, 30), R("mobile", 40, 30, 58, 25), R("gl_compatibility", 45, 30, 50, 30) });
        Check(d4.Method == "mobile" && d4.Quality == 0, "ninguém na meta na Média: o preferido que segura 60 na Baixa");
        var d5 = RendererSelector.Choose(new List<ProbeResult> { R("forward_plus", 20, 80, 25, 60), R("gl_compatibility", 28, 45, 33, 40) });
        Check(d5.Method == "gl_compatibility" && d5.Recovery && d5.Quality == 0, "nenhum na meta: o mais rápido que funciona, em perfil de recuperação (não é 'incompatível')");
        var d6 = RendererSelector.Choose(new List<ProbeResult> { R("forward_plus", 0, 0, started: false), R("mobile", 0, 0, started: false), R("gl_compatibility", 0, 0, rendered: false) });
        Check(d6.Method == "gl_compatibility" && d6.Recovery, "nada validado: Compatibility em modo seguro");
        var d7 = RendererSelector.Choose(new List<ProbeResult> { R("forward_plus", 140, 12), R("mobile", 90, 12) }, new[] { "forward_plus" });
        Check(d7.Method == "mobile", "renderizador marcado como falho não é escolhido");

        // ---- validade da avaliação salva
        var hw = new HardwareFingerprint { Os = "Windows 10", Cpu = "i5-1135G7", Cores = 8, RamGb = 16, GpuDriver = "Intel 31.0.101", ScreenW = 1920, ScreenH = 1080, RefreshHz = 60 };
        var prof = new RenderProfile { EvalVersion = RendererSelector.EvalVersion, Engine = "4.5.1", Hardware = hw, Method = "mobile" };
        Check(RendererSelector.IsValid(prof, hw, "4.5.1"), "mesma máquina e motor: não testa de novo");
        Check(RendererSelector.IsValid(prof, new HardwareFingerprint { Os = hw.Os, Cpu = hw.Cpu, Cores = 8, RamGb = 16, GpuDriver = hw.GpuDriver, ScreenW = 1920, ScreenH = 1080, RefreshHz = 144 }, "4.5.1"), "só a taxa do monitor mudou: continua valendo");
        Check(!RendererSelector.IsValid(prof, new HardwareFingerprint { Os = hw.Os, Cpu = hw.Cpu, Cores = 8, RamGb = 16, GpuDriver = "Intel 32.0.101", ScreenW = 1920, ScreenH = 1080 }, "4.5.1"), "driver de vídeo atualizado: reavalia");
        Check(!RendererSelector.IsValid(prof, hw, "4.6.0"), "motor gráfico novo: reavalia");
        Check(!RendererSelector.IsValid(new RenderProfile { EvalVersion = RendererSelector.EvalVersion - 1, Engine = "4.5.1", Hardware = hw, Method = "mobile" }, hw, "4.5.1"), "requisitos do jogo mudaram (versão da avaliação): reavalia");
        prof.FailedMethods.Add("mobile");
        Check(!RendererSelector.IsValid(prof, hw, "4.5.1"), "renderizador escolhido falhou ao iniciar: reavalia sem ele");
        Check(!RendererSelector.IsValid(null, hw, "4.5.1"), "sem avaliação: avalia");

        // ---- perfil inicial respeita a escolha do jogador
        var mine = new SettingsData { Quality = 2, QualityManual = true };
        Check(!RenderBootstrap.ApplyInitialQuality(new RenderDecision("mobile", 0, 0.75f, true, ""), mine) && mine.Quality == 2, "qualidade escolhida à mão nunca é trocada");
        var auto = new SettingsData { Quality = 1 };
        Check(RenderBootstrap.ApplyInitialQuality(new RenderDecision("forward_plus", 2, 1f, false, ""), auto) && auto.Quality == 2, "sem escolha manual: aplica o perfil inicial da avaliação");
        var legacyLow = System.Text.Json.JsonSerializer.Deserialize<SettingsData>("{\"Version\":2,\"Quality\":0}");
        Settings.Migrate(legacyLow, "x");
        Check(legacyLow.QualityManual && legacyLow.Version == Settings.CurrentVersion, "configuração antiga na Baixa conta como escolha do jogador");

        // ---- arquivo de override lido pela Godot ao iniciar
        Check(RenderStore.OverrideText("mobile").Contains("[rendering]") && RenderStore.OverrideText("mobile").Contains("renderer/rendering_method=\"mobile\""), "override no formato da Godot");
        Check(((string)ProjectSettings.GetSetting("application/config/project_settings_override", "")) == RenderStore.OverridePath, "projeto aponta o override para a pasta do usuário");
        // o valor efetivo pode vir do override desta máquina; o padrão do projeto é o que vale sem avaliação
        Check(FileAccess.GetFileAsString("res://project.godot").Contains("renderer/rendering_method=\"gl_compatibility\""), "sem avaliação o jogo abre no Compatibility (o mais seguro)");
        Check(!RenderBootstrap.ShouldRun(new[] { "--run-tests" }), "seleção não roda nos testes e ferramentas");
        var proj = FileAccess.GetFileAsString("res://project.godot");
        Check(proj.Contains("gl_compatibility/driver.windows=\"opengl3_angle\"") && proj.Contains("gl_compatibility/fallback_to_native=true"),
            "Compatibility no Windows: ANGLE (DirectX 11) primeiro, OpenGL nativo como reserva");
        Check(RenderStore.OverridePath.Contains("_v2") && RendererSelector.EvalVersion >= 2, "override da v0.11.0 ignorado e reavaliação forçada");
        var noRd = new GpuCaps { Checked = true };
        Check(!noRd.RenderingDevice && new GpuCaps { D3D12 = true }.RenderingDevice && new GpuCaps { Vulkan = true }.RenderingDevice, "sem Vulkan nem Direct3D 12: Forward+/Mobile não são testados");
    }

    void TestFpsSettings()
    {
        var fresh = new SettingsData();
        Check(!fresh.VSync && fresh.MaxFps == 144 && fresh.AntiAliasing == 2 && fresh.DynamicResolution, "padrão novo: sem VSync, até 144 FPS, SMAA, resolução dinâmica");
        // configuração antiga no padrão (VSync + 60 FPS) numa RTX
        var old = System.Text.Json.JsonSerializer.Deserialize<SettingsData>("{\"VSync\":true,\"MaxFps\":60,\"Quality\":1}");
        Check(old.Version == 0, "arquivo antigo não tem versão");
        Settings.Migrate(old, "NVIDIA GeForce RTX 3060");
        Check(!old.VSync && old.MaxFps == 144 && old.Quality == 2 && old.Version == Settings.CurrentVersion, "antigo no padrão: libera o FPS e sobe para Alta numa RTX");
        // quem escolheu outra coisa mantém
        var custom = System.Text.Json.JsonSerializer.Deserialize<SettingsData>("{\"VSync\":true,\"MaxFps\":30,\"Quality\":0}");
        Settings.Migrate(custom, "NVIDIA GeForce RTX 3060");
        Check(custom.VSync && custom.MaxFps == 30 && custom.Quality == 0, "escolhas do jogador são mantidas");
        var weak = System.Text.Json.JsonSerializer.Deserialize<SettingsData>("{\"VSync\":true,\"MaxFps\":60,\"Quality\":1}");
        Settings.Migrate(weak, "Intel(R) UHD Graphics 620");
        Check(weak.Quality == 1 && !weak.VSync, "integrada não sobe para Alta");
        var again = new SettingsData { Version = Settings.CurrentVersion, VSync = true, MaxFps = 60 };
        Settings.Migrate(again, "NVIDIA GeForce RTX 3060");
        Check(again.VSync && again.MaxFps == 60, "migração não roda de novo");
        Check(Settings.StrongGpu("NVIDIA GeForce RTX 3060") && Settings.StrongGpu("AMD Radeon RX 6600") && !Settings.StrongGpu("NVIDIA GeForce GTX 750") && !Settings.StrongGpu("llvmpipe"), "detecção de placa forte");

        // resolução dinâmica segura 120 FPS (ou o limite, se menor)
        Check(Main.DynamicTarget(144, -1) == 120 && Main.DynamicTarget(0, -1) == 120, "alvo 120 FPS com limite 144 ou ilimitado");
        Check(Main.DynamicTarget(60, -1) == 57 && Main.DynamicTarget(144, 60) == 57, "alvo abaixo do limite/VSync quando eles são menores");
        float sc = 1f;
        sc = Main.NextDynamicScale(sc, 1f, 90, 120);
        Check(Mathf.IsEqualApprox(sc, 0.95f), "abaixo do alvo: baixa a resolução");
        for (int i = 0; i < 20; i++) sc = Main.NextDynamicScale(sc, 1f, 40, 120);
        Check(Mathf.IsEqualApprox(sc, 0.6f), "nunca abaixo de 60%");
        sc = Main.NextDynamicScale(sc, 1f, 140, 120);
        Check(sc > 0.6f, "com folga: sobe de novo");
        Check(Mathf.IsEqualApprox(Main.NextDynamicScale(0.8f, 1f, 125, 120), 0.8f), "perto do alvo: mantém (sem oscilar)");
        Check(Mathf.IsEqualApprox(Main.NextDynamicScale(1f, 1f, 200, 120), 1f), "não passa da escala escolhida");
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
        float dmgBase0 = ToolData.All[0].Damage;
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
        Check(!bat.Contains("--continue"), "aberto do menu inicial: reabre no menu");
        string bat2 = System.IO.File.ReadAllText(UpdateManager.WriteApplyScript(dir, @"C:\Jogo", @"C:\Jogo\CrystalExcavator.exe", files, 1234, true));
        Check(bat2.Contains("start \"\" \"C:\\Jogo\\CrystalExcavator.exe\" -- --continue"), "atualizado no meio da sessão: reabre direto na partida");
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

    async Task PadPress(JoyButton b)
    {
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = b, Pressed = true, Device = 0 });
        Input.FlushBufferedEvents();
        await Frames(2);
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = b, Pressed = false, Device = 0 });
        Input.FlushBufferedEvents();
        await Frames(2);
    }

    /// <summary>Diálogo, terminal de melhorias e troca de aba usando apenas eventos de controle.</summary>
    async Task TestGamepadMenus()
    {
        Fresh();
        Check(InputMap.ActionGetEvents("ui_accept").Any(e => e is InputEventJoypadButton jb && jb.ButtonIndex == JoyButton.A), "A confirma nos menus");
        Check(InputMap.ActionGetEvents("ui_down").Any(e => e is InputEventJoypadButton jb && jb.ButtonIndex == JoyButton.DpadDown), "direcional navega nos menus");
        // diálogo: o foco começa num botão; direcional troca; A confirma
        var choice = new ChoiceScreen();
        AddChild(choice);
        await Frames(2);
        bool? answer = null;
        choice.Ask("Teste", "Pagar?", "Pagar", "Não pagar", true, yes => answer = yes);
        await Frames(4);
        await PadPress(JoyButton.DpadRight);
        await PadPress(JoyButton.A);
        Check(answer != null, $"A responde ao diálogo pelo controle (resposta: {answer})");
        choice.QueueFree();
        // terminal de melhorias: comprar só com o controle
        GS.AddMoney(100000);
        var shop = new ShopScreen();
        AddChild(shop);
        await Frames(2);
        TerminalScreen.AtSellStation = true;
        shop.Open();
        await Frames(4);
        int levels = GS.Data.Upgrades.Values.Sum();
        for (int i = 0; i < 12 && GS.Data.Upgrades.Values.Sum() == levels; i++)
        {
            await PadPress(JoyButton.DpadDown);
            await PadPress(JoyButton.A);
        }
        Check(GS.Data.Upgrades.Values.Sum() > levels, "comprar melhoria só com direcional + A");
        bool switched = false;
        shop.SwitchToSell = () => switched = true;
        TerminalScreen.AtSellStation = true;
        await PadPress(JoyButton.RightShoulder);
        Check(switched, "RB troca para a aba Vender (na Estação de Venda)");
        TerminalScreen.AtSellStation = false;
        switched = false;
        await PadPress(JoyButton.RightShoulder);
        Check(!switched, "num terminal de melhorias o RB não abre a venda");
        shop.CloseSilently();
        shop.QueueFree();
        await Frames(2);
        Fresh();
    }

    async Task TestShopScroll()
    {
        Fresh();
        GS.AddMoney(1_000_000_000);
        var shop = new ShopScreen();
        AddChild(shop);
        await Frames(2);
        TerminalScreen.AtSellStation = false;
        shop.Open();
        await Frames(6);
        // mouse: clica em comprar várias vezes; a tela tem que ficar no topo
        InputHints.Observe(new InputEventKey { PhysicalKeycode = Key.W, Pressed = true });
        int baseScroll = -1;
        for (int k = 0; k < 6; k++)
        {
            var btn = FindAll<Button>(shop).First(b => b.HasMeta("uid") && (string)b.GetMeta("uid") == UpgradeIds.Capacity);
            btn.GrabFocus(); // o clique do mouse dá foco ao botão (o botão está à vista)
            btn.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames(4);
            if (k == 0) baseScroll = shop.ScrollPos; // a tela já mostra o botão clicado
        }
        Check(GS.Data.Level(UpgradeIds.Capacity) == 6, "compras aplicadas");
        Check(Math.Abs(shop.ScrollPos - baseScroll) < 10, $"comprando com o mouse a tela não pula (rolagem {baseScroll} → {shop.ScrollPos})");
        // controle: compra a última melhoria da lista; a tela acompanha o botão, sem pular para as regiões
        InputHints.Observe(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true });
        var last = FindAll<Button>(shop).First(b => b.HasMeta("uid") && (string)b.GetMeta("uid") == UpgradeIds.Resonance);
        last.GrabFocus();
        await Frames(4);
        int before = shop.ScrollPos;
        last.EmitSignal(BaseButton.SignalName.Pressed);
        await Frames(6);
        var owner = shop.GetViewport().GuiGetFocusOwner();
        Check(owner != null && owner.HasMeta("uid") && (string)owner.GetMeta("uid") == UpgradeIds.Resonance, "controle: o foco continua na mesma melhoria");
        Check(Math.Abs(shop.ScrollPos - before) < 40, $"controle: a rolagem não pula ({before} → {shop.ScrollPos})");
        shop.CloseSilently();
        shop.QueueFree();
        await Frames(2);
        Fresh();
    }

    static IEnumerable<T> FindAll<T>(Node n) where T : Node
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is T t) yield return t;
            foreach (var x in FindAll<T>(c)) yield return x;
        }
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
        Check(Mathf.AngleDifference(yaw0, player.Yaw) > 0.3f, $"analógico direito deve girar a câmera (yaw {yaw0:0.00} → {player.Yaw:0.00})");
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
            // nada fica sem volta: cada câmara tem chão alcançável (ida e volta) perto do centro
            int isolated = 0;
            foreach (var ch in L.Chambers)
            {
                bool any = false;
                for (float dx = -ch.RA * 0.6f; dx <= ch.RA * 0.6f && !any; dx += 1f)
                    for (float dz = -ch.RA * 0.6f; dz <= ch.RA * 0.6f && !any; dz += 1f)
                        if (L.InGrid(ch.A.X + dx, ch.A.Y + dz) && L.Reachable(ch.A.X + dx, ch.A.Y + dz)) any = true;
                if (!any) isolated++;
            }
            Check(isolated == 0, $"{r.Id}: todas as {L.Chambers.Count} câmaras têm volta ({isolated} isoladas)");
            foreach (var pit in L.Pits)
                Check(L.Reachable(pit.RopeTop.X, pit.RopeTop.Z) && L.Reachable(pit.RopeBottom.X, pit.RopeBottom.Z) && pit.RopeTop.Y - pit.RopeBottom.Y > 4f,
                    $"{r.Id}: poço com corda ligando o fundo ({pit.RopeTop.Y - pit.RopeBottom.Y:0} m)");
            Check(L.Pits.Count >= 1, $"{r.Id}: tem poços ({L.Pits.Count})");
            if (r.GiantMaterialId != null)
            {
                var gc = world.Crystals.FirstOrDefault(c => c.Material.Id == r.GiantMaterialId);
                Check(L.Giant != null && gc != null && L.Reachable(gc.StandPoint.X, gc.StandPoint.Z), $"{r.Id}: câmara colossal com a formação gigante, alcançável");
            }
            Check(world.PitRopes.Count(rp => WorldBuilder.RegionAt(rp.Top).Id == r.Id) == L.Pits.Count + L.FixedRopes.Count, $"{r.Id}: corda fixa em cada poço/degrau ({L.FixedRopes.Count} degraus)");
            foreach (var s2 in L.Secrets)
            {
                bool ok2 = L.Reachable(s2.A.X, s2.A.Y);
                Check(ok2, $"{r.Id}: câmara secreta alcançável (agachado)");
            }
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
        Check(depths.Where(c => !c.Material.Special).All(c => c.RespawnRate > 5.5f), "Profundezas esgotadas: renascimento acelerado (formações especiais têm tempo próprio)");
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

    /// <summary>Procura um ponto válido para construir perto de um ponto do caminho principal.</summary>
    static bool FindBuildSpot(BuildingManager bm, CaveLayout L, string type, int pathIndex, out Vector3 spot)
    {
        var pp = L.PathPoints[Math.Clamp(pathIndex, 0, L.PathPoints.Count - 1)];
        return FindBuildSpotNear(bm, L, type, new Vector2(pp.X, pp.Z), out spot);
    }

    static bool FindBuildSpotNear(BuildingManager bm, CaveLayout L, string type, Vector2 c, out Vector3 spot, System.Func<Vector3, bool> extra = null)
    {
        for (int r = 0; r < 14; r++)
            for (int a = 0; a < 12; a++)
            {
                float ang = a * Mathf.Tau / 12f;
                float x = c.X + Mathf.Cos(ang) * r * 1.5f, z = c.Y + Mathf.Sin(ang) * r * 1.5f;
                if (!L.InGrid(x, z)) continue;
                var p = L.OnGround(x, z);
                if (bm.CanPlace(type, p, out _) && (extra == null || extra(p))) { spot = p; return true; }
            }
        spot = default;
        return false;
    }

    /// <summary>A esteira não pode atravessar parede, obstáculo do cenário nem depósito, nem entrar no chão.</summary>
    void RouteIsClean(WorldBuilder world, List<Vector3> route, string name)
    {
        int walls = 0, blocked = 0, crystals = 0, ground = 0;
        float total = BuildingManager.PathLength(route);
        float acc = 0;
        for (int i = 1; i < route.Count; i++)
        {
            var a = route[i - 1]; var b = route[i];
            float len = a.DistanceTo(b);
            for (float t = 0; t < len; t += 0.25f, acc += 0.25f)
            {
                if (acc < 2.5f || acc > total - 3.5f) continue; // pontas: funil e estação
                var q = a.Lerp(b, t / Mathf.Max(len, 0.001f));
                var L = world.LayoutAt(q);
                if (!L.InGrid(q.X, q.Z)) continue; // túnel entre regiões
                if (L.WallDist(q.X, q.Z) > -0.5f) walls++;
                int id = L.Idx(Mathf.RoundToInt(q.X - L.X0), Mathf.RoundToInt(q.Z - L.Z0));
                if (L.Blocked != null && L.Blocked[id]) blocked++;
                if (world.Crystals.Any(c => !c.Material.Special && new Vector2(c.GlobalPosition.X - q.X, c.GlobalPosition.Z - q.Z).Length() < 0.8f)) crystals++;
                if (Mathf.Abs(L.Ground(q.X, q.Z) - q.Y) > 0.6f) ground++;
            }
        }
        Check(walls == 0 && blocked == 0 && crystals == 0, $"esteira ({name}) não atravessa parede/obstáculo/depósito (parede {walls}, obstáculo {blocked}, depósito {crystals})");
        Check(ground == 0, $"esteira ({name}) acompanha o chão ({ground} pontos fora)");
    }

    async Task TestRarityByDepth()
    {
        Fresh();
        var root = new Node3D();
        AddChild(root);
        root.AddChild(new Effects());
        root.AddChild(new ShardManager());
        var world = new WorldBuilder();
        root.AddChild(world);
        await Frames(3);

        // regra
        Check(WorldBuilder.MaxRarityAt(0, 0.05f) == (int)Rarity.Comum, "começo do jogo: só comuns");
        Check(WorldBuilder.MaxRarityAt(0, 1f) < (int)Rarity.Lendario && WorldBuilder.MaxRarityAt(0, 1f, true) < (int)Rarity.Lendario, "região inicial nunca tem lendário (nem no desvio mais fundo)");
        Check(WorldBuilder.MaxRarityAt(0, 0.6f) > WorldBuilder.MaxRarityAt(0, 0.1f), "mais fundo, mais raro");
        Check(WorldBuilder.MaxRarityAt(3, 0.9f) == (int)Rarity.Lendario, "lendário existe no fundo das regiões avançadas");

        int bad = 0, total = 0;
        var report = new System.Text.StringBuilder();
        for (int ri = 0; ri < world.Layouts.Count; ri++)
        {
            var L = world.Layouts[ri];
            var counts = new int[5];
            foreach (var c in world.Crystals)
            {
                if (c.Material.Special || WorldBuilder.RegionAt(c.GlobalPosition).Index != ri) continue;
                total++;
                counts[(int)c.Material.Rarity]++;
                var f = L.FeatureAt(c.StandPoint.X, c.StandPoint.Z);
                bool detour = f?.Kind == CaveKind.Secret || f?.Wing != null;
                // folga: o ponto usado na geração pode ser o depósito ou o ponto onde o jogador fica
                int cap = Math.Max(WorldBuilder.MaxRarityAt(ri, L.Depth(c.StandPoint.X, c.StandPoint.Z), true), WorldBuilder.MaxRarityAt(ri, L.Depth(c.GlobalPosition.X, c.GlobalPosition.Z), true));
                if ((int)c.Material.Rarity > cap) bad++;
            }
            report.Append($" R{ri + 1}[C{counts[0]} I{counts[1]} R{counts[2]} É{counts[3]} L{counts[4]}]");
        }
        GD.Print("[Raridade] " + report);
        Check(total > 500 && bad == 0, $"nenhum depósito mais raro que o permitido pela profundidade ({bad} de {total})");

        var L0 = world.Layouts[0];
        var nearStart = world.Crystals.Where(c => !c.Material.Special && WorldBuilder.RegionAt(c.GlobalPosition).Index == 0 && L0.Depth(c.StandPoint.X, c.StandPoint.Z) < 0.2f).ToList();
        Check(nearStart.Count >= 5 && nearStart.All(c => c.Material.Rarity == Rarity.Comum), $"perto da entrada só minério comum ({nearStart.Count} depósitos)");
        Check(world.Crystals.Where(c => WorldBuilder.RegionAt(c.GlobalPosition).Index == 0).All(c => c.Material.Rarity != Rarity.Lendario || c.Material.Special), "nenhum lendário na região inicial");

        // pedras: todas as rochas soltas usam a textura da caverna; as que não dá para atravessar têm colisão
        await Frames(2);
        var space = world.GetWorld3D().DirectSpaceState;
        int bigPebbles = 0, noCollision = 0, flatRocks = 0;
        foreach (var mm in FindAll<MultiMeshInstance3D>(world))
        {
            var mesh = mm.Multimesh?.Mesh;
            bool rocky = mesh == MeshFactory.Rock(3, 0.3f) || mesh == MeshFactory.Rock(5, 0.35f) || mesh == MeshFactory.Rock(11, 0.4f);
            if (rocky && mm.MaterialOverride is not ShaderMaterial) flatRocks++;
        }
        foreach (var (pos, sc) in world.Pebbles)
        {
            if (sc <= WorldBuilder.MaxWalkThroughPebble) continue;
            bigPebbles++;
            var q = new PhysicsPointQueryParameters3D { Position = pos + Vector3.Up * sc * 0.2f, CollisionMask = PlayerController.WorldLayer };
            if (space.IntersectPoint(q, 1).Count == 0) noCollision++;
        }
        Check(flatRocks == 0, $"rochas soltas com a textura da caverna ({flatRocks} sem textura)");
        Check(bigPebbles > 50 && noCollision == 0, $"pedras maiores que ~35 cm têm colisão ({noCollision} sem colisão de {bigPebbles})");

        // regeneração (evento e renascimento normal): 200 renascimentos perto da entrada continuam comuns
        int regrowBad = 0;
        foreach (var c in nearStart.Take(10))
            for (int k = 0; k < 20; k++)
            {
                var m = c.PickMaterial?.Invoke(new Random(k * 7919 + c.GetHashCode()));
                if (m != null && m.Rarity != Rarity.Comum) regrowBad++;
            }
        Check(regrowBad == 0, $"cristal que renasce perto da entrada não vira raro ({regrowBad} de 200)");
        // e no fundo da região as raridades maiores aparecem
        var deep = world.Crystals.Where(c => WorldBuilder.RegionAt(c.GlobalPosition).Index == 0 && L0.Depth(c.StandPoint.X, c.StandPoint.Z) > 0.75f).ToList();
        Check(deep.Any(c => c.Material.Rarity >= Rarity.Raro), $"no fundo da região inicial aparecem raros ({deep.Count} depósitos fundos)");

        root.QueueFree();
        await Frames(2);
        Fresh();
    }

    async Task TestBuildings()
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
        player.SimulateCapturedMouse = true;
        player.GlobalPosition = world.SpawnPoint;
        var bm = new BuildingManager { World = world, GetPlayer = () => player, CanUseBuildMode = () => false };
        root.AddChild(bm);
        shards.FindSink = bm.FindSink;
        shards.PlayerPosition = () => player.BodyCenter;
        shards.GroundAt = world.GroundAt;
        GS.IsPlaying = true;
        await Frames(3);
        var L0 = world.Layouts[0];

        // ---- poste de luz (sempre liberado) ----
        Check(!bm.CanPlace(BuildIds.Light, world.SellStation.GlobalPosition, out _), "não constrói em cima da Estação de Venda");
        Check(!bm.CanPlace(BuildIds.Light, PlayerHouse.Origin + new Vector3(0, 0.1f, 0), out _), "não constrói dentro da casa");
        Check(FindBuildSpot(bm, L0, BuildIds.Light, 2, out var lp), "acha lugar para um poste");
        Check(bm.Place(BuildIds.Light, lp, 0) == null, "sem moeda não constrói");
        GS.AddMoney(5_000_000);
        long before = GS.Data.Money;
        var post = bm.Place(BuildIds.Light, lp, 0);
        await Frames(2);
        Check(post != null && GS.Data.Money == before - BuildData.Of(BuildIds.Light).Cost, "poste construído e pago");
        var postNode = bm.NodeOf(post.Id);
        Check(postNode is LightPost && postNode.GetChildren().OfType<OmniLight3D>().Any(), "poste tem luz");
        Check(!bm.CanPlace(BuildIds.Light, lp + new Vector3(0.5f, 0, 0), out _), "não constrói colado em outra construção");

        // ---- ramo Construção: a esteira precisa ser aprendida ----
        Check(bm.Available.Select(d => d.Id).SequenceEqual(new[] { BuildIds.Rope, BuildIds.Light }), "no começo só corda e poste no modo construção");
        Check(!bm.CanPlace(BuildIds.Belt, lp + new Vector3(3, 0, 0), out _) && bm.PlaceBelt(lp + new Vector3(3, 0, 0), bm.SellIntake) == null, "esteira bloqueada antes da habilidade");
        Check(!GS.SkillAvailable(SkillIds.BuildHopper) && GS.TryBuySkill(SkillIds.BuildBelt), "aprende Esteira (o Funil exige a Esteira antes)");
        Check(bm.Available.Any(d => d.Id == BuildIds.Belt) && !bm.Available.Any(d => d.Id == BuildIds.Hopper), "esteira liberada; funil ainda não");

        // ---- esteira: início marcado, ponta na Estação de Venda ----
        Check(FindBuildSpot(bm, L0, BuildIds.Belt, L0.PathPoints.Count / 2, out var bs), "acha lugar para o início da esteira");
        bm.BeltEnd(world.SellStation.GlobalPosition + new Vector3(1, 0, 1), out _, out int f0, out _);
        Check(f0 == 0, "mirando na Estação de Venda a ponta se liga nela");
        bm.BeltEnd(bs + new Vector3(30, 0, 0), out _, out int fFree, out _);
        Check(fFree == -1, "ponta solta no meio do nada não liga em nada");
        before = GS.Data.Money;
        var belt1 = bm.PlaceBelt(bs, bm.SellIntake);
        await Frames(2);
        Check(belt1 != null && belt1.Feeds == 0 && bm.BeltOf(belt1.Id) != null && bm.NodeOf(belt1.Id) is BeltIntake, "esteira construída até a Estação de Venda, com entrada no início");
        var route = BuildingManager.Unflatten(belt1?.Path);
        float len = BuildingManager.PathLength(route);
        Check(belt1 != null && GS.Data.Money == before - BuildData.Of(BuildIds.Belt).Cost - BuildingManager.BeltCost(len), $"esteira paga por metro ({len:0} m)");
        Check(route.Count >= 2 && route[0].DistanceTo(bs) < 0.6f && route[^1].DistanceTo(world.SellStation.GlobalPosition) < 3f, "trajeto vai do início marcado até a Estação");
        RouteIsClean(world, route, "região 1");

        // mochila na entrada → esteira → venda automática
        GS.AddItem("quartz", 10);
        long value = GS.SellPrice(GameData.Mat("quartz")) * 10;
        int moved = GS.DepositInventoryInHopper(belt1.Id);
        Check(moved == 10 && GS.InventoryCount == 0, "mochila vai para a entrada da esteira");
        before = GS.Data.Money;
        long sold0 = GS.Data.ShardsSold;
        await WaitSold(sold0 + 10, len);
        Check(GS.Data.ShardsSold - sold0 == 10 && GS.Data.Money - before == value, $"a esteira leva e vende tudo na Estação ({GS.Data.ShardsSold - sold0}/10, +{GS.Data.Money - before}/{value})");

        // ---- segunda esteira emendada na primeira ----
        var mid = BuildingManager.Unflatten(belt1.Path)[route.Count / 2];
        var L0b = L0;
        Check(FindBuildSpotNear(bm, L0, BuildIds.Belt, new Vector2(mid.X, mid.Z) + new Vector2(0, 0), out var bs2,
            p => p.DistanceTo(mid) > 7f && p.DistanceTo(mid) < 16f && bm.RouteBetween(p, mid) != null), "acha início para uma esteira-galho");
        var belt2 = bm.PlaceBelt(bs2, mid);
        await Frames(2);
        Check(belt2 != null && belt2.Feeds == belt1.Id && belt2.JoinS > 0, $"ponta na outra esteira: emenda ({belt2?.JoinS:0.0} m)");
        Check(bm.DistanceToSell(belt2.Id) > BuildingManager.PathLength(BuildingManager.Unflatten(belt2.Path)), "distância até a venda soma as duas esteiras");
        GS.AddItem("quartz", 5);
        GS.DepositInventoryInHopper(belt2.Id);
        sold0 = GS.Data.ShardsSold;
        await WaitSold(sold0 + 5, bm.DistanceToSell(belt2.Id));
        Check(GS.Data.ShardsSold - sold0 == 5, $"o que entra na esteira-galho passa para a principal e é vendido ({GS.Data.ShardsSold - sold0}/5)");

        // ---- funil: só ao lado de uma esteira ----
        Check(GS.TryBuySkill(SkillIds.BuildHopper), "aprende Funil");
        Check(bm.Place(BuildIds.Hopper, lp + new Vector3(0, 0, 0), 0) == null, "funil longe de esteira não é construído");
        Vector3 hp = default; bool foundHop = false;
        var bp = BuildingManager.Unflatten(belt1.Path);
        for (int k = 2; k < bp.Count - 2 && !foundHop; k++)
            for (int side = -1; side <= 1 && !foundHop; side += 2)
            {
                var dir = (bp[k + 1] - bp[k]); dir.Y = 0;
                if (dir.LengthSquared() < 0.01f) continue;
                var q = bp[k] + dir.Normalized().Cross(Vector3.Up) * 1.8f * side;
                if (!L0.InGrid(q.X, q.Z)) continue;
                q = L0.OnGround(q.X, q.Z);
                if (bm.CanPlace(BuildIds.Hopper, q, out _) && bm.HopperSpot(q, out _, out _, out _)) { hp = q; foundHop = true; }
            }
        Check(foundHop, "acha lugar para o funil ao lado da esteira");
        var hop = bm.Place(BuildIds.Hopper, hp, 0);
        await Frames(2);
        Check(hop != null && hop.Feeds > 0 && bm.NodeOf(hop.Id) is Hopper, "funil construído e ligado à esteira do lado");
        player.GlobalPosition = world.SpawnPoint;
        long s1 = GS.Data.ShardsSold;
        shards.SpawnShards(GameData.Mat("quartz"), hp + new Vector3(2f, 1f, 0), 5);
        for (int i = 0; i < 300 && shards.ActiveCount > 0; i++) await Frames(1);
        Check(shards.ActiveCount == 0, "fragmentos soltos perto do funil são puxados para ele");
        await WaitSold(s1 + 5, bm.DistanceToSell(hop.Id));
        Check(GS.Data.ShardsSold - s1 == 5, $"o funil despeja na esteira e tudo é vendido ({GS.Data.ShardsSold - s1}/5)");

        // em trânsito: gravado no save e volta para a fila ao carregar
        GS.AddItem("quartz", 6);
        GS.DepositInventoryInHopper(belt1.Id);
        await Frames(30);
        var b1 = bm.BeltOf(belt1.Id);
        int inFlight = b1.Items.Count, queued = GS.HopperQueued(belt1.Id);
        bm.WriteInFlight(GS.Data);
        var json = System.Text.Json.JsonSerializer.Serialize(GS.Data);
        Check(SaveSystem.TryParse(json, out var loaded), "save com construções carrega");
        var lb = loaded.Buildings.First(b => b.Id == belt1.Id);
        Check(inFlight > 0 && lb.Queue.Values.Sum() == inFlight + queued, $"fragmentos na esteira não se perdem no save ({inFlight} + {queued})");
        Check(lb.Path != null && lb.Path.Count == belt1.Path.Count && lb.Feeds == 0 && loaded.Buildings.First(b => b.Id == belt2.Id).Feeds == belt1.Id, "trajeto e ligações da esteira salvos");

        // ---- trajeto longo entre regiões (Núcleo → acampamento e para baixo) ----
        foreach (var r in GameData.Regions) if (!GS.IsUnlocked(r.Id)) GS.Data.UnlockedRegions.Add(r.Id);
        var L2 = world.Layouts[2];
        Check(FindBuildSpot(bm, L2, BuildIds.Belt, L2.PathPoints.Count / 2, out var deep), "acha lugar no Núcleo");
        var deepRoute = bm.RouteToSell(deep);
        Check(deepRoute != null && deepRoute[^1].DistanceTo(world.SellStation.GlobalPosition) < 3f, "esteira do Núcleo chega à Estação de Venda passando pelas outras regiões");
        if (deepRoute != null)
        {
            RouteIsClean(world, deepRoute, "Núcleo → acampamento");
            bool crosses = deepRoute.Any(p => WorldBuilder.RegionAt(p).Index == 1);
            Check(crosses && BuildingManager.PathLength(deepRoute) > 300f, $"esteira longa atravessa as Profundezas ({BuildingManager.PathLength(deepRoute):0} m)");
        }
        var down = bm.RouteBetween(bs, deep);
        Check(down != null && WorldBuilder.RegionAt(down[^1]).Index == 2, "trajeto também desce pelas regiões");
        if (down != null) RouteIsClean(world, down, "acampamento → Núcleo");

        // ---- desmontar: quem despejava nela fica sem saída ----
        before = GS.Data.Money;
        long paid = GS.Building(post.Id).Paid;
        bm.Demolish(post.Id);
        await Frames(2);
        Check(GS.Building(post.Id) == null && bm.NodeOf(post.Id) == null && GS.Data.Money == before + (long)(paid * BuildData.RefundFraction), "desmontar devolve metade");
        bm.Demolish(belt1.Id);
        await Frames(2);
        Check(bm.BeltOf(belt1.Id) == null && GS.Building(belt2.Id).Feeds == -1 && GS.Building(hop.Id).Feeds == -1 && !bm.HasOutlet(belt2.Id) && !bm.HasOutlet(hop.Id),
            "esteira desmontada: o galho e o funil ficam sem saída");
        GS.AddItem("quartz", 3);
        GS.DepositInventoryInHopper(belt2.Id);
        await Frames(120);
        Check(GS.HopperQueued(belt2.Id) == 3, "esteira sem saída segura os fragmentos na fila");

        // ---- drone de área ----
        Check(GS.TryBuySkill(SkillIds.BuildDrone), "aprende Drone de área");
        bm.Demolish(belt2.Id); bm.Demolish(hop.Id);
        await Frames(2);
        var camp = L0.OnGround(L0.Camp.A.X, L0.Camp.A.Y);
        Check(FindBuildSpotNear(bm, L0, BuildIds.Drone, L0.Camp.A, out var dp), "acha lugar para a base do drone");
        var droneB = bm.Place(BuildIds.Drone, dp, 0);
        await Frames(3);
        var dock = bm.NodeOf(droneB?.Id ?? -1) as Dock;
        Check(droneB != null && dock != null && dock.Unit is DroneBot && bm.Drone != null, "base do drone construída, drone ativo");
        player.GlobalPosition = world.SpawnPoint + new Vector3(0, 0, 0);
        GS.Data.Inventory.Clear();
        Vector3 drop = default, far = default;
        for (int k = 0; k < 32; k++)
        {
            float ang = k * Mathf.Tau / 32f;
            var c = dp + new Vector3(Mathf.Cos(ang) * 7f, 0, Mathf.Sin(ang) * 7f);
            var c2 = dp + new Vector3(Mathf.Cos(ang) * (BuildData.DroneRange + 6f), 0, Mathf.Sin(ang) * (BuildData.DroneRange + 6f));
            if (drop == default && L0.InGrid(c.X, c.Z) && L0.Reachable(c.X, c.Z) && bm.FindSink(c) == null && c.DistanceTo(player.GlobalPosition) > 8f) drop = L0.OnGround(c.X, c.Z, 0.5f);
            if (far == default && L0.InGrid(c2.X, c2.Z) && L0.Reachable(c2.X, c2.Z) && c2.DistanceTo(player.GlobalPosition) > 8f) far = L0.OnGround(c2.X, c2.Z, 0.5f);
        }
        player.GlobalPosition = L0.OnGround(L0.PathPoints[^1].X, L0.PathPoints[^1].Z); // longe: o ímã não interfere
        shards.SpawnShards(GameData.Mat("amethyst"), drop, 3);
        if (far != default) shards.SpawnShards(GameData.Mat("quartz"), far, 2);
        for (int i = 0; i < 1200 && dock.Stored < 3; i++) await Frames(1);
        Check(dock.Stored == 3, $"drone recolhe fragmentos na área e guarda na base sem funil por perto ({dock.Stored}/3)");
        Check(far == default || shards.Loose.Count(s => s.Mat.Id == "quartz") == 2, "drone ignora fragmentos fora da área");
        Check(GS.Data.Discovered.Contains("amethyst"), "material recolhido pelo drone conta como descoberto (ex.: Coração)");
        player.GlobalPosition = dp + new Vector3(1.5f, 0.2f, 0);
        dock.Interact(player);
        Check(GS.InventoryCount == 3 && dock.Stored == 0, "jogador pega o que está guardado na base");
        foreach (var s in shards.Loose.ToList()) shards.Take(s);

        // ---- robô minerador ----
        Check(!bm.CanPlace(BuildIds.Robot, dp + new Vector3(5, 0, 0), out _), "robô bloqueado antes da habilidade");
        Check(GS.TryBuySkill(SkillIds.BuildRobot), "aprende Robô mineiro");
        var target = world.Crystals.Where(c => c.IsMineable && !c.Material.Special && WorldBuilder.RegionAt(c.GlobalPosition).Index == 0 && c.GlobalTransform.Basis.Y.Normalized().Y > 0.9f
                                               && c.StandPoint.DistanceTo(dp) > BuildData.DroneRange + 4f)
                                   .OrderBy(c => c.StandPoint.DistanceTo(L0.OnGround(L0.PathPoints[L0.PathPoints.Count / 3].X, L0.PathPoints[L0.PathPoints.Count / 3].Z))).FirstOrDefault();
        Check(target != null, "acha um depósito para o robô");
        Check(FindBuildSpotNear(bm, L0, BuildIds.Robot, new Vector2(target.StandPoint.X, target.StandPoint.Z), out var rp,
            p => p.DistanceTo(target.StandPoint) > 2.5f && p.DistanceTo(target.StandPoint) < 7f && L0.FindPath(p, target.StandPoint) != null), "acha lugar para a base do robô perto do depósito");
        var robotB = bm.Place(BuildIds.Robot, rp, 0);
        await Frames(3);
        var rdock = bm.NodeOf(robotB?.Id ?? -1) as Dock;
        Check(rdock?.Unit is MinerBot, "base do robô construída, robô ativo");
        var outside = world.Crystals.Where(c => c.IsMineable && c.StandPoint.DistanceTo(rp) > BuildData.RobotRange + 3f && WorldBuilder.RegionAt(c.GlobalPosition).Index == 0).Take(40).ToList();
        GS.Data.DayOver = false;
        Engine.TimeScale = 8;
        for (int i = 0; i < 2400 && rdock.Stored == 0; i++) await Frames(1);
        Engine.TimeScale = 1;
        Check(rdock.Stored > 0, $"robô minera dentro da área e guarda na base ({rdock.Stored})");
        Check(outside.All(c => c.IsMineable), "robô não minera fora da área dele");
        Check(BuildData.RobotDpsFactor < 1f, "robô é mais fraco que o goblin da região");
        GS.Data.DayOver = true;
        await Frames(10);
        Check(rdock.Unit.Status == Loc.T("parado (fora do turno)"), "robô para fora do turno");
        GS.Data.DayOver = false;

        // ---- modo construção com o botão: marcar início, arrastar até a venda, soltar ----
        bm.CanUseBuildMode = () => true;
        bm.SetActive(true);
        Check(bm.SelectId(BuildIds.Belt), "esteira selecionada no modo construção");
        await PFrames(2);
        var st = world.SellStation.GlobalPosition;
        async Task<bool> LookFrom(Vector3 target, float dist0)
        {
            for (int k = 0; k < 48; k++)
            {
                float dist = dist0 + (k / 16 == 1 ? -1f : k / 16 == 2 ? 1f : 0f); // 3 distâncias × 16 direções
                float ang = (k % 16) * Mathf.Tau / 16f;
                var at = target + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * dist;
                if (!L0.InGrid(at.X, at.Z) || !L0.Reachable(at.X, at.Z)) continue;
                player.GlobalPosition = L0.OnGround(at.X, at.Z) + Vector3.Up * 0.05f;
                player.Velocity = Vector3.Zero;
                await PFrames(1);
                var cam = player.Camera.GlobalPosition;
                var d = target + Vector3.Up * 0.05f - cam;
                player.SetLook(Mathf.Atan2(-d.X, -d.Z), Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
                await PFrames(2);
                // a mira precisa ver o alvo
                var hit = player.GetWorld3D().DirectSpaceState.IntersectRay(new PhysicsRayQueryParameters3D { From = cam, To = target + Vector3.Up * 0.05f, CollisionMask = PlayerController.WorldLayer, Exclude = new Godot.Collections.Array<Rid> { player.GetRid() } });
                if (hit.Count == 0 || hit["position"].AsVector3().DistanceTo(target) < 0.5f) return true;
            }
            return false;
        }
        async Task HoldAim(Vector3 target, int ms)
        {
            var pos = player.GlobalPosition;
            ulong t0 = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - t0 < (ulong)ms)
            {
                // mantém o jogador parado e a mira no alvo enquanto o trajeto é recalculado (a cada 0,25 s)
                player.GlobalPosition = pos;
                player.Velocity = Vector3.Zero;
                var d = target + Vector3.Up * 0.05f - player.Camera.GlobalPosition;
                player.SetLook(Mathf.Atan2(-d.X, -d.Z), Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()));
                await PFrames(1);
            }
        }
        Check(FindBuildSpotNear(bm, L0, BuildIds.Belt, new Vector2(st.X, st.Z), out var ds,
            p => p.DistanceTo(st) > 12f && p.DistanceTo(st) < 26f && bm.RouteBetween(p, bm.SellIntake) != null), "acha início para arrastar até a venda");
        int count0 = GS.Data.Buildings.Count(b => b.Type == BuildIds.Belt);
        Check(await LookFrom(ds, 3f), "mira no início");
        Input.ActionPress("mine");
        await PFrames(3);
        Check(bm.BeltStart != null && bm.BeltStart.Value.DistanceTo(ds) < 1.2f, "apertar marca o início da esteira");
        Check(await LookFrom(bm.SellIntake, 4f), "arrastando, mira na Estação de Venda");
        await HoldAim(bm.SellIntake, 1200);
        Input.ActionRelease("mine");
        await PFrames(3);
        var dragged = GS.Data.Buildings.Where(b => b.Type == BuildIds.Belt).OrderBy(b => b.Id).LastOrDefault();
        Check(GS.Data.Buildings.Count(b => b.Type == BuildIds.Belt) == count0 + 1 && dragged?.Feeds == 0 && new Vector3(dragged.X, dragged.Y, dragged.Z).DistanceTo(ds) < 1.2f && bm.BeltStart == null,
            "soltar o botão na Estação de Venda constrói a esteira arrastada");
        // clique, clique: segunda esteira emendada na arrastada
        var dmid = BuildingManager.Unflatten(dragged?.Path ?? new List<float>());
        // ponto do meio da esteira como ela é desenhada (seguindo o chão)
        var dBelt = dragged != null ? bm.BeltOf(dragged.Id) : null;
        Vector3 beltMid = default;
        if (dBelt != null) dBelt.Sample(dBelt.Length * 0.5f, out beltMid, out _);
        Vector3 cs = default;
        Check(dmid.Count >= 3 && FindBuildSpotNear(bm, L0, BuildIds.Belt, new Vector2(beltMid.X, beltMid.Z), out cs,
            p => p.DistanceTo(beltMid) > 6f && p.DistanceTo(beltMid) < 14f && bm.RouteBetween(p, beltMid) != null), "acha início para a esteira de clique-clique");
        Check(await LookFrom(cs, 3f), "mira no início (clique)");
        Input.ActionPress("mine"); await PFrames(2); Input.ActionRelease("mine"); await PFrames(2);
        Check(bm.BeltStart != null, "clique rápido deixa o início marcado");
        Input.ActionPress("interact"); await PFrames(2); Input.ActionRelease("interact"); await PFrames(2);
        Check(bm.BeltStart == null && GS.Data.Buildings.Count(b => b.Type == BuildIds.Belt) == count0 + 1, "interagir cancela o início sem construir nem desmontar");
        Check(await LookFrom(cs, 3f), "mira de novo no início");
        Input.ActionPress("mine"); await PFrames(2); Input.ActionRelease("mine"); await PFrames(2);
        Check(await LookFrom(beltMid, 3.5f), "mira na esteira arrastada");
        await HoldAim(beltMid, 1200);
        Input.ActionPress("mine"); await PFrames(2); Input.ActionRelease("mine"); await PFrames(2);
        var clicked = GS.Data.Buildings.Where(b => b.Type == BuildIds.Belt).OrderBy(b => b.Id).LastOrDefault();
        Check(clicked != null && clicked.Id != dragged?.Id && clicked.Feeds == dragged?.Id, "segundo clique na outra esteira constrói a emenda");
        bm.SetActive(false);
        bm.CanUseBuildMode = () => false;

        // ---- renascimento: construções e ramo Construção recomeçam ----
        GS.Data.RunEarned = 10_000_000;
        GS.Data.GameCompleted = true;
        if (GS.CanPrestige) { GS.Prestige(); bm.Rebuild(); }
        await Frames(2);
        Check(GS.Data.Buildings.Count == 0 && bm.NodeCount == 0 && !GS.BuildUnlocked(BuildIds.Belt), "renascimento: construções zeradas, ramo Construção recomeça");

        // ---- save antigo: funil com esteira automática e drone que seguia o jogador ----
        GS.Data.Buildings.Clear();
        GS.Data.Skills.Clear();
        GS.Data.DroneOwned = true;
        long m0 = GS.Data.Money;
        var legacy = new BuildingSave { Id = GS.Data.NextBuildingId++, Type = BuildIds.Hopper, X = hp.X, Y = hp.Y, Z = hp.Z, Paid = 5000 };
        legacy.Queue["quartz"] = 4;
        GS.Data.Buildings.Add(legacy);
        bm.Rebuild();
        await Frames(2);
        var mb = GS.Data.Buildings.FirstOrDefault(b => b.Type == BuildIds.Belt);
        Check(mb != null && mb.Feeds == 0 && legacy.Feeds == mb.Id && legacy.Ver == 1 && bm.HasOutlet(legacy.Id), "save antigo: funil vira funil + esteira até a venda");
        Check(legacy.Paid + (mb?.Paid ?? 0) == 5000, "valor pago dividido entre funil e esteira");
        Check(!GS.Data.DroneOwned && GS.Data.Money == m0 + BuildData.Of(BuildIds.Drone).Cost && GS.HasSkill(SkillIds.BuildDrone) && GS.HasSkill(SkillIds.BuildHopper), "save antigo: drone devolvido e ramo Construção liberado");

        Engine.TimeScale = 1;
        root.QueueFree();
        await Frames(2);
        Fresh();
    }

    /// <summary>Espera a esteira vender até <paramref name="target"/> fragmentos (tempo acelerado).</summary>
    async Task WaitSold(long target, float len)
    {
        Engine.TimeScale = 8;
        float maxWait = Mathf.Max(0, len) / BuildData.BeltSpeed + 20 * BuildData.BeltInterval + 3f;
        ulong t0 = Time.GetTicksMsec();
        while (GS.Data.ShardsSold < target && (Time.GetTicksMsec() - t0) / 1000f * 8 < maxWait * 1.5f) await Frames(1);
        Engine.TimeScale = 1;
    }

    async Task TestRopes()
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
        var bm = new BuildingManager { World = world, GetPlayer = () => player, CanUseBuildMode = () => false };
        root.AddChild(bm);
        Rope.PlayerPos = () => player.GlobalPosition;
        GS.IsPlaying = true;
        await Frames(3);
        var L = world.Layouts[0];
        Check(L.Pits.Count > 0 && world.PitRopes.Count > 0, $"há poços com corda na região inicial ({L.Pits.Count})");
        var pit = L.Pits[0];
        var rope = world.PitRopes.First(r => r.Top.DistanceTo(pit.RopeTop) < 0.1f);
        // desce pelo poço
        player.GlobalPosition = pit.TopExit + Vector3.Up * 0.1f;
        await Frames(10);
        Check(rope.PlayerAtTop() && rope.GetPrompt().Contains(Loc.T("Descer")), "perto do alto: a corda oferece descer");
        rope.Interact(player);
        Check(player.Climbing, "começa a escalar");
        bool? up = null;
        player.ClimbFinished += u => up = u;
        // sem apertar nada fica pendurado no mesmo lugar
        await Frames(5);
        var hang = player.GlobalPosition;
        await Frames(30);
        Check(player.Climbing && player.GlobalPosition.DistanceTo(hang) < 0.05f, "sem apertar nada fica parado na corda");
        // as mãos ficam na corda (mundo), não na câmera: olhando para cima elas não sobem junto
        var handsNode = player.FindChild("ClimbHands", true, false) as Node3D;
        var h0 = handsNode.GlobalPosition;
        player.SetLook(player.Yaw, 1.2f);
        await Frames(3);
        Check(handsNode.GlobalPosition.DistanceTo(h0) < 0.05f, "olhar para cima não leva as mãos/corda junto");
        player.SetLook(player.Yaw, 0f);
        Input.ActionPress("move_back");
        for (int i = 0; i < 600 && player.Climbing; i++) await Frames(1);
        Input.ActionRelease("move_back");
        await Frames(10);
        Check(!player.Climbing && up == false, "chega ao fundo");
        Check(player.GlobalPosition.Y < pit.RopeTop.Y - 4f && L.PitAt(player.GlobalPosition) != null, $"está no fundo do poço (y={player.GlobalPosition.Y:0.0})");
        // sobe de volta
        Check(!rope.PlayerAtTop() && rope.GetPrompt().Contains(Loc.T("Subir")), "no fundo: a corda oferece subir");
        rope.Interact(player);
        Input.ActionPress("move_forward");
        for (int i = 0; i < 600 && player.Climbing; i++) await Frames(1);
        Input.ActionRelease("move_forward");
        await Frames(20);
        Check(up == true && player.GlobalPosition.DistanceTo(pit.TopExit) < 1.5f && player.IsOnFloor(), $"subiu e ficou em pé na borda (dist {player.GlobalPosition.DistanceTo(pit.TopExit):0.0})");
        // pular solta a corda
        rope.Interact(player);
        await Frames(20);
        Input.ParseInputEvent(new InputEventAction { Action = "jump", Pressed = true });
        Input.FlushBufferedEvents();
        await Frames(3);
        Input.ParseInputEvent(new InputEventAction { Action = "jump", Pressed = false });
        Input.FlushBufferedEvents();
        await Frames(3);
        Check(!player.Climbing, "pular solta a corda");
        // corda do jogador: mirando na beira de outro poço (ou do mesmo, de outro lado)
        GS.AddMoney(100000);
        var other = L.Pits[^1];
        var dirIn = new Vector3(other.Center.X, 0, other.Center.Y) - new Vector3(other.RopeTop.X, 0, other.RopeTop.Z);
        var side = new Vector3(-dirIn.Z, 0, dirIn.X).Normalized();
        bool placed = false;
        foreach (float ang in new[] { 1.6f, -1.6f, 2.4f, -2.4f })
        {
            var lipDir = new Vector3(Mathf.Cos(ang) * dirIn.X - Mathf.Sin(ang) * dirIn.Z, 0, Mathf.Sin(ang) * dirIn.X + Mathf.Cos(ang) * dirIn.Z).Normalized();
            var c = new Vector3(other.Center.X, 0, other.Center.Y);
            var aim = L.OnGround(c.X - lipDir.X * (other.Radius + 1.6f), c.Z - lipDir.Z * (other.Radius + 1.6f));
            var stand = L.OnGround(c.X - lipDir.X * (other.Radius + 4.5f), c.Z - lipDir.Z * (other.Radius + 4.5f));
            if (!L.Reachable(stand.X, stand.Z)) continue;
            if (bm.RopeFor(aim, stand, out var t, out var b, out var e, out _))
            {
                var bs = bm.PlaceRope(t, b, e);
                placed = bs != null && bs.Type == BuildIds.Rope && bm.NodeOf(bs.Id) is Rope;
                Check(placed && t.Y - b.Y > 4f, $"corda do jogador colocada na beira ({t.Y - b.Y:0} m)");
                break;
            }
        }
        Check(placed, "achou uma beira para jogar a corda");
        Check(!bm.RopeFor(L.OnGround(L.Camp.A.X, L.Camp.A.Y), L.OnGround(L.Camp.A.X + 3, L.Camp.A.Y), out _, out _, out _, out _), "chão plano: não dá para jogar corda");
        root.QueueFree();
        await Frames(2);
        Fresh();
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

    /// <summary>Espera quadros de processo (não de física): um aperto de botão precisa durar quadros visuais.</summary>
    async Task PFrames(int n)
    {
        for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
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
        int guard = 0, toolBuys = 0, ammoBuys = 0;
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
            GS.WearTool((int)Math.Ceiling(mat.Hp / Math.Max(0.01f, GS.Damage))); // cada golpe gasta a ferramenta
            GS.AddItem(mat.Id, shards);
            // mochila cheia, ou a ferramenta quebrou: volta ao acampamento (vende e compra outra)
            if (GS.InventoryFull || GS.UsingHands)
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
                // laser: mantém células de energia
                for (int k = 0; k < 3 && GS.IsLaser && d.Ammo < ToolData.AmmoPack && GS.CanAfford(ToolData.AmmoPackCost); k++) { GS.TryBuyAmmo(); ammoBuys++; }
                // ferramenta: a melhor liberada que dá para pagar (sempre repõe se quebrou)
                for (int ti = ToolData.All.Count - 1; ti >= 0; ti--)
                {
                    if (!GS.ToolAvailable(ti) || !GS.CanAfford(ToolData.All[ti].Cost)) continue;
                    if (ti > d.Tool || GS.ToolBroken) { GS.TryBuyTool(ti); toolBuys++; }
                    break;
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
            if (unlock3 > 0 && heart > 0 && UpgradeIds.All.All(GS.IsMaxed) && d.Tool == ToolData.All.Count - 1) break;
        }
        string Fmt(double s) => s < 0 ? "não alcançado" : $"{s / 60:0.0} min";
        GD.Print($"      Região 2 em {Fmt(unlock2)}, Região 3 em {Fmt(unlock3)}, Coração viável em {Fmt(heart)}");
        GD.Print($"      Após {Fmt(t)}: {GS.CurrentTool.NamePt}, dano {GS.Damage:0}, {GS.SwingsPerSecond:0.0} golpes/s, cap {GS.Capacity}, valor x{GS.ValueMultiplier:0.00}, ferramentas compradas {toolBuys}, pacotes de munição {ammoBuys}");
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
