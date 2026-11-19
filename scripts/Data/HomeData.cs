using System;
using System.Collections.Generic;
using System.Linq;

namespace CrystalExcavator.Data;

/// <summary>Melhoria da casa: comprada com dinheiro em níveis; cada nível dá um bônus permanente.</summary>
public sealed class HouseUpgradeDef
{
    public string Id;
    string _name;
    /// <summary>Traduzido para o idioma escolhido (fonte em português).</summary>
    public string Name { get => Core.Loc.T(_name); set => _name = value; }
    /// <summary>Texto original em português (chave estável).</summary>
    public string NamePt => _name;
    string _description;
    /// <summary>Traduzido para o idioma escolhido (fonte em português).</summary> 
    public string Description { get => Core.Loc.T(_description); set => _description = value; }
    /// <summary>Texto original em português (chave estável).</summary>
    public string DescriptionPt => _description;
    public long BaseCost;
    public double CostGrowth;
    public int MaxLevel;
    public float PerLevel;       // bônus por nível (fração ou segundos, conforme o item)

    public long CostAt(int level)
    {
        if (level >= MaxLevel) return long.MaxValue;
        double c = BaseCost * Math.Pow(CostGrowth, level);
        if (c > 1000) c = Math.Round(c / 100.0) * 100.0;
        return (long)Math.Ceiling(c);
    }
}

/// <summary>Nó da árvore de habilidades (comprado uma vez; exige o nó anterior do mesmo ramo).</summary>
public sealed class SkillDef
{
    public string Id;
    string _name;
    /// <summary>Traduzido para o idioma escolhido (fonte em português).</summary>
    public string Name { get => Core.Loc.T(_name); set => _name = value; }
    /// <summary>Texto original em português (chave estável).</summary>
    public string NamePt => _name;
    string _description;
    /// <summary>Traduzido para o idioma escolhido (fonte em português).</summary>
    public string Description { get => Core.Loc.T(_description); set => _description = value; }
    /// <summary>Texto original em português (chave estável).</summary>
    public string DescriptionPt => _description;
    public int Branch;           // coluna
    public int Tier;             // 1..3
    public long Cost;
    public string Requires;      // null no primeiro nível
}

public static class HouseIds
{
    public const string Bed = "bed";
    public const string Workbench = "workbench";
    public const string Wardrobe = "wardrobe";
    public const string Kitchen = "kitchen";
    public const string Trophies = "trophies";
}

public static class SkillIds
{
    public const string Magnet = "magnet", DirectCollect = "direct_collect", Compactor = "compactor";
    public const string Early = "early_bird", Overtime = "overtime", ShiftBonus = "shift_bonus";
    public const string FirmStrike = "firm_strike", Cadence = "cadence", Critical = "critical";
    public const string Haggle = "haggle", RareNose = "rare_nose", Jeweler = "jeweler";
    public const string BuildBelt = "build_belt", BuildHopper = "build_hopper", BuildDrone = "build_drone", BuildRobot = "build_robot";
}

public static class HomeData
{
    public static readonly List<HouseUpgradeDef> House = new()
    {
        new() { Id = HouseIds.Bed, Name = "Cama", Description = "Descanso melhor: +1 min de turno por nível.", BaseCost = 1500, CostGrowth = 4, MaxLevel = 3, PerLevel = 60f },
        new() { Id = HouseIds.Workbench, Name = "Bancada de ferramentas", Description = "Ajustes na ferramenta: +6% de dano por nível.", BaseCost = 2500, CostGrowth = 3.2, MaxLevel = 5, PerLevel = 0.06f },
        new() { Id = HouseIds.Wardrobe, Name = "Armário de equipamento", Description = "Mochila melhor organizada: +10% de capacidade por nível.", BaseCost = 2000, CostGrowth = 3.5, MaxLevel = 4, PerLevel = 0.10f },
        new() { Id = HouseIds.Kitchen, Name = "Cozinha", Description = "Café reforçado: +5% de velocidade por nível.", BaseCost = 1200, CostGrowth = 3, MaxLevel = 3, PerLevel = 0.05f },
        new() { Id = HouseIds.Trophies, Name = "Estante de troféus", Description = "Fama de escavador: +4% no valor de venda por nível.", BaseCost = 4000, CostGrowth = 3.4, MaxLevel = 5, PerLevel = 0.04f },
    };

    public static readonly string[] BranchNames = { "COLETA", "TURNO", "MINERAÇÃO", "COMÉRCIO", "CONSTRUÇÃO" };

    public static readonly List<SkillDef> Skills = new()
    {
        new() { Id = SkillIds.Magnet, Name = "Ímã ampliado", Description = "+30% de raio do ímã de fragmentos.", Branch = 0, Tier = 1, Cost = 2000 },
        new() { Id = SkillIds.DirectCollect, Name = "Coleta direta", Description = "Fragmentos voam direto para a mochila, de qualquer distância.", Branch = 0, Tier = 2, Cost = 30000, Requires = SkillIds.Magnet },
        new() { Id = SkillIds.Compactor, Name = "Compactador", Description = "+25% de capacidade da mochila.", Branch = 0, Tier = 3, Cost = 350000, Requires = SkillIds.DirectCollect },

        new() { Id = SkillIds.Early, Name = "Madrugador", Description = "+2 min de turno.", Branch = 1, Tier = 1, Cost = 3000 },
        new() { Id = SkillIds.Overtime, Name = "Hora extra", Description = "+2 min de turno.", Branch = 1, Tier = 2, Cost = 40000, Requires = SkillIds.Early },
        new() { Id = SkillIds.ShiftBonus, Name = "Bônus do turno", Description = "Ao chegar em casa, recebe +10% do que ganhou no dia.", Branch = 1, Tier = 3, Cost = 300000, Requires = SkillIds.Overtime },

        new() { Id = SkillIds.FirmStrike, Name = "Golpe firme", Description = "+15% de dano.", Branch = 2, Tier = 1, Cost = 2500 },
        new() { Id = SkillIds.Cadence, Name = "Cadência", Description = "+10% de golpes por segundo.", Branch = 2, Tier = 2, Cost = 35000, Requires = SkillIds.FirmStrike },
        new() { Id = SkillIds.Critical, Name = "Golpe crítico", Description = "12% de chance de causar dano triplo.", Branch = 2, Tier = 3, Cost = 400000, Requires = SkillIds.Cadence },

        new() { Id = SkillIds.Haggle, Name = "Negociador", Description = "+10% no valor de venda.", Branch = 3, Tier = 1, Cost = 3000 },
        new() { Id = SkillIds.RareNose, Name = "Faro para raros", Description = "Minérios raros aparecem 50% mais.", Branch = 3, Tier = 2, Cost = 45000, Requires = SkillIds.Haggle },
        new() { Id = SkillIds.Jeweler, Name = "Joalheiro", Description = "Minérios raros e especiais valem +50%.", Branch = 3, Tier = 3, Cost = 500000, Requires = SkillIds.RareNose },

        new() { Id = SkillIds.BuildBelt, Name = "Esteira", Description = "Libera a esteira no modo construção (B): marque o início e arraste até a Estação de Venda.", Branch = 4, Tier = 1, Cost = 1500 },
        new() { Id = SkillIds.BuildHopper, Name = "Funil", Description = "Libera o funil: ao lado de uma esteira, puxa fragmentos soltos e despeja nela.", Branch = 4, Tier = 2, Cost = 12000, Requires = SkillIds.BuildBelt },
        new() { Id = SkillIds.BuildDrone, Name = "Drone de área", Description = "Libera o drone: recolhe fragmentos soltos dentro da área da base dele.", Branch = 4, Tier = 3, Cost = 60000, Requires = SkillIds.BuildHopper },
        new() { Id = SkillIds.BuildRobot, Name = "Robô mineiro", Description = "Libera o robô: minera sozinho dentro da área dele, mais devagar que um goblin.", Branch = 4, Tier = 4, Cost = 200000, Requires = SkillIds.BuildDrone },
    };

    public static HouseUpgradeDef HouseDef(string id) => House.FirstOrDefault(h => h.Id == id);
    public static SkillDef Skill(string id) => Skills.FirstOrDefault(s => s.Id == id);
}

/// <summary>Goblin minerador de uma região: escondido numa câmara secreta até ser encontrado.</summary>
public sealed class GoblinDef
{
    public string RegionId;
    public string Name;
    string _title;
    /// <summary>Traduzido para o idioma escolhido (fonte em português).</summary> 
    public string Title { get => Core.Loc.T(_title); set => _title = value; }
    /// <summary>Texto original em português (chave estável).</summary>
    public string TitlePt => _title;
    public long HireCost, RehireCost;
    public float MineDps;         // devagar em relação ao jogador naquela fase
    public string[] Comrades;     // comparsas na rebelião
    public Godot.Color Skin;
    public long MinFee => System.Math.Max(30, HireCost / 16);
}

public static class GoblinData
{
    public static readonly List<GoblinDef> All = new()
    {
        new() { RegionId = "caves", Name = "Grub", Title = "o goblin minerador", HireCost = 500, RehireCost = 2000, MineDps = 4f, Comrades = new[] { "Snik", "Brugo" }, Skin = new Godot.Color(0.42f, 0.66f, 0.3f) },
        new() { RegionId = "depths", Name = "Frizz", Title = "o goblin do gelo", HireCost = 6000, RehireCost = 20000, MineDps = 35f, Comrades = new[] { "Pingo", "Geada" }, Skin = new Godot.Color(0.45f, 0.65f, 0.85f) },
        new() { RegionId = "core", Name = "Krag", Title = "o goblin da forja", HireCost = 60000, RehireCost = 200000, MineDps = 300f, Comrades = new[] { "Brasa", "Fuligem" }, Skin = new Godot.Color(0.75f, 0.35f, 0.25f) },
        new() { RegionId = "abyss", Name = "Nixa", Title = "a goblin abissal", HireCost = 250000, RehireCost = 800000, MineDps = 1500f, Comrades = new[] { "Lodo", "Concha" }, Skin = new Godot.Color(0.3f, 0.7f, 0.65f) },
        new() { RegionId = "spore", Name = "Musgo", Title = "o goblin dos cogumelos", HireCost = 1_200_000, RehireCost = 4_000_000, MineDps = 7000f, Comrades = new[] { "Bolor", "Fungo" }, Skin = new Godot.Color(0.55f, 0.75f, 0.35f) },
        new() { RegionId = "cathedral", Name = "Zirko", Title = "o goblin lapidador", HireCost = 6_000_000, RehireCost = 20_000_000, MineDps = 40000f, Comrades = new[] { "Lasca", "Quilate" }, Skin = new Godot.Color(0.6f, 0.55f, 0.8f) },
    };

    public static GoblinDef Of(string regionId) => All.Find(g => g.RegionId == regionId);
}

