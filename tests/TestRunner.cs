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
            Run("Áudio sem saturação", TestAudio);
            await RunAsync("Integração: minerar, coletar, renascer", TestMiningIntegration);
            await RunAsync("Integração: portões e estações", TestGatesIntegration);
            await RunAsync("Integração: pulso de ressonância", TestResonance);
            await RunAsync("Integração: eventos de região", TestRegionEvents);
            await RunAsync("Integração: marcador de objetivo", TestObjectiveMarker);
            await RunAsync("Integração: controle (gamepad)", TestGamepad);
            await RunAsync("Integração: galerias laterais", TestWings);
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

        var crystal = world.Crystals.Where(c => c.Material.Id == "quartz").OrderBy(c => c.GlobalPosition.DistanceTo(world.SpawnPoint)).First();
        // posiciona o jogador ao lado e olhando para o cristal
        var cpos = crystal.GlobalPosition;
        player.GlobalPosition = cpos + new Vector3(0, 0, 1.6f);
        player.SetLook(0, -0.35f);
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
        player.GlobalPosition = c2.GlobalPosition + new Vector3(0, 0, 1.6f);
        player.SetLook(0, -0.35f);
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
        Check(WorldBuilder.RegionAt(new Vector3(0, 0, -60)).Id == "depths", "detecção de região por posição");
        Check(WorldBuilder.RegionAt(new Vector3(0, 0, 10)).Id == "caves", "detecção da região inicial");
        Check(WorldBuilder.RegionAt(new Vector3(0, 0, -130)).Id == "core", "detecção do Núcleo");
        Check(WorldBuilder.RegionAt(new Vector3(0, 0, -200)).Id == "abyss", "detecção do Reservatório");
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
        player.GlobalPosition = target.GlobalPosition + new Vector3(0, 0, 1.6f);
        player.SetLook(0, -0.35f);
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
        var crystal = world.Crystals.First(c => c.Material.Id == "quartz");
        player.GlobalPosition = crystal.GlobalPosition + new Vector3(0, 0, 1.6f);
        player.SetLook(0.6f, -0.35f);
        float yaw0 = player.Yaw;
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = -1f });
        await Frames(20);
        Input.ParseInputEvent(new InputEventJoypadMotion { Axis = JoyAxis.RightX, AxisValue = 0f });
        await Frames(2);
        Check(player.Yaw > yaw0 + 0.3f, $"analógico direito deve girar a câmera (yaw {yaw0:0.00} → {player.Yaw:0.00})");
        Check(InputHints.UsingPad && InputHints.Interact == "[X]", "dicas devem mudar para o controle");

        // gatilho direito minera
        player.SetLook(0, -0.35f);
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

    /// <summary>Galerias laterais: existem, têm cristais do minério em destaque e são alcançáveis andando.</summary>
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
        int total = 0;
        foreach (var r in GameData.Regions)
        {
            Check(r.Wings.Count >= 2, $"região {r.Id} deve ter ao menos 2 galerias laterais");
            foreach (var w in r.Wings)
            {
                total++;
                Check(GameData.Mat(w.FeaturedMaterialId) != null, $"galeria {w.Id}: minério em destaque inexistente");
                var wing = world.RegionRoots[r.Index].GetNodeOrNull<Node3D>("Wing_" + w.Id);
                Check(wing != null, $"galeria {w.Id} não foi construída");
                if (wing == null) continue;
                var crystals = world.Crystals.Where(c => wing.IsAncestorOf(c)).ToList();
                Check(crystals.Count >= w.Slots - 2, $"galeria {w.Id}: poucos cristais ({crystals.Count})");
                Check(crystals.Count(c => c.Material.Id == w.FeaturedMaterialId) >= 2, $"galeria {w.Id}: minério em destaque quase ausente");
                // anda da sala principal até dentro da galeria
                float oz = r.CenterZ + w.ZOffset;
                player.GlobalPosition = new Vector3(w.Side * (r.HalfX - 6f), 0.1f, oz);
                player.Velocity = Vector3.Zero;
                player.SetLook(-w.Side * Mathf.Pi / 2, 0);
                await Frames(2);
                Input.ActionPress("move_forward");
                float goal = r.HalfX + 2 + WorldBuilder.WingTunnelLength + 2f;
                for (int i = 0; i < 600 && player.GlobalPosition.X * w.Side < goal; i++) await Frames(1);
                Input.ActionRelease("move_forward");
                Check(player.GlobalPosition.X * w.Side >= goal, $"galeria {w.Id}: jogador não conseguiu entrar andando (x={player.GlobalPosition.X:0.0})");
                Check(player.GlobalPosition.Y > -1f, $"galeria {w.Id}: jogador caiu do mapa");
            }
        }
        Check(total >= 8, $"devem existir ao menos 8 galerias (há {total})");
        // oclusão por distância: longe da entrada some, dentro aparece
        var r1 = GameData.Regions[1]; var w1 = r1.Wings[0];
        var wn = world.RegionRoots[1].GetNode<Node3D>("Wing_" + w1.Id);
        world.UpdateWingVisibility(new Vector3(0, 1.5f, GameData.Regions[0].CenterZ));
        Check(!wn.Visible, "galeria distante deve ficar oculta");
        world.UpdateWingVisibility(new Vector3(WorldBuilder.WingCenterX(r1, w1), 1.5f, r1.CenterZ + w1.ZOffset));
        Check(wn.Visible, "galeria deve aparecer com a câmera dentro dela");
        world.UpdateWingVisibility(new Vector3(w1.Side * (r1.HalfX - 8f), 1.5f, r1.CenterZ + w1.ZOffset));
        Check(wn.Visible, "galeria deve aparecer perto da entrada");
        root.QueueFree();
        await Frames(2);
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
        var marker = new ObjectiveMarker { World = world, PlayerPos = () => pos, CurrentRegion = () => GameData.Regions[0] };
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
        int maxNormal = (int)MathF.Round(sapph.Material.ShardsMax * sapph.SizeScale);
        Check(spawned > maxNormal, $"fragmentos com evento ({spawned}) devem superar o máximo normal ({maxNormal})");
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
