using System;
using CrystalExcavator.Core;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Player;

/// <summary>
/// Escavadeira de feixe em primeira pessoa (corpo laranja/cinza, tela ciano, emissor),
/// com animação procedural (balanço, recuo a cada pulso de dano) e feixe de energia até o ponto atingido.
/// Cores e tamanho do emissor evoluem com o tier de dano.
/// </summary>
public partial class ToolView : Node3D
{
    Node3D _pivot;
    Node3D _model;
    Node3D _muzzle;
    StandardMaterial3D _screenMat, _coilMat, _beamMat;
    MeshInstance3D _beam, _beamCore, _flare;
    float _recoilT = 1f;
    float _recoilDuration = 0.15f;
    float _bobT;
    float _time;
    Vector3 _restPos = new(0.26f, -0.25f, -0.4f);
    int _tier = -1;
    Vector2 _sway;
    float _beamAlpha;

    public bool Firing;            // definido pelo jogador a cada quadro
    public Vector3 BeamEnd;        // ponto global atingido
    public Color BeamColor = new(0.4f, 0.9f, 1f);

    public static readonly Color[] TierColors =
    {
        new(0.35f, 0.9f, 1f), new(1f, 0.3f, 0.85f), new(0.4f, 0.75f, 1f), new(0.7f, 0.4f, 1f), new(1f, 0.55f, 0.85f), new(1f, 0.25f, 0.75f)
    };

    public override void _Ready()
    {
        _pivot = new Node3D { Position = _restPos };
        AddChild(_pivot);
        Rebuild(GameState.Instance?.ToolTier ?? 0);
        if (GameState.Instance != null) GameState.Instance.UpgradesChanged += OnUpgrades;
        BuildBeam();
    }

    public override void _ExitTree()
    {
        if (GameState.Instance != null) GameState.Instance.UpgradesChanged -= OnUpgrades;
    }

    void OnUpgrades()
    {
        int t = GameState.Instance.ToolTier;
        if (t != _tier) Rebuild(t);
    }

    MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? rot = null)
    {
        var m = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = mat, Position = pos,
            Rotation = rot ?? Vector3.Zero,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(m);
        return m;
    }

    static StandardMaterial3D Lit(Color c, float rough = 0.55f) => new() { AlbedoColor = c, Roughness = rough, Metallic = 0.15f, RimEnabled = true, Rim = 0.3f, RimTint = 0.6f };

    void Rebuild(int tier)
    {
        _tier = tier;
        _model?.QueueFree();
        _model = new Node3D();
        _pivot.AddChild(_model);
        var accent = TierColors[tier];
        var orange = Lit(new Color(0.95f, 0.5f, 0.15f), 0.5f);
        var grey = Lit(new Color(0.42f, 0.44f, 0.5f), 0.45f);
        var dark = Lit(new Color(0.16f, 0.17f, 0.21f), 0.6f);
        var steel = Lit(new Color(0.6f, 0.63f, 0.7f), 0.3f);
        _screenMat = new StandardMaterial3D
        {
            AlbedoColor = accent.Darkened(0.7f), EmissionEnabled = true, Emission = accent.Darkened(0.2f), EmissionEnergyMultiplier = 0.7f,
        };
        _coilMat = new StandardMaterial3D
        {
            AlbedoColor = accent, EmissionEnabled = true, Emission = accent, EmissionEnergyMultiplier = 1.2f,
        };
        var rx = new Vector3(Mathf.Pi / 2, 0, 0);
        float bulk = 1f + tier * 0.07f; // ferramentas superiores são mais robustas

        // corpo (comum a todas)
        Part(_model, new BoxMesh { Size = new Vector3(0.11f, 0.1f, 0.2f) * bulk }, orange, new Vector3(0, 0, 0.07f));
        Part(_model, new BoxMesh { Size = new Vector3(0.125f, 0.05f, 0.16f) * bulk }, orange, new Vector3(0, 0.065f * bulk, 0.05f));
        Part(_model, new BoxMesh { Size = new Vector3(0.128f, 0.012f, 0.165f) * bulk }, grey, new Vector3(0, 0.035f * bulk, 0.05f));
        Part(_model, new BoxMesh { Size = new Vector3(0.13f, 0.03f, 0.1f) * bulk }, dark, new Vector3(0, -0.06f, 0.1f));
        Part(_model, new BoxMesh { Size = new Vector3(0.005f, 0.045f, 0.09f) }, _screenMat, new Vector3(-0.058f * bulk, 0.005f, 0.07f));
        Part(_model, new BoxMesh { Size = new Vector3(0.06f, 0.004f, 0.08f) }, _screenMat, new Vector3(0, 0.092f * bulk, 0.06f));
        // empunhadura + luva + manga
        Part(_model, new BoxMesh { Size = new Vector3(0.06f, 0.14f, 0.07f) }, dark, new Vector3(0, -0.1f, 0.1f), new Vector3(-0.3f, 0, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.09f, 0.1f, 0.1f) }, Lit(new Color(0.12f, 0.12f, 0.14f), 0.85f), new Vector3(0.005f, -0.12f, 0.11f), new Vector3(-0.3f, 0, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.1f, 0.12f, 0.22f) }, orange, new Vector3(0.02f, -0.2f, 0.26f), new Vector3(-0.55f, 0, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.105f, 0.02f, 0.05f) }, _coilMat, new Vector3(0.02f, -0.155f, 0.18f), new Vector3(-0.55f, 0, 0));
        if (tier >= 2)
        {
            // alça superior de transporte
            Part(_model, new BoxMesh { Size = new Vector3(0.03f, 0.025f, 0.16f) }, dark, new Vector3(0, 0.15f * bulk, 0.04f));
            Part(_model, new BoxMesh { Size = new Vector3(0.03f, 0.06f, 0.025f) }, dark, new Vector3(0, 0.12f * bulk, -0.03f));
            Part(_model, new BoxMesh { Size = new Vector3(0.03f, 0.06f, 0.025f) }, dark, new Vector3(0, 0.12f * bulk, 0.11f));
        }

        // carcaça frontal
        Part(_model, new CylinderMesh { TopRadius = 0.06f * bulk, BottomRadius = 0.065f * bulk, Height = 0.1f, RadialSegments = 10, Rings = 1 }, grey, new Vector3(0, 0.005f, -0.07f), rx);
        float z = -0.125f;
        switch (tier)
        {
            case 0: // Broca Inicial: duas bobinas + ponta cônica
            case 1: // Cortador de Pulso: três bobinas magenta
            {
                int coils = tier == 0 ? 2 : 3;
                for (int i = 0; i < coils; i++)
                    Part(_model, new CylinderMesh { TopRadius = 0.047f, BottomRadius = 0.047f, Height = 0.014f, RadialSegments = 10, Rings = 1 }, _coilMat, new Vector3(0, 0.005f, z - 0.012f - i * 0.026f), rx);
                z -= coils * 0.026f + 0.01f;
                Part(_model, new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.04f, Height = 0.04f, RadialSegments = 10, Rings = 1 }, dark, new Vector3(0, 0.005f, z - 0.02f), rx);
                z -= 0.04f;
                Part(_model, MeshFactory.Spike(8, 0.034f, 0.1f), steel, new Vector3(0, 0.005f, z), new Vector3(-Mathf.Pi / 2, 0, 0));
                z -= 0.1f;
                break;
            }
            case 2: // Perfurador de Arco: anéis espirais elétricos + broca grande
            {
                for (int i = 0; i < 5; i++)
                    Part(_model, new TorusMesh { InnerRadius = 0.04f, OuterRadius = 0.055f, Rings = 12, RingSegments = 6 }, _coilMat, new Vector3(0, 0.005f, z - 0.015f - i * 0.024f), rx);
                Part(_model, new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 0.13f, RadialSegments = 10, Rings = 1 }, dark, new Vector3(0, 0.005f, z - 0.065f), rx);
                z -= 0.13f;
                Part(_model, MeshFactory.Spike(8, 0.05f, 0.14f), steel, new Vector3(0, 0.005f, z), new Vector3(-Mathf.Pi / 2, 0, 0));
                z -= 0.14f;
                break;
            }
            case 3: // Ressonador Cristalino: câmara de vidro com cristais dentro
            {
                var glass = new StandardMaterial3D { AlbedoColor = new Color(accent, 0.25f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.05f, EmissionEnabled = true, Emission = accent * 0.3f };
                Part(_model, new CylinderMesh { TopRadius = 0.058f, BottomRadius = 0.058f, Height = 0.13f, RadialSegments = 12, Rings = 1 }, glass, new Vector3(0, 0.005f, z - 0.065f), rx);
                for (int i = 0; i < 3; i++)
                    Part(_model, MeshFactory.CrystalPrism(6, 1, 1, 0.4f), _coilMat, new Vector3(-0.025f + i * 0.025f, -0.035f, z - 0.03f - i * 0.035f)).Scale = new Vector3(0.016f, 0.06f + i * 0.01f, 0.016f);
                for (int k = 0; k < 2; k++)
                    Part(_model, new CylinderMesh { TopRadius = 0.064f, BottomRadius = 0.064f, Height = 0.014f, RadialSegments = 12, Rings = 1 }, orange, new Vector3(0, 0.005f, z - k * 0.13f), rx);
                z -= 0.14f;
                Part(_model, new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.03f, Height = 0.04f, RadialSegments = 12, Rings = 1 }, _coilMat, new Vector3(0, 0.005f, z - 0.02f), new Vector3(-Mathf.Pi / 2, 0, 0));
                z -= 0.04f;
                break;
            }
            case 4: // Lança Prismática: ponta de cristal longa
            {
                for (int i = 0; i < 3; i++)
                    Part(_model, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.05f, Height = 0.016f, RadialSegments = 10, Rings = 1 }, i == 1 ? orange : _coilMat, new Vector3(0, 0.005f, z - 0.012f - i * 0.026f), rx);
                z -= 0.09f;
                var lance = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.6f, 0.9f), EmissionEnabled = true, Emission = new Color(1f, 0.45f, 0.85f), EmissionEnergyMultiplier = 1.4f, RimEnabled = true, Rim = 0.8f };
                Part(_model, MeshFactory.CrystalPrism(6, 1, 1, 0.45f), lance, new Vector3(0, 0.005f, z + 0.02f), new Vector3(-Mathf.Pi / 2, 0, 0)).Scale = new Vector3(0.04f, 0.22f, 0.04f);
                z -= 0.2f;
                break;
            }
            default: // Quebra-Núcleo: canhão largo com três anéis
            {
                Part(_model, new CylinderMesh { TopRadius = 0.075f, BottomRadius = 0.075f, Height = 0.16f, RadialSegments = 12, Rings = 1 }, dark, new Vector3(0, 0.005f, z - 0.08f), rx);
                for (int i = 0; i < 3; i++)
                    Part(_model, new TorusMesh { InnerRadius = 0.07f, OuterRadius = 0.088f, Rings = 14, RingSegments = 6 }, _coilMat, new Vector3(0, 0.005f, z - 0.03f - i * 0.05f), rx);
                z -= 0.16f;
                Part(_model, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.08f, Height = 0.05f, RadialSegments = 12, Rings = 1 }, orange, new Vector3(0, 0.005f, z - 0.025f), rx);
                z -= 0.05f;
                break;
            }
        }
        _muzzle = new Node3D { Position = new Vector3(0, 0.005f, z - 0.01f) };
        _model.AddChild(_muzzle);
        _model.Rotation = new Vector3(0.1f, 0.32f, 0.06f);
        _model.Scale = Vector3.One * 0.8f;
    }

    void BuildBeam()
    {
        _beamMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(BeamColor, 0.6f),
            NoDepthTest = false,
        };
        var coreMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(1, 1, 1, 0.9f),
        };
        // o feixe fica no espaço do mundo (não herda o balanço da arma)
        var holder = new Node3D { TopLevel = true };
        AddChild(holder);
        _beam = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 8, Rings = 1 }, MaterialOverride = _beamMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _beamCore = new MeshInstance3D { Mesh = _beam.Mesh, MaterialOverride = coreMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _flare = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 8, Rings = 4 }, MaterialOverride = _beamMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        holder.AddChild(_beam);
        holder.AddChild(_beamCore);
        holder.AddChild(_flare);
    }

    /// <summary>Pulso de dano: recuo curto com duração ligada à cadência.</summary>
    public void Swing(float interval)
    {
        _recoilDuration = Mathf.Clamp(interval * 0.8f, 0.06f, 0.2f);
        _recoilT = 0f;
    }

    public void AddSway(Vector2 mouseDelta) => _sway += mouseDelta * 0.0006f;

    public void UpdateView(float dt, float moveSpeed01, bool grounded)
    {
        _time += dt;
        if (grounded && moveSpeed01 > 0.05f) _bobT += dt * (6f + moveSpeed01 * 6f);
        float bobX = MathF.Sin(_bobT) * 0.01f * moveSpeed01;
        float bobY = MathF.Abs(MathF.Cos(_bobT)) * 0.012f * moveSpeed01;
        _sway = _sway.Lerp(Vector2.Zero, Mathf.Min(1, dt * 10f));
        _sway = _sway.Clamp(new Vector2(-0.06f, -0.06f), new Vector2(0.06f, 0.06f));

        float kick = 0;
        if (_recoilT < 1f)
        {
            _recoilT = Mathf.Min(1f, _recoilT + dt / _recoilDuration);
            kick = MathF.Sin(_recoilT * MathF.PI) * (1f - _recoilT * 0.5f);
        }
        float idle = MathF.Sin(_time * 1.6f) * 0.004f;
        float vibrate = Firing ? MathF.Sin(_time * 70f) * 0.0015f : 0f;
        _pivot.Position = _restPos + new Vector3(bobX - _sway.X + vibrate, -bobY + _sway.Y + idle, kick * 0.035f);
        _pivot.Rotation = new Vector3(kick * 0.06f, 0, 0);

        // tela e bobinas pulsam com o disparo
        float pulse = Firing ? 1.2f + 0.8f * MathF.Abs(MathF.Sin(_time * 12f)) : 0.6f + 0.3f * MathF.Sin(_time * 2f);
        if (_screenMat != null) _screenMat.EmissionEnergyMultiplier = 0.5f + pulse * 0.25f;
        if (_coilMat != null) _coilMat.EmissionEnergyMultiplier = 0.4f + pulse * 0.5f + kick;

        UpdateBeam(dt);
    }

    void UpdateBeam(float dt)
    {
        _beamAlpha = Mathf.MoveToward(_beamAlpha, Firing ? 1f : 0f, dt * (Firing ? 14f : 8f));
        bool show = _beamAlpha > 0.01f && _muzzle != null;
        _beam.Visible = _beamCore.Visible = _flare.Visible = show;
        if (!show) return;
        var start = _muzzle.GlobalPosition;
        var end = BeamEnd;
        var dir = end - start;
        float len = dir.Length();
        if (len < 0.05f) { _beam.Visible = _beamCore.Visible = false; return; }
        var mid = start + dir * 0.5f;
        var up = dir / len;
        var basis = BasisFromUp(up);
        float flicker = 0.85f + 0.15f * MathF.Sin(_time * 50f) + 0.1f * MathF.Sin(_time * 23f);
        float w = 0.03f * flicker * (1f + _tier * 0.12f) * (0.6f + 0.4f * _beamAlpha);
        _beam.GlobalTransform = new Transform3D(basis.ScaleLocal(new Vector3(w, len, w)), mid);
        _beamCore.GlobalTransform = new Transform3D(basis.ScaleLocal(new Vector3(w * 0.35f, len, w * 0.35f)), mid);
        _flare.GlobalTransform = new Transform3D(Basis.Identity.ScaleLocal(Vector3.One * (0.06f + 0.03f * flicker) * _beamAlpha), end);
        _beamMat.AlbedoColor = new Color(BeamColor.Lightened(0.2f), 0.55f * _beamAlpha);
    }

    static Basis BasisFromUp(Vector3 up)
    {
        var x = up.Cross(MathF.Abs(up.Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized();
        var z = x.Cross(up).Normalized();
        return new Basis(x, up, z);
    }
}
