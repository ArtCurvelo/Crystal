using System;
using System.Collections.Generic;
using CrystalExcavator.Data;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Mining;

/// <summary>
/// Monta o visual de cada tipo de minério (formato + material/shader próprio).
/// Cada depósito vira 1–2 malhas mescladas (poucas chamadas de desenho).
/// </summary>
public static class OreVisuals
{
    static Shader _crystal, _ore;

    public sealed class Result
    {
        public readonly List<ShaderMaterial> Materials = new();
        public float Height = 1.5f;   // altura aproximada (colisão/partículas)
        public float Radius = 0.42f;
    }

    public static Result Build(Node3D parent, MaterialDef mat, Random rng, float s)
    {
        _crystal ??= GD.Load<Shader>("res://shaders/crystal.gdshader");
        _ore ??= GD.Load<Shader>("res://shaders/ore.gdshader");
        var r = new Result();
        switch (mat.Archetype)
        {
            case OreArchetype.Vein: BuildVein(parent, mat, rng, s, r); break;
            case OreArchetype.Geode: BuildGeode(parent, mat, rng, s, r); break;
            case OreArchetype.Banded: BuildBanded(parent, mat, rng, s, r); break;
            case OreArchetype.Shards: BuildShards(parent, mat, rng, s, r); break;
            case OreArchetype.Magma: BuildMagma(parent, mat, rng, s, r); break;
            case OreArchetype.Coral: BuildCoral(parent, mat, rng, s, r); break;
            case OreArchetype.Pearl: BuildPearl(parent, mat, rng, s, r); break;
            case OreArchetype.Ice: BuildCrystal(parent, mat, rng, s, r, ice: true); break;
            default: BuildCrystal(parent, mat, rng, s, r, ice: false); break;
        }
        return r;
    }

    // ---------------------------------------------------------------- materiais
    static ShaderMaterial CrystalMat(MaterialDef m, Random rng, Result r)
    {
        var sm = new ShaderMaterial { Shader = _crystal };
        sm.SetShaderParameter("base_color", m.Color);
        sm.SetShaderParameter("glow_color", m.Glow);
        sm.SetShaderParameter("seed", (float)rng.NextDouble() * 50f);
        r.Materials.Add(sm);
        return sm;
    }

    static ShaderMaterial OreMat(MaterialDef m, Random rng, Result r, int pattern, Color baseCol, Color col2, float scale = 3f)
    {
        var sm = new ShaderMaterial { Shader = _ore };
        sm.SetShaderParameter("pattern", pattern);
        sm.SetShaderParameter("base_color", baseCol);
        sm.SetShaderParameter("color2", col2);
        sm.SetShaderParameter("glow_color", m.Glow);
        sm.SetShaderParameter("scale", scale);
        sm.SetShaderParameter("seed", (float)rng.NextDouble() * 50f);
        r.Materials.Add(sm);
        return sm;
    }

    static readonly Color RockDark = new(0.3f, 0.25f, 0.27f);

    static void Add(Node3D parent, Mesh mesh, Material mat)
    {
        parent.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    static float F(Random rng) => (float)rng.NextDouble();

    /// <summary>Base de rocha (duas rochas mescladas).</summary>
    static ArrayMesh RockBase(Random rng, float s, float size = 1f, float roughness = 0.32f)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rock = MeshFactory.Rock(rng.Next(0, 6), roughness);
        st.AppendFrom(rock, 0, new Transform3D(Basis.Identity.ScaleLocal(new Vector3(0.62f, 0.36f, 0.62f) * s * size), new Vector3(0, 0.08f * s, 0)));
        float a = F(rng) * Mathf.Tau;
        st.AppendFrom(rock, 0, new Transform3D(new Basis(Vector3.Up, a).ScaleLocal(new Vector3(0.38f, 0.26f, 0.38f) * s * size),
            new Vector3(MathF.Cos(a) * 0.42f * s * size, 0.05f * s, MathF.Sin(a) * 0.42f * s * size)));
        return st.Commit();
    }

    // ---------------------------------------------------------------- cristal (e gelo)
    static void BuildCrystal(Node3D parent, MaterialDef m, Random rng, float s, Result r, bool ice)
    {
        bool special = m.Special;
        if (special) s = 2.6f;
        Add(parent, RockBase(rng, s), MeshFactory.RockMaterial(RockDark));
        Material crystalMat = ice
            ? OreMat(m, rng, r, 6, m.Color, Colors.White, 4f)
            : CrystalMat(m, rng, r);
        int count = special ? 9 : rng.Next(4, 8);
        int shards = special ? 8 : rng.Next(3, 6);
        var prism = MeshFactory.CrystalPrism(ice ? 4 : 6, 1f, 1f, ice ? 0.45f : 0.3f);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < count + shards; i++)
        {
            bool small = i >= count;
            float h = small ? (0.18f + F(rng) * 0.2f) * s : (0.6f + F(rng) * 0.9f) * s;
            float rad = small ? (0.05f + F(rng) * 0.04f) * s : (0.12f + F(rng) * 0.1f) * s;
            if (i == 0) { h *= 1.35f; rad *= 1.3f; }
            float ang = F(rng) * Mathf.Tau;
            float dist = i == 0 ? 0 : small ? (0.4f + F(rng) * 0.25f) * s : (0.12f + F(rng) * 0.22f) * s;
            var pos = new Vector3(MathF.Cos(ang) * dist, small ? 0.02f : 0.05f * s, MathF.Sin(ang) * dist);
            float tilt = i == 0 ? 0.1f : small ? 0.6f + F(rng) * 0.5f : 0.25f + F(rng) * 0.45f;
            var rot = new Vector3(MathF.Sin(ang) * tilt, F(rng) * Mathf.Tau, -MathF.Cos(ang) * tilt);
            st.AppendFrom(prism, 0, new Transform3D(Basis.FromEuler(rot).ScaleLocal(new Vector3(rad, h, rad)), pos));
        }
        Add(parent, st.Commit(), crystalMat);
        r.Height = 1.5f * s; r.Radius = 0.42f * s;
    }

    // ---------------------------------------------------------------- veio metálico
    static void BuildVein(Node3D parent, MaterialDef m, Random rng, float s, Result r)
    {
        // pedregulho grande com pepitas na textura + pepitas salientes de metal puro
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rock = MeshFactory.Rock(rng.Next(0, 6), 0.38f);
        st.AppendFrom(rock, 0, new Transform3D(Basis.Identity.ScaleLocal(new Vector3(0.75f, 0.6f, 0.7f) * s), new Vector3(0, 0.35f * s, 0)));
        for (int i = 0; i < 2; i++)
        {
            float a = F(rng) * Mathf.Tau;
            st.AppendFrom(rock, 0, new Transform3D(new Basis(Vector3.Up, a).ScaleLocal(new Vector3(0.42f, 0.34f, 0.42f) * s),
                new Vector3(MathF.Cos(a) * 0.55f * s, 0.15f * s, MathF.Sin(a) * 0.55f * s)));
        }
        Add(parent, st.Commit(), OreMat(m, rng, r, 3, m.Color.Darkened(0.4f).Lerp(new Color(0.35f, 0.32f, 0.33f), 0.5f), m.Color.Lightened(0.15f), 3.2f));
        var nug = new SurfaceTool();
        nug.Begin(Mesh.PrimitiveType.Triangles);
        var nm = MeshFactory.Rock(7, 0.45f);
        int n = rng.Next(5, 9);
        for (int i = 0; i < n; i++)
        {
            float a = F(rng) * Mathf.Tau, y = 0.2f + F(rng) * 0.7f;
            float rr = (0.62f - (y - 0.35f) * 0.5f) * s;
            float k = (0.07f + F(rng) * 0.08f) * s;
            nug.AppendFrom(nm, 0, new Transform3D(Basis.FromEuler(new Vector3(F(rng) * 3, F(rng) * 3, F(rng) * 3)).ScaleLocal(Vector3.One * k),
                new Vector3(MathF.Cos(a) * rr, y * s, MathF.Sin(a) * rr)));
        }
        var metal = new StandardMaterial3D { AlbedoColor = m.Color, Metallic = 0.6f, Roughness = 0.25f, EmissionEnabled = true, Emission = m.Color.Lerp(m.Glow, 0.5f), EmissionEnergyMultiplier = 0.8f, VertexColorUseAsAlbedo = true };
        Add(parent, nug.Commit(), metal);
        r.Height = 1.1f * s; r.Radius = 0.7f * s;
    }

    // ---------------------------------------------------------------- geodo
    static void BuildGeode(Node3D parent, MaterialDef m, Random rng, float s, Result r)
    {
        // casca de rocha arredondada com abertura no topo; cristais (ou esferas) brotando do centro
        var shell = new SurfaceTool();
        shell.Begin(Mesh.PrimitiveType.Triangles);
        var rock = MeshFactory.Rock(rng.Next(0, 6), 0.22f);
        int seg = 7;
        for (int i = 0; i < seg; i++)
        {
            float a = Mathf.Tau * i / seg;
            shell.AppendFrom(rock, 0, new Transform3D(new Basis(Vector3.Up, -a).ScaleLocal(new Vector3(0.34f, 0.32f, 0.2f) * s),
                new Vector3(MathF.Cos(a) * 0.58f * s, 0.2f * s, MathF.Sin(a) * 0.58f * s)));
        }
        shell.AppendFrom(rock, 0, new Transform3D(Basis.Identity.ScaleLocal(new Vector3(0.6f, 0.18f, 0.6f) * s), new Vector3(0, 0.08f * s, 0)));
        Add(parent, shell.Commit(), OreMat(m, rng, r, 0, new Color(0.42f, 0.38f, 0.36f), Colors.White, 2.5f));
        var inner = new SurfaceTool();
        inner.Begin(Mesh.PrimitiveType.Triangles);
        bool spheres = m.GeodeSpheres;
        Mesh piece = spheres ? new SphereMesh { Radius = 1, Height = 2, RadialSegments = 8, Rings = 4 } : MeshFactory.CrystalPrism(6, 1, 1, 0.4f);
        int n = rng.Next(14, 20);
        for (int i = 0; i < n; i++)
        {
            float a = F(rng) * Mathf.Tau, d = F(rng) * 0.4f * s;
            float h = (0.4f + F(rng) * 0.5f) * s;
            float rad = (0.08f + F(rng) * 0.06f) * s;
            var pos = new Vector3(MathF.Cos(a) * d, 0.18f * s, MathF.Sin(a) * d);
            if (spheres)
                inner.AppendFrom(piece, 0, new Transform3D(Basis.Identity.ScaleLocal(Vector3.One * rad * 2.2f), pos + Vector3.Up * rad * 1.6f));
            else
                inner.AppendFrom(piece, 0, new Transform3D(Basis.FromEuler(new Vector3(MathF.Sin(a) * 0.4f, F(rng) * 6, -MathF.Cos(a) * 0.4f)).ScaleLocal(new Vector3(rad, h, rad)), pos));
        }
        Material innerMat = spheres ? OreMat(m, rng, r, 4, m.Color, m.Glow, 3f) : CrystalMat(m, rng, r);
        Add(parent, inner.Commit(), innerMat);
        r.Height = 0.9f * s; r.Radius = 0.62f * s;
    }

    // ---------------------------------------------------------------- rocha em faixas
    static void BuildBanded(Node3D parent, MaterialDef m, Random rng, float s, Result r)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rock = MeshFactory.Rock(rng.Next(0, 6), 0.12f);
        st.AppendFrom(rock, 0, new Transform3D(Basis.FromEuler(new Vector3(0, F(rng) * 6, 0.15f)).ScaleLocal(new Vector3(0.55f, 0.75f, 0.5f) * s), new Vector3(0, 0.55f * s, 0)));
        float a = F(rng) * Mathf.Tau;
        st.AppendFrom(rock, 0, new Transform3D(Basis.FromEuler(new Vector3(0.2f, a, 0)).ScaleLocal(new Vector3(0.38f, 0.42f, 0.36f) * s),
            new Vector3(MathF.Cos(a) * 0.5f * s, 0.25f * s, MathF.Sin(a) * 0.5f * s)));
        var band2 = m.Color.Luminance > 0.4f ? m.Color.Darkened(0.6f) : m.Color.Lightened(0.5f);
        Add(parent, st.Commit(), OreMat(m, rng, r, 2, m.Color, band2, 2.4f));
        r.Height = 1.3f * s; r.Radius = 0.6f * s;
    }

    // ---------------------------------------------------------------- lascas (obsidiana)
    static void BuildShards(Node3D parent, MaterialDef m, Random rng, float s, Result r)
    {
        Add(parent, RockBase(rng, s, 0.9f), MeshFactory.RockMaterial(RockDark));
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var spike = MeshFactory.Spike(3, 1f, 1f);
        int n = rng.Next(5, 9);
        for (int i = 0; i < n; i++)
        {
            float a = F(rng) * Mathf.Tau, d = (i == 0 ? 0 : 0.1f + F(rng) * 0.3f) * s;
            float h = (0.7f + F(rng) * 0.9f) * s * (i == 0 ? 1.3f : 1f);
            float rad = (0.12f + F(rng) * 0.1f) * s;
            float tilt = i == 0 ? 0.05f : 0.2f + F(rng) * 0.5f;
            st.AppendFrom(spike, 0, new Transform3D(Basis.FromEuler(new Vector3(MathF.Sin(a) * tilt, F(rng) * 6, -MathF.Cos(a) * tilt)).ScaleLocal(new Vector3(rad, h, rad * 0.6f)),
                new Vector3(MathF.Cos(a) * d, 0.05f * s, MathF.Sin(a) * d)));
        }
        Add(parent, st.Commit(), OreMat(m, rng, r, 7, m.Color, m.Glow, 3f));
        r.Materials[^1].SetShaderParameter("glow_energy", 0.6f);
        r.Height = 1.5f * s; r.Radius = 0.45f * s;
    }

    // ---------------------------------------------------------------- magma
    static void BuildMagma(Node3D parent, MaterialDef m, Random rng, float s, Result r)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var rock = MeshFactory.Rock(rng.Next(0, 6), 0.42f);
        st.AppendFrom(rock, 0, new Transform3D(Basis.Identity.ScaleLocal(new Vector3(0.7f, 0.65f, 0.7f) * s), new Vector3(0, 0.4f * s, 0)));
        for (int i = 0; i < 3; i++)
        {
            float a = F(rng) * Mathf.Tau;
            st.AppendFrom(rock, 0, new Transform3D(new Basis(Vector3.Up, a).ScaleLocal(new Vector3(0.3f, 0.28f, 0.3f) * s),
                new Vector3(MathF.Cos(a) * 0.55f * s, 0.12f * s, MathF.Sin(a) * 0.55f * s)));
        }
        Add(parent, st.Commit(), OreMat(m, rng, r, 5, new Color(0.16f, 0.1f, 0.09f), m.Color, 2.6f));
        r.Height = 1.2f * s; r.Radius = 0.7f * s;
    }

    // ---------------------------------------------------------------- coral
    static void BuildCoral(Node3D parent, MaterialDef m, Random rng, float s, Result r)
    {
        Add(parent, RockBase(rng, s, 0.8f), MeshFactory.RockMaterial(new Color(0.25f, 0.3f, 0.36f)));
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var branch = new CylinderMesh { TopRadius = 0.7f, BottomRadius = 1f, Height = 1f, RadialSegments = 6, Rings = 1 };
        var tip = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 6, Rings = 3 };
        void Grow(Vector3 start, Vector3 dir, float len, float rad, int depth)
        {
            var end = start + dir * len;
            var up = dir.Normalized();
            var x = up.Cross(MathF.Abs(up.Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized();
            var z = x.Cross(up).Normalized();
            st.AppendFrom(branch, 0, new Transform3D(new Basis(x, up, z).ScaleLocal(new Vector3(rad, len, rad)), (start + end) / 2));
            if (depth == 0) { st.AppendFrom(tip, 0, new Transform3D(Basis.Identity.ScaleLocal(Vector3.One * rad * 1.4f), end)); return; }
            int kids = 2 + rng.Next(2);
            for (int k = 0; k < kids; k++)
            {
                var nd = (dir + new Vector3(F(rng) - 0.5f, 0.2f + F(rng) * 0.4f, F(rng) - 0.5f) * 1.3f).Normalized();
                Grow(end, nd, len * (0.6f + F(rng) * 0.2f), rad * 0.7f, depth - 1);
            }
        }
        int trunks = rng.Next(3, 5);
        for (int t = 0; t < trunks; t++)
        {
            float a = F(rng) * Mathf.Tau;
            Grow(new Vector3(MathF.Cos(a) * 0.2f * s, 0.1f * s, MathF.Sin(a) * 0.2f * s), new Vector3(MathF.Cos(a) * 0.35f, 1f, MathF.Sin(a) * 0.35f).Normalized(), 0.6f * s, 0.09f * s, 2);
        }
        Add(parent, st.Commit(), OreMat(m, rng, r, 1, m.Color, m.Glow.Lightened(0.3f), 5f));
        r.Height = 1.4f * s; r.Radius = 0.55f * s;
    }

    // ---------------------------------------------------------------- pérolas
    static void BuildPearl(Node3D parent, MaterialDef m, Random rng, float s, Result r)
    {
        Add(parent, RockBase(rng, s, 0.9f), MeshFactory.RockMaterial(new Color(0.18f, 0.16f, 0.22f)));
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var sphere = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 12, Rings = 6 };
        int n = rng.Next(4, 7);
        for (int i = 0; i < n; i++)
        {
            float a = F(rng) * Mathf.Tau, d = (i == 0 ? 0 : 0.2f + F(rng) * 0.2f) * s;
            float rad = (i == 0 ? 0.42f : 0.16f + F(rng) * 0.14f) * s;
            st.AppendFrom(sphere, 0, new Transform3D(Basis.Identity.ScaleLocal(Vector3.One * rad), new Vector3(MathF.Cos(a) * d, 0.15f * s + rad, MathF.Sin(a) * d)));
        }
        Add(parent, st.Commit(), OreMat(m, rng, r, 4, m.Color, m.Glow, 2f));
        r.Height = 1f * s; r.Radius = 0.5f * s;
    }
}
