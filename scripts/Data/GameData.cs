using System;
using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.Data;

/// <summary>Formato visual do depósito (cada um com malha e textura procedural próprias).</summary>
public enum OreArchetype { Crystal, Vein, Geode, Banded, Shards, Ice, Magma, Coral, Pearl }

/// <summary>Raridade de jogo (valor/frequência), exibida com cor e selo.</summary>
public enum Rarity { Comum, Incomum, Raro, Epico, Lendario }

public static class RarityInfo
{
    public static string Name(Rarity r) => r switch
    {
        Rarity.Comum => "Comum", Rarity.Incomum => "Incomum", Rarity.Raro => "Raro", Rarity.Epico => "Épico", _ => "Lendário",
    };

    public static Color Color(Rarity r) => r switch
    {
        Rarity.Comum => new Color(0.85f, 0.88f, 0.95f),
        Rarity.Incomum => new Color(0.35f, 1f, 0.45f),
        Rarity.Raro => new Color(0.3f, 0.8f, 1f),
        Rarity.Epico => new Color(0.75f, 0.45f, 1f),
        _ => new Color(1f, 0.8f, 0.25f),
    };

    public static string Badge(Rarity r) => (r == Rarity.Lendario ? "★ " : "◆ ") + Name(r);
}

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
    public Rarity Rarity = Rarity.Comum;
    public OreArchetype Archetype = OreArchetype.Crystal;
    /// <summary>Geodo com esferas iridescentes (opala) em vez de cristais.</summary>
    public bool GeodeSpheres;
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
    /// <summary>Câmaras com nome e minério em destaque dentro da caverna da região.</summary>
    public List<WingDef> Wings = new();
    /// <summary>Distância entre regiões (comprimento do túnel com o portão).</summary>
    public const float TunnelLength = 14f;
}

/// <summary>Galeria lateral de uma região: sala menor com um minério em destaque.</summary>
public sealed class WingDef
{
    public string Id;
    public string Name;
    /// <summary>-1 = oeste, +1 = leste.</summary>
    public int Side;
    /// <summary>Deslocamento em Z da abertura em relação ao centro da região.</summary>
    public float ZOffset;
    public string FeaturedMaterialId;
    public int Slots = 9;
    public float HalfX = 13, HalfZ = 12;
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
            Id = "quartz", Rarity = Rarity.Comum, Name = "Quartzo Lúmen", Value = 3, Hp = 24,
            Description = "Cristal comum que brilha com luz suave.",
            Color = new Color(0.55f, 0.95f, 1.0f), Glow = new Color(0.25f, 0.85f, 1.0f),
            ShardsMin = 3, ShardsMax = 4, RespawnSeconds = 18
        });
        Add(new MaterialDef
        {
            Id = "amethyst", Archetype = OreArchetype.Geode, Rarity = Rarity.Incomum, Name = "Ametista Cintilante", Value = 8, Hp = 60,
            Description = "Mais densa, vibra quando atingida.",
            Color = new Color(0.72f, 0.45f, 1.0f), Glow = new Color(0.6f, 0.25f, 1.0f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 24
        });
        Add(new MaterialDef
        {
            Id = "sunstone", Rarity = Rarity.Epico, Name = "Lágrima Solar", Value = 60, Hp = 110, Rare = true,
            Description = "Raríssima. Aquece a palma da mão.",
            Color = new Color(1.0f, 0.85f, 0.35f), Glow = new Color(1.0f, 0.65f, 0.1f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 40
        });
        Add(new MaterialDef
        {
            Id = "sapphire", Rarity = Rarity.Incomum, Name = "Safira Prismática", Value = 26, Hp = 240,
            Description = "Refrata a luz em feixes azuis.",
            Color = new Color(0.3f, 0.55f, 1.0f), Glow = new Color(0.15f, 0.35f, 1.0f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 24
        });
        Add(new MaterialDef
        {
            Id = "emerald", Rarity = Rarity.Raro, Name = "Esmeralda Refrativa", Value = 55, Hp = 480,
            Description = "Cresce em veios sob a névoa fria.",
            Color = new Color(0.3f, 1.0f, 0.6f), Glow = new Color(0.1f, 0.9f, 0.45f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 28
        });
        Add(new MaterialDef
        {
            Id = "starprism", Rarity = Rarity.Epico, Name = "Prisma Estelar", Value = 420, Hp = 900, Rare = true,
            Description = "Contém pontos de luz como um céu noturno.",
            Color = new Color(1.0f, 0.55f, 0.95f), Glow = new Color(1.0f, 0.35f, 0.9f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 45
        });
        Add(new MaterialDef
        {
            Id = "ruby", Rarity = Rarity.Raro, Name = "Rubi Nuclear", Value = 160, Hp = 2200,
            Description = "Pulsa no ritmo do núcleo do planeta.",
            Color = new Color(1.0f, 0.3f, 0.3f), Glow = new Color(1.0f, 0.12f, 0.1f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 26
        });
        Add(new MaterialDef
        {
            Id = "ancestralite", Archetype = OreArchetype.Banded, Rarity = Rarity.Epico, Name = "Ancestralita", Value = 340, Hp = 4500,
            Description = "Mineral antigo com runas naturais.",
            Color = new Color(1.0f, 0.62f, 0.25f), Glow = new Color(1.0f, 0.45f, 0.05f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 30
        });
        Add(new MaterialDef
        {
            Id = "voidpearl", Archetype = OreArchetype.Pearl, Rarity = Rarity.Lendario, Name = "Pérola do Vazio", Value = 2600, Hp = 8000, Rare = true,
            Description = "Absorve a luz ao redor. Valor incalculável.",
            Color = new Color(0.85f, 0.95f, 1.0f), Glow = new Color(0.55f, 0.4f, 1.0f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 55
        });
        Add(new MaterialDef
        {
            Id = "heart", Rarity = Rarity.Lendario, Name = "Coração Ancestral", Value = 2200, Hp = 600000, Special = true,
            Description = "Formação especial colossal. Libera uma chuva de fragmentos.",
            Color = new Color(1.0f, 0.95f, 0.75f), Glow = new Color(1.0f, 0.55f, 0.2f),
            ShardsMin = 18, ShardsMax = 24, RespawnSeconds = 120
        });
        // Reservatório Abissal (região bônus)
        Add(new MaterialDef
        {
            Id = "tidecrystal", Rarity = Rarity.Raro, Name = "Cristal de Maré", Value = 700, Hp = 9000,
            Description = "Cresce sob a água e pulsa com as correntes.",
            Color = new Color(0.35f, 1.0f, 0.95f), Glow = new Color(0.1f, 0.85f, 0.9f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 28
        });
        Add(new MaterialDef
        {
            Id = "coralprism", Archetype = OreArchetype.Coral, Rarity = Rarity.Epico, Name = "Coral Prismático", Value = 1300, Hp = 16000,
            Description = "Mistura de cristal e vida; muda de cor com a luz.",
            Color = new Color(1.0f, 0.45f, 0.8f), Glow = new Color(0.95f, 0.25f, 0.75f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 32
        });
        Add(new MaterialDef
        {
            Id = "abyssaltear", Rarity = Rarity.Lendario, Name = "Lágrima Abissal", Value = 9000, Hp = 30000, Rare = true,
            Description = "Dizem que guarda o brilho de um oceano esquecido.",
            Color = new Color(0.7f, 0.85f, 1.0f), Glow = new Color(0.4f, 0.55f, 1.0f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 60
        });


        // ---------------- Novos minérios (variedade por região) ----------------
        Add(new MaterialDef
        {
            Id = "copper", Rarity = Rarity.Comum, Archetype = OreArchetype.Vein, Name = "Cobre Nativo", Value = 5, Hp = 40,
            Description = "Veios de cobre puro que afloram nas cavernas rasas.",
            Color = new Color(0.95f, 0.55f, 0.3f), Glow = new Color(1.0f, 0.45f, 0.15f),
            ShardsMin = 3, ShardsMax = 4, RespawnSeconds = 20
        });
        Add(new MaterialDef
        {
            Id = "frostice", Rarity = Rarity.Incomum, Archetype = OreArchetype.Ice, Name = "Gelo Eterno", Value = 30, Hp = 280,
            Description = "Nunca derrete. Estala a cada golpe.",
            Color = new Color(0.7f, 0.9f, 1.0f), Glow = new Color(0.45f, 0.8f, 1.0f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 24
        });
        Add(new MaterialDef
        {
            Id = "silver", Rarity = Rarity.Incomum, Archetype = OreArchetype.Vein, Name = "Prata Gélida", Value = 40, Hp = 340,
            Description = "Pepitas de prata presas em rocha congelada.",
            Color = new Color(0.82f, 0.86f, 0.92f), Glow = new Color(0.6f, 0.8f, 1.0f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 26
        });
        Add(new MaterialDef
        {
            Id = "malachite", Rarity = Rarity.Raro, Archetype = OreArchetype.Banded, Name = "Malaquita", Value = 75, Hp = 620,
            Description = "Faixas verdes hipnóticas, polidas pela água.",
            Color = new Color(0.08f, 0.45f, 0.3f), Glow = new Color(0.3f, 1.0f, 0.6f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 30
        });
        Add(new MaterialDef
        {
            Id = "obsidian", Rarity = Rarity.Incomum, Archetype = OreArchetype.Shards, Name = "Obsidiana", Value = 120, Hp = 1800,
            Description = "Vidro vulcânico afiado como navalha.",
            Color = new Color(0.08f, 0.06f, 0.1f), Glow = new Color(0.7f, 0.4f, 1.0f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 24
        });
        Add(new MaterialDef
        {
            Id = "magmite", Rarity = Rarity.Raro, Archetype = OreArchetype.Magma, Name = "Magmita", Value = 210, Hp = 2900,
            Description = "Rocha viva: as rachaduras pulsam com calor.",
            Color = new Color(1.0f, 0.45f, 0.1f), Glow = new Color(1.0f, 0.35f, 0.05f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 28
        });
        Add(new MaterialDef
        {
            Id = "gold", Rarity = Rarity.Raro, Archetype = OreArchetype.Vein, Name = "Ouro Ancestral", Value = 280, Hp = 3600,
            Description = "Ouro denso, marcado por runas antigas.",
            Color = new Color(1.0f, 0.8f, 0.3f), Glow = new Color(1.0f, 0.7f, 0.2f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 30
        });
        Add(new MaterialDef
        {
            Id = "titanium", Rarity = Rarity.Raro, Archetype = OreArchetype.Vein, Name = "Titânio Abissal", Value = 1000, Hp = 12000,
            Description = "Leve e indestrutível. Brilha azul sob a água.",
            Color = new Color(0.6f, 0.7f, 0.85f), Glow = new Color(0.3f, 0.7f, 1.0f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 30
        });
        Add(new MaterialDef
        {
            Id = "opal", Rarity = Rarity.Epico, Archetype = OreArchetype.Geode, GeodeSpheres = true, Name = "Opala Abissal", Value = 1900, Hp = 21000,
            Description = "Geodo repleto de esferas que mudam de cor.",
            Color = new Color(0.85f, 0.9f, 1.0f), Glow = new Color(0.7f, 0.5f, 1.0f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 40
        });

        // ---------------- Atualização das cavernas: mais variedade ----------------
        Add(new MaterialDef
        {
            Id = "pyrite", Rarity = Rarity.Comum, Archetype = OreArchetype.Vein, Name = "Pirita", Value = 4, Hp = 32,
            Description = "O \"ouro dos tolos\": cubos dourados que enganam escavadores novatos, mas ainda valem algo.",
            Color = new Color(0.85f, 0.75f, 0.35f), Glow = new Color(1.0f, 0.85f, 0.3f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 20
        });
        Add(new MaterialDef
        {
            Id = "jasper", Rarity = Rarity.Incomum, Archetype = OreArchetype.Banded, Name = "Jaspe Rubro", Value = 10, Hp = 75,
            Description = "Pedra em faixas vermelhas e alaranjadas, formada por camadas de lama antiga.",
            Color = new Color(0.75f, 0.25f, 0.18f), Glow = new Color(1.0f, 0.45f, 0.25f),
            ShardsMin = 3, ShardsMax = 4, RespawnSeconds = 24
        });
        Add(new MaterialDef
        {
            Id = "aquamarine", Rarity = Rarity.Incomum, Archetype = OreArchetype.Crystal, Name = "Água-marinha", Value = 34, Hp = 300,
            Description = "Cristal verde-azulado, translúcido como a água das poças das Profundezas.",
            Color = new Color(0.45f, 0.95f, 0.9f), Glow = new Color(0.3f, 1.0f, 0.85f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 26
        });
        Add(new MaterialDef
        {
            Id = "cobalt", Rarity = Rarity.Raro, Archetype = OreArchetype.Vein, Name = "Cobalto Glacial", Value = 62, Hp = 520,
            Description = "Metal azul-profundo; os veios brilham como gelo quando atingidos.",
            Color = new Color(0.2f, 0.35f, 0.85f), Glow = new Color(0.3f, 0.55f, 1.0f),
            ShardsMin = 3, ShardsMax = 4, RespawnSeconds = 28
        });
        Add(new MaterialDef
        {
            Id = "fluorite", Rarity = Rarity.Raro, Archetype = OreArchetype.Geode, Name = "Fluorita Dupla", Value = 90, Hp = 700,
            Description = "Geodo com cristais roxos e verdes que trocam de cor sob a luz da ferramenta.",
            Color = new Color(0.6f, 0.4f, 0.95f), Glow = new Color(0.4f, 1.0f, 0.6f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 30
        });
        Add(new MaterialDef
        {
            Id = "sulfur", Rarity = Rarity.Incomum, Archetype = OreArchetype.Shards, Name = "Enxofre Vivo", Value = 140, Hp = 2000,
            Description = "Lascas amarelas que chiam no calor do Núcleo. Cheiro inconfundível.",
            Color = new Color(0.95f, 0.85f, 0.2f), Glow = new Color(1.0f, 0.9f, 0.25f),
            ShardsMin = 4, ShardsMax = 6, RespawnSeconds = 28
        });
        Add(new MaterialDef
        {
            Id = "garnet", Rarity = Rarity.Raro, Archetype = OreArchetype.Crystal, Name = "Granada Profunda", Value = 190, Hp = 2500,
            Description = "Cristais vinho-escuros, densos, que soam graves ao serem atingidos.",
            Color = new Color(0.55f, 0.08f, 0.2f), Glow = new Color(0.9f, 0.15f, 0.35f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 32
        });
        Add(new MaterialDef
        {
            Id = "meteorite", Rarity = Rarity.Epico, Archetype = OreArchetype.Vein, Name = "Ferro Meteórico", Value = 420, Hp = 5200,
            Description = "Metal que caiu do céu há eras e afundou até o Núcleo. Veios com brilho azul-estelar.",
            Color = new Color(0.3f, 0.3f, 0.36f), Glow = new Color(0.45f, 0.65f, 1.0f),
            ShardsMin = 3, ShardsMax = 4, RespawnSeconds = 34
        });
        Add(new MaterialDef
        {
            Id = "lapis", Rarity = Rarity.Raro, Archetype = OreArchetype.Banded, Name = "Lápis-lazúli", Value = 850, Hp = 10500,
            Description = "Rocha azul-real salpicada de pontos dourados, em camadas do fundo do lago.",
            Color = new Color(0.12f, 0.2f, 0.7f), Glow = new Color(1.0f, 0.82f, 0.3f),
            ShardsMin = 3, ShardsMax = 5, RespawnSeconds = 30
        });
        Add(new MaterialDef
        {
            Id = "blackpearl", Rarity = Rarity.Epico, Archetype = OreArchetype.Pearl, Name = "Pérola Negra", Value = 2200, Hp = 24000,
            Description = "Pérolas escuras com reflexos de arco-íris, crescidas em ostras fossilizadas.",
            Color = new Color(0.12f, 0.1f, 0.16f), Glow = new Color(0.75f, 0.5f, 1.0f),
            ShardsMin = 3, ShardsMax = 4, RespawnSeconds = 36
        });

        // ---------------- Regiões ----------------
        var r1 = new RegionDef
        {
            Id = "caves", Index = 0, Name = "Cavernas Luminosas",
            Subtitle = "Onde todo escavador começa.", UnlockCost = 0,
            AmbientColor = new Color(0.45f, 0.35f, 0.8f), AmbientEnergy = 0.6f,
            FogColor = new Color(0.07f, 0.03f, 0.14f), FogDensity = 0.018f,
            RockColor = new Color(0.24f, 0.18f, 0.26f), AccentColor = new Color(0.15f, 1.0f, 1.0f),
            LightColor = new Color(1.0f, 0.35f, 0.85f),
            RareMaterialId = "sunstone", RareChance = 0.05f, CrystalSlots = 130,
            CenterZ = 0, HalfX = 95, HalfZ = 90, Height = 18
        };
r1.Spawns.Add(new SpawnEntry("quartz", 0.4f));
        r1.Spawns.Add(new SpawnEntry("copper", 0.25f));
        r1.Spawns.Add(new SpawnEntry("pyrite", 0.15f));
        r1.Spawns.Add(new SpawnEntry("amethyst", 0.12f));
        r1.Spawns.Add(new SpawnEntry("jasper", 0.08f));
        r1.Wings.Add(new WingDef { Id = "copper_vein", Name = "Veio de Cobre", Side = -1, ZOffset = -12, FeaturedMaterialId = "copper", Slots = 8, HalfX = 11, HalfZ = 10 });
        r1.Wings.Add(new WingDef { Id = "amethyst_grotto", Name = "Gruta das Ametistas", Side = 1, ZOffset = 6, FeaturedMaterialId = "amethyst", Slots = 8, HalfX = 11, HalfZ = 10 });
        Regions.Add(r1);

        var r2 = new RegionDef
        {
            Id = "depths", Index = 1, Name = "Profundezas Prismáticas",
            Subtitle = "Névoa gelada e cristais resistentes.", UnlockCost = 6000,
            AmbientColor = new Color(0.2f, 0.5f, 0.9f), AmbientEnergy = 0.7f,
            FogColor = new Color(0.02f, 0.08f, 0.18f), FogDensity = 0.026f,
            RockColor = new Color(0.12f, 0.17f, 0.26f), AccentColor = new Color(0.1f, 1.0f, 0.65f),
            LightColor = new Color(0.25f, 0.6f, 1.0f),
            RareMaterialId = "starprism", RareChance = 0.06f, CrystalSlots = 160,
            CenterZ = -204, HalfX = 110, HalfZ = 100, Height = 22
        };
r2.Spawns.Add(new SpawnEntry("sapphire", 0.26f));
        r2.Spawns.Add(new SpawnEntry("frostice", 0.2f));
        r2.Spawns.Add(new SpawnEntry("aquamarine", 0.14f));
        r2.Spawns.Add(new SpawnEntry("silver", 0.14f));
        r2.Spawns.Add(new SpawnEntry("emerald", 0.1f));
        r2.Spawns.Add(new SpawnEntry("cobalt", 0.08f));
        r2.Spawns.Add(new SpawnEntry("malachite", 0.05f));
        r2.Spawns.Add(new SpawnEntry("fluorite", 0.03f));
        r2.Wings.Add(new WingDef { Id = "frozen_grotto", Name = "Gruta Congelada", Side = -1, ZOffset = 2, FeaturedMaterialId = "frostice" });
        r2.Wings.Add(new WingDef { Id = "silver_vein", Name = "Veio de Prata", Side = 1, ZOffset = -8, FeaturedMaterialId = "silver" });
        Regions.Add(r2);

        var r3 = new RegionDef
        {
            Id = "core", Index = 2, Name = "Núcleo Ancestral",
            Subtitle = "O coração quente do mundo.", UnlockCost = 400000,
            AmbientColor = new Color(0.7f, 0.25f, 0.6f), AmbientEnergy = 0.6f,
            FogColor = new Color(0.14f, 0.03f, 0.1f), FogDensity = 0.022f,
            RockColor = new Color(0.22f, 0.12f, 0.16f), AccentColor = new Color(1.0f, 0.5f, 0.1f),
            LightColor = new Color(1.0f, 0.2f, 0.6f),
            RareMaterialId = "voidpearl", RareChance = 0.05f, CrystalSlots = 170,
            CenterZ = -423, HalfX = 115, HalfZ = 105, Height = 28
        };
r3.Spawns.Add(new SpawnEntry("ruby", 0.26f));
        r3.Spawns.Add(new SpawnEntry("obsidian", 0.2f));
        r3.Spawns.Add(new SpawnEntry("sulfur", 0.14f));
        r3.Spawns.Add(new SpawnEntry("magmite", 0.14f));
        r3.Spawns.Add(new SpawnEntry("garnet", 0.1f));
        r3.Spawns.Add(new SpawnEntry("gold", 0.08f));
        r3.Spawns.Add(new SpawnEntry("ancestralite", 0.05f));
        r3.Spawns.Add(new SpawnEntry("meteorite", 0.03f));
        r3.Wings.Add(new WingDef { Id = "old_forge", Name = "Forja Antiga", Side = -1, ZOffset = -0.5f, FeaturedMaterialId = "magmite", Slots = 10, HalfX = 14, HalfZ = 13 });
        r3.Wings.Add(new WingDef { Id = "obsidian_hall", Name = "Câmara de Obsidiana", Side = 1, ZOffset = -0.5f, FeaturedMaterialId = "obsidian", Slots = 10, HalfX = 14, HalfZ = 13 });
        Regions.Add(r3);

        var r4 = new RegionDef
        {
            Id = "abyss", Index = 3, Name = "Reservatório Abissal",
            Subtitle = "Região bônus: um lago sereno e luminoso.", UnlockCost = 1500000,
            AmbientColor = new Color(0.2f, 0.6f, 0.9f), AmbientEnergy = 0.75f,
            FogColor = new Color(0.01f, 0.09f, 0.14f), FogDensity = 0.024f,
            RockColor = new Color(0.1f, 0.15f, 0.22f), AccentColor = new Color(1.0f, 0.25f, 0.85f),
            LightColor = new Color(0.2f, 0.95f, 1.0f),
            RareMaterialId = "abyssaltear", RareChance = 0.05f, CrystalSlots = 170,
            CenterZ = -647, HalfX = 115, HalfZ = 105, Height = 26
        };
r4.Spawns.Add(new SpawnEntry("tidecrystal", 0.32f));
        r4.Spawns.Add(new SpawnEntry("lapis", 0.2f));
        r4.Spawns.Add(new SpawnEntry("coralprism", 0.18f));
        r4.Spawns.Add(new SpawnEntry("titanium", 0.15f));
        r4.Spawns.Add(new SpawnEntry("opal", 0.1f));
        r4.Spawns.Add(new SpawnEntry("blackpearl", 0.05f));
        r4.Wings.Add(new WingDef { Id = "coral_garden", Name = "Jardim de Coral", Side = -1, ZOffset = 4, FeaturedMaterialId = "coralprism", Slots = 10, HalfX = 14, HalfZ = 13 });
        r4.Wings.Add(new WingDef { Id = "opal_well", Name = "Poço de Opalas", Side = 1, ZOffset = 6, FeaturedMaterialId = "opal", Slots = 10, HalfX = 14, HalfZ = 13 });
        Regions.Add(r4);

        // ---------------- Upgrades ----------------
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Damage, Name = "Potência do Feixe", Icon = "⚡",
            Description = "Aumenta o dano de cada pulso do feixe.",
            BaseCost = 20, CostGrowth = 1.55, MaxLevel = 30,
            ValueAt = l => 4f * MathF.Pow(1.32f, l) + l * 2f,
            Format = v => $"{v:0} dano"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Speed, Name = "Ritmo de Extração", Icon = "»",
            Description = "Golpes mais rápidos por segundo.",
            BaseCost = 35, CostGrowth = 1.62, MaxLevel = 15,
            ValueAt = l => 1f / MathF.Max(0.11f, 0.5f * MathF.Pow(0.9f, l)),
            Format = v => $"{v:0.0} golpes/s"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Capacity, Name = "Mochila Reforçada", Icon = "▣",
            Description = "Carrega mais fragmentos antes de vender.",
            BaseCost = 30, CostGrowth = 1.56, MaxLevel = 20,
            ValueAt = l => 30 + l * 15 + l * l * 2,
            Format = v => $"{v:0} fragmentos"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Move, Name = "Botas de Escavador", Icon = "➤",
            Description = "Movimenta-se mais rápido pelas cavernas.",
            BaseCost = 45, CostGrowth = 1.7, MaxLevel = 10,
            ValueAt = l => 4.6f + l * 0.42f,
            Format = v => $"{v:0.0} m/s"
        });
        AddUp(new UpgradeDef
        {
            Id = UpgradeIds.Value, Name = "Lapidação Fina", Icon = "◆",
            Description = "Cada fragmento vale mais na venda.",
            BaseCost = 90, CostGrowth = 1.7, MaxLevel = 25,
            ValueAt = l => 1f + l * 0.12f + l * l * 0.004f,
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
            BaseCost = 400, CostGrowth = 1.9, MaxLevel = 12,
            ValueAt = l => 1.5f + l * 0.5f,
            Format = v => $"pulso x{v:0.0} · raio {ResonanceRadius(v):0.0} m"
        });
    }

    /// <summary>Raio do pulso de ressonância derivado do multiplicador.</summary>
    public static float ResonanceRadius(float mult) => 4f + (mult - 1.5f) * 0.55f;
    /// <summary>Golpes rítmicos necessários para carregar um pulso.</summary>
    public const int ResonanceHits = 10;
    /// <summary>Janela (s) entre golpes para manter o ritmo.</summary>
    public const float ResonanceWindow = 1.4f;

    static void Add(MaterialDef m) => Materials[m.Id] = m;
    static void AddUp(UpgradeDef u) => Upgrades[u.Id] = u;

    public static MaterialDef Mat(string id) => Materials.TryGetValue(id, out var m) ? m : null;
    public static RegionDef Region(string id) => Regions.Find(r => r.Id == id);
}
