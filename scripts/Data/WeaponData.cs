using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.Data;

public enum WeaponKind { Sword, Hammer }

/// <summary>Arma forjada na vila para lutar nas Profundezas Proibidas (a ferramenta continua só para minerar).</summary>
public sealed class WeaponDef
{
    public int Index;
    public string Id;
    string _name, _desc;
    public string Name { get => Core.Loc.T(_name); set => _name = value; }
    public string NamePt => _name;
    public string Description { get => Core.Loc.T(_desc); set => _desc = value; }
    public WeaponKind Kind;
    public int Tier;
    public float Damage;
    /// <summary>Golpes por segundo.</summary>
    public float Speed;
    /// <summary>Alcance (m) e abertura do golpe (graus, para os lados).</summary>
    public float Reach, Arc;
    public float Knockback;
    public long Fee;
    public List<ForgeCost> Costs = new();
    /// <summary>Minério da lâmina/cabeça (cor e brilho).</summary>
    public string Ore;
    /// <summary>Região (índice) que precisa estar liberada para forjar.</summary>
    public int Region;
    public float Dps => Damage * Speed;
}

/// <summary>
/// Armas em 6 níveis: espada (rápida, arco largo) e martelo (lento, forte, empurra e acerta em volta).
/// Os 3 primeiros níveis usam minérios da mina; os seguintes, das Profundezas.
/// </summary>
public static class WeaponData
{
    public static readonly List<WeaponDef> All = new();

    static void W(string id, string name, WeaponKind k, int tier, float dmg, string ore, string gem, int a1, int a2, long fee, int region, string desc)
    {
        bool sword = k == WeaponKind.Sword;
        var w = new WeaponDef
        {
            Index = All.Count, Id = id, Name = name, Kind = k, Tier = tier, Description = desc, Ore = ore, Region = region,
            Damage = sword ? dmg : dmg * 2.6f, Speed = sword ? 2.0f : 0.85f, Reach = sword ? 2.9f : 2.6f, Arc = sword ? 55f : 80f,
            Knockback = sword ? 2.5f : 7f, Fee = (long)System.Math.Round(fee * Economy.UpgradeCost),
        };
        w.Costs.Add(new ForgeCost(ore, a1));
        w.Costs.Add(new ForgeCost(gem, a2));
        All.Add(w);
    }

    static WeaponData()
    {
        W("sword_meteor", "Espada de Ferro Meteórico", WeaponKind.Sword, 0, 220, "meteorite", "obsidian", 25, 20, 400_000, 2, "Ferro que caiu do céu, batido na forja da vila. Leve e rápida.");
        W("hammer_meteor", "Martelo de Ferro Meteórico", WeaponKind.Hammer, 0, 220, "meteorite", "gold", 30, 20, 450_000, 2, "Cabeça maciça de meteorito: lento, mas derruba qualquer coisa.");
        W("sword_diamond", "Espada de Diamante", WeaponKind.Sword, 1, 600, "diamond", "tanzanite", 20, 25, 3_000_000, 5, "Fio de diamante: corta o Lúmen como se fosse cera.");
        W("hammer_diamond", "Martelo de Diamante", WeaponKind.Hammer, 1, 600, "diamond", "alexandrite", 24, 20, 3_400_000, 5, "Diamante sobre alexandrita. Cada golpe faz a caverna tremer.");
        W("sword_runic", "Espada Rúnica", WeaponKind.Sword, 2, 1600, "runestone", "lumenite", 25, 40, 12_000_000, 6, "Runas do Povo Lúmen acendem na lâmina quando há perigo perto.");
        W("hammer_runic", "Martelo Rúnico", WeaponKind.Hammer, 2, 1600, "runestone", "ancientglass", 30, 30, 13_000_000, 6, "Um martelo que o Povo Lúmen usava para acordar a pedra.");
    }

    public static WeaponDef Of(int i) => i >= 0 && i < All.Count ? All[i] : null;
    public static Color BladeColor(WeaponDef w) => w != null && GameData.Mat(w.Ore) is { } m ? m.Color : new Color(0.6f, 0.6f, 0.65f);
    public static Color GlowColor(WeaponDef w) => w != null && GameData.Mat(w.Ore) is { } m ? m.Glow : new Color(0.8f, 0.8f, 0.9f);
}
