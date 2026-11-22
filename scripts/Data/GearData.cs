using System.Collections.Generic;

namespace CrystalExcavator.Data;

/// <summary>Peças de equipamento forjadas pelo ferreiro da vila.</summary>
public enum GearSlot { Helmet, Gloves, Boots, Bag }

/// <summary>Ingrediente de uma receita da forja: N fragmentos de um minério.</summary>
public readonly record struct ForgeCost(string Mat, int Amount);

/// <summary>Um nível de uma peça: o que custa forjar e o bônus que dá.</summary>
public sealed class GearTierDef
{
    public GearSlot Slot;
    public int Tier;           // 0..5 (= região onde se acham os minérios)
    string _name;
    public string Name { get => Core.Loc.T(_name); set => _name = value; }
    public string NamePt => _name;
    public long Fee;
    public List<ForgeCost> Costs = new();
    /// <summary>Bônus principal (fração: 0,10 = +10%).</summary>
    public float Bonus;
    /// <summary>Bônus secundário (capacete: chance de raros).</summary>
    public float Bonus2;
}

/// <summary>
/// Forja (etapa 2 do plano de conteúdo): 4 peças em 6 níveis. Cada nível usa o metal e uma gema da região
/// de mesmo número, mais uma taxa em Lúmens. Forjar um nível substitui o anterior da mesma peça. Ao renascer,
/// o equipamento fica para trás (como as ferramentas).
/// </summary>
public static class GearData
{
    public const int Tiers = 6;
    public static readonly GearSlot[] Slots = { GearSlot.Helmet, GearSlot.Gloves, GearSlot.Boots, GearSlot.Bag };

    public static string SlotId(GearSlot s) => s switch { GearSlot.Helmet => "helmet", GearSlot.Gloves => "gloves", GearSlot.Boots => "boots", _ => "bag" };

    public static string SlotName(GearSlot s) => Core.Loc.T(s switch
    {
        GearSlot.Helmet => "Capacete", GearSlot.Gloves => "Luvas", GearSlot.Boots => "Botas", _ => "Bolsa",
    });

    /// <summary>Metal de cada nível (vai em todas as peças do nível).</summary>
    static readonly string[] Metal = { "copper", "silver", "gold", "titanium", "jade", "tanzanite" };
    static readonly string[] MetalName = { "Cobre", "Prata", "Ouro", "Titânio", "Jade", "Tanzanita" };
    /// <summary>Gema de cada peça por nível: capacete, luvas, botas, bolsa.</summary>
    static readonly string[,] Gem =
    {
        { "quartz", "hematite", "pyrite", "amethyst" },
        { "aquamarine", "cobalt", "frostice", "celestite" },
        { "garnet", "magmite", "obsidian", "sulfur" },
        { "turquoise", "lapis", "tidecrystal", "opal" },
        { "moonstone", "amber", "sporite", "chrysocolla" },
        { "smokyquartz", "diamond", "rainbowquartz", "alexandrite" },
    };
    static readonly long[] Fees = { 300, 6000, 60000, 300000, 1200000, 4000000 };
    static readonly int[] MetalAmount = { 20, 35, 40, 45, 50, 55 };
    static readonly int[] GemAmount = { 12, 20, 22, 25, 28, 30 };

    static readonly float[] Damage = { 0.10f, 0.20f, 0.32f, 0.45f, 0.60f, 0.80f };
    static readonly float[] Speed = { 0.04f, 0.08f, 0.12f, 0.16f, 0.20f, 0.25f };
    static readonly float[] Bag = { 0.12f, 0.25f, 0.40f, 0.55f, 0.75f, 1.00f };
    static readonly float[] Lamp = { 0.10f, 0.20f, 0.30f, 0.40f, 0.50f, 0.60f };
    static readonly float[] Rare = { 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.30f };

    static readonly Dictionary<GearSlot, GearTierDef[]> _all = new();

    static GearData()
    {
        foreach (var s in Slots)
        {
            var arr = new GearTierDef[Tiers];
            for (int t = 0; t < Tiers; t++)
            {
                string piece = s switch { GearSlot.Helmet => "Capacete", GearSlot.Gloves => "Luvas", GearSlot.Boots => "Botas", _ => "Bolsa" };
                var d = new GearTierDef
                {
                    Slot = s, Tier = t, Name = $"{piece} de {MetalName[t]}",
                    Fee = (long)System.Math.Round(Fees[t] * Economy.UpgradeCost),
                    Bonus = s switch { GearSlot.Helmet => Lamp[t], GearSlot.Gloves => Damage[t], GearSlot.Boots => Speed[t], _ => Bag[t] },
                    Bonus2 = s == GearSlot.Helmet ? Rare[t] : 0f,
                };
                d.Costs.Add(new ForgeCost(Metal[t], MetalAmount[t]));
                d.Costs.Add(new ForgeCost(Gem[t, (int)s], GemAmount[t]));
                arr[t] = d;
            }
            _all[s] = arr;
        }
    }

    public static GearTierDef Of(GearSlot s, int tier) => tier >= 0 && tier < Tiers ? _all[s][tier] : null;

    /// <summary>Texto do bônus de um nível ("+10% de dano").</summary>
    public static string BonusText(GearTierDef d) => d == null ? "" : d.Slot switch
    {
        GearSlot.Helmet => Core.Loc.F("+{0}% alcance da lanterna · +{1}% chance de raros", (int)System.Math.Round(d.Bonus * 100), (int)System.Math.Round(d.Bonus2 * 100)),
        GearSlot.Gloves => Core.Loc.F("+{0}% de dano", (int)System.Math.Round(d.Bonus * 100)),
        GearSlot.Boots => Core.Loc.F("+{0}% de velocidade", (int)System.Math.Round(d.Bonus * 100)),
        _ => Core.Loc.F("+{0}% de capacidade da mochila", (int)System.Math.Round(d.Bonus * 100)),
    };

    /// <summary>Conserto na forja: devolve a resistência da ferramenta por uma parte do preço e fragmentos do minério dela.</summary>
    public const float RepairFeeFraction = 0.35f;
    public const int RepairOreAmount = 12;
}
