using System;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.Player;

/// <summary>
/// Arma na mão (primeira pessoa): espada com lâmina do minério e fio brilhante, ou martelo de cabeça maciça.
/// Golpe da espada = corte em arco da direita para a esquerda; do martelo = pancada de cima para baixo.
/// </summary>
public partial class WeaponView : Node3D
{
    Node3D _pivot, _model;
    WeaponDef _def;
    float _t = 1f, _duration = 0.4f, _time, _equip = 1f;
    MeshInstance3D _trail;
    StandardMaterial3D _trailMat;
    readonly Vector3 _rest = new(0.3f, -0.32f, -0.48f);

    public override void _Ready()
    {
        _pivot = new Node3D { Position = _rest };
        AddChild(_pivot);
        _trailMat = new StandardMaterial3D { AlbedoColor = new Color(1, 1, 1, 0), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = true, EmissionEnergyMultiplier = 2f };
        _trail = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.3f, OuterRadius = 0.33f, Rings = 24, RingSegments = 3 }, MaterialOverride = _trailMat, Position = new Vector3(0.08f, -0.12f, -0.6f), Rotation = new Vector3(Mathf.Pi / 2 - 0.3f, 0, 0), Scale = new Vector3(1f, 0.12f, 0.7f), Layers = ToolView.ToolLayer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_trail);
    }

    MeshInstance3D Part(Mesh m, Material mat, Vector3 pos, Vector3? rot = null)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, Layers = ToolView.ToolLayer, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _model.AddChild(mi);
        return mi;
    }

    /// <summary>Monta o modelo da arma (null = nenhuma).</summary>
    public void SetWeapon(WeaponDef w)
    {
        if (w == _def && _model != null) return;
        _def = w;
        _model?.QueueFree();
        _model = null;
        if (w == null) return;
        _model = new Node3D();
        _pivot.AddChild(_model);
        var c = WeaponData.BladeColor(w);
        var g = WeaponData.GlowColor(w);
        var blade = new StandardMaterial3D { AlbedoColor = c.Lightened(0.1f), Roughness = 0.35f, Metallic = 0.3f, EmissionEnabled = true, Emission = g, EmissionEnergyMultiplier = 0.08f };
        var edge = new StandardMaterial3D { AlbedoColor = g, EmissionEnabled = true, Emission = g, EmissionEnergyMultiplier = 0.8f };
        var grip = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.15f, 0.1f), Roughness = 0.9f };
        var metal = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.5f, 0.42f), Roughness = 0.35f, Metallic = 0.5f };
        _trailMat.AlbedoColor = new Color(g, 0f);
        _trailMat.Emission = g;
        if (w.Kind == WeaponKind.Sword)
        {
            // cabo com couro, guarda, lâmina afinando e fio aceso
            Part(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.024f, Height = 0.2f, RadialSegments = 8 }, grip, new Vector3(0, -0.06f, 0));
            Part(new SphereMesh { Radius = 0.035f, Height = 0.07f }, metal, new Vector3(0, -0.17f, 0));
            Part(new BoxMesh { Size = new Vector3(0.2f, 0.03f, 0.05f) }, metal, new Vector3(0, 0.05f, 0));
            Part(new PrismMesh { Size = new Vector3(0.07f, 0.62f, 0.016f), LeftToRight = 0.5f }, blade, new Vector3(0, 0.38f, 0));
            Part(new BoxMesh { Size = new Vector3(0.012f, 0.55f, 0.02f) }, edge, new Vector3(0, 0.35f, 0));
            Part(new CrystalPrismCache().Get(), edge, new Vector3(0, 0.05f, 0.03f)).Scale = new Vector3(0.025f, 0.05f, 0.025f);
            _model.Rotation = new Vector3(-0.35f, 0.25f, -0.2f);
        }
        else
        {
            // cabo comprido e cabeça grande com placas do minério
            Part(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.027f, Height = 0.6f, RadialSegments = 8 }, grip, new Vector3(0, 0.1f, 0));
            Part(new BoxMesh { Size = new Vector3(0.3f, 0.16f, 0.16f) }, blade, new Vector3(0, 0.43f, 0));
            foreach (int s in new[] { -1, 1 })
            {
                Part(new BoxMesh { Size = new Vector3(0.04f, 0.19f, 0.19f) }, metal, new Vector3(s * 0.15f, 0.43f, 0));
                Part(new BoxMesh { Size = new Vector3(0.02f, 0.1f, 0.1f) }, edge, new Vector3(s * 0.172f, 0.43f, 0));
            }
            _model.Rotation = new Vector3(-0.25f, 0.2f, -0.1f);
        }
        _equip = 0f;
    }

    /// <summary>Começa um golpe que dura 'interval' segundos.</summary>
    public void Swing(float interval)
    {
        _duration = Mathf.Clamp(interval * 0.85f, 0.22f, 0.8f);
        _t = 0f;
    }

    /// <summary>Instante (s) em que o golpe acerta, a partir do clique.</summary>
    public static float HitDelay(WeaponDef w) => w != null && w.Kind == WeaponKind.Hammer ? 0.32f : 0.13f;

    public override void _Process(double delta)
    {
        if (_model == null || !Visible) return;
        float dt = (float)delta;
        _time += dt;
        _equip = Mathf.Min(1f, _equip + dt / 0.3f);
        _t = Mathf.Min(1f, _t + dt / _duration);
        float drop = (1f - _equip) * 0.35f;
        float idle = MathF.Sin(_time * 1.6f) * 0.006f;
        bool hammer = _def.Kind == WeaponKind.Hammer;
        Vector3 rot, pos = _rest + new Vector3(0, idle - drop, 0);
        float trail = 0f;
        if (_t < 1f)
        {
            if (!hammer)
            {
                // corte em arco: prepara à direita no alto e varre para a esquerda embaixo
                float a = _t < 0.25f ? -(_t / 0.25f) : (_t - 0.25f) / 0.75f;
                float k = a < 0 ? a : EaseOut(a);
                rot = new Vector3(-0.3f - Mathf.Abs(k) * 0.5f, 0.6f * k, 1.4f * -k + (a < 0 ? 0.6f * -a : 0));
                pos += new Vector3(-0.32f * Mathf.Max(0, k), 0.1f * (a < 0 ? -a : 1 - k), -0.08f * Mathf.Max(0, k));
                trail = a > 0 && a < 0.6f ? 1f - a / 0.6f : 0f;
            }
            else
            {
                // martelo: ergue atrás da cabeça e desce com força
                float a = _t < 0.45f ? _t / 0.45f : 1f - (_t - 0.45f) / 0.55f;
                float k = _t < 0.45f ? EaseOut(a) : a * a;
                rot = new Vector3(-0.3f - 1.5f * k + (_t >= 0.45f ? 0.9f * (1 - a) * (1 - a) * 0 : 0), 0.15f, -0.1f);
                if (_t >= 0.45f && _t < 0.6f) rot.X = -0.3f + 0.8f * (1f - (_t - 0.45f) / 0.15f);
                pos += new Vector3(-0.08f * k, 0.18f * k, 0.05f * k);
            }
        }
        else rot = Vector3.Zero;
        _pivot.Rotation = rot;
        _pivot.Position = pos;
        _trail.Visible = false; // o arco do rastro atrapalhava a visão; o golpe já é bem visível
        _ = trail;
        _trailMat.AlbedoColor = new Color(_trailMat.Emission, trail * 0.22f);
        _trail.Rotation = new Vector3(Mathf.Pi / 2 - 0.3f, 0, (1f - trail) * -1.2f);
    }

    static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

    sealed class CrystalPrismCache
    {
        static Mesh _m;
        public Mesh Get() => _m ??= World.MeshFactory.CrystalPrism(6, 1, 1, 0.3f);
    }
}
