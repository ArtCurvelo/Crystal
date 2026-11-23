using System.Collections.Generic;

namespace CrystalExcavator.Data;

public enum EnemyKind { Crawler, Sentinel }

/// <summary>Inimigo das Profundezas Proibidas.</summary>
public sealed class EnemyDef
{
    public string Id;
    string _name;
    public string Name { get => Core.Loc.T(_name); set => _name = value; }
    public string NamePt => _name;
    public EnemyKind Kind;
    public float Hp, Damage, Speed;
    /// <summary>Distância do golpe; preparo (aviso) antes do golpe; espera entre golpes.</summary>
    public float Range, Windup, Cooldown;
    /// <summary>Raio do golpe em área (0 = só quem estiver na frente).</summary>
    public float Area;
    public float Aggro = 14f;
    public float Scale = 1f;
    /// <summary>Fragmentos que larga ao morrer.</summary>
    public string Drop;
    public int DropMin, DropMax;
    public string RareDrop;
    public float RareChance;
    public string Region;
}

public static class EnemyData
{
    public static readonly List<EnemyDef> All = new();

    static EnemyData()
    {
        All.Add(new EnemyDef
        {
            Id = "crawler", Name = "Rastejante de Cristal", Kind = EnemyKind.Crawler, Region = "forgotten",
            Hp = 900, Damage = 9, Speed = 4.2f, Range = 1.6f, Windup = 0.35f, Cooldown = 1.3f, Aggro = 10f,
            Drop = "lumenite", DropMin = 1, DropMax = 3, RareDrop = "runestone", RareChance = 0.08f,
        });
        All.Add(new EnemyDef
        {
            Id = "sentinel", Name = "Sentinela de Lúmen", Kind = EnemyKind.Sentinel, Region = "forgotten",
            Hp = 5200, Damage = 24, Speed = 1.9f, Range = 2.8f, Windup = 0.9f, Cooldown = 2.6f, Area = 3.0f, Aggro = 12f, Scale = 1.5f,
            Drop = "ancientglass", DropMin = 3, DropMax = 5, RareDrop = "lumenheart", RareChance = 0.06f,
        });
    }

    public static EnemyDef Of(string id) => All.Find(e => e.Id == id);
}
