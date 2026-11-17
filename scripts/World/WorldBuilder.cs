using System;
using System.Collections.Generic;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Constrói todo o mundo proceduralmente (determinístico por semente):
/// salas das regiões, túneis, portões, estações, cristais e decoração.
/// Decoração usa MultiMesh para manter poucas chamadas de desenho.
/// </summary>
public partial class WorldBuilder : Node3D
{
    public const float TunnelHalfWidth = 3.5f;
    public const float TunnelHeight = 5f;
    public readonly List<Node3D> RegionRoots = new();
    public readonly List<Crystal> Crystals = new();
    public readonly List<RegionGate> Gates = new();
    public Vector3 SpawnPoint = new(0, 0.1f, 15f);

    public override void _Ready()
    {
        Build();
    }

    void Build()
    {
        var regions = GameData.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            var root = new Node3D { Name = "Region_" + r.Id };
            AddChild(root);
            RegionRoots.Add(root);
            BuildRoom(root, r, i);
            if (i + 1 < regions.Count) BuildTunnel(r, regions[i + 1]);
        }
        // chão contínuo de colisão sob todo o mapa
        float minZ = regions[^1].CenterZ - regions[^1].HalfZ - 2;
        float maxZ = regions[0].CenterZ + regions[0].HalfZ + 2;
        AddCollider(this, new Vector3(0, -0.5f, (minZ + maxZ) / 2), new Vector3(80, 1, maxZ - minZ));
    }

    // =================== Salas ===================
    void BuildRoom(Node3D root, RegionDef r, int index)
    {
        var rng = new Random(1000 + index * 77);
        float cz = r.CenterZ, hx = r.HalfX, hz = r.HalfZ, h = r.Height;
        var wallMat = new StandardMaterial3D { AlbedoColor = r.RockColor, Roughness = 1f };
        var floorMat = new StandardMaterial3D { AlbedoColor = r.RockColor.Darkened(0.35f), Roughness = 1f };
        var ceilMat = new StandardMaterial3D { AlbedoColor = r.RockColor.Darkened(0.5f), Roughness = 1f };

        // chão e teto
        AddBox(root, new Vector3(0, -0.25f, cz), new Vector3(hx * 2 + 2, 0.5f, hz * 2 + 2), floorMat, false);
        AddBox(root, new Vector3(0, h + 0.5f, cz), new Vector3(hx * 2 + 2, 1f, hz * 2 + 2), ceilMat, true);

        bool hasNorth = index + 1 < GameData.Regions.Count;
        bool hasSouth = index > 0;
        // paredes leste/oeste
        AddBox(root, new Vector3(-hx - 0.5f, h / 2, cz), new Vector3(1, h, hz * 2 + 2), wallMat, true);
        AddBox(root, new Vector3(hx + 0.5f, h / 2, cz), new Vector3(1, h, hz * 2 + 2), wallMat, true);
        BuildWallWithOpening(root, cz - hz - 0.5f, hx, h, hasNorth, wallMat);
        BuildWallWithOpening(root, cz + hz + 0.5f, hx, h, hasSouth, wallMat);

        BuildDecoration(root, r, rng);
        BuildLights(root, r, rng);
        BuildAmbientParticles(root, r);

        // estações próximas da entrada da região
        float entranceZ = index == 0 ? cz + hz - 6f : cz + hz - 5f;
        var sell = new SellStation { Name = "SellStation" };
        root.AddChild(sell);
        sell.Position = new Vector3(-5.5f, 0, entranceZ);
        var shop = new ShopTerminal { Name = "ShopTerminal" };
        root.AddChild(shop);
        shop.Position = new Vector3(5.5f, 0, entranceZ);
        // tela (+Z local) voltada para o centro da sala
        shop.LookAt(new Vector3(0, 0, entranceZ - 4f), Vector3.Up);
        shop.RotateY(Mathf.Pi);
        if (index == 0)
        {
            SpawnPoint = new Vector3(0, 0.1f, cz + hz - 3f);
            AddPlatform(root, new Vector3(0, 0, entranceZ), r);
        }

        BuildCrystals(root, r, rng, entranceZ);
    }

    void BuildWallWithOpening(Node3D root, float z, float hx, float h, bool opening, Material mat)
    {
        if (!opening)
        {
            AddBox(root, new Vector3(0, h / 2, z), new Vector3(hx * 2 + 2, h, 1), mat, true);
            return;
        }
        float w = TunnelHalfWidth;
        float sideW = hx + 1 - w;
        AddBox(root, new Vector3(-(w + sideW / 2), h / 2, z), new Vector3(sideW, h, 1), mat, true);
        AddBox(root, new Vector3(w + sideW / 2, h / 2, z), new Vector3(sideW, h, 1), mat, true);
        AddBox(root, new Vector3(0, (h + TunnelHeight) / 2, z), new Vector3(w * 2, h - TunnelHeight, 1), mat, true);
    }

    void BuildTunnel(RegionDef from, RegionDef to)
    {
        var root = new Node3D { Name = $"Tunnel_{from.Id}_{to.Id}" };
        AddChild(root);
        float z0 = from.CenterZ - from.HalfZ - 1;
        float z1 = to.CenterZ + to.HalfZ + 1;
        float len = z0 - z1;
        float cz = (z0 + z1) / 2;
        var mat = new StandardMaterial3D { AlbedoColor = from.RockColor.Lerp(to.RockColor, 0.5f).Darkened(0.2f), Roughness = 1 };
        AddBox(root, new Vector3(-TunnelHalfWidth - 0.5f, TunnelHeight / 2, cz), new Vector3(1, TunnelHeight, len + 0.2f), mat, true);
        AddBox(root, new Vector3(TunnelHalfWidth + 0.5f, TunnelHeight / 2, cz), new Vector3(1, TunnelHeight, len + 0.2f), mat, true);
        AddBox(root, new Vector3(0, TunnelHeight + 0.5f, cz), new Vector3(TunnelHalfWidth * 2 + 2, 1, len + 0.2f), mat, true);
        AddBox(root, new Vector3(0, -0.25f, cz), new Vector3(TunnelHalfWidth * 2, 0.5f, len + 0.2f), mat.Duplicate() as Material, false);

        // faixa luminosa no chão guiando o caminho
        var strip = MeshFactory.Emissive(to.AccentColor.Darkened(0.4f), to.AccentColor, 1.5f);
        for (float z = z1 + 1; z < z0; z += 2.2f)
            AddMeshOnly(root, new BoxMesh { Size = new Vector3(0.6f, 0.03f, 1.0f) }, strip, new Vector3(0, 0.02f, z));
        var light = new OmniLight3D { LightColor = to.LightColor, LightEnergy = 1.2f, OmniRange = 9f, Position = new Vector3(0, TunnelHeight - 0.8f, cz) };
        root.AddChild(light);

        var gate = new RegionGate { Name = "Gate_" + to.Id };
        gate.Init(to, TunnelHalfWidth * 2, TunnelHeight);
        root.AddChild(gate);
        gate.Position = new Vector3(0, 0, z0 - 3f);
        Gates.Add(gate);
    }

    void AddPlatform(Node3D root, Vector3 pos, RegionDef r)
    {
        // plataforma decorativa do acampamento inicial
        var mat = MeshFactory.Emissive(new Color(0.1f, 0.09f, 0.14f), r.AccentColor, 0.06f);
        AddMeshOnly(root, new CylinderMesh { TopRadius = 9f, BottomRadius = 9.2f, Height = 0.08f, RadialSegments = 24, Rings = 1 }, mat, pos + new Vector3(0, 0.02f, 0));
        var lantern = MeshFactory.Emissive(new Color(1f, 0.8f, 0.5f), new Color(1f, 0.7f, 0.35f), 2f);
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6 + 0.3f;
            var p = pos + new Vector3(Mathf.Cos(a) * 8.5f, 0, Mathf.Sin(a) * 8.5f);
            AddMeshOnly(root, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 1.6f, RadialSegments = 6, Rings = 1 }, MeshFactory.Metal(new Color(0.3f, 0.25f, 0.2f)), p + new Vector3(0, 0.8f, 0));
            AddMeshOnly(root, new SphereMesh { Radius = 0.14f, Height = 0.28f, RadialSegments = 8, Rings = 4 }, lantern, p + new Vector3(0, 1.7f, 0));
        }
        var warm = new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.45f), LightEnergy = 1.3f, OmniRange = 11f, Position = pos + new Vector3(0, 3.5f, 0) };
        root.AddChild(warm);
    }

    // =================== Cristais ===================
    void BuildCrystals(Node3D root, RegionDef r, Random rng, float entranceZ)
    {
        var placed = new List<Vector2>();
        // reserva áreas de estações, caminho central e túneis
        placed.Add(new Vector2(-5.5f, entranceZ)); placed.Add(new Vector2(5.5f, entranceZ));
        int attempts = 0;
        int slots = r.CrystalSlots;
        if (r.Id == "core")
        {
            // formação especial central
            var heart = MakeCrystal(root, r, GameData.Mat("heart"), new Vector3(0, 0, r.CenterZ - 4f), rng, 1f);
            heart.PickMaterial = _ => GameData.Mat("heart");
            placed.Add(new Vector2(0, r.CenterZ - 4f));
            BuildHeartPedestal(root, new Vector3(0, 0, r.CenterZ - 4f), r);
        }
        while (placed.Count < slots + 2 && attempts < 2000)
        {
            attempts++;
            float x = (float)(rng.NextDouble() * 2 - 1) * (r.HalfX - 2.5f);
            float z = r.CenterZ + (float)(rng.NextDouble() * 2 - 1) * (r.HalfZ - 2.5f);
            var p = new Vector2(x, z);
            if (MathF.Abs(x) < 2.2f) continue; // corredor central livre
            if (r.Index == 0 && new Vector2(0, entranceZ).DistanceTo(p) < 8.5f) continue; // acampamento livre
            bool ok = true;
            float minD = r.Index == 2 && new Vector2(0, r.CenterZ - 4f).DistanceTo(p) < 9 ? 9 : 3.2f;
            foreach (var q in placed) if (q.DistanceTo(p) < minD) { ok = false; break; }
            if (!ok) continue;
            placed.Add(p);
            float size = 0.8f + (float)rng.NextDouble() * 0.55f;
            var mat = PickFor(r, rng);
            var c = MakeCrystal(root, r, mat, new Vector3(x, 0, z), rng, size);
            c.PickMaterial = rr => PickFor(r, rr);
        }
        // na região inicial, garante cristais fáceis bem perto do jogador
        if (r.Index == 0)
        {
            var q = GameData.Mat("quartz");
            MakeCrystal(root, r, q, new Vector3(-3.2f, 0, entranceZ - 9.5f), rng, 0.9f).PickMaterial = rr => PickFor(r, rr);
            MakeCrystal(root, r, q, new Vector3(3.4f, 0, entranceZ - 10.5f), rng, 0.9f).PickMaterial = rr => PickFor(r, rr);
        }
    }

    public static MaterialDef PickFor(RegionDef r, Random rng)
    {
        if (rng.NextDouble() < r.RareChance) return GameData.Mat(r.RareMaterialId);
        double total = 0;
        foreach (var s in r.Spawns) total += s.Weight;
        double roll = rng.NextDouble() * total;
        foreach (var s in r.Spawns)
        {
            roll -= s.Weight;
            if (roll <= 0) return GameData.Mat(s.MaterialId);
        }
        return GameData.Mat(r.Spawns[0].MaterialId);
    }

    Crystal MakeCrystal(Node3D root, RegionDef r, MaterialDef mat, Vector3 pos, Random rng, float size)
    {
        var c = new Crystal { Name = $"Crystal_{Crystals.Count}" };
        c.Init(mat, rng.Next(), size);
        root.AddChild(c);
        c.Position = pos;
        c.Rotation = new Vector3(0, (float)rng.NextDouble() * Mathf.Tau, 0);
        Crystals.Add(c);
        return c;
    }

    void BuildHeartPedestal(Node3D root, Vector3 pos, RegionDef r)
    {
        var mat = MeshFactory.Emissive(new Color(0.18f, 0.08f, 0.07f), r.AccentColor, 0.12f);
        AddMeshOnly(root, new CylinderMesh { TopRadius = 4.2f, BottomRadius = 4.8f, Height = 0.15f, RadialSegments = 20, Rings = 1 }, mat, pos + new Vector3(0, 0.05f, 0));
        var rune = MeshFactory.Emissive(r.AccentColor, r.AccentColor, 2.5f);
        for (int i = 0; i < 8; i++)
        {
            float a = Mathf.Tau * i / 8;
            var p = pos + new Vector3(Mathf.Cos(a) * 5.5f, 0, Mathf.Sin(a) * 5.5f);
            var spike = AddMeshOnly(root, MeshFactory.Spike(4, 0.35f, 3.2f + (i % 2) * 1.2f), MeshFactory.RockMaterial(r.RockColor), p);
            spike.Rotation = new Vector3(0, a, 0);
            AddMeshOnly(root, new BoxMesh { Size = new Vector3(0.12f, 0.6f, 0.12f) }, rune, p + new Vector3(0, 1.4f, 0) + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * -0.28f);
            AddCollider(root, p + new Vector3(0, 1.2f, 0), new Vector3(0.6f, 2.4f, 0.6f));
        }
    }

    // =================== Decoração ===================
    void BuildDecoration(Node3D root, RegionDef r, Random rng)
    {
        float cz = r.CenterZ, hx = r.HalfX, hz = r.HalfZ, h = r.Height;
        var rockMat = MeshFactory.RockMaterial(r.RockColor.Lightened(0.15f));
        float density = Settings.Quality switch { GraphicsQuality.Low => 0.6f, GraphicsQuality.Medium => 0.85f, _ => 1f };

        // rochas grandes ao longo das paredes (quebram o formato de caixa)
        var wallRocks = new List<Transform3D>();
        int perimeter = (int)((hx + hz) * 2 / 2.2f * density);
        for (int i = 0; i < perimeter; i++)
        {
            Vector3 p;
            int side = rng.Next(4);
            float t = (float)(rng.NextDouble() * 2 - 1);
            p = side switch
            {
                0 => new Vector3(-hx, 0, cz + t * hz),
                1 => new Vector3(hx, 0, cz + t * hz),
                2 => new Vector3(t * hx, 0, cz - hz),
                _ => new Vector3(t * hx, 0, cz + hz),
            };
            // mantém aberturas dos túneis livres
            if ((side == 2 || side == 3) && MathF.Abs(p.X) < TunnelHalfWidth + 2.5f) continue;
            float s = 1.6f + (float)rng.NextDouble() * 2.8f;
            p.Y = (float)rng.NextDouble() * h * 0.7f;
            var basis = new Basis(new Vector3(0, 1, 0), (float)rng.NextDouble() * Mathf.Tau).ScaleLocal(new Vector3(s, s * (0.8f + (float)rng.NextDouble() * 0.6f), s));
            wallRocks.Add(new Transform3D(basis, p));
        }
        AddMulti(root, MeshFactory.Rock(11, 0.4f), rockMat, wallRocks);

        // estalactites e estalagmites
        var spikesDown = new List<Transform3D>();
        var spikesUp = new List<Transform3D>();
        int spikeCount = (int)(hx * hz / 14f * density);
        for (int i = 0; i < spikeCount; i++)
        {
            float x = (float)(rng.NextDouble() * 2 - 1) * (hx - 1);
            float z = cz + (float)(rng.NextDouble() * 2 - 1) * (hz - 1);
            float len = 0.8f + (float)rng.NextDouble() * (h * 0.35f);
            float rad = 0.25f + (float)rng.NextDouble() * 0.5f;
            var b = new Basis(new Vector3(1, 0, 0), Mathf.Pi).ScaleLocal(new Vector3(rad, len, rad));
            spikesDown.Add(new Transform3D(b, new Vector3(x, h, z)));
            if (i % 3 == 0 && MathF.Abs(x) > hx * 0.75f)
            {
                var b2 = Basis.Identity.ScaleLocal(new Vector3(rad * 1.2f, len * 0.5f, rad * 1.2f));
                spikesUp.Add(new Transform3D(b2, new Vector3(x, 0, z)));
            }
        }
        var spikeMesh = MeshFactory.Spike(5, 1f, 1f);
        AddMulti(root, spikeMesh, rockMat, spikesDown);
        AddMulti(root, spikeMesh, rockMat, spikesUp);

        // pedras pequenas no chão
        var pebbles = new List<Transform3D>();
        for (int i = 0; i < (int)(70 * density); i++)
        {
            float x = (float)(rng.NextDouble() * 2 - 1) * (hx - 1);
            float z = cz + (float)(rng.NextDouble() * 2 - 1) * (hz - 1);
            float s = 0.12f + (float)rng.NextDouble() * 0.35f;
            pebbles.Add(new Transform3D(new Basis(Vector3.Up, (float)rng.NextDouble() * 6f).ScaleLocal(Vector3.One * s), new Vector3(x, s * 0.2f, z)));
        }
        AddMulti(root, MeshFactory.Rock(3, 0.3f), rockMat, pebbles);

        // cristais decorativos (não mineráveis) incrustados nas paredes e teto
        var deco = new List<Transform3D>();
        for (int i = 0; i < (int)(60 * density); i++)
        {
            int side = rng.Next(3);
            float t = (float)(rng.NextDouble() * 2 - 1);
            Vector3 p; Vector3 up;
            if (side == 0) { p = new Vector3(-hx + 0.2f, (float)rng.NextDouble() * h, cz + t * hz); up = Vector3.Right; }
            else if (side == 1) { p = new Vector3(hx - 0.2f, (float)rng.NextDouble() * h, cz + t * hz); up = Vector3.Left; }
            else { p = new Vector3(t * hx, h - 0.1f, cz + (float)(rng.NextDouble() * 2 - 1) * hz); up = Vector3.Down; }
            up = (up + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.9f).Normalized();
            var basis = BasisFromUp(up).ScaleLocal(new Vector3(0.12f, 0.4f + (float)rng.NextDouble() * 0.9f, 0.12f) * (1f + (float)rng.NextDouble()));
            deco.Add(new Transform3D(basis, p));
        }
        var decoMat = MeshFactory.Emissive(r.AccentColor, r.AccentColor, 1.6f);
        AddMulti(root, MeshFactory.CrystalPrism(6, 1, 1, 0.3f), decoMat, deco);

        // marcas especiais por região
        if (r.Id == "depths")
        {
            // poças de água luminescente
            var pool = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.1f, 0.5f, 0.6f, 0.6f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                EmissionEnabled = true, Emission = new Color(0.1f, 0.6f, 0.7f), EmissionEnergyMultiplier = 0.8f,
                Metallic = 0.6f, Roughness = 0.05f,
            };
            for (int i = 0; i < 7; i++)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * (hx - 4);
                float z = cz + (float)(rng.NextDouble() * 2 - 1) * (hz - 4);
                if (MathF.Abs(x) < 3) x += MathF.Sign(x == 0 ? 1 : x) * 4;
                float rad = 1.5f + (float)rng.NextDouble() * 2.5f;
                AddMeshOnly(root, new CylinderMesh { TopRadius = rad, BottomRadius = rad, Height = 0.02f, RadialSegments = 14, Rings = 1 }, pool, new Vector3(x, 0.02f, z));
            }
        }
        else if (r.Id == "core")
        {
            // fissuras de lava/energia no chão
            var crack = MeshFactory.Emissive(new Color(1f, 0.35f, 0.1f), new Color(1f, 0.35f, 0.05f), 2.6f);
            var cracks = new List<Transform3D>();
            for (int i = 0; i < 40; i++)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * (hx - 1);
                float z = cz + (float)(rng.NextDouble() * 2 - 1) * (hz - 1);
                var b = new Basis(Vector3.Up, (float)rng.NextDouble() * Mathf.Tau).ScaleLocal(new Vector3(0.12f + (float)rng.NextDouble() * 0.2f, 1, 1.5f + (float)rng.NextDouble() * 4f));
                cracks.Add(new Transform3D(b, new Vector3(x, 0.015f, z)));
            }
            AddMulti(root, new BoxMesh { Size = new Vector3(1, 0.02f, 1) }, crack, cracks);
            // rochas flutuantes animadas
            for (int i = 0; i < 9; i++)
            {
                float a = Mathf.Tau * i / 9f;
                float rad = 10f + (float)rng.NextDouble() * 10f;
                var fr = new FloatingRock
                {
                    Mesh = MeshFactory.Rock(20 + i, 0.4f),
                    MaterialOverride = MeshFactory.RockMaterial(r.RockColor.Lightened(0.2f)),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                    Scale = Vector3.One * (0.6f + (float)rng.NextDouble() * 1.2f),
                };
                root.AddChild(fr);
                fr.Position = new Vector3(Mathf.Cos(a) * rad, 6f + (float)rng.NextDouble() * 6f, cz + Mathf.Sin(a) * rad * 0.9f);
                fr.Phase = (float)rng.NextDouble() * 10f;
            }
        }
    }

    static Basis BasisFromUp(Vector3 up)
    {
        var x = up.Cross(MathF.Abs(up.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        var z = x.Cross(up).Normalized();
        return new Basis(x, up, z);
    }

    void BuildLights(Node3D root, RegionDef r, Random rng)
    {
        int count = Settings.Quality == GraphicsQuality.Low ? 2 : r.Index == 0 ? 4 : 5;
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Tau * i / count + 0.4f;
            var light = new OmniLight3D
            {
                LightColor = i % 2 == 0 ? r.LightColor : r.AccentColor,
                LightEnergy = Settings.Quality == GraphicsQuality.Low ? 2.0f : 1.6f,
                OmniRange = Mathf.Max(r.HalfX, r.HalfZ) * (Settings.Quality == GraphicsQuality.Low ? 1.1f : 0.85f),
                OmniAttenuation = 1.2f,
                ShadowEnabled = false,
                Position = new Vector3(Mathf.Cos(a) * r.HalfX * 0.55f, r.Height * 0.7f, r.CenterZ + Mathf.Sin(a) * r.HalfZ * 0.55f),
            };
            root.AddChild(light);
        }
    }

    void BuildAmbientParticles(Node3D root, RegionDef r)
    {
        int amount = Settings.Quality switch { GraphicsQuality.Low => 20, GraphicsQuality.Medium => 45, _ => 80 };
        var p = new CpuParticles3D
        {
            Amount = amount,
            Lifetime = 8f,
            Preprocess = 8f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(r.HalfX, r.Height / 2, r.HalfZ),
            Position = new Vector3(0, r.Height / 2, r.CenterZ),
            Mesh = new QuadMesh { Size = new Vector2(0.06f, 0.06f) },
            Gravity = Vector3.Zero,
            Direction = Vector3.Up,
            Spread = 180,
            InitialVelocityMin = 0.05f,
            InitialVelocityMax = 0.3f,
            Color = r.AccentColor,
            ParticleFlagAlignY = false,
        };
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            VertexColorUseAsAlbedo = true,
        };
        if (r.Id == "depths")
        {
            // gotejamento frio
            p.Gravity = new Vector3(0, -2.5f, 0);
            p.Direction = Vector3.Down;
            p.Spread = 5;
            p.Mesh = new QuadMesh { Size = new Vector2(0.03f, 0.16f) };
        }
        else if (r.Id == "core")
        {
            // brasas subindo
            p.Gravity = new Vector3(0, 0.8f, 0);
            p.Color = new Color(1f, 0.55f, 0.2f);
            p.InitialVelocityMax = 0.8f;
        }
        p.MaterialOverride = mat;
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1, 1, 1, 0));
        ramp.SetColor(1, new Color(1, 1, 1, 0));
        ramp.AddPoint(0.2f, new Color(1, 1, 1, 1));
        ramp.AddPoint(0.8f, new Color(1, 1, 1, 0.8f));
        p.ColorRamp = ramp;
        root.AddChild(p);
    }

    // =================== Utilidades de construção ===================
    void AddBox(Node3D root, Vector3 pos, Vector3 size, Material mat, bool collide)
    {
        AddMeshOnly(root, new BoxMesh { Size = size }, mat, pos);
        if (collide) AddCollider(root, pos, size);
    }

    static MeshInstance3D AddMeshOnly(Node3D root, Mesh mesh, Material mat, Vector3 pos)
    {
        var mi = new MeshInstance3D
        {
            Mesh = mesh,
            MaterialOverride = mat,
            Position = pos,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(mi);
        return mi;
    }

    static void AddCollider(Node3D root, Vector3 pos, Vector3 size)
    {
        var body = new StaticBody3D { CollisionLayer = PlayerController.WorldLayer, CollisionMask = 0, Position = pos };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        root.AddChild(body);
    }

    static void AddMulti(Node3D root, Mesh mesh, Material mat, List<Transform3D> xforms)
    {
        if (xforms.Count == 0) return;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = xforms.Count,
        };
        for (int i = 0; i < xforms.Count; i++) mm.SetInstanceTransform(i, xforms[i]);
        var mmi = new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(mmi);
    }

    /// <summary>Região em que um ponto está (por coordenada Z).</summary>
    public static RegionDef RegionAt(Vector3 p)
    {
        RegionDef best = GameData.Regions[0];
        foreach (var r in GameData.Regions)
            if (p.Z <= r.CenterZ + r.HalfZ + 0.5f) best = r;
        return best;
    }
}

/// <summary>Rocha que flutua lentamente (Núcleo Ancestral).</summary>
public partial class FloatingRock : MeshInstance3D
{
    public float Phase;
    Vector3 _base;
    bool _init;

    public override void _Process(double delta)
    {
        if (!_init) { _base = Position; _init = true; }
        Phase += (float)delta;
        Position = _base + new Vector3(0, Mathf.Sin(Phase * 0.6f) * 0.5f, 0);
        RotateY((float)delta * 0.1f);
    }
}
