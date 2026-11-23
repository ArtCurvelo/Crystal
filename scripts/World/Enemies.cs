using System;
using System.Collections.Generic;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Inimigo das Profundezas Proibidas: anda pela caverna (física de verdade), persegue o jogador quando o vê,
/// avisa antes de golpear (brilho + pose) e larga fragmentos ao morrer. Volta para casa se for longe demais e
/// renasce depois de um tempo, quando o jogador não está olhando.
/// </summary>
public partial class Enemy : CharacterBody3D
{
    public const uint EnemyLayer = 32;
    public enum State { Idle, Chase, Windup, Recover, Hurt, Dead }

    public EnemyDef Def;
    public Vector3 Home;
    public float Hp { get; private set; }
    public State Mode { get; private set; } = State.Idle;
    public bool Alive => Mode != State.Dead;
    public EnemyManager Manager;
    /// <summary>Filhote (divisão de um Lúmen Vivo): não renasce, some depois de morto.</summary>
    public bool Temporary;
    /// <summary>Chefão parado até a conversa de chegada terminar.</summary>
    public bool Dormant;
    int _attack;
    float _summonCd;
    /// <summary>Fase do chefão: 1 (cheio), 2 (abaixo de 2/3), 3 (abaixo de 1/3, enfurecido).</summary>
    public int Phase => Hp > Def.Hp * 0.66f ? 1 : Hp > Def.Hp * 0.33f ? 2 : 3;
    float Fury => Def.Boss ? (Phase == 3 ? 1.35f : Phase == 2 ? 1.15f : 1f) : 1f;

    Node3D _body, _armL, _armR, _head;
    readonly List<Node3D> _legs = new();
    StandardMaterial3D _glowMat, _skinMat;
    MeshInstance3D _ring;
    Node3D _bar;
    MeshInstance3D _barFill;
    float _t, _timer, _flash, _respawn, _wanderT, _barShow;
    Vector3 _wander, _knock;
    readonly Random _rng;
    static readonly Color FlashColor = new(1f, 1f, 1f);

    public Enemy(EnemyDef def, Vector3 home, int seed)
    {
        Def = def; Home = home; _rng = new Random(seed);
        Hp = def.Hp;
        CollisionLayer = EnemyLayer;
        CollisionMask = PlayerController.WorldLayer;
        FloorMaxAngle = Mathf.DegToRad(50);
        bool low = def.Kind is EnemyKind.Crawler or EnemyKind.Blob;
        float h = def.Kind switch { EnemyKind.Crawler => 0.9f, EnemyKind.Blob => 1.1f, EnemyKind.Ember or EnemyKind.Eye => 1.0f, EnemyKind.Root => 2.6f, EnemyKind.Spitter => 1.6f, EnemyKind.Chained or EnemyKind.Overseer or EnemyKind.Corvo => 1.8f, _ => 2.2f } * def.Scale;
        float r = def.Kind switch { EnemyKind.Crawler => 0.45f, EnemyKind.Ember or EnemyKind.Eye => 0.45f, EnemyKind.Chained or EnemyKind.Overseer or EnemyKind.Corvo => 0.35f, _ => 0.6f } * (low ? def.Scale : def.Scale);
        var shape = low && def.Kind == EnemyKind.Crawler ? (Shape3D)new CapsuleShape3D { Radius = 0.45f * def.Scale, Height = 0.9f * def.Scale } : new CapsuleShape3D { Radius = r, Height = Mathf.Max(h, r * 2.1f) };
        AddChild(new CollisionShape3D { Shape = shape, Position = new Vector3(0, def.Flying ? 0f : (def.Kind == EnemyKind.Crawler ? 0.45f * def.Scale : Mathf.Max(h, r * 2.1f) / 2f), 0), Rotation = def.Kind == EnemyKind.Crawler ? new Vector3(0, 0, Mathf.Pi / 2) : Vector3.Zero });
        if (def.Flying) MotionMode = MotionModeEnum.Floating;
        BuildBody();
    }
    public Enemy() : this(EnemyData.All[0], Vector3.Zero, 1) { }

    public override void _Ready() { GlobalPosition = Home; }

    // ------------------------------------------------------------------ aparência
    MeshInstance3D Part(Node3D p, Mesh m, Material mat, Vector3 pos, Vector3? rot = null, Vector3? scale = null)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One };
        p.AddChild(mi);
        return mi;
    }

    void BuildBody()
    {
        var glow = GameData.Mat(Def.Drop)?.Glow ?? new Color(0.5f, 1f, 0.9f);
        var skinColor = Def.Kind switch
        {
            EnemyKind.Crawler => new Color(0.16f, 0.14f, 0.2f), EnemyKind.Slag => new Color(0.18f, 0.15f, 0.14f), EnemyKind.Root or EnemyKind.Spitter => new Color(0.3f, 0.22f, 0.14f),
            EnemyKind.Blob => new Color(0.9f, 0.5f, 0.85f, 0.75f), EnemyKind.Eye => new Color(0.92f, 0.9f, 0.86f), EnemyKind.Ember => new Color(0.25f, 0.08f, 0.04f), _ => new Color(0.3f, 0.3f, 0.34f),
        };
        _skinMat = new StandardMaterial3D { AlbedoColor = skinColor, Roughness = Def.Kind == EnemyKind.Blob ? 0.15f : 0.7f, EmissionEnabled = true, Emission = FlashColor, EmissionEnergyMultiplier = 0f, Transparency = Def.Kind == EnemyKind.Blob ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled };
        _glowMat = new StandardMaterial3D { AlbedoColor = glow, EmissionEnabled = true, Emission = glow, EmissionEnergyMultiplier = 0.8f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.2f, 0.25f), EmissionEnabled = true, Emission = new Color(1f, 0.15f, 0.2f), EmissionEnergyMultiplier = 3.5f };
        _body = new Node3D { Scale = Vector3.One * Def.Scale };
        AddChild(_body);
        if (Def.Kind == EnemyKind.Crawler)
        {
            // corpo baixo e achatado, seis patas, espinhos de cristal nas costas
            Part(_body, new SphereMesh { Radius = 0.45f, Height = 0.6f, RadialSegments = 14, Rings = 7 }, _skinMat, new Vector3(0, 0.5f, -0.1f), null, new Vector3(1.0f, 0.75f, 1.35f));
            _head = new Node3D { Position = new Vector3(0, 0.52f, 0.5f) };
            _body.AddChild(_head);
            Part(_head, new SphereMesh { Radius = 0.26f, Height = 0.44f, RadialSegments = 12, Rings = 6 }, _skinMat, Vector3.Zero, null, new Vector3(1.1f, 0.85f, 1f));
            foreach (int s in new[] { -1, 1 })
            {
                Part(_head, new SphereMesh { Radius = 0.05f, Height = 0.1f }, eyeMat, new Vector3(s * 0.1f, 0.06f, 0.22f));
                Part(_head, new PrismMesh { Size = new Vector3(0.06f, 0.24f, 0.06f) }, _glowMat, new Vector3(s * 0.12f, -0.1f, 0.25f), new Vector3(Mathf.Pi * 0.6f, 0, s * 0.3f));
            }
            for (int i = 0; i < 5; i++)
                Part(_body, MeshFactory.CrystalPrism(5, 1, 1, 0.25f), _glowMat, new Vector3(((i % 2) * 2 - 1) * 0.12f, 0.75f, -0.45f + i * 0.17f), new Vector3(-0.35f + i * 0.08f, 0, ((i % 2) * 2 - 1) * 0.35f), new Vector3(0.1f, 0.38f + (i % 3) * 0.1f, 0.1f));
            for (int i = 0; i < 6; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                var leg = new Node3D { Position = new Vector3(side * 0.35f, 0.5f, -0.35f + (i / 2) * 0.33f) };
                _body.AddChild(leg);
                Part(leg, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.025f, Height = 0.6f, RadialSegments = 6 }, _skinMat, new Vector3(side * 0.22f, -0.12f, 0), new Vector3(0, 0, side * 1.05f));
                Part(leg, new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.01f, Height = 0.45f, RadialSegments = 6 }, _glowMat, new Vector3(side * 0.47f, -0.3f, 0), new Vector3(0, 0, -side * 0.35f));
                _legs.Add(leg);
            }
        }
        else if (Def.Kind is EnemyKind.Sentinel or EnemyKind.Slag)
        {
            // golem de pedra com núcleo de Lúmen no peito, braços enormes (escória: pedra escura com lava)
            var stone = MeshFactory.RockMaterial(Def.Kind == EnemyKind.Slag ? new Color(0.2f, 0.17f, 0.16f) : new Color(0.38f, 0.38f, 0.42f));
            Part(_body, MeshFactory.Rock(91, 0.3f), stone, new Vector3(0, 1.35f, 0), null, new Vector3(0.75f, 0.9f, 0.55f));
            Part(_body, new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 12, Rings = 6 }, _glowMat, new Vector3(0, 1.45f, 0.34f));
            _head = new Node3D { Position = new Vector3(0, 2.15f, 0.05f) };
            _body.AddChild(_head);
            Part(_head, MeshFactory.Rock(92, 0.25f), stone, Vector3.Zero, null, new Vector3(0.34f, 0.3f, 0.32f));
            foreach (int s in new[] { -1, 1 })
                Part(_head, new BoxMesh { Size = new Vector3(0.1f, 0.05f, 0.05f) }, eyeMat, new Vector3(s * 0.1f, 0.02f, 0.27f));
            Part(_head, MeshFactory.CrystalPrism(6, 1, 1, 0.3f), _glowMat, new Vector3(0, 0.25f, -0.05f), null, new Vector3(0.12f, 0.5f, 0.12f));
            foreach (int s in new[] { -1, 1 })
            {
                var arm = new Node3D { Position = new Vector3(s * 0.75f, 1.85f, 0) };
                _body.AddChild(arm);
                Part(arm, MeshFactory.Rock(93 + s, 0.3f), stone, new Vector3(0, -0.45f, 0), null, new Vector3(0.28f, 0.5f, 0.28f));
                Part(arm, MeshFactory.Rock(95 + s, 0.3f), stone, new Vector3(0, -1.05f, 0.1f), null, new Vector3(0.36f, 0.36f, 0.36f));
                Part(arm, MeshFactory.CrystalPrism(5, 1, 1, 0.3f), _glowMat, new Vector3(s * 0.12f, -1.0f, 0.3f), new Vector3(1.0f, 0, 0), new Vector3(0.08f, 0.35f, 0.08f));
                if (s < 0) _armL = arm; else _armR = arm;
            }
            foreach (int s in new[] { -1, 1 })
            {
                var leg = new Node3D { Position = new Vector3(s * 0.35f, 0.75f, 0) };
                _body.AddChild(leg);
                Part(leg, MeshFactory.Rock(97 + s, 0.3f), stone, new Vector3(0, -0.4f, 0), null, new Vector3(0.3f, 0.45f, 0.3f));
                _legs.Add(leg);
            }
            if (Def.Kind == EnemyKind.Slag)
                for (int i = 0; i < 6; i++)
                    Part(_body, new BoxMesh { Size = new Vector3(0.05f, 0.5f, 0.04f) }, _glowMat, new Vector3(-0.4f + i * 0.16f, 1.2f + (i % 2) * 0.25f, 0.4f), new Vector3(0, 0, (i % 3 - 1) * 0.5f));
        }
        else BuildOther(eyeMat);
        if (Def.Area > 0)
        {
            // anel no chão que avisa a área do golpe
            var center = Def.Exploder || Def.Stationary ? Vector3.Zero : new Vector3(0, 0, Def.Range * 0.55f);
            _ring = Part(this, new TorusMesh { InnerRadius = Def.Area - 0.12f, OuterRadius = Def.Area, Rings = 32, RingSegments = 4 },
                new StandardMaterial3D { AlbedoColor = new Color(1f, 0.25f, 0.2f, 0.8f), EmissionEnabled = true, Emission = new Color(1f, 0.2f, 0.15f), EmissionEnergyMultiplier = 2.5f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                center + new Vector3(0, Def.Flying ? -0.9f : 0.08f, 0));
            _ring.Visible = false;
        }
        if (Def.Guardian)
        {
            // coroa de cristais e nome em cima: é o Guardião da área
            var crown = new Node3D { Position = new Vector3(0, CrownHeight(), 0) };
            AddChild(crown);
            for (int i = 0; i < 7; i++)
            {
                float a = Mathf.Tau * i / 7f;
                crown.AddChild(new MeshInstance3D { Mesh = MeshFactory.CrystalPrism(5, 1, 1, 0.25f), MaterialOverride = _glowMat, Position = new Vector3(Mathf.Cos(a) * 0.35f, 0, Mathf.Sin(a) * 0.35f), Scale = new Vector3(0.08f, 0.35f + (i % 2) * 0.2f, 0.08f) });
            }
            crown.AddChild(new OmniLight3D { LightColor = _glowMat.AlbedoColor, LightEnergy = 1.4f, OmniRange = 7f });
            AddChild(new Label3D { Text = Def.Name.ToUpper(), FontSize = 64, OutlineSize = 12, PixelSize = 0.006f, Modulate = new Color(1f, 0.7f, 0.5f), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, CrownHeight() + 0.8f, 0), NoDepthTest = false });
        }
        // barra de vida (aparece depois de levar dano)
        _bar = new Node3D { Position = new Vector3(0, CrownHeight() + (Def.Guardian ? 0.5f : 0.2f), 0), Visible = false };
        AddChild(_bar);
        var bg = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.08f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
        var fg = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.3f, 0.3f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, RenderPriority = 1 };
        Part(_bar, new QuadMesh { Size = new Vector2(1.1f, 0.13f) }, bg, Vector3.Zero);
        _barFill = Part(_bar, new QuadMesh { Size = new Vector2(1.0f, 0.08f) }, fg, Vector3.Zero);
    }

    float CrownHeight() => (Def.Kind switch
    {
        EnemyKind.Crawler => 1.0f, EnemyKind.Blob => 1.2f, EnemyKind.Ember => 0.6f, EnemyKind.Eye => 0.8f, EnemyKind.Root => 2.9f, EnemyKind.Spitter => 1.8f,
        EnemyKind.Chained => 2.0f, EnemyKind.Overseer or EnemyKind.Corvo => 2.1f, _ => 2.6f,
    }) * Def.Scale;

    Humanoid _human;

    void BuildOther(StandardMaterial3D eyeMat)
    {
        switch (Def.Kind)
        {
            case EnemyKind.Ember:
            {
                // bola de fogo flutuante com casca de carvão rachada
                Part(_body, new SphereMesh { Radius = 0.42f, Height = 0.84f, RadialSegments = 14, Rings = 7 }, _skinMat, Vector3.Zero);
                Part(_body, new SphereMesh { Radius = 0.34f, Height = 0.68f, RadialSegments = 12, Rings = 6 }, _glowMat, new Vector3(0, 0, 0.12f));
                foreach (int sx in new[] { -1, 1 }) Part(_body, new SphereMesh { Radius = 0.06f, Height = 0.12f }, eyeMat, new Vector3(sx * 0.13f, 0.08f, 0.4f));
                var fade = new Gradient(); fade.SetColor(0, new Color(1f, 0.6f, 0.2f, 0.9f)); fade.SetColor(1, new Color(1f, 0.2f, 0.05f, 0f));
                _body.AddChild(new CpuParticles3D { Amount = 18, Lifetime = 0.7f, Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 6, Rings = 3 }, Direction = Vector3.Up, Spread = 30, Gravity = new Vector3(0, 2f, 0), InitialVelocityMin = 0.4f, InitialVelocityMax = 1f, ColorRamp = fade, EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 0.35f,
                    MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha } });
                _body.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 1.2f, OmniRange = 5f });
                break;
            }
            case EnemyKind.Root:
            {
                // tronco retorcido saindo do chão, galhos-chicote e espinhos de cristal
                for (int i = 0; i < 5; i++)
                    Part(_body, new CylinderMesh { TopRadius = 0.32f - i * 0.05f, BottomRadius = 0.4f - i * 0.05f, Height = 0.55f, RadialSegments = 8 }, _skinMat, new Vector3(Mathf.Sin(i * 1.3f) * 0.12f, 0.27f + i * 0.5f, 0), new Vector3(0, 0, Mathf.Sin(i * 1.7f) * 0.2f));
                foreach (int sx in new[] { -1, 1 })
                {
                    var whip = new Node3D { Position = new Vector3(sx * 0.25f, 1.9f, 0) };
                    _body.AddChild(whip);
                    for (int k = 0; k < 4; k++)
                        Part(whip, new CylinderMesh { TopRadius = 0.09f - k * 0.02f, BottomRadius = 0.11f - k * 0.02f, Height = 0.5f, RadialSegments = 6 }, _skinMat, new Vector3(sx * (0.25f + k * 0.4f), 0.1f + k * 0.12f, 0), new Vector3(0, 0, -sx * 1.2f));
                    if (sx < 0) _armL = whip; else _armR = whip;
                }
                for (int i = 0; i < 8; i++)
                    Part(_body, MeshFactory.CrystalPrism(5, 1, 1, 0.25f), _glowMat, new Vector3(Mathf.Cos(i * 0.8f) * 0.3f, 0.4f + i * 0.28f, Mathf.Sin(i * 0.8f) * 0.3f), new Vector3(Mathf.Sin(i) * 0.9f, 0, Mathf.Cos(i) * 0.9f), new Vector3(0.06f, 0.22f, 0.06f));
                _head = new Node3D { Position = new Vector3(0, 2.6f, 0) };
                _body.AddChild(_head);
                Part(_head, new SphereMesh { Radius = 0.3f, Height = 0.5f }, _glowMat, Vector3.Zero);
                for (int i = 0; i < 6; i++)
                {
                    float a = Mathf.Tau * i / 6f;
                    Part(_body, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.12f, Height = 1.1f, RadialSegments = 6 }, _skinMat, new Vector3(Mathf.Cos(a) * 0.55f, 0.15f, Mathf.Sin(a) * 0.55f), new Vector3(Mathf.Sin(a) * 1.25f, 0, -Mathf.Cos(a) * 1.25f));
                }
                break;
            }
            case EnemyKind.Spitter:
            {
                // planta-bulbo em pernas de raiz, boca que cospe farpas
                Part(_body, new SphereMesh { Radius = 0.55f, Height = 1.0f, RadialSegments = 14, Rings = 7 }, _skinMat, new Vector3(0, 1.05f, 0));
                _head = new Node3D { Position = new Vector3(0, 1.15f, 0.42f) };
                _body.AddChild(_head);
                Part(_head, new CylinderMesh { TopRadius = 0.28f, BottomRadius = 0.18f, Height = 0.35f, RadialSegments = 10 }, _skinMat, Vector3.Zero, new Vector3(Mathf.Pi / 2, 0, 0));
                Part(_head, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.2f, Height = 0.05f, RadialSegments = 10 }, eyeMat, new Vector3(0, 0, 0.18f), new Vector3(Mathf.Pi / 2, 0, 0));
                for (int i = 0; i < 6; i++)
                    Part(_body, MeshFactory.CrystalPrism(5, 1, 1, 0.25f), _glowMat, new Vector3(Mathf.Cos(i) * 0.4f, 1.4f + (i % 2) * 0.1f, Mathf.Sin(i) * 0.4f - 0.1f), new Vector3(Mathf.Sin(i) * 0.6f, 0, Mathf.Cos(i) * 0.6f), new Vector3(0.07f, 0.3f, 0.07f));
                for (int i = 0; i < 4; i++)
                {
                    var leg = new Node3D { Position = new Vector3((i % 2 == 0 ? -1 : 1) * 0.3f, 0.7f, (i < 2 ? -1 : 1) * 0.25f) };
                    _body.AddChild(leg);
                    Part(leg, new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.03f, Height = 0.8f, RadialSegments = 6 }, _skinMat, new Vector3(0, -0.35f, 0), new Vector3(0, 0, (i % 2 == 0 ? 1 : -1) * 0.35f));
                    _legs.Add(leg);
                }
                break;
            }
            case EnemyKind.Chained:
            case EnemyKind.Overseer:
            {
                // escravo corrompido: o mesmo corpo dos moradores, pele cinza, olhos vermelhos e correntes
                bool boss = Def.Kind == EnemyKind.Overseer;
                var look = new Look
                {
                    Skin = new Color(0.55f, 0.55f, 0.6f), Shirt = boss ? new Color(0.25f, 0.12f, 0.12f) : new Color(0.4f, 0.36f, 0.32f), Pants = new Color(0.22f, 0.2f, 0.22f),
                    Shoes = new Color(0.12f, 0.1f, 0.1f), Hair = new Color(0.15f, 0.12f, 0.15f), Accent = new Color(0.3f, 0.2f, 0.25f), HairStyle = boss ? 3 : 0, Beard = boss, Vest = boss, Hat = boss ? 3 : 0,
                };
                _human = new Humanoid(look, Def.Id.Length * 13) { Mode = Humanoid.Pose.Walk };
                _body.AddChild(_human);
                Part(_body, new SphereMesh { Radius = 0.03f, Height = 0.06f }, eyeMat, new Vector3(-0.05f, 1.72f, 0.13f));
                Part(_body, new SphereMesh { Radius = 0.03f, Height = 0.06f }, eyeMat, new Vector3(0.05f, 1.72f, 0.13f));
                var iron = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.43f, 0.42f), Metallic = 0.4f, Roughness = 0.5f };
                foreach (int sx in new[] { -1, 1 })
                {
                    Part(_body, new TorusMesh { InnerRadius = 0.06f, OuterRadius = 0.1f, Rings = 12, RingSegments = 6 }, iron, new Vector3(sx * 0.28f, 0.95f, 0.05f), new Vector3(Mathf.Pi / 2, 0, 0));
                    for (int k = 0; k < 4; k++)
                        Part(_body, new TorusMesh { InnerRadius = 0.03f, OuterRadius = 0.055f, Rings = 8, RingSegments = 4 }, iron, new Vector3(sx * (0.3f + k * 0.02f), 0.82f - k * 0.1f, 0.05f), new Vector3(k % 2 == 0 ? 0 : Mathf.Pi / 2, 0, 0));
                }
                Part(_body, new TorusMesh { InnerRadius = 0.09f, OuterRadius = 0.13f, Rings = 12, RingSegments = 6 }, iron, new Vector3(0, 1.48f, 0));
                for (int i = 0; i < 4; i++)
                    Part(_body, MeshFactory.CrystalPrism(5, 1, 1, 0.25f), _glowMat, new Vector3(-0.12f + i * 0.08f, 1.35f, -0.14f), new Vector3(-0.6f, 0, 0), new Vector3(0.04f, 0.18f, 0.04f));
                if (boss)
                {
                    // chicote de corrente
                    var whip = new Node3D();
                    _human.Hold(whip);
                    for (int k = 0; k < 10; k++)
                        whip.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.025f, OuterRadius = 0.045f, Rings = 8, RingSegments = 4 }, MaterialOverride = _glowMat, Position = new Vector3(0, -0.05f, 0.08f + k * 0.12f), Rotation = new Vector3(k % 2 == 0 ? 0 : Mathf.Pi / 2, 0, 0) });
                }
                break;
            }
            case EnemyKind.Corvo:
            {
                // o ferreiro corrompido: pele cinza-avermelhada rachada de brasa, barba preta, cristais nas costas, martelo enorme
                var look = new Look
                {
                    Skin = new Color(0.45f, 0.32f, 0.3f), Shirt = new Color(0.12f, 0.1f, 0.12f), Pants = new Color(0.1f, 0.08f, 0.09f), Shoes = new Color(0.06f, 0.05f, 0.05f),
                    Hair = new Color(0.05f, 0.04f, 0.05f), Accent = new Color(0.08f, 0.06f, 0.07f), HairStyle = 3, Beard = true, Apron = true, Hat = 3, Width = 1.25f,
                };
                _human = new Humanoid(look, 666) { Mode = Humanoid.Pose.Idle };
                _body.AddChild(_human);
                foreach (int sx in new[] { -1, 1 })
                    Part(_body, new SphereMesh { Radius = 0.03f, Height = 0.06f }, eyeMat, new Vector3(sx * 0.05f, 1.72f, 0.13f));
                for (int i = 0; i < 9; i++)
                    Part(_body, new BoxMesh { Size = new Vector3(0.015f, 0.18f + (i % 3) * 0.06f, 0.01f) }, _glowMat, new Vector3(-0.16f + i * 0.04f, 1.15f + (i % 2) * 0.12f, 0.17f), new Vector3(0, 0, (i % 3 - 1) * 0.6f));
                for (int i = 0; i < 7; i++)
                    Part(_body, MeshFactory.CrystalPrism(5, 1, 1, 0.25f), _glowMat, new Vector3(-0.18f + i * 0.06f, 1.3f + (i % 2) * 0.1f, -0.15f), new Vector3(-0.7f, 0, (i - 3) * 0.2f), new Vector3(0.04f, 0.2f + (i % 3) * 0.08f, 0.04f));
                // martelo: cabo comprido e cabeça de Pedra-Bigorna em brasa
                var hammer = new Node3D();
                _human.Hold(hammer);
                var dark = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.12f, 0.12f), Roughness = 0.5f, Metallic = 0.4f };
                hammer.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.025f, Height = 0.8f, RadialSegments = 6 }, MaterialOverride = dark, Position = new Vector3(0, -0.1f, 0.3f), Rotation = new Vector3(Mathf.Pi / 2, 0, 0) });
                hammer.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.32f, 0.2f, 0.2f) }, MaterialOverride = dark, Position = new Vector3(0, -0.1f, 0.72f) });
                hammer.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.04f, 0.22f) }, MaterialOverride = _glowMat, Position = new Vector3(0, -0.1f, 0.72f) });
                _body.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.3f, 0.2f), LightEnergy = 0.7f, OmniRange = 4f, Position = new Vector3(0, 2.3f, -0.5f) });
                break;
            }
            case EnemyKind.Blob:
            {
                // gota de Lúmen gelatinosa com núcleo e bolhas
                var jelly = Part(_body, new SphereMesh { Radius = 0.62f, Height = 1.0f, RadialSegments = 16, Rings = 8 }, _skinMat, new Vector3(0, 0.5f, 0));
                _head = new Node3D { Position = new Vector3(0, 0.5f, 0) };
                _body.AddChild(_head);
                Part(_head, new SphereMesh { Radius = 0.22f, Height = 0.44f }, _glowMat, Vector3.Zero);
                Part(_head, new SphereMesh { Radius = 0.07f, Height = 0.14f }, eyeMat, new Vector3(-0.12f, 0.15f, 0.48f));
                Part(_head, new SphereMesh { Radius = 0.07f, Height = 0.14f }, eyeMat, new Vector3(0.12f, 0.15f, 0.48f));
                for (int i = 0; i < 5; i++)
                    Part(_body, new SphereMesh { Radius = 0.06f + i * 0.015f, Height = 0.12f + i * 0.03f }, _glowMat, new Vector3(Mathf.Cos(i * 1.3f) * 0.3f, 0.35f + i * 0.08f, Mathf.Sin(i * 1.3f) * 0.3f));
                break;
            }
            case EnemyKind.Eye:
            {
                // olho voador com íris acesa e tentáculos pendurados
                Part(_body, new SphereMesh { Radius = 0.5f, Height = 1.0f, RadialSegments = 16, Rings = 8 }, _skinMat, Vector3.Zero);
                _head = new Node3D();
                _body.AddChild(_head);
                Part(_head, new SphereMesh { Radius = 0.24f, Height = 0.2f, RadialSegments = 14, Rings = 6 }, _glowMat, new Vector3(0, 0, 0.42f));
                Part(_head, new SphereMesh { Radius = 0.11f, Height = 0.1f }, new StandardMaterial3D { AlbedoColor = new Color(0.03f, 0.02f, 0.03f) }, new Vector3(0, 0, 0.52f));
                for (int i = 0; i < 6; i++)
                {
                    var leg = new Node3D { Position = new Vector3(Mathf.Cos(i * 1.05f) * 0.25f, -0.35f, Mathf.Sin(i * 1.05f) * 0.25f - 0.1f) };
                    _body.AddChild(leg);
                    for (int k = 0; k < 3; k++)
                        Part(leg, new CylinderMesh { TopRadius = 0.05f - k * 0.012f, BottomRadius = 0.04f - k * 0.012f, Height = 0.35f, RadialSegments = 6 }, k == 2 ? _glowMat : _skinMat, new Vector3(0, -0.15f - k * 0.3f, 0));
                    _legs.Add(leg);
                }
                _body.AddChild(new OmniLight3D { LightColor = _glowMat.AlbedoColor, LightEnergy = 1.0f, OmniRange = 5f, Position = new Vector3(0, 0, 0.7f) });
                break;
            }
        }
    }

    // ------------------------------------------------------------------ dano
    /// <summary>Leva um golpe: pisca, é empurrado e, ao zerar, morre largando fragmentos.</summary>
    public bool TakeHit(float dmg, Vector3 from, float knockback)
    {
        if (!Alive) return false;
        Hp -= dmg;
        _flash = 1f;
        _barShow = 6f;
        var push = GlobalPosition - from; push.Y = 0;
        if (push.LengthSquared() > 0.001f) _knock = push.Normalized() * knockback / Mathf.Max(1f, Def.Scale);
        Effects.Instance?.Hit(GlobalPosition + Vector3.Up * 0.8f * Def.Scale, push.Normalized(), _glowMat.AlbedoColor);
        Effects.Instance?.FloatText(GlobalPosition + Vector3.Up * (1.4f * Def.Scale + 0.4f), GameState.FormatNumber(dmg), new Color(1f, 0.9f, 0.5f), 1.1f);
        Audio.AudioManager.Instance?.Play("thud", -6f, 1.2f, 0.15f);
        if (Hp <= 0) { Die(); return true; }
        if (Mode != State.Windup) { Mode = State.Hurt; _timer = 0.25f; }
        return true;
    }

    void Die()
    {
        Mode = State.Dead;
        Hp = 0;
        Visible = false;
        CollisionLayer = 0;
        _respawn = Def.Boss ? float.MaxValue : 150f;
        Effects.Instance?.Burst(GlobalPosition + Vector3.Up * 0.6f * Def.Scale, _glowMat.AlbedoColor, 1.4f * Def.Scale);
        Audio.AudioManager.Instance?.Play("break", -2f, 0.8f, 0.1f);
        var drop = GameData.Mat(Def.Drop);
        if (drop != null) Manager?.SpawnShards?.Invoke(drop, GlobalPosition + Vector3.Up * 0.6f, _rng.Next(Def.DropMin, Def.DropMax + 1));
        if (Def.RareDrop != null && _rng.NextDouble() < Def.RareChance && GameData.Mat(Def.RareDrop) is { } rare)
            Manager?.SpawnShards?.Invoke(rare, GlobalPosition + Vector3.Up * 0.8f, 1);
        if (Def.SplitInto != null && EnemyData.Of(Def.SplitInto) is { } child)
            for (int i = 0; i < Def.SplitCount; i++)
            {
                float a = i * Mathf.Tau / Def.SplitCount + Rotation.Y;
                Manager?.SpawnTemporary(child, GlobalPosition + new Vector3(Mathf.Cos(a), 0.3f, Mathf.Sin(a)) * 1.2f);
            }
        Manager?.OnKilled(this);
        if (Temporary) QueueFree();
    }

    /// <summary>Chefão já derrotado em outra sessão: some sem largar nada.</summary>
    public void Retire()
    {
        Mode = State.Dead;
        Hp = 0;
        Visible = false;
        CollisionLayer = 0;
        _respawn = float.MaxValue;
    }

    void Respawn()
    {
        Hp = Def.Hp;
        Mode = State.Idle;
        GlobalPosition = Home;
        Velocity = Vector3.Zero;
        CollisionLayer = EnemyLayer;
        Visible = true;
        _bar.Visible = false;
    }

    // ------------------------------------------------------------------ comportamento
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var player = Manager?.Player;
        if (Mode == State.Dead)
        {
            _respawn -= dt;
            if (_respawn <= 0 && (player == null || player.GlobalPosition.DistanceTo(Home) > 30f)) Respawn();
            return;
        }
        // longe do jogador: dorme (economiza CPU)
        float pd = player != null ? player.GlobalPosition.DistanceTo(GlobalPosition) : 999f;
        if (pd > 60f || !IsVisibleInTree()) return;
        _t += dt;
        _flash = Mathf.Max(0, _flash - dt * 5f);
        _skinMat.EmissionEnergyMultiplier = _flash * 2.5f;
        if (_barShow > 0) { _barShow -= dt; _bar.Visible = true; _barFill.Scale = new Vector3(Mathf.Max(0.01f, Hp / Def.Hp), 1, 1); _barFill.Position = new Vector3(-(1f - Hp / Def.Hp) * 0.5f, 0, 0.001f); }
        else _bar.Visible = false;

        var vel = Velocity;
        if (Def.Flying)
        {
            // paira na altura do peito de quem persegue (ou um pouco acima de casa)
            float wantY = (Mode is State.Chase or State.Windup or State.Recover) && player != null ? player.GlobalPosition.Y + 1.3f : Home.Y + 1.6f + Mathf.Sin(_t * 1.3f) * 0.3f;
            vel.Y = Mathf.Clamp((wantY - GlobalPosition.Y) * 2.5f, -4f, 4f);
        }
        else vel.Y = IsOnFloor() ? -0.5f : vel.Y - 18f * dt;
        Vector3 want = Vector3.Zero;
        var toP = player != null ? player.GlobalPosition - GlobalPosition : Vector3.Zero;
        toP.Y = 0;
        bool sees = player != null && player.Hp > 0 && pd < Def.Aggro && Manager.CanSee(this, player) && !Dormant;
        float home = GlobalPosition.DistanceTo(Home);
        if (Def.Boss) home = Mathf.Min(home, 20f); // o chefão não desiste da arena (só se o jogador sair dela)
        if (_summonCd > 0) _summonCd -= dt;
        switch (Mode)
        {
            case State.Idle:
                if (sees && home < 28f) { Mode = State.Chase; break; }
                // passeia perto de casa
                _wanderT -= dt;
                if (_wanderT <= 0) { _wanderT = 2f + (float)_rng.NextDouble() * 3f; float a = (float)_rng.NextDouble() * Mathf.Tau; _wander = Home + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (float)_rng.NextDouble() * 4f; }
                var tw = _wander - GlobalPosition; tw.Y = 0;
                if (tw.Length() > 0.4f && !Def.Stationary) want = tw.Normalized() * Def.Speed * 0.35f;
                break;
            case State.Chase:
                if (home > 30f || !sees && pd > Def.Aggro * 1.6f) { Mode = State.Idle; _wander = Home; Hp = Mathf.Min(Def.Hp, Hp + Def.Hp * 0.5f); break; }
                if (Def.Boss && sees && BossRanged(toP.Length())) { Mode = State.Windup; _timer = Def.Windup / Fury; break; }
                if (toP.Length() <= Def.Range && (Def.Projectile <= 0 || sees)) { Mode = State.Windup; _timer = Def.Windup / Fury; break; }
                if (!Def.Stationary) want = toP.Normalized() * Def.Speed * Fury;
                break;
            case State.Windup:
                _timer -= dt;
                if (_timer <= 0) { Strike(player); if (Mode == State.Dead) return; Mode = State.Recover; _timer = Def.Cooldown / Fury; }
                break;
            case State.Recover:
                _timer -= dt;
                if (_timer <= 0) Mode = State.Chase;
                break;
            case State.Hurt:
                _timer -= dt;
                if (_timer <= 0) Mode = State.Chase;
                break;
        }
        // vira para quem persegue / para onde anda
        var face = Mode is State.Chase or State.Windup or State.Recover or State.Hurt ? toP : want;
        if (face.LengthSquared() > 0.01f)
            Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(face.X, face.Z), Mathf.Min(1f, dt * (Mode == State.Windup ? 2f : 8f))), 0);
        if (Def.Stationary) _knock = Vector3.Zero;
        vel.X = want.X + _knock.X;
        vel.Z = want.Z + _knock.Z;
        _knock = _knock.Lerp(Vector3.Zero, Mathf.Min(1f, dt * 6f));
        // parede na frente: tenta contornar de lado
        if (IsOnWall() && want.LengthSquared() > 0.01f)
        {
            var side = want.Cross(Vector3.Up).Normalized() * Def.Speed * (((int)(_t * 0.5f)) % 2 == 0 ? 1 : -1);
            vel.X += side.X * 0.8f; vel.Z += side.Z * 0.8f;
        }
        Velocity = vel;
        MoveAndSlide();
        if (GlobalPosition.Y < Home.Y - 30f) { if (Temporary) QueueFree(); else Respawn(); }
        Animate(dt, new Vector2(vel.X, vel.Z).Length());
    }

    /// <summary>Ataque à distância do chefão: rajada em leque (todas as fases) e crias (fase 2+), alternando.</summary>
    bool BossRanged(float dist)
    {
        if (dist <= Def.Range) { _attack = 0; return false; }
        if (dist > 22f) return false;
        _attack = Phase >= 2 && _summonCd <= 0 ? 2 : 1;
        return true;
    }

    void Strike(PlayerController player)
    {
        if (player == null || player.Hp <= 0) return;
        if (Def.Boss && _attack == 1)
        {
            // rajada: 5 raios em leque (7 enfurecido)
            int n = Phase == 3 ? 7 : 5;
            var from = GlobalPosition + Vector3.Up * 2.6f;
            var dir = (player.GlobalPosition + Vector3.Up * 1.1f - from).Normalized();
            for (int i = 0; i < n; i++)
            {
                float a = (i - (n - 1) / 2f) * 0.16f;
                var fd = dir.Rotated(Vector3.Up, a);
                Manager?.Fire(from, from + fd * 20f, 15f, Def.Damage * 0.5f * (Manager?.DamageTakenMultiplier?.Invoke() ?? 1f), new Color(1f, 0.25f, 0.2f));
            }
            Audio.AudioManager.Instance?.Play("swing", -2f, 0.6f, 0.05f);
            _attack = 0;
            return;
        }
        if (Def.Boss && _attack == 2)
        {
            // chama crias de Lúmen em volta
            if (EnemyData.Of("crowling") is { } cr)
                for (int i = 0; i < 3; i++)
                {
                    float a = Rotation.Y + (i - 1) * 1.2f;
                    Manager?.SpawnTemporary(cr, GlobalPosition + new Vector3(Mathf.Sin(a), 0.4f, Mathf.Cos(a)) * 3f);
                }
            Effects.Instance?.Burst(GlobalPosition + Vector3.Up * 2f, new Color(1f, 0.3f, 0.25f), 3f);
            _summonCd = 14f;
            _attack = 0;
            return;
        }
        if (Def.Projectile > 0)
        {
            // farpa / raio: vai até onde o jogador estava (dá para desviar andando de lado)
            var from = GlobalPosition + Vector3.Up * (Def.Flying ? 0f : 1.2f * Def.Scale) + GlobalTransform.Basis.Z * 0.6f;
            Manager?.Fire(from, player.GlobalPosition + Vector3.Up * 1.1f, Def.Projectile, Def.Damage * (Manager?.DamageTakenMultiplier?.Invoke() ?? 1f), _glowMat.AlbedoColor);
            Audio.AudioManager.Instance?.Play("swing", -6f, 1.6f, 0.1f);
            return;
        }
        var center = Def.Area > 0 ? GlobalPosition + GlobalTransform.Basis.Z * Def.Range * 0.55f : GlobalPosition;
        var d = player.GlobalPosition - center; d.Y = 0;
        float reach = Def.Area > 0 ? Def.Area : Def.Range * 1.25f;
        float dy = Mathf.Abs(player.GlobalPosition.Y - GlobalPosition.Y);
        if (Def.Area > 0)
        {
            Effects.Instance?.Burst(center + Vector3.Up * 0.2f, new Color(1f, 0.5f, 0.3f), 1.6f);
            Audio.AudioManager.Instance?.Play("thud", -1f, 0.6f, 0.05f);
        }
        if (d.Length() <= reach && dy < (Def.Flying ? 3.5f : 2.5f))
            player.TakeDamage(Def.Damage * (Manager?.DamageTakenMultiplier?.Invoke() ?? 1f), GlobalPosition);
        if (Def.Exploder)
        {
            // a brasa se desfaz na explosão (larga o que tinha)
            Effects.Instance?.Burst(GlobalPosition, new Color(1f, 0.55f, 0.2f), 2.4f);
            Die();
        }
    }

    void Animate(float dt, float speed)
    {
        bool windup = Mode == State.Windup;
        float k = windup ? 1f - _timer / Mathf.Max(0.01f, Def.Windup) : 0f;
        _glowMat.EmissionEnergyMultiplier = 0.8f + (windup ? k * 3f : 0f);
        if (_ring != null) _ring.Visible = windup;
        switch (Def.Kind)
        {
            case EnemyKind.Ember:
                _body.Scale = Vector3.One * Def.Scale * (1f + (windup ? k * 0.35f : Mathf.Sin(_t * 6f) * 0.05f));
                _body.Rotation = new Vector3(0, _t * 2f, 0);
                return;
            case EnemyKind.Eye:
                _body.Position = new Vector3(0, Mathf.Sin(_t * 2f) * 0.08f, 0);
                for (int i = 0; i < _legs.Count; i++) _legs[i].Rotation = new Vector3(Mathf.Sin(_t * 3f + i) * 0.3f, 0, Mathf.Cos(_t * 2.5f + i) * 0.3f);
                _head.Scale = Vector3.One * (1f + (windup ? k * 0.3f : 0f));
                return;
            case EnemyKind.Blob:
                float sq = Mathf.Sin(_t * (4f + speed * 2f)) * 0.08f + (windup ? -0.25f * k : 0f);
                _body.Scale = new Vector3(1f - sq * 0.6f, 1f + sq, 1f - sq * 0.6f) * Def.Scale;
                return;
            case EnemyKind.Root:
            {
                float sw = windup ? -1.2f * k : Mode == State.Recover && _timer > Def.Cooldown - 0.25f ? 1.4f : Mathf.Sin(_t * 1.5f) * 0.15f;
                _armL.Rotation = new Vector3(0, sw * 1.5f, sw * 0.3f);
                _armR.Rotation = new Vector3(0, -sw * 1.5f, -sw * 0.3f);
                _body.Rotation = new Vector3(Mathf.Sin(_t * 0.9f) * 0.04f, 0, Mathf.Cos(_t * 0.7f) * 0.04f);
                return;
            }
            case EnemyKind.Spitter:
                for (int i = 0; i < _legs.Count; i++) _legs[i].Rotation = new Vector3(Mathf.Sin(_t * 6f + i * 1.6f) * 0.3f * Mathf.Min(1f, speed), 0, 0);
                _head.Scale = Vector3.One * (1f + (windup ? k * 0.4f : 0f));
                _body.Position = new Vector3(0, windup ? -0.1f * k : 0f, 0);
                return;
            case EnemyKind.Corvo:
            case EnemyKind.Chained:
            case EnemyKind.Overseer:
                _human.WalkSpeed = Mathf.Max(0.6f, speed * 0.8f);
                _human.Mode = windup ? Humanoid.Pose.Work : speed > 0.3f ? Humanoid.Pose.Walk : Humanoid.Pose.Idle;
                return;
        }
        if (Def.Kind == EnemyKind.Crawler)
        {
            float ph = _t * (6f + speed * 3f);
            for (int i = 0; i < _legs.Count; i++)
                _legs[i].Rotation = new Vector3(Mathf.Sin(ph + i * 1.7f) * 0.35f * Mathf.Min(1f, speed), 0, 0);
            // agacha no preparo e dá o bote
            _body.Position = new Vector3(0, windup ? -0.12f * k : Mathf.Abs(Mathf.Sin(ph)) * 0.03f, Mode == State.Recover && _timer > Def.Cooldown - 0.2f ? 0.35f : 0f);
            _head.Rotation = new Vector3(windup ? -0.4f * k : 0f, 0, 0);
        }
        else
        {
            float ph = _t * 3f;
            for (int i = 0; i < _legs.Count; i++)
                _legs[i].Rotation = new Vector3(Mathf.Sin(ph + i * Mathf.Pi) * 0.4f * Mathf.Min(1f, speed), 0, 0);
            // levanta os dois braços e bate no chão
            float arm = windup ? -2.6f * k : Mode == State.Recover && _timer > Def.Cooldown - 0.3f ? 0.4f : Mathf.Sin(ph) * 0.2f * Mathf.Min(1f, speed);
            _armL.Rotation = new Vector3(arm, 0, 0.15f);
            _armR.Rotation = new Vector3(arm, 0, -0.15f);
            _body.Position = new Vector3(0, Mathf.Abs(Mathf.Sin(ph)) * 0.04f * Mathf.Min(1f, speed), 0);
        }
    }
}

/// <summary>Cuida dos inimigos: nasce nos salões das Profundezas, recebe os golpes do jogador, conta abates.</summary>
public partial class EnemyManager : Node3D
{
    public readonly List<Enemy> Enemies = new();
    /// <summary>O Mestre Corvo (null se a área ainda não existe).</summary>
    public Enemy Boss => Enemies.Find(e => e.Def.Boss);
    /// <summary>Luta em andamento: barra do chefão no topo da tela.</summary>
    public bool BossEngaged => Boss is { } b && b.Alive && !b.Dormant && b.Mode != Enemy.State.Idle && b.Visible && b.IsVisibleInTree();
    public PlayerController Player;
    public Action<MaterialDef, Vector3, int> SpawnShards;
    public Func<float> DamageTakenMultiplier;
    public event Action<Enemy> Killed;
    PhysicsRayQueryParameters3D _ray;

    readonly Dictionary<string, Node3D> _groups = new();
    /// <summary>Um nó por Profundeza: só a área onde o jogador está fica visível (e processa).</summary>
    Node3D Group(string region)
    {
        region ??= "";
        if (_groups.TryGetValue(region, out var g)) return g;
        g = new Node3D { Name = "Group_" + region };
        AddChild(g);
        _groups[region] = g;
        return g;
    }

    public void ShowOnly(string region)
    {
        foreach (var kv in _groups) { bool v = kv.Key == region; if (kv.Value.Visible != v) kv.Value.Visible = v; }
    }

    public Enemy Spawn(EnemyDef def, Vector3 pos, int seed)
    {
        var e = new Enemy(def, pos, seed) { Manager = this, Name = $"Enemy_{def.Id}_{Enemies.Count}" };
        Group(def.Region).AddChild(e);
        Enemies.Add(e);
        return e;
    }

    public void OnKilled(Enemy e) => Killed?.Invoke(e);

    /// <summary>Filhote de divisão: nasce já perseguindo e some ao morrer.</summary>
    public Enemy SpawnTemporary(EnemyDef def, Vector3 pos)
    {
        var e = new Enemy(def, pos, Enemies.Count * 31 + 5) { Manager = this, Temporary = true, Name = $"Enemy_{def.Id}_t{Enemies.Count}" };
        Group(def.Region).AddChild(e);
        Enemies.Add(e);
        e.TreeExiting += () => Enemies.Remove(e);
        return e;
    }

    public void Fire(Vector3 from, Vector3 to, float speed, float damage, Color color)
    {
        var p = new EnemyProjectile { Manager = this, Velocity = (to - from).Normalized() * speed, Damage = damage, Color = color };
        AddChild(p);
        p.GlobalPosition = from;
    }

    /// <summary>Linha de visão (sem parede no meio) do inimigo até o jogador.</summary>
    public bool CanSee(Enemy e, PlayerController p)
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return true;
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = PlayerController.WorldLayer };
        _ray.From = e.GlobalPosition + Vector3.Up * 1.0f;
        _ray.To = p.GlobalPosition + Vector3.Up * 1.2f;
        _ray.Exclude = new Godot.Collections.Array<Rid> { e.GetRid(), p.GetRid() };
        return space.IntersectRay(_ray).Count == 0;
    }

    /// <summary>Golpe de arma: acerta todos os inimigos vivos no cone à frente. Devolve quantos acertou.</summary>
    public int HitCone(Vector3 origin, Vector3 forward, float reach, float arcDeg, float damage, float knockback)
    {
        int n = 0;
        forward.Y = 0;
        if (forward.LengthSquared() < 0.001f) return 0;
        forward = forward.Normalized();
        float cos = Mathf.Cos(Mathf.DegToRad(arcDeg));
        foreach (var e in Enemies.ToArray())
        {
            if (!IsInstanceValid(e) || !e.Alive || !e.Visible) continue;
            var d = e.GlobalPosition - origin;
            float dy = Mathf.Abs(d.Y);
            d.Y = 0;
            float r = reach + 0.45f * e.Def.Scale;
            if (d.Length() > r || dy > 2.6f) continue;
            if (d.Length() > 0.4f && d.Normalized().Dot(forward) < cos) continue;
            if (e.TakeHit(damage, origin, knockback)) n++;
        }
        return n;
    }

    public Enemy NearestAlive(Vector3 p, float max)
    {
        Enemy best = null; float bd = max;
        foreach (var e in Enemies) { if (!e.Alive) continue; float d = e.GlobalPosition.DistanceTo(p); if (d < bd) { bd = d; best = e; } }
        return best;
    }
}

/// <summary>Portal de volta, na entrada de cada Profundeza: leva ao portão selado da Catedral.</summary>
public partial class DeepPortal : StationBase
{
    public RegionDef Region;
    OmniLight3D _light;
    MeshInstance3D _swirl;

    public override void _Ready()
    {
        SetupBody(new Vector3(3.6f, 4.2f, 0.8f));
        var stone = MeshFactory.RockMaterial(new Color(0.5f, 0.5f, 0.56f));
        foreach (int s in new[] { -1, 1 })
            AddMesh(new BoxMesh { Size = new Vector3(0.7f, 4.0f, 0.9f) }, stone, new Vector3(s * 1.6f, 2.0f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(4.0f, 0.7f, 1.0f) }, stone, new Vector3(0, 4.2f, 0));
        _swirl = AddMesh(new BoxMesh { Size = new Vector3(2.5f, 3.6f, 0.05f) }, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.4f, 0.9f, 1f, 0.55f), EmissionEnabled = true, Emission = new Color(0.4f, 0.85f, 1f), EmissionEnergyMultiplier = 1.8f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        }, new Vector3(0, 1.9f, 0));
        _light = new OmniLight3D { LightColor = new Color(0.45f, 0.85f, 1f), LightEnergy = 1.6f, OmniRange = 9f, Position = new Vector3(0, 2f, 1f) };
        AddChild(_light);
        Label = MakeLabel("SAÍDA · CATEDRAL", new Color(0.6f, 0.9f, 1f), 5.0f, 52);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsVisibleInTree()) return;
        _light.LightEnergy = 1.6f + 0.4f * Mathf.Sin(Time * 2.5f);
        _swirl.Scale = new Vector3(1f + 0.03f * Mathf.Sin(Time * 3f), 1f, 1f);
    }

    public override string GetPrompt() => Loc.F("{0} Voltar ao portão da Catedral", InputHints.Interact);
    public override void Interact(PlayerController player) => UiBus.RequestDeepExit(Region?.Id);
}

/// <summary>Passagem para a próxima Profundeza, no ponto mais fundo da área: abre quando o Guardião cai.</summary>
public partial class DeepDescent : StationBase
{
    public RegionDef Region;
    MeshInstance3D _seal;
    OmniLight3D _light;

    public override void _Ready()
    {
        SetupBody(new Vector3(3.4f, 0.6f, 3.4f));
        var stone = MeshFactory.RockMaterial(new Color(0.42f, 0.4f, 0.46f));
        // poço circular com escada em espiral descendo e um selo de Lúmen por cima
        for (int i = 0; i < 14; i++)
        {
            float a = Mathf.Tau * i / 14f;
            AddMesh(new BoxMesh { Size = new Vector3(0.8f, 0.55f, 0.45f) }, stone, new Vector3(Mathf.Cos(a) * 1.75f, 0.27f, Mathf.Sin(a) * 1.75f)).Rotation = new Vector3(0, -a + Mathf.Pi / 2, 0);
        }
        AddMesh(new CylinderMesh { TopRadius = 1.5f, BottomRadius = 1.5f, Height = 0.05f, RadialSegments = 24 }, new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.01f, 0.02f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, new Vector3(0, 0.1f, 0));
        _seal = AddMesh(new CylinderMesh { TopRadius = 1.45f, BottomRadius = 1.45f, Height = 0.06f, RadialSegments = 24 }, new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.35f, 0.3f, 0.6f), EmissionEnabled = true, Emission = new Color(1f, 0.3f, 0.25f), EmissionEnergyMultiplier = 1.6f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        }, new Vector3(0, 0.5f, 0));
        _light = new OmniLight3D { LightColor = new Color(1f, 0.35f, 0.3f), LightEnergy = 1.4f, OmniRange = 8f, Position = new Vector3(0, 1.2f, 0) };
        AddChild(_light);
        Label = MakeLabel("PASSAGEM", new Color(1f, 0.7f, 0.6f), 2.2f, 52);
    }

    bool Open => GameState.Instance?.GuardianDefeated(Region?.Id) ?? false;

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsVisibleInTree()) return;
        bool open = Open;
        _seal.Visible = !open;
        _light.LightColor = open ? new Color(0.5f, 0.9f, 1f) : new Color(1f, 0.35f, 0.3f);
        _light.LightEnergy = 1.4f + 0.4f * Mathf.Sin(Time * 2f);
    }

    public override string GetPrompt() => Open
        ? Loc.F("{0} Descer para a próxima Profundeza", InputHints.Interact)
        : Loc.F("{0} Passagem lacrada (derrote o Guardião)", InputHints.Interact);

    public override void Interact(PlayerController player) => UiBus.RequestDescend(Region?.Id);
}

/// <summary>Farpa de cristal / raio de Lúmen: voa em linha reta, machuca o jogador e some ao bater na rocha.</summary>
public partial class EnemyProjectile : Node3D
{
    public EnemyManager Manager;
    public Vector3 Velocity;
    public float Damage;
    public Color Color = new(0.6f, 1f, 0.5f);
    float _life = 4f;
    PhysicsRayQueryParameters3D _ray;

    public override void _Ready()
    {
        var mat = new StandardMaterial3D { AlbedoColor = Color, EmissionEnabled = true, Emission = Color, EmissionEnergyMultiplier = 3f, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.09f, Height = 0.55f, RadialSegments = 8, Rings = 2 }, MaterialOverride = mat, Rotation = new Vector3(Mathf.Pi / 2, 0, 0) });
        AddChild(new OmniLight3D { LightColor = Color, LightEnergy = 1.2f, OmniRange = 3.5f });
        if (Velocity.LengthSquared() > 0.01f) LookAt(GlobalPosition + Velocity, Vector3.Up);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _life -= dt;
        var step = Velocity * dt;
        var space = GetWorld3D().DirectSpaceState;
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = PlayerController.WorldLayer };
        _ray.From = GlobalPosition; _ray.To = GlobalPosition + step;
        bool wall = space.IntersectRay(_ray).Count > 0;
        GlobalPosition += step;
        var p = Manager?.Player;
        if (p != null && p.Hp > 0 && (p.GlobalPosition + Vector3.Up * 1.0f).DistanceTo(GlobalPosition) < 0.8f)
        {
            p.TakeDamage(Damage, GlobalPosition - Velocity.Normalized());
            Effects.Instance?.Hit(GlobalPosition, -Velocity.Normalized(), Color);
            QueueFree();
            return;
        }
        if (wall || _life <= 0)
        {
            Effects.Instance?.Hit(GlobalPosition, -Velocity.Normalized(), Color);
            QueueFree();
        }
    }
}
