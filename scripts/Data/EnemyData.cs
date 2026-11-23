using System.Collections.Generic;

namespace CrystalExcavator.Data;

/// <summary>Aparência (e o jeito de andar) do inimigo.</summary>
public enum EnemyKind { Crawler, Sentinel, Slag, Ember, Root, Spitter, Chained, Overseer, Blob, Eye, Corvo }

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
    /// <summary>Atira projéteis (velocidade em m/s) em vez de bater de perto.</summary>
    public float Projectile;
    /// <summary>Não sai do lugar (raízes).</summary>
    public bool Stationary;
    /// <summary>Explode no golpe (some depois).</summary>
    public bool Exploder;
    /// <summary>Flutua (ignora a gravidade e paira na altura do peito).</summary>
    public bool Flying;
    /// <summary>Ao morrer, vira N inimigos menores deste tipo.</summary>
    public string SplitInto;
    public int SplitCount;
    /// <summary>Guardião da área: muito mais forte; derrotá-lo abre a passagem para a próxima Profundeza.</summary>
    public bool Guardian;
    /// <summary>Chefão final (Mestre Corvo): fases, rajadas e crias; não renasce depois de derrotado.</summary>
    public bool Boss;
    /// <summary>Fragmentos que larga ao morrer.</summary>
    public string Drop;
    public int DropMin, DropMax;
    public string RareDrop;
    public float RareChance;
    public string Region;
}

/// <summary>
/// Inimigos por Profundeza (vida e dano crescem ~2,5x por área) e o Guardião de cada uma (elite que guarda a
/// passagem para a próxima). Na 6ª área fica o Mestre Corvo (próxima etapa).
/// </summary>
public static class EnemyData
{
    public static readonly List<EnemyDef> All = new();

    static void E(EnemyDef d) => All.Add(d);

    static EnemyDef Guardian(string id, string name, EnemyDef from, float hpMul, string drop, string rare)
    {
        var g = new EnemyDef
        {
            Id = id, Name = name, Kind = from.Kind, Region = from.Region, Guardian = true,
            Hp = from.Hp * hpMul, Damage = from.Damage * 1.6f, Speed = from.Speed * 0.85f, Range = from.Range * 1.3f, Windup = from.Windup * 1.1f,
            Cooldown = from.Cooldown, Area = from.Area > 0 ? from.Area * 1.4f : 0, Aggro = 16f, Scale = from.Scale * 1.8f,
            Projectile = from.Projectile, Stationary = from.Stationary, Flying = from.Flying,
            Drop = drop, DropMin = 8, DropMax = 12, RareDrop = rare, RareChance = 1f,
        };
        All.Add(g);
        return g;
    }

    static EnemyData()
    {
        // 1 · Galerias Esquecidas
        var crawler = new EnemyDef
        {
            Id = "crawler", Name = "Rastejante de Cristal", Kind = EnemyKind.Crawler, Region = "forgotten",
            Hp = 900, Damage = 9, Speed = 4.2f, Range = 1.6f, Windup = 0.35f, Cooldown = 1.3f, Aggro = 10f,
            Drop = "lumenite", DropMin = 1, DropMax = 3, RareDrop = "runestone", RareChance = 0.08f,
        };
        E(crawler);
        var sentinel = new EnemyDef
        {
            Id = "sentinel", Name = "Sentinela de Lúmen", Kind = EnemyKind.Sentinel, Region = "forgotten",
            Hp = 5200, Damage = 24, Speed = 1.9f, Range = 2.8f, Windup = 0.9f, Cooldown = 2.6f, Area = 3.0f, Aggro = 12f, Scale = 1.5f,
            Drop = "ancientglass", DropMin = 3, DropMax = 5, RareDrop = "lumenheart", RareChance = 0.06f,
        };
        E(sentinel);
        Guardian("guardian_forgotten", "Sentinela Anciã", sentinel, 6f, "runestone", "lumenheart");

        // 2 · Fornalha Abandonada
        var slag = new EnemyDef
        {
            Id = "slag", Name = "Golem de Escória", Kind = EnemyKind.Slag, Region = "furnace",
            Hp = 15000, Damage = 40, Speed = 1.8f, Range = 2.8f, Windup = 0.85f, Cooldown = 2.4f, Area = 3.2f, Aggro = 12f, Scale = 1.6f,
            Drop = "slagiron", DropMin = 3, DropMax = 5, RareDrop = "forgecore", RareChance = 0.1f,
        };
        E(slag);
        var ember = new EnemyDef
        {
            Id = "ember", Name = "Brasa Viva", Kind = EnemyKind.Ember, Region = "furnace", Flying = true, Exploder = true,
            Hp = 4000, Damage = 34, Speed = 5.0f, Range = 1.8f, Windup = 0.7f, Cooldown = 1f, Area = 2.6f, Aggro = 13f,
            Drop = "emberstone", DropMin = 1, DropMax = 2, RareDrop = "phoenixite", RareChance = 0.03f,
        };
        E(ember);
        Guardian("guardian_furnace", "Fornalha Viva", slag, 5f, "forgecore", "phoenixite");

        // 3 · Raízes do Coração
        var root = new EnemyDef
        {
            Id = "root", Name = "Raiz Viva", Kind = EnemyKind.Root, Region = "roots", Stationary = true,
            Hp = 36000, Damage = 55, Speed = 0f, Range = 3.6f, Windup = 0.7f, Cooldown = 1.8f, Area = 3.6f, Aggro = 8f, Scale = 1.3f,
            Drop = "rootamber", DropMin = 3, DropMax = 6, RareDrop = "veinruby", RareChance = 0.1f,
        };
        E(root);
        var spitter = new EnemyDef
        {
            Id = "spitter", Name = "Espinhador", Kind = EnemyKind.Spitter, Region = "roots",
            Hp = 15000, Damage = 26, Speed = 1.6f, Range = 13f, Windup = 0.8f, Cooldown = 2.2f, Aggro = 16f, Projectile = 13f,
            Drop = "heartwood", DropMin = 2, DropMax = 4, RareDrop = "seedglass", RareChance = 0.03f,
        };
        E(spitter);
        Guardian("guardian_roots", "Raiz-Mãe", root, 5f, "veinruby", "seedglass");

        // 4 · Poço dos Acorrentados
        var chained = new EnemyDef
        {
            Id = "chained", Name = "Acorrentado", Kind = EnemyKind.Chained, Region = "chained",
            Hp = 40000, Damage = 34, Speed = 3.4f, Range = 1.9f, Windup = 0.45f, Cooldown = 1.2f, Aggro = 12f,
            Drop = "chainsteel", DropMin = 2, DropMax = 4, RareDrop = "bloodgarnet", RareChance = 0.08f,
        };
        E(chained);
        var overseer = new EnemyDef
        {
            Id = "overseer", Name = "Capataz Corrompido", Kind = EnemyKind.Overseer, Region = "chained",
            Hp = 95000, Damage = 50, Speed = 2.2f, Range = 4.5f, Windup = 0.75f, Cooldown = 2.0f, Aggro = 14f, Scale = 1.25f,
            Drop = "tearopal", DropMin = 3, DropMax = 5, RareDrop = "freedomstone", RareChance = 0.04f,
        };
        E(overseer);
        Guardian("guardian_chained", "Primeiro Capataz", overseer, 5f, "bloodgarnet", "freedomstone");

        // 5 · Ninho de Lúmen Bruto
        var blobSmall = new EnemyDef
        {
            Id = "blob_small", Name = "Gota de Lúmen", Kind = EnemyKind.Blob, Region = "nest",
            Hp = 30000, Damage = 30, Speed = 4.0f, Range = 1.5f, Windup = 0.4f, Cooldown = 1.1f, Aggro = 14f, Scale = 0.6f,
            Drop = "rawlumen", DropMin = 1, DropMax = 2,
        };
        E(blobSmall);
        var blob = new EnemyDef
        {
            Id = "blob", Name = "Lúmen Vivo", Kind = EnemyKind.Blob, Region = "nest", SplitInto = "blob_small", SplitCount = 2,
            Hp = 110000, Damage = 55, Speed = 2.6f, Range = 2.2f, Windup = 0.6f, Cooldown = 1.6f, Area = 2.4f, Aggro = 12f, Scale = 1.2f,
            Drop = "rawlumen", DropMin = 3, DropMax = 5, RareDrop = "eyecrystal", RareChance = 0.08f,
        };
        E(blob);
        var eye = new EnemyDef
        {
            Id = "eye", Name = "Vigia do Ninho", Kind = EnemyKind.Eye, Region = "nest", Flying = true,
            Hp = 70000, Damage = 40, Speed = 2.4f, Range = 15f, Windup = 0.9f, Cooldown = 2.0f, Aggro = 18f, Projectile = 16f,
            Drop = "nestpearl", DropMin = 2, DropMax = 4, RareDrop = "primelumen", RareChance = 0.03f,
        };
        E(eye);
        Guardian("guardian_nest", "Mãe do Ninho", blob, 5f, "eyecrystal", "primelumen");

        // 6 · Trono de Corvo
        E(new EnemyDef
        {
            Id = "servant", Name = "Servo do Corvo", Kind = EnemyKind.Chained, Region = "throne",
            Hp = 160000, Damage = 50, Speed = 3.6f, Range = 2.0f, Windup = 0.4f, Cooldown = 1.1f, Aggro = 13f, Scale = 1.15f,
            Drop = "crowsteel", DropMin = 2, DropMax = 4, RareDrop = "bloodlumen", RareChance = 0.12f,
        });
        E(new EnemyDef
        {
            Id = "crowling", Name = "Cria de Lúmen", Kind = EnemyKind.Crawler, Region = "throne",
            Hp = 70000, Damage = 34, Speed = 5.0f, Range = 1.6f, Windup = 0.3f, Cooldown = 1.0f, Aggro = 14f, Scale = 1.1f,
            Drop = "crowsteel", DropMin = 1, DropMax = 2,
        });
        E(new EnemyDef
        {
            Id = "corvo", Name = "Mestre Corvo", Kind = EnemyKind.Corvo, Region = "throne", Boss = true,
            Hp = 4_500_000, Damage = 70, Speed = 2.6f, Range = 3.4f, Windup = 0.9f, Cooldown = 1.6f, Area = 4.5f, Aggro = 30f, Scale = 2.4f,
            Projectile = 0f, Drop = "anvilstone", DropMin = 6, DropMax = 8,
        });
    }

    public static EnemyDef Of(string id) => All.Find(e => e.Id == id);
    public static EnemyDef GuardianOf(string region) => All.Find(e => e.Guardian && e.Region == region);
}
