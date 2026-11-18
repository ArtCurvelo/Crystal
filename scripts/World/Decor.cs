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
    /// <summary>Caixote de armazenamento: painéis cinza, cantos laranja e faixa luminosa.</summary>
    public static void Crate(Node3D root, Vector3 pos, float yaw, float scale = 1f, bool collide = false)
    {
        var node = new Node3D { Position = pos, Rotation = new Vector3(0, yaw, 0), Scale = Vector3.One * scale };
        root.AddChild(node);
        var grey = Lit(new Color(0.45f, 0.46f, 0.52f), 0.6f);
        var dark = Lit(new Color(0.18f, 0.19f, 0.23f), 0.7f);
        var orange = Lit(new Color(0.95f, 0.5f, 0.15f), 0.5f);
        Mesh(node, new BoxMesh { Size = new Vector3(1.1f, 0.7f, 0.75f) }, grey, new Vector3(0, 0.35f, 0));
        Mesh(node, new BoxMesh { Size = new Vector3(1.14f, 0.12f, 0.79f) }, dark, new Vector3(0, 0.7f, 0));
        for (int sx = -1; sx <= 1; sx += 2)
            Mesh(node, new BoxMesh { Size = new Vector3(0.1f, 0.74f, 0.8f) }, orange, new Vector3(sx * 0.5f, 0.37f, 0));
        Mesh(node, new BoxMesh { Size = new Vector3(0.6f, 0.07f, 0.02f) }, Glow(new Color(1f, 0.6f, 0.2f), 2f), new Vector3(0, 0.5f, 0.38f));
        if (collide) Collider(node, new Vector3(0, 0.35f, 0), new Vector3(1.1f, 0.7f, 0.75f));
    }

    /// <summary>Cápsula de refino: tanque de vidro com brilho dourado, canos e painel.</summary>
    public static void RefineryPod(Node3D root, Vector3 pos, float yaw)
    {
        var node = new Node3D { Position = pos, Rotation = new Vector3(0, yaw, 0) };
        root.AddChild(node);
        var grey = Lit(new Color(0.45f, 0.46f, 0.52f), 0.55f);
        var dark = Lit(new Color(0.18f, 0.19f, 0.23f), 0.7f);
        var orange = Lit(new Color(0.95f, 0.5f, 0.15f), 0.5f);
        Mesh(node, new CylinderMesh { TopRadius = 1.0f, BottomRadius = 1.1f, Height = 0.4f, RadialSegments = 12, Rings = 1 }, dark, new Vector3(0, 0.2f, 0));
        Mesh(node, new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.7f, Height = 1.5f, RadialSegments = 14, Rings = 1 }, Glow(new Color(1f, 0.7f, 0.2f), 1.8f), new Vector3(0, 1.15f, 0));
        for (int i = 0; i < 2; i++)
            Mesh(node, new CylinderMesh { TopRadius = 0.82f, BottomRadius = 0.82f, Height = 0.2f, RadialSegments = 12, Rings = 1 }, grey, new Vector3(0, 0.45f + i * 1.45f, 0));
        for (int i = 0; i < 4; i++)
        {
            float a = Mathf.Tau * i / 4 + 0.4f;
            Mesh(node, new BoxMesh { Size = new Vector3(0.12f, 1.5f, 0.12f) }, orange, new Vector3(Mathf.Cos(a) * 0.76f, 1.15f, Mathf.Sin(a) * 0.76f));
        }
        Mesh(node, new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.75f, Height = 0.35f, RadialSegments = 12, Rings = 1 }, grey, new Vector3(0, 2.15f, 0));
        // cano em arco
        Mesh(node, new TorusMesh { InnerRadius = 0.55f, OuterRadius = 0.7f, Rings = 16, RingSegments = 6 }, dark, new Vector3(0.9f, 1.9f, 0), new Vector3(Mathf.Pi / 2, 0, 0));
        Mesh(node, new BoxMesh { Size = new Vector3(0.7f, 0.5f, 0.12f) }, dark, new Vector3(0, 0.75f, 0.85f));
        Mesh(node, new BoxMesh { Size = new Vector3(0.6f, 0.38f, 0.02f) }, Glow(new Color(0.35f, 0.9f, 1f), 1.4f), new Vector3(0, 0.75f, 0.92f));
        node.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.7f, 0.3f), LightEnergy = 1.2f, OmniRange = 5f, Position = new Vector3(0, 1.2f, 1.2f) });
        Collider(node, new Vector3(0, 1.1f, 0), new Vector3(1.8f, 2.2f, 1.8f));
    }

    /// <summary>Scanner de minério em tripé com cone de varredura holográfico.</summary>
    public static void OreScanner(Node3D root, Vector3 pos, float yaw)
    {
        var node = new ScannerNode { Position = pos, Rotation = new Vector3(0, yaw, 0) };
        root.AddChild(node);
        var dark = Lit(new Color(0.18f, 0.19f, 0.23f), 0.7f);
        var orange = Lit(new Color(0.95f, 0.5f, 0.15f), 0.5f);
        for (int i = 0; i < 3; i++)
        {
            float a = Mathf.Tau * i / 3;
            Mesh(node, new BoxMesh { Size = new Vector3(0.06f, 1.2f, 0.06f) }, dark, new Vector3(Mathf.Cos(a) * 0.3f, 0.55f, Mathf.Sin(a) * 0.3f), new Vector3(Mathf.Sin(a) * 0.3f, 0, -Mathf.Cos(a) * 0.3f));
            Mesh(node, new BoxMesh { Size = new Vector3(0.12f, 0.1f, 0.12f) }, orange, new Vector3(Mathf.Cos(a) * 0.48f, 0.05f, Mathf.Sin(a) * 0.48f));
        }
        var head = new Node3D { Position = new Vector3(0, 1.2f, 0) };
        node.AddChild(head);
        node.Head = head;
        Mesh(head, new BoxMesh { Size = new Vector3(0.45f, 0.3f, 0.4f) }, orange, Vector3.Zero);
        Mesh(head, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.12f, RadialSegments = 10, Rings = 1 }, dark, new Vector3(0, 0, -0.24f), new Vector3(Mathf.Pi / 2, 0, 0));
        Mesh(head, new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.08f, Height = 0.02f, RadialSegments = 10, Rings = 1 }, Glow(new Color(0.35f, 0.9f, 1f), 3f), new Vector3(0, 0, -0.3f), new Vector3(Mathf.Pi / 2, 0, 0));
        Mesh(head, new BoxMesh { Size = new Vector3(0.02f, 0.3f, 0.02f) }, dark, new Vector3(0.15f, 0.3f, 0.1f));
        var cone = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled, AlbedoColor = new Color(0.3f, 0.85f, 1f, 0.18f) };
        Mesh(head, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 1.1f, Height = 3f, RadialSegments = 4, Rings = 1, CapTop = false, CapBottom = false }, cone, new Vector3(0, 0, -1.8f), new Vector3(-Mathf.Pi / 2, 0, 0));
    }

    /// <summary>Grade de piso (decal metálico) com faixa luminosa.</summary>
    public static void FloorGrate(Node3D root, Vector3 pos, Vector2 size, float yaw)
    {
        var node = new Node3D { Position = pos, Rotation = new Vector3(0, yaw, 0) };
        root.AddChild(node);
        Mesh(node, new BoxMesh { Size = new Vector3(size.X, 0.03f, size.Y) }, Lit(new Color(0.2f, 0.21f, 0.25f), 0.5f), new Vector3(0, 0.015f, 0));
        int bars = Mathf.Max(3, (int)(size.X / 0.25f));
        for (int i = 0; i < bars; i++)
            Mesh(node, new BoxMesh { Size = new Vector3(0.05f, 0.035f, size.Y * 0.9f) }, Lit(new Color(0.35f, 0.36f, 0.4f), 0.4f), new Vector3(-size.X / 2 + 0.15f + i * (size.X - 0.3f) / (bars - 1), 0.02f, 0));
        Mesh(node, new BoxMesh { Size = new Vector3(size.X, 0.04f, 0.06f) }, Glow(new Color(1f, 0.6f, 0.2f), 1.6f), new Vector3(0, 0.02f, size.Y / 2));
    }

    // ------------------------------------------------------------------
    /// <summary>Passarela suspensa de madeira com corrimãos e lanternas (decorativa).</summary>
    public static void Bridge(Node3D root, Vector3 from, Vector3 to, float width = 1.6f)
    {
        var node = new Node3D { Position = (from + to) / 2 };
        root.AddChild(node);
        var dir = to - from;
        float len = dir.Length();
        node.Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Z), 0);
        var wood = Lit(new Color(0.42f, 0.28f, 0.18f), 0.9f);
        var dark = Lit(new Color(0.2f, 0.18f, 0.18f), 0.8f);
        var lantern = Glow(new Color(1f, 0.7f, 0.35f), 2.4f);
        Mesh(node, new BoxMesh { Size = new Vector3(width, 0.12f, len) }, wood, Vector3.Zero);
        for (int sx = -1; sx <= 1; sx += 2)
        {
            Mesh(node, new BoxMesh { Size = new Vector3(0.06f, 0.06f, len) }, dark, new Vector3(sx * width / 2, 0.75f, 0));
            int posts = Mathf.Max(2, (int)(len / 2.5f));
            for (int i = 0; i <= posts; i++)
            {
                float z = -len / 2 + i * len / posts;
                Mesh(node, new BoxMesh { Size = new Vector3(0.08f, 0.85f, 0.08f) }, dark, new Vector3(sx * width / 2, 0.38f, z));
                if (i % 2 == 0)
                {
                    Mesh(node, new BoxMesh { Size = new Vector3(0.05f, 0.5f, 0.05f) }, dark, new Vector3(sx * width / 2, 1.05f, z));
                    Mesh(node, new BoxMesh { Size = new Vector3(0.16f, 0.2f, 0.16f) }, lantern, new Vector3(sx * width / 2, 1.38f, z));
                }
            }
        }
        // pilares de sustentação até o chão
        for (int i = 0; i < 2; i++)
        {
            float z = (i == 0 ? -1 : 1) * len * 0.3f;
            Mesh(node, new BoxMesh { Size = new Vector3(0.25f, from.Y, 0.25f) }, dark, new Vector3(0, -from.Y / 2, z));
        }
        node.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.7f, 0.4f), LightEnergy = 1f, OmniRange = Mathf.Min(10f, len), Position = new Vector3(0, 1.4f, 0) });
    }

    /// <summary>Lago luminoso cobrindo o piso (o jogador caminha pela água rasa).</summary>
    public static void Lake(Node3D root, Vector3 center, float radius, Color glow)
    {
        var mat = Water(glow, new Color(0.01f, 0.06f, 0.1f), new Color(0.06f, 0.35f, 0.45f));
        mat.SetShaderParameter("depth_fade", 0.7f);
        mat.SetShaderParameter("foam_width", 0.12f);
        // superfície subdividida (marolas); a borda fica escondida sob as paredes e o relevo
        Mesh(root, new PlaneMesh { Size = new Vector2(radius * 2, radius * 2), SubdivideWidth = 48, SubdivideDepth = 48 }, mat, center);
    }

    static Shader _water;

    /// <summary>Material de água (reflexo, refração, profundidade, espuma, cáusticas).</summary>
    public static ShaderMaterial Water(Color glow, Color deep, Color shallow)
    {
        _water ??= GD.Load<Shader>("res://shaders/water.gdshader");
        var m = new ShaderMaterial { Shader = _water };
        m.SetShaderParameter("glow_color", glow);
        m.SetShaderParameter("deep_color", deep);
        m.SetShaderParameter("shallow_color", shallow);
        m.SetShaderParameter("noise_tex", CaveTerrain.NoiseTexture);
        return m;
    }

    /// <summary>Águas-vivas luminosas flutuando no ar úmido do reservatório.</summary>
    public static void Jellyfish(Node3D root, Vector3 center, float radius, float height, Random rng, int count)
    {
        var colors = new[] { new Color(0.4f, 0.9f, 1f), new Color(1f, 0.5f, 0.85f), new Color(0.6f, 0.6f, 1f) };
        for (int i = 0; i < count; i++)
        {
            var c = colors[i % colors.Length];
            var j = new JellyNode
            {
                Position = center + new Vector3(((float)rng.NextDouble() * 2 - 1) * radius * 0.7f, 3f + (float)rng.NextDouble() * Mathf.Max(1f, height - 6f), ((float)rng.NextDouble() * 2 - 1) * radius * 0.7f),
                Phase = (float)rng.NextDouble() * 6f,
                Scale = Vector3.One * (0.6f + (float)rng.NextDouble() * 0.7f),
            };
            root.AddChild(j);
            var bellMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(c, 0.55f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.4f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                RimEnabled = true, Rim = 1f,
            };
            j.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.7f, Height = 0.9f, IsHemisphere = true, RadialSegments = 12, Rings = 4 }, MaterialOverride = bellMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            // tentáculos mesclados numa malha
            var st = new SurfaceTool();
            st.Begin(Godot.Mesh.PrimitiveType.Triangles);
            var strand = new BoxMesh { Size = new Vector3(0.03f, 1f, 0.03f) };
            for (int k = 0; k < 7; k++)
            {
                float a = Mathf.Tau * k / 7;
                float len = 1.0f + (float)rng.NextDouble() * 1.2f;
                var b = new Basis(Vector3.Forward, ((float)rng.NextDouble() - 0.5f) * 0.3f).ScaleLocal(new Vector3(1, len, 1));
                st.AppendFrom(strand, 0, new Transform3D(b, new Vector3(Mathf.Cos(a) * 0.4f, -len / 2, Mathf.Sin(a) * 0.4f)));
            }
            j.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Glow(c, 1.6f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
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
        var pool = Water(color, new Color(0.02f, 0.08f, 0.12f), new Color(color.R * 0.3f, color.G * 0.5f, color.B * 0.6f));
        pool.SetShaderParameter("puddle", 1f);
        pool.SetShaderParameter("wave_speed", 1.4f);
        Mesh(node, new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 0.01f, RadialSegments = 32, Rings = 1 }, pool, new Vector3(0, 0.06f, width * 0.8f), null, new Vector3(width * 1.3f, 1, width * 1.3f));
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
    public static void Mushrooms(Node3D root, CaveLayout L, Random rng, int clusters, Color capColor)
    {
        var stems = new List<Transform3D>();
        var caps = new List<Transform3D>();
        for (int c = 0; c < clusters; c++)
        {
            // perto das paredes
            if (!L.RandomNearWall(rng, out var center, -3f, -0.8f)) continue;
            int n = rng.Next(3, 7);
            for (int i = 0; i < n; i++)
            {
                var p = center + new Vector3(((float)rng.NextDouble() - 0.5f) * 1.6f, 0, ((float)rng.NextDouble() - 0.5f) * 1.6f);
                p.Y = L.Ground(p.X, p.Z) - 0.05f;
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
    public static void Ferns(Node3D root, CaveLayout L, Random rng, int count, Color color)
    {
        var leaves = new List<Transform3D>();
        for (int i = 0; i < count; i++)
        {
            if (!L.RandomNearWall(rng, out var p, -4f, -0.5f)) continue;
            int fronds = rng.Next(5, 8);
            float size = 0.6f + (float)rng.NextDouble() * 0.6f;
            for (int f = 0; f < fronds; f++)
            {
                float a = Mathf.Tau * f / fronds + (float)rng.NextDouble() * 0.3f;
                var b = new Basis(Vector3.Up, a) * new Basis(Vector3.Right, -0.9f - (float)rng.NextDouble() * 0.4f);
                leaves.Add(new Transform3D(b.ScaleLocal(new Vector3(0.12f, size, 0.02f)), p));
            }
        }
        var mat = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.8f, CullMode = BaseMaterial3D.CullModeEnum.Disabled, EmissionEnabled = true, Emission = color * 0.25f, EmissionEnergyMultiplier = 0.5f };
        AddMulti(root, MeshFactory.Spike(4, 1f, 1f), mat, leaves);
    }

    // ------------------------------------------------------------------
    /// <summary>Ruínas ancestrais: pilares com runas e um grande anel rúnico flutuante.</summary>
    public static void AncientRuins(Node3D root, CaveLayout L, Vector3 heart, Random rng)
    {
        var stone = Lit(new Color(0.62f, 0.52f, 0.56f), 0.85f);
        stone.EmissionEnabled = true; stone.Emission = new Color(0.25f, 0.12f, 0.3f); stone.EmissionEnergyMultiplier = 0.6f;
        var rune = Glow(new Color(1f, 0.75f, 0.3f), 2.4f);
        // pilares ao longo das laterais
        for (int i = 0; i < 6; i++)
        {
            float side = 0;
            float pa = Mathf.Tau * i / 6 + 0.26f;
            var p = heart + new Vector3(Mathf.Cos(pa) * 12.5f, 0, Mathf.Sin(pa) * 12.5f);
            p.Y = L.Ground(p.X, p.Z) - 0.2f;
            float h = 5.5f + (float)rng.NextDouble() * 2.5f;
            Mesh(root, new BoxMesh { Size = new Vector3(1.4f, h, 1.4f) }, stone, p + Vector3.Up * h / 2);
            Mesh(root, new BoxMesh { Size = new Vector3(1.9f, 0.5f, 1.9f) }, stone, p + Vector3.Up * (h + 0.25f));
            Mesh(root, new BoxMesh { Size = new Vector3(1.9f, 0.4f, 1.9f) }, stone, p + Vector3.Up * 0.2f);
            for (int k = 0; k < 3; k++)
                Mesh(root, new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.04f) }, rune, p + new Vector3(0, 1.6f + k * 1.3f, -side * 0 + 0.72f));
            Collider(root, p + Vector3.Up * h / 2, new Vector3(1.4f, h, 1.4f));
        }
        // anel rúnico vertical acima do Coração
        var ring = new RuneRing { Position = heart + new Vector3(0, 9.5f, 0) };
        root.AddChild(ring);
        root.AddChild(new OmniLight3D { LightColor = new Color(0.85f, 0.5f, 1f), LightEnergy = 2f, OmniRange = 14f, Position = heart + new Vector3(0, 9.5f, 6f) });
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
            fr.Position = heart + new Vector3(Mathf.Cos(a) * 9f, 8f + (float)rng.NextDouble() * 3f, Mathf.Sin(a) * 9f);
        }
    }

    /// <summary>Formações gigantes de cristal (decorativas) junto às paredes, com o shader de cristal.</summary>
    public static void GiantClusters(Node3D root, CaveLayout L, Random rng, int count, Color[] palette, float height = 10f,
        Func<Vector3, float, bool> free = null, Action<Shape3D, Transform3D, Vector3, float> solid = null)
    {
        var shader = GD.Load<Shader>("res://shaders/crystal.gdshader");
        var mesh = MeshFactory.CrystalPrism(6, 1, 1, 0.3f);
        foreach (var col in palette)
        {
            var mat = new ShaderMaterial { Shader = shader };
            mat.SetShaderParameter("base_color", col.Lightened(0.15f));
            mat.SetShaderParameter("glow_color", col);
            mat.SetShaderParameter("glow_energy", 0.55f);
            mat.SetShaderParameter("seed", (float)rng.NextDouble() * 40f);
            var xf = new List<Transform3D>();
            for (int c = 0; c < count; c++)
            {
                // junto a uma parede, longe das passagens estreitas
                if (!L.RandomNearWall(rng, out var basePos, -1.6f, -0.6f)) continue;
                if (free != null && !free(basePos, 2.5f)) continue;
                solid?.Invoke(null, default, basePos, 2.2f); // bloqueia a base no grid
                var inward = L.Inward(basePos.X, basePos.Z);
                int prisms = rng.Next(4, 7);
                for (int i = 0; i < prisms; i++)
                {
                    float h = 2.2f + (float)rng.NextDouble() * (height * 0.45f);
                    float rad = 0.35f + (float)rng.NextDouble() * 0.45f;
                    if (i == 0) { h *= 1.3f; rad *= 1.3f; }
                    var dir = (Vector3.Up * 1.4f + inward * (0.3f + (float)rng.NextDouble() * 0.6f)
                               + new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f) * 0.9f).Normalized();
                    var x = dir.Cross(Vector3.Forward).Normalized();
                    if (x.LengthSquared() < 0.01f) x = Vector3.Right;
                    var z = x.Cross(dir).Normalized();
                    var b = new Basis(x, dir, z).ScaleLocal(new Vector3(rad, h, rad));
                    var off = new Vector3((float)rng.NextDouble() - 0.5f, -0.2f, (float)rng.NextDouble() - 0.5f) * 1.4f;
                    xf.Add(new Transform3D(b, basePos + off));
                    // colisão do prisma (cilindro ao longo do eixo do cristal)
                    var axis = new Basis(x, dir, z);
                    solid?.Invoke(new CylinderShape3D { Radius = rad * 0.85f, Height = h * 0.9f }, new Transform3D(axis, basePos + off + dir * h * 0.45f), basePos, 0f);
                }
            }
            AddMulti(root, mesh, mat, xf);
        }
    }

    /// <summary>Cogumelos gigantes bioluminescentes (Jardim Fúngico): caule, chapéu brilhante e lamelas, com colisão no caule.</summary>
    public static void GiantMushrooms(Node3D root, CaveLayout L, Random rng, int count, Color[] capColors,
        Func<Vector3, float, bool> free = null, Action<Shape3D, Transform3D, Vector3, float> solid = null)
    {
        var stems = new List<Transform3D>();
        var gillsBy = new List<Transform3D>[capColors.Length];
        for (int k = 0; k < capColors.Length; k++) gillsBy[k] = new List<Transform3D>();
        var capsBy = new List<Transform3D>[capColors.Length];
        for (int k = 0; k < capColors.Length; k++) capsBy[k] = new List<Transform3D>();
        for (int c = 0, tries = 0; c < count && tries < count * 8; tries++)
        {
            if (!L.RandomFloor(rng, 4f, 0.25f, out var p)) continue;
            float clearance = L.Ceiling(p.X, p.Z) - p.Y;
            if (clearance < 6f) continue;
            float h = Mathf.Min(clearance - 1.5f, 4f + (float)rng.NextDouble() * 7f);
            float capR = 1.6f + h * 0.28f + (float)rng.NextDouble() * 1.2f;
            if (free != null && !free(p, 1.4f)) continue;
            c++;
            float stemR = 0.35f + h * 0.04f;
            var lean = new Basis(new Vector3((float)rng.NextDouble() - 0.5f, 0, (float)rng.NextDouble() - 0.5f).Normalized(), (float)rng.NextDouble() * 0.18f);
            var up = lean * Vector3.Up;
            stems.Add(new Transform3D(lean.ScaleLocal(new Vector3(stemR, h, stemR)), p + up * h * 0.5f));
            var top = p + up * h;
            int ci = rng.Next(capColors.Length);
            capsBy[ci].Add(new Transform3D(lean.ScaleLocal(new Vector3(capR, capR * 0.55f, capR)), top - up * 0.15f));
            gillsBy[ci].Add(new Transform3D(lean.ScaleLocal(new Vector3(capR * 0.95f, 0.06f, capR * 0.95f)), top - up * 0.18f));
            solid?.Invoke(new CylinderShape3D { Radius = stemR * 1.05f, Height = h }, new Transform3D(lean, p + up * h * 0.5f), p, 1.2f);
            // cogumelos menores em volta da base
            int small = rng.Next(2, 5);
            for (int i = 0; i < small; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.Tau, d = 1.2f + (float)rng.NextDouble() * 1.6f;
                var q = new Vector3(p.X + Mathf.Cos(a) * d, 0, p.Z + Mathf.Sin(a) * d);
                q.Y = L.Ground(q.X, q.Z) - 0.05f;
                float sh = 0.5f + (float)rng.NextDouble() * 1.2f, sr = 0.3f + (float)rng.NextDouble() * 0.4f;
                stems.Add(new Transform3D(Basis.Identity.ScaleLocal(new Vector3(0.09f, sh, 0.09f)), q + Vector3.Up * sh * 0.5f));
                capsBy[rng.Next(capColors.Length)].Add(new Transform3D(Basis.Identity.ScaleLocal(new Vector3(sr, sr * 0.55f, sr)), q + Vector3.Up * sh));
            }
        }
        AddMulti(root, new CylinderMesh { TopRadius = 0.8f, BottomRadius = 1.2f, Height = 1, RadialSegments = 10, Rings = 2 }, Lit(new Color(0.82f, 0.8f, 0.72f), 0.85f), stems, 1f);
        for (int k = 0; k < capColors.Length; k++)
        {
            // lamelas: escuras, com um brilho fraco da cor do chapéu
            var gillMat = new StandardMaterial3D
            {
                AlbedoColor = capColors[k].Darkened(0.75f), EmissionEnabled = true, Emission = capColors[k], EmissionEnergyMultiplier = 0.18f,
                Roughness = 0.9f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            AddMulti(root, new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 20, Rings = 1 }, gillMat, gillsBy[k], 1f);
            var capMat = new StandardMaterial3D
            {
                AlbedoColor = capColors[k].Darkened(0.35f), EmissionEnabled = true, Emission = capColors[k], EmissionEnergyMultiplier = 0.75f,
                Roughness = 0.5f, RimEnabled = true, Rim = 0.5f,
            };
            AddMulti(root, new SphereMesh { Radius = 1, Height = 1, IsHemisphere = true, RadialSegments = 20, Rings = 6 }, capMat, capsBy[k], 1f);
        }
    }

    static void AddMulti(Node3D root, Mesh mesh, Material mat, List<Transform3D> xforms, float lowFraction = 0.6f)
    {
        if (xforms.Count == 0) return;
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xforms.Count };
        for (int i = 0; i < xforms.Count; i++) mm.SetInstanceTransform(i, xforms[i]);
        var mmi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        mmi.SetMeta(WorldBuilder.LowFractionMeta, lowFraction);
        root.AddChild(mmi);
    }
}

/// <summary>Água-viva: flutua, pulsa e deriva lentamente.</summary>
public partial class JellyNode : Node3D
{
    public float Phase;
    Vector3 _base;
    Vector3 _scale0;
    bool _init;
    public override void _Process(double delta)
    {
        if (!_init) { _base = Position; _scale0 = Scale; _init = true; }
        Phase += (float)delta;
        float pulse = Mathf.Sin(Phase * 2.2f);
        Position = _base + new Vector3(Mathf.Sin(Phase * 0.3f) * 1.2f, Mathf.Sin(Phase * 0.7f) * 0.6f + pulse * 0.08f, Mathf.Cos(Phase * 0.25f) * 1.2f);
        Scale = _scale0 * new Vector3(1f + pulse * 0.08f, 1f - pulse * 0.1f, 1f + pulse * 0.08f);
    }
}

/// <summary>Cabeça do scanner que varre de um lado para o outro.</summary>
public partial class ScannerNode : Node3D
{
    public Node3D Head;
    float _t;
    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (Head != null) Head.Rotation = new Vector3(-0.15f, Mathf.Sin(_t * 0.6f) * 0.6f, 0);
    }
}

/// <summary>Anel rúnico que gira lentamente na vertical.</summary>
public partial class RuneRing : Node3D
{
    public override void _Ready() => Rotation = new Vector3(Mathf.Pi / 2, 0, 0);
    public override void _Process(double delta) => RotateObjectLocal(Vector3.Up, (float)delta * 0.25f);
}
