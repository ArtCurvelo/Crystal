using System;
using System.Linq;
using System.Collections.Generic;
using CrystalExcavator.Data;
using Data_ = CrystalExcavator.Data.GoblinData;
using Godot;

namespace CrystalExcavator.Core;

public enum NotifyKind { Info, Success, Warning, Unlock, Rare }

/// <summary>
/// Autoload central: dados do jogador, economia, upgrades, regiões e salvamento.
/// Toda a lógica de regras fica aqui para ser testável sem a cena 3D.
/// </summary>
public partial class GameState : Node
{
    public static GameState Instance { get; private set; }

    public SaveData Data { get; private set; } = new();
    public bool IsPlaying { get; set; }
    public string CurrentRegionId { get; set; } = "caves";

    public event Action MoneyChanged;
    public event Action InventoryChanged;
    public event Action UpgradesChanged;
    public event Action RegionsChanged;
    public event Action<string, string, NotifyKind> Notified;
    public event Action<MaterialDef> MaterialDiscovered;
    public event Action<long> Sold;
    public event Action<bool> Saved; // (automático)
    public event Action<string> BigMessage; // anúncios de destaque no centro da tela

    public const float AutosaveInterval = 45f;
    float _autosaveTimer;
    public double LastSaveTime { get; private set; } = -1;

    public override void _EnterTree()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        InputSetup.Register();
        Settings.Load();
    }

    public override void _Ready()
    {
        Settings.Apply(GetTree());
    }

    public override void _Process(double delta)
    {
        if (!IsPlaying || GetTree().Paused) return;
        Data.PlayTimeSeconds += delta;
        TickDay(delta);
        _autosaveTimer += (float)delta;
        if (_autosaveTimer >= AutosaveInterval)
        {
            _autosaveTimer = 0;
            SaveGame(true);
        }
    }

    // ================= Estatísticas derivadas dos upgrades =================
    public float StatValue(string id) => GameData.Upgrades[id].ValueAt(Data.Level(id));
    /// <summary>Multiplicadores temporários de eventos de região (não persistem).</summary>
    public float EventDamageMultiplier { get; set; } = 1f;
    public float EventShardMultiplier { get; set; } = 1f;
    /// <summary>Bônus permanente da Essência Ancestral: +10% por ponto.</summary>
    public float EssenceMultiplier => 1f + Data.Essence * EssenceBonusPerPoint;
    public const float EssenceBonusPerPoint = 0.10f;
    public float Damage => StatValue(UpgradeIds.Damage) * EventDamageMultiplier * EssenceMultiplier
                           * HouseMul(HouseIds.Workbench) * (HasSkill(SkillIds.FirmStrike) ? 1.15f : 1f);
    public float SwingsPerSecond => StatValue(UpgradeIds.Speed) * (HasSkill(SkillIds.Cadence) ? 1.10f : 1f);
    public float SwingInterval => 1f / SwingsPerSecond;
    public int Capacity => (int)(StatValue(UpgradeIds.Capacity) * HouseMul(HouseIds.Wardrobe) * (HasSkill(SkillIds.Compactor) ? 1.25f : 1f));
    public float MoveSpeed => StatValue(UpgradeIds.Move) * HouseMul(HouseIds.Kitchen);
    public float ValueMultiplier => StatValue(UpgradeIds.Value) * EssenceMultiplier
                                    * HouseMul(HouseIds.Trophies) * (HasSkill(SkillIds.Haggle) ? 1.10f : 1f);
    public float MagnetRadius => StatValue(UpgradeIds.Reach) * (HasSkill(SkillIds.Magnet) ? 1.3f : 1f);
    public float CritChance => HasSkill(SkillIds.Critical) ? 0.12f : 0f;
    public const float CritMultiplier = 3f;
    public float RareChanceMultiplier => HasSkill(SkillIds.RareNose) ? 1.5f : 1f;
    public bool DirectCollect => HasSkill(SkillIds.DirectCollect);
    public float MiningReach => 3.2f + Data.Level(UpgradeIds.Reach) * 0.28f;
    public float DamagePerSecond => Damage * SwingsPerSecond;
    public float ResonanceMultiplier => StatValue(UpgradeIds.Resonance);
    public float ResonanceRadius => GameData.ResonanceRadius(ResonanceMultiplier);

    /// <summary>Nível de "tier" visual da ferramenta (0..4) baseado em dano.</summary>
    public int ToolTier
    {
        get
        {
            int l = Data.Level(UpgradeIds.Damage);
            return l >= 24 ? 5 : l >= 18 ? 4 : l >= 12 ? 3 : l >= 7 ? 2 : l >= 3 ? 1 : 0;
        }
    }

    public static readonly string[] ToolTierNames =
        { "Broca Inicial", "Cortador de Pulso", "Perfurador de Arco", "Ressonador Cristalino", "Lança Prismática", "Quebra-Núcleo" };

    // ================= Inventário =================
    public int InventoryCount => Data.Inventory.Values.Sum();
    public int FreeSpace => Math.Max(0, Capacity - InventoryCount);
    public bool InventoryFull => InventoryCount >= Capacity;

    /// <summary>Adiciona fragmentos respeitando a capacidade. Retorna quantos foram aceitos.</summary>
    public int AddItem(string materialId, int amount = 1)
    {
        var def = GameData.Mat(materialId);
        if (def == null || amount <= 0) return 0;
        int accepted = Math.Min(amount, FreeSpace);
        if (accepted <= 0) return 0;
        Data.Inventory[materialId] = (Data.Inventory.TryGetValue(materialId, out int c) ? c : 0) + accepted;
        if (!Data.Discovered.Contains(materialId))
        {
            Data.Discovered.Add(materialId);
            MaterialDiscovered?.Invoke(def);
            Notify(def.Rarity >= Rarity.Epico ? $"Material {RarityInfo.Name(def.Rarity)} descoberto!" : "Novo material descoberto",
                $"{def.Name} — {FormatMoney(SellPrice(def))} por fragmento",
                def.Rare || def.Special ? NotifyKind.Rare : NotifyKind.Info);
        }
        InventoryChanged?.Invoke();
        return accepted;
    }

    public long SellPrice(MaterialDef def) =>
        (long)Math.Round(def.Value * ValueMultiplier * ((def.Rare || def.Special) && HasSkill(SkillIds.Jeweler) ? 1.5f : 1f));

    public long InventoryValue() =>
        Data.Inventory.Sum(kv => GameData.Mat(kv.Key) is { } m ? SellPrice(m) * kv.Value : 0);

    /// <summary>Vende todo o inventário. Retorna o valor recebido.</summary>
    public long SellAll()
    {
        long total = InventoryValue();
        int count = InventoryCount;
        if (count == 0) return 0;
        Data.Inventory.Clear();
        Data.ShardsSold += count;
        Data.DayEarned += total;
        AddMoney(total);
        InventoryChanged?.Invoke();
        Sold?.Invoke(total);
        return total;
    }

    public void AddMoney(long amount)
    {
        if (amount <= 0) return;
        Data.Money += amount;
        Data.TotalEarned += amount;
        Data.RunEarned += amount;
        MoneyChanged?.Invoke();
    }

    // ================= Upgrades =================
    public long UpgradeCost(string id) => GameData.Upgrades[id].CostAt(Data.Level(id));
    public bool IsMaxed(string id) => Data.Level(id) >= GameData.Upgrades[id].MaxLevel;
    public bool CanAfford(long cost) => cost != long.MaxValue && Data.Money >= cost;

    public bool TryBuyUpgrade(string id)
    {
        if (!GameData.Upgrades.ContainsKey(id) || IsMaxed(id)) return false;
        if (!RequireWork()) return false;
        long cost = UpgradeCost(id);
        if (!CanAfford(cost)) return false;
        int prevTier = ToolTier;
        Data.Money -= cost;
        Data.Upgrades[id] = Data.Level(id) + 1;
        MoneyChanged?.Invoke();
        UpgradesChanged?.Invoke();
        if (ToolTier > prevTier)
        {
            Notify("Ferramenta evoluiu!", ToolTierNames[ToolTier], NotifyKind.Unlock);
            BigMessage?.Invoke(ToolTierNames[ToolTier].ToUpper());
        }
        return true;
    }

    // ================= Regiões =================
    public bool IsUnlocked(string regionId) => Data.UnlockedRegions.Contains(regionId);

    /// <summary>Próxima região bloqueada, ou null.</summary>
    public RegionDef NextLockedRegion() => GameData.Regions.FirstOrDefault(r => !IsUnlocked(r.Id));

    public bool CanUnlock(RegionDef r)
    {
        if (r == null || IsUnlocked(r.Id)) return false;
        // exige a região anterior
        if (r.Index > 0 && !IsUnlocked(GameData.Regions[r.Index - 1].Id)) return false;
        return Data.Money >= r.UnlockCost;
    }

    public bool TryUnlockRegion(string regionId)
    {
        var r = GameData.Region(regionId);
        if (!CanUnlock(r)) return false;
        if (!RequireWork()) return false;
        Data.Money -= r.UnlockCost;
        Data.UnlockedRegions.Add(r.Id);
        MoneyChanged?.Invoke();
        RegionsChanged?.Invoke();
        Notify("Região desbloqueada!", r.Name, NotifyKind.Unlock);
        BigMessage?.Invoke("NOVA REGIÃO: " + r.Name.ToUpper());
        SaveGame(true);
        return true;
    }

    // ================= Turno (ciclo do dia) =================
    /// <summary>Duração do turno em segundos reais (06:00 → 18:00 no relógio do jogo).</summary>
    public const float DefaultDayLength = 600f;
    /// <summary>Permite a testes/bot encurtar o dia.</summary>
    public static float? DayLengthOverride;
    public float DayLength => DayLengthOverride ?? DefaultDayLength
        + HouseLevel(HouseIds.Bed) * HomeData.HouseDef(HouseIds.Bed).PerLevel
        + (HasSkill(SkillIds.Early) ? 120f : 0f) + (HasSkill(SkillIds.Overtime) ? 120f : 0f);
    public const float DayWarningSeconds = 60f;
    public bool DayOver => Data.DayOver;
    /// <summary>Fora do turno o jogador só pode vender e voltar para casa.</summary>
    public bool CanWork => !Data.DayOver;
    public float DayProgress => Math.Clamp((float)(Data.DayTimeSeconds / DayLength), 0f, 1f);
    public float DayRemaining => Math.Max(0f, DayLength - (float)Data.DayTimeSeconds);
    public event Action DayEnded;
    public event Action DayStarted;
    public const int DayStartHour = 6, DayEndHour = 18;

    /// <summary>Relógio do jogo (HH:MM), de 06:00 a 18:00 ao longo do turno.</summary>
    public string ClockText
    {
        get
        {
            int minutes = (int)(DayStartHour * 60 + DayProgress * (DayEndHour - DayStartHour) * 60);
            minutes -= minutes % 10;
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }
    }

    public void TickDay(double delta)
    {
        if (Data.DayOver) return;
        double before = Data.DayTimeSeconds;
        Data.DayTimeSeconds += delta;
        double warnAt = DayLength - DayWarningSeconds;
        if (before < warnAt && Data.DayTimeSeconds >= warnAt && DayLength > DayWarningSeconds * 2)
            Notify("O turno está acabando", "Falta 1 minuto. Leve os minérios à Estação de Venda no acampamento.", NotifyKind.Warning);
        if (Data.DayTimeSeconds >= DayLength) EndDay();
    }

    /// <summary>Encerra o turno: a mineração, a loja e os portões ficam bloqueados até o próximo dia.</summary>
    public void EndDay(bool early = false)
    {
        if (Data.DayOver) return;
        Data.DayOver = true;
        if (!early)
        {
            Data.DayTimeSeconds = Math.Max(Data.DayTimeSeconds, DayLength);
            BigMessage?.Invoke("FIM DO TURNO");
            Notify("Turno encerrado", "Venda seus minérios e pegue o elevador para casa.", NotifyKind.Warning);
        }
        DayEnded?.Invoke();
        SaveGame(true);
    }

    /// <summary>Começa um novo dia (depois de dormir em casa).</summary>
    public void StartNewDay()
    {
        Data.Day++;
        Data.DayTimeSeconds = 0;
        Data.DayOver = false;
        Data.DayEarned = 0;
        Data.DayCrystals = 0;
        Data.DayBonus = 0;
        DayStarted?.Invoke();
        SaveGame(true);
    }

    /// <summary>Prêmio do turno (habilidade): +10% do ganho do dia, pago uma vez ao chegar em casa.</summary>
    public long PayShiftBonus()
    {
        if (!HasSkill(SkillIds.ShiftBonus) || Data.DayBonus > 0 || Data.DayEarned <= 0) return 0;
        long bonus = Math.Max(1, Data.DayEarned / 10);
        Data.DayBonus = bonus;
        AddMoney(bonus);
        return bonus;
    }

    // ================= Goblins mineradores (um por região) =================
    public enum GoblinMood { Available = 0, Working = 1, Fled = 2, Rebel = 3 }
    public enum FeeOutcome { Paid, Forgiven, Warned, Fled, Rebel, NoFee }
    public const int GoblinRehireDays = 3;
    public event Action GoblinChanged;

    public GoblinSave GoblinData(string id)
    {
        if (!Data.Goblins.TryGetValue(id, out var g)) { g = new GoblinSave(); Data.Goblins[id] = g; }
        return g;
    }
    public GoblinMood Goblin(string id) => (GoblinMood)GoblinData(id).State;
    public bool GoblinFound(string id) => GoblinData(id).Found;
    public int GoblinStashCount(string id) => GoblinData(id).Stash.Values.Sum();
    public long GoblinStashValue(string id) => GoblinData(id).Stash.Sum(kv => GameData.Mat(kv.Key) is { } m ? SellPrice(m) * kv.Value : 0);

    /// <summary>Taxa do dia: um quarto do que ele minerou (com mínimo por goblin).</summary>
    public long GoblinFee(string id) => Goblin(id) == GoblinMood.Working ? Math.Max(Data_.Of(id).MinFee, GoblinData(id).DayValue / 4) : 0;
    public bool GoblinFeeDue(string id) => Goblin(id) == GoblinMood.Working && GoblinData(id).ChargedDay != Data.Day;
    public bool CanRehireGoblin(string id) => Goblin(id) == GoblinMood.Fled && Data.Day >= GoblinData(id).FledDay + GoblinRehireDays;
    public long GoblinHirePrice(string id) => Goblin(id) == GoblinMood.Fled ? Data_.Of(id).RehireCost : Data_.Of(id).HireCost;
    /// <summary>Goblins com taxa a cobrar hoje (na ordem das regiões).</summary>
    public List<GoblinDef> DueGoblins() => Data_.All.Where(d => GoblinFeeDue(d.RegionId)).ToList();
    public bool AnyGoblinWorking => Data_.All.Any(d => Goblin(d.RegionId) == GoblinMood.Working);

    /// <summary>O jogador encontrou o goblin na câmara secreta (uma vez). Devolve true na primeira vez.</summary>
    public bool MarkGoblinFound(string id)
    {
        var g = GoblinData(id);
        if (g.Found) return false;
        g.Found = true;
        GoblinChanged?.Invoke();
        SaveGame(true);
        return true;
    }

    public bool TryHireGoblin(string id)
    {
        var def = Data_.Of(id);
        var g = GoblinData(id);
        bool ok = g.Found && (Goblin(id) == GoblinMood.Available || CanRehireGoblin(id));
        long price = GoblinHirePrice(id);
        if (!ok || !CanAfford(price)) return false;
        Data.Money -= price;
        g.State = (int)GoblinMood.Working;
        g.Strikes = 0;
        g.DayValue = 0;
        g.ChargedDay = 0;
        MoneyChanged?.Invoke();
        GoblinChanged?.Invoke();
        Notify($"{def.Name} contratado!", "Ele vai montar acampamento perto da entrada da região, minerar e guardar tudo no baú. Cobra uma taxa no fim do dia.", NotifyKind.Success);
        return true;
    }

    /// <summary>O goblin quebrou um depósito: os fragmentos vão para o baú dele.</summary>
    public void GoblinMined(string id, MaterialDef m, int count)
    {
        if (count <= 0) return;
        var g = GoblinData(id);
        g.Stash[m.Id] = (g.Stash.TryGetValue(m.Id, out int n) ? n : 0) + count;
        g.DayValue += SellPrice(m) * count;
        GoblinChanged?.Invoke();
    }

    /// <summary>Passa o conteúdo do baú para a mochila (até encher). Devolve quantos foram pegos.</summary>
    public int CollectGoblinStash(string id)
    {
        var g = GoblinData(id);
        int moved = 0;
        foreach (var key in g.Stash.Keys.ToList())
        {
            int take = Math.Min(g.Stash[key], FreeSpace);
            if (take <= 0) break;
            int added = AddItem(key, take);
            moved += added;
            g.Stash[key] -= added;
            if (g.Stash[key] <= 0) g.Stash.Remove(key);
        }
        if (moved > 0) GoblinChanged?.Invoke();
        return moved;
    }

    /// <summary>
    /// Cobrança do fim do dia. Pagar zera as faltas. Sem pagar: 1ª falta ele perdoa, 2ª avisa,
    /// a partir da 3ª ele foge ou se rebela (sorteio estável pelo dia; forceRebel para testes).
    /// </summary>
    public FeeOutcome SettleGoblinFee(string id, bool pay, bool? forceRebel = null)
    {
        if (!GoblinFeeDue(id)) return FeeOutcome.NoFee;
        var g = GoblinData(id);
        long fee = GoblinFee(id);
        g.ChargedDay = Data.Day;
        g.DayValue = 0;
        FeeOutcome result;
        if (pay && CanAfford(fee))
        {
            Data.Money -= fee;
            g.Strikes = 0;
            MoneyChanged?.Invoke();
            result = FeeOutcome.Paid;
        }
        else
        {
            g.Strikes++;
            if (g.Strikes == 1) result = FeeOutcome.Forgiven;
            else if (g.Strikes == 2) result = FeeOutcome.Warned;
            else
            {
                bool rebel = forceRebel ?? (new Random(Data.Day * 7919 + Data.PrestigeCount + id.Length * 31).NextDouble() < 0.5);
                if (rebel) { g.State = (int)GoblinMood.Rebel; result = FeeOutcome.Rebel; }
                else { g.State = (int)GoblinMood.Fled; g.FledDay = Data.Day; result = FeeOutcome.Fled; }
            }
        }
        GoblinChanged?.Invoke();
        return result;
    }

    /// <summary>Fim da rebelião: vencida → volta a trabalhar envergonhado; perdida → os goblins fogem com o baú.</summary>
    public void EndGoblinRebellion(string id, bool playerWon)
    {
        if (Goblin(id) != GoblinMood.Rebel) return;
        var g = GoblinData(id);
        g.Strikes = 0;
        if (playerWon) g.State = (int)GoblinMood.Working;
        else { g.State = (int)GoblinMood.Fled; g.FledDay = Data.Day; g.Stash.Clear(); }
        GoblinChanged?.Invoke();
    }

    /// <summary>Goblins roubam parte da mochila (quando o jogador desmaia).</summary>
    public int StealFromInventory(float fraction)
    {
        int stolen = 0;
        foreach (var key in Data.Inventory.Keys.ToList())
        {
            int n = (int)Math.Ceiling(Data.Inventory[key] * fraction);
            n = Math.Min(n, Data.Inventory[key]);
            Data.Inventory[key] -= n;
            stolen += n;
            if (Data.Inventory[key] <= 0) Data.Inventory.Remove(key);
        }
        if (stolen > 0) InventoryChanged?.Invoke();
        return stolen;
    }

    // ================= Casa e habilidades =================
    public event Action HomeChanged;
    public int HouseLevel(string id) => Data.House.TryGetValue(id, out int l) ? l : 0;
    float HouseMul(string id) => 1f + HouseLevel(id) * HomeData.HouseDef(id).PerLevel;
    public long HouseCost(string id) => HomeData.HouseDef(id).CostAt(HouseLevel(id));
    public bool HouseMaxed(string id) => HouseLevel(id) >= HomeData.HouseDef(id).MaxLevel;

    public bool TryBuyHouse(string id)
    {
        var def = HomeData.HouseDef(id);
        if (def == null || HouseMaxed(id)) return false;
        long cost = HouseCost(id);
        if (!CanAfford(cost)) return false;
        Data.Money -= cost;
        Data.House[id] = HouseLevel(id) + 1;
        MoneyChanged?.Invoke();
        UpgradesChanged?.Invoke();
        HomeChanged?.Invoke();
        return true;
    }

    public bool HasSkill(string id) => Data.Skills.Contains(id);
    public bool SkillAvailable(string id)
    {
        var def = HomeData.Skill(id);
        return def != null && !HasSkill(id) && (def.Requires == null || HasSkill(def.Requires));
    }

    public bool TryBuySkill(string id)
    {
        var def = HomeData.Skill(id);
        if (!SkillAvailable(id) || !CanAfford(def.Cost)) return false;
        Data.Money -= def.Cost;
        Data.Skills.Add(id);
        MoneyChanged?.Invoke();
        UpgradesChanged?.Invoke();
        InventoryChanged?.Invoke(); // capacidade pode mudar
        HomeChanged?.Invoke();
        return true;
    }

    /// <summary>Avisa e devolve false quando a ação exige o turno aberto.</summary>
    public bool RequireWork()
    {
        if (CanWork) return true;
        Notify("Turno encerrado", "Agora você só pode vender e voltar para casa pelo elevador.", NotifyKind.Warning);
        return false;
    }

    // ================= Sessão / Save =================
    public void NewGame()
    {
        Data = new SaveData();
        Data.Sanitize();
        _autosaveTimer = 0;
        SessionActive = true;
        RaiseAll();
    }

    /// <summary>Carrega o save existente. Retorna false (e inicia novo jogo) se não houver save válido.</summary>
    public bool LoadGame()
    {
        var loaded = SaveSystem.Load(out string msg);
        if (!string.IsNullOrEmpty(msg)) Notify("Salvamento", msg, NotifyKind.Warning);
        if (loaded == null) { NewGame(); return false; }
        Data = loaded;
        _autosaveTimer = 0;
        SessionActive = true;
        RaiseAll();
        return true;
    }

    public Func<SaveData, bool> BeforeSave; // permite à cena gravar posição do jogador

    /// <summary>
    /// True somente depois de NewGame()/LoadGame(). Antes disso (menu ao abrir o jogo) os dados em memória
    /// são um placeholder vazio e NUNCA podem ser gravados por cima do save do jogador.
    /// </summary>
    public bool SessionActive { get; private set; }

    /// <summary>Simula o estado logo após abrir o jogo (só para testes).</summary>
    public void ResetToBootStateForTests() { Data = new SaveData(); SessionActive = false; }

    public bool SaveGame(bool auto = false)
    {
        if (!SessionActive)
        {
            GD.Print("[Salvamento] ignorado: nenhuma partida carregada (protege o save existente).");
            return false;
        }
        BeforeSave?.Invoke(Data);
        bool ok = SaveSystem.Save(Data);
        if (ok) { LastSaveTime = Time.GetTicksMsec() / 1000.0; Saved?.Invoke(auto); }
        if (!auto || !ok)
            Notify(ok ? "Jogo salvo" : "Erro ao salvar", ok ? "Progresso gravado com sucesso." : "Verifique o espaço em disco.",
                ok ? NotifyKind.Success : NotifyKind.Warning);
        return ok;
    }

    /// <summary>Encerra o jogo de forma limpa: libera caches estáticos e coleta o lixo gerenciado antes de sair.</summary>
    public async void QuitClean(int exitCode = 0)
    {
        World.MeshFactory.ClearCaches();
        World.CaveTerrain.ClearCaches();
        Mining.ShardManager.ClearCaches();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GC.Collect();
        GetTree().Quit(exitCode);
    }

    public void Notify(string title, string body, NotifyKind kind = NotifyKind.Info) => Notified?.Invoke(title, body, kind);

    public void RaiseAll()
    {
        MoneyChanged?.Invoke();
        InventoryChanged?.Invoke();
        UpgradesChanged?.Invoke();
        RegionsChanged?.Invoke();
    }

    public void RecordCrystalMined() { Data.CrystalsMined++; Data.DayCrystals++; }

    /// <summary>Jornada concluída: todas as regiões liberadas e o Coração Ancestral destruído.</summary>
    public bool JourneyComplete => GameData.Regions.All(r => IsUnlocked(r.Id)) && Data.Discovered.Contains("heart");

    public event Action Completed;
    public event Action Prestiged;

    /// <summary>Essência que seria obtida ao renascer agora: raiz dos ganhos da partida (em blocos de 250 mil).</summary>
    public int PendingEssence => (int)Math.Floor(Math.Sqrt(Data.RunEarned / 250_000.0));
    public bool CanPrestige => Data.GameCompleted && PendingEssence > 0;

    /// <summary>
    /// Renascimento: converte ganhos da partida em Essência e recomeça a progressão.
    /// Mantém Coleção, estatísticas, Essência e contagem de renascimentos.
    /// </summary>
    public bool Prestige()
    {
        if (!CanPrestige) return false;
        int gained = PendingEssence;
        Data.Essence += gained;
        Data.PrestigeCount++;
        Data.Money = 0;
        Data.RunEarned = 0;
        Data.Upgrades.Clear();
        Data.Skills.Clear(); // a árvore recomeça; a casa é permanente
        Data.Inventory.Clear();
        Data.UnlockedRegions.Clear();
        Data.UnlockedRegions.Add("caves");
        Data.GameCompleted = false;
        Data.HasPosition = false;
        RaiseAll();
        Notify("Renascimento!", $"+{gained} Essência Ancestral · bônus permanente x{EssenceMultiplier:0.0}", NotifyKind.Unlock);
        BigMessage?.Invoke($"RENASCIMENTO {Data.PrestigeCount}");
        Prestiged?.Invoke();
        SaveGame(true);
        return true;
    }

    /// <summary>Verifica a conclusão (dispara uma única vez por save).</summary>
    public void CheckCompletion()
    {
        if (Data.GameCompleted || !JourneyComplete) return;
        Data.GameCompleted = true;
        Data.CompletedAtSeconds = Data.PlayTimeSeconds;
        Completed?.Invoke();
        SaveGame(true);
    }

    // ================= Utilidades =================
    public static string FormatMoney(long v) => "◈ " + FormatNumber(v);

    public static string FormatNumber(double v)
    {
        if (v < 10000) return ((long)v).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
        string[] suf = { "", "K", "M", "B", "T", "Qa" };
        int i = 0;
        while (v >= 1000 && i < suf.Length - 1) { v /= 1000; i++; }
        return (v < 100 ? v.ToString("0.0#") : v.ToString("0")) + suf[i];
    }
}
