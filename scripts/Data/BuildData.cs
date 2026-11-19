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
    /// <summary>Habilidade (ramo Construção) que libera este item; null = sempre disponível.</summary>
    public string Skill;
}

public static class BuildIds
{
    public const string Light = "light", Hopper = "hopper", Drone = "drone", Rope = "rope", Belt = "belt", Robot = "robot";
}

public static class BuildData
{
    /// <summary>Custo da esteira por metro (mais a entrada).</summary>
    public const long BeltCostPerMeter = 12;
    /// <summary>Distância máxima do funil até a esteira em que ele despeja.</summary>
    public const float HopperBeltDist = 2.6f;
    /// <summary>Distância para a ponta da esteira se ligar à Estação de Venda ou a outra esteira.</summary>
    public const float StationSnap = 4f, BeltSnap = 1.8f;
    /// <summary>Esteira mais curta permitida (m).</summary>
    public const float BeltMinLength = 2f;
    /// <summary>Velocidade da esteira (m/s) e intervalo entre fragmentos que saem do funil (s).</summary>
    public const float BeltSpeed = 5f, BeltInterval = 0.22f;
    /// <summary>Raio em que o funil puxa fragmentos soltos.</summary>
    public const float HopperPullRadius = 5f;
    /// <summary>Devolução ao desmontar.</summary>
    public const float RefundFraction = 0.5f;
    /// <summary>Área do drone (raio em volta da base) e quantos fragmentos ele carrega.</summary>
    public const float DroneRange = 14f;
    public const int DroneCargo = 8;
    /// <summary>Robô mineiro: área (raio em volta da base), carga, velocidade e força em relação ao goblin da região.</summary>
    public const float RobotRange = 10f, RobotSpeed = 1.6f, RobotDpsFactor = 0.5f;
    public const int RobotCargo = 12;
    /// <summary>Quanto a base (drone/robô) guarda quando não há funil nem esteira na área.</summary>
    public const int DockStorage = 60;

    public static readonly List<BuildDef> All = new()
    {
        new BuildDef { Id = BuildIds.Rope, Name = "Corda", Description = "Mire na beira de um buraco para descer, ou numa borda acima para subir. Depois é só usar (E).", Cost = 300, Max = 40 },
        new BuildDef { Id = BuildIds.Light, Name = "Poste de luz", Description = "Ilumina a área ao redor.", Cost = 150, Max = 120 },
        new BuildDef { Id = BuildIds.Belt, Name = "Esteira", Description = "Marque o início e arraste até a Estação de Venda ou até outra esteira. Deposite fragmentos na entrada.", Cost = 200, Max = 24, Skill = Data.SkillIds.BuildBelt },
        new BuildDef { Id = BuildIds.Hopper, Name = "Funil", Description = "Fica ao lado de uma esteira: puxa fragmentos soltos por perto e despeja na esteira.", Cost = 2500, Max = 12, Skill = Data.SkillIds.BuildHopper },
        new BuildDef { Id = BuildIds.Drone, Name = "Drone de área", Description = "Base com drone: recolhe fragmentos soltos dentro da área e leva ao funil ou à esteira mais perto.", Cost = 6000, Max = 4, Skill = Data.SkillIds.BuildDrone },
        new BuildDef { Id = BuildIds.Robot, Name = "Robô mineiro", Description = "Minera sozinho os depósitos dentro da área (mais devagar que um goblin) e leva ao funil ou à esteira.", Cost = 25000, Max = 6, Skill = Data.SkillIds.BuildRobot },
    };

    public static BuildDef Of(string id) => All.FirstOrDefault(b => b.Id == id);
}
