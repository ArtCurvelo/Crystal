using System;
using System.Linq;
using CrystalExcavator.Data;
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
    public float Damage => StatValue(UpgradeIds.Damage) * EventDamageMultiplier;
    public float SwingsPerSecond => StatValue(UpgradeIds.Speed);
    public float SwingInterval => 1f / SwingsPerSecond;
    public int Capacity => (int)StatValue(UpgradeIds.Capacity);
    public float MoveSpeed => StatValue(UpgradeIds.Move);
    public float ValueMultiplier => StatValue(UpgradeIds.Value);
    public float MagnetRadius => StatValue(UpgradeIds.Reach);
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
            return l >= 24 ? 4 : l >= 16 ? 3 : l >= 9 ? 2 : l >= 4 ? 1 : 0;
        }
    }

    public static readonly string[] ToolTierNames =
        { "Picareta de Ferro", "Picareta de Aço Azul", "Martelo Prismático", "Perfurador Solar", "Ressonador Ancestral" };

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
            Notify(def.Rare || def.Special ? "Material raro descoberto!" : "Novo material descoberto",
                $"{def.Name} — {FormatMoney(SellPrice(def))} por fragmento",
                def.Rare || def.Special ? NotifyKind.Rare : NotifyKind.Info);
        }
        InventoryChanged?.Invoke();
        return accepted;
    }

    public long SellPrice(MaterialDef def) => (long)Math.Round(def.Value * ValueMultiplier);

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
        MoneyChanged?.Invoke();
    }

    // ================= Upgrades =================
    public long UpgradeCost(string id) => GameData.Upgrades[id].CostAt(Data.Level(id));
    public bool IsMaxed(string id) => Data.Level(id) >= GameData.Upgrades[id].MaxLevel;
    public bool CanAfford(long cost) => cost != long.MaxValue && Data.Money >= cost;

    public bool TryBuyUpgrade(string id)
    {
        if (!GameData.Upgrades.ContainsKey(id) || IsMaxed(id)) return false;
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
        Data.Money -= r.UnlockCost;
        Data.UnlockedRegions.Add(r.Id);
        MoneyChanged?.Invoke();
        RegionsChanged?.Invoke();
        Notify("Região desbloqueada!", r.Name, NotifyKind.Unlock);
        BigMessage?.Invoke("NOVA REGIÃO: " + r.Name.ToUpper());
        SaveGame(true);
        return true;
    }

    // ================= Sessão / Save =================
    public void NewGame()
    {
        Data = new SaveData();
        Data.Sanitize();
        _autosaveTimer = 0;
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
        RaiseAll();
        return true;
    }

    public Func<SaveData, bool> BeforeSave; // permite à cena gravar posição do jogador

    public bool SaveGame(bool auto = false)
    {
        BeforeSave?.Invoke(Data);
        bool ok = SaveSystem.Save(Data);
        if (ok) LastSaveTime = Time.GetTicksMsec() / 1000.0;
        if (!auto || !ok)
            Notify(ok ? "Jogo salvo" : "Erro ao salvar", ok ? "Progresso gravado com sucesso." : "Verifique o espaço em disco.",
                ok ? NotifyKind.Success : NotifyKind.Warning);
        return ok;
    }

    public void Notify(string title, string body, NotifyKind kind = NotifyKind.Info) => Notified?.Invoke(title, body, kind);

    public void RaiseAll()
    {
        MoneyChanged?.Invoke();
        InventoryChanged?.Invoke();
        UpgradesChanged?.Invoke();
        RegionsChanged?.Invoke();
    }

    public void RecordCrystalMined() => Data.CrystalsMined++;

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
