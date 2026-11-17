using System.Collections.Generic;
using CrystalExcavator.Core;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Mining;

/// <summary>
/// Pool de partículas CPU (compatível com GPUs integradas antigas) e textos flutuantes.
/// Evita criar nós durante o jogo.
/// </summary>
public partial class Effects : Node3D
{
    public static Effects Instance { get; private set; }

    readonly List<CpuParticles3D> _hitPool = new();
    readonly List<CpuParticles3D> _burstPool = new();
    readonly List<Label3D> _textPool = new();
    int _hitIdx, _burstIdx, _textIdx;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public override void _Ready()
    {
        var shardMesh = MeshFactory.CrystalPrism(4, 0.05f, 0.14f, 0.4f);
        var mat = MeshFactory.Unshaded(Colors.White);
        for (int i = 0; i < 8; i++) _hitPool.Add(MakeEmitter(shardMesh, mat, 14, 0.45f, 4.5f, 0.6f));
        for (int i = 0; i < 4; i++) _burstPool.Add(MakeEmitter(shardMesh, mat, 40, 0.9f, 7f, 1.3f));
        for (int i = 0; i < 10; i++)
        {
            var l = new Label3D
            {
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 48,
                OutlineSize = 12,
                PixelSize = 0.0042f,
                NoDepthTest = true,
                Visible = false,
                Modulate = Colors.White,
                OutlineModulate = new Color(0, 0, 0, 0.8f),
            };
            AddChild(l);
            _textPool.Add(l);
        }
        Settings.Changed += ApplyQuality;
        ApplyQuality();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete) Settings.Changed -= ApplyQuality;
    }

    void ApplyQuality()
    {
        float f = Settings.Quality switch { GraphicsQuality.Low => 0.45f, GraphicsQuality.Medium => 0.75f, _ => 1f };
        foreach (var p in _hitPool) p.Amount = Mathf.Max(4, (int)(14 * f));
        foreach (var p in _burstPool) p.Amount = Mathf.Max(10, (int)(40 * f));
    }

    CpuParticles3D MakeEmitter(Mesh mesh, Material mat, int amount, float life, float speed, float scale)
    {
        var p = new CpuParticles3D
        {
            Emitting = false,
            OneShot = true,
            Amount = amount,
            Lifetime = life,
            Explosiveness = 0.95f,
            Mesh = mesh,
            Direction = Vector3.Up,
            Spread = 70f,
            InitialVelocityMin = speed * 0.4f,
            InitialVelocityMax = speed,
            Gravity = new Vector3(0, -12f, 0),
            AngularVelocityMin = -360,
            AngularVelocityMax = 360,
            ScaleAmountMin = scale * 0.6f,
            ScaleAmountMax = scale * 1.3f,
            DampingMin = 1f,
            DampingMax = 3f,
            LocalCoords = false,
        };
        p.MaterialOverride = mat;
        var curve = new Curve();
        curve.AddPoint(new Vector2(0, 1));
        curve.AddPoint(new Vector2(0.7f, 0.8f));
        curve.AddPoint(new Vector2(1, 0));
        p.ScaleAmountCurve = curve;
        AddChild(p);
        return p;
    }

    public void Hit(Vector3 pos, Vector3 normal, Color color)
    {
        var p = _hitPool[_hitIdx++ % _hitPool.Count];
        Emit(p, pos, normal, color);
    }

    public void Burst(Vector3 pos, Color color, float scale = 1f)
    {
        var p = _burstPool[_burstIdx++ % _burstPool.Count];
        p.Scale = Vector3.One * scale;
        Emit(p, pos, Vector3.Up, color);
    }

    static void Emit(CpuParticles3D p, Vector3 pos, Vector3 dir, Color color)
    {
        p.GlobalPosition = pos;
        p.Direction = dir.LengthSquared() > 0.01f ? dir.Normalized() : Vector3.Up;
        p.Color = color;
        p.Restart();
        p.Emitting = true;
    }

    readonly List<MeshInstance3D> _waves = new();
    int _waveIdx;

    /// <summary>Anel de energia expandindo (pulso de ressonância).</summary>
    public void Shockwave(Vector3 pos, float radius, Color color)
    {
        if (_waves.Count == 0)
        {
            for (int i = 0; i < 3; i++)
            {
                var m = new MeshInstance3D
                {
                    Mesh = new TorusMesh { InnerRadius = 0.86f, OuterRadius = 1f, Rings = 32, RingSegments = 6 },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Visible = false,
                };
                m.MaterialOverride = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                };
                AddChild(m);
                _waves.Add(m);
            }
        }
        var w = _waves[_waveIdx++ % _waves.Count];
        var mat = (StandardMaterial3D)w.MaterialOverride;
        mat.AlbedoColor = new Color(color.Lightened(0.3f), 1f);
        w.GlobalPosition = pos;
        w.Scale = new Vector3(0.2f, 0.6f, 0.2f);
        w.Visible = true;
        var tw = w.CreateTween().SetParallel();
        tw.TweenProperty(w, "scale", new Vector3(radius, 1.4f, radius), 0.45f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(mat, "albedo_color:a", 0f, 0.5f).SetDelay(0.1f);
        tw.Chain().TweenCallback(Callable.From(() => w.Visible = false));
        Burst(pos + Vector3.Up * 0.5f, color.Lightened(0.4f), 1.6f);
    }

    public void FloatText(Vector3 pos, string text, Color color, float size = 1f)
    {
        var l = _textPool[_textIdx++ % _textPool.Count];
        l.Text = text;
        l.Modulate = color;
        l.GlobalPosition = pos;
        l.Scale = Vector3.One * size;
        l.Visible = true;
        var tw = l.CreateTween();
        tw.SetParallel();
        tw.TweenProperty(l, "global_position", pos + Vector3.Up * 0.9f, 0.9f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tw.TweenProperty(l, "modulate:a", 0f, 0.9f).SetDelay(0.3f);
        tw.Chain().TweenCallback(Callable.From(() => l.Visible = false));
    }
}
