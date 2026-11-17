using System;
using CrystalExcavator.Core;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Player;

/// <summary>
/// Ferramenta em primeira pessoa com animação procedural (balanço ao andar, golpe, recuo).
/// A aparência evolui conforme o nível de dano (tier), reforçando a sensação de poder.
/// </summary>
public partial class ToolView : Node3D
{
    Node3D _pivot;
    Node3D _model;
    MeshInstance3D _headGem;
    StandardMaterial3D _gemMat;
    float _swingT = 1f;
    float _swingDuration = 0.25f;
    float _bobT;
    Vector3 _restPos = new(0.38f, -0.36f, -0.55f);
    int _tier = -1;
    Vector2 _sway;

    static readonly Color[] HeadColors =
    {
        new(0.55f, 0.55f, 0.6f), new(0.35f, 0.55f, 0.9f), new(0.7f, 0.4f, 1f), new(1f, 0.75f, 0.3f), new(1f, 0.45f, 0.25f)
    };

    public override void _Ready()
    {
        _pivot = new Node3D { Position = _restPos };
        AddChild(_pivot);
        Rebuild(GameState.Instance?.ToolTier ?? 0);
        if (GameState.Instance != null) GameState.Instance.UpgradesChanged += OnUpgrades;
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

    void Rebuild(int tier)
    {
        _tier = tier;
        _model?.QueueFree();
        _model = new Node3D();
        _pivot.AddChild(_model);
        // cabo
        var handle = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.03f, Height = 0.62f, RadialSegments = 8, Rings = 1 },
            MaterialOverride = MeshFactory.Metal(tier >= 3 ? new Color(0.25f, 0.2f, 0.18f) : new Color(0.4f, 0.27f, 0.16f), 0.8f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        handle.Position = new Vector3(0, 0.1f, 0);
        _model.AddChild(handle);
        // anéis no cabo
        for (int i = 0; i < 1 + tier; i++)
        {
            var ring = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.034f, BottomRadius = 0.034f, Height = 0.025f, RadialSegments = 8, Rings = 1 },
                MaterialOverride = MeshFactory.Emissive(HeadColors[tier], HeadColors[tier], tier * 0.6f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(0, -0.12f + i * 0.06f, 0),
            };
            _model.AddChild(ring);
        }
        // cabeça da picareta (duas pontas)
        var headMat = MeshFactory.Metal(HeadColors[tier], 0.3f);
        var head = new Node3D { Position = new Vector3(0, 0.4f, 0) };
        _model.AddChild(head);
        float len = 0.2f + tier * 0.025f;
        for (int side = -1; side <= 1; side += 2)
        {
            var spike = new MeshInstance3D
            {
                Mesh = MeshFactory.Spike(4, 0.045f, len),
                MaterialOverride = headMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Rotation = new Vector3(0, 0, side * (Mathf.Pi / 2f + 0.15f)),
            };
            head.AddChild(spike);
        }
        var block = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.11f, 0.08f, 0.08f) },
            MaterialOverride = headMat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        head.AddChild(block);
        // gema central que brilha mais em tiers altos e pulsa ao golpear
        _gemMat = MeshFactory.CrystalMaterial(HeadColors[tier].Lightened(0.3f), HeadColors[tier], 0.6f + tier * 0.6f);
        _headGem = new MeshInstance3D
        {
            Mesh = MeshFactory.CrystalPrism(6, 1, 1, 0.4f),
            MaterialOverride = _gemMat,
            Scale = new Vector3(0.03f, 0.07f, 0.03f) * (1 + tier * 0.25f),
            Position = new Vector3(0, 0.035f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        head.AddChild(_headGem);
        _model.Rotation = new Vector3(-0.2f, 0.25f, 0.15f);
    }

    /// <summary>Inicia a animação de golpe com a duração ligada à velocidade de extração.</summary>
    public void Swing(float interval)
    {
        _swingDuration = Mathf.Clamp(interval * 0.9f, 0.08f, 0.35f);
        _swingT = 0f;
    }

    public void AddSway(Vector2 mouseDelta) => _sway += mouseDelta * 0.0006f;

    public void UpdateView(float dt, float moveSpeed01, bool grounded)
    {
        // balanço ao andar
        if (grounded && moveSpeed01 > 0.05f) _bobT += dt * (6f + moveSpeed01 * 6f);
        float bobX = MathF.Sin(_bobT) * 0.012f * moveSpeed01;
        float bobY = MathF.Abs(MathF.Cos(_bobT)) * 0.016f * moveSpeed01;
        _sway = _sway.Lerp(Vector2.Zero, Mathf.Min(1, dt * 10f));
        _sway = _sway.Clamp(new Vector2(-0.08f, -0.08f), new Vector2(0.08f, 0.08f));

        // curva do golpe: antecipação → impacto → recuperação
        float rotX = 0, posZ = 0;
        if (_swingT < 1f)
        {
            _swingT = Mathf.Min(1f, _swingT + dt / _swingDuration);
            float t = _swingT;
            if (t < 0.35f) { float k = t / 0.35f; rotX = Mathf.Lerp(0, 0.7f, Ease(k)); }
            else if (t < 0.55f) { float k = (t - 0.35f) / 0.2f; rotX = Mathf.Lerp(0.7f, -1.25f, k * k); posZ = -0.12f * k; }
            else { float k = (t - 0.55f) / 0.45f; rotX = Mathf.Lerp(-1.25f, 0, Ease(k)); posZ = Mathf.Lerp(-0.12f, 0, k); }
        }
        _pivot.Position = _restPos + new Vector3(bobX - _sway.X, -bobY + _sway.Y, posZ);
        _pivot.Rotation = new Vector3(rotX, 0, 0);
        if (_gemMat != null)
        {
            float pulse = _swingT < 1f ? (1f - _swingT) * 2f : 0f;
            _gemMat.EmissionEnergyMultiplier = 0.6f + _tier * 0.6f + pulse;
        }
    }

    static float Ease(float k) => 1f - (1f - k) * (1f - k);
}
