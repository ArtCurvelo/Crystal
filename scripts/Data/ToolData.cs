using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.Data;

public enum ToolKind { Pickaxe, Drill, Laser }

/// <summary>
/// Ferramenta de mineração comprada pronta na mesa de melhorias (Terminal). Cada uma tem dano, golpes por
/// segundo e durabilidade (golpes até quebrar). Quebrou: volta ao Terminal e compra outra (a mesma ou melhor);
/// até lá minera com as mãos. As melhores só aparecem quando a região do minério delas é liberada.
/// </summary>
public sealed class ToolDef
{
    public int Index;
    public string Id;
    string _name, _desc;
    public string Name { get => Core.Loc.T(_name); set => _name = value; }
    public string NamePt => _name;
    public string Description { get => Core.Loc.T(_desc); set => _desc = value; }
    public ToolKind Kind;
    /// <summary>Região que precisa estar liberada para comprar (onde se acha o minério dela).</summary>
    public int Region;
    public float Damage;
    public float Swings;
    public int Durability;
    public long Cost;
    /// <summary>Minério do jogo de que a cabeça/broca é feita (cor e brilho vêm dele). Null = madeira.</summary>
    public string Ore;
    public float DamagePerSecond => Damage * Swings;
}

public static class ToolData
{
    /// <summary>Sem ferramenta (quebrou): minerar com as mãos.</summary>
    public const float HandsDamage = 1f, HandsSwings = 1.2f;
    /// <summary>Laser: cada disparo que acerta gasta 1 célula de energia (comprada em pacotes no Terminal).</summary>
    public const int AmmoPack = 200;
    public static readonly long AmmoPackCost = (long)(120_000 * Economy.ToolCost);
    /// <summary>Células que já vêm com um laser novo.</summary>
    public const int AmmoWithLaser = 100;

    public static readonly List<ToolDef> All = new();

    static ToolData()
    {
        void T(string id, string name, ToolKind k, int region, float dmg, float swings, int dur, long cost, string ore, string desc) =>
            All.Add(new ToolDef { Index = All.Count, Id = id, Name = name, Kind = k, Region = region, Damage = dmg, Swings = swings, Durability = dur, Cost = (long)System.Math.Round(cost * Economy.ToolCost), Ore = ore, Description = desc });
        T("wood", "Picareta de Madeira", ToolKind.Pickaxe, 0, 3, 1.6f, 250, 15, null, "Lasca a rocha mais mole. Quebra rápido.");
        T("copper", "Picareta de Cobre", ToolKind.Pickaxe, 0, 8, 1.9f, 350, 60, "copper", "Cabeça de cobre batido: o dobro da madeira.");
        T("hematite", "Picareta de Hematita", ToolKind.Pickaxe, 0, 18, 2.2f, 450, 200, "hematite", "Ferro avermelhado, pesado e firme.");
        T("amethyst", "Picareta de Ametista", ToolKind.Pickaxe, 0, 40, 2.5f, 550, 800, "amethyst", "Ponta de cristal que racha os depósitos fundos da primeira caverna.");
        T("silver", "Picareta de Prata", ToolKind.Pickaxe, 1, 90, 2.8f, 650, 4500, "silver", "Leve e afiada para o gelo das Profundezas.");
        T("sapphire", "Picareta de Safira", ToolKind.Pickaxe, 1, 200, 3.1f, 750, 16000, "sapphire", "Safira lapidada: corta cristais resistentes.");
        T("obsidian", "Picareta de Obsidiana", ToolKind.Pickaxe, 2, 450, 3.4f, 850, 60000, "obsidian", "Vidro vulcânico: aguenta o calor do Núcleo.");
        T("ruby", "Picareta de Rubi", ToolKind.Pickaxe, 2, 1000, 3.7f, 950, 220000, "ruby", "A melhor picareta: rubi forjado na lava.");
        T("titanium", "Broca de Titânio", ToolKind.Drill, 3, 2200, 4.2f, 1100, 850000, "titanium", "Broca giratória para a rocha do abismo.");
        T("peridot", "Broca de Peridoto", ToolKind.Drill, 4, 4500, 4.8f, 1250, 3200000, "peridot", "Broca com ponta de peridoto, rápida e precisa.");
        T("diamond_laser", "Cortador Laser de Diamante", ToolKind.Laser, 5, 9000, 5.5f, 1500, 7500000, "diamond", "Feixe concentrado por um diamante: derrete qualquer cristal.");
        T("prism_lance", "Lança Prismática", ToolKind.Laser, 5, 18000, 6.5f, 1800, 18000000, "eternaldiamond", "O feixe definitivo, guiado por um diamante eterno.");
    }

    public static ToolDef Of(int i) => i >= 0 && i < All.Count ? All[i] : All[0];

    /// <summary>Cor da cabeça da ferramenta (do minério; madeira = marrom).</summary>
    public static Color HeadColor(ToolDef t) => t.Ore != null && GameData.Mat(t.Ore) is { } m ? m.Color : new Color(0.55f, 0.38f, 0.22f);
    public static Color GlowColor(ToolDef t) => t.Ore != null && GameData.Mat(t.Ore) is { } m ? m.Glow : new Color(0.9f, 0.7f, 0.4f);
}
