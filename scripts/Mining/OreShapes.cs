using System;
using System.Collections.Generic;
using CrystalExcavator.Data;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Mining;

/// <summary>
/// Forma própria de cada minério (o fragmento que cai e o ícone do HUD usam a mesma malha):
/// a família do minério (cristal, lasca, geodo, pepita, faixas, gelo, brasa, coral, pérola) define o estilo,
/// e um número tirado do nome dele varia pontas, facetas, tamanho, inclinação e o degradê de duas cores.
/// Cores vão nos vértices (cor do minério → brilho dele), então dois minérios da mesma família não ficam iguais.
/// </summary>
public static class OreShapes
{
    static readonly Dictionary<string, Mesh> Cache = new();

    public static Mesh For(MaterialDef m)
    {
        if (Cache.TryGetValue(m.Id, out var mesh)) return mesh;
        var rng = new Random(StableHash(m.Id));
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // duas cores bem distintas: a do minério (mais escura) e o brilho dele
        var a = m.Color.Darkened(0.15f); var b = m.Glow.Lightened(0.1f);
        switch (m.Archetype)
        {
            case OreArchetype.Shards: Shards(st, rng, a, b); break;
            case OreArchetype.Geode: Geode(st, rng, a, b, m.GeodeSpheres); break;
            case OreArchetype.Vein: Nugget(st, rng, a, b); break;
            case OreArchetype.Banded: Banded(st, rng, a, b); break;
            case OreArchetype.Ice: Ice(st, rng, a, b); break;
            case OreArchetype.Magma: Ember(st, rng, a, b); break;
            case OreArchetype.Coral: Coral(st, rng, a, b); break;
            case OreArchetype.Pearl: Pearl(st, rng, a, b); break;
            default: Cluster(st, rng, a, b); break;
        }
        st.GenerateNormals();
        mesh = st.Commit();
        Cache[m.Id] = mesh;
        return mesh;
    }

    public static void ClearCache() => Cache.Clear();

    /// <summary>Hash estável entre execuções (string.GetHashCode do .NET muda a cada processo).</summary>
    public static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }

    static float R(Random r, float a, float b) => a + (float)r.NextDouble() * (b - a);

    static void Tri(SurfaceTool st, Vector3 p0, Color c0, Vector3 p1, Color c1, Vector3 p2, Color c2)
    {
        st.SetColor(c0); st.AddVertex(p0);
        st.SetColor(c1); st.AddVertex(p1);
        st.SetColor(c2); st.AddVertex(p2);
    }

    static Basis Aim(Vector3 dir)
    {
        dir = dir.Normalized();
        var x = dir.Cross(Mathf.Abs(dir.Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized();
        var z = x.Cross(dir).Normalized();
        return new Basis(x, dir, z);
    }

    /// <summary>Prisma com ponta: base → corpo → ponta, cor degradê da base para a ponta.</summary>
    static void Prism(SurfaceTool st, Vector3 at, Vector3 dir, float rad, float len, int sides, float tip, Color cb, Color ct, float twist = 0f, float flat = 1f, bool doubleEnd = false)
    {
        var B = Aim(dir);
        Vector3 P(float ang, float y, float r) => at + B * new Vector3(Mathf.Cos(ang + twist * y) * r, y, Mathf.Sin(ang + twist * y) * r * flat);
        float body = len * (1f - tip);
        float y0 = doubleEnd ? len * tip * 0.6f : 0f;
        var mid = cb.Lerp(ct, 0.55f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = Mathf.Tau * i / sides, a1 = Mathf.Tau * (i + 1) / sides;
            var b0 = P(a0, y0, rad); var b1 = P(a1, y0, rad);
            var t0 = P(a0, body, rad * 0.92f); var t1 = P(a1, body, rad * 0.92f);
            Tri(st, b0, cb, t1, mid, t0, mid);
            Tri(st, b0, cb, b1, cb, t1, mid);
            var apex = at + B * new Vector3(0, len, 0);
            Tri(st, t0, mid, t1, mid, apex, ct);
            if (doubleEnd) Tri(st, b1, cb, b0, cb, at, cb.Darkened(0.2f));
            else Tri(st, b1, cb.Darkened(0.3f), b0, cb.Darkened(0.3f), at + B * new Vector3(0, y0, 0), cb.Darkened(0.4f));
        }
    }

    /// <summary>Esfera achatada / irregular (base de pepitas, pérolas, brasas).</summary>
    static void Blob(SurfaceTool st, Random rng, Vector3 c, Vector3 size, float rough, Func<Vector3, Color> color, int rings = 5, int segs = 8)
    {
        var jit = new float[(rings + 1) * segs];
        for (int i = 0; i < jit.Length; i++) jit[i] = 1f + R(rng, -rough, rough);
        Vector3 V(int ri, int si)
        {
            float th = Mathf.Pi * ri / rings, ph = Mathf.Tau * (si % segs) / segs;
            float j = ri == 0 || ri == rings ? 1f : jit[ri * segs + si % segs];
            var n = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
            return c + new Vector3(n.X * size.X, n.Y * size.Y, n.Z * size.Z) * j;
        }
        for (int ri = 0; ri < rings; ri++)
            for (int si = 0; si < segs; si++)
            {
                var p00 = V(ri, si); var p01 = V(ri, si + 1); var p10 = V(ri + 1, si); var p11 = V(ri + 1, si + 1);
                Tri(st, p00, color(p00 - c), p01, color(p01 - c), p11, color(p11 - c));
                Tri(st, p00, color(p00 - c), p11, color(p11 - c), p10, color(p10 - c));
            }
    }

    // ------------------------------------------------------------ famílias
    static void Cluster(SurfaceTool st, Random rng, Color a, Color b)
    {
        int n = 2 + rng.Next(3), sides = 4 + rng.Next(5);
        float tip = R(rng, 0.18f, 0.4f), twist = R(rng, -2f, 2f);
        bool dbl = rng.NextDouble() < 0.3;
        for (int i = 0; i < n; i++)
        {
            var dir = new Vector3(R(rng, -0.45f, 0.45f), 1f, R(rng, -0.45f, 0.45f));
            if (i == 0) dir = new Vector3(R(rng, -0.15f, 0.15f), 1f, R(rng, -0.15f, 0.15f));
            float s = i == 0 ? 1f : R(rng, 0.5f, 0.8f);
            Prism(st, new Vector3(R(rng, -0.03f, 0.03f), -0.1f, R(rng, -0.03f, 0.03f)), dir, R(rng, 0.045f, 0.075f) * s, R(rng, 0.22f, 0.32f) * s, sides, tip, a, b, twist, 1f, dbl);
        }
    }

    static void Shards(SurfaceTool st, Random rng, Color a, Color b)
    {
        int n = 2 + rng.Next(3);
        for (int i = 0; i < n; i++)
        {
            var dir = new Vector3(R(rng, -0.7f, 0.7f), 1f, R(rng, -0.7f, 0.7f));
            Prism(st, new Vector3(R(rng, -0.04f, 0.04f), -0.08f, R(rng, -0.04f, 0.04f)), dir, R(rng, 0.03f, 0.05f), R(rng, 0.16f, 0.3f), 3, R(rng, 0.35f, 0.6f), a, b, 0f, R(rng, 0.25f, 0.45f));
        }
    }

    static void Geode(SurfaceTool st, Random rng, Color a, Color b, bool spheres)
    {
        var shell = a.Darkened(0.55f);
        // casca: meia esfera virada para cima, aberta
        Blob(st, rng, new Vector3(0, -0.02f, 0), new Vector3(0.12f, 0.08f, 0.12f), 0.12f, p => p.Y > 0.03f ? shell.Lightened(0.1f) : shell, 4, 9);
        int n = 6 + rng.Next(7);
        for (int i = 0; i < n; i++)
        {
            float ang = Mathf.Tau * i / n + R(rng, -0.2f, 0.2f), rr = R(rng, 0.02f, 0.08f);
            var at = new Vector3(Mathf.Cos(ang) * rr, 0.05f, Mathf.Sin(ang) * rr);
            if (spheres) Blob(st, rng, at + Vector3.Up * 0.02f, Vector3.One * R(rng, 0.018f, 0.03f), 0.05f, _ => b, 3, 6);
            else Prism(st, at, new Vector3(Mathf.Cos(ang) * 0.4f, 1f, Mathf.Sin(ang) * 0.4f), R(rng, 0.012f, 0.022f), R(rng, 0.05f, 0.1f), 4, 0.45f, a, b);
        }
    }

    static void Nugget(SurfaceTool st, Random rng, Color a, Color b)
    {
        var rock = new Color(0.32f, 0.3f, 0.33f);
        float fleckScale = R(rng, 45f, 90f), seed = R(rng, 0f, 50f), amount = R(rng, 0.35f, 0.6f);
        // pepita: pedra com pintas/veias do metal (padrão diferente por minério)
        Blob(st, rng, Vector3.Zero, new Vector3(R(rng, 0.08f, 0.12f), R(rng, 0.06f, 0.09f), R(rng, 0.08f, 0.11f)), 0.28f,
            p => Mathf.Sin(p.X * fleckScale + seed) * Mathf.Cos(p.Z * fleckScale * 0.8f + seed) + Mathf.Sin(p.Y * fleckScale * 1.3f) * 0.5f > 1f - amount * 2f ? b : rock.Lerp(a, 0.25f), 9, 16);
    }

    static void Banded(SurfaceTool st, Random rng, Color a, Color b)
    {
        int bands = 3 + rng.Next(4);
        float freq = bands * Mathf.Pi / 0.12f, wav = R(rng, 0f, 30f);
        var c2 = rng.NextDouble() < 0.5 ? b : a.Lightened(0.45f);
        // seixo achatado com faixas onduladas
        Blob(st, rng, Vector3.Zero, new Vector3(R(rng, 0.11f, 0.14f), R(rng, 0.035f, 0.05f), R(rng, 0.08f, 0.1f)), 0.08f,
            p => Mathf.Sin(p.X * freq + Mathf.Sin(p.Z * wav) * 1.5f) > 0 ? a : c2, 8, 22);
    }

    static void Ice(SurfaceTool st, Random rng, Color a, Color b)
    {
        // bloco irregular de gelo (cubo com cantos deslocados) e um prisma preso nele
        float s = R(rng, 0.07f, 0.09f);
        var v = new Vector3[8];
        for (int i = 0; i < 8; i++) v[i] = new Vector3((i & 1) == 0 ? -s : s, (i & 2) == 0 ? -s : s, (i & 4) == 0 ? -s : s) + new Vector3(R(rng, -0.025f, 0.025f), R(rng, -0.025f, 0.025f), R(rng, -0.025f, 0.025f));
        int[][] f = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
        var pale = a.Lerp(Colors.White, 0.35f);
        foreach (var q in f)
        {
            Tri(st, v[q[0]], pale, v[q[1]], b, v[q[2]], pale);
            Tri(st, v[q[0]], pale, v[q[2]], pale, v[q[3]], b);
        }
        if (rng.NextDouble() < 0.6) Prism(st, new Vector3(0, s * 0.5f, 0), new Vector3(R(rng, -0.5f, 0.5f), 1, R(rng, -0.5f, 0.5f)), s * 0.4f, s * 1.6f, 6, 0.4f, pale, b);
    }

    static void Ember(SurfaceTool st, Random rng, Color a, Color b)
    {
        var dark = new Color(0.12f, 0.07f, 0.06f);
        float freq = R(rng, 55f, 90f), seed = R(rng, 0f, 20f);
        // brasa: pedra escura com rachaduras brilhando (padrão próprio)
        Blob(st, rng, Vector3.Zero, new Vector3(R(rng, 0.08f, 0.11f), R(rng, 0.07f, 0.1f), R(rng, 0.08f, 0.11f)), 0.3f,
            p => Mathf.Abs(Mathf.Sin(p.X * freq + seed) + Mathf.Sin(p.Z * freq * 0.9f) + Mathf.Sin(p.Y * freq * 1.2f)) < 0.5f ? b : dark, 9, 16);
    }

    static void Coral(SurfaceTool st, Random rng, Color a, Color b)
    {
        int arms = 2 + rng.Next(3);
        Prism(st, new Vector3(0, -0.1f, 0), Vector3.Up, 0.022f, 0.14f, 6, 0.1f, a, a.Lerp(b, 0.5f));
        for (int i = 0; i < arms; i++)
        {
            float ang = Mathf.Tau * i / arms + R(rng, -0.4f, 0.4f);
            var dir = new Vector3(Mathf.Cos(ang) * R(rng, 0.5f, 1f), 1f, Mathf.Sin(ang) * R(rng, 0.5f, 1f));
            Prism(st, new Vector3(0, R(rng, -0.04f, 0.02f), 0), dir, R(rng, 0.012f, 0.018f), R(rng, 0.1f, 0.16f), 5, 0.12f, a, b);
        }
    }

    static void Pearl(SurfaceTool st, Random rng, Color a, Color b)
    {
        int n = rng.NextDouble() < 0.35 ? 2 : 1;
        float squash = R(rng, 0.75f, 1.1f);
        for (int i = 0; i < n; i++)
        {
            float r = (i == 0 ? R(rng, 0.065f, 0.08f) : R(rng, 0.035f, 0.05f));
            var c = i == 0 ? Vector3.Zero : new Vector3(0.07f, -0.02f, 0.02f);
            Blob(st, rng, c, new Vector3(r, r * squash, r), 0.02f, p => a.Lerp(b, Mathf.Clamp(p.Y / r * 0.5f + 0.5f, 0, 1) * 0.6f), 6, 10);
        }
    }
}
