using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Portão do Povo Lúmen no ponto mais fundo da Catedral de Cristal, lacrado pela Guilda (correntes + selo do
/// Mestre). Ato 2: tirar o molde do selo e depois abrir com a chave falsa. Aberto, mostra a escuridão das
/// Profundezas Proibidas.
/// </summary>
public partial class SealedGate : StationBase
{
    Node3D _doorL, _doorR, _chains, _seal;
    OmniLight3D _glow, _deep;
    MeshInstance3D _void;
    float _open, _openTarget;

    public override void _Ready()
    {
        SetupBody(new Vector3(6.4f, 7.2f, 1.6f));
        var stone = MeshFactory.RockMaterial(new Color(0.55f, 0.52f, 0.6f));
        var carved = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.4f, 0.5f), Roughness = 0.85f };
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.29f, 0.3f), Roughness = 0.5f, Metallic = 0.4f };
        var rust = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.28f, 0.2f), Roughness = 0.8f, Metallic = 0.2f };
        var lumen = MeshFactory.Emissive(new Color(0.5f, 0.9f, 1f), new Color(0.35f, 0.85f, 1f), 1.6f);
        // pilares e arco do Povo Lúmen, com runas acesas
        foreach (int s in new[] { -1, 1 })
        {
            AddMesh(new BoxMesh { Size = new Vector3(1.1f, 6.2f, 1.5f) }, stone, new Vector3(s * 2.75f, 3.1f, 0));
            AddMesh(new BoxMesh { Size = new Vector3(1.4f, 0.5f, 1.8f) }, carved, new Vector3(s * 2.75f, 0.25f, 0));
            for (int i = 0; i < 5; i++)
                AddMesh(new BoxMesh { Size = new Vector3(0.28f, 0.28f, 0.05f) }, lumen, new Vector3(s * 2.75f, 1.2f + i * 1.0f, 0.76f)).Rotation = new Vector3(0, 0, Mathf.Pi / 4);
        }
        AddMesh(new BoxMesh { Size = new Vector3(6.8f, 1.1f, 1.7f) }, stone, new Vector3(0, 6.6f, 0));
        AddMesh(new PrismMesh { Size = new Vector3(4.6f, 1.6f, 1.5f) }, carved, new Vector3(0, 7.95f, 0));
        AddMesh(MeshFactory.CrystalPrism(6, 1, 1, 0.3f), lumen, new Vector3(0, 8.2f, 0.5f), new Vector3(0.35f, 1.2f, 0.35f));
        // portas de ferro (giram nas dobradiças)
        _doorL = Door(-1, iron, rust);
        _doorR = Door(1, iron, rust);
        // fundo escuro (só aparece com as portas abertas) e luz vermelha das profundezas
        _void = AddMesh(new BoxMesh { Size = new Vector3(4.4f, 5.9f, 0.1f) }, new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.0f, 0.02f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, new Vector3(0, 3.0f, -0.4f));
        _deep = new OmniLight3D { LightColor = new Color(1f, 0.2f, 0.25f), LightEnergy = 0f, OmniRange = 9f, Position = new Vector3(0, 2.5f, 0.6f) };
        AddChild(_deep);
        // correntes cruzadas e o selo do Mestre (martelo dourado em brasa)
        _chains = new Node3D();
        AddChild(_chains);
        foreach (int s in new[] { -1, 1 })
            for (int i = 0; i < 16; i++)
            {
                float t = i / 15f;
                var p = new Vector3(Mathf.Lerp(-2.3f, 2.3f, t) * s, Mathf.Lerp(5.6f, 0.9f, t), 0.62f);
                _chains.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.07f, OuterRadius = 0.13f, Rings = 10, RingSegments = 6 }, MaterialOverride = iron, Position = p, Rotation = new Vector3(i % 2 == 0 ? 0 : Mathf.Pi / 2, 0, s * 0.8f) });
            }
        _seal = new Node3D { Position = new Vector3(0, 3.3f, 0.7f) };
        AddChild(_seal);
        var gold = MeshFactory.Emissive(new Color(1f, 0.7f, 0.25f), new Color(1f, 0.45f, 0.15f), 1.8f);
        _seal.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.62f, Height = 0.12f, RadialSegments = 24 }, MaterialOverride = gold, Rotation = new Vector3(Mathf.Pi / 2, 0, 0) });
        _seal.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.75f, RadialSegments = 6 }, MaterialOverride = rust, Position = new Vector3(0, -0.05f, 0.1f), Rotation = new Vector3(0, 0, 0.7f) });
        _seal.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.42f, 0.2f, 0.12f) }, MaterialOverride = iron, Position = new Vector3(0.22f, 0.24f, 0.12f), Rotation = new Vector3(0, 0, 0.7f) });
        _glow = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.25f), LightEnergy = 1.5f, OmniRange = 8f, Position = new Vector3(0, 3.3f, 1.6f) };
        AddChild(_glow);
        Label = MakeLabel("PORTÃO SELADO", new Color(1f, 0.6f, 0.4f), 9.4f, 64);
        Label.VisibilityRangeEnd = 45f;
        bool open = (GameState.Instance?.Data.StoryStage ?? 0) >= StoryData.GateOpen;
        _open = _openTarget = open ? 1f : 0f;
        Apply();
    }

    Node3D Door(int side, Material iron, Material rust)
    {
        var hinge = new Node3D { Position = new Vector3(side * 2.2f, 0, 0.1f) };
        AddChild(hinge);
        hinge.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.2f, 5.9f, 0.22f) }, MaterialOverride = iron, Position = new Vector3(-side * 1.1f, 2.95f + 0.1f, 0) });
        for (int i = 0; i < 4; i++)
            hinge.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2.25f, 0.14f, 0.28f) }, MaterialOverride = rust, Position = new Vector3(-side * 1.1f, 0.7f + i * 1.5f, 0) });
        for (int y = 0; y < 8; y++)
            hinge.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 6, Rings = 3 }, MaterialOverride = rust, Position = new Vector3(-side * 0.25f, 0.5f + y * 0.7f, 0.13f) });
        return hinge;
    }

    void Apply()
    {
        float k = Mathf.SmoothStep(0f, 1f, _open);
        _doorL.Rotation = new Vector3(0, k * 1.55f, 0);
        _doorR.Rotation = new Vector3(0, -k * 1.55f, 0);
        _chains.Visible = _open < 0.05f;
        _seal.Visible = _open < 0.05f;
        _glow.Visible = _open < 0.05f;
        _deep.LightEnergy = 2.2f * k;
        _void.Visible = _open > 0.01f;
    }

    /// <summary>Abre as portas (animação de 2,5 s).</summary>
    public void Open() => _openTarget = 1f;
    public bool IsOpen => _open >= 0.99f;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsVisibleInTree()) return;
        if (Mathf.Abs(_open - _openTarget) > 0.001f) { _open = Mathf.MoveToward(_open, _openTarget, (float)delta * 0.4f); Apply(); }
        if (_glow.Visible) _glow.LightEnergy = 1.5f + 0.4f * Mathf.Sin(Time * 2.2f);
        if (_open > 0.5f) _deep.LightEnergy = 2.2f + 0.8f * Mathf.Sin(Time * 1.3f) * Mathf.Sin(Time * 0.7f);
    }

    public override string GetPrompt()
    {
        int st = GameState.Instance?.Story ?? 0;
        return st switch
        {
            StoryData.FindGate => Loc.F("{0} Tirar um molde de cera do selo", InputHints.Interact),
            StoryData.OpenGate => Loc.F("{0} Usar a chave falsa da Guilda", InputHints.Interact),
            StoryData.GateOpen => Loc.F("{0} Olhar a escuridão", InputHints.Interact),
            _ => Loc.F("{0} Examinar o portão selado", InputHints.Interact),
        };
    }

    public override void Interact(PlayerController player) => UiBus.RequestGate();
}
