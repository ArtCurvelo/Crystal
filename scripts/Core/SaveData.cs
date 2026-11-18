using System.Collections.Generic;
using System.Linq;
using CrystalExcavator.Data;

namespace CrystalExcavator.Core;

/// <summary>Estado persistente do jogador. Todos os campos têm padrões seguros.</summary>
public sealed class SaveData
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public long Money { get; set; }
    public long TotalEarned { get; set; }
    public Dictionary<string, int> Upgrades { get; set; } = new();
    public List<string> UnlockedRegions { get; set; } = new() { "caves" };
    public Dictionary<string, int> Inventory { get; set; } = new();
    public List<string> Discovered { get; set; } = new();
    public long CrystalsMined { get; set; }
    public long ShardsSold { get; set; }
    public double PlayTimeSeconds { get; set; }
    public bool TutorialDone { get; set; }
    public bool GameCompleted { get; set; }
    /// <summary>Ganhos na partida atual (zerado no Renascimento).</summary>
    public long RunEarned { get; set; }
    /// <summary>Essência Ancestral acumulada (bônus permanente).</summary>
    public int Essence { get; set; }
    public int PrestigeCount { get; set; }
    public double CompletedAtSeconds { get; set; }
    // ciclo de trabalho (turnos)
    public int Day { get; set; } = 1;
    public double DayTimeSeconds { get; set; }
    public bool DayOver { get; set; }
    public long DayEarned { get; set; }
    public long DayCrystals { get; set; }
    public long DayBonus { get; set; }
    // casa e árvore de habilidades
    public Dictionary<string, int> House { get; set; } = new();
    public List<string> Skills { get; set; } = new();
    public List<string> Secrets { get; set; } = new();
    /// <summary>Goblins mineradores por região (id da região → estado).</summary>
    public Dictionary<string, GoblinSave> Goblins { get; set; } = new();
    // campos antigos (v0.3.x, só o Grub): lidos e migrados para Goblins["caves"]
    public int GoblinState { get; set; }             // 0 disponível, 1 trabalhando, 2 fugiu, 3 rebelado
    public int GoblinStrikes { get; set; }           // dias seguidos sem pagar
    public long GoblinDayValue { get; set; }         // valor minerado por ele hoje
    public int GoblinChargedDay { get; set; }
    public int GoblinFledDay { get; set; }
    public Dictionary<string, int> GoblinStash { get; set; } = new();
    public float PlayerX { get; set; }
    public float PlayerY { get; set; } = 1f;
    public float PlayerZ { get; set; } = 14f;
    public float PlayerYaw { get; set; }
    public bool HasPosition { get; set; }

    public int Level(string upgradeId) => Upgrades.TryGetValue(upgradeId, out int l) ? l : 0;

    /// <summary>Corrige qualquer valor inválido (dados corrompidos, editados ou de versões antigas).</summary>
    public void Sanitize()
    {
        Upgrades ??= new();
        UnlockedRegions ??= new();
        Inventory ??= new();
        Discovered ??= new();
        if (Money < 0) Money = 0;
        if (TotalEarned < Money) TotalEarned = Money;
        if (CrystalsMined < 0) CrystalsMined = 0;
        if (RunEarned < 0) RunEarned = 0;
        // saves anteriores ao Renascimento: a partida atual é todo o histórico
        if (RunEarned == 0 && PrestigeCount == 0 && TotalEarned > 0) RunEarned = TotalEarned;
        if (RunEarned > TotalEarned) RunEarned = TotalEarned;
        Essence = System.Math.Clamp(Essence, 0, 100000);
        PrestigeCount = System.Math.Max(0, PrestigeCount);
        if (ShardsSold < 0) ShardsSold = 0;
        if (PlayTimeSeconds < 0 || double.IsNaN(PlayTimeSeconds)) PlayTimeSeconds = 0;
        if (Day < 1) Day = 1;
        if (DayTimeSeconds < 0 || double.IsNaN(DayTimeSeconds)) DayTimeSeconds = 0;
        if (DayEarned < 0) DayEarned = 0;
        if (DayCrystals < 0) DayCrystals = 0;
        if (DayBonus < 0) DayBonus = 0;
        House ??= new();
        Skills ??= new();
        Secrets ??= new();
        GoblinStash ??= new();
        Goblins ??= new();
        // migração: saves da v0.3.x tinham um único goblin (o das Cavernas), já encontrado
        if (!Goblins.ContainsKey("caves") && (GoblinState != 0 || GoblinStash.Count > 0))
        {
            Goblins["caves"] = new GoblinSave
            {
                State = GoblinState, Strikes = GoblinStrikes, DayValue = GoblinDayValue, ChargedDay = GoblinChargedDay,
                FledDay = GoblinFledDay, Stash = new Dictionary<string, int>(GoblinStash), Found = true,
            };
        }
        GoblinState = 0; GoblinStrikes = 0; GoblinDayValue = 0; GoblinStash.Clear();
        foreach (var key in Goblins.Keys.ToList())
        {
            if (GoblinData.Of(key) == null || Goblins[key] == null) { Goblins.Remove(key); continue; }
            Goblins[key].Sanitize();
        }
        Secrets = Secrets.Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        foreach (var key in House.Keys.ToList())
        {
            var hd = HomeData.HouseDef(key);
            if (hd == null) { House.Remove(key); continue; }
            House[key] = System.Math.Clamp(House[key], 0, hd.MaxLevel);
        }
        Skills = Skills.Where(k => k != null && HomeData.Skill(k) != null).Distinct().ToList();

        // upgrades: remove desconhecidos e limita níveis
        foreach (var key in Upgrades.Keys.ToList())
        {
            if (!GameData.Upgrades.TryGetValue(key, out var def)) { Upgrades.Remove(key); continue; }
            Upgrades[key] = System.Math.Clamp(Upgrades[key], 0, def.MaxLevel);
        }
        // regiões: apenas conhecidas, sem duplicatas, a primeira sempre liberada
        UnlockedRegions = UnlockedRegions.Where(r => r != null && GameData.Region(r) != null).Distinct().ToList();
        if (!UnlockedRegions.Contains("caves")) UnlockedRegions.Insert(0, "caves");
        // inventário: apenas materiais conhecidos e quantidades positivas
        foreach (var key in Inventory.Keys.ToList())
        {
            if (GameData.Mat(key) == null || Inventory[key] <= 0) Inventory.Remove(key);
        }
        Discovered = Discovered.Where(d => d != null && GameData.Mat(d) != null).Distinct().ToList();

        bool bad(float f) => float.IsNaN(f) || float.IsInfinity(f) || System.MathF.Abs(f) > 10000f;
        if (bad(PlayerX) || bad(PlayerY) || bad(PlayerZ) || bad(PlayerYaw)) HasPosition = false;
        Version = CurrentVersion;
    }
}

/// <summary>Estado salvo de um goblin minerador.</summary>
public sealed class GoblinSave
{
    public int State { get; set; }               // 0 disponível, 1 trabalhando, 2 fugiu, 3 rebelado
    public bool Found { get; set; }              // já foi encontrado na câmara secreta
    public int Strikes { get; set; }
    public long DayValue { get; set; }
    public int ChargedDay { get; set; }
    public int FledDay { get; set; }
    public Dictionary<string, int> Stash { get; set; } = new();

    public void Sanitize()
    {
        State = System.Math.Clamp(State, 0, 3);
        Strikes = System.Math.Clamp(Strikes, 0, 10);
        if (DayValue < 0) DayValue = 0;
        Stash ??= new();
        foreach (var key in Stash.Keys.ToList())
            if (GameData.Mat(key) == null || Stash[key] <= 0) Stash.Remove(key);
        if (State != 0) Found = true;
    }
}

