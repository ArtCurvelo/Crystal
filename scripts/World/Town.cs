using System;
using System.Collections.Generic;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Vila dos Ferreiros: superfície ao ar livre em volta da casa do escavador. O elevador da mina chega na
/// estação da vila; a casa (cama, painel) é um prédio com porta; o ferreiro compra minérios e vende
/// ferramentas; o quadro de missões tem encomendas que pagam mais que a venda comum.
/// </summary>
public partial class Town : Node3D
{
    /// <summary>Centro da praça (a casa fica a leste, de frente para a praça).</summary>
    public static Vector3 PlazaCenter => PlayerHouse.Origin + new Vector3(-15f, 0, 0);
    public const float Radius = 30f;

    public static bool Contains(Vector3 p)
    {
        var d = new Vector2(p.X - PlazaCenter.X, p.Z - PlazaCenter.Z);
        return d.Length() < Radius + 4f && p.Y > -3f && p.Y < 30f;
    }

    /// <summary>Onde o jogador aparece ao subir da mina (na frente da estação do elevador).</summary>
    public Vector3 ArrivePoint => PlazaCenter + new Vector3(-8.5f, 0.1f, 0);
    public const float ArriveYaw = -Mathf.Pi / 2; // olhando para a praça (+X)
    /// <summary>Saída da casa (do lado de fora da porta, olhando para a praça).</summary>
    public static Vector3 HouseOutsidePoint => PlayerHouse.Origin + new Vector3(-4.9f, 0.1f, 1.4f);
    public const float HouseOutsideYaw = Mathf.Pi / 2;

    public MineElevator Elevator;
    public HouseDoor DoorOutside;
    public BlacksmithCounter Smith;
    public BlacksmithBench SmithBench;
    public ForgeAnvil Anvil;
    public MissionBoard Board;
    DirectionalLight3D _sun;
    MultiMeshInstance3D _stars;
    OmniLight3D _forgeLight;
    SmithNpc _smithNpc;
    float _t;

    static StandardMaterial3D Mat(Color c, float rough = 0.85f, float metal = 0f) => new() { AlbedoColor = c, Roughness = rough, Metallic = metal };
    static StandardMaterial3D Glow(Color c, float e) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = e };

    MeshInstance3D M(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? rot = null, Vector3? scale = null)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(m);
        return m;
    }

    StaticBody3D _solids;
    void Solid(Vector3 pos, Vector3 size, float yaw = 0f)
    {
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = pos, Rotation = new Vector3(0, yaw, 0) };
        _solids.AddChild(cs);
    }

    public override void _Ready()
    {
        _solids = new StaticBody3D { Name = "TownSolids", CollisionLayer = PlayerController.WorldLayer, CollisionMask = 0 };
        AddChild(_solids);
        var c = PlazaCenter;
        BuildGround(c);
        BuildSurroundings(c);
        BuildPlaza(c);
        BuildHouseExterior();
        BuildStation(c);
        BuildBlacksmith(c + new Vector3(0, 0, -11f));
        BuildBoard(c + new Vector3(3f, 0, 9f));
        BuildVillage(c);
        BuildSky();
        // casas, cercas, barracas...: peças estáticas com o mesmo material viram uma malha só (menos chamadas de desenho)
        int saved = StaticBatcher.Batch(this);
        GD.Print($"[Vila] {saved} malhas agrupadas");
        if (GameState.Instance != null) { GameState.Instance.DayStarted += RefreshSky; GameState.Instance.DayEnded += RefreshSky; }
        RefreshSky();
    }

    public override void _ExitTree()
    {
        if (GameState.Instance != null) { GameState.Instance.DayStarted -= RefreshSky; GameState.Instance.DayEnded -= RefreshSky; }
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_forgeLight != null) _forgeLight.LightEnergy = 1.6f + 0.35f * Mathf.Sin(_t * 9f) * Mathf.Sin(_t * 3.3f);
    }

    // ------------------------------------------------------------------ chão e entorno
    static Texture2D _groundNoise;
    /// <summary>Ruído em tons de cinza para o chão da vila (o da caverna guarda só o canal vermelho).</summary>
    public static Texture2D GroundNoise
    {
        get
        {
            if (_groundNoise != null) return _groundNoise;
            var n = new FastNoiseLite { Seed = 31, Frequency = 0.03f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
            var img = n.GetSeamlessImage(256, 256);
            img.Convert(Image.Format.Rgb8);
            // contraste suave em volta de cinza claro (multiplica a cor do material)
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    float v = 0.7f + (img.GetPixel(x, y).R - 0.5f) * 0.6f;
                    img.SetPixel(x, y, new Color(v, v, v));
                }
            _groundNoise = ImageTexture.CreateFromImage(img);
            return _groundNoise;
        }
    }

    void BuildGround(Vector3 c)
    {
        var dirt = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.5f, 0.22f), Roughness = 0.95f, AlbedoTexture = GroundNoise, Uv1Scale = new Vector3(8, 8, 8) };
        M(this, new PlaneMesh { Size = new Vector2(Radius * 2.6f, Radius * 2.6f) }, dirt, c + new Vector3(0, -0.02f, 0));
        Solid(c + new Vector3(0, -0.5f, 0), new Vector3(Radius * 2.6f, 1f, Radius * 2.6f));
        // muro invisível no limite (a cerca e as rochas marcam onde é)
        for (int i = 0; i < 28; i++)
        {
            float a = Mathf.Tau * i / 28f;
            var p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (Radius + 1.5f);
            Solid(p + Vector3.Up * 3f, new Vector3(7.2f, 6f, 1f), -a + Mathf.Pi / 2);
        }
    }

    void BuildSurroundings(Vector3 c)
    {
        var rock = MeshFactory.RockMaterial(new Color(0.42f, 0.4f, 0.42f));
        var rng = new Random(4242);
        // montanhas de rocha em volta (escondem o horizonte)
        for (int i = 0; i < 22; i++)
        {
            float a = Mathf.Tau * i / 22f + (float)rng.NextDouble() * 0.15f;
            float r = Radius + 6f + (float)rng.NextDouble() * 6f;
            float s = 7f + (float)rng.NextDouble() * 7f;
            var p = c + new Vector3(Mathf.Cos(a) * r, -1f, Mathf.Sin(a) * r);
            M(this, MeshFactory.Rock(60 + i % 6, 0.4f), rock, p, new Vector3(0, (float)rng.NextDouble() * 6f, 0), new Vector3(s, s * (0.9f + (float)rng.NextDouble() * 0.8f), s));
        }
        // cerca de madeira
        var wood = Mat(new Color(0.42f, 0.28f, 0.17f));
        int posts = 44;
        for (int i = 0; i < posts; i++)
        {
            float a = Mathf.Tau * i / posts;
            var p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * Radius;
            // abertura da cerca perto da casa (a casa fica dentro da vila)
            if (Mathf.Abs(Mathf.Wrap(a, -Mathf.Pi, Mathf.Pi)) < 0.18f) continue;
            M(this, new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.09f, Height = 1.3f, RadialSegments = 6 }, wood, p + Vector3.Up * 0.65f);
            float a2 = Mathf.Tau * (i + 1) / posts;
            var q = c + new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2)) * Radius;
            var mid = (p + q) * 0.5f;
            float len = p.DistanceTo(q);
            foreach (float y in new[] { 0.55f, 1.05f })
                M(this, new BoxMesh { Size = new Vector3(0.06f, 0.08f, len) }, wood, mid + Vector3.Up * y, new Vector3(0, Mathf.Atan2(q.X - p.X, q.Z - p.Z), 0));
        }
        // pinheiros escuros entre a cerca e as rochas
        var pine = Mat(new Color(0.12f, 0.22f, 0.16f), 0.9f);
        var trunk = Mat(new Color(0.3f, 0.2f, 0.13f));
        for (int i = 0; i < 26; i++)
        {
            float a = Mathf.Tau * (i + 0.5f) / 26f;
            float r = Radius + 2.5f + (float)rng.NextDouble() * 2.5f;
            var p = c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            float h = 4f + (float)rng.NextDouble() * 3f;
            M(this, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.18f, Height = h * 0.4f, RadialSegments = 6 }, trunk, p + Vector3.Up * h * 0.2f);
            for (int k = 0; k < 3; k++)
                M(this, new CylinderMesh { TopRadius = 0f, BottomRadius = h * (0.32f - k * 0.07f), Height = h * 0.45f, RadialSegments = 8 }, pine, p + Vector3.Up * (h * (0.35f + k * 0.22f)));
        }
    }

    // ------------------------------------------------------------------ praça
    void BuildPlaza(Vector3 c)
    {
        var stone = Mat(new Color(0.33f, 0.31f, 0.3f), 0.9f);
        var stoneDark = Mat(new Color(0.22f, 0.21f, 0.21f), 0.95f);
        M(this, new CylinderMesh { TopRadius = 9.5f, BottomRadius = 9.6f, Height = 0.06f, RadialSegments = 48 }, stoneDark, c + new Vector3(0, 0.0f, 0));
        // pedras do calçamento em anéis
        var rng = new Random(77);
        for (int ring = 1; ring <= 4; ring++)
        {
            int n = ring * 16;
            for (int i = 0; i < n; i++)
            {
                float a = Mathf.Tau * (i + (float)rng.NextDouble() * 0.15f) / n;
                float r = ring * 2.05f;
                M(this, new BoxMesh { Size = new Vector3(1.1f, 0.04f, 0.75f) }, stone, c + new Vector3(Mathf.Cos(a) * r, 0.04f, Mathf.Sin(a) * r), new Vector3(0, -a, 0));
            }
        }
        // monumento: cristal sobre pedestal no centro da praça
        M(this, new CylinderMesh { TopRadius = 0.9f, BottomRadius = 1.2f, Height = 0.8f, RadialSegments = 8 }, stone, c + Vector3.Up * 0.4f);
        Solid(c + Vector3.Up * 0.8f, new Vector3(2.2f, 1.6f, 2.2f));
        var crystal = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/crystal.gdshader") };
        crystal.SetShaderParameter("base_color", new Color(0.5f, 0.9f, 1f));
        crystal.SetShaderParameter("glow_color", new Color(0.3f, 0.85f, 1f));
        crystal.SetShaderParameter("glow_energy", 0.9f);
        M(this, MeshFactory.CrystalPrism(6, 1, 1, 0.3f), crystal, c + Vector3.Up * 0.8f, null, new Vector3(0.45f, 2.4f, 0.45f));
        M(this, MeshFactory.CrystalPrism(6, 1, 1, 0.3f), crystal, c + new Vector3(0.35f, 0.8f, 0.2f), new Vector3(0, 0, -0.35f), new Vector3(0.25f, 1.3f, 0.25f));
        M(this, MeshFactory.CrystalPrism(6, 1, 1, 0.3f), crystal, c + new Vector3(-0.3f, 0.8f, -0.25f), new Vector3(0.3f, 0, 0.3f), new Vector3(0.22f, 1.1f, 0.22f));
        AddChild(new OmniLight3D { LightColor = new Color(0.4f, 0.85f, 1f), LightEnergy = 1.4f, OmniRange = 9f, Position = c + Vector3.Up * 2.5f });
        // lampiões em volta da praça
        var post = Mat(new Color(0.12f, 0.12f, 0.14f), 0.6f, 0.4f);
        var lamp = Glow(new Color(1f, 0.75f, 0.4f), 2f);
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6f + 0.5f;
            var p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 10.5f;
            M(this, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.08f, Height = 3f, RadialSegments = 8 }, post, p + Vector3.Up * 1.5f);
            M(this, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.2f, Height = 0.12f, RadialSegments = 8 }, post, p + Vector3.Up * 3.1f);
            M(this, new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.14f, Height = 0.3f, RadialSegments = 8 }, lamp, p + Vector3.Up * 2.88f);
            Solid(p + Vector3.Up * 1.5f, new Vector3(0.25f, 3f, 0.25f));
            if (i % 2 == 0) AddChild(new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.45f), LightEnergy = 1.1f, OmniRange = 11f, Position = p + Vector3.Up * 2.8f });
        }
        // carroça de minério, barris e caixotes
        var wood = Mat(new Color(0.45f, 0.3f, 0.18f));
        var cart = c + new Vector3(-6.4f, 0, -7.4f);
        M(this, new BoxMesh { Size = new Vector3(1.6f, 0.6f, 1.0f) }, wood, cart + Vector3.Up * 0.75f, new Vector3(0, 0.4f, 0));
        foreach (float dz in new[] { -0.55f, 0.55f })
            M(this, new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.35f, Height = 0.08f, RadialSegments = 12 }, post, cart + new Vector3(0, 0.35f, dz), new Vector3(Mathf.Pi / 2, 0.4f, 0));
        M(this, MeshFactory.Rock(3, 0.4f), MeshFactory.RockMaterial(new Color(0.55f, 0.5f, 0.6f)), cart + Vector3.Up * 1.1f, null, Vector3.One * 0.5f);
        Solid(cart + Vector3.Up * 0.6f, new Vector3(1.6f, 1.2f, 1.2f), 0.4f);
        for (int i = 0; i < 3; i++)
        {
            var b = c + new Vector3(-17.6f + i * 0.7f, 0, 12.4f + (i % 2) * 0.5f);
            M(this, new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.32f, Height = 0.9f, RadialSegments = 10 }, wood, b + Vector3.Up * 0.45f);
            foreach (float y in new[] { 0.15f, 0.75f })
                M(this, new CylinderMesh { TopRadius = 0.335f, BottomRadius = 0.335f, Height = 0.05f, RadialSegments = 10 }, post, b + Vector3.Up * y);
        }
        Solid(c + new Vector3(-16.9f, 0.45f, 12.6f), new Vector3(2.2f, 0.9f, 1.2f));
        Decor.Crate(this, c + new Vector3(-13.9f, 0, 14.2f), 0.3f, 0.9f, true);
    }

    // ------------------------------------------------------------------ fachada da casa
    void BuildHouseExterior()
    {
        var h = PlayerHouse.Origin;
        var plank = Mat(new Color(0.48f, 0.32f, 0.2f));
        var plankDark = Mat(new Color(0.3f, 0.2f, 0.13f));
        var roof = Mat(new Color(0.35f, 0.17f, 0.13f), 0.8f);
        var stone = MeshFactory.RockMaterial(new Color(0.45f, 0.43f, 0.42f));
        // paredes externas (por fora das paredes internas)
        M(this, new BoxMesh { Size = new Vector3(6.8f, 3.6f, 0.12f) }, plank, h + new Vector3(0, 1.75f, -2.68f));
        M(this, new BoxMesh { Size = new Vector3(6.8f, 3.6f, 0.12f) }, plank, h + new Vector3(0, 1.75f, 2.68f));
        M(this, new BoxMesh { Size = new Vector3(0.12f, 3.6f, 5.5f) }, plank, h + new Vector3(3.34f, 1.75f, 0));
        M(this, new BoxMesh { Size = new Vector3(0.12f, 3.6f, 5.5f) }, plank, h + new Vector3(-3.34f, 1.75f, 0));
        // base de pedra e tábuas verticais
        foreach (var (p, s) in new[] { (new Vector3(0, 0.25f, -2.72f), new Vector3(6.9f, 0.5f, 0.1f)), (new Vector3(0, 0.25f, 2.72f), new Vector3(6.9f, 0.5f, 0.1f)), (new Vector3(-3.38f, 0.25f, 0), new Vector3(0.1f, 0.5f, 5.5f)), (new Vector3(3.38f, 0.25f, 0), new Vector3(0.1f, 0.5f, 5.5f)) })
            M(this, new BoxMesh { Size = s }, stone, h + p);
        for (int i = 0; i < 9; i++)
        {
            M(this, new BoxMesh { Size = new Vector3(0.05f, 3.4f, 0.03f) }, plankDark, h + new Vector3(-3.0f + i * 0.75f, 1.85f, -2.76f));
            M(this, new BoxMesh { Size = new Vector3(0.05f, 3.4f, 0.03f) }, plankDark, h + new Vector3(-3.0f + i * 0.75f, 1.85f, 2.76f));
        }
        // telhado de duas águas com beiral
        foreach (float sgn in new[] { -1f, 1f })
            M(this, new BoxMesh { Size = new Vector3(7.4f, 0.14f, 3.6f) }, roof, h + new Vector3(0, 4.2f, sgn * 1.55f), new Vector3(sgn * 0.55f, 0, 0));
        M(this, new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.1f, Height = 7.5f, RadialSegments = 8 }, plankDark, h + new Vector3(0, 5.0f, 0), new Vector3(0, 0, Mathf.Pi / 2));
        foreach (float x in new[] { -3.4f, 3.4f })
            M(this, new PrismMesh { Size = new Vector3(5.5f, 1.5f, 0.1f) }, plank, h + new Vector3(x, 4.3f, 0), new Vector3(0, Mathf.Pi / 2, 0));
        // chaminé com fumaça
        M(this, new BoxMesh { Size = new Vector3(0.7f, 2.2f, 0.7f) }, stone, h + new Vector3(2.0f, 4.8f, -1.2f));
        var fade = new Gradient();
        fade.SetColor(0, new Color(0.75f, 0.75f, 0.78f, 0.3f));
        fade.SetColor(1, new Color(0.75f, 0.75f, 0.78f, 0f));
        AddChild(new CpuParticles3D
        {
            Amount = 12, Lifetime = 4f, Mesh = new SphereMesh { Radius = 0.25f, Height = 0.5f, RadialSegments = 8, Rings = 4 }, Direction = Vector3.Up, Spread = 12,
            Gravity = new Vector3(0.15f, 0.35f, 0), InitialVelocityMin = 0.3f, InitialVelocityMax = 0.55f, ScaleAmountMin = 0.7f, ScaleAmountMax = 1.8f,
            ColorRamp = fade, Position = h + new Vector3(2.0f, 6.0f, -1.2f),
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true },
        });
        // porta (lado da praça), alpendre, lampião e janelas acesas
        var door = h + new Vector3(-3.42f, 0, 1.4f);
        M(this, new BoxMesh { Size = new Vector3(0.1f, 2.3f, 1.2f) }, plankDark, door + Vector3.Up * 1.15f);
        M(this, new BoxMesh { Size = new Vector3(0.06f, 2.1f, 0.95f) }, plank, door + new Vector3(-0.06f, 1.05f, 0));
        M(this, new SphereMesh { Radius = 0.05f, Height = 0.1f }, Mat(new Color(0.8f, 0.65f, 0.3f), 0.3f, 0.8f), door + new Vector3(-0.12f, 1.0f, -0.3f));
        M(this, new BoxMesh { Size = new Vector3(1.4f, 0.16f, 2.0f) }, stone, door + new Vector3(-0.7f, 0.08f, 0));
        M(this, new BoxMesh { Size = new Vector3(1.4f, 0.08f, 2.2f) }, roof, door + new Vector3(-0.75f, 2.55f, 0), new Vector3(0, 0, 0.25f));
        M(this, new BoxMesh { Size = new Vector3(0.18f, 0.3f, 0.18f) }, Glow(new Color(1f, 0.75f, 0.4f), 2f), door + new Vector3(-0.2f, 2.0f, -0.85f));
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.45f), LightEnergy = 1.0f, OmniRange = 7f, Position = door + new Vector3(-1.2f, 2.2f, 0) });
        var win = Glow(new Color(1f, 0.8f, 0.45f), 1.6f);
        foreach (float z in new[] { -1.0f })
        {
            M(this, new BoxMesh { Size = new Vector3(0.08f, 0.9f, 1.1f) }, plankDark, h + new Vector3(-3.42f, 1.9f, z));
            M(this, new BoxMesh { Size = new Vector3(0.06f, 0.75f, 0.95f) }, win, h + new Vector3(-3.45f, 1.9f, z));
        }
        M(this, new BoxMesh { Size = new Vector3(1.7f, 1.2f, 0.08f) }, plankDark, h + new Vector3(1.2f, 1.9f, -2.78f));
        M(this, new BoxMesh { Size = new Vector3(1.55f, 1.05f, 0.06f) }, win, h + new Vector3(1.2f, 1.9f, -2.8f));
        DoorOutside = new HouseDoor { Name = "HouseDoorOutside", ToInside = true, Position = door + new Vector3(-0.15f, 0, 0), Rotation = new Vector3(0, -Mathf.Pi / 2, 0) };
        AddChild(DoorOutside);
    }

    // ------------------------------------------------------------------ estação do elevador
    void BuildStation(Vector3 c)
    {
        var p = c + new Vector3(-11.5f, 0, 0);
        var stone = MeshFactory.RockMaterial(new Color(0.4f, 0.38f, 0.37f));
        var wood = Mat(new Color(0.42f, 0.28f, 0.17f));
        // plataforma de pedra e galpão em volta do poço
        M(this, new BoxMesh { Size = new Vector3(6.5f, 0.3f, 7f) }, stone, p + new Vector3(-0.5f, 0.15f, 0));
        Solid(p + new Vector3(-0.5f, 0.15f, 0), new Vector3(6.5f, 0.3f, 7f));
        foreach (float z in new[] { -3.2f, 3.2f })
        {
            M(this, new BoxMesh { Size = new Vector3(5.5f, 4.2f, 0.25f) }, wood, p + new Vector3(-1.0f, 2.4f, z));
            Solid(p + new Vector3(-1.0f, 2.4f, z), new Vector3(5.5f, 4.2f, 0.25f));
        }
        M(this, new BoxMesh { Size = new Vector3(0.25f, 4.2f, 6.6f) }, wood, p + new Vector3(-3.6f, 2.4f, 0));
        Solid(p + new Vector3(-3.6f, 2.4f, 0), new Vector3(0.25f, 4.2f, 6.6f));
        M(this, new BoxMesh { Size = new Vector3(6.2f, 0.18f, 7.2f) }, Mat(new Color(0.3f, 0.22f, 0.2f)), p + new Vector3(-1.0f, 4.6f, 0), new Vector3(0, 0, -0.08f));
        // placa
        var sign = new Label3D
        {
            Text = Loc.T("ESTAÇÃO DA MINA"), FontSize = 72, OutlineSize = 14, PixelSize = 0.006f, Modulate = new Color(1f, 0.82f, 0.45f),
            Position = p + new Vector3(1.75f, 4.95f, 0), Rotation = new Vector3(0, Mathf.Pi / 2, 0),
        };
        AddChild(sign);
        Elevator = new MineElevator { Name = "TownElevator", GoesDown = true, Position = p + new Vector3(0.2f, 0.3f, 0), Rotation = new Vector3(0, Mathf.Pi / 2, 0) };
        AddChild(Elevator);
    }

    // ------------------------------------------------------------------ ferreiro
    void BuildBlacksmith(Vector3 p)
    {
        var stone = MeshFactory.RockMaterial(new Color(0.4f, 0.37f, 0.35f));
        var wood = Mat(new Color(0.42f, 0.28f, 0.17f));
        var woodDark = Mat(new Color(0.28f, 0.18f, 0.11f));
        var iron = Mat(new Color(0.25f, 0.25f, 0.28f), 0.45f, 0.6f);
        var roof = Mat(new Color(0.3f, 0.2f, 0.16f), 0.8f);
        // piso de pedra, pilares e telhado inclinado (oficina aberta para a praça)
        M(this, new BoxMesh { Size = new Vector3(9f, 0.2f, 6f) }, stone, p + new Vector3(0, 0.1f, 0));
        foreach (float x in new[] { -4.2f, 4.2f })
            foreach (float z in new[] { -2.7f, 2.7f })
            {
                M(this, new BoxMesh { Size = new Vector3(0.3f, 3.6f, 0.3f) }, woodDark, p + new Vector3(x, 1.9f, z));
                Solid(p + new Vector3(x, 1.9f, z), new Vector3(0.3f, 3.6f, 0.3f));
            }
        M(this, new BoxMesh { Size = new Vector3(9.8f, 0.16f, 7f) }, roof, p + new Vector3(0, 3.85f, 0), new Vector3(0.18f, 0, 0));
        M(this, new BoxMesh { Size = new Vector3(9f, 3.4f, 0.25f) }, stone, p + new Vector3(0, 1.9f, -2.9f));
        Solid(p + new Vector3(0, 1.9f, -2.9f), new Vector3(9f, 3.6f, 0.4f));
        // forja de pedra com brasas, coifa e chaminé
        var forge = p + new Vector3(-2.6f, 0, -1.8f);
        M(this, new BoxMesh { Size = new Vector3(2.2f, 1.0f, 1.6f) }, stone, forge + Vector3.Up * 0.5f);
        M(this, new BoxMesh { Size = new Vector3(1.6f, 0.1f, 1.0f) }, Glow(new Color(1f, 0.42f, 0.1f), 2.6f), forge + Vector3.Up * 1.02f);
        M(this, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 1.0f, Height = 0.9f, RadialSegments = 4 }, iron, forge + Vector3.Up * 2.4f, new Vector3(0, Mathf.Pi / 4, 0));
        M(this, new BoxMesh { Size = new Vector3(0.5f, 3f, 0.5f) }, stone, forge + Vector3.Up * 4.2f);
        Solid(forge + Vector3.Up * 0.5f, new Vector3(2.2f, 1.0f, 1.6f));
        _forgeLight = new OmniLight3D { LightColor = new Color(1f, 0.5f, 0.2f), LightEnergy = 1.6f, OmniRange = 9f, Position = forge + new Vector3(0, 1.6f, 0.8f) };
        AddChild(_forgeLight);
        AddChild(new CpuParticles3D
        {
            Amount = 16, Lifetime = 1.4f, Mesh = new BoxMesh { Size = new Vector3(0.03f, 0.03f, 0.03f) }, Direction = Vector3.Up, Spread = 25,
            Gravity = new Vector3(0, 1.2f, 0), InitialVelocityMin = 0.6f, InitialVelocityMax = 1.4f, Color = new Color(1f, 0.6f, 0.2f),
            Position = forge + Vector3.Up * 1.1f, EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.7f, 0.05f, 0.4f),
            MaterialOverride = Glow(new Color(1f, 0.6f, 0.2f), 3f),
        });
        // bigorna sobre tora: é a estação da forja (equipamentos e conserto)
        Anvil = new ForgeAnvil { Name = "ForgeAnvil", Position = p + new Vector3(0.6f, 0.2f, 1.6f) };
        AddChild(Anvil);
        // tina de têmpera e prateleira de ferramentas
        M(this, new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.4f, Height = 0.6f, RadialSegments = 12 }, wood, p + new Vector3(2.0f, 0.5f, -1.8f));
        M(this, new CylinderMesh { TopRadius = 0.4f, BottomRadius = 0.4f, Height = 0.02f, RadialSegments = 12 }, Mat(new Color(0.1f, 0.2f, 0.25f), 0.1f), p + new Vector3(2.0f, 0.78f, -1.8f));
        Solid(p + new Vector3(2.0f, 0.5f, -1.8f), new Vector3(0.9f, 0.8f, 0.9f));
        M(this, new BoxMesh { Size = new Vector3(2.4f, 1.2f, 0.06f) }, woodDark, p + new Vector3(2.4f, 2.0f, -2.72f));
        for (int i = 0; i < 5; i++)
            M(this, new BoxMesh { Size = new Vector3(0.05f, 0.6f, 0.05f) }, iron, p + new Vector3(1.5f + i * 0.45f, 2.0f, -2.66f), new Vector3(0, 0, 0.2f * (i % 2 == 0 ? 1 : -1)));
        // ferreiro (NPC) e as duas estações: balcão (venda) e bancada (ferramentas)
        _smithNpc = new SmithNpc { Position = p + new Vector3(-0.6f, 0.2f, -0.6f), Rotation = new Vector3(0, 0.4f, 0) };
        AddChild(_smithNpc);
        Smith = new BlacksmithCounter { Name = "SmithCounter", Position = p + new Vector3(-1.2f, 0.2f, 1.6f) };
        AddChild(Smith);
        SmithBench = new BlacksmithBench { Name = "SmithBench", Position = p + new Vector3(2.4f, 0.2f, 1.6f) };
        AddChild(SmithBench);
        var label = new Label3D
        {
            Text = Loc.T("FERREIRO"), FontSize = 80, OutlineSize = 14, PixelSize = 0.006f, Modulate = new Color(1f, 0.65f, 0.35f),
            Position = p + new Vector3(0, 4.4f, 2.9f), Billboard = BaseMaterial3D.BillboardModeEnum.Disabled,
        };
        AddChild(label);
    }

    // ------------------------------------------------------------------ quadro de missões
    void BuildBoard(Vector3 p)
    {
        Board = new MissionBoard { Name = "MissionBoard", Position = p, Rotation = new Vector3(0, Mathf.Pi, 0) };
        AddChild(Board);
    }

    // ------------------------------------------------------------------ céu
    void BuildSky()
    {
        _sun = new DirectionalLight3D { Name = "TownSun", Rotation = new Vector3(-0.7f, 0.6f, 0), ShadowEnabled = false };
        AddChild(_sun);
        // estrelas num domo distante
        var rng = new Random(9);
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = new QuadMesh { Size = new Vector2(0.35f, 0.35f) }, InstanceCount = 380 };
        for (int i = 0; i < mm.InstanceCount; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.Tau, e = 0.15f + (float)rng.NextDouble() * 1.35f;
            var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Sin(a) * Mathf.Cos(e));
            var pos = PlazaCenter + dir * 140f;
            var b = Basis.LookingAt(-dir, Mathf.Abs(dir.Y) > 0.95f ? Vector3.Forward : Vector3.Up).Scaled(Vector3.One * (0.5f + (float)rng.NextDouble()));
            mm.SetInstanceTransform(i, new Transform3D(b, pos));
        }
        _stars = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.97f, 0.9f), BillboardMode = BaseMaterial3D.BillboardModeEnum.Disabled }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_stars);
        // lua
        M(this, new SphereMesh { Radius = 6f, Height = 12f }, Glow(new Color(0.95f, 0.95f, 1f), 1.6f), PlazaCenter + new Vector3(60f, 80f, -90f));
    }

    /// <summary>Noite depois do turno (estrelas, lua), manhã depois de dormir.</summary>
    public bool Night => GameState.Instance?.DayOver ?? true;

    public void RefreshSky()
    {
        bool night = Night;
        RefreshVillageLights(night);
        if (_stars != null) _stars.Visible = night;
        if (_sun != null)
        {
            _sun.LightColor = night ? new Color(0.55f, 0.65f, 1f) : new Color(1f, 0.85f, 0.65f);
            _sun.LightEnergy = night ? 0.35f : 1.1f;
            _sun.Rotation = night ? new Vector3(-0.9f, 0.6f, 0) : new Vector3(-0.35f, -0.9f, 0);
        }
    }
}

/// <summary>Porta da casa: leva de dentro para a vila e da vila para dentro.</summary>
public partial class HouseDoor : StationBase
{
    public bool ToInside;
    public override void _Ready() => SetupBody(new Vector3(0.4f, 2.2f, 1.1f));
    public override string GetPrompt() => ToInside ? Loc.F("{0} Entrar em casa", InputHints.Interact) : Loc.F("{0} Sair para a vila", InputHints.Interact);
    public override void Interact(PlayerController player) => UiBus.RequestDoor(ToInside);
}

/// <summary>Balcão do ferreiro: compra os minérios da mochila.</summary>
public partial class BlacksmithCounter : StationBase
{
    public override void _Ready()
    {
        SetupBody(new Vector3(2.2f, 1.1f, 0.8f));
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.18f), Roughness = 0.85f };
        var top = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.2f, 0.12f), Roughness = 0.8f };
        AddMesh(new BoxMesh { Size = new Vector3(2.2f, 1.0f, 0.8f) }, wood, new Vector3(0, 0.5f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(2.35f, 0.08f, 0.9f) }, top, new Vector3(0, 1.04f, 0));
        // balança e pilha de lúmens
        var brass = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.65f, 0.3f), Roughness = 0.35f, Metallic = 0.7f };
        AddMesh(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.03f, Height = 0.35f, RadialSegments = 6 }, brass, new Vector3(-0.5f, 1.25f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(0.5f, 0.02f, 0.02f) }, brass, new Vector3(-0.5f, 1.42f, 0));
        foreach (float x in new[] { -0.72f, -0.28f })
            AddMesh(new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.07f, Height = 0.03f, RadialSegments = 10 }, brass, new Vector3(x, 1.3f, 0));
        for (int i = 0; i < 4; i++)
            AddMesh(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 0.02f, RadialSegments = 6 }, MeshFactory.Emissive(new Color(1f, 0.8f, 0.35f), new Color(1f, 0.75f, 0.3f), 1.2f), new Vector3(0.5f, 1.1f + i * 0.022f, 0.05f));
        Label = MakeLabel("VENDER MINÉRIOS", new Color(1f, 0.82f, 0.4f), 1.9f, 48);
    }
    public override string GetPrompt()
    {
        var gs = GameState.Instance;
        return gs.InventoryCount > 0
            ? Loc.F("{0} Vender ao ferreiro {1} fragmentos ({2})", InputHints.Interact, gs.InventoryCount, GameState.FormatMoney(gs.InventoryValue()))
            : Loc.F("{0} Ferreiro: vender minérios", InputHints.Interact);
    }
    public override void Interact(PlayerController player) => UiBus.RequestSell();
}

/// <summary>Bancada do ferreiro: ferramentas, munição e melhorias (o mesmo Terminal da mina).</summary>
public partial class BlacksmithBench : StationBase
{
    public override void _Ready()
    {
        SetupBody(new Vector3(2.2f, 1.1f, 0.8f));
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.27f, 0.16f), Roughness = 0.85f };
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.52f, 0.56f), Roughness = 0.4f, Metallic = 0.5f };
        AddMesh(new BoxMesh { Size = new Vector3(2.2f, 0.08f, 0.8f) }, wood, new Vector3(0, 0.95f, 0));
        foreach (float x in new[] { -1.0f, 1.0f })
            AddMesh(new BoxMesh { Size = new Vector3(0.1f, 0.95f, 0.7f) }, wood, new Vector3(x, 0.47f, 0));
        // picaretas expostas sobre a bancada
        for (int i = 0; i < 3; i++)
        {
            var h = AddMesh(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.025f, Height = 0.7f, RadialSegments = 6 }, wood, new Vector3(-0.6f + i * 0.6f, 1.02f, 0), new Vector3(0, 0, Mathf.Pi / 2));
            AddMesh(new BoxMesh { Size = new Vector3(0.06f, 0.05f, 0.4f) }, iron, new Vector3(-0.3f + i * 0.6f, 1.02f, 0));
        }
        Label = MakeLabel("FERRAMENTAS", new Color(0.6f, 0.9f, 1f), 1.9f, 48);
    }
    public override string GetPrompt() => Loc.F("{0} Ferreiro: ferramentas, munição e melhorias", InputHints.Interact);
    public override void Interact(PlayerController player) => UiBus.RequestShop();
}

/// <summary>Bigorna do ferreiro: abre a forja (equipamentos feitos com minérios e conserto da ferramenta).</summary>
public partial class ForgeAnvil : StationBase
{
    public override void _Ready()
    {
        SetupBody(new Vector3(0.9f, 1.0f, 0.6f));
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.28f, 0.17f), Roughness = 0.85f };
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.32f, 0.36f), Roughness = 0.45f, Metallic = 0.55f };
        var hot = MeshFactory.Emissive(new Color(1f, 0.45f, 0.12f), new Color(1f, 0.45f, 0.1f), 2.2f);
        AddMesh(new CylinderMesh { TopRadius = 0.32f, BottomRadius = 0.36f, Height = 0.6f, RadialSegments = 10 }, wood, new Vector3(0, 0.3f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(0.35f, 0.2f, 0.22f) }, iron, new Vector3(0, 0.7f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(0.7f, 0.18f, 0.3f) }, iron, new Vector3(0, 0.88f, 0));
        var horn = AddMesh(new CylinderMesh { TopRadius = 0f, BottomRadius = 0.09f, Height = 0.3f, RadialSegments = 6 }, iron, new Vector3(0.48f, 0.88f, 0));
        horn.Rotation = new Vector3(0, 0, -Mathf.Pi / 2);
        // lâmina em brasa sobre a bigorna
        AddMesh(new BoxMesh { Size = new Vector3(0.42f, 0.03f, 0.09f) }, hot, new Vector3(-0.05f, 0.99f, 0.02f));
        Label = MakeLabel("FORJA", new Color(1f, 0.6f, 0.3f), 1.9f, 48);
    }
    public override string GetPrompt()
    {
        var gs = GameState.Instance;
        int ready = 0;
        foreach (var s in GearData.Slots) if (gs.CanForge(s)) ready++;
        if (gs.CanRepair && (gs.ToolBroken || gs.ToolDurability01 < 0.5f)) return Loc.F("{0} Forja: consertar {1}", InputHints.Interact, gs.CurrentTool.Name);
        return ready > 0 ? Loc.F("{0} Forja ({1} peça(s) prontas para forjar!)", InputHints.Interact, ready) : Loc.F("{0} Forja: equipamentos e conserto", InputHints.Interact);
    }
    public override void Interact(PlayerController player) => UiBus.RequestForge();
}

/// <summary>Quadro de missões: encomendas de minério.</summary>
public partial class MissionBoard : StationBase
{
    public override void _Ready()
    {
        SetupBody(new Vector3(2.4f, 2.4f, 0.5f));
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.28f, 0.17f), Roughness = 0.85f };
        var cork = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.45f, 0.28f), Roughness = 0.95f };
        foreach (float x in new[] { -1.1f, 1.1f })
            AddMesh(new BoxMesh { Size = new Vector3(0.15f, 2.6f, 0.15f) }, wood, new Vector3(x, 1.3f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(2.2f, 1.3f, 0.08f) }, cork, new Vector3(0, 1.6f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(2.5f, 0.12f, 0.4f) }, wood, new Vector3(0, 2.35f, 0), new Vector3(0.35f, 0, 0));
        var paper = new StandardMaterial3D { AlbedoColor = new Color(0.92f, 0.88f, 0.76f), Roughness = 1f };
        for (int i = 0; i < 3; i++)
        {
            AddMesh(new BoxMesh { Size = new Vector3(0.5f, 0.65f, 0.01f) }, paper, new Vector3(-0.65f + i * 0.65f, 1.6f + (i % 2) * 0.08f, 0.05f), new Vector3(0, 0, (i - 1) * 0.06f));
            AddMesh(new SphereMesh { Radius = 0.025f, Height = 0.05f }, MeshFactory.Emissive(new Color(1f, 0.3f, 0.2f), new Color(1f, 0.3f, 0.2f), 1f), new Vector3(-0.65f + i * 0.65f, 1.88f + (i % 2) * 0.08f, 0.07f));
        }
        Label = MakeLabel("ENCOMENDAS", new Color(1f, 0.82f, 0.4f), 2.9f, 56);
    }
    public override string GetPrompt()
    {
        var gs = GameState.Instance;
        int ready = 0;
        for (int i = 0; i < gs.Data.Contracts.Count; i++) if (gs.CanDeliver(i)) ready++;
        return ready > 0 ? Loc.F("{0} Encomendas ({1} pronta(s) para entregar!)", InputHints.Interact, ready) : Loc.F("{0} Quadro de encomendas", InputHints.Interact);
    }
    public override void Interact(PlayerController player) => UiBus.RequestMissions();
}

/// <summary>Ferreiro anão martelando na bigorna (feito de primitivas, animação simples).</summary>
public partial class SmithNpc : Node3D
{
    Node3D _arm;
    float _t;
    public override void _Ready()
    {
        var skin = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.62f, 0.48f), Roughness = 0.8f };
        var apron = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.2f, 0.12f), Roughness = 0.9f };
        var shirt = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.25f, 0.18f), Roughness = 0.9f };
        var beard = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.42f, 0.18f), Roughness = 1f };
        var iron = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.35f, 0.38f), Roughness = 0.4f, Metallic = 0.6f };
        void A(Node3D p, Mesh m, Material mat, Vector3 pos, Vector3? rot = null) => p.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero });
        A(this, new CapsuleMesh { Radius = 0.36f, Height = 1.1f }, shirt, new Vector3(0, 0.75f, 0));
        A(this, new BoxMesh { Size = new Vector3(0.6f, 0.75f, 0.05f) }, apron, new Vector3(0, 0.62f, 0.33f));
        foreach (float x in new[] { -0.16f, 0.16f })
            A(this, new CylinderMesh { TopRadius = 0.11f, BottomRadius = 0.12f, Height = 0.35f }, apron, new Vector3(x, 0.15f, 0));
        A(this, new SphereMesh { Radius = 0.26f, Height = 0.52f }, skin, new Vector3(0, 1.48f, 0.02f));
        A(this, new PrismMesh { Size = new Vector3(0.42f, 0.5f, 0.2f) }, beard, new Vector3(0, 1.22f, 0.2f), new Vector3(Mathf.Pi, 0, 0));
        A(this, new SphereMesh { Radius = 0.05f, Height = 0.1f }, skin, new Vector3(0, 1.48f, 0.27f)); // nariz
        A(this, new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.28f, Height = 0.12f }, apron, new Vector3(0, 1.72f, 0)); // gorro
        A(this, new CapsuleMesh { Radius = 0.09f, Height = 0.55f }, shirt, new Vector3(-0.42f, 0.9f, 0.05f), new Vector3(0, 0, 0.3f));
        _arm = new Node3D { Position = new Vector3(0.4f, 1.15f, 0.05f) };
        AddChild(_arm);
        A(_arm, new CapsuleMesh { Radius = 0.09f, Height = 0.55f }, shirt, new Vector3(0.05f, -0.22f, 0.1f), new Vector3(0.6f, 0, -0.2f));
        A(_arm, new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.025f, Height = 0.45f }, new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.27f, 0.15f) }, new Vector3(0.1f, -0.35f, 0.45f), new Vector3(Mathf.Pi / 2, 0, 0));
        A(_arm, new BoxMesh { Size = new Vector3(0.12f, 0.12f, 0.22f) }, iron, new Vector3(0.1f, -0.35f, 0.68f), new Vector3(0, Mathf.Pi / 2, 0));
    }
    public override void _Process(double delta)
    {
        _t += (float)delta;
        // martela: levanta devagar, desce rápido
        float c = (_t * 0.9f) % 1f;
        float k = c < 0.7f ? c / 0.7f : 1f - (c - 0.7f) / 0.3f;
        _arm.Rotation = new Vector3(-0.2f - k * 1.2f, 0, 0);
    }
}
