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
        long __t = Core.Prof.Start();
        _ProcessInner(delta);
        Core.Prof.End("gamestate", __t);
    }

    void _ProcessInner(double delta)
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
    // ---------------- ferramenta ----------------
    public ToolDef CurrentTool => ToolData.Of(Data.Tool);
    public bool ToolBroken => Data.ToolBroken;
    /// <summary>Golpes até quebrar (com o bônus do Cabo reforçado).</summary>
    public int ToolMaxDurability => (int)(CurrentTool.Durability * (HasSkill(SkillIds.ToolGrip) ? 1.25f : 1f) * HouseMul(HouseIds.ToolRack));
    public int ToolDurabilityLeft => Math.Max(0, (int)Math.Ceiling(ToolMaxDurability - Data.ToolWear));
    public float ToolDurability01 => ToolBroken ? 0f : ToolDurabilityLeft / (float)Math.Max(1, ToolMaxDurability);
    /// <summary>Ferramenta laser na mão (gasta munição em vez de desgastar).</summary>
    public bool IsLaser => CurrentTool.Kind == ToolKind.Laser && !ToolBroken;
    public bool OutOfAmmo => IsLaser && Data.Ammo <= 0;
    /// <summary>Sem ferramenta utilizável (quebrou ou laser sem munição): minera com as mãos.</summary>
    public bool UsingHands => ToolBroken || OutOfAmmo;
    /// <summary>Golpes por clique (habilidade Golpe triplo).</summary>
    public int HitsPerClick => HasSkill(SkillIds.TripleTap) ? 3 : 1;
    /// <summary>Segurar o botão continua minerando (habilidade Mineração contínua).</summary>
    public bool HoldToMine => HasSkill(SkillIds.HoldMine);
    /// <summary>O pulso ressonante só existe nas ferramentas a laser (com munição).</summary>
    public bool ResonanceActive => IsLaser && !OutOfAmmo;
    public float BaseDamage => UsingHands ? ToolData.HandsDamage : CurrentTool.Damage;
    public float BaseSwings => UsingHands ? ToolData.HandsSwings : CurrentTool.Swings;
    /// <summary>O Amplificador de Ressonância só aparece no Terminal quando já existe um laser para usar.</summary>
    public bool UpgradeShown(string id) => id != UpgradeIds.Resonance || AnyLaserAvailable;
    public bool AnyLaserAvailable => ToolData.All.Any(t => t.Kind == ToolKind.Laser && ToolAvailable(t.Index));

    /// <summary>Compra um pacote de células de energia para o laser.</summary>
    public bool TryBuyAmmo()
    {
        if (!AnyLaserAvailable || !RequireWork() || !CanAfford(ToolData.AmmoPackCost)) return false;
        Data.Money -= ToolData.AmmoPackCost;
        Data.Ammo += ToolData.AmmoPack;
        MoneyChanged?.Invoke();
        UpgradesChanged?.Invoke();
        ToolChanged?.Invoke();
        Notify("Munição do laser", Loc.F("+{0} células de energia (total {1})", ToolData.AmmoPack, Data.Ammo), NotifyKind.Success);
        return true;
    }
    public event Action ToolChanged;

    public float Damage => BaseDamage * EventDamageMultiplier * EssenceMultiplier
                           * HouseMul(HouseIds.Workbench) * (HasSkill(SkillIds.FirmStrike) ? 1.15f : 1f)
                           * (!UsingHands && HasSkill(SkillIds.Temper) ? 1.15f : 1f) * (1f + GearBonus(GearSlot.Gloves));
    public float SwingsPerSecond => BaseSwings * (HasSkill(SkillIds.Cadence) ? 1.10f : 1f) * (!UsingHands && HasSkill(SkillIds.Hone) ? 1.10f : 1f);
    public float SwingInterval => 1f / SwingsPerSecond;
    public int Capacity => (int)(StatValue(UpgradeIds.Capacity) * HouseMul(HouseIds.Wardrobe) * HouseMul(HouseIds.Chest) * (HasSkill(SkillIds.Compactor) ? 1.25f : 1f) * (1f + GearBonus(GearSlot.Bag)));
    public float MoveSpeed => StatValue(UpgradeIds.Move) * HouseMul(HouseIds.Kitchen) * (HasSkill(SkillIds.LightBoots) ? 1.08f : 1f) * (1f + GearBonus(GearSlot.Boots));
    public float ValueMultiplier => StatValue(UpgradeIds.Value) * EssenceMultiplier
                                    * HouseMul(HouseIds.Trophies) * (HasSkill(SkillIds.Haggle) ? 1.10f : 1f) * (HasSkill(SkillIds.LoyalBuyer) ? 1.10f : 1f);
    public float MagnetRadius => StatValue(UpgradeIds.Reach) * (HasSkill(SkillIds.Magnet) ? 1.3f : 1f);
    public float CritChance => HasSkill(SkillIds.Critical) ? 0.12f : 0f;
    public float CritMultiplier => HasSkill(SkillIds.Piercing) ? 4f : 3f;
    /// <summary>Chance de 1 fragmento a mais por depósito (Bolsos fundos).</summary>
    public float ExtraShardChance => HasSkill(SkillIds.ExtraShards) ? 0.2f : 0f;
    /// <summary>Multiplicador do tempo de renascimento dos depósitos (Geólogo).</summary>
    public float RespawnMultiplier => HasSkill(SkillIds.Geologist) ? 0.7f : 1f;
    /// <summary>Alcance da lanterna do capacete (Lanterna potente).</summary>
    public float LampRangeMultiplier => (HasSkill(SkillIds.BrightLamp) ? 1.35f : 1f) * HouseMul(HouseIds.Lanterns) * (1f + GearBonus(GearSlot.Helmet));
    /// <summary>Preço de uma construção para o jogador (Kit de cordas: cordas pela metade).</summary>
    public long BuildCost(string type) => BuildData.Of(type) is { } bd ? (type == BuildIds.Rope && HasSkill(SkillIds.RopeKit) ? bd.Cost / 2 : bd.Cost) : 0;
    public float RareChanceMultiplier => (HasSkill(SkillIds.RareNose) ? 1.5f : 1f) * (1f + (GearData.Of(GearSlot.Helmet, GearTier(GearSlot.Helmet))?.Bonus2 ?? 0f));
    public bool DirectCollect => HasSkill(SkillIds.DirectCollect);
    public float MiningReach => 3.2f + Data.Level(UpgradeIds.Reach) * 0.28f;
    public float DamagePerSecond => Damage * SwingsPerSecond;
    public float ResonanceMultiplier => StatValue(UpgradeIds.Resonance);
    public float ResonanceRadius => GameData.ResonanceRadius(ResonanceMultiplier);

    /// <summary>Ferramenta equipada (índice) — também define o visual em primeira pessoa.</summary>
    public int ToolTier => Data.Tool;

    public static string ToolName(int tier) => ToolData.Of(tier).Name;
    public string ToolLabel => ToolBroken ? Loc.F("{0} (quebrada)", CurrentTool.Name) : OutOfAmmo ? Loc.F("{0} (sem munição)", CurrentTool.Name) : CurrentTool.Name;

    /// <summary>A ferramenta pode ser comprada agora (região do minério dela liberada)?</summary>
    public bool ToolAvailable(int i) => i >= 0 && i < ToolData.All.Count && IsUnlocked(GameData.Regions[Math.Clamp(ToolData.All[i].Region, 0, GameData.Regions.Count - 1)].Id);

    /// <summary>Compra (ou recompra) uma ferramenta: vem nova, com durabilidade cheia.</summary>
    public bool TryBuyTool(int i)
    {
        if (!ToolAvailable(i)) return false;
        if (!RequireWork()) return false;
        var t = ToolData.All[i];
        if (!CanAfford(t.Cost)) return false;
        bool upgrade = i > Data.Tool || ToolBroken;
        Data.Money -= t.Cost;
        Data.Tool = i;
        Data.ToolWear = 0;
        Data.ToolBroken = false;
        if (t.Kind == ToolKind.Laser) Data.Ammo += ToolData.AmmoWithLaser;
        MoneyChanged?.Invoke();
        UpgradesChanged?.Invoke();
        ToolChanged?.Invoke();
        Notify(Loc.F("{0} equipada", t.Name), Loc.F("{0} de dano · {1:0.0} golpes/s · {2} de resistência", FormatNumber(t.Damage), t.Swings, t.Durability), NotifyKind.Success);
        if (upgrade) BigMessage?.Invoke(t.Name.ToUpper());
        return true;
    }

    /// <summary>Admin/testes: equipa uma ferramenta nova (sem cobrar) ou quebra a atual.</summary>
    public void DebugSetTool(int i, bool broken = false)
    {
        Data.Tool = Math.Clamp(i, 0, ToolData.All.Count - 1);
        Data.ToolWear = broken ? ToolMaxDurability : 0;
        Data.ToolBroken = broken;
        UpgradesChanged?.Invoke();
        ToolChanged?.Invoke();
    }

    /// <summary>Um golpe que acertou um depósito gasta a ferramenta; no fim da durabilidade ela quebra.</summary>
    /// <summary>
    /// Golpe de picareta/broca que acertou um minério: gasta a resistência na proporção do dano
    /// (o minério inteiro tira ToolData.WearFor). Laser: cada disparo gasta uma célula.
    /// </summary>
    public void WearTool(MaterialDef ore, float fractionOfOre)
    {
        if (ToolBroken) return;
        if (CurrentTool.Kind == ToolKind.Laser) { WearTool(1); return; }
        AddWear(ToolData.WearFor(ore) * Math.Clamp(fractionOfOre, 0f, 1f));
    }

    void AddWear(float amount)
    {
        float max = ToolMaxDurability;
        float before = Data.ToolWear;
        Data.ToolWear += amount;
        if (before < max * 0.8f && Data.ToolWear >= max * 0.8f && Data.ToolWear < max)
            Notify("Ferramenta desgastada", Loc.F("{0}: resta {1} de resistência. Compre outra no Terminal.", CurrentTool.Name, ToolDurabilityLeft), NotifyKind.Warning);
        if (Data.ToolWear >= max)
        {
            Data.ToolBroken = true;
            Notify(Loc.F("{0} quebrou!", CurrentTool.Name), Loc.T("Você está minerando com as mãos. Volte ao Terminal e compre outra ferramenta."), NotifyKind.Warning);
            BigMessage?.Invoke(Loc.T("FERRAMENTA QUEBROU"));
            UpgradesChanged?.Invoke();
            ToolChanged?.Invoke();
        }
    }

    /// <summary>Gasta resistência direto (testes/admin) ou células do laser.</summary>
    public void WearTool(int hits = 1)
    {
        if (ToolBroken) return;
        if (CurrentTool.Kind == ToolKind.Laser)
        {
            // laser não desgasta: cada disparo gasta uma célula de energia
            if (Data.Ammo <= 0) return;
            int before = Data.Ammo;
            int spent = hits;
            if (HasSkill(SkillIds.CellSaver)) { spent = 0; for (int k = 0; k < hits; k++) if (Random.Shared.NextDouble() >= 0.25) spent++; }
            Data.Ammo = Math.Max(0, Data.Ammo - spent);
            if (before > 20 && Data.Ammo <= 20) Notify("Munição acabando", Loc.F("Restam {0} células de energia. Compre mais no Terminal.", Data.Ammo), NotifyKind.Warning);
            if (Data.Ammo == 0)
            {
                Notify("Laser sem munição!", Loc.T("Você está minerando com as mãos. Compre células de energia no Terminal."), NotifyKind.Warning);
                BigMessage?.Invoke(Loc.T("SEM MUNIÇÃO"));
                UpgradesChanged?.Invoke();
                ToolChanged?.Invoke();
            }
            return;
        }
        AddWear(hits);
    }

    /// <summary>
    /// Saves de antes das ferramentas: o dano vinha da melhoria "Potência do Feixe". O jogador recebe
    /// a melhor ferramenta liberada que não passa do dano que já tinha (nova, durabilidade cheia).
    /// </summary>
    public void MigrateTools()
    {
        // desgaste antigo (golpes) vira resistência na mesma proporção
        if (Data.WearVersion < 1)
        {
            Data.WearVersion = 1;
            int t = Math.Clamp(Data.Tool, 0, ToolData.OldHitDurability.Length - 1);
            if (Data.ToolWear > 0 && !Data.ToolBroken)
                Data.ToolWear = Math.Min(0.95f, Data.ToolWear / ToolData.OldHitDurability[t]) * Math.Max(1, ToolData.Of(Data.Tool).Durability);
            else if (Data.ToolBroken) Data.ToolWear = ToolData.Of(Data.Tool).Durability;
        }
        if (Data.ToolsMigrated) return;
        Data.ToolsMigrated = true;
        int lvl = Data.Level(UpgradeIds.Damage);
        if (lvl <= 0) { Data.Tool = 0; return; }
        float old = GameData.Upgrades[UpgradeIds.Damage].ValueAt(lvl);
        int best = 0;
        for (int i = 0; i < ToolData.All.Count; i++)
            if (ToolData.All[i].Damage <= old * 1.05f && ToolAvailable(i)) best = i;
        Data.Tool = best;
        Data.ToolWear = 0;
        Data.ToolBroken = false;
    }

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
        Discover(def);
        InventoryChanged?.Invoke();
        return accepted;
    }

    /// <summary>Registra um material na Coleção (primeira vez que é coletado).</summary>
    void Discover(MaterialDef def)
    {
        if (Data.Discovered.Contains(def.Id)) return;
        Data.Discovered.Add(def.Id);
        MaterialDiscovered?.Invoke(def);
        Notify(def.Rarity >= Rarity.Epico ? Loc.F("Material {0} descoberto!", RarityInfo.Name(def.Rarity)) : Loc.T("Novo material descoberto"),
            Loc.F("{0} — {1} por fragmento", def.Name, FormatMoney(SellPrice(def))),
            def.Rare || def.Special ? NotifyKind.Rare : NotifyKind.Info);
    }

    // ================= Construções =================
    public event Action BuildingsChanged;
    /// <summary>Venda feita pela esteira (valor, material).</summary>
    public event Action<long, MaterialDef> BeltSold;

    public int BuildingCount(string type) => Data.Buildings.Count(b => b.Type == type);
    /// <summary>O item de construção já foi liberado na árvore (ramo Construção)?</summary>
    public bool BuildUnlocked(string type) => BuildData.Of(type) is { } d && (d.Skill == null || HasSkill(d.Skill));
    public bool BuildingLimitReached(string type) => BuildingCount(type) >= BuildData.Of(type).Max;
    public BuildingSave Building(int id) => Data.Buildings.FirstOrDefault(b => b.Id == id);

    /// <summary>Paga e registra uma construção. extraCost = esteira do funil. Devolve null se não puder.</summary>
    public BuildingSave TryBuild(string type, Vector3 pos, float yaw, long extraCost = 0)
    {
        var def = BuildData.Of(type);
        if (def == null || BuildingLimitReached(type) || !BuildUnlocked(type)) return null;
        long cost = BuildCost(type) + Math.Max(0, extraCost);
        if (!CanAfford(cost)) return null;
        Data.Money -= cost;
        MoneyChanged?.Invoke();
        var b = new BuildingSave { Id = Data.NextBuildingId++, Type = type, X = pos.X, Y = pos.Y, Z = pos.Z, Yaw = yaw, Paid = cost, Ver = 1 };
        Data.Buildings.Add(b);
        BuildingsChanged?.Invoke();
        return b;
    }

    /// <summary>Desmonta: devolve metade do que foi pago; o que estava no funil é vendido na hora.</summary>
    public long Demolish(int id)
    {
        var b = Building(id);
        if (b == null) return 0;
        foreach (var kv in b.Queue)
            if (GameData.Mat(kv.Key) is { } m) SellFromBelt(m, kv.Value);
        b.Queue.Clear();
        Data.Buildings.Remove(b);
        long refund = (long)(b.Paid * BuildData.RefundFraction);
        if (refund > 0) { Data.Money += refund; MoneyChanged?.Invoke(); }
        BuildingsChanged?.Invoke();
        return refund;
    }

    /// <summary>Coloca fragmentos na fila do funil.</summary>
    public void HopperAdd(int id, string materialId, int count = 1)
    {
        var b = Building(id);
        if (b == null || count <= 0 || GameData.Mat(materialId) == null) return;
        // coletado por drone/robô/funil também conta como descoberto (ex.: fragmentos do Coração)
        Discover(GameData.Mat(materialId));
        b.Queue[materialId] = (b.Queue.TryGetValue(materialId, out int c) ? c : 0) + count;
    }

    /// <summary>Esvazia a mochila no funil. Devolve quantos fragmentos entraram.</summary>
    public int DepositInventoryInHopper(int id)
    {
        var b = Building(id);
        if (b == null || InventoryCount == 0) return 0;
        int n = InventoryCount;
        foreach (var kv in Data.Inventory) HopperAdd(id, kv.Key, kv.Value);
        Data.Inventory.Clear();
        InventoryChanged?.Invoke();
        return n;
    }

    /// <summary>Tira um fragmento da fila do funil (para a esteira). Null se vazio.</summary>
    public string HopperTake(int id)
    {
        var b = Building(id);
        if (b == null || b.Queue.Count == 0) return null;
        var key = b.Queue.Keys.First();
        if (--b.Queue[key] <= 0) b.Queue.Remove(key);
        return key;
    }

    public int HopperQueued(int id) => Building(id)?.Queue.Values.Sum() ?? 0;

    /// <summary>Fragmentos que chegaram à Estação de Venda pela esteira: vendidos na hora.</summary>
    public long SellFromBelt(MaterialDef m, int count = 1)
    {
        if (m == null || count <= 0) return 0;
        Discover(m);
        long v = SellPrice(m) * count;
        Data.ShardsSold += count;
        Data.DayEarned += v;
        AddMoney(v);
        BeltSold?.Invoke(v, m);
        return v;
    }

    public long SellPrice(MaterialDef def) =>
        Math.Max(1, (long)Math.Round(def.Value * Economy.SellValue * ValueMultiplier * ((def.Rare || def.Special) && HasSkill(SkillIds.Jeweler) ? 1.5f : 1f)));

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
        Data.Money -= cost;
        Data.Upgrades[id] = Data.Level(id) + 1;
        MoneyChanged?.Invoke();
        UpgradesChanged?.Invoke();
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
        BigMessage?.Invoke(Loc.T("NOVA REGIÃO: ") + r.Name.ToUpper());
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
        + (HasSkill(SkillIds.Early) ? 120f : 0f) + (HasSkill(SkillIds.Overtime) ? 120f : 0f) + (HasSkill(SkillIds.LongShift) ? 180f : 0f);
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
    // ================= Encomendas (quadro de missões da vila) =================
    public const int ContractSlots = 3;
    public event Action ContractsChanged;

    /// <summary>Recompensa: o dobro do valor de venda + um bônus fixo, arredondada.</summary>
    public long ContractReward(MaterialDef m, int amount)
    {
        double v = SellPrice(m) * (double)amount * 2.0 + 40;
        return v > 1000 ? (long)(Math.Round(v / 50.0) * 50) : (long)(Math.Round(v / 10.0) * 10);
    }

    /// <summary>Quantidade pedida por raridade (comum pede muito, lendário pede 1).</summary>
    public static int ContractAmount(MaterialDef m, Random rng)
    {
        int b = m.Rarity switch { Rarity.Comum => 20, Rarity.Incomum => 12, Rarity.Raro => 6, Rarity.Epico => 3, _ => 1 };
        return Math.Max(1, (int)Math.Round(b * (0.75 + rng.NextDouble() * 0.5)));
    }

    /// <summary>Preenche as vagas vazias (e troca as entregues se renew) com minérios já descobertos.</summary>
    public void EnsureContracts(bool renew = false)
    {
        Data.Contracts ??= new List<ContractSave>();
        int before = Data.Contracts.Count;
        if (renew) Data.Contracts.RemoveAll(c => c.Done);
        bool changed = Data.Contracts.Count != before;
        var pool = Data.Discovered.Select(GameData.Mat).Where(m => m != null && !m.Special).ToList();
        if (pool.Count == 0) pool.Add(GameData.Mat("quartz"));
        var rng = new Random(Data.Day * 7919 + Data.Contracts.Count * 31 + Data.TotalEarned.GetHashCode());
        while (Data.Contracts.Count < ContractSlots)
        {
            // comuns aparecem mais; não repete minério já pedido
            var options = pool.Where(m => Data.Contracts.All(c => c.Mat != m.Id)).ToList();
            if (options.Count == 0) options = pool;
            double total = options.Sum(m => 1.0 / (1 + (int)m.Rarity));
            double pick = rng.NextDouble() * total;
            var chosen = options[^1];
            foreach (var m in options) { pick -= 1.0 / (1 + (int)m.Rarity); if (pick <= 0) { chosen = m; break; } }
            int amount = ContractAmount(chosen, rng);
            Data.Contracts.Add(new ContractSave { Mat = chosen.Id, Amount = amount, Reward = ContractReward(chosen, amount) });
            changed = true;
        }
        // só avisa quando algo mudou (a tela de encomendas chama isto ao se atualizar: avisar sempre virava um laço sem fim)
        if (changed) ContractsChanged?.Invoke();
    }

    public int InventoryOf(string matId) => Data.Inventory.TryGetValue(matId, out int c) ? c : 0;
    public bool CanDeliver(int slot) => slot >= 0 && slot < Data.Contracts.Count && !Data.Contracts[slot].Done && InventoryOf(Data.Contracts[slot].Mat) >= Data.Contracts[slot].Amount;

    /// <summary>Entrega a encomenda: tira os fragmentos da mochila e paga a recompensa.</summary>
    public bool TryDeliver(int slot)
    {
        if (!CanDeliver(slot)) return false;
        var c = Data.Contracts[slot];
        Data.Inventory[c.Mat] -= c.Amount;
        if (Data.Inventory[c.Mat] <= 0) Data.Inventory.Remove(c.Mat);
        c.Done = true;
        Data.ShardsSold += c.Amount;
        Data.DayEarned += c.Reward;
        AddMoney(c.Reward);
        InventoryChanged?.Invoke();
        ContractsChanged?.Invoke();
        Notify("Encomenda entregue!", Loc.F("+{0} pela entrega de {1}x {2}.", FormatMoney(c.Reward), c.Amount, GameData.Mat(c.Mat)?.Name ?? c.Mat), NotifyKind.Success);
        return true;
    }

    // ================= Forja (ferreiro da vila) =================
    public event Action GearChanged;

    /// <summary>Nível forjado da peça (−1 = nenhum).</summary>
    public int GearTier(GearSlot s) => Data.Gear.TryGetValue(GearData.SlotId(s), out int t) ? t : -1;
    public float GearBonus(GearSlot s) => GearData.Of(s, GearTier(s))?.Bonus ?? 0f;
    /// <summary>Próximo nível que dá para forjar (null = já no máximo).</summary>
    public GearTierDef NextGear(GearSlot s) => GearData.Of(s, GearTier(s) + 1);
    /// <summary>O nível usa minérios da região de mesmo número: ela precisa estar liberada.</summary>
    public bool GearTierAvailable(GearTierDef d) => d != null && d.Tier < GameData.Regions.Count && IsUnlocked(GameData.Regions[d.Tier].Id);
    public bool HasForgeCosts(IEnumerable<ForgeCost> costs) => costs.All(c => InventoryOf(c.Mat) >= c.Amount);
    public bool CanForge(GearSlot s) => NextGear(s) is { } d && GearTierAvailable(d) && CanAfford(d.Fee) && HasForgeCosts(d.Costs);

    void PayForgeCosts(IEnumerable<ForgeCost> costs)
    {
        foreach (var c in costs)
        {
            Data.Inventory[c.Mat] -= c.Amount;
            if (Data.Inventory[c.Mat] <= 0) Data.Inventory.Remove(c.Mat);
        }
    }

    public bool TryForge(GearSlot s)
    {
        if (!CanForge(s) || !RequireWork()) return false;
        var d = NextGear(s);
        Data.Money -= d.Fee;
        PayForgeCosts(d.Costs);
        Data.Gear[GearData.SlotId(s)] = d.Tier;
        MoneyChanged?.Invoke();
        InventoryChanged?.Invoke();
        UpgradesChanged?.Invoke();
        GearChanged?.Invoke();
        Notify(Loc.F("{0} forjado!", d.Name), GearData.BonusText(d), NotifyKind.Success);
        BigMessage?.Invoke(d.Name.ToUpper());
        return true;
    }

    /// <summary>Conserto da ferramenta na forja: taxa (parte do preço) + fragmentos do minério dela. Lasers não se consertam.</summary>
    public bool RepairAvailable => CurrentTool.Kind != ToolKind.Laser && (ToolBroken || Data.ToolWear > 0.5f);
    public long RepairFee => Math.Max(5, (long)Math.Round(CurrentTool.Cost * GearData.RepairFeeFraction * Math.Clamp(ToolBroken ? 1f : Data.ToolWear / Math.Max(1f, ToolMaxDurability), 0.2f, 1f)));
    public List<ForgeCost> RepairCosts => CurrentTool.Ore != null && GameData.Mat(CurrentTool.Ore) != null
        ? new List<ForgeCost> { new(CurrentTool.Ore, GearData.RepairOreAmount) } : new List<ForgeCost>();
    public bool CanRepair => RepairAvailable && CanAfford(RepairFee) && HasForgeCosts(RepairCosts);

    public bool TryRepair()
    {
        if (!CanRepair || !RequireWork()) return false;
        long fee = RepairFee;
        Data.Money -= fee;
        PayForgeCosts(RepairCosts);
        Data.ToolWear = 0;
        Data.ToolBroken = false;
        MoneyChanged?.Invoke();
        InventoryChanged?.Invoke();
        UpgradesChanged?.Invoke();
        ToolChanged?.Invoke();
        GearChanged?.Invoke();
        Notify(Loc.F("{0} consertada", CurrentTool.Name), Loc.F("Resistência restaurada: {0}.", ToolMaxDurability), NotifyKind.Success);
        return true;
    }

    public void StartNewDay()
    {
        Data.Day++;
        Data.DayTimeSeconds = 0;
        Data.DayOver = false;
        Data.DayEarned = 0;
        Data.DayCrystals = 0;
        Data.DayBonus = 0;
        EnsureContracts(renew: true); // encomendas entregues são trocadas por novas
        int charged = ChargerAmmoPerDay;
        if (charged > 0)
        {
            Data.Ammo += charged;
            Notify("Carregador de células", Loc.F("+{0} células de energia recarregadas à noite (total {1})", charged, Data.Ammo), NotifyKind.Success);
        }
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
    public long GoblinFee(string id) => Goblin(id) == GoblinMood.Working && GoblinData(id).HutBuilt ? Math.Max(Data_.Of(id).MinFee, GoblinData(id).DayValue / 4) : 0;
    /// <summary>Só cobra depois que a cabana está pronta (enquanto monta, não trabalha).</summary>
    public bool GoblinFeeDue(string id) => Goblin(id) == GoblinMood.Working && GoblinData(id).HutBuilt && GoblinData(id).ChargedDay != Data.Day;
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
        if (!g.HutBuilt) g.HireDay = Data.Day;
        MoneyChanged?.Invoke();
        GoblinChanged?.Invoke();
        if (g.HutBuilt) Notify(Loc.F("{0} contratado!", def.Name), "Ele volta para a cabana e recomeça a minerar.", NotifyKind.Success);
        else Notify(Loc.F("{0} contratado!", def.Name), "Primeiro ele precisa montar a cabana perto da entrada da região (cerca de um dia e meio). Depois minera e guarda tudo no baú; cobra uma taxa no fim do dia.", NotifyKind.Success);
        return true;
    }

    // ---------------- cabana e melhorias do goblin ----------------
    /// <summary>Tempo para montar a cabana (em dias de trabalho).</summary>
    public const float GoblinBuildDays = 1.5f;
    public const int GoblinMaxLevel = 4;
    public static readonly float[] GoblinWorkFractions = { 0.3f, 0.45f, 0.6f, 0.8f, 1f };
    public static readonly int[] GoblinCarryLevels = { 8, 12, 16, 22, 30 };
    public static readonly float[] GoblinSpeedLevels = { 1f, 1.35f, 1.8f, 2.4f, 3.2f };
    public enum GoblinUpgrade { Hut, Carry, Speed }

    /// <summary>Progresso da montagem da cabana (0..1). Fica pronta sozinha com o passar dos dias.</summary>
    public float GoblinBuildProgress(string id)
    {
        var g = GoblinData(id);
        if (g.HutBuilt) return 1f;
        if (Goblin(id) != GoblinMood.Working) return 0f;
        float p = ((Data.Day - g.HireDay) + DayProgress) / GoblinBuildDays;
        if (p >= 1f)
        {
            g.HutBuilt = true;
            Notify(Loc.F("A cabana de {0} está pronta!", Data_.Of(id).Name), "Agora ele começa a minerar e guardar tudo no baú.", NotifyKind.Success);
            GoblinChanged?.Invoke();
            return 1f;
        }
        return Math.Clamp(p, 0f, 0.999f);
    }

    public bool GoblinBuilt(string id) => GoblinBuildProgress(id) >= 1f;
    public float GoblinWorkFraction(string id) => GoblinWorkFractions[GoblinData(id).HutLevel];
    public int GoblinCarryMax(string id) => GoblinCarryLevels[GoblinData(id).CarryLevel];
    public float GoblinDps(string id) => Data_.Of(id).MineDps * GoblinSpeedLevels[GoblinData(id).SpeedLevel];
    /// <summary>Está no horário de trabalho dele agora? (a cabana define quanto do dia ele trabalha)</summary>
    public bool GoblinOnShift(string id) => Goblin(id) == GoblinMood.Working && GoblinBuilt(id) && !DayOver && DayProgress < GoblinWorkFraction(id);

    public int GoblinLevel(string id, GoblinUpgrade u) => u switch
    {
        GoblinUpgrade.Hut => GoblinData(id).HutLevel,
        GoblinUpgrade.Carry => GoblinData(id).CarryLevel,
        _ => GoblinData(id).SpeedLevel,
    };

    public long GoblinUpgradeCost(string id, GoblinUpgrade u)
    {
        int l = GoblinLevel(id, u);
        if (l >= GoblinMaxLevel) return long.MaxValue;
        double[] mul = u switch
        {
            GoblinUpgrade.Hut => new[] { 1.5, 3, 6, 12 },
            GoblinUpgrade.Carry => new[] { 1.0, 2, 4, 8 },
            _ => new[] { 1.2, 2.5, 5, 10 },
        };
        return (long)(Data_.Of(id).HireCost * mul[l]);
    }

    public bool TryUpgradeGoblin(string id, GoblinUpgrade u)
    {
        if (Goblin(id) != GoblinMood.Working || !GoblinBuilt(id)) return false;
        long cost = GoblinUpgradeCost(id, u);
        if (!CanAfford(cost)) return false;
        Data.Money -= cost;
        var g = GoblinData(id);
        switch (u)
        {
            case GoblinUpgrade.Hut: g.HutLevel++; break;
            case GoblinUpgrade.Carry: g.CarryLevel++; break;
            default: g.SpeedLevel++; break;
        }
        MoneyChanged?.Invoke();
        GoblinChanged?.Invoke();
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

    /// <summary>Células recarregadas a cada manhã pelo carregador da casa (só com um laser equipado).</summary>
    public int ChargerAmmoPerDay => IsLaser ? (int)(HouseLevel(HouseIds.Charger) * HomeData.HouseDef(HouseIds.Charger).PerLevel) : 0;
    /// <summary>Bancada da casa vende ferramentas e munição (suporte de ferramentas nível 1+).</summary>
    public bool HomeToolShop => HouseLevel(HouseIds.ToolRack) >= 1;
    /// <summary>Baú da casa compra a mochila (baú nível 1+).</summary>
    public bool HomeChestSell => HouseLevel(HouseIds.Chest) >= 1;

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
    /// <summary>Jogador está na vila (ou em casa): dá para comprar mesmo com o turno encerrado.</summary>
    public bool InTown { get; set; }

    public bool RequireWork()
    {
        if (CanWork || InTown) return true;
        Notify("Turno encerrado", "Agora você só pode vender e voltar para casa pelo elevador.", NotifyKind.Warning);
        return false;
    }

    // ================= Sessão / Save =================
    public void NewGame()
    {
        Data = new SaveData { ToolsMigrated = true, WearVersion = 1 }; // começa com a picareta de madeira
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
        MigrateTools();
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

    /// <summary>Testes: usa um save em memória como se tivesse sido carregado.</summary>
    public void UseDataForTests(SaveData d) => Data = d;

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
            Notify(ok ? Loc.T("Jogo salvo") : Loc.T("Erro ao salvar"), ok ? Loc.T("Progresso gravado com sucesso.") : Loc.T("Verifique o espaço em disco."),
                ok ? NotifyKind.Success : NotifyKind.Warning);
        return ok;
    }

    /// <summary>Encerra o jogo de forma limpa: libera caches estáticos e coleta o lixo gerenciado antes de sair.</summary>
    public async void QuitClean(int exitCode = 0)
    {
        World.MeshFactory.ClearCaches();
        World.CaveTerrain.ClearCaches();
        World.DecorMeshes.ClearCaches();
        Mining.ShardManager.ClearCaches();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GC.Collect();
        GetTree().Quit(exitCode);
    }

    public void Notify(string title, string body, NotifyKind kind = NotifyKind.Info) => Notified?.Invoke(Loc.T(title), Loc.T(body), kind);

    public void RaiseAll()
    {
        MoneyChanged?.Invoke();
        InventoryChanged?.Invoke();
        UpgradesChanged?.Invoke();
        RegionsChanged?.Invoke();
    }

    public void RecordCrystalMined() { Data.CrystalsMined++; Data.DayCrystals++; }

    /// <summary>Jornada concluída: todas as regiões liberadas e o Coração Ancestral destruído.</summary>
    public bool JourneyComplete => GameData.Regions.Take(GameData.JourneyRegions).All(r => IsUnlocked(r.Id)) && Data.Discovered.Contains("heart");

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
        Data.Tool = 0; Data.ToolWear = 0; Data.ToolBroken = false; Data.Ammo = 0; // a jornada recomeça com a picareta de madeira
        Data.Skills.Clear(); // a árvore recomeça; a casa é permanente
        Data.Gear.Clear(); // o equipamento forjado fica para trás, como as ferramentas
        Data.Inventory.Clear();
        Data.UnlockedRegions.Clear();
        Data.UnlockedRegions.Add("caves");
        Data.GameCompleted = false;
        Data.HasPosition = false;
        Data.Buildings.Clear(); // as construções ficam para trás (a árvore, com o ramo Construção, recomeça)
        BuildingsChanged?.Invoke();
        RaiseAll();
        Notify("Renascimento!", Loc.F("+{0} Essência Ancestral · bônus permanente x{1:0.0}", gained, EssenceMultiplier), NotifyKind.Unlock);
        BigMessage?.Invoke(Loc.F("RENASCIMENTO {0}", Data.PrestigeCount));
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
