using System;
using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.World;

/// <summary>Aparência de um morador (sorteada por semente fixa, nunca pelo gerador do mundo).</summary>
public sealed class Look
{
    public Color Skin, Shirt, Pants, Shoes, Hair, Accent;
    public int HairStyle;   // 0 curto, 1 comprido, 2 coque, 3 careca, 4 rabo de cavalo
    public int Hat;         // 0 nenhum, 1 chapéu de palha, 2 gorro de lã, 3 boné de ferreiro
    public bool Skirt, Apron, Beard, Vest;
    /// <summary>Velho Bram: tapa-olho (olho esquerdo) e perna de pau (direita).</summary>
    public bool EyePatch, PegLeg;
    public float Height = 1f, Width = 1f;

    static readonly Color[] Skins = { new(0.96f, 0.8f, 0.66f), new(0.9f, 0.7f, 0.55f), new(0.78f, 0.56f, 0.4f), new(0.6f, 0.4f, 0.28f), new(0.45f, 0.3f, 0.2f), new(0.98f, 0.86f, 0.75f) };
    static readonly Color[] Cloth =
    {
        new(0.62f, 0.2f, 0.18f), new(0.2f, 0.38f, 0.62f), new(0.3f, 0.5f, 0.28f), new(0.75f, 0.6f, 0.3f), new(0.5f, 0.3f, 0.55f),
        new(0.85f, 0.8f, 0.7f), new(0.35f, 0.35f, 0.4f), new(0.7f, 0.42f, 0.2f), new(0.25f, 0.55f, 0.55f), new(0.78f, 0.45f, 0.5f),
    };
    static readonly Color[] Pantses = { new(0.3f, 0.25f, 0.2f), new(0.22f, 0.25f, 0.33f), new(0.4f, 0.33f, 0.25f), new(0.18f, 0.18f, 0.2f), new(0.45f, 0.4f, 0.3f) };
    static readonly Color[] Hairs = { new(0.12f, 0.08f, 0.06f), new(0.35f, 0.2f, 0.1f), new(0.6f, 0.38f, 0.15f), new(0.85f, 0.7f, 0.4f), new(0.7f, 0.25f, 0.1f), new(0.75f, 0.75f, 0.75f) };

    public static Look Random(int seed)
    {
        var r = new Random(seed * 7919 + 13);
        var l = new Look
        {
            Skin = Skins[r.Next(Skins.Length)], Shirt = Cloth[r.Next(Cloth.Length)], Pants = Pantses[r.Next(Pantses.Length)],
            Shoes = new Color(0.22f, 0.14f, 0.09f), Hair = Hairs[r.Next(Hairs.Length)], Accent = Cloth[r.Next(Cloth.Length)],
            Skirt = r.NextDouble() < 0.45, Apron = r.NextDouble() < 0.3, Vest = r.NextDouble() < 0.3,
            Height = 0.92f + (float)r.NextDouble() * 0.16f, Width = 0.92f + (float)r.NextDouble() * 0.2f,
        };
        l.HairStyle = l.Skirt ? new[] { 1, 2, 4, 0 }[r.Next(4)] : new[] { 0, 0, 3, 4, 1 }[r.Next(5)];
        l.Beard = !l.Skirt && r.NextDouble() < 0.45;
        l.Hat = r.NextDouble() < 0.4 ? 1 + r.Next(3) : 0;
        return l;
    }
}

/// <summary>
/// Corpo articulado de um morador (pés em y=0): pernas com joelho, braços com cotovelo, cabeça que olha em volta.
/// Animações procedurais: andar, ficar parado (respira e olha), conversar (gesticula), sentar e trabalhar.
/// </summary>
public partial class Humanoid : Node3D
{
    public enum Pose { Idle, Walk, Talk, Sit, Work }
    public Pose Mode = Pose.Idle;
    /// <summary>Fica sentado mesmo quando gesticula (Velho Bram).</summary>
    public bool Seated;
    public float WalkSpeed = 1.2f;

    Node3D _hips, _torso, _head, _legL, _legR, _kneeL, _kneeR, _armL, _armR, _elbowL, _elbowR;
    float _phase, _t, _lookYaw, _lookTarget, _lookTimer;
    readonly float _seedOffset;
    public Node3D RightHand { get; private set; }

    static readonly Dictionary<Color, StandardMaterial3D> _mats = new();
    public static StandardMaterial3D Mat(Color c, float rough = 0.85f)
    {
        if (_mats.TryGetValue(c, out var m)) return m;
        m = new StandardMaterial3D { AlbedoColor = c, Roughness = rough };
        _mats[c] = m;
        return m;
    }

    public Humanoid(Look look, int seed)
    {
        _seedOffset = (seed % 97) * 0.37f;
        Build(look);
    }
    public Humanoid() : this(Look.Random(1), 1) { }

    static MeshInstance3D Part(Node3D parent, Mesh mesh, Color c, Vector3 pos, Vector3? rot = null, Vector3? scale = null)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = Mat(c), Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(m);
        return m;
    }
    static CapsuleMesh Cap(float r, float h) => new() { Radius = r, Height = h, RadialSegments = 10, Rings = 3 };
    static SphereMesh Sph(float r) => new() { Radius = r, Height = r * 2, RadialSegments = 12, Rings = 6 };

    void Build(Look l)
    {
        Scale = new Vector3(l.Width, l.Height, l.Width);
        // sombra de contato (disco escuro): ancora o boneco no chão
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 0.005f, RadialSegments = 16 },
            MaterialOverride = BlobMat, Position = new Vector3(0, 0.01f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _hips = new Node3D { Position = new Vector3(0, 0.9f, 0) };
        AddChild(_hips);
        // pernas: coxa (do quadril ao joelho) e canela + sapato (do joelho ao pé)
        (_legL, _kneeL) = Leg(-0.1f, l);
        (_legR, _kneeR) = Leg(0.1f, l, l.PegLeg);
        Part(_hips, Cap(0.15f, 0.36f), l.Pants, new Vector3(0, 0.02f, 0), new Vector3(0, 0, Mathf.Pi / 2), new Vector3(1, 1, 0.9f));
        if (l.Skirt)
            Part(_hips, new CylinderMesh { TopRadius = 0.19f, BottomRadius = 0.33f, Height = 0.62f, RadialSegments = 14 }, l.Accent, new Vector3(0, -0.24f, 0));

        _torso = new Node3D { Position = new Vector3(0, 0.05f, 0) };
        _hips.AddChild(_torso);
        Part(_torso, Cap(0.19f, 0.62f), l.Shirt, new Vector3(0, 0.3f, 0), null, new Vector3(1.08f, 1, 0.72f));
        Part(_torso, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.205f, Height = 0.06f, RadialSegments = 14 }, new Color(0.25f, 0.16f, 0.1f), new Vector3(0, 0.06f, 0), null, new Vector3(1.08f, 1, 0.74f));
        if (l.Vest)
            Part(_torso, Cap(0.2f, 0.5f), l.Accent.Darkened(0.2f), new Vector3(0, 0.3f, -0.005f), null, new Vector3(1.1f, 0.9f, 0.76f));
        if (l.Apron)
            Part(_torso, new BoxMesh { Size = new Vector3(0.3f, 0.55f, 0.02f) }, new Color(0.88f, 0.85f, 0.78f), new Vector3(0, 0.05f, 0.15f));
        // pescoço e cabeça
        Part(_torso, new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.065f, Height = 0.12f, RadialSegments = 8 }, l.Skin, new Vector3(0, 0.64f, 0));
        _head = new Node3D { Position = new Vector3(0, 0.79f, 0) };
        _torso.AddChild(_head);
        BuildHead(l);
        // braços
        (_armL, _elbowL) = Arm(-1, l);
        (_armR, _elbowR) = Arm(1, l);
        RightHand = new Node3D { Position = new Vector3(0, -0.28f, 0.02f) };
        _elbowR.AddChild(RightHand);
    }

    static StandardMaterial3D _blob;
    static StandardMaterial3D BlobMat => _blob ??= new StandardMaterial3D
    {
        AlbedoColor = new Color(0, 0, 0, 0.32f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    (Node3D, Node3D) Leg(float x, Look l, bool peg = false)
    {
        var hip = new Node3D { Position = new Vector3(x, 0, 0) };
        _hips.AddChild(hip);
        Part(hip, Cap(0.075f, 0.5f), l.Pants, new Vector3(0, -0.22f, 0));
        var knee = new Node3D { Position = new Vector3(0, -0.45f, 0) };
        hip.AddChild(knee);
        if (peg)
        {
            // perna de pau: calça amarrada no joelho e um toco de madeira com ponteira
            Part(knee, Sph(0.08f), l.Pants.Darkened(0.2f), new Vector3(0, -0.02f, 0));
            Part(knee, new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.03f, Height = 0.42f, RadialSegments = 8 }, new Color(0.5f, 0.35f, 0.2f), new Vector3(0, -0.22f, 0));
            Part(knee, new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 0.04f, RadialSegments = 8 }, new Color(0.3f, 0.3f, 0.32f), new Vector3(0, -0.43f, 0));
            return (hip, knee);
        }
        Part(knee, Cap(0.065f, 0.46f), l.Pants, new Vector3(0, -0.2f, 0));
        Part(knee, new BoxMesh { Size = new Vector3(0.11f, 0.08f, 0.22f) }, l.Shoes, new Vector3(0, -0.41f, 0.04f));
        return (hip, knee);
    }

    (Node3D, Node3D) Arm(int side, Look l)
    {
        var sh = new Node3D { Position = new Vector3(side * 0.25f, 0.55f, 0) };
        _torso.AddChild(sh);
        Part(sh, Sph(0.075f), l.Shirt, Vector3.Zero);
        Part(sh, Cap(0.06f, 0.32f), l.Shirt, new Vector3(0, -0.14f, 0));
        var el = new Node3D { Position = new Vector3(0, -0.28f, 0) };
        sh.AddChild(el);
        Part(el, Cap(0.052f, 0.28f), l.Vest ? l.Skin : l.Shirt, new Vector3(0, -0.12f, 0));
        Part(el, Sph(0.055f), l.Skin, new Vector3(0, -0.27f, 0.01f), null, new Vector3(0.9f, 1.1f, 0.8f));
        sh.Rotation = new Vector3(0, 0, side * 0.08f);
        return (sh, el);
    }

    void BuildHead(Look l)
    {
        var h = _head;
        Part(h, Sph(0.135f), l.Skin, Vector3.Zero, null, new Vector3(1f, 1.1f, 1.02f));
        foreach (int s in new[] { -1, 1 })
        {
            Part(h, Sph(0.03f), l.Skin, new Vector3(s * 0.135f, 0, -0.005f), null, new Vector3(0.6f, 1.2f, 1f));
            Part(h, Sph(0.022f), new Color(0.97f, 0.97f, 0.95f), new Vector3(s * 0.048f, 0.02f, 0.118f), null, new Vector3(1f, 1f, 0.5f));
            Part(h, Sph(0.012f), new Color(0.08f, 0.06f, 0.06f), new Vector3(s * 0.048f, 0.02f, 0.128f));
            Part(h, new BoxMesh { Size = new Vector3(0.045f, 0.01f, 0.012f) }, l.Hair.Darkened(0.2f), new Vector3(s * 0.05f, 0.058f, 0.125f), new Vector3(0, 0, -s * 0.12f));
        }
        Part(h, Sph(0.024f), l.Skin.Darkened(0.06f), new Vector3(0, -0.015f, 0.14f), null, new Vector3(0.9f, 1.1f, 1.1f));
        Part(h, new BoxMesh { Size = new Vector3(0.05f, 0.008f, 0.01f) }, new Color(0.45f, 0.22f, 0.2f), new Vector3(0, -0.068f, 0.122f));
        // cabelo
        var hair = l.Hair;
        switch (l.HairStyle)
        {
            case 0:
                Part(h, Sph(0.145f), hair, new Vector3(0, 0.055f, -0.035f), null, new Vector3(1.04f, 0.85f, 1.0f));
                break;
            case 1:
                Part(h, Sph(0.148f), hair, new Vector3(0, 0.055f, -0.04f), null, new Vector3(1.05f, 0.88f, 1.0f));
                Part(h, new BoxMesh { Size = new Vector3(0.28f, 0.3f, 0.08f) }, hair, new Vector3(0, -0.1f, -0.1f));
                break;
            case 2:
                Part(h, Sph(0.145f), hair, new Vector3(0, 0.055f, -0.035f), null, new Vector3(1.04f, 0.85f, 1.0f));
                Part(h, Sph(0.07f), hair, new Vector3(0, 0.12f, -0.1f));
                break;
            case 4:
                Part(h, Sph(0.145f), hair, new Vector3(0, 0.055f, -0.035f), null, new Vector3(1.04f, 0.85f, 1.0f));
                Part(h, Cap(0.04f, 0.26f), hair, new Vector3(0, -0.06f, -0.15f), new Vector3(0.3f, 0, 0));
                break;
        }
        if (l.Beard)
            Part(h, Sph(0.1f), hair, new Vector3(0, -0.085f, 0.07f), null, new Vector3(1.1f, l.EyePatch ? 1.4f : 0.9f, 0.7f));
        if (l.EyePatch)
        {
            Part(h, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.012f, RadialSegments = 10 }, new Color(0.06f, 0.05f, 0.05f), new Vector3(-0.048f, 0.02f, 0.13f), new Vector3(Mathf.Pi / 2, 0, 0));
            Part(h, new TorusMesh { InnerRadius = 0.138f, OuterRadius = 0.148f, Rings = 16, RingSegments = 4 }, new Color(0.06f, 0.05f, 0.05f), new Vector3(0, 0.04f, 0), new Vector3(0.3f, 0, 0.25f));
        }
        switch (l.Hat)
        {
            case 1:
                var straw = new Color(0.85f, 0.72f, 0.42f);
                Part(h, new CylinderMesh { TopRadius = 0.27f, BottomRadius = 0.27f, Height = 0.015f, RadialSegments = 18 }, straw, new Vector3(0, 0.1f, 0));
                Part(h, new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.135f, Height = 0.11f, RadialSegments = 14 }, straw, new Vector3(0, 0.16f, 0));
                Part(h, new CylinderMesh { TopRadius = 0.137f, BottomRadius = 0.137f, Height = 0.025f, RadialSegments = 14 }, l.Accent, new Vector3(0, 0.12f, 0));
                break;
            case 2:
                Part(h, Sph(0.15f), l.Accent, new Vector3(0, 0.08f, -0.03f), null, new Vector3(1.04f, 0.8f, 1.0f));
                Part(h, Sph(0.04f), l.Accent.Lightened(0.3f), new Vector3(0, 0.2f, -0.01f));
                break;
            case 3:
                Part(h, new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.148f, Height = 0.08f, RadialSegments = 14 }, new Color(0.3f, 0.2f, 0.13f), new Vector3(0, 0.1f, -0.01f));
                break;
        }
    }

    /// <summary>Segura um objeto na mão direita (cesto, balde, saco...).</summary>
    public void Hold(Node3D prop) => RightHand.AddChild(prop);

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        float dt = (float)delta;
        _t += dt;
        float t = _t + _seedOffset;
        // olhar em volta de vez em quando
        _lookTimer -= dt;
        if (_lookTimer <= 0)
        {
            _lookTimer = 2.5f + (Mathf.Sin(t * 1.7f) + 1f) * 2f;
            _lookTarget = Mode == Pose.Walk ? Mathf.Sin(t * 0.9f) * 0.25f : Mathf.Sin(t * 2.3f) * 0.7f;
        }
        _lookYaw = Mathf.Lerp(_lookYaw, _lookTarget, Mathf.Min(1f, dt * 2.5f));

        float legL = 0, legR = 0, kneeL = 0, kneeR = 0, armL = 0, armR = 0, elL = -0.15f, elR = -0.15f, bob = 0, lean = 0, hipY = 0.9f, headPitch = 0;
        float armSideL = 0, armSideR = 0;
        switch (Mode)
        {
            case Pose.Walk:
            {
                _phase += dt * WalkSpeed * 5.2f;
                float s = Mathf.Sin(_phase);
                legL = s * 0.5f; legR = -s * 0.5f;
                kneeL = Mathf.Max(0, Mathf.Sin(_phase - 1.2f)) * 0.75f;
                kneeR = Mathf.Max(0, Mathf.Sin(_phase - 1.2f + Mathf.Pi)) * 0.75f;
                armL = -s * 0.42f; armR = s * 0.42f;
                elL = -0.25f - Mathf.Max(0, -s) * 0.3f; elR = -0.25f - Mathf.Max(0, s) * 0.3f;
                bob = Mathf.Abs(Mathf.Cos(_phase)) * 0.035f;
                lean = 0.06f;
                break;
            }
            case Pose.Talk:
            {
                float g = Mathf.Sin(t * 2.6f);
                armR = -0.5f - Mathf.Max(0, g) * 0.5f;
                elR = -0.9f - g * 0.3f;
                armSideR = 0.15f;
                armL = -0.1f + Mathf.Sin(t * 1.3f) * 0.08f;
                headPitch = Mathf.Sin(t * 3.1f) * 0.05f;
                bob = Mathf.Sin(t * 2f) * 0.005f;
                break;
            }
            case Pose.Sit:
            {
                hipY = 0.5f;
                legL = legR = 1.5f;
                kneeL = kneeR = 1.5f;
                armL = armR = -0.45f; elL = elR = -0.9f;
                lean = -0.05f;
                break;
            }
            case Pose.Work:
            {
                // mexe nas coisas da barraca / tira água do poço: braços à frente, sobe e desce
                float g = Mathf.Sin(t * 2.2f);
                armL = -0.9f + g * 0.15f; armR = -0.9f - g * 0.15f;
                elL = elR = -0.6f;
                lean = 0.18f;
                headPitch = 0.25f;
                break;
            }
            default:
            {
                float br = Mathf.Sin(t * 1.6f);
                armL = br * 0.03f; armR = -br * 0.03f;
                bob = br * 0.006f;
                break;
            }
        }
        if (Seated && Mode != Pose.Walk) { hipY = 0.5f; legL = legR = 1.5f; kneeL = kneeR = 1.5f; }
        _hips.Position = new Vector3(0, hipY + bob, 0);
        _torso.Rotation = new Vector3(lean, 0, 0);
        _legL.Rotation = new Vector3(-legL, 0, 0);
        _legR.Rotation = new Vector3(-legR, 0, 0);
        _kneeL.Rotation = new Vector3(kneeL, 0, 0);
        _kneeR.Rotation = new Vector3(kneeR, 0, 0);
        _armL.Rotation = new Vector3(armL, 0, -0.08f - armSideL);
        _armR.Rotation = new Vector3(armR, 0, 0.08f + armSideR);
        _elbowL.Rotation = new Vector3(elL, 0, 0);
        _elbowR.Rotation = new Vector3(elR, 0, 0);
        _head.Rotation = new Vector3(headPitch, _lookYaw, 0);
    }
}
