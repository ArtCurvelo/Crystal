using System;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Maquete 3D da casa do escavador (mundo próprio, renderizada num SubViewport da tela de casa).
/// Cada melhoria comprada aparece como um móvel novo ou maior.
/// </summary>
public partial class HouseDiorama : Node3D
{
    Node3D _props;
    Camera3D _cam;
    /// <summary>true = casa de verdade no mundo do jogo (paredes fechadas, teto e colisões); false = maquete.</summary>
    public bool Interior;
    public const string AlwaysOnMeta = "always_on"; // luz mantida na qualidade Baixa
    float _t;

    static readonly Color Wood = new(0.42f, 0.27f, 0.16f);
    static readonly Color WoodDark = new(0.27f, 0.17f, 0.1f);
    static readonly Color Wall = new(0.55f, 0.5f, 0.46f);
    static readonly Color Metal = new(0.45f, 0.47f, 0.52f);
    static readonly Color Fabric = new(0.2f, 0.45f, 0.7f);
    static readonly Color Lamp = new(1f, 0.78f, 0.45f);

    public override void _Ready()
    {
        if (!Interior) BuildViewer();
        var lamp = new OmniLight3D { LightColor = Lamp, LightEnergy = Interior ? 1.4f : 1.6f, OmniRange = 9f, Position = new Vector3(0.3f, 2.6f, 0.6f) };
        lamp.SetMeta(AlwaysOnMeta, true);
        AddChild(lamp);
        BuildRoom();
        _props = new Node3D();
        AddChild(_props);
        Rebuild();
        if (GameState.Instance != null) GameState.Instance.HomeChanged += Rebuild;
    }

    void BuildViewer()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.04f, 0.035f, 0.06f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.6f, 0.55f, 0.65f),
            AmbientLightEnergy = 0.55f,
        };
        AddChild(new WorldEnvironment { Environment = env });
        AddChild(new DirectionalLight3D { LightColor = new Color(0.75f, 0.8f, 1f), LightEnergy = 0.35f, Rotation = new Vector3(-0.9f, 0.6f, 0) });
        _cam = new Camera3D { Fov = 42, Current = true };
        AddChild(_cam);
    }

    void BuildRoom()
    {
        // cômodo em corte: piso, duas paredes, janela noturna e porta
        Box(this, new Vector3(6.4f, 0.2f, 5.2f), new Vector3(0, -0.1f, 0), Wood);
        for (int i = 0; i < 8; i++) Box(this, new Vector3(0.03f, 0.01f, 5.2f), new Vector3(-2.8f + i * 0.8f, 0.005f, 0), WoodDark);
        Box(this, new Vector3(6.4f, 3.2f, 0.2f), new Vector3(0, 1.6f, -2.5f), Wall);
        Box(this, new Vector3(0.2f, 3.2f, 5.2f), new Vector3(-3.1f, 1.6f, 0), Wall.Darkened(0.1f));
        Box(this, new Vector3(6.4f, 0.25f, 0.24f), new Vector3(0, 0.12f, -2.38f), WoodDark);
        // janela com céu noturno e cristais distantes
        Box(this, new Vector3(1.6f, 1.1f, 0.05f), new Vector3(1.2f, 1.9f, -2.38f), new Color(0.05f, 0.08f, 0.2f), new Color(0.1f, 0.15f, 0.4f), 0.8f);
        Box(this, new Vector3(1.7f, 0.08f, 0.1f), new Vector3(1.2f, 1.33f, -2.35f), WoodDark);
        Box(this, new Vector3(0.06f, 1.1f, 0.08f), new Vector3(1.2f, 1.9f, -2.34f), WoodDark);
        Box(this, new Vector3(1.6f, 0.06f, 0.08f), new Vector3(1.2f, 1.9f, -2.34f), WoodDark);
        Box(this, new Vector3(0.07f, 0.07f, 0.02f), new Vector3(0.75f, 2.2f, -2.35f), Colors.White, Colors.White, 2f);
        Box(this, new Vector3(0.05f, 0.05f, 0.02f), new Vector3(1.55f, 2.3f, -2.35f), Colors.White, Colors.White, 2f);
        // porta na parede lateral
        Box(this, new Vector3(0.08f, 2.1f, 1.0f), new Vector3(-2.98f, 1.05f, 1.4f), WoodDark);
        Box(this, new Vector3(0.1f, 0.08f, 0.08f), new Vector3(-2.92f, 1.0f, 1.05f), Metal);
        // luminária de teto
        Box(this, new Vector3(0.02f, 0.6f, 0.02f), new Vector3(0.3f, 2.9f, 0.6f), Metal);
        Sphere(this, 0.16f, new Vector3(0.3f, 2.55f, 0.6f), Lamp, Lamp, 2.2f);
        // tapete
        Box(this, new Vector3(2.0f, 0.02f, 1.3f), new Vector3(0.4f, 0.02f, 0.9f), new Color(0.55f, 0.18f, 0.16f));

        if (!Interior) return;
        // casa de verdade: fecha o cômodo (parede da frente, parede direita, teto) e cria colisões
        Box(this, new Vector3(6.4f, 3.2f, 0.2f), new Vector3(0, 1.6f, 2.5f), Wall.Darkened(0.05f));
        Box(this, new Vector3(0.2f, 3.2f, 5.2f), new Vector3(3.1f, 1.6f, 0), Wall.Darkened(0.15f));
        Box(this, new Vector3(6.4f, 0.2f, 5.2f), new Vector3(0, 3.3f, 0), new Color(0.32f, 0.24f, 0.18f));
        for (int i = 0; i < 4; i++) Box(this, new Vector3(0.18f, 0.16f, 5.2f), new Vector3(-2.4f + i * 1.6f, 3.12f, 0), WoodDark); // vigas
        Solid(this, new Vector3(6.4f, 0.2f, 5.2f), new Vector3(0, -0.1f, 0));
        Solid(this, new Vector3(6.4f, 3.4f, 0.2f), new Vector3(0, 1.6f, -2.5f));
        Solid(this, new Vector3(6.4f, 3.4f, 0.2f), new Vector3(0, 1.6f, 2.5f));
        Solid(this, new Vector3(0.2f, 3.4f, 5.2f), new Vector3(-3.1f, 1.6f, 0));
        Solid(this, new Vector3(0.2f, 3.4f, 5.2f), new Vector3(3.1f, 1.6f, 0));
        Solid(this, new Vector3(6.4f, 0.2f, 5.2f), new Vector3(0, 3.3f, 0));
        // quadro de cortiça com o mapa da mina, na parede da frente
        Box(this, new Vector3(1.3f, 0.8f, 0.04f), new Vector3(-0.1f, 1.9f, 2.38f), new Color(0.6f, 0.45f, 0.28f));
        for (int i = 0; i < 4; i++)
            Box(this, new Vector3(0.2f, 0.25f, 0.02f), new Vector3(-0.5f + i * 0.27f, 1.85f + (i % 2) * 0.12f, 2.36f), new Color(0.9f, 0.88f, 0.8f));
    }

    /// <summary>Colisão estática (só na casa de verdade).</summary>
    void Solid(Node3D parent, Vector3 size, Vector3 pos)
    {
        if (!Interior) return;
        var body = new StaticBody3D { CollisionLayer = Player.PlayerController.WorldLayer, CollisionMask = 0, Position = pos };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(body);
    }

    public override void _ExitTree()
    {
        if (GameState.Instance != null) GameState.Instance.HomeChanged -= Rebuild;
    }

    public override void _Process(double delta)
    {
        if (_cam == null) return;
        _t += (float)delta;
        float a = 0.55f + Mathf.Sin(_t * 0.25f) * 0.18f;
        var target = new Vector3(0.2f, 0.9f, -0.4f);
        _cam.Position = target + new Vector3(Mathf.Sin(a) * 6.6f, 3.4f, Mathf.Cos(a) * 6.6f);
        _cam.LookAt(target, Vector3.Up);
    }

    /// <summary>Remonta os móveis conforme os níveis atuais da casa.</summary>
    public void Rebuild()
    {
        if (_props == null) return;
        foreach (Node c in _props.GetChildren()) c.QueueFree();
        var gs = GameState.Instance;
        int bed = gs?.HouseLevel(HouseIds.Bed) ?? 0;
        int bench = gs?.HouseLevel(HouseIds.Workbench) ?? 0;
        int wardrobe = gs?.HouseLevel(HouseIds.Wardrobe) ?? 0;
        int kitchen = gs?.HouseLevel(HouseIds.Kitchen) ?? 0;
        int trophies = gs?.HouseLevel(HouseIds.Trophies) ?? 0;
        var p = _props;

        // ---- cama (canto do fundo à esquerda)
        var bp = new Vector3(-1.9f, 0, -1.3f);
        if (bed == 0)
            Box(p, new Vector3(1.0f, 0.1f, 1.9f), bp + new Vector3(0, 0.05f, 0), new Color(0.35f, 0.33f, 0.3f)); // colchonete
        else
        {
            Box(p, new Vector3(1.2f, 0.35f, 2.1f), bp + new Vector3(0, 0.18f, 0), Wood);
            // (a colisão da cama vem da estação "Dormir" da casa)
            Box(p, new Vector3(1.1f, 0.18f, 2.0f), bp + new Vector3(0, 0.44f, 0), new Color(0.9f, 0.88f, 0.82f));
            if (bed >= 2)
            {
                Box(p, new Vector3(1.14f, 0.06f, 1.3f), bp + new Vector3(0, 0.55f, 0.32f), Fabric);
                Box(p, new Vector3(0.7f, 0.14f, 0.35f), bp + new Vector3(0, 0.6f, -0.75f), Colors.White);
            }
            if (bed >= 3)
            {
                Box(p, new Vector3(1.3f, 1.1f, 0.12f), bp + new Vector3(0, 0.55f, -1.05f), WoodDark);
                Box(p, new Vector3(0.45f, 0.5f, 0.45f), bp + new Vector3(0.95f, 0.25f, -0.75f), Wood);
                Sphere(p, 0.12f, bp + new Vector3(0.95f, 0.65f, -0.75f), Lamp, Lamp, 2f);
            }
        }

        // ---- bancada de ferramentas (parede do fundo, centro)
        var wp = new Vector3(0.2f, 0, -2.05f);
        if (bench >= 1)
        {
            Box(p, new Vector3(1.6f, 0.08f, 0.7f), wp + new Vector3(0, 0.9f, 0), Wood);
            Solid(p, new Vector3(1.6f, 0.95f, 0.7f), wp + new Vector3(0, 0.47f, 0));
            foreach (float x in new[] { -0.7f, 0.7f })
                Box(p, new Vector3(0.08f, 0.9f, 0.6f), wp + new Vector3(x, 0.45f, 0), WoodDark);
            Box(p, new Vector3(1.6f, 0.6f, 0.04f), wp + new Vector3(0, 1.6f, -0.3f), new Color(0.3f, 0.32f, 0.3f));
            for (int i = 0; i < bench; i++)
            {
                // ferramentas penduradas e peças na bancada
                Box(p, new Vector3(0.05f, 0.35f, 0.04f), wp + new Vector3(-0.6f + i * 0.3f, 1.6f, -0.26f), Metal);
                Box(p, new Vector3(0.18f, 0.08f, 0.12f), wp + new Vector3(-0.55f + i * 0.28f, 0.98f, 0.1f), i % 2 == 0 ? Metal : new Color(1f, 0.55f, 0.18f));
            }
            if (bench >= 5)
            {
                // núcleo de cristal brilhando no torno
                var prism = new MeshInstance3D { Mesh = MeshFactory.CrystalPrism(6, 0.12f, 0.45f, 0.35f), MaterialOverride = MeshFactory.Emissive(new Color(0.4f, 0.9f, 1f), new Color(0.4f, 0.9f, 1f), 2.4f), Position = wp + new Vector3(0.55f, 0.95f, 0.1f) };
                p.AddChild(prism);
            }
        }
        else { Box(p, new Vector3(0.6f, 0.5f, 0.6f), wp + new Vector3(0, 0.25f, 0.1f), WoodDark); Solid(p, new Vector3(0.6f, 0.5f, 0.6f), wp + new Vector3(0, 0.25f, 0.1f)); } // caixote

        // ---- armário (parede lateral esquerda, perto da porta)
        if (wardrobe >= 1)
        {
            float h = 1.3f + wardrobe * 0.25f, w = 0.7f + wardrobe * 0.15f;
            var ap = new Vector3(-2.65f, 0, 0.1f);
            Box(p, new Vector3(0.6f, h, w), ap + new Vector3(0, h / 2, 0), Wood);
            Solid(p, new Vector3(0.6f, h, w), ap + new Vector3(0, h / 2, 0));
            Box(p, new Vector3(0.02f, h - 0.1f, 0.02f), ap + new Vector3(0.31f, h / 2, 0), WoodDark);
            Box(p, new Vector3(0.04f, 0.12f, 0.04f), ap + new Vector3(0.33f, h * 0.55f, 0.08f), Metal);
            Box(p, new Vector3(0.04f, 0.12f, 0.04f), ap + new Vector3(0.33f, h * 0.55f, -0.08f), Metal);
            // capacete de mineiro em cima
            Sphere(p, 0.17f, ap + new Vector3(0, h + 0.12f, 0), new Color(0.95f, 0.72f, 0.12f), null, 0);
        }

        // ---- cozinha (canto do fundo à direita)
        var kp = new Vector3(2.45f, 0, -1.8f);
        if (kitchen >= 1)
        {
            Box(p, new Vector3(1.1f, 0.9f, 0.8f), kp + new Vector3(0, 0.45f, 0), new Color(0.75f, 0.75f, 0.72f));
            Solid(p, new Vector3(1.1f, 0.9f, 0.8f), kp + new Vector3(0, 0.45f, 0));
            Box(p, new Vector3(1.12f, 0.05f, 0.82f), kp + new Vector3(0, 0.92f, 0), new Color(0.2f, 0.2f, 0.22f));
            Box(p, new Vector3(0.25f, 0.02f, 0.25f), kp + new Vector3(-0.25f, 0.95f, 0), new Color(1f, 0.3f, 0.1f), new Color(1f, 0.3f, 0.1f), 1.5f);
            if (kitchen >= 2)
                Cyl(p, 0.1f, 0.22f, kp + new Vector3(-0.25f, 1.07f, 0), Metal); // chaleira
            if (kitchen >= 3)
            {
                Box(p, new Vector3(0.75f, 1.9f, 0.7f), kp + new Vector3(0.05f, 0.95f, 1.0f), new Color(0.85f, 0.88f, 0.9f));
                Solid(p, new Vector3(0.75f, 1.9f, 0.7f), kp + new Vector3(0.05f, 0.95f, 1.0f));
                Box(p, new Vector3(0.03f, 0.5f, 0.04f), kp + new Vector3(-0.34f, 1.3f, 1.2f), Metal);
                Cyl(p, 0.07f, 0.12f, kp + new Vector3(0.25f, 1.0f, 0.1f), new Color(0.95f, 0.95f, 0.95f)); // caneca
            }
        }

        // ---- estante de troféus (parede do fundo, entre a janela e a cozinha)
        if (trophies >= 1)
        {
            var tp = new Vector3(1.2f, 0, -2.25f);
            Box(p, new Vector3(1.6f, 0.06f, 0.35f), tp + new Vector3(0, 0.95f, 0), WoodDark);
            if (trophies >= 3) Box(p, new Vector3(1.6f, 0.06f, 0.35f), tp + new Vector3(0, 0.45f, 0), WoodDark);
            var mats = GameData.Materials.Values;
            int i = 0;
            foreach (var m in mats)
            {
                if (i >= trophies * 2) break;
                float shelfY = i < 5 ? 0.98f : 0.48f;
                float x = -0.6f + (i % 5) * 0.3f;
                var prism = new MeshInstance3D
                {
                    Mesh = MeshFactory.CrystalPrism(6, 0.07f, 0.3f, 0.35f),
                    MaterialOverride = MeshFactory.Emissive(m.Color, m.Glow, 1.6f),
                    Position = tp + new Vector3(x, shelfY, 0),
                };
                p.AddChild(prism);
                i++;
            }
        }
    }

    // ---------------- utilidades ----------------
    static MeshInstance3D Box(Node3D parent, Vector3 size, Vector3 pos, Color col, Color? emission = null, float energy = 0)
    {
        var mi = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Mat(col, emission, energy), Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(mi);
        return mi;
    }

    static void Sphere(Node3D parent, float r, Vector3 pos, Color col, Color? emission, float energy)
    {
        parent.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 2, RadialSegments = 12, Rings = 6 }, MaterialOverride = Mat(col, emission, energy), Position = pos });
    }

    static void Cyl(Node3D parent, float r, float h, Vector3 pos, Color col)
    {
        parent.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h, RadialSegments = 10, Rings = 1 }, MaterialOverride = Mat(col, null, 0), Position = pos });
    }

    static StandardMaterial3D Mat(Color col, Color? emission, float energy)
    {
        var m = new StandardMaterial3D { AlbedoColor = col, Roughness = 0.8f };
        if (emission is Color e) { m.EmissionEnabled = true; m.Emission = e; m.EmissionEnergyMultiplier = energy; }
        return m;
    }
}
