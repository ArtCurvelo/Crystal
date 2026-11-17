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

    readonly List<OmniLight3D> _flashes = new();
    int _flashIdx;

    /// <summary>Clarão breve de luz na quebra de um cristal (pool de 3 luzes; desligado na qualidade Baixa).</summary>
    public void Flash(Vector3 pos, Color color, float strength = 1f)
    {
        if (Settings.Quality == GraphicsQuality.Low) return;
        if (_flashes.Count == 0)
            for (int i = 0; i < 3; i++)
            {
                var l = new OmniLight3D { Visible = false, ShadowEnabled = false, OmniRange = 6f };
                AddChild(l);
                _flashes.Add(l);
            }
        var f = _flashes[_flashIdx++ % _flashes.Count];
        f.GlobalPosition = pos;
        f.LightColor = color;
        f.OmniRange = 5f * strength;
        f.LightEnergy = 4f * strength;
        f.Visible = true;
        var tw = f.CreateTween();
        tw.TweenProperty(f, "light_energy", 0f, 0.35f).SetEase(Tween.EaseType.Out);
        tw.TweenCallback(Callable.From(() => f.Visible = false));
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

    // ===================== Efeitos próprios por tipo de minério =====================
    readonly Dictionary<string, List<CpuParticles3D>> _style = new();
    readonly Dictionary<string, int> _styleIdx = new();

    static StandardMaterial3D Soft(bool additive) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
        VertexColorUseAsAlbedo = true,
    };

    /// <summary>Cria (uma vez) um emissor de estilo: malha, gravidade, velocidade e vida próprias.</summary>
    CpuParticles3D StyleEmitter(string style)
    {
        CpuParticles3D p;
        var unshaded = MeshFactory.Unshaded(Colors.White);
        switch (style)
        {
            case "sparks": // faíscas metálicas: riscos rápidos que caem
                p = MakeEmitter(new BoxMesh { Size = new Vector3(0.02f, 0.02f, 0.22f) }, unshaded, 36, 0.7f, 9f, 1f);
                p.Gravity = new Vector3(0, -16f, 0); p.Spread = 85f; p.ParticleFlagAlignY = true; break;
            case "dust": // poeira de pedra: nuvens que se espalham devagar
                p = MakeEmitter(new QuadMesh { Size = new Vector2(0.5f, 0.5f) }, Soft(false), 18, 1.6f, 1.6f, 1.4f);
                p.Gravity = new Vector3(0, 0.25f, 0); p.Spread = 180f; p.DampingMin = 2f; p.DampingMax = 3f; break;
            case "chunks": // pedaços de rocha
                p = MakeEmitter(MeshFactory.Rock(9, 0.4f), unshaded, 12, 1.1f, 6f, 0.13f);
                p.Gravity = new Vector3(0, -15f, 0); break;
            case "splinters": // estilhaços de vidro: finos, longe
                p = MakeEmitter(MeshFactory.CrystalPrism(3, 0.025f, 0.3f, 0.6f), unshaded, 46, 0.9f, 11f, 1f);
                p.Gravity = new Vector3(0, -14f, 0); p.Spread = 110f; break;
            case "frost": // névoa gelada e flocos
                p = MakeEmitter(new QuadMesh { Size = new Vector2(0.12f, 0.12f) }, Soft(true), 40, 2.2f, 2.2f, 1f);
                p.Gravity = new Vector3(0, -0.6f, 0); p.Spread = 180f; p.DampingMin = 1.5f; p.DampingMax = 2.5f; break;
            case "embers": // brasas subindo
                p = MakeEmitter(new QuadMesh { Size = new Vector2(0.09f, 0.09f) }, Soft(true), 40, 2.0f, 4.5f, 1f);
                p.Gravity = new Vector3(0, 2.2f, 0); p.Spread = 60f; break;
            case "smoke": // fumaça escura
                p = MakeEmitter(new QuadMesh { Size = new Vector2(0.8f, 0.8f) }, Soft(false), 10, 2.4f, 1.2f, 1.5f);
                p.Gravity = new Vector3(0, 1.2f, 0); p.Spread = 40f; break;
            case "bubbles": // bolhas que sobem balançando
                p = MakeEmitter(new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 8, Rings = 4 }, Soft(true), 26, 2.4f, 1.8f, 1f);
                p.Gravity = new Vector3(0, 1.6f, 0); p.Spread = 50f; p.ParticleFlagAlignY = false; break;
            case "orbs": // orbes luminosos flutuando
                p = MakeEmitter(new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 8, Rings = 4 }, unshaded, 16, 2.6f, 1.4f, 1f);
                p.Gravity = new Vector3(0, 0.9f, 0); p.Spread = 180f; p.DampingMin = 1f; p.DampingMax = 2f; break;
            case "glitter": // brilho fino (geodos)
                p = MakeEmitter(new QuadMesh { Size = new Vector2(0.07f, 0.07f) }, Soft(true), 50, 1.5f, 3.5f, 1f);
                p.Gravity = new Vector3(0, -2f, 0); p.Spread = 180f; break;
            default:
                p = MakeEmitter(MeshFactory.CrystalPrism(4, 0.05f, 0.14f, 0.4f), unshaded, 40, 0.9f, 7f, 1.3f); break;
        }
        float f = Settings.Quality switch { GraphicsQuality.Low => 0.5f, GraphicsQuality.Medium => 0.75f, _ => 1f };
        p.Amount = Mathf.Max(6, (int)(p.Amount * f));
        return p;
    }

    void EmitStyle(string style, Vector3 pos, Vector3 dir, Color color, float scale = 1f)
    {
        if (!_style.TryGetValue(style, out var list))
        {
            list = new List<CpuParticles3D>();
            for (int i = 0; i < 3; i++) list.Add(StyleEmitter(style));
            _style[style] = list;
            _styleIdx[style] = 0;
        }
        var p = list[_styleIdx[style]++ % list.Count];
        p.Scale = Vector3.One * scale;
        Emit(p, pos, dir, color);
    }

    /// <summary>Efeito de quebra conforme o formato do minério.</summary>
    public void BreakFx(Data.MaterialDef m, Vector3 pos, float scale = 1f)
    {
        var glow = m.Glow.Lightened(0.2f);
        switch (m.Archetype)
        {
            case Data.OreArchetype.Vein:
                EmitStyle("sparks", pos, Vector3.Up, new Color(1f, 0.85f, 0.5f).Lerp(glow, 0.4f), scale);
                EmitStyle("chunks", pos, Vector3.Up, m.Color, scale);
                break;
            case Data.OreArchetype.Banded:
                EmitStyle("dust", pos, Vector3.Up, new Color(m.Color.Lerp(new Color(0.6f, 0.55f, 0.5f), 0.6f), 0.55f), scale);
                EmitStyle("chunks", pos, Vector3.Up, m.Color, scale);
                break;
            case Data.OreArchetype.Shards:
                EmitStyle("splinters", pos, Vector3.Up, glow, scale);
                EmitStyle("dust", pos, Vector3.Up, new Color(0.15f, 0.12f, 0.18f, 0.5f), scale * 0.7f);
                break;
            case Data.OreArchetype.Ice:
                EmitStyle("frost", pos, Vector3.Up, new Color(0.85f, 0.95f, 1f, 0.8f), scale);
                Burst(pos, glow, scale);
                break;
            case Data.OreArchetype.Magma:
                EmitStyle("embers", pos, Vector3.Up, new Color(1f, 0.55f, 0.15f), scale);
                EmitStyle("smoke", pos, Vector3.Up, new Color(0.12f, 0.1f, 0.1f, 0.6f), scale);
                break;
            case Data.OreArchetype.Coral:
                EmitStyle("bubbles", pos, Vector3.Up, new Color(glow, 0.7f), scale);
                Burst(pos, glow, scale * 0.8f);
                break;
            case Data.OreArchetype.Pearl:
                EmitStyle("orbs", pos, Vector3.Up, glow, scale);
                EmitStyle("bubbles", pos, Vector3.Up, new Color(0.8f, 0.9f, 1f, 0.6f), scale * 0.8f);
                break;
            case Data.OreArchetype.Geode:
                EmitStyle("glitter", pos, Vector3.Up, glow, scale);
                Burst(pos, glow, scale);
                break;
            default:
                Burst(pos, glow, scale);
                break;
        }
    }

    /// <summary>Faísca de golpe conforme o formato (metal solta faíscas, magma brasas, pedra poeira).</summary>
    public void HitFx(Data.MaterialDef m, Vector3 pos, Vector3 normal)
    {
        switch (m.Archetype)
        {
            case Data.OreArchetype.Vein: EmitStyle("sparks", pos, normal, new Color(1f, 0.85f, 0.5f), 0.45f); break;
            case Data.OreArchetype.Magma: EmitStyle("embers", pos, normal, new Color(1f, 0.55f, 0.15f), 0.4f); break;
            case Data.OreArchetype.Banded: EmitStyle("dust", pos, normal, new Color(0.6f, 0.55f, 0.5f, 0.35f), 0.35f); break;
        }
        Hit(pos, normal, m.Glow.Lightened(0.3f));
    }
}
