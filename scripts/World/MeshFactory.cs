using System;
using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.World;

/// <summary>Geração procedural de malhas low-poly (cristais, rochas, estalactites) com cache.</summary>
public static class MeshFactory
{
    static readonly Dictionary<string, Mesh> Cache = new();
    static readonly Dictionary<string, Material> MatCache = new();

    /// <summary>Libera caches estáticos (antes de encerrar o processo, evita referências C# pendentes).</summary>
    public static void ClearCaches()
    {
        Cache.Clear();
        MatCache.Clear();
    }

    /// <summary>Prisma hexagonal com ponta (cristal). Base em y=0, ponta em y=height.</summary>
    public static ArrayMesh CrystalPrism(int sides = 6, float radius = 0.25f, float height = 1.2f, float tipRatio = 0.28f)
    {
        string key = $"prism{sides}_{radius}_{height}_{tipRatio}";
        if (Cache.TryGetValue(key, out var m)) return (ArrayMesh)m;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float body = height * (1 - tipRatio);
        var bottom = new Vector3[sides];
        var top = new Vector3[sides];
        for (int i = 0; i < sides; i++)
        {
            float a = Mathf.Tau * i / sides;
            float r0 = radius * 0.85f;
            bottom[i] = new Vector3(MathF.Cos(a) * r0, 0, MathF.Sin(a) * r0);
            top[i] = new Vector3(MathF.Cos(a) * radius, body, MathF.Sin(a) * radius);
        }
        var tip = new Vector3(0, height, 0);
        // UV.x = ângulo (0..1), UV.y = altura relativa (0..1): usados pelo shader mesmo após mesclar prismas
        void V(Vector3 p, Color c, float u)
        {
            st.SetColor(c);
            st.SetUV(new Vector2(u, p.Y / height));
            st.AddVertex(p);
        }
        var low = new Color(0.55f, 0.55f, 0.55f);
        var high = new Color(1, 1, 1);
        var tipC = new Color(1.25f, 1.25f, 1.25f);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            float ui = (float)i / sides, uj = (float)(i + 1) / sides;
            // laterais (ordem igual a Tri/Quad: a, c, b)
            V(bottom[i], low, ui); V(top[j], high, uj); V(bottom[j], low, uj);
            V(bottom[i], low, ui); V(top[i], high, ui); V(top[j], high, uj);
            // ponta
            V(top[i], high, ui); V(tip, tipC, (ui + uj) * 0.5f); V(top[j], high, uj);
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        Cache[key] = mesh;
        return mesh;
    }

    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
    {
        // ordem horária para Godot (face frontal)
        st.SetColor(ca); st.AddVertex(a);
        st.SetColor(cc); st.AddVertex(c);
        st.SetColor(cb); st.AddVertex(b);
    }

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color low, Color high)
    {
        Tri(st, a, b, c, low, low, high);
        Tri(st, a, c, d, low, high, high);
    }

    /// <summary>Rocha: icosfera deformada com ruído e sombreamento facetado.</summary>
    public static ArrayMesh Rock(int seed, float roughness = 0.35f)
    {
        string key = $"rock{seed}_{roughness}";
        if (Cache.TryGetValue(key, out var cached)) return (ArrayMesh)cached;
        var rng = new Random(seed);
        // icosaedro subdividido 1x
        var verts = new List<Vector3>();
        var tris = new List<int[]>();
        float t = (1f + MathF.Sqrt(5f)) / 2f;
        Vector3[] baseV =
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1)
        };
        foreach (var v in baseV) verts.Add(v.Normalized());
        int[][] faces =
        {
            new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
            new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
            new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
            new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1}
        };
        var mid = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (mid.TryGetValue(k, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).Normalized());
            mid[k] = verts.Count - 1;
            return verts.Count - 1;
        }
        foreach (var f in faces)
        {
            int a = Mid(f[0], f[1]), b = Mid(f[1], f[2]), c = Mid(f[2], f[0]);
            tris.Add(new[] { f[0], a, c }); tris.Add(new[] { f[1], b, a });
            tris.Add(new[] { f[2], c, b }); tris.Add(new[] { a, b, c });
        }
        var squash = new Vector3(1f + (float)rng.NextDouble() * 0.4f, 0.6f + (float)rng.NextDouble() * 0.4f, 1f + (float)rng.NextDouble() * 0.3f);
        for (int i = 0; i < verts.Count; i++)
        {
            float n = 1f + ((float)rng.NextDouble() * 2 - 1) * roughness;
            verts[i] = verts[i] * n * squash;
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var tr in tris)
        {
            var a = verts[tr[0]]; var b = verts[tr[1]]; var c = verts[tr[2]];
            float shade = 0.75f + (float)rng.NextDouble() * 0.25f;
            // tons mais escuros na parte de baixo (oclusão falsa)
            float ya = Mathf.Clamp(0.6f + a.Y * 0.35f, 0.35f, 1f) * shade;
            float yb = Mathf.Clamp(0.6f + b.Y * 0.35f, 0.35f, 1f) * shade;
            float yc = Mathf.Clamp(0.6f + c.Y * 0.35f, 0.35f, 1f) * shade;
            st.SetColor(new Color(ya, ya, ya)); st.AddVertex(a);
            st.SetColor(new Color(yc, yc, yc)); st.AddVertex(c);
            st.SetColor(new Color(yb, yb, yb)); st.AddVertex(b);
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        Cache[key] = mesh;
        return mesh;
    }

    /// <summary>Cone facetado (estalactite/estalagmite). Base em y=0, ponta em y=height.</summary>
    public static ArrayMesh Spike(int sides = 5, float radius = 0.5f, float height = 2f)
    {
        string key = $"spike{sides}_{radius}_{height}";
        if (Cache.TryGetValue(key, out var cached)) return (ArrayMesh)cached;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var tip = new Vector3(0, height, 0);
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
            var p0 = new Vector3(MathF.Cos(a0) * radius, 0, MathF.Sin(a0) * radius);
            var p1 = new Vector3(MathF.Cos(a1) * radius, 0, MathF.Sin(a1) * radius);
            Tri(st, p0, p1, tip, new Color(0.5f, 0.5f, 0.5f), new Color(0.5f, 0.5f, 0.5f), new Color(1, 1, 1));
        }
        st.GenerateNormals();
        var mesh = st.Commit();
        Cache[key] = mesh;
        return mesh;
    }

    // ===================== Materiais =====================

    /// <summary>Material de rocha com cor de vértice (sombreamento barato, sem texturas).</summary>
    public static StandardMaterial3D RockMaterial(Color c)
    {
        string key = "rock" + c.ToHtml();
        if (MatCache.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = c,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            Metallic = 0f,
        };
        MatCache[key] = mat;
        return mat;
    }

    /// <summary>Material de cristal emissivo (único por instância para permitir flash).</summary>
    public static StandardMaterial3D CrystalMaterial(Color albedo, Color glow, float energy = 1.1f)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = albedo,
            VertexColorUseAsAlbedo = true,
            Metallic = 0.2f,
            Roughness = 0.15f,
            EmissionEnabled = true,
            Emission = glow,
            EmissionEnergyMultiplier = energy,
            RimEnabled = true,
            Rim = 0.6f,
            RimTint = 0.3f,
        };
    }

    public static StandardMaterial3D Unshaded(Color c, bool transparent = false)
    {
        string key = "unsh" + c.ToHtml() + transparent;
        if (MatCache.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = c,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
        };
        if (transparent)
        {
            mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
            mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
            mat.BlendMode = BaseMaterial3D.BlendModeEnum.Add;
        }
        MatCache[key] = mat;
        return mat;
    }

    public static StandardMaterial3D Emissive(Color albedo, Color emission, float energy)
    {
        string key = "emi" + albedo.ToHtml() + emission.ToHtml() + energy;
        if (MatCache.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = albedo,
            EmissionEnabled = true,
            Emission = emission,
            EmissionEnergyMultiplier = energy,
            Roughness = 0.6f,
        };
        MatCache[key] = mat;
        return mat;
    }

    public static StandardMaterial3D Metal(Color c, float rough = 0.35f)
    {
        string key = "metal" + c.ToHtml() + rough;
        if (MatCache.TryGetValue(key, out var m)) return (StandardMaterial3D)m;
        var mat = new StandardMaterial3D { AlbedoColor = c, Metallic = 0.25f, Roughness = rough, RimEnabled = true, Rim = 0.25f, RimTint = 0.5f };
        MatCache[key] = mat;
        return mat;
    }
}

/// <summary>Utilitários de Basis.</summary>
public static class BasisExt
{
    /// <summary>Escala nos eixos locais (Basis.Scaled do Godot escala nos eixos globais).</summary>
    public static Basis ScaleLocal(this Basis b, Vector3 s) => b * Basis.FromScale(s);
}
