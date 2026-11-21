using System;
using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Malhas procedurais da decoração (estalactites, cogumelos, samambaias), com mais forma que
/// as primitivas: perfis torneados com irregularidade, curvas e cores gravadas nos vértices.
/// Tudo em cache: cada variante é criada uma vez e reaproveitada pelos MultiMesh.
/// </summary>
public static class DecorMeshes
{
    static readonly Dictionary<string, ArrayMesh> Cache = new();
    public static void ClearCaches() => Cache.Clear();

    /// <summary>
    /// Sólido de revolução: perfil (raio, altura, cor) girado em torno do eixo Y.
    /// wobble(ângulo, índice do anel) multiplica o raio (irregularidade); bend desloca em X pela altura.
    /// </summary>
    static ArrayMesh Lathe(IList<(float r, float y, Color c)> profile, int sides, Func<float, int, float> wobble = null, Func<float, float> bend = null, bool flipWinding = false)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        int rings = profile.Count;
        for (int i = 0; i < rings; i++)
        {
            var (r, y, c) = profile[i];
            float bx = bend?.Invoke(y) ?? 0f;
            for (int k = 0; k <= sides; k++)
            {
                float a = Mathf.Tau * (k % sides) / sides;
                float rr = r * (wobble?.Invoke(a, i) ?? 1f);
                st.SetColor(c);
                st.SetUV(new Vector2(k / (float)sides, y));
                st.AddVertex(new Vector3(MathF.Cos(a) * rr + bx, y, MathF.Sin(a) * rr));
            }
        }
        int row = sides + 1;
        for (int i = 0; i < rings - 1; i++)
            for (int k = 0; k < sides; k++)
            {
                int a = i * row + k, b = a + 1, c = a + row, d = c + 1;
                // a Godot usa sentido horário para a face da frente (vista de fora, com o perfil subindo)
                if (!flipWinding) { st.AddIndex(a); st.AddIndex(b); st.AddIndex(c); st.AddIndex(b); st.AddIndex(d); st.AddIndex(c); }
                else { st.AddIndex(a); st.AddIndex(c); st.AddIndex(b); st.AddIndex(b); st.AddIndex(c); st.AddIndex(d); }
            }
        st.GenerateNormals();
        return st.Commit();
    }

    static float Hash(int seed, int a, int b)
    {
        unchecked
        {
            uint h = (uint)(seed * 73856093) ^ (uint)(a * 19349663) ^ (uint)(b * 83492791);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xffffff) / (float)0xffffff;
        }
    }

    /// <summary>
    /// Estalagmite/estalactite: base larga com "anéis" de escorrimento, levemente torta, afinando até a
    /// ponta. Cor nos vértices: base escura (úmida) e ponta clara (calcita). Base em y=0, ponta em y=1, raio 1.
    /// </summary>
    public static ArrayMesh Dripstone(int variant)
    {
        string key = "drip" + variant;
        if (Cache.TryGetValue(key, out var m)) return m;
        var prof = new List<(float, float, Color)>();
        const int rings = 14;
        float phase = variant * 1.7f;
        for (int i = 0; i <= rings; i++)
        {
            float y = i / (float)rings;
            float r = MathF.Pow(1f - y, 1.35f) * (1f + 0.1f * MathF.Sin(y * 22f + phase)) + 0.015f;
            if (i == 0) r *= 1.15f; // pé mais largo, apoiado no chão
            float shade = Mathf.Lerp(0.55f, 1.05f, MathF.Pow(y, 0.8f));
            prof.Add((i == rings ? 0f : r, y, new Color(shade, shade, shade * 0.98f)));
        }
        float bendAmt = (Hash(variant, 1, 1) - 0.5f) * 0.5f;
        m = Lathe(prof, 9,
            (a, i) => 1f + (Hash(variant, (int)(a * 10), i) - 0.5f) * 0.28f * (1f - i / (float)rings),
            y => bendAmt * y * y);
        Cache[key] = m;
        return m;
    }

    /// <summary>Pé do cogumelo: bulbo na base, afinando e alargando um pouco sob o chapéu. Altura 1, raio ~1.</summary>
    public static ArrayMesh MushroomStem()
    {
        if (Cache.TryGetValue("mstem", out var m)) return m;
        var light = new Color(0.92f, 0.9f, 0.84f);
        var dark = new Color(0.55f, 0.52f, 0.5f);
        m = Lathe(new List<(float, float, Color)>
        {
            (0.9f, 0f, dark), (1.45f, 0.06f, dark.Lerp(light, 0.4f)), (1.25f, 0.16f, light), (0.95f, 0.35f, light),
            (0.85f, 0.7f, light), (0.95f, 0.92f, light), (1.1f, 1f, light), (0f, 1.01f, light),
        }, 8, (a, i) => 1f + MathF.Sin(a * 3f + i) * 0.04f);
        Cache["mstem"] = m;
        return m;
    }

    /// <summary>Chapéu do cogumelo (só a parte de cima): cúpula com borda dobrada. Cor clara no topo, mais escura na borda.</summary>
    public static ArrayMesh MushroomCap(int variant)
    {
        string key = "mcap" + variant;
        if (Cache.TryGetValue(key, out var m)) return m;
        float dome = variant == 0 ? 0.55f : variant == 1 ? 0.8f : 0.38f; // redondo, alto (sino) ou achatado
        var top = new Color(1f, 1f, 1f);
        var rim = new Color(0.62f, 0.62f, 0.62f);
        var prof = new List<(float, float, Color)>();
        const int n = 9;
        for (int i = 0; i <= n; i++)
        {
            float t = i / (float)n;          // 0 = centro do topo, 1 = borda
            float ang = t * Mathf.Pi * 0.5f;
            float r = MathF.Sin(ang) * (variant == 1 ? 0.85f : 1f);
            float y = MathF.Cos(ang) * dome;
            prof.Add((r, y, top.Lerp(rim, t * t)));
        }
        prof.Add((1.04f, -0.04f, rim.Darkened(0.2f))); // borda dobrada para baixo
        m = Lathe(prof, 12, (a, i) => 1f + (i > 6 ? MathF.Sin(a * 5f + variant) * 0.035f : 0f), null, true);
        Cache[key] = m;
        return m;
    }

    /// <summary>Lamelas sob o chapéu: cone raso escuro, com nervuras (raio ondulado).</summary>
    public static ArrayMesh MushroomGills()
    {
        if (Cache.TryGetValue("mgill", out var m)) return m;
        var c = new Color(0.35f, 0.3f, 0.32f);
        m = Lathe(new List<(float, float, Color)> { (1.0f, -0.03f, c), (0.55f, 0.06f, c.Darkened(0.2f)), (0.12f, 0.1f, c.Darkened(0.4f)), (0f, 0.1f, c) },
            24, (a, i) => i == 0 ? 1f : 1f + MathF.Sin(a * 24f) * 0.08f);
        Cache["mgill"] = m;
        return m;
    }

    /// <summary>
    /// Folha de samambaia: nervura central que se arqueia para fora (−Z) e folíolos dos dois lados,
    /// maiores no meio e menores nas pontas. Comprimento 1 em Y, largura total ~0.5. Cor nos vértices:
    /// base escura, ponta clara.
    /// </summary>
    public static ArrayMesh Frond(int variant)
    {
        string key = "frond" + variant;
        if (Cache.TryGetValue(key, out var m)) return m;
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        float arch = 0.35f + variant * 0.12f;
        Vector3 Rib(float t) => new(0, t * (1f - 0.25f * t), -arch * t * t);
        Color Col(float t) { float s = Mathf.Lerp(0.45f, 1.1f, t); return new Color(s, s, s); }
        void V(Vector3 p, float t) { st.SetColor(Col(t)); st.AddVertex(p); }
        const int leaflets = 11;
        // nervura: tira fina
        for (int i = 0; i < leaflets; i++)
        {
            float t0 = i / (float)leaflets, t1 = (i + 1) / (float)leaflets;
            var a = Rib(t0); var b = Rib(t1);
            float w0 = 0.012f * (1f - t0), w1 = 0.012f * (1f - t1);
            V(a + Vector3.Left * w0, t0); V(b + Vector3.Left * w1, t1); V(b + Vector3.Right * w1, t1);
            V(a + Vector3.Left * w0, t0); V(b + Vector3.Right * w1, t1); V(a + Vector3.Right * w0, t0);
        }
        // folíolos: losangos alongados que apontam para a ponta
        for (int i = 1; i < leaflets; i++)
        {
            float t = i / (float)leaflets;
            float len = 0.26f * MathF.Sin(MathF.PI * MathF.Pow(t, 0.8f)) + 0.03f;
            var root = Rib(t);
            var fwd = (Rib(Mathf.Min(1f, t + 0.05f)) - root).Normalized();
            foreach (float side in new[] { -1f, 1f })
            {
                var outDir = (Vector3.Right * side + fwd * 0.55f).Normalized();
                var tip = root + outDir * len + Vector3.Down * len * 0.15f;
                var mid = root + outDir * len * 0.45f;
                var perp = fwd * len * 0.16f;
                float tt = Mathf.Min(1f, t + 0.15f);
                V(root, t); V(mid + perp, tt); V(tip, tt);
                V(root, t); V(tip, tt); V(mid - perp, tt);
            }
        }
        st.GenerateNormals();
        m = st.Commit();
        Cache[key] = m;
        return m;
    }
}
