using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace CrystalExcavator.Data;

public enum WeaponKind { Sword, Hammer }

/// <summary>Arma forjada combinando minérios (a ferramenta continua só para minerar).</summary>
public sealed class WeaponDef
{
    public WeaponKind Kind;
    public List<string> Ores = new();
    public string Name;
    public float Damage;
    /// <summary>Golpes por segundo.</summary>
    public float Speed;
    /// <summary>Alcance (m) e abertura do golpe (graus, para os lados).</summary>
    public float Reach, Arc;
    public float Knockback;
    public long Fee;
    public List<ForgeCost> Costs = new();
    /// <summary>Minério principal (lâmina/cabeça) e o segundo (fio aceso).</summary>
    public string Ore => Ores.Count > 0 ? Ores[0] : null;
    public string Ore2 => Ores.Count > 1 ? Ores[1] : Ore;
    public float Dps => Damage * Speed;
    public string Description => Core.Loc.F("{0} dano · {1:0.0} golpes/s · alcance {2:0.0} m", Core.GameState.FormatNumber(Damage), Speed, Reach);
}

/// <summary>Armadura forjada combinando 3 a 5 minérios: diminui o dano das criaturas e aumenta a vida.</summary>
public sealed class ArmorDef
{
    public List<string> Ores = new();
    public string Name;
    public float Reduction;
    public float BonusHp;
    public long Fee;
    public List<ForgeCost> Costs = new();
    public string Ore => Ores.Count > 0 ? Ores[0] : null;
    public string Description => Core.Loc.F("-{0}% de dano das criaturas · +{1} de vida", (int)Math.Round(Reduction * 100), (int)BonusHp);
}

/// <summary>
/// Forja por combinação (pedido do diretor): arma = 2 a 3 minérios quaisquer; armadura = 3 a 5. Cada minério
/// tem uma força que vem do valor dele e da raridade; somar os melhores dá a melhor arma/armadura possível.
/// O nome e as cores saem dos minérios usados.
/// </summary>
public static class WeaponData
{
    public const int WeaponMinOres = 2, WeaponMaxOres = 3, ArmorMinOres = 3, ArmorMaxOres = 5;
    /// <summary>Fragmentos de cada minério escolhido.</summary>
    public const int WeaponOreAmount = 20, ArmorOreAmount = 15;

    public static float RarityMul(Rarity r) => r switch { Rarity.Comum => 1f, Rarity.Incomum => 1.05f, Rarity.Raro => 1.12f, Rarity.Epico => 1.22f, _ => 1.38f };

    /// <summary>Força de um minério na forja (cresce devagar com o valor; raridade dá um extra).</summary>
    public static float OrePower(MaterialDef m) => m == null ? 0f : Mathf.Pow(m.Value, 0.65f) * RarityMul(m.Rarity);

    /// <summary>Minérios que servem na forja: os coletáveis (formações especiais e gigantes não).</summary>
    public static bool Forgeable(MaterialDef m) => m != null && !m.Special;

    static List<string> Clean(IEnumerable<string> ores, int max) =>
        ores.Where(o => Forgeable(GameData.Mat(o))).Distinct().Take(max)
            .OrderByDescending(o => OrePower(GameData.Mat(o))).ToList();

    static string Joined(string piece, List<string> ores)
    {
        var names = ores.Take(2).Select(o => GameData.Mat(o)?.Name ?? o).ToList();
        return names.Count >= 2 ? Core.Loc.F("{0} de {1} e {2}", Core.Loc.T(piece), names[0], names[1]) : Core.Loc.F("{0} de {1}", Core.Loc.T(piece), names.FirstOrDefault() ?? "?");
    }

    static long FeeFor(float power) => (long)Math.Round(400.0 * Math.Pow(power, 1.5) * Economy.UpgradeCost / 100.0) * 100;

    /// <summary>Monta a arma de uma combinação (null se não tiver 2 a 3 minérios válidos).</summary>
    public static WeaponDef Make(WeaponKind kind, IEnumerable<string> ores)
    {
        var list = Clean(ores ?? Array.Empty<string>(), WeaponMaxOres);
        if (list.Count < WeaponMinOres) return null;
        float power = list.Sum(o => OrePower(GameData.Mat(o)));
        bool sword = kind == WeaponKind.Sword;
        var w = new WeaponDef
        {
            Kind = kind, Ores = list,
            Damage = Mathf.Round(power * 3f * (sword ? 1f : 2.6f)),
            Speed = sword ? 2.0f : 0.85f, Reach = sword ? 2.9f : 2.6f, Arc = sword ? 55f : 80f, Knockback = sword ? 2.5f : 7f,
            Fee = FeeFor(power),
            Name = Joined(sword ? "Espada" : "Martelo", list),
        };
        foreach (var o in list) w.Costs.Add(new ForgeCost(o, WeaponOreAmount));
        return w;
    }

    /// <summary>Monta a armadura de uma combinação (null se não tiver 3 a 5 minérios válidos).</summary>
    public static ArmorDef MakeArmor(IEnumerable<string> ores)
    {
        var list = Clean(ores ?? Array.Empty<string>(), ArmorMaxOres);
        if (list.Count < ArmorMinOres) return null;
        float power = list.Sum(o => OrePower(GameData.Mat(o)));
        var a = new ArmorDef
        {
            Ores = list,
            Reduction = Mathf.Min(0.75f, 0.75f * power / (power + 450f)),
            BonusHp = Mathf.Round(25f * Mathf.Log(1f + power / 40f)),
            Fee = FeeFor(power * 0.8f),
            Name = Joined("Couraça", list),
        };
        foreach (var o in list) a.Costs.Add(new ForgeCost(o, ArmorOreAmount));
        return a;
    }

    public static Color BladeColor(WeaponDef w) => w != null && GameData.Mat(w.Ore) is { } m ? m.Color : new Color(0.6f, 0.6f, 0.65f);
    public static Color GlowColor(WeaponDef w) => w != null && GameData.Mat(w.Ore2) is { } m ? m.Glow : new Color(0.8f, 0.8f, 0.9f);

    /// <summary>Saves da v0.27 guardavam o índice de uma receita fixa: vira a combinação equivalente.</summary>
    public static readonly (WeaponKind kind, string[] ores)[] LegacyRecipes =
    {
        (WeaponKind.Sword, new[] { "meteorite", "obsidian" }), (WeaponKind.Hammer, new[] { "meteorite", "gold" }),
        (WeaponKind.Sword, new[] { "diamond", "tanzanite" }), (WeaponKind.Hammer, new[] { "diamond", "alexandrite" }),
        (WeaponKind.Sword, new[] { "runestone", "lumenite" }), (WeaponKind.Hammer, new[] { "runestone", "ancientglass" }),
    };
}
