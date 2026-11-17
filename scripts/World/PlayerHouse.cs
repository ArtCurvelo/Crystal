using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Casa do escavador, no topo do elevador da mina: um cômodo onde se anda de verdade.
/// Cama (dormir → novo dia), painel (melhorias da casa e habilidades) e elevador para descer à mina.
/// Os móveis mudam conforme as melhorias compradas (HouseDiorama em modo interior).
/// </summary>
public partial class PlayerHouse : Node3D
{
    public static readonly Vector3 Origin = new(0, 0, 70f);
    public HouseDiorama Room;
    public HouseBed Bed;
    public HouseTerminal Panel;
    public MineElevator ElevatorDown;

    /// <summary>Onde o jogador aparece ao subir da mina (na frente do elevador).</summary>
    public Vector3 ArrivePoint => Origin + new Vector3(1.7f, 0.1f, 0.3f);
    /// <summary>Onde o jogador acorda (ao lado da cama, olhando para o elevador).</summary>
    public Vector3 WakePoint => Origin + new Vector3(-0.6f, 0.1f, -0.9f);

    public static bool Contains(Vector3 p) =>
        Mathf.Abs(p.X - Origin.X) < 3.5f && Mathf.Abs(p.Z - Origin.Z) < 3f && p.Y < 4f && p.Y > -2f;

    public override void _Ready()
    {
        Position = Origin;
        Room = new HouseDiorama { Name = "Room", Interior = true };
        AddChild(Room);
        Bed = new HouseBed { Name = "Bed", Position = new Vector3(-1.9f, 0, -1.3f) };
        AddChild(Bed);
        Panel = new HouseTerminal { Name = "HousePanel", Position = new Vector3(-1.7f, 0, 2.05f), Rotation = new Vector3(0, Mathf.Pi, 0) };
        AddChild(Panel);
        ElevatorDown = new MineElevator { Name = "ElevatorDown", GoesDown = true, Position = new Vector3(1.7f, 0, 1.78f), Rotation = new Vector3(0, Mathf.Pi, 0), Scale = Vector3.One * 0.72f };
        AddChild(ElevatorDown);
        var warm = new OmniLight3D { LightColor = new Color(1f, 0.8f, 0.55f), LightEnergy = 0.8f, OmniRange = 6f, Position = new Vector3(-1.5f, 2.4f, -0.5f) };
        warm.SetMeta(HouseDiorama.AlwaysOnMeta, true);
        AddChild(warm);
    }
}

/// <summary>Cama: dormir encerra a noite e começa o próximo dia.</summary>
public partial class HouseBed : StationBase
{
    public override void _Ready()
    {
        // corpo da estação cobre a cama (serve de colisão); o visual vem da maquete
        SetupBody(new Vector3(1.2f, 1.0f, 2.1f));
    }

    public override string GetPrompt() => GameState.Instance.DayOver
        ? $"{InputHints.Interact} Dormir (começar o Dia {GameState.Instance.Data.Day + 1})"
        : "Você já descansou. Desça pelo elevador para trabalhar.";

    public override void Interact(PlayerController player) => UiBus.RequestSleep();
}

/// <summary>Painel da casa: abre melhorias da casa e árvore de habilidades.</summary>
public partial class HouseTerminal : StationBase
{
    public override void _Ready()
    {
        SetupBody(new Vector3(1.1f, 1.5f, 0.6f));
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.2f, 0.12f), Roughness = 0.8f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.1f, 0.13f), Roughness = 0.7f };
        AddMesh(new BoxMesh { Size = new Vector3(1.1f, 0.06f, 0.6f) }, wood, new Vector3(0, 0.8f, 0));
        foreach (float x in new[] { -0.5f, 0.5f })
            AddMesh(new BoxMesh { Size = new Vector3(0.06f, 0.8f, 0.55f) }, wood, new Vector3(x, 0.4f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(0.75f, 0.5f, 0.05f) }, dark, new Vector3(0, 1.15f, -0.15f));
        var gold = new Color(1f, 0.79f, 0.34f);
        AddMesh(new BoxMesh { Size = new Vector3(0.68f, 0.43f, 0.01f) }, MeshFactory.Emissive(new Color(0.05f, 0.08f, 0.12f), new Color(0.15f, 0.3f, 0.4f), 1.2f), new Vector3(0, 1.15f, -0.12f));
        AddMesh(new BoxMesh { Size = new Vector3(0.4f, 0.04f, 0.01f) }, MeshFactory.Emissive(gold, gold, 2f), new Vector3(0, 1.25f, -0.11f));
        AddMesh(new BoxMesh { Size = new Vector3(0.3f, 0.04f, 0.01f) }, MeshFactory.Emissive(UI.UiTheme.Cyan, UI.UiTheme.Cyan, 2f), new Vector3(-0.05f, 1.15f, -0.11f));
        AddMesh(new BoxMesh { Size = new Vector3(0.12f, 0.2f, 0.12f) }, dark, new Vector3(0, 0.93f, -0.15f));
        AddMesh(new BoxMesh { Size = new Vector3(0.45f, 0.02f, 0.16f) }, dark, new Vector3(0, 0.84f, 0.1f));
        Label = MakeLabel("PAINEL DA CASA", gold, 1.75f, 48);
    }

    public override string GetPrompt() => $"{InputHints.Interact} Painel: melhorias da casa e habilidades";
    public override void Interact(PlayerController player) => UiBus.RequestHomePanel();
}
