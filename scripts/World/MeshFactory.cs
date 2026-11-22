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

    /// <summary>
    /// Rocha: icosfera subdividida 2x, deformada por ruído contínuo (volumes, não "amassado" aleatório)
    /// e cortada por planos de fratura (faces lascadas de pedra quebrada). Sombreamento nos vértices:
    /// base e reentrâncias mais escuras, arestas salientes mais claras. Raio máximo ~1 (vezes o achatamento).
    /// </summary>
    public static ArrayMesh Rock(int seed, float roughness = 0.35f)
    {
        string key = $"rock{seed}_{roughness}";
        if (Cache.TryGetValue(key, out var cached)) return (ArrayMesh)cached;
        var rng = new Random(seed);
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
        tris.AddRange(faces);
        var mid = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (mid.TryGetValue(k, out int idx)) return idx;
            verts.Add(((verts[a] + verts[b]) * 0.5f).Normalized());
            mid[k] = verts.Count - 1;
            return verts.Count - 1;
        }
        for (int level = 0; level < 2; level++)
        {
            var next = new List<int[]>();
            foreach (var f in tris)
            {
                int a = Mid(f[0], f[1]), b = Mid(f[1], f[2]), c = Mid(f[2], f[0]);
                next.Add(new[] { f[0], a, c }); next.Add(new[] { f[1], b, a });
                next.Add(new[] { f[2], c, b }); next.Add(new[] { a, b, c });
            }
            tris = next;
            mid.Clear();
        }
        // ruído contínuo: soma de ondas em direções aleatórias (volumes grandes + detalhe)
        var waves = new List<(Vector3 dir, float freq, float phase, float amp)>();
        for (int w = 0; w < 7; w++)
        {
            var d = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 2 - 1).Normalized();
            float freq = w < 3 ? 1.2f + (float)rng.NextDouble() * 1.2f : 4.5f + (float)rng.NextDouble() * 4f;
            waves.Add((d, freq, (float)rng.NextDouble() * Mathf.Tau, w < 3 ? 0.55f : 0.3f)); // detalhe fino mais forte: pedra, não "pétala" lisa
        }
        float Noise(Vector3 p) { float n = 0; foreach (var (d, f, ph, a) in waves) n += MathF.Sin(p.Dot(d) * f + ph) * a; return n; }
        var squash = new Vector3(1f + (float)rng.NextDouble() * 0.4f, 0.6f + (float)rng.NextDouble() * 0.4f, 1f + (float)rng.NextDouble() * 0.3f);
        var disp = new float[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            disp[i] = Noise(verts[i] * 1.3f) * roughness * 0.55f;
            verts[i] = verts[i] * (1f + disp[i]);
        }
        // planos de fratura: o que passa do plano é achatado nele (faces planas e arestas vivas)
        int cuts = 3 + rng.Next(3);
        for (int k = 0; k < cuts; k++)
        {
            var n = new Vector3((float)rng.NextDouble() * 2 - 1, (float)rng.NextDouble() * 1.4f - 0.4f, (float)rng.NextDouble() * 2 - 1).Normalized();
            float dist = 0.62f + (float)rng.NextDouble() * 0.25f;
            for (int i = 0; i < verts.Count; i++)
            {
                float over = verts[i].Dot(n) - dist;
                if (over > 0) { verts[i] -= n * over * 0.92f; disp[i] -= over * 0.5f; }
            }
        }
        // apoio: a parte de baixo é achatada (a pedra assenta no chão)
        for (int i = 0; i < verts.Count; i++)
            if (verts[i].Y < -0.55f) verts[i] = new Vector3(verts[i].X, -0.55f + (verts[i].Y + 0.55f) * 0.25f, verts[i].Z);
        for (int i = 0; i < verts.Count; i++) verts[i] *= squash;

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            float up = Mathf.Clamp(0.55f + (v.Y / squash.Y) * 0.45f, 0.3f, 1f);   // base escura (oclusão falsa)
            float cav = Mathf.Clamp(1f + disp[i] * 1.6f, 0.6f, 1.15f);             // reentrâncias escuras, saliências claras
            float speck = 0.92f + 0.08f * MathF.Sin(v.X * 23f + v.Z * 17f + v.Y * 11f);
            float c = Mathf.Clamp(up * cav * speck, 0.25f, 1.1f);
            st.SetColor(new Color(c, c, c * 0.98f));
            st.AddVertex(v);
        }
        foreach (var tr in tris) { st.AddIndex(tr[0]); st.AddIndex(tr[2]); st.AddIndex(tr[1]); }
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

    static Shader _rockShader;

    /// <summary>
    /// Material das rochas soltas (decoração, pedregulhos, estalagmites, base dos depósitos): a mesma textura
    /// procedural da caverna (camadas, placas, rachaduras e granulado), na cor pedida, sem poças por cima,
    /// mais o sombreamento gravado nos vértices da malha. Antes era uma cor lisa, sem textura.
    /// </summary>
    public static Material RockMaterial(Color c)
    {
        string key = "rock" + c.ToHtml();
        if (MatCache.TryGetValue(key, out var m)) return m;
        _rockShader ??= GD.Load<Shader>("res://shaders/cave.gdshader");
        var mat = new ShaderMaterial { Shader = _rockShader };
        mat.SetShaderParameter("rock_color", c);
        mat.SetShaderParameter("floor_color", c.Darkened(0.08f));
        mat.SetShaderParameter("accent", new Color(0.35f, 0.9f, 1f));
        mat.SetShaderParameter("speckle", 0.4f);
        mat.SetShaderParameter("wetness", 0f);
        mat.SetShaderParameter("vertex_shade", 1f);
        mat.SetShaderParameter("noise_tex", CaveTerrain.NoiseTexture);
        mat.SetShaderParameter("cell_tex", CaveTerrain.CellTexture);
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
