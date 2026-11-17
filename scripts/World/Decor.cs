using System;
using System.Collections.Generic;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Cenografia procedural inspirada na direção de arte: posto avançado com janelas acesas,
/// cachoeiras animadas, cogumelos luminosos, samambaias, ruínas ancestrais e anel rúnico.
/// Tudo com MultiMesh ou poucas malhas, sem sombras.
/// </summary>
public static class Decor
{
    static Shader _waterfall;

    static MeshInstance3D Mesh(Node3D root, Mesh mesh, Material mat, Vector3 pos, Vector3? rot = null, Vector3? scale = null)
    {
        var m = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = mat, Position = pos,
            Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(m);
        return m;
    }

    static void Collider(Node3D root, Vector3 pos, Vector3 size, float yaw = 0)
    {
        var body = new StaticBody3D { CollisionLayer = PlayerController.WorldLayer, CollisionMask = 0, Position = pos, Rotation = new Vector3(0, yaw, 0) };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        root.AddChild(body);
    }

    static StandardMaterial3D Lit(Color c, float rough = 0.7f) => new() { AlbedoColor = c, Roughness = rough, Metallic = 0.1f };

    static StandardMaterial3D Glow(Color c, float e) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = e };

    // ------------------------------------------------------------------
    /// <summary>Posto avançado do escavador: módulos com janelas acesas, antena parabólica, caixotes.</summary>
    public static void Outpost(Node3D root, Vector3 origin, float yaw)
    {
        var node = new Node3D { Position = origin, Rotation = new Vector3(0, yaw, 0) };
        root.AddChild(node);
        var hull = Lit(new Color(0.55f, 0.55f, 0.6f), 0.5f);
        var trim = Lit(new Color(0.9f, 0.5f, 0.15f), 0.5f);
        var dark = Lit(new Color(0.18f, 0.18f, 0.22f), 0.6f);
        var window = Glow(new Color(1f, 0.7f, 0.35f), 2.2f);
        for (int i = 0; i < 2; i++)
        {
            var m = new Vector3(i * 4.6f, 0, 0);
            // módulo cilíndrico deitado
            Mesh(node, new CylinderMesh { TopRadius = 1.4f, BottomRadius = 1.4f, Height = 4f, RadialSegments = 12, Rings = 1 }, hull, m + new Vector3(0, 1.5f, 0), new Vector3(0, 0, Mathf.Pi / 2));
            Mesh(node, new BoxMesh { Size = new Vector3(4.1f, 0.25f, 2.6f) }, dark, m + new Vector3(0, 0.12f, 0));
            for (int k = -1; k <= 1; k += 2)
                Mesh(node, new CylinderMesh { TopRadius = 1.45f, BottomRadius = 1.45f, Height = 0.22f, RadialSegments = 12, Rings = 1 }, trim, m + new Vector3(k * 2f, 1.5f, 0), new Vector3(0, 0, Mathf.Pi / 2));
            // janelas acesas
            for (int w = 0; w < 3; w++)
                Mesh(node, new BoxMesh { Size = new Vector3(0.7f, 0.32f, 0.05f) }, window, m + new Vector3(-1.1f + w * 1.1f, 1.75f, 1.38f));
            Collider(node, m + new Vector3(0, 1.4f, 0), new Vector3(4.2f, 2.8f, 2.8f));
        }
        // antena parabólica
        var dishPos = new Vector3(2.3f, 2.9f, -0.3f);
        Mesh(node, new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = 1.4f, RadialSegments = 6, Rings = 1 }, dark, dishPos + new Vector3(0, 0.5f, 0));
        Mesh(node, new SphereMesh { Radius = 0.75f, Height = 0.6f, IsHemisphere = true, RadialSegments = 14, Rings = 4 }, hull, dishPos + new Vector3(0, 1.4f, 0), new Vector3(-2.3f, 0.6f, 0));
        Mesh(node, new SphereMesh { Radius = 0.08f, Height = 0.16f, RadialSegments = 6, Rings = 3 }, Glow(new Color(1f, 0.3f, 0.2f), 3f), dishPos + new Vector3(0, 1.3f, 0.5f));
        // caixotes com faixa luminosa
        for (int i = 0; i < 3; i++)
        {
            var p = new Vector3(-2.8f + i * 0.2f, 0.35f + (i == 2 ? 0.7f : 0), 1.9f + (i == 1 ? 0.8f : 0));
            if (i == 2) p = new Vector3(-2.7f, 1.05f, 2.2f);
            Mesh(node, new BoxMesh { Size = new Vector3(1.1f, 0.7f, 0.75f) }, dark, p);
            Mesh(node, new BoxMesh { Size = new Vector3(1.12f, 0.08f, 0.77f) }, Glow(new Color(1f, 0.6f, 0.2f), 1.8f), p + new Vector3(0, 0.12f, 0));
        }
        Collider(node, new Vector3(-2.7f, 0.7f, 2.3f), new Vector3(1.4f, 1.4f, 1.8f));
        var light = new OmniLight3D { LightColor = new Color(1f, 0.65f, 0.35f), LightEnergy = 1.6f, OmniRange = 9f, Position = new Vector3(2.3f, 3f, 2.5f) };
        node.AddChild(light);
    }

    // ------------------------------------------------------------------
    /// <summary>Cachoeira animada encostada numa parede, com poça e névoa.</summary>
    public static void Waterfall(Node3D root, Vector3 basePos, float height, float width, float yaw, Color color)
    {
        _waterfall ??= GD.Load<Shader>("res://shaders/waterfall.gdshader");
        var mat = new ShaderMaterial { Shader = _waterfall };
        mat.SetShaderParameter("water_color", color);
        var node = new Node3D { Position = basePos, Rotation = new Vector3(0, yaw, 0) };
        root.AddChild(node);
        Mesh(node, new QuadMesh { Size = new Vector2(width, height) }, mat, new Vector3(0, height / 2, 0));
        Mesh(node, new QuadMesh { Size = new Vector2(width * 0.6f, height) }, mat, new Vector3(0, height / 2, 0.15f));
        var pool = new StandardMaterial3D
        {
            AlbedoColor = new Color(color.R * 0.4f, color.G * 0.6f, color.B * 0.8f, 0.75f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true, Emission = color * 0.5f, EmissionEnergyMultiplier = 0.9f,
            Roughness = 0.05f, Metallic = 0.5f,
        };
        Mesh(node, new CylinderMesh { TopRadius = width * 1.1f, BottomRadius = width * 1.1f, Height = 0.03f, RadialSegments = 16, Rings = 1 }, pool, new Vector3(0, 0.02f, width * 0.8f));
        var mist = new CpuParticles3D
        {
            Amount = Settings.Quality == GraphicsQuality.Low ? 8 : 18,
            Lifetime = 1.6f,
            Mesh = new QuadMesh { Size = new Vector2(0.5f, 0.5f) },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(width * 0.5f, 0.1f, 0.3f),
            Direction = Vector3.Up, Spread = 40, Gravity = new Vector3(0, 0.3f, 0),
            InitialVelocityMin = 0.4f, InitialVelocityMax = 1f,
            Color = new Color(color, 0.25f),
            Position = new Vector3(0, 0.2f, 0.4f),
        };
        mist.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, VertexColorUseAsAlbedo = true,
        };
        node.AddChild(mist);
    }

    // ------------------------------------------------------------------
    /// <summary>Aglomerados de cogumelos luminosos (MultiMesh: caules + chapéus).</summary>
    public static void Mushrooms(Node3D root, RegionDef r, Random rng, int clusters, Color capColor)
    {
        var stems = new List<Transform3D>();
        var caps = new List<Transform3D>();
        for (int c = 0; c < clusters; c++)
        {
            // perto das paredes laterais
            float side = rng.Next(2) == 0 ? -1 : 1;
            var center = new Vector3(side * (r.HalfX - 1.5f - (float)rng.NextDouble() * 3f), 0, r.CenterZ + ((float)rng.NextDouble() * 2 - 1) * (r.HalfZ - 3));
            int n = rng.Next(3, 7);
            for (int i = 0; i < n; i++)
            {
                var p = center + new Vector3(((float)rng.NextDouble() - 0.5f) * 1.6f, 0, ((float)rng.NextDouble() - 0.5f) * 1.6f);
                float h = 0.25f + (float)rng.NextDouble() * 0.6f;
                float capR = 0.15f + (float)rng.NextDouble() * 0.25f;
                var tilt = new Basis(new Vector3(1, 0, 0), ((float)rng.NextDouble() - 0.5f) * 0.4f);
                stems.Add(new Transform3D(tilt.ScaleLocal(new Vector3(0.05f, h, 0.05f)), p + Vector3.Up * h * 0.5f));
                caps.Add(new Transform3D(tilt.ScaleLocal(new Vector3(capR, capR * 0.6f, capR)), p + Vector3.Up * h));
            }
        }
        AddMulti(root, new CylinderMesh { TopRadius = 1, BottomRadius = 1.2f, Height = 1, RadialSegments = 5, Rings = 1 }, Lit(new Color(0.8f, 0.85f, 0.85f)), stems);
        AddMulti(root, new SphereMesh { Radius = 1, Height = 1, IsHemisphere = true, RadialSegments = 8, Rings = 3 }, Glow(capColor, 1.8f), caps);
    }

    /// <summary>Samambaias estilizadas (folhas em leque) em tons quentes.</summary>
    public static void Ferns(Node3D root, RegionDef r, Random rng, int count, Color color)
    {
        var leaves = new List<Transform3D>();
        for (int i = 0; i < count; i++)
        {
            float side = rng.Next(2) == 0 ? -1 : 1;
            var p = new Vector3(side * (r.HalfX - 1f - (float)rng.NextDouble() * 4f), 0, r.CenterZ + ((float)rng.NextDouble() * 2 - 1) * (r.HalfZ - 2));
            int fronds = rng.Next(5, 8);
            float size = 0.6f + (float)rng.NextDouble() * 0.6f;
            for (int f = 0; f < fronds; f++)
            {
                float a = Mathf.Tau * f / fronds + (float)rng.NextDouble() * 0.3f;
                var b = new Basis(Vector3.Up, a) * new Basis(Vector3.Right, -0.9f - (float)rng.NextDouble() * 0.4f);
                leaves.Add(new Transform3D(b.ScaleLocal(new Vector3(0.12f, size, 0.02f)), p));
            }
        }
        var mat = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = true, Emission = color * 0.35f, EmissionEnergyMultiplier = 0.8f };
        AddMulti(root, MeshFactory.Spike(4, 1f, 1f), mat, leaves);
    }

    // ------------------------------------------------------------------
    /// <summary>Ruínas ancestrais: pilares com runas e um grande anel rúnico flutuante.</summary>
    public static void AncientRuins(Node3D root, RegionDef r, Random rng)
    {
        var stone = Lit(new Color(0.62f, 0.52f, 0.56f), 0.85f);
        stone.EmissionEnabled = true; stone.Emission = new Color(0.25f, 0.12f, 0.3f); stone.EmissionEnergyMultiplier = 0.6f;
        var rune = Glow(new Color(1f, 0.75f, 0.3f), 2.4f);
        // pilares ao longo das laterais
        for (int i = 0; i < 6; i++)
        {
            float side = i % 2 == 0 ? -1 : 1;
            var p = new Vector3(side * (r.HalfX - 4f), 0, r.CenterZ + r.HalfZ - 8 - (i / 2) * 15f);
            float h = 5.5f + (float)rng.NextDouble() * 2.5f;
            Mesh(root, new BoxMesh { Size = new Vector3(1.4f, h, 1.4f) }, stone, p + Vector3.Up * h / 2);
            Mesh(root, new BoxMesh { Size = new Vector3(1.9f, 0.5f, 1.9f) }, stone, p + Vector3.Up * (h + 0.25f));
            Mesh(root, new BoxMesh { Size = new Vector3(1.9f, 0.4f, 1.9f) }, stone, p + Vector3.Up * 0.2f);
            for (int k = 0; k < 3; k++)
                Mesh(root, new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.04f) }, rune, p + new Vector3(0, 1.6f + k * 1.3f, -side * 0 + 0.72f));
            Collider(root, p + Vector3.Up * h / 2, new Vector3(1.4f, h, 1.4f));
        }
        // anel rúnico vertical acima do Coração
        var ring = new RuneRing { Position = new Vector3(0, 9.5f, r.CenterZ - 4f) };
        root.AddChild(ring);
        root.AddChild(new OmniLight3D { LightColor = new Color(0.85f, 0.5f, 1f), LightEnergy = 2f, OmniRange = 14f, Position = new Vector3(0, 9.5f, r.CenterZ + 2f) });
        ring.AddChild(new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 4.6f, OuterRadius = 5.2f, Rings = 32, RingSegments = 6 },
            MaterialOverride = stone, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.Tau * i / 12;
            ring.AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.45f, 0.45f, 0.7f) }, MaterialOverride = rune,
                Position = new Vector3(Mathf.Cos(a) * 4.9f, 0, Mathf.Sin(a) * 4.9f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        // cristais flutuantes ao redor
        var fl = MeshFactory.CrystalPrism(6, 1, 1, 0.45f);
        var flMat = Glow(new Color(0.85f, 0.4f, 1f), 1.6f);
        for (int i = 0; i < 5; i++)
        {
            float a = Mathf.Tau * i / 5 + 0.3f;
            var fr = new FloatingRock
            {
                Mesh = fl, MaterialOverride = flMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Scale = new Vector3(0.6f, 2.2f, 0.6f) * (0.7f + (float)rng.NextDouble() * 0.6f),
                Phase = (float)rng.NextDouble() * 6f,
            };
            root.AddChild(fr);
            fr.Position = new Vector3(Mathf.Cos(a) * 9f, 8f + (float)rng.NextDouble() * 3f, r.CenterZ - 4f + Mathf.Sin(a) * 9f);
        }
    }

    static void AddMulti(Node3D root, Mesh mesh, Material mat, List<Transform3D> xforms)
    {
        if (xforms.Count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xforms.Count };
        for (int i = 0; i < xforms.Count; i++) mm.SetInstanceTransform(i, xforms[i]);
        root.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }
}

/// <summary>Anel rúnico que gira lentamente na vertical.</summary>
public partial class RuneRing : Node3D
{
    public override void _Ready() => Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
    public override void _Process(double delta) => RotateObjectLocal(Vector3.Up, (float)delta * 0.25f);
}
