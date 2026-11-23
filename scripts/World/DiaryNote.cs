using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Diário de outro escravo numa câmara secreta: caixote com o caderno aberto, vela acesa, picareta velha e
/// um grilhão partido. Interagir abre a leitura (e conta na coleção).
/// </summary>
public partial class DiaryNote : StationBase
{
    public string DiaryId;
    OmniLight3D _candle;
    MeshInstance3D _flame;

    public override void _Ready()
    {
        SetupBody(new Vector3(0.9f, 0.75f, 0.7f));
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.28f, 0.17f), Roughness = 0.9f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.16f, 0.1f), Roughness = 0.9f };
        var paper = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.86f, 0.7f), Roughness = 0.95f };
        var cover = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.18f, 0.12f), Roughness = 0.8f };
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.38f, 0.36f), Roughness = 0.6f, Metallic = 0.3f };
        var wax = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.92f, 0.82f), Roughness = 0.7f };
        // caixote
        AddMesh(new BoxMesh { Size = new Vector3(0.85f, 0.6f, 0.65f) }, wood, new Vector3(0, 0.3f, 0));
        foreach (float y in new[] { 0.1f, 0.5f })
            AddMesh(new BoxMesh { Size = new Vector3(0.87f, 0.06f, 0.67f) }, dark, new Vector3(0, y, 0));
        // caderno aberto
        var book = new Node3D { Position = new Vector3(-0.05f, 0.61f, 0), Rotation = new Vector3(0, 0.25f, 0) };
        AddChild(book);
        foreach (int s in new[] { -1, 1 })
        {
            book.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.012f, 0.27f) }, MaterialOverride = cover, Position = new Vector3(s * 0.1f, 0, 0), Rotation = new Vector3(0, 0, -s * 0.08f) });
            book.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.02f, 0.25f) }, MaterialOverride = paper, Position = new Vector3(s * 0.095f, 0.012f, 0), Rotation = new Vector3(0, 0, -s * 0.08f) });
            for (int l = 0; l < 6; l++)
                book.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.13f, 0.002f, 0.008f) }, MaterialOverride = dark, Position = new Vector3(s * 0.095f, 0.024f, -0.09f + l * 0.035f), Rotation = new Vector3(0, 0, -s * 0.08f) });
        }
        // vela acesa (luz quente que denuncia o esconderijo de longe)
        AddMesh(new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.04f, Height = 0.16f, RadialSegments = 10 }, wax, new Vector3(0.3f, 0.68f, 0.18f));
        AddMesh(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.08f, Height = 0.02f, RadialSegments = 10 }, iron, new Vector3(0.3f, 0.61f, 0.18f));
        _flame = AddMesh(new SphereMesh { Radius = 0.025f, Height = 0.07f, RadialSegments = 8, Rings = 4 },
            new StandardMaterial3D { AlbedoColor = new Color(1f, 0.8f, 0.4f), EmissionEnabled = true, Emission = new Color(1f, 0.65f, 0.25f), EmissionEnergyMultiplier = 4f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            new Vector3(0.3f, 0.79f, 0.18f));
        _candle = new OmniLight3D { LightColor = new Color(1f, 0.68f, 0.35f), LightEnergy = 1.6f, OmniRange = 5.5f, Position = new Vector3(0.3f, 0.95f, 0.18f) };
        AddChild(_candle);
        // picareta velha encostada e grilhão partido no chão
        var pick = new Node3D { Position = new Vector3(0.52f, 0, -0.1f), Rotation = new Vector3(0, 0, 0.35f) };
        AddChild(pick);
        pick.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.025f, Height = 0.8f, RadialSegments = 6 }, MaterialOverride = dark, Position = new Vector3(0, 0.4f, 0) });
        pick.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.05f, 0.05f) }, MaterialOverride = iron, Position = new Vector3(0, 0.8f, 0), Rotation = new Vector3(0, 0, 0.1f) });
        for (int i = 0; i < 4; i++)
            AddMesh(new TorusMesh { InnerRadius = 0.025f, OuterRadius = 0.045f, Rings = 10, RingSegments = 6 }, iron, new Vector3(-0.55f + i * 0.07f, 0.03f, 0.3f - i * 0.03f)).Rotation = new Vector3(i % 2 == 0 ? 0 : Mathf.Pi / 2, i * 0.4f, 0);
        AddMesh(new TorusMesh { InnerRadius = 0.06f, OuterRadius = 0.09f, Rings = 12, RingSegments = 6 }, iron, new Vector3(-0.72f, 0.05f, 0.38f)).Rotation = new Vector3(0.2f, 0, 0.3f);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (_candle == null || !IsVisibleInTree()) return;
        float f = 1f + 0.18f * Mathf.Sin(Time * 11f) * Mathf.Sin(Time * 3.7f);
        _candle.LightEnergy = 1.6f * f;
        _flame.Scale = new Vector3(1f, f, 1f);
    }

    public override string GetPrompt()
    {
        bool read = GameState.Instance?.Data.Diaries.Contains(DiaryId) ?? false;
        return read ? Loc.F("{0} Reler o diário", InputHints.Interact) : Loc.F("{0} Ler o diário abandonado", InputHints.Interact);
    }

    public override void Interact(PlayerController player) => UiBus.RequestDiary(DiaryId);
}
