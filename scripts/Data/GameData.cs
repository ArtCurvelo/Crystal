using System;
using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.Data;

/// <summary>Definição imutável de um material minerável.</summary>
public sealed class MaterialDef
{
    public string Id;
    public string Name;
    public string Description;
    public int Value;          // valor base de venda por fragmento
    public float Hp;           // resistência da formação
    public Color Color;        // cor principal
    public Color Glow;         // cor de emissão
    public int ShardsMin = 3;
    public int ShardsMax = 5;
    public bool Rare;
    public bool Special;       // formação especial (ex.: Coração Ancestral)
    public float RespawnSeconds = 25f;
}

public sealed class SpawnEntry
{
    public string MaterialId;
    public float Weight;
    public SpawnEntry(string id, float w) { MaterialId = id; Weight = w; }
}

/// <summary>Definição de uma região desbloqueável.</summary>
public sealed class RegionDef
{
    public string Id;
    public int Index;
    public string Name;
    public string Subtitle;
    public long UnlockCost;
    public Color AmbientColor;
    public float AmbientEnergy;
    public Color FogColor;
    public float FogDensity;
    public Color RockColor;
    public Color AccentColor;
    public Color LightColor;
    public List<SpawnEntry> Spawns = new();
    public string RareMaterialId;
    public float RareChance;
    public int CrystalSlots;
    // Geometria da sala (eixo Z negativo = mais fundo)
    public float CenterZ;
    public float HalfX;
    public float HalfZ;
    public float Height;
}

/// <summary>Definição de um upgrade com custo e efeito progressivos.</summary>
public sealed class UpgradeDef
{
    public string Id;
    public string Name;
    public string Description;
    public string Icon;          // caractere de ícone simples
    public long BaseCost;
    public double CostGrowth;
    public int MaxLevel;
    public Func<int, float> ValueAt;   // valor do atributo em um nível
    public Func<float, string> Format; // formatação para a UI

    public long CostAt(int level)
    {
        if (level >= MaxLevel) return long.MaxValue;
        double c = BaseCost * Math.Pow(CostGrowth, level);
        // arredonda para números "bonitos"
        if (c > 1000) c = Math.Round(c / 10.0) * 10.0;
        return (long)Math.Ceiling(c);
    }
}

public static class UpgradeIds
{
    public const string Damage = "damage";
    public const string Speed = "swing_speed";
    public const string Capacity = "capacity";
    public const string Move = "move_speed";
    public const string Value = "sell_value";
    public const string Reach = "reach";
    public const string Resonance = "resonance";
    public static readonly string[] All = { Damage, Speed, Capacity, Move, Value, Reach, Resonance };
}

public static class GameData
{
    public static readonly Dictionary<string, MaterialDef> Materials = new();
    public static readonly List<RegionDef> Regions = new();
    public static readonly Dictionary<string, UpgradeDef> Upgrades = new();

    static GameData()
    {
        // ---------------- Materiais ----------------
        Add(new MaterialDef
        {
            Id = "quartz", Name = "Quartzo Lúmen", Value = 3, Hp = 24,
            Description = "Cristal comum que brilha com luz suave.",
            Color = new Color(0.55f, 0.95f, 1.0f), Glow = new Color(0.25f, 0.85f, 1.0f),
            ShardsMin = 3, ShardsMax = 4, RespawnSeconds = 18
        });
        Add(new MaterialDef
        {
            Id = "amethyst", Name = "Ametista Cintilante", Value = 8, Hp = 60,
            Description = "Mais densa, vibra quando atingida.",
            Color = new Color(0.72f, 0.45f, 1.0f), Glow = new Color(0.6f, 0.25f, 1.0f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 24
        });
        Add(new MaterialDef
        {
            Id = "sunstone", Name = "Lágrima Solar", Value = 60, Hp = 110, Rare = true,
            Description = "Raríssima. Aquece a palma da mão.",
            Color = new Color(1.0f, 0.85f, 0.35f), Glow = new Color(1.0f, 0.65f, 0.1f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 40
        });
        Add(new MaterialDef
        {
            Id = "sapphire", Name = "Safira Prismática", Value = 26, Hp = 240,
            Description = "Refrata a luz em feixes azuis.",
            Color = new Color(0.3f, 0.55f, 1.0f), Glow = new Color(0.15f, 0.35f, 1.0f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 24
        });
        Add(new MaterialDef
        {
            Id = "emerald", Name = "Esmeralda Refrativa", Value = 55, Hp = 480,
            Description = "Cresce em veios sob a névoa fria.",
            Color = new Color(0.3f, 1.0f, 0.6f), Glow = new Color(0.1f, 0.9f, 0.45f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 28
        });
        Add(new MaterialDef
        {
            Id = "starprism", Name = "Prisma Estelar", Value = 420, Hp = 900, Rare = true,
            Description = "Contém pontos de luz como um céu noturno.",
            Color = new Color(1.0f, 0.55f, 0.95f), Glow = new Color(1.0f, 0.35f, 0.9f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 45
        });
        Add(new MaterialDef
        {
            Id = "ruby", Name = "Rubi Nuclear", Value = 160, Hp = 2200,
            Description = "Pulsa no ritmo do núcleo do planeta.",
            Color = new Color(1.0f, 0.3f, 0.3f), Glow = new Color(1.0f, 0.12f, 0.1f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 26
        });
        Add(new MaterialDef
        {
            Id = "ancestralite", Name = "Ancestralita", Value = 340, Hp = 4500,
            Description = "Mineral antigo com runas naturais.",
            Color = new Color(1.0f, 0.62f, 0.25f), Glow = new Color(1.0f, 0.45f, 0.05f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 30
        });
        Add(new MaterialDef
        {
            Id = "voidpearl", Name = "Pérola do Vazio", Value = 2600, Hp = 8000, Rare = true,
            Description = "Absorve a luz ao redor. Valor incalculável.",
            Color = new Color(0.85f, 0.95f, 1.0f), Glow = new Color(0.55f, 0.4f, 1.0f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 55
        });
        Add(new MaterialDef
        {
            Id = "heart", Name = "Coração Ancestral", Value = 2200, Hp = 150000, Special = true,
            Description = "Formação especial colossal. Libera uma chuva de fragmentos.",
            Color = new Color(1.0f, 0.95f, 0.75f), Glow = new Color(1.0f, 0.55f, 0.2f),
            ShardsMin = 18, ShardsMax = 24, RespawnSeconds = 120
        });

        // ---------------- Regiões ----------------
        var r1 = new RegionDef
        {
            Id = "caves", Index = 0, Name = "Cavernas Luminosas",
            Subtitle = "Onde todo escavador começa.", UnlockCost = 0,
            AmbientColor = new Color(0.35f, 0.45f, 0.7f), AmbientEnergy = 0.55f,
            FogColor = new Color(0.06f, 0.08f, 0.16f), FogDensity = 0.018f,
            RockColor = new Color(0.27f, 0.24f, 0.36f), AccentColor = new Color(0.35f, 0.9f, 1.0f),
            LightColor = new Color(0.55f, 0.8f, 1.0f),
            RareMaterialId = "sunstone", RareChance = 0.05f, CrystalSlots = 20,
            CenterZ = 0, HalfX = 21, HalfZ = 20, Height = 9
        };
        r1.Spawns.Add(new SpawnEntry("quartz", 0.72f));
        r1.Spawns.Add(new SpawnEntry("amethyst", 0.28f));
        Regions.Add(r1);

        var r2 = new RegionDef
        {
            Id = "depths", Index = 1, Name = "Profundezas Prismáticas",
            Subtitle = "Névoa gelada e cristais resistentes.", UnlockCost = 4000,
            AmbientColor = new Color(0.2f, 0.45f, 0.6f), AmbientEnergy = 0.5f,
            FogColor = new Color(0.05f, 0.14f, 0.2f), FogDensity = 0.035f,
            RockColor = new Color(0.16f, 0.25f, 0.33f), AccentColor = new Color(0.3f, 1.0f, 0.7f),
            LightColor = new Color(0.4f, 1.0f, 0.85f),
            RareMaterialId = "starprism", RareChance = 0.06f, CrystalSlots = 22,
            CenterZ = -58, HalfX = 26, HalfZ = 26, Height = 12
        };
        r2.Spawns.Add(new SpawnEntry("sapphire", 0.62f));
        r2.Spawns.Add(new SpawnEntry("emerald", 0.38f));
        Regions.Add(r2);

        var r3 = new RegionDef
        {
            Id = "core", Index = 2, Name = "Núcleo Ancestral",
            Subtitle = "O coração quente do mundo.", UnlockCost = 150000,
            AmbientColor = new Color(0.6f, 0.3f, 0.2f), AmbientEnergy = 0.55f,
            FogColor = new Color(0.18f, 0.05f, 0.03f), FogDensity = 0.022f,
            RockColor = new Color(0.3f, 0.14f, 0.12f), AccentColor = new Color(1.0f, 0.55f, 0.2f),
            LightColor = new Color(1.0f, 0.55f, 0.3f),
            RareMaterialId = "voidpearl", RareChance = 0.05f, CrystalSlots = 24,
            CenterZ = -124, HalfX = 30, HalfZ = 30, Height = 16
        };
        r3.Spawns.Add(new SpawnEntry("ruby", 0.6f));
        r3.Spawns.Add(new SpawnEntry("ancestralite", 0.4f));
        Regions.Add(r3);

        // ---------------- Upgrades ----------------
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Damage, Name = "Força da Picareta", Icon = "⚒",
            Description = "Aumenta o dano de cada golpe.",
            BaseCost = 20, CostGrowth = 1.48, MaxLevel = 30,
            ValueAt = l => 4f * MathF.Pow(1.32f, l) + l * 2f,
            Format = v => $"{v:0} dano"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Speed, Name = "Ritmo de Extração", Icon = "⚡",
            Description = "Golpes mais rápidos por segundo.",
            BaseCost = 35, CostGrowth = 1.62, MaxLevel = 15,
            ValueAt = l => 1f / MathF.Max(0.11f, 0.5f * MathF.Pow(0.9f, l)),
            Format = v => $"{v:0.0} golpes/s"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Capacity, Name = "Mochila Reforçada", Icon = "▣",
            Description = "Carrega mais fragmentos antes de vender.",
            BaseCost = 30, CostGrowth = 1.5, MaxLevel = 20,
            ValueAt = l => 30 + l * 15 + l * l * 2,
            Format = v => $"{v:0} fragmentos"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Move, Name = "Botas de Escavador", Icon = "»",
            Description = "Movimenta-se mais rápido pelas cavernas.",
            BaseCost = 45, CostGrowth = 1.7, MaxLevel = 10,
            ValueAt = l => 4.6f + l * 0.42f,
            Format = v => $"{v:0.0} m/s"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Value, Name = "Lapidação Fina", Icon = "◆",
            Description = "Cada fragmento vale mais na venda.",
            BaseCost = 90, CostGrowth = 1.6, MaxLevel = 25,
            ValueAt = l => 1f + l * 0.15f + l * l * 0.01f,
            Format = v => $"x{v:0.00} valor"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Reach, Name = "Ímã de Coleta", Icon = "◎",
            Description = "Alcance de mineração e raio de atração de fragmentos.",
            BaseCost = 55, CostGrowth = 1.6, MaxLevel = 12,
            ValueAt = l => 2.5f + l * 0.9f,
            Format = v => $"{v:0.0} m de atração"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Resonance, Name = "Amplificador de Ressonância", Icon = "✺",
            Description = "Golpes em ritmo carregam um pulso que atinge todos os cristais próximos.",
            BaseCost = 250, CostGrowth = 1.75, MaxLevel = 12,
            ValueAt = l => 1.5f + l * 0.75f,
            Format = v => $"pulso x{v:0.0} · raio {ResonanceRadius(v):0.0} m"
        });
    }

    /// <summary>Raio do pulso de ressonância derivado do multiplicador.</summary>
    public static float ResonanceRadius(float mult) => 4f + (mult - 1.5f) * 0.8f;
    /// <summary>Golpes rítmicos necessários para carregar um pulso.</summary>
    public const int ResonanceHits = 10;
    /// <summary>Janela (s) entre golpes para manter o ritmo.</summary>
    public const float ResonanceWindow = 1.4f;

    static void Add(MaterialDef m) => Materials[m.Id] = m;
    static void AddUp(UpgradeDef u) => Upgrades[u.Id] = u;

    public static MaterialDef Mat(string id) => Materials.TryGetValue(id, out var m) ? m : null;
    public static RegionDef Region(string id) => Regions.Find(r => r.Id == id);
}
