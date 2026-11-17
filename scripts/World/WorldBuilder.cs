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
/// cavernas orgânicas com relevo (CaveLayout + CaveTerrain), túneis com portões,
/// estações, depósitos no chão e nas paredes, câmaras secretas e decoração.
/// Decoração usa MultiMesh para manter poucas chamadas de desenho.
/// </summary>
public partial class WorldBuilder : Node3D
{
    public const float TunnelHalfWidth = 3.5f;
    public const float TunnelHeight = 5f;
    public readonly List<Node3D> RegionRoots = new();
    public readonly List<CaveLayout> Layouts = new();
    public readonly List<Crystal> Crystals = new();
    public readonly List<RegionGate> Gates = new();
    public Vector3 SpawnPoint = new(0, 0.1f, 15f);
    public MineElevator Elevator;
    public PlayerHouse House;
    public SellStation SellStation;
    int _tick;

    public override void _Ready()
    {
        Build();
    }

    public override void _Process(double delta)
    {
        if (++_tick % 30 == 0) UpdateRespawnRates();
    }

    /// <summary>
    /// Renascimento dinâmico: quanto mais depósitos quebrados numa região,
    /// mais rápido eles voltam. Com a região toda minerada, renascem até 6x mais rápido.
    /// </summary>
    public void UpdateRespawnRates()
    {
        var broken = new int[GameData.Regions.Count];
        var total = new int[GameData.Regions.Count];
        if (_crystalRegion == null || _crystalRegion.Length != Crystals.Count)
        {
            _crystalRegion = new int[Crystals.Count];
            for (int k = 0; k < Crystals.Count; k++) _crystalRegion[k] = RegionAt(Crystals[k].GlobalPosition).Index;
        }
        for (int k = 0; k < Crystals.Count; k++)
        {
            var c = Crystals[k];
            if (c.Material.Special) continue;
            int i = _crystalRegion[k];
            total[i]++;
            if (!c.IsMineable) broken[i]++;
        }
        for (int k = 0; k < Crystals.Count; k++)
        {
            var c = Crystals[k];
            int i = _crystalRegion[k];
            c.RespawnRate = c.Material.Special ? 1f : RespawnRateFor(total[i] == 0 ? 0 : broken[i] / (float)total[i]);
        }
    }

    int[] _crystalRegion;
    public static float RespawnRateFor(float brokenFraction) => 1f + 5f * brokenFraction * brokenFraction;

    /// <summary>Caverna da região em que um ponto está.</summary>
    public CaveLayout LayoutAt(Vector3 p) => Layouts[RegionAt(p).Index];

    /// <summary>Altura do chão da caverna num ponto (0 fora das cavernas, ex.: túneis).</summary>
    public float GroundAt(float x, float z)
    {
        var L = Layouts[RegionAt(new Vector3(x, 0, z)).Index];
        return L.InGrid(x, z) ? L.Ground(x, z) : 0f;
    }

    /// <summary>Um ponto salvo ainda é válido (dentro de uma caverna, alcançável, ou na casa)?</summary>
    public bool IsSafeSpot(Vector3 p)
    {
        if (PlayerHouse.Contains(p)) return true;
        var L = LayoutAt(p);
        return L.InGrid(p.X, p.Z) && L.Reachable(p.X, p.Z) && Mathf.Abs(p.Y - L.Ground(p.X, p.Z)) < 2.5f;
    }

    void Build()
    {
        ulong t0 = Time.GetTicksMsec();
        var regions = GameData.Regions;
        for (int i = 0; i < regions.Count; i++)
        {
            var r = regions[i];
            var L = new CaveLayout(r, 4242 + i * 101);
            Layouts.Add(L);
            var root = new Node3D { Name = "Region_" + r.Id };
            AddChild(root);
            RegionRoots.Add(root);
            var terrain = new CaveTerrain { Name = "Terrain" };
            root.AddChild(terrain);
            terrain.Build(L, r);
            BuildRegion(root, r, L, i);
            if (i + 1 < regions.Count) BuildTunnel(r, regions[i + 1]);
        }

        House = new PlayerHouse { Name = "PlayerHouse" };
        AddChild(House);

        // agrupa malhas estáticas por material em cada região e túnel (o terreno fica de fora)
        int saved = 0;
        foreach (Node c in GetChildren())
            if (c.GetType() == typeof(Node3D)) saved += StaticBatcher.Batch((Node3D)c);
        GD.Print($"[World] Agrupamento estático: {saved} instâncias de malha a menos");
        GD.Print($"[World] Mundo construído em {Time.GetTicksMsec() - t0} ms ({Crystals.Count} cristais)");
    }

    // =================== Regiões ===================
    void BuildRegion(Node3D root, RegionDef r, CaveLayout L, int index)
    {
        var rng = new Random(1000 + index * 77);
        var reserved = new List<Vector3>();

        if (index == 0)
        {
            // acampamento: única Estação de Venda do mapa, terminal e elevador para casa
            var cc = L.OnGround(L.Camp.A.X, L.Camp.A.Y);
            var sell = new SellStation { Name = "SellStation" };
            root.AddChild(sell);
            sell.Position = L.OnGround(cc.X - 6.5f, cc.Z + 4f);
            sell.LookAt(cc + new Vector3(0, sell.Position.Y - cc.Y, -1f), Vector3.Up);
            sell.RotateY(Mathf.Pi);
            SellStation = sell;
            var shop = new ShopTerminal { Name = "ShopTerminal" };
            root.AddChild(shop);
            shop.Position = L.OnGround(cc.X + 6.5f, cc.Z + 4f);
            shop.LookAt(cc + new Vector3(0, shop.Position.Y - cc.Y, -1f), Vector3.Up);
            shop.RotateY(Mathf.Pi);
            Elevator = new MineElevator { Name = "MineElevator" };
            root.AddChild(Elevator);
            Elevator.Position = L.OnGround(cc.X, cc.Z + 11.3f, -0.05f);
            Elevator.Rotation = new Vector3(0, Mathf.Pi, 0); // frente (+Z local) voltada para dentro da caverna
            SpawnPoint = L.OnGround(cc.X, cc.Z + 8.6f, 0.1f);
            AddPlatform(root, cc, r);
            reserved.Add(sell.Position); reserved.Add(shop.Position); reserved.Add(Elevator.Position); reserved.Add(cc);
        }
        else
        {
            var shop = new ShopTerminal { Name = "ShopTerminal" };
            root.AddChild(shop);
            float tz = r.CenterZ + r.HalfZ - 9f;
            shop.Position = L.OnGround(4.6f, tz);
            shop.LookAt(L.OnGround(0, tz - 4f) + new Vector3(0, shop.Position.Y - L.Ground(0, tz - 4f), 0), Vector3.Up);
            shop.RotateY(Mathf.Pi);
            reserved.Add(shop.Position);
        }

        BuildDecoration(root, r, L, rng);
        BuildLights(root, r, L);
        BuildAmbientParticles(root, r);
        BuildSetPieces(root, r, L, new Random(500 + index), reserved);
        BuildChamberSigns(root, r, L);
        BuildCrystals(root, r, L, rng, reserved);
    }

    /// <summary>Cenografia por região (posto avançado, cachoeiras, flora, ruínas, lago).</summary>
    void BuildSetPieces(Node3D root, RegionDef r, CaveLayout L, Random rng, List<Vector3> reserved)
    {
        // posto avançado: na primeira câmara grande (ou no acampamento)
        CaveFeature host = L.Camp;
        if (host == null)
            foreach (var c in L.Chambers)
                if (c.Wing == null && c != L.Heart && c != L.Lake && c.Radius >= 9f && Mathf.Abs(c.Level) < 0.1f) { host = c; break; }
        if (host != null && r.Id != "core")
        {
            var hc = host.A;
            var o = L.OnGround(hc.X - (host == L.Camp ? 9.5f : 4f), hc.Y + 3f);
            Decor.Outpost(root, o, Mathf.Pi / 2);
            Decor.RefineryPod(root, L.OnGround(hc.X - 9.8f, hc.Y + (host == L.Camp ? 6.5f : -4f)), 0.6f);
            Decor.Crate(root, L.OnGround(hc.X - 6.2f, hc.Y - 3.5f), 0.4f, 1f, true);
            Decor.Crate(root, L.OnGround(hc.X - 5.4f, hc.Y - 2.3f), -0.2f, 0.8f, true);
            reserved.Add(o); reserved.Add(L.OnGround(hc.X - 9.8f, hc.Y + 6.5f));
            reserved.Add(L.OnGround(hc.X - 6f, hc.Y - 3f));
            if (host == L.Camp)
            {
                Decor.OreScanner(root, L.OnGround(hc.X - 4.5f, hc.Y - 5.5f), 0.9f);
                Decor.FloorGrate(root, L.OnGround(hc.X - 6.5f, hc.Y + 2.8f, 0.01f), new Vector2(2.6f, 1.2f), 0);
                Decor.FloorGrate(root, L.OnGround(hc.X + 6.5f, hc.Y + 2.8f, 0.01f), new Vector2(2.6f, 1.2f), 0);
                reserved.Add(L.OnGround(hc.X - 4.5f, hc.Y - 5.5f));
            }
        }
        var water = r.Id switch { "depths" => new Color(0.4f, 1f, 0.85f), "abyss" => new Color(0.4f, 0.9f, 1f), _ => new Color(0.45f, 0.85f, 1f) };
        int falls = r.Id switch { "caves" => 2, "depths" => 3, "abyss" => 4, _ => 0 };
        for (int i = 0; i < falls; i++) Waterfall(root, L, rng, water);
        switch (r.Id)
        {
            case "caves":
                Decor.Mushrooms(root, L, rng, 16, new Color(0.3f, 1f, 0.9f));
                Decor.Ferns(root, L, rng, 22, new Color(0.95f, 0.42f, 0.15f));
                Decor.GiantClusters(root, L, rng, 5, new[] { new Color(0.3f, 0.85f, 1f), new Color(0.65f, 0.35f, 1f) }, r.Height);
                break;
            case "depths":
                Decor.Mushrooms(root, L, rng, 14, new Color(0.4f, 0.6f, 1f));
                Decor.Ferns(root, L, rng, 14, new Color(0.2f, 0.7f, 0.6f));
                Decor.GiantClusters(root, L, rng, 5, new[] { new Color(0.25f, 0.5f, 1f), new Color(0.2f, 0.95f, 0.6f), new Color(0.95f, 0.4f, 0.9f) }, r.Height);
                break;
            case "abyss":
                if (L.Lake != null)
                {
                    var lc = L.Lake;
                    var center = new Vector3(lc.A.X, lc.Level + 0.22f, lc.A.Y);
                    Decor.Lake(root, center, lc.Radius + 3f, r.AccentColor);
                    Decor.Jellyfish(root, center, lc.Radius, lc.HA, rng, 12);
                }
                Decor.GiantClusters(root, L, rng, 5, new[] { new Color(0.3f, 0.95f, 1f), new Color(1f, 0.45f, 0.85f) }, r.Height);
                break;
            case "core":
                if (L.Heart != null) Decor.AncientRuins(root, L, L.OnGround(L.Heart.A.X, L.Heart.A.Y), rng);
                Decor.GiantClusters(root, L, rng, 5, new[] { new Color(0.8f, 0.35f, 1f), new Color(1f, 0.75f, 0.25f) }, r.Height);
                break;
        }
    }

    static void Waterfall(Node3D root, CaveLayout L, Random rng, Color color)
    {
        if (!L.RandomNearWall(rng, out var p, -1.1f, -0.5f)) return;
        var inward = L.Inward(p.X, p.Z);
        float h = L.Ceiling(p.X, p.Z) - p.Y;
        if (h < 5f) return;
        var foot = p - inward * 0.6f;
        foot.Y = p.Y;
        Decor.Waterfall(root, foot, h + 0.5f, 2f + (float)rng.NextDouble() * 1.5f, Mathf.Atan2(inward.X, inward.Z), color);
    }

    /// <summary>Placas com o nome das câmaras de minério em destaque.</summary>
    void BuildChamberSigns(Node3D root, RegionDef r, CaveLayout L)
    {
        foreach (var c in L.Chambers)
        {
            if (c.Wing == null) continue;
            var featured = GameData.Mat(c.Wing.FeaturedMaterialId);
            root.AddChild(new Label3D
            {
                Name = "Sign_" + c.Id,
                Text = c.Wing.Name.ToUpperInvariant(),
                FontSize = 64, PixelSize = 0.0065f, OutlineSize = 12,
                Modulate = featured.Glow.Lightened(0.35f), OutlineModulate = new Color(0, 0, 0, 0.85f),
                Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Shaded = false,
                Position = new Vector3(c.A.X, c.Level + Mathf.Min(4.2f, c.HA - 1.5f), c.A.Y),
                VisibilityRangeEnd = 35f,
            });
        }
    }

    // =================== Túneis entre regiões ===================
    void BuildTunnel(RegionDef from, RegionDef to)
    {
        var root = new Node3D { Name = $"Tunnel_{from.Id}_{to.Id}" };
        AddChild(root);
        float z0 = from.CenterZ - from.HalfZ;   // borda norte da região de origem
        float z1 = to.CenterZ + to.HalfZ;       // borda sul da região seguinte
        float len = z0 - z1;
        float cz = (z0 + z1) / 2;
        var mat = new StandardMaterial3D { AlbedoColor = from.RockColor.Lerp(to.RockColor, 0.5f).Darkened(0.2f), Roughness = 1 };
        var beam = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.22f, 0.14f), Roughness = 0.9f };
        AddBox(root, new Vector3(-TunnelHalfWidth - 0.5f, TunnelHeight / 2, cz), new Vector3(1, TunnelHeight, len + 0.2f), mat, true);
        AddBox(root, new Vector3(TunnelHalfWidth + 0.5f, TunnelHeight / 2, cz), new Vector3(1, TunnelHeight, len + 0.2f), mat, true);
        AddBox(root, new Vector3(0, TunnelHeight + 0.5f, cz), new Vector3(TunnelHalfWidth * 2 + 2, 1, len + 0.2f), mat, true);
        AddBox(root, new Vector3(0, -0.25f, cz), new Vector3(TunnelHalfWidth * 2, 0.5f, len + 0.2f), mat.Duplicate() as Material, true);
        // escoramento de madeira (galeria de mina)
        for (float z = z1 + 2f; z < z0 - 1f; z += 3.5f)
        {
            AddMeshOnly(root, new BoxMesh { Size = new Vector3(0.3f, TunnelHeight, 0.3f) }, beam, new Vector3(-TunnelHalfWidth + 0.15f, TunnelHeight / 2, z));
            AddMeshOnly(root, new BoxMesh { Size = new Vector3(0.3f, TunnelHeight, 0.3f) }, beam, new Vector3(TunnelHalfWidth - 0.15f, TunnelHeight / 2, z));
            AddMeshOnly(root, new BoxMesh { Size = new Vector3(TunnelHalfWidth * 2, 0.3f, 0.35f) }, beam, new Vector3(0, TunnelHeight - 0.15f, z));
        }
        // molduras nas duas bocas: fecham o vão entre o túnel e a rocha da caverna
        foreach (float zb in new[] { z0, z1 })
        {
            float off = zb == z0 ? -0.5f : 0.5f;
            AddBox(root, new Vector3(-TunnelHalfWidth - 5f, 6f, zb + off), new Vector3(10f, 22f, 1f), mat, true);
            AddBox(root, new Vector3(TunnelHalfWidth + 5f, 6f, zb + off), new Vector3(10f, 22f, 1f), mat, true);
            AddBox(root, new Vector3(0, TunnelHeight + 8.5f, zb + off), new Vector3(TunnelHalfWidth * 2 + 0.2f, 17f, 1f), mat, true);
        }

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


    // =================== Depósitos (chão e paredes) ===================
    void BuildCrystals(Node3D root, RegionDef r, CaveLayout L, Random rng, List<Vector3> reserved)
    {
        var placed = new List<Vector3>(reserved);
        if (L.Heart != null)
        {
            // formação especial no centro da Câmara do Coração
            var hp = L.OnGround(L.Heart.A.X, L.Heart.A.Y);
            var heart = MakeCrystal(root, GameData.Mat("heart"), hp, rng, 1f, null);
            heart.PickMaterial = _ => GameData.Mat("heart");
            heart.StandPoint = hp + new Vector3(0, 0, 4.2f);
            placed.Add(hp);
            BuildHeartPedestal(root, hp, r);
        }
        bool NearPlaced(Vector3 p, float min)
        {
            foreach (var q in placed) if (q.DistanceTo(p) < min) return true;
            return false;
        }
        float minHeart = 9f;
        bool NearHeart(Vector3 p) => L.Heart != null && new Vector2(p.X - L.Heart.A.X, p.Z - L.Heart.A.Y).Length() < minHeart;
        bool InCamp(Vector3 p) => L.Camp != null && new Vector2(p.X - L.Camp.A.X, p.Z - L.Camp.A.Y).Length() < 9.5f;

        int wallSlots = (int)(r.CrystalSlots * 0.32f);
        int floorSlots = r.CrystalSlots - wallSlots;
        int made = 0;
        for (int tries = 0; made < floorSlots && tries < 4000; tries++)
        {
            if (!L.RandomFloor(rng, 1.3f, 0.22f, out var p)) continue;
            if (NearPlaced(p, 3.2f) || NearHeart(p) || InCamp(p)) continue;
            if (!FindStand(L, p, rng, out var stand)) continue;
            var c = MakeCrystal(root, MaterialFor(r, L, p, rng), p, rng, 0.8f + (float)rng.NextDouble() * 0.55f, null);
            c.StandPoint = stand;
            var pr = r; var pl = L; var pp = p;
            c.PickMaterial = rr => MaterialFor(pr, pl, pp, rr);
            placed.Add(p);
            made++;
        }
        int wall = 0;
        for (int tries = 0; wall < wallSlots && tries < 3000; tries++)
        {
            if (!L.RandomWallSpot(rng, 0.7f, 2.5f, out var spot, out var n, out var stand)) continue;
            if (NearPlaced(spot, 3f) || NearHeart(spot) || InCamp(stand)) continue;
            var up = n.Lerp(Vector3.Up, 0.15f).Normalized();
            float size = 0.7f + (float)rng.NextDouble() * 0.35f;
            // o olho do jogador não pode ficar colado nem longe demais do centro do depósito
            var eye = stand + Vector3.Up * 1.55f;
            float ed = eye.DistanceTo(spot - n * 0.12f + up * 0.7f * size);
            if (ed < 1.4f || ed > 3.3f) continue;
            var basis = BasisFromUp(up) * new Basis(Vector3.Up, (float)rng.NextDouble() * Mathf.Tau);
            var c = MakeCrystal(root, MaterialFor(r, L, stand, rng), spot - n * 0.12f, rng, size, basis);
            c.StandPoint = stand + Vector3.Up * 0.1f;
            var pr = r; var pl = L; var ps = stand;
            c.PickMaterial = rr => MaterialFor(pr, pl, ps, rr);
            placed.Add(spot);
            wall++;
        }
        // câmaras secretas: tesouro garantido (raro da região + minério mais valioso)
        foreach (var s in L.Secrets)
        {
            var best = r.Spawns[^1].MaterialId;
            for (int k = 0; k < 4; k++)
            {
                float a = Mathf.Tau * k / 4 + 0.4f;
                var p = L.OnGround(s.A.X + Mathf.Cos(a) * s.RA * 0.5f, s.A.Y + Mathf.Sin(a) * s.RA * 0.5f);
                if (NearPlaced(p, 2.4f)) continue;
                var mat = GameData.Mat(k % 2 == 0 ? r.RareMaterialId : best);
                var c = MakeCrystal(root, mat, p, rng, 1.0f, null);
                c.StandPoint = L.OnGround(s.A.X, s.A.Y, 0.1f);
                var pr = r; var pl = L; var pp = p;
                c.PickMaterial = rr => MaterialFor(pr, pl, pp, rr);
                placed.Add(p);
            }
        }
        // na região inicial, garante depósitos fáceis perto do elevador
        if (L.Camp != null)
        {
            var q = GameData.Mat("quartz");
            foreach (var off in new[] { new Vector2(-3.4f, -6.5f), new Vector2(3.6f, -7.5f) })
            {
                var p = L.OnGround(L.Camp.A.X + off.X, L.Camp.A.Y + off.Y);
                var c = MakeCrystal(root, q, p, rng, 0.9f, null);
                c.StandPoint = p + new Vector3(0, 0.1f, 1.7f);
                var pr = r; var pl = L; var pp = p;
                c.PickMaterial = rr => MaterialFor(pr, pl, pp, rr);
            }
        }
    }

    /// <summary>Ponto onde o jogador fica para minerar um depósito no chão (alcançável e no mesmo nível).</summary>
    static bool FindStand(CaveLayout L, Vector3 p, Random rng, out Vector3 stand)
    {
        float a0 = (float)rng.NextDouble() * Mathf.Tau;
        for (int k = 0; k < 8; k++)
        {
            float a = a0 + Mathf.Tau * k / 8;
            var q = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.7f;
            if (!L.Reachable(q.X, q.Z) || L.WallDist(q.X, q.Z) > -0.6f) continue;
            float g = L.Ground(q.X, q.Z);
            if (Mathf.Abs(g - p.Y) > 0.7f) continue;
            stand = new Vector3(q.X, g + 0.1f, q.Z);
            return true;
        }
        stand = default;
        return false;
    }

    /// <summary>Material conforme o lugar: câmaras com minério em destaque e câmaras secretas mudam a tabela.</summary>
    static MaterialDef MaterialFor(RegionDef r, CaveLayout L, Vector3 p, Random rng)
    {
        var f = L.FeatureAt(p.X, p.Z);
        if (f?.Wing != null && rng.NextDouble() < 0.55) return GameData.Mat(f.Wing.FeaturedMaterialId);
        if (f?.Kind == CaveKind.Secret && rng.NextDouble() < r.RareChance * 4f) return GameData.Mat(r.RareMaterialId);
        return PickFor(r, rng);
    }

    Crystal MakeCrystal(Node3D root, MaterialDef mat, Vector3 pos, Random rng, float size, Basis? basis)
    {
        var c = new Crystal { Name = $"Crystal_{Crystals.Count}" };
        c.Init(mat, rng.Next(), size);
        root.AddChild(c);
        c.Position = pos;
        c.Basis = basis ?? new Basis(Vector3.Up, (float)rng.NextDouble() * Mathf.Tau);
        Crystals.Add(c);
        return c;
    }

    public static MaterialDef PickFor(RegionDef r, Random rng)
    {
        if (rng.NextDouble() < r.RareChance * (GameState.Instance?.RareChanceMultiplier ?? 1f)) return GameData.Mat(r.RareMaterialId);
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


    // =================== Decoração ===================
    void BuildDecoration(Node3D root, RegionDef r, CaveLayout L, Random rng)
    {
        var rockMat = MeshFactory.RockMaterial(r.RockColor.Lightened(0.15f));
        float area = L.Nx * L.Nz;

        // rochas grandes no pé das paredes (quebram o contorno)
        var wallRocks = new List<Transform3D>();
        for (int i = 0; i < (int)(area / 55); i++)
        {
            if (!L.RandomNearWall(rng, out var p, -0.9f, 0.9f)) continue;
            float s = 1.2f + (float)rng.NextDouble() * 2.4f;
            p.Y -= s * 0.35f;
            var basis = new Basis(Vector3.Up, (float)rng.NextDouble() * Mathf.Tau).ScaleLocal(new Vector3(s, s * (0.7f + (float)rng.NextDouble() * 0.6f), s));
            wallRocks.Add(new Transform3D(basis, p));
        }
        AddMulti(root, MeshFactory.Rock(11, 0.4f), rockMat, wallRocks);

        // pedregulhos soltos no chão (alguns grandes, viram obstáculos/esconderijos visuais)
        var boulders = new List<Transform3D>();
        for (int i = 0; i < (int)(area / 260); i++)
        {
            if (!L.RandomFloor(rng, 3f, 0.3f, out var p)) continue;
            float s = 0.6f + (float)rng.NextDouble() * 1.1f;
            boulders.Add(new Transform3D(new Basis(Vector3.Up, (float)rng.NextDouble() * 6f).ScaleLocal(new Vector3(s, s * 0.8f, s)), p + Vector3.Down * s * 0.25f));
        }
        AddMulti(root, MeshFactory.Rock(5, 0.35f), rockMat, boulders);

        // estalactites (teto) e estalagmites (chão, perto das paredes)
        var spikesDown = new List<Transform3D>();
        var spikesUp = new List<Transform3D>();
        for (int i = 0; i < (int)(area / 38); i++)
        {
            float x = L.X0 + 2 + (float)rng.NextDouble() * (L.Nx - 4);
            float z = L.Z0 + 2 + (float)rng.NextDouble() * (L.Nz - 4);
            if (L.WallDist(x, z) > -1f) continue;
            float g = L.Ground(x, z), c = L.Ceiling(x, z);
            if (c - g < 4.5f) continue;
            float len = 0.8f + (float)rng.NextDouble() * Mathf.Min(3.2f, (c - g) * 0.3f);
            float rad = 0.25f + (float)rng.NextDouble() * 0.5f;
            spikesDown.Add(new Transform3D(new Basis(new Vector3(1, 0, 0), Mathf.Pi).ScaleLocal(new Vector3(rad, len, rad)), new Vector3(x, c + 0.2f, z)));
            if (i % 3 == 0 && L.WallDist(x, z) > -3.5f)
                spikesUp.Add(new Transform3D(Basis.Identity.ScaleLocal(new Vector3(rad * 1.2f, len * 0.6f, rad * 1.2f)), new Vector3(x, g - 0.1f, z)));
        }
        var spikeMesh = MeshFactory.Spike(5, 1f, 1f);
        AddMulti(root, spikeMesh, rockMat, spikesDown);
        AddMulti(root, spikeMesh, rockMat, spikesUp);

        // pedras pequenas no chão
        var pebbles = new List<Transform3D>();
        for (int i = 0; i < (int)(area / 70); i++)
        {
            if (!L.RandomFloor(rng, 0.8f, 0.5f, out var p)) continue;
            float s = 0.12f + (float)rng.NextDouble() * 0.35f;
            pebbles.Add(new Transform3D(new Basis(Vector3.Up, (float)rng.NextDouble() * 6f).ScaleLocal(Vector3.One * s), p + Vector3.Up * s * 0.1f));
        }
        AddMulti(root, MeshFactory.Rock(3, 0.3f), rockMat, pebbles);

        // cristais decorativos (não mineráveis) incrustados nas paredes, em várias alturas
        var deco = new List<Transform3D>();
        for (int i = 0; i < (int)(area / 70); i++)
        {
            if (!L.RandomNearWall(rng, out var foot, -1.5f, -0.3f)) continue;
            var outDir = -L.Inward(foot.X, foot.Z);
            float y = foot.Y + 0.6f + (float)rng.NextDouble() * 7f;
            if (y > L.Ceiling(foot.X, foot.Z) - 0.5f && L.WallDist(foot.X, foot.Z) < 0) continue;
            for (float s = 0.3f; s < 4f; s += 0.25f)
            {
                var q = foot + outDir * s;
                if (L.Ground(q.X, q.Z) < y) continue;
                var n = L.Normal(q.X, q.Z);
                var up = (n + new Vector3((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.8f).Normalized();
                var b = BasisFromUp(up).ScaleLocal(new Vector3(0.12f, 0.4f + (float)rng.NextDouble() * 0.9f, 0.12f) * (1f + (float)rng.NextDouble()));
                deco.Add(new Transform3D(b, new Vector3(q.X, y, q.Z)));
                break;
            }
        }
        var decoMat = MeshFactory.Emissive(r.AccentColor, r.AccentColor, 1.6f);
        AddMulti(root, MeshFactory.CrystalPrism(6, 1, 1, 0.3f), decoMat, deco);

        // marcas especiais por região
        if (r.Id == "depths")
        {
            var pool = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.1f, 0.5f, 0.6f, 0.6f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                EmissionEnabled = true, Emission = new Color(0.1f, 0.6f, 0.7f), EmissionEnergyMultiplier = 0.8f,
                Metallic = 0.6f, Roughness = 0.05f,
            };
            for (int i = 0; i < 12; i++)
            {
                if (!L.RandomFloor(rng, 4f, 0.08f, out var p)) continue;
                float rad = 1.2f + (float)rng.NextDouble() * 2f;
                AddMeshOnly(root, new CylinderMesh { TopRadius = rad, BottomRadius = rad, Height = 0.02f, RadialSegments = 14, Rings = 1 }, pool, p + Vector3.Up * 0.04f);
            }
        }
        else if (r.Id == "core")
        {
            // fissuras de lava/energia no chão
            var crack = MeshFactory.Emissive(new Color(1f, 0.7f, 0.25f), new Color(1f, 0.6f, 0.15f), 2.2f);
            var cracks = new List<Transform3D>();
            for (int i = 0; i < 90; i++)
            {
                if (!L.RandomFloor(rng, 1f, 0.15f, out var p)) continue;
                var b = new Basis(Vector3.Up, (float)rng.NextDouble() * Mathf.Tau).ScaleLocal(new Vector3(0.12f + (float)rng.NextDouble() * 0.2f, 1, 1.5f + (float)rng.NextDouble() * 4f));
                cracks.Add(new Transform3D(b, p + Vector3.Up * 0.03f));
            }
            AddMulti(root, new BoxMesh { Size = new Vector3(1, 0.02f, 1) }, crack, cracks, 1f);
            // rochas flutuantes animadas sobre a Câmara do Coração
            if (L.Heart != null)
                for (int i = 0; i < 9; i++)
                {
                    float a = Mathf.Tau * i / 9f;
                    float rad = 8f + (float)rng.NextDouble() * 7f;
                    var fr = new FloatingRock
                    {
                        Mesh = MeshFactory.Rock(20 + i, 0.4f),
                        MaterialOverride = MeshFactory.RockMaterial(r.RockColor.Lightened(0.2f)),
                        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                        Scale = Vector3.One * (0.6f + (float)rng.NextDouble() * 1.2f),
                    };
                    root.AddChild(fr);
                    fr.Position = new Vector3(L.Heart.A.X + Mathf.Cos(a) * rad, 7f + (float)rng.NextDouble() * 6f, L.Heart.A.Y + Mathf.Sin(a) * rad * 0.9f);
                    fr.Phase = (float)rng.NextDouble() * 10f;
                }
        }
    }

    void BuildLights(Node3D root, RegionDef r, CaveLayout L)
    {
        // na Baixa as luzes são ocultadas em tempo real; nas outras, uma por câmara principal e pelo caminho
        int n = 0;
        foreach (var c in L.Chambers)
        {
            if (n >= 6) break;
            root.AddChild(new OmniLight3D
            {
                LightColor = n % 2 == 0 ? r.LightColor : r.AccentColor,
                LightEnergy = 1.5f, OmniRange = c.Radius * 1.7f, OmniAttenuation = 1.2f, ShadowEnabled = false,
                Position = new Vector3(c.A.X, c.Level + c.HA * 0.65f, c.A.Y),
            });
            n++;
        }
        for (int i = 1; i < L.PathPoints.Count - 1; i += 2)
        {
            var p = L.PathPoints[i];
            root.AddChild(new OmniLight3D
            {
                LightColor = r.LightColor, LightEnergy = 1.3f, OmniRange = 16f, OmniAttenuation = 1.2f, ShadowEnabled = false,
                Position = new Vector3(p.X, p.Y + r.Height * 0.6f, p.Z),
            });
        }
    }

    static Basis BasisFromUp(Vector3 up)
    {
        var x = up.Cross(MathF.Abs(up.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized();
        var z = x.Cross(up).Normalized();
        return new Basis(x, up, z);
    }


    void BuildAmbientParticles(Node3D root, RegionDef r)
    {
        int amount = AmbientAmount();
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
        else if (r.Id == "abyss")
        {
            // bolhas e partículas luminosas subindo
            p.Gravity = new Vector3(0, 0.5f, 0);
            p.Color = new Color(0.5f, 0.95f, 1f);
            p.Mesh = new SphereMesh { Radius = 0.04f, Height = 0.08f, RadialSegments = 6, Rings = 3 };
        }
        else if (r.Id == "core")
        {
            // brasas subindo
            p.Gravity = new Vector3(0, 0.8f, 0);
            p.Color = new Color(1f, 0.75f, 0.35f);
            p.InitialVelocityMax = 0.8f;
        }
        p.MaterialOverride = mat;
        p.AddToGroup(AmbientGroup);
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1, 1, 1, 0));
        ramp.SetColor(1, new Color(1, 1, 1, 0));
        ramp.AddPoint(0.2f, new Color(1, 1, 1, 1));
        ramp.AddPoint(0.8f, new Color(1, 1, 1, 0.8f));
        p.ColorRamp = ramp;
        root.AddChild(p);
    }

    public const string AmbientGroup = "ambient_particles";
    /// <summary>Meta nos MultiMesh de decoração: fração de instâncias desenhadas na qualidade Baixa.</summary>
    public const string LowFractionMeta = "low_fraction";
    public static int AmbientAmount() => Settings.Quality switch { GraphicsQuality.Low => 20, GraphicsQuality.Medium => 45, _ => 80 };

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

    static void AddMulti(Node3D root, Mesh mesh, Material mat, List<Transform3D> xforms, float lowFraction = 0.6f)
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
        mmi.SetMeta(LowFractionMeta, lowFraction);
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
