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
    /// <summary>Ferramenta equipada (índice em ToolData.All), resistência já gasta e se quebrou.</summary>
    public int Tool { get; set; }
    public float ToolWear { get; set; }
    /// <summary>1 = desgaste em pontos de resistência (antes eram golpes).</summary>
    public int WearVersion { get; set; }
    public bool ToolBroken { get; set; }
    /// <summary>Células de energia para as ferramentas laser.</summary>
    public int Ammo { get; set; }
    /// <summary>Saves de antes das ferramentas (dano/velocidade eram melhorias) já foram convertidos.</summary>
    public bool ToolsMigrated { get; set; }
    public long TotalEarned { get; set; }
    public Dictionary<string, int> Upgrades { get; set; } = new();
    public List<string> UnlockedRegions { get; set; } = new() { "caves" };
    public Dictionary<string, int> Inventory { get; set; } = new();
    public List<string> Discovered { get; set; } = new();
    /// <summary>Encomendas do quadro de missões da vila.</summary>
    public List<ContractSave> Contracts { get; set; } = new();
    /// <summary>Equipamento forjado na vila: peça ("helmet", "gloves", "boots", "bag") → nível 0..5 (ausente = nenhum).</summary>
    public Dictionary<string, int> Gear { get; set; } = new();
    /// <summary>Diários de outros escravos já encontrados na mina (ficam ao renascer).</summary>
    public List<string> Diaries { get; set; } = new();
    /// <summary>Mapa: partes de cada região já vistas (id da região → bits em base64, ver ExploreMap). Fica ao renascer.</summary>
    public Dictionary<string, string> Explored { get; set; } = new();
    /// <summary>Etapa do ato 2 ("A Chave Falsa", ver StoryData). Fica ao renascer.</summary>
    public int StoryStage { get; set; }
    /// <summary>Campo antigo (v0.27: índice de receita fixa); migrado para WeaponOres.</summary>
    public int Weapon { get; set; } = -1;
    /// <summary>Arma forjada: tipo (0 espada, 1 martelo) e os minérios combinados (vazio = nenhuma). Fica para trás ao renascer.</summary>
    public int WeaponKind { get; set; }
    public List<string> WeaponOres { get; set; } = new();
    /// <summary>Armadura forjada: os minérios combinados (vazio = nenhuma). Fica para trás ao renascer.</summary>
    public List<string> ArmorOres { get; set; } = new();
    /// <summary>Inimigos derrotados nas Profundezas (estatística).</summary>
    public long EnemiesKilled { get; set; }
    /// <summary>Profundezas cujo Guardião já caiu (a passagem para a próxima fica aberta; vale entre renascimentos).</summary>
    public List<string> Guardians { get; set; } = new();
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
    /// <summary>Construções colocadas nas cavernas (postes, funis com esteira).</summary>
    public List<BuildingSave> Buildings { get; set; } = new();
    public int NextBuildingId { get; set; } = 1;
    /// <summary>Drone coletor comprado (é mantido no Renascimento).</summary>
    public bool DroneOwned { get; set; }
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
        Contracts ??= new();
        Contracts.RemoveAll(c => c == null || GameData.Mat(c.Mat) == null || c.Amount <= 0 || c.Reward < 0);
        if (Contracts.Count > 3) Contracts.RemoveRange(3, Contracts.Count - 3);
        Gear ??= new();
        StoryStage = System.Math.Clamp(StoryStage, 0, StoryData.Freed);
        WeaponOres ??= new();
        ArmorOres ??= new();
        if (Weapon >= 0 && Weapon < WeaponData.LegacyRecipes.Length && WeaponOres.Count == 0)
        {
            WeaponKind = (int)WeaponData.LegacyRecipes[Weapon].kind;
            WeaponOres = WeaponData.LegacyRecipes[Weapon].ores.ToList();
        }
        Weapon = -1;
        WeaponKind = System.Math.Clamp(WeaponKind, 0, 1);
        if (WeaponData.Make((Data.WeaponKind)WeaponKind, WeaponOres) == null) WeaponOres = new();
        if (WeaponData.MakeArmor(ArmorOres) == null) ArmorOres = new();
        if (EnemiesKilled < 0) EnemiesKilled = 0;
        Guardians = (Guardians ?? new()).Where(g => GameData.Regions.Any(r => r.Deep && r.Id == g)).Distinct().ToList();
        Diaries = (Diaries ?? new()).Where(d => DiaryData.Of(d) != null).Distinct().ToList();
        Explored = (Explored ?? new()).Where(kv => GameData.Regions.Any(r => r.Id == kv.Key) && !string.IsNullOrEmpty(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var key in Gear.Keys.ToList())
        {
            if (System.Array.IndexOf(new[] { "helmet", "gloves", "boots", "bag" }, key) < 0 || Gear[key] < 0) { Gear.Remove(key); continue; }
            Gear[key] = System.Math.Min(Gear[key], GearData.Tiers - 1);
        }
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

        Tool = System.Math.Clamp(Tool, 0, ToolData.All.Count - 1);
        ToolWear = float.IsFinite(ToolWear) ? System.Math.Max(0f, ToolWear) : 0f;
        Ammo = System.Math.Max(0, Ammo);
        Buildings ??= new();
        Buildings = Buildings.Where(b => b != null && BuildData.Of(b.Type) != null
                                         && !float.IsNaN(b.X) && !float.IsNaN(b.Y) && !float.IsNaN(b.Z)
                                         && System.MathF.Abs(b.X) < 10000 && System.MathF.Abs(b.Z) < 10000).ToList();
        foreach (var b in Buildings)
        {
            b.Queue ??= new();
            if (b.InFlight != null)
                foreach (var kv in b.InFlight)
                    if (kv.Value > 0) b.Queue[kv.Key] = (b.Queue.TryGetValue(kv.Key, out int q) ? q : 0) + kv.Value;
            b.InFlight = new();
            foreach (var key in b.Queue.Keys.ToList())
                if (GameData.Mat(key) == null || b.Queue[key] <= 0) b.Queue.Remove(key);
            if (b.Id <= 0) b.Id = NextBuildingId++;
        }
        Buildings = Buildings.GroupBy(b => b.Id).Select(g => g.First()).ToList();
        if (NextBuildingId <= Buildings.DefaultIfEmpty().Max(b => b?.Id ?? 0)) NextBuildingId = Buildings.Max(b => b.Id) + 1;
        if (NextBuildingId < 1) NextBuildingId = 1;

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
    /// <summary>Dia em que foi contratado (a cabana leva um tempo para ficar pronta).</summary>
    public int HireDay { get; set; }
    public bool HutBuilt { get; set; }
    /// <summary>Melhorias: cabana (tempo de trabalho no dia), mochila e picareta.</summary>
    public int HutLevel { get; set; }
    public int CarryLevel { get; set; }
    public int SpeedLevel { get; set; }

    public void Sanitize()
    {
        State = System.Math.Clamp(State, 0, 3);
        // saves antigos: quem já trabalhava tem a barraca pronta
        if ((State == 1 || State == 3) && HireDay <= 0) HutBuilt = true;
        HutLevel = System.Math.Clamp(HutLevel, 0, 4);
        CarryLevel = System.Math.Clamp(CarryLevel, 0, 4);
        SpeedLevel = System.Math.Clamp(SpeedLevel, 0, 4);
        Strikes = System.Math.Clamp(Strikes, 0, 10);
        if (DayValue < 0) DayValue = 0;
        Stash ??= new();
        foreach (var key in Stash.Keys.ToList())
            if (GameData.Mat(key) == null || Stash[key] <= 0) Stash.Remove(key);
        if (State != 0) Found = true;
    }
}


/// <summary>Uma construção salva.</summary>
public sealed class BuildingSave
{
    public int Id { get; set; }
    public string Type { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    /// <summary>Corda: ponta de baixo e ponto onde o jogador sai em cima.</summary>
    public float BX { get; set; }
    public float BY { get; set; }
    public float BZ { get; set; }
    public float EX { get; set; }
    public float EY { get; set; }
    public float EZ { get; set; }
    /// <summary>Quanto foi pago (esteira incluída): base da devolução ao desmontar.</summary>
    public long Paid { get; set; }
    /// <summary>Funil: fragmentos esperando para entrar na esteira.</summary>
    public Dictionary<string, int> Queue { get; set; } = new();
    /// <summary>Fragmentos que estavam andando na esteira na hora do save (voltam para a fila ao carregar).</summary>
    public Dictionary<string, int> InFlight { get; set; } = new();
    /// <summary>Esteira: pontos do trajeto (x, y, z em sequência).</summary>
    public List<float> Path { get; set; }
    /// <summary>Esteira/funil: para onde despeja (0 = Estação de Venda, id = outra esteira, -1 = sem saída).</summary>
    public int Feeds { get; set; } = -1;
    /// <summary>Ponto da esteira de destino (metros desde o início) onde entra.</summary>
    public float JoinS { get; set; }
    /// <summary>Formato: 0 = funil antigo com esteira automática; 1 = sistema de construção atual.</summary>
    public int Ver { get; set; }
}

/// <summary>Encomenda da vila: entregar N fragmentos de um minério em troca de uma recompensa.</summary>
public class ContractSave
{
    public string Mat { get; set; } = "quartz";
    public int Amount { get; set; } = 10;
    public long Reward { get; set; }
    public bool Done { get; set; }
}
