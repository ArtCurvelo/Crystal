using System.Collections.Generic;
using System.Linq;

namespace CrystalExcavator.Data;

/// <summary>Construções que o jogador coloca nas cavernas (modo construção).</summary>
public sealed class BuildDef
{
    public string Id;
    string _name, _desc;
    public string Name { get => Core.Loc.T(_name); set => _name = value; }
    public string Description { get => Core.Loc.T(_desc); set => _desc = value; }
    public long Cost;
    /// <summary>Quantas podem existir ao mesmo tempo.</summary>
    public int Max;
}

public static class BuildIds
{
    public const string Light = "light", Hopper = "hopper", Drone = "drone";
}

public static class BuildData
{
    /// <summary>Custo da esteira por metro (cobrado junto com o funil).</summary>
    public const long BeltCostPerMeter = 12;
    /// <summary>Velocidade da esteira (m/s) e intervalo entre fragmentos que saem do funil (s).</summary>
    public const float BeltSpeed = 5f, BeltInterval = 0.22f;
    /// <summary>Raio em que o funil puxa fragmentos soltos.</summary>
    public const float HopperPullRadius = 5f;
    /// <summary>Devolução ao desmontar.</summary>
    public const float RefundFraction = 0.5f;
    /// <summary>Alcance do drone em volta do jogador e quantos fragmentos ele carrega.</summary>
    public const float DroneRange = 18f;
    public const int DroneCargo = 8;

    public static readonly List<BuildDef> All = new()
    {
        new BuildDef { Id = BuildIds.Light, Name = "Poste de luz", Description = "Ilumina a área ao redor.", Cost = 150, Max = 120 },
        new BuildDef { Id = BuildIds.Hopper, Name = "Funil + esteira", Description = "Puxa fragmentos soltos e leva tudo pela esteira até a Estação de Venda, que vende sozinha.", Cost = 2500, Max = 8 },
        new BuildDef { Id = BuildIds.Drone, Name = "Drone coletor", Description = "Segue você, recolhe fragmentos espalhados e leva ao funil quando a mochila enche.", Cost = 6000, Max = 1 },
    };

    public static BuildDef Of(string id) => All.FirstOrDefault(b => b.Id == id);
}
