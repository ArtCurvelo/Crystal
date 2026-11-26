using System;
using System.Collections.Generic;
using CrystalExcavator.Data;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Core;

/// <summary>
/// Névoa do mapa (tecla M): cada região é dividida em quadrados de <see cref="Cell"/> m e guarda, em bits, quais o
/// jogador já viu (raio da lanterna em volta dele). Fica no save (SaveData.Explored, base64) e continua depois
/// do renascimento — a mina é a mesma.
/// </summary>
public static class ExploreMap
{
    public const int Cell = 4;
    public const float RevealRadius = 14f;

    static SaveData _owner;
    static readonly Dictionary<string, byte[]> _bits = new();

    public static int Width(CaveLayout L) => (L.Nx + Cell - 1) / Cell;
    public static int Height(CaveLayout L) => (L.Nz + Cell - 1) / Cell;

    static byte[] Bits(SaveData d, string region, CaveLayout L, bool create)
    {
        if (d != _owner) { _bits.Clear(); _owner = d; }
        if (_bits.TryGetValue(region, out var b)) return b;
        int n = (Width(L) * Height(L) + 7) / 8;
        if (d.Explored != null && d.Explored.TryGetValue(region, out var s))
        {
            try { b = Convert.FromBase64String(s); } catch { b = null; }
            if (b != null && b.Length != n) b = null; // tamanho da região mudou: recomeça
        }
        if (b == null)
        {
            if (!create) return null;
            b = new byte[n];
        }
        _bits[region] = b;
        return b;
    }

    static bool Get(byte[] b, int k) => (b[k >> 3] & (1 << (k & 7))) != 0;

    /// <summary>Marca como visto o círculo em volta de p. true se algo novo apareceu.</summary>
    public static bool Reveal(SaveData d, RegionDef r, CaveLayout L, Vector3 p, float radius = RevealRadius)
    {
        if (d == null || L == null) return false;
        var b = Bits(d, r.Id, L, true);
        int w = Width(L), h = Height(L);
        float cx = (p.X - L.X0) / Cell, cz = (p.Z - L.Z0) / Cell, rc = radius / Cell;
        int i0 = Math.Max(0, (int)Mathf.Floor(cx - rc)), i1 = Math.Min(w - 1, (int)Mathf.Floor(cx + rc));
        int j0 = Math.Max(0, (int)Mathf.Floor(cz - rc)), j1 = Math.Min(h - 1, (int)Mathf.Floor(cz + rc));
        bool changed = false;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                float dx = i + 0.5f - cx, dz = j + 0.5f - cz;
                if (dx * dx + dz * dz > rc * rc + 0.5f) continue;
                int k = j * w + i;
                if (Get(b, k)) continue;
                b[k >> 3] |= (byte)(1 << (k & 7));
                changed = true;
            }
        if (changed)
        {
            d.Explored ??= new();
            d.Explored[r.Id] = Convert.ToBase64String(b);
        }
        return changed;
    }

    /// <summary>O ponto (x, z) da região já foi visto?</summary>
    public static bool IsExplored(SaveData d, RegionDef r, CaveLayout L, float x, float z)
    {
        var b = d == null ? null : Bits(d, r.Id, L, false);
        if (b == null) return false;
        int i = (int)((x - L.X0) / Cell), j = (int)((z - L.Z0) / Cell);
        if (i < 0 || j < 0 || i >= Width(L) || j >= Height(L)) return false;
        return Get(b, j * Width(L) + i);
    }

    /// <summary>A região tem alguma parte vista?</summary>
    public static bool Any(SaveData d, RegionDef r, CaveLayout L)
    {
        var b = d == null ? null : Bits(d, r.Id, L, false);
        if (b == null) return false;
        foreach (var x in b) if (x != 0) return true;
        return false;
    }

    /// <summary>Fração (0..1) do chão andável da região que já foi visto.</summary>
    public static float Fraction(SaveData d, RegionDef r, CaveLayout L)
    {
        var b = d == null ? null : Bits(d, r.Id, L, false);
        int w = Width(L), h = Height(L);
        int total = 0, seen = 0;
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
            {
                // o quadrado conta se o centro dele é chão alcançável
                int gi = Math.Min(L.Nx - 1, i * Cell + Cell / 2), gj = Math.Min(L.Nz - 1, j * Cell + Cell / 2);
                int idx = L.Idx(gi, gj);
                if (L.D[idx] >= 0f || L.Reach == null || !L.Reach[idx]) continue;
                total++;
                if (b != null && Get(b, j * w + i)) seen++;
            }
        return total == 0 ? 0f : (float)seen / total;
    }

    /// <summary>Esquece o cache (ex.: testes que trocam de save).</summary>
    public static void Reset() { _owner = null; _bits.Clear(); }
}
