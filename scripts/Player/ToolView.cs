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
        new(0.35f, 0.9f, 1f), new(0.45f, 0.7f, 1f), new(0.75f, 0.45f, 1f), new(1f, 0.78f, 0.3f), new(1f, 0.4f, 0.3f)
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
        var orange = Lit(tier >= 3 ? new Color(0.95f, 0.62f, 0.2f) : new Color(0.95f, 0.5f, 0.15f), 0.5f);
        var grey = Lit(new Color(0.42f, 0.44f, 0.5f), 0.45f);
        var dark = Lit(new Color(0.16f, 0.17f, 0.21f), 0.6f);
        var glove = Lit(new Color(0.12f, 0.12f, 0.14f), 0.85f);
        _screenMat = new StandardMaterial3D
        {
            AlbedoColor = accent.Darkened(0.7f), EmissionEnabled = true, Emission = accent.Darkened(0.2f), EmissionEnergyMultiplier = 0.7f,
        };
        _coilMat = new StandardMaterial3D
        {
            AlbedoColor = accent, EmissionEnabled = true, Emission = accent, EmissionEnergyMultiplier = 1.2f,
        };

        // corpo principal (eixo da arma ao longo de -Z)
        Part(_model, new BoxMesh { Size = new Vector3(0.11f, 0.1f, 0.2f) }, orange, new Vector3(0, 0, 0.07f));
        Part(_model, new BoxMesh { Size = new Vector3(0.125f, 0.05f, 0.16f) }, orange, new Vector3(0, 0.065f, 0.05f));
        Part(_model, new BoxMesh { Size = new Vector3(0.128f, 0.012f, 0.165f) }, grey, new Vector3(0, 0.035f, 0.05f));
        Part(_model, new BoxMesh { Size = new Vector3(0.13f, 0.03f, 0.1f) }, dark, new Vector3(0, -0.06f, 0.1f));
        // tela lateral e superior
        Part(_model, new BoxMesh { Size = new Vector3(0.005f, 0.045f, 0.09f) }, _screenMat, new Vector3(-0.058f, 0.005f, 0.07f));
        Part(_model, new BoxMesh { Size = new Vector3(0.06f, 0.004f, 0.08f) }, _screenMat, new Vector3(0, 0.092f, 0.06f));
        // carcaça frontal e cano
        Part(_model, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.065f, Height = 0.1f, RadialSegments = 10, Rings = 1 }, grey,
            new Vector3(0, 0.005f, -0.07f), new Vector3(Mathf.Pi / 2, 0, 0));
        int coils = 3 + tier;
        for (int i = 0; i < coils; i++)
            Part(_model, new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.045f, Height = 0.012f, RadialSegments = 10, Rings = 1 }, i % 2 == 0 ? _coilMat : dark,
                new Vector3(0, 0.005f, -0.135f - i * 0.024f), new Vector3(Mathf.Pi / 2, 0, 0));
        float barrelLen = 0.1f + coils * 0.024f;
        Part(_model, new CylinderMesh { TopRadius = 0.028f, BottomRadius = 0.036f, Height = barrelLen, RadialSegments = 10, Rings = 1 }, dark,
            new Vector3(0, 0.005f, -0.125f - barrelLen / 2), new Vector3(Mathf.Pi / 2, 0, 0));
        // emissor
        float muzzleZ = -0.13f - barrelLen;
        Part(_model, new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.026f, Height = 0.02f, RadialSegments = 10, Rings = 1 }, _coilMat,
            new Vector3(0, 0.005f, muzzleZ), new Vector3(Mathf.Pi / 2, 0, 0));
        _muzzle = new Node3D { Position = new Vector3(0, 0.005f, muzzleZ - 0.02f) };
        _model.AddChild(_muzzle);
        // empunhadura + luva
        Part(_model, new BoxMesh { Size = new Vector3(0.06f, 0.14f, 0.07f) }, dark, new Vector3(0, -0.1f, 0.1f), new Vector3(-0.3f, 0, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.09f, 0.1f, 0.1f) }, glove, new Vector3(0.005f, -0.12f, 0.11f), new Vector3(-0.3f, 0, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.1f, 0.12f, 0.22f) }, orange, new Vector3(0.02f, -0.2f, 0.26f), new Vector3(-0.55f, 0, 0)); // manga
        Part(_model, new BoxMesh { Size = new Vector3(0.105f, 0.02f, 0.05f) }, _coilMat, new Vector3(0.02f, -0.155f, 0.18f), new Vector3(-0.55f, 0, 0));
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
