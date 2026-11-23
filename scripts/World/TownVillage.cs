using System;
using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.World;

/// <summary>Casas dos moradores (enxaimel), feira, poço, bancos, árvores, bandeirolas, flores e grama da vila.</summary>
public partial class Town
{
    public TownLife Life;
    /// <summary>Vidro das janelas: brilha mais à noite (moradores em casa).</summary>
    StandardMaterial3D _windowGlass;
    readonly List<OmniLight3D> _nightLights = new();

    /// <summary>Peça num nó girado (coordenadas locais da construção).</summary>
    static MeshInstance3D L(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? rot = null)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        parent.AddChild(m);
        return m;
    }

    void SolidLocal(Node3D n, Vector3 local, Vector3 size) => Solid(n.Position + local.Rotated(Vector3.Up, n.Rotation.Y), size, n.Rotation.Y);

    void BuildVillage(Vector3 c)
    {
        _windowGlass = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.82f, 0.5f), EmissionEnabled = true, Emission = new Color(1f, 0.72f, 0.38f), EmissionEnergyMultiplier = 1.2f, Roughness = 0.2f };
        // casas dos moradores (frente = +Z local, virada para a praça)
        TimberHouse(c + new Vector3(-4f, 0, 18.6f), Mathf.Pi, 6.0f, 4.6f, 0, true);
        TimberHouse(c + new Vector3(7.4f, 0, 18.4f), Mathf.Pi, 5.2f, 4.4f, 1, false);
        TimberHouse(c + new Vector3(-15.0f, 0, -14.0f), Mathf.Pi / 4, 5.6f, 4.6f, 2, true);
        TimberHouse(c + new Vector3(14.8f, 0, -13.8f), -Mathf.Pi / 4, 5.4f, 4.4f, 3, false);
        Stall(c + new Vector3(-11.3f, 0, 12.6f), Mathf.Pi, new Color(0.8f, 0.18f, 0.15f), 0);
        Stall(c + new Vector3(-15.3f, 0, 8.4f), Mathf.Pi / 2, new Color(0.15f, 0.45f, 0.75f), 1);
        Well(c + new Vector3(10.1f, 0, 10.5f));
        Bench(c, 118f);
        Bench(c, 58.6f);
        // árvores de folha (as de copa colorida dão vida à praça)
        var rng = new Random(321);
        foreach (var (x, z) in new[] { (-9.5f, 18f), (2.0f, 21.5f), (13.5f, 16f), (-20f, 2f), (-21f, -6f), (20.5f, 6.5f), (21f, -4f), (-6f, -19.5f), (6.5f, -19.5f), (-19.5f, 13.5f), (0.5f, -22.5f) })
            Tree(c + new Vector3(x, 0, z), rng);
        Bunting(c);
        Flowers(c, rng);
        Grass(c, rng);
        Paths(c);
        Life = new TownLife(c) { IsNight = () => Night };
        AddChild(Life);
    }

    // ------------------------------------------------------------------ casa enxaimel
    void TimberHouse(Vector3 pos, float yaw, float w, float d, int style, bool twoFloors)
    {
        var n = new Node3D { Position = pos, Rotation = new Vector3(0, yaw, 0), Name = "VillageHouse" + style };
        AddChild(n);
        Color[] plasters = { new(0.93f, 0.88f, 0.76f), new(0.88f, 0.8f, 0.68f), new(0.85f, 0.88f, 0.82f), new(0.95f, 0.84f, 0.72f) };
        Color[] roofs = { new(0.55f, 0.2f, 0.14f), new(0.25f, 0.27f, 0.32f), new(0.62f, 0.5f, 0.28f), new(0.45f, 0.22f, 0.16f) };
        Color[] shutters = { new(0.2f, 0.4f, 0.25f), new(0.55f, 0.18f, 0.15f), new(0.2f, 0.3f, 0.5f), new(0.3f, 0.45f, 0.45f) };
        var plaster = Mat(plasters[style]);
        var beam = Mat(new Color(0.3f, 0.19f, 0.12f));
        var roof = Mat(roofs[style], 0.8f);
        var stone = MeshFactory.RockMaterial(new Color(0.48f, 0.45f, 0.43f));
        var shutter = Mat(shutters[style]);
        float h1 = 2.8f, h2 = twoFloors ? 2.2f : 0f;
        float top = 0.5f + h1 + h2;
        // base de pedra, paredes e vigas
        L(n, new BoxMesh { Size = new Vector3(w + 0.2f, 0.5f, d + 0.2f) }, stone, new Vector3(0, 0.25f, 0));
        L(n, new BoxMesh { Size = new Vector3(w, h1, d) }, plaster, new Vector3(0, 0.5f + h1 / 2, 0));
        if (twoFloors)
        {
            // segundo andar avançado (balanço típico do enxaimel)
            L(n, new BoxMesh { Size = new Vector3(w + 0.4f, h2, d + 0.4f) }, plaster, new Vector3(0, 0.5f + h1 + h2 / 2, 0));
            L(n, new BoxMesh { Size = new Vector3(w + 0.5f, 0.16f, d + 0.5f) }, beam, new Vector3(0, 0.5f + h1, 0));
        }
        SolidLocal(n, new Vector3(0, top / 2, 0), new Vector3(w + 0.3f, top, d + 0.3f));
        float fw = twoFloors ? w + 0.4f : w, fd = twoFloors ? d + 0.4f : d;
        foreach (var (y0, hh, ww, dd) in new[] { (0.5f, h1, w, d), (0.5f + h1, h2, fw, fd) })
        {
            if (hh <= 0) continue;
            foreach (int sx in new[] { -1, 1 })
                foreach (int sz in new[] { -1, 1 })
                    L(n, new BoxMesh { Size = new Vector3(0.18f, hh, 0.18f) }, beam, new Vector3(sx * ww / 2, y0 + hh / 2, sz * dd / 2));
            // vigas horizontais e as cruzes na frente e atrás
            foreach (int sz in new[] { -1, 1 })
            {
                L(n, new BoxMesh { Size = new Vector3(ww, 0.14f, 0.06f) }, beam, new Vector3(0, y0 + hh - 0.07f, sz * (dd / 2 + 0.02f)));
                L(n, new BoxMesh { Size = new Vector3(ww, 0.14f, 0.06f) }, beam, new Vector3(0, y0 + 0.07f, sz * (dd / 2 + 0.02f)));
                foreach (int sx in new[] { -1, 1 })
                {
                    float len = Mathf.Sqrt(hh * hh + (ww * 0.22f) * (ww * 0.22f));
                    L(n, new BoxMesh { Size = new Vector3(0.12f, len, 0.05f) }, beam, new Vector3(sx * ww * 0.39f, y0 + hh / 2, sz * (dd / 2 + 0.03f)), new Vector3(0, 0, sx * Mathf.Atan2(ww * 0.22f, hh)));
                }
            }
            foreach (int sx in new[] { -1, 1 })
            {
                L(n, new BoxMesh { Size = new Vector3(0.06f, 0.14f, dd) }, beam, new Vector3(sx * (ww / 2 + 0.02f), y0 + hh - 0.07f, 0));
                L(n, new BoxMesh { Size = new Vector3(0.06f, hh, 0.12f) }, beam, new Vector3(sx * (ww / 2 + 0.03f), y0 + hh / 2, 0));
            }
        }
        // telhado de duas águas com beiral largo e oitões
        float rw = fw + 0.9f, rd = fd / 2 + 0.6f, pitch = 0.62f;
        float rise = Mathf.Tan(pitch) * (fd / 2);
        foreach (int s in new[] { -1, 1 })
            L(n, new BoxMesh { Size = new Vector3(rw, 0.16f, rd / Mathf.Cos(pitch)) }, roof, new Vector3(0, top + rise / 2 + 0.05f, s * rd / 2 * 0.95f), new Vector3(s * pitch, 0, 0));
        L(n, new BoxMesh { Size = new Vector3(rw + 0.05f, 0.2f, 0.2f) }, beam, new Vector3(0, top + rise + 0.12f, 0));
        foreach (int sx in new[] { -1, 1 })
        {
            var gable = L(n, new PrismMesh { Size = new Vector3(fd, rise, 0.12f) }, plaster, new Vector3(sx * fw / 2, top + rise / 2, 0), new Vector3(0, Mathf.Pi / 2, 0));
            L(n, new BoxMesh { Size = new Vector3(0.05f, rise * 0.9f, 0.12f) }, beam, new Vector3(sx * (fw / 2 + 0.04f), top + rise * 0.45f, 0));
        }
        // chaminé com fumaça
        var chim = new Vector3(w * 0.28f, top + rise * 0.6f, -d * 0.15f);
        L(n, new BoxMesh { Size = new Vector3(0.6f, rise + 1.4f, 0.6f) }, stone, chim);
        L(n, new BoxMesh { Size = new Vector3(0.75f, 0.12f, 0.75f) }, stone, chim + new Vector3(0, (rise + 1.4f) / 2, 0));
        Smoke(n, chim + new Vector3(0, (rise + 1.4f) / 2 + 0.3f, 0));
        // porta com degrau, toldinho e lampião
        float fz = d / 2;
        L(n, new BoxMesh { Size = new Vector3(1.3f, 2.25f, 0.1f) }, beam, new Vector3(-w * 0.18f, 0.5f + 1.1f, fz + 0.03f));
        L(n, new BoxMesh { Size = new Vector3(1.05f, 2.05f, 0.08f) }, Mat(new Color(0.42f, 0.27f, 0.16f)), new Vector3(-w * 0.18f, 0.5f + 1.02f, fz + 0.07f));
        for (int i = 0; i < 3; i++)
            L(n, new BoxMesh { Size = new Vector3(0.03f, 1.95f, 0.02f) }, beam, new Vector3(-w * 0.18f - 0.3f + i * 0.3f, 0.5f + 1.02f, fz + 0.115f));
        L(n, new SphereMesh { Radius = 0.04f, Height = 0.08f }, Mat(new Color(0.75f, 0.6f, 0.3f), 0.3f, 0.7f), new Vector3(-w * 0.18f + 0.38f, 0.5f + 1.0f, fz + 0.14f));
        L(n, new BoxMesh { Size = new Vector3(1.6f, 0.2f, 0.7f) }, stone, new Vector3(-w * 0.18f, 0.1f, fz + 0.4f));
        L(n, new BoxMesh { Size = new Vector3(1.8f, 0.08f, 0.9f) }, roof, new Vector3(-w * 0.18f, 0.5f + 2.45f, fz + 0.4f), new Vector3(-0.35f, 0, 0));
        var lampPos = new Vector3(-w * 0.18f + 0.95f, 0.5f + 1.95f, fz + 0.18f);
        L(n, new BoxMesh { Size = new Vector3(0.16f, 0.24f, 0.16f) }, Glow(new Color(1f, 0.75f, 0.4f), 2.2f), lampPos);
        L(n, new BoxMesh { Size = new Vector3(0.2f, 0.04f, 0.2f) }, beam, lampPos + new Vector3(0, 0.14f, 0));
        var light = new OmniLight3D { LightColor = new Color(1f, 0.72f, 0.42f), LightEnergy = 0.9f, OmniRange = 6f, Position = n.Position + (lampPos + new Vector3(0, 0, 0.4f)).Rotated(Vector3.Up, n.Rotation.Y) };
        AddChild(light);
        _nightLights.Add(light);
        // janelas com venezianas e floreiras
        void Window(Vector3 p, float rotY)
        {
            var wnode = new Node3D { Position = p, Rotation = new Vector3(0, rotY, 0) };
            n.AddChild(wnode);
            L(wnode, new BoxMesh { Size = new Vector3(0.95f, 0.95f, 0.08f) }, beam, Vector3.Zero);
            L(wnode, new BoxMesh { Size = new Vector3(0.78f, 0.78f, 0.06f) }, _windowGlass, new Vector3(0, 0, 0.02f));
            L(wnode, new BoxMesh { Size = new Vector3(0.05f, 0.8f, 0.04f) }, beam, new Vector3(0, 0, 0.06f));
            L(wnode, new BoxMesh { Size = new Vector3(0.8f, 0.05f, 0.04f) }, beam, new Vector3(0, 0, 0.06f));
            foreach (int s in new[] { -1, 1 })
                L(wnode, new BoxMesh { Size = new Vector3(0.4f, 0.95f, 0.04f) }, shutter, new Vector3(s * 0.7f, 0, 0.05f), new Vector3(0, s * 0.25f, 0));
            L(wnode, new BoxMesh { Size = new Vector3(0.95f, 0.18f, 0.22f) }, Mat(new Color(0.45f, 0.3f, 0.18f)), new Vector3(0, -0.56f, 0.12f));
            var fr = new Random((int)(p.X * 100 + p.Z * 37 + style * 11));
            Color[] petals = { new(0.95f, 0.25f, 0.35f), new(1f, 0.8f, 0.2f), new(0.95f, 0.95f, 0.95f), new(0.7f, 0.35f, 0.9f), new(1f, 0.5f, 0.2f) };
            for (int i = 0; i < 7; i++)
            {
                var col = petals[fr.Next(petals.Length)];
                L(wnode, new SphereMesh { Radius = 0.07f, Height = 0.12f, RadialSegments = 6, Rings = 3 }, Mat(col), new Vector3(-0.38f + i * 0.127f, -0.42f + (float)fr.NextDouble() * 0.05f, 0.14f + (float)fr.NextDouble() * 0.05f));
                L(wnode, new SphereMesh { Radius = 0.06f, Height = 0.1f, RadialSegments = 6, Rings = 3 }, Mat(new Color(0.25f, 0.5f, 0.2f)), new Vector3(-0.38f + i * 0.127f, -0.47f, 0.08f));
            }
        }
        Window(new Vector3(w * 0.22f, 0.5f + 1.5f, fz + 0.05f), 0);
        Window(new Vector3(w / 2 + 0.05f, 0.5f + 1.5f, 0), Mathf.Pi / 2);
        Window(new Vector3(-w / 2 - 0.05f, 0.5f + 1.5f, 0), -Mathf.Pi / 2);
        if (twoFloors)
        {
            Window(new Vector3(-w * 0.22f, 0.5f + h1 + 1.1f, fz + 0.25f), 0);
            Window(new Vector3(w * 0.22f, 0.5f + h1 + 1.1f, fz + 0.25f), 0);
        }
        // lenha empilhada ao lado
        var log = Mat(new Color(0.5f, 0.34f, 0.2f));
        for (int i = 0; i < 9; i++)
            L(n, new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.1f, Height = 0.9f, RadialSegments = 7 }, log, new Vector3(w / 2 + 0.45f, 0.12f + (i / 3) * 0.19f, -d * 0.25f + (i % 3) * 0.21f + (i / 3 % 2) * 0.1f), new Vector3(0, 0, Mathf.Pi / 2));
        SolidLocal(n, new Vector3(w / 2 + 0.45f, 0.35f, -d * 0.05f), new Vector3(0.95f, 0.7f, 0.8f));
    }

    void Smoke(Node3D parent, Vector3 pos)
    {
        var fade = new Gradient();
        fade.SetColor(0, new Color(0.78f, 0.78f, 0.8f, 0.28f));
        fade.SetColor(1, new Color(0.78f, 0.78f, 0.8f, 0f));
        parent.AddChild(new CpuParticles3D
        {
            Amount = 10, Lifetime = 4f, Mesh = new SphereMesh { Radius = 0.25f, Height = 0.5f, RadialSegments = 8, Rings = 4 }, Direction = Vector3.Up, Spread = 12,
            Gravity = new Vector3(0.15f, 0.35f, 0), InitialVelocityMin = 0.3f, InitialVelocityMax = 0.55f, ScaleAmountMin = 0.7f, ScaleAmountMax = 1.8f,
            ColorRamp = fade, Position = pos,
            MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true },
        });
    }

    // ------------------------------------------------------------------ feira
    void Stall(Vector3 pos, float yaw, Color cloth, int kind)
    {
        var n = new Node3D { Position = pos, Rotation = new Vector3(0, yaw, 0), Name = "Stall" + kind };
        AddChild(n);
        var wood = Mat(new Color(0.5f, 0.34f, 0.2f));
        var dark = Mat(new Color(0.32f, 0.21f, 0.13f));
        // mesa, postes e toldo listrado (frente = −Z local, para o comprador)
        L(n, new BoxMesh { Size = new Vector3(2.6f, 0.1f, 1.0f) }, wood, new Vector3(0, 0.92f, 0));
        L(n, new BoxMesh { Size = new Vector3(2.5f, 0.85f, 0.06f) }, dark, new Vector3(0, 0.45f, -0.47f));
        foreach (int sx in new[] { -1, 1 })
            foreach (float z in new[] { -0.5f, 0.6f })
                L(n, new BoxMesh { Size = new Vector3(0.1f, z < 0 ? 2.3f : 2.7f, 0.1f) }, dark, new Vector3(sx * 1.25f, z < 0 ? 1.15f : 1.35f, z));
        for (int i = 0; i < 7; i++)
            L(n, new BoxMesh { Size = new Vector3(2.9f / 7f, 0.04f, 1.55f) }, Mat(i % 2 == 0 ? cloth : new Color(0.96f, 0.94f, 0.88f)), new Vector3(-1.45f + (i + 0.5f) * 2.9f / 7f, 2.5f, 0.0f), new Vector3(-0.26f, 0, 0));
        for (int i = 0; i < 7; i++)
            L(n, new PrismMesh { Size = new Vector3(2.9f / 7f, 0.22f, 0.03f) }, Mat(i % 2 == 0 ? cloth : new Color(0.96f, 0.94f, 0.88f)), new Vector3(-1.45f + (i + 0.5f) * 2.9f / 7f, 2.18f, -0.78f), new Vector3(Mathf.Pi, 0, 0));
        SolidLocal(n, new Vector3(0, 0.5f, 0), new Vector3(2.7f, 1.0f, 1.1f));
        // mercadoria: frutas e legumes em caixotes, ou pães e queijos
        var r = new Random(77 + kind);
        for (int b = 0; b < 3; b++)
        {
            var bx = new Vector3(-0.85f + b * 0.85f, 1.02f, -0.1f);
            L(n, new BoxMesh { Size = new Vector3(0.7f, 0.12f, 0.55f) }, dark, bx);
            Color col = kind == 0 ? new[] { new Color(0.85f, 0.12f, 0.1f), new Color(0.98f, 0.6f, 0.1f), new Color(0.4f, 0.7f, 0.2f) }[b]
                                  : new[] { new Color(0.85f, 0.6f, 0.3f), new Color(0.98f, 0.85f, 0.4f), new Color(0.6f, 0.35f, 0.25f) }[b];
            for (int i = 0; i < 9; i++)
            {
                float sz = kind == 0 ? 0.075f : 0.1f;
                Mesh m = kind == 0 ? new SphereMesh { Radius = sz, Height = sz * 2, RadialSegments = 8, Rings = 4 } : new CapsuleMesh { Radius = sz * 0.7f, Height = sz * 3f, RadialSegments = 8, Rings = 2 };
                L(n, m, Mat(col.Lightened((float)r.NextDouble() * 0.12f)), bx + new Vector3(-0.22f + (i % 3) * 0.22f, 0.12f + (i / 3 == 1 ? 0.04f : 0), -0.16f + (i / 3) * 0.16f), kind == 0 ? null : new Vector3(0, 0, Mathf.Pi / 2));
            }
        }
        // cestos, sacos e barris em volta
        L(n, new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 0.85f, RadialSegments = 10 }, wood, new Vector3(1.75f, 0.43f, 0.2f));
        L(n, new SphereMesh { Radius = 0.32f, Height = 0.55f, RadialSegments = 10, Rings = 5 }, Mat(new Color(0.78f, 0.7f, 0.52f)), new Vector3(-1.7f, 0.28f, 0.1f));
        L(n, new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.18f, Height = 0.25f, RadialSegments = 10 }, Mat(new Color(0.7f, 0.52f, 0.3f)), new Vector3(-1.75f, 0.12f, -0.6f));
        SolidLocal(n, new Vector3(1.75f, 0.43f, 0.2f), new Vector3(0.6f, 0.86f, 0.6f));
        // placa
        var sign = new Label3D
        {
            Text = Core.Loc.T(kind == 0 ? "FRUTAS" : "PADARIA"), FontSize = 56, OutlineSize = 10, PixelSize = 0.006f, Modulate = new Color(1f, 0.92f, 0.75f),
            Position = n.Position + new Vector3(0, 2.15f, -0.82f).Rotated(Vector3.Up, n.Rotation.Y), Rotation = new Vector3(0, n.Rotation.Y + Mathf.Pi, 0),
        };
        AddChild(sign);
    }

    // ------------------------------------------------------------------ poço
    void Well(Vector3 p)
    {
        var stone = MeshFactory.RockMaterial(new Color(0.55f, 0.52f, 0.5f));
        var wood = Mat(new Color(0.42f, 0.28f, 0.17f));
        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.Tau * i / 12f;
            M(this, new BoxMesh { Size = new Vector3(0.5f, 0.85f, 0.32f) }, stone, p + new Vector3(Mathf.Cos(a) * 0.82f, 0.42f, Mathf.Sin(a) * 0.82f), new Vector3(0, -a + Mathf.Pi / 2, 0));
        }
        M(this, new CylinderMesh { TopRadius = 0.7f, BottomRadius = 0.7f, Height = 0.02f, RadialSegments = 16 }, Mat(new Color(0.05f, 0.12f, 0.18f), 0.1f), p + Vector3.Up * 0.55f);
        foreach (int s in new[] { -1, 1 })
            M(this, new BoxMesh { Size = new Vector3(0.14f, 2.0f, 0.14f) }, wood, p + new Vector3(s * 0.85f, 1.3f, 0));
        M(this, new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 1.9f, RadialSegments = 8 }, wood, p + new Vector3(0, 1.9f, 0), new Vector3(0, 0, Mathf.Pi / 2));
        foreach (int s in new[] { -1, 1 })
            M(this, new BoxMesh { Size = new Vector3(2.1f, 0.08f, 0.95f) }, Mat(new Color(0.5f, 0.22f, 0.16f), 0.8f), p + new Vector3(0, 2.55f, s * 0.4f), new Vector3(s * 0.55f, 0, 0));
        M(this, new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.8f, RadialSegments = 4 }, Mat(new Color(0.7f, 0.6f, 0.45f)), p + new Vector3(0.1f, 1.5f, 0));
        M(this, new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.12f, Height = 0.25f, RadialSegments = 10 }, wood, p + new Vector3(0.1f, 1.0f, 0));
        Solid(p + Vector3.Up * 0.6f, new Vector3(2.0f, 1.2f, 2.0f));
    }

    void Bench(Vector3 c, float deg)
    {
        float a = Mathf.DegToRad(deg);
        var p = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 8.6f;
        var n = new Node3D { Position = p, Rotation = new Vector3(0, Mathf.Atan2(-Mathf.Cos(a), -Mathf.Sin(a)), 0) };
        AddChild(n);
        var wood = Mat(new Color(0.5f, 0.33f, 0.2f));
        var iron = Mat(new Color(0.15f, 0.15f, 0.17f), 0.5f, 0.4f);
        for (int i = 0; i < 3; i++)
            L(n, new BoxMesh { Size = new Vector3(1.7f, 0.05f, 0.13f) }, wood, new Vector3(0, 0.46f, -0.15f + i * 0.14f));
        for (int i = 0; i < 2; i++)
            L(n, new BoxMesh { Size = new Vector3(1.7f, 0.12f, 0.04f) }, wood, new Vector3(0, 0.7f + i * 0.16f, -0.25f), new Vector3(-0.15f, 0, 0));
        foreach (int s in new[] { -1, 1 })
        {
            L(n, new BoxMesh { Size = new Vector3(0.06f, 0.46f, 0.4f) }, iron, new Vector3(s * 0.75f, 0.23f, 0));
            L(n, new BoxMesh { Size = new Vector3(0.06f, 0.5f, 0.05f) }, iron, new Vector3(s * 0.75f, 0.72f, -0.26f), new Vector3(-0.15f, 0, 0));
        }
        SolidLocal(n, new Vector3(0, 0.3f, -0.05f), new Vector3(1.75f, 0.6f, 0.45f));
    }

    // ------------------------------------------------------------------ natureza
    void Tree(Vector3 p, Random rng)
    {
        var bark = Mat(new Color(0.36f, 0.25f, 0.17f), 0.95f);
        Color[] greens = { new(0.28f, 0.48f, 0.2f), new(0.35f, 0.55f, 0.22f), new(0.22f, 0.42f, 0.22f), new(0.7f, 0.45f, 0.15f), new(0.55f, 0.6f, 0.2f) };
        float h = 3.2f + (float)rng.NextDouble() * 1.6f;
        M(this, new CylinderMesh { TopRadius = 0.14f, BottomRadius = 0.26f, Height = h, RadialSegments = 8 }, bark, p + Vector3.Up * h / 2);
        // galhos
        for (int i = 0; i < 3; i++)
        {
            float a = i * 2.1f + (float)rng.NextDouble();
            M(this, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.09f, Height = 1.3f, RadialSegments = 6 }, bark, p + new Vector3(Mathf.Cos(a) * 0.4f, h * 0.75f, Mathf.Sin(a) * 0.4f), new Vector3(Mathf.Sin(a) * 0.8f, 0, -Mathf.Cos(a) * 0.8f));
        }
        var leaf = Mat(greens[rng.Next(greens.Length)], 0.9f);
        var leaf2 = Mat(greens[rng.Next(greens.Length)].Darkened(0.12f), 0.9f);
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6f + (float)rng.NextDouble() * 0.5f;
            float r = i == 0 ? 0 : 0.9f + (float)rng.NextDouble() * 0.5f;
            float s = 1.0f + (float)rng.NextDouble() * 0.6f;
            M(this, new SphereMesh { Radius = s, Height = s * 1.7f, RadialSegments = 12, Rings = 6 }, i % 2 == 0 ? leaf : leaf2, p + new Vector3(Mathf.Cos(a) * r, h + (i == 0 ? 0.7f : (float)rng.NextDouble() * 0.6f), Mathf.Sin(a) * r));
        }
        Solid(p + Vector3.Up * 1.5f, new Vector3(0.5f, 3f, 0.5f));
    }

    /// <summary>Bandeirolas coloridas penduradas entre os lampiões da praça (uma malha só).</summary>
    void Bunting(Vector3 c)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        Color[] cols = { new(0.9f, 0.2f, 0.2f), new(1f, 0.8f, 0.2f), new(0.2f, 0.6f, 0.9f), new(0.3f, 0.75f, 0.35f), new(0.9f, 0.5f, 0.85f), new(1f, 0.55f, 0.15f) };
        int k = 0;
        for (int i = 0; i < 6; i++)
        {
            float a0 = Mathf.Tau * i / 6f + 0.5f, a1 = Mathf.Tau * (i + 1) / 6f + 0.5f;
            var p0 = c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * 10.5f + Vector3.Up * 2.95f;
            var p1 = c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * 10.5f + Vector3.Up * 2.95f;
            // também uma corda até o monumento a cada lampião, formando uma "tenda" de bandeirolas
            foreach (var (q0, q1) in new[] { (p0, p1), (p0, c + Vector3.Up * 4.2f) })
            {
                int n = 14;
                Vector3 prev = q0;
                for (int j = 0; j <= n; j++)
                {
                    float t = j / (float)n;
                    var pt = q0.Lerp(q1, t) + Vector3.Down * Mathf.Sin(t * Mathf.Pi) * 0.7f;
                    if (j > 0)
                    {
                        // corda (fita fina) e bandeirinha triangular
                        var side = (pt - prev).Cross(Vector3.Up).Normalized() * 0.012f;
                        Quad(st, prev + side, pt + side, pt - side + Vector3.Down * 0.02f, prev - side + Vector3.Down * 0.02f, new Color(0.9f, 0.88f, 0.8f));
                        if (j % 1 == 0)
                        {
                            var col = cols[k++ % cols.Length];
                            var mid = prev.Lerp(pt, 0.5f);
                            var along = (pt - prev).Normalized() * 0.17f;
                            Tri(st, mid - along, mid + along, mid + Vector3.Down * 0.36f, col);
                        }
                    }
                    prev = pt;
                }
            }
        }
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 0.9f };
        M(this, st.Commit(), mat, Vector3.Zero);
    }

    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color col)
    {
        var nrm = (b - a).Cross(c - a).Normalized();
        foreach (var v in new[] { a, b, c }) { st.SetColor(col); st.SetNormal(nrm); st.AddVertex(v); }
    }
    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col) { Tri(st, a, b, c, col); Tri(st, a, c, d, col); }

    /// <summary>Canteiros de flores e moitas em volta da praça e das casas (MultiMesh: barato).</summary>
    void Flowers(Vector3 c, Random rng)
    {
        Color[] petals = { new(0.95f, 0.25f, 0.35f), new(1f, 0.82f, 0.2f), new(0.97f, 0.97f, 0.97f), new(0.65f, 0.35f, 0.95f), new(1f, 0.5f, 0.2f), new(0.4f, 0.6f, 1f) };
        var pts = new List<(Vector3 p, Color col, float s)>();
        // canteiros em arco entre os lampiões, fora do calçamento
        for (int i = 0; i < 6; i++)
        {
            float mid = Mathf.Tau * i / 6f + 0.5f + Mathf.Pi / 6f;
            if (Mathf.Abs(Mathf.Wrap(mid - Mathf.Pi, -Mathf.Pi, Mathf.Pi)) < 0.4f) continue; // na frente da estação
            if (Mathf.Abs(Mathf.Wrap(mid, -Mathf.Pi, Mathf.Pi)) < 0.35f) continue;          // caminho da casa
            for (int j = 0; j < 40; j++)
            {
                float a = mid + ((float)rng.NextDouble() - 0.5f) * 0.55f;
                float r = 10.0f + (float)rng.NextDouble() * 1.1f;
                pts.Add((c + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r), petals[rng.Next(petals.Length)], 0.5f + (float)rng.NextDouble() * 0.35f));
            }
        }
        var flower = new SphereMesh { Radius = 0.07f, Height = 0.1f, RadialSegments = 6, Rings = 3 };
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = flower, InstanceCount = pts.Count * 2 };
        for (int i = 0; i < pts.Count; i++)
        {
            var (p, col, s) = pts[i];
            mm.SetInstanceTransform(i * 2, new Transform3D(Basis.Identity.Scaled(Vector3.One * s), p + Vector3.Up * (0.28f * s)));
            mm.SetInstanceColor(i * 2, col);
            mm.SetInstanceTransform(i * 2 + 1, new Transform3D(Basis.Identity.Scaled(new Vector3(1.6f, 1.3f, 1.6f) * s), p + Vector3.Up * 0.05f));
            mm.SetInstanceColor(i * 2 + 1, new Color(0.22f, 0.45f, 0.18f).Lightened((float)rng.NextDouble() * 0.15f));
        }
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.9f }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>Tufos de grama espalhados longe das construções e dos caminhos.</summary>
    void Grass(Vector3 c, Random rng)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // tufo: 3 lâminas cruzadas
        var gr = new Random(5);
        for (int b = 0; b < 7; b++)
        {
            float a = b * 2.4f;
            var off = new Vector3(Mathf.Cos(a * 1.7f), 0, Mathf.Sin(a * 1.7f)) * 0.08f * (b % 3);
            var side = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.025f;
            var tip = off + new Vector3(Mathf.Cos(a + 1.3f), 0, Mathf.Sin(a + 1.3f)) * 0.1f + Vector3.Up * (0.28f + (float)gr.NextDouble() * 0.2f);
            foreach (var v in new[] { off - side, off + side, tip }) { st.SetColor(v.Y > 0.1f ? new Color(1f, 1f, 0.85f) : new Color(0.5f, 0.55f, 0.45f)); st.SetNormal(Vector3.Up); st.AddVertex(v); }
        }
        var blade = st.Commit();
        var list = new List<Transform3D>();
        var cols = new List<Color>();
        int tries = 0;
        while (list.Count < 900 && tries++ < 9000)
        {
            float a = (float)rng.NextDouble() * Mathf.Tau, r = 12f + (float)rng.NextDouble() * 17f;
            var p = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            if (Mathf.Abs(p.X + 11.5f) < 5f && Mathf.Abs(p.Z) < 5f) continue;   // estação
            if (Mathf.Abs(p.X) < 6f && p.Z < -7f && p.Z > -15f) continue;        // ferreiro
            if (p.X > 9f && Mathf.Abs(p.Z) < 4.5f) continue;                     // casa do jogador
            list.Add(new Transform3D(Basis.Identity.Rotated(Vector3.Up, (float)rng.NextDouble() * 3f).Scaled(Vector3.One * (0.7f + (float)rng.NextDouble() * 0.8f)), c + p));
            cols.Add(new Color(0.24f, 0.4f, 0.15f).Lightened((float)rng.NextDouble() * 0.12f));
        }
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = blade, InstanceCount = list.Count };
        for (int i = 0; i < list.Count; i++) { mm.SetInstanceTransform(i, list[i]); mm.SetInstanceColor(i, cols[i]); }
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1f }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>Caminhos de terra batida da praça até cada construção.</summary>
    void Paths(Vector3 c)
    {
        var dirt = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.52f, 0.38f), Roughness = 1f, AlbedoTexture = GroundNoise, Uv1Scale = new Vector3(3, 3, 3) };
        void Strip(Vector2 a, Vector2 b, float w)
        {
            var d = b - a;
            var mid = (a + b) / 2;
            M(this, new PlaneMesh { Size = new Vector2(w, d.Length()) }, dirt, c + new Vector3(mid.X, 0.006f, mid.Y), new Vector3(0, Mathf.Atan2(d.X, d.Y), 0));
        }
        Strip(new Vector2(-1.0f, 9.6f), new Vector2(-4f, 15.8f), 1.8f);
        Strip(new Vector2(5f, 8.5f), new Vector2(7.4f, 15.6f), 1.8f);
        Strip(new Vector2(-7f, -6f), new Vector2(-12.6f, -11.6f), 1.8f);
        Strip(new Vector2(7f, -6f), new Vector2(12.4f, -11.4f), 1.8f);
        Strip(new Vector2(-8f, 6f), new Vector2(-12f, 10.5f), 2.4f);
        Strip(new Vector2(9.5f, 0.8f), new Vector2(12f, 1.4f), 1.6f);
    }

    /// <summary>Janelas e lampiões das casas mais fortes à noite.</summary>
    void RefreshVillageLights(bool night)
    {
        if (_windowGlass != null) _windowGlass.EmissionEnergyMultiplier = night ? 2.4f : 0.6f;
        foreach (var l in _nightLights) l.Visible = night;
    }
}
