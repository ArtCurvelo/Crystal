using System.Linq;
using System;
using System.Collections.Generic;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.World;

public enum CaveKind { Path, Chamber, Corridor, Crawl, Secret }

/// <summary>Poço: buraco fundo dentro de uma câmara, com tesouro no fundo e corda fixa para subir.</summary>
public sealed class CavePit
{
    public CaveFeature Host;
    public Vector2 Center;
    public float Radius, Depth;
    public Vector3 RopeTop, RopeBottom, TopExit;
    public float Floor => Host.Level - Depth;
}

/// <summary>Elemento da caverna: segmento (ou círculo, se A == B) com raio, nível do chão e altura do teto interpolados.</summary>
public sealed class CaveFeature
{
    public CaveKind Kind;
    public string Name;
    public Vector2 A, B;
    public float RA, RB;        // raio no início/fim
    public float LA, LB;        // nível do chão no início/fim
    public float HA, HB;        // altura livre até o teto
    public float Noise = 1f;    // irregularidade da borda
    public bool Flat;           // sem ondulações no chão (acampamento, túneis baixos)
    public WingDef Wing;        // câmara com minério em destaque
    public string Id;

    public Vector2 Center => (A + B) * 0.5f;
    public float Radius => Mathf.Max(RA, RB);
    public float Level => (LA + LB) * 0.5f;

    /// <summary>Distância com sinal até a borda (negativa dentro) e parâmetro t ao longo do segmento.</summary>
    public float Dist(Vector2 p, out float t)
    {
        var ab = B - A;
        float len2 = ab.LengthSquared();
        t = len2 < 1e-4f ? 0f : Mathf.Clamp((p - A).Dot(ab) / len2, 0f, 1f);
        var q = A + ab * t;
        return p.DistanceTo(q) - Mathf.Lerp(RA, RB, t);
    }
}

/// <summary>
/// Caverna orgânica de uma região, gerada de forma determinística num grid de 1 m:
/// caminho principal sinuoso, câmaras laterais em alturas diferentes ligadas por rampas,
/// câmaras com minério em destaque, câmaras secretas por túneis baixos (só agachado).
/// Produz o chão com relevo (F), o teto (C) e a distância às paredes (D).
/// </summary>
public sealed class CaveLayout
{
    public RegionDef Region;
    public float X0, Z0;          // canto (x mín, z mín) do grid
    public int Nx, Nz;
    public float[] F, C, D, L;    // chão, teto, distância com sinal (<0 = livre), nível base
    public bool[] Reach;          // alcançável a pé desde a entrada
    public readonly List<CaveFeature> Features = new();
    public readonly List<CaveFeature> Chambers = new();
    public readonly List<CaveFeature> Secrets = new();
    public readonly List<CaveFeature> Crawls = new();
    public readonly List<CavePit> Pits = new();
    /// <summary>Cordas fixas ligando áreas que ficariam isoladas (degraus altos demais).</summary>
    public readonly List<(Vector3 top, Vector3 bottom, Vector3 exit)> FixedRopes = new();
    public readonly List<Vector3> PathPoints = new();   // pontos do caminho principal (entrada → saída)
    public CaveFeature Camp, Heart, Lake, Giant;
    public Vector3 Entrance, Exit;
    public const float CrawlHeight = 1.45f;

    readonly FastNoiseLite _edge, _bump, _ceil;
    readonly Random _rng;

    public CaveLayout(RegionDef r, int seed)
    {
        Region = r;
        _rng = new Random(seed);
        _edge = new FastNoiseLite { Seed = seed, Frequency = 0.07f, FractalOctaves = 3, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
        _bump = new FastNoiseLite { Seed = seed + 1, Frequency = 0.09f, FractalOctaves = 2 };
        _ceil = new FastNoiseLite { Seed = seed + 2, Frequency = 0.16f, FractalOctaves = 2 };
        X0 = -r.HalfX; Z0 = r.CenterZ - r.HalfZ;
        Nx = (int)(r.HalfX * 2) + 1; Nz = (int)(r.HalfZ * 2) + 1;
        Plan();
        Rasterize();
        CarvePits();
        Flood();
        ConnectIsolated();
    }

    float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    // =========================== Planejamento ===========================
    void Plan()
    {
        var r = Region;
        bool first = r.Index == 0;
        bool hasNorth = r.Index + 1 < GameData.Regions.Count && !GameData.Regions[r.Index + 1].Deep && !r.Deep;
        float top = r.CenterZ + r.HalfZ, bottom = r.CenterZ - r.HalfZ;
        float baseH = r.Height;

        // nós do caminho principal
        var nodes = new List<(Vector2 p, float rad, float lvl, float h)>();
        if (first)
        {
            var campC = new Vector2(0, top - 18f);
            Camp = Add(new CaveFeature { Kind = CaveKind.Chamber, Id = "camp", Name = "Acampamento", A = campC, B = campC, RA = 15, RB = 15, LA = 0, LB = 0, HA = 12f, HB = 12f, Noise = 0.45f, Flat = true });
            Chambers.Add(Camp);
            nodes.Add((campC, 8f, 0f, 12f));
        }
        else
        {
            nodes.Add((new Vector2(0, top), 3.6f, 0f, 6.5f));
            nodes.Add((new Vector2(0, top - 12f), 9f, 0f, baseH * 0.7f));
        }
        // regiões maiores têm mais nós no caminho principal
        int mids = Mathf.Clamp((int)(r.HalfZ / 16f), 6, 11);
        float zA = nodes[^1].p.Y, zB = hasNorth ? bottom + 10f : bottom + 16f;
        // relevo forte: o caminho principal sobe e desce (rampas longas entre os nós)
        float[] lvls = { -4f, -2f, 0f, 2f, 4f, 6f, 8f };
        for (int i = 1; i <= mids; i++)
        {
            float z = Mathf.Lerp(zA, zB, i / (float)(mids + 1));
            float x = R(-1f, 1f) * r.HalfX * 0.45f;
            // salões grandes alternados com passagens mais estreitas
            bool hall = i % 2 == 1;
            nodes.Add((new Vector2(x, z), hall ? R(12f, 16f) : R(8f, 10f), lvls[_rng.Next(lvls.Length)], hall ? baseH + R(4f, 10f) : baseH + R(-3f, 2f)));
        }
        if (hasNorth)
        {
            nodes.Add((new Vector2(0, bottom + 12f), 9f, 0f, baseH * 0.7f));
            nodes.Add((new Vector2(0, bottom), 3.6f, 0f, 6.5f));
        }
        else nodes.Add((new Vector2(0, bottom + 22f), 16f, 0f, baseH + 8));

        // Núcleo: o Coração fica numa grande câmara no meio do caminho
        if (r.Id == "core")
        {
            int mid = nodes.Count / 2;
            var n = nodes[mid];
            nodes[mid] = (new Vector2(0, n.p.Y), 12f, 0f, baseH + 8f);
            var hc = new Vector2(0, n.p.Y);
            Heart = Add(new CaveFeature { Kind = CaveKind.Chamber, Id = "heart", Name = "Câmara do Coração", A = hc, B = hc, RA = 22, RB = 22, LA = 0, LB = 0, HA = baseH + 10, HB = baseH + 10, Noise = 0.6f, Flat = true });
            Chambers.Add(Heart);
        }

        for (int i = 0; i + 1 < nodes.Count; i++)
        {
            var a = nodes[i]; var b = nodes[i + 1];
            Add(new CaveFeature { Kind = CaveKind.Path, A = a.p, B = b.p, RA = a.rad, RB = b.rad, LA = a.lvl, LB = b.lvl, HA = a.h, HB = b.h, Noise = (a.rad < 4 || b.rad < 4) ? 0.25f : 1f });
        }
        foreach (var n in nodes) PathPoints.Add(new Vector3(n.p.X, n.lvl, n.p.Y));
        Entrance = first ? new Vector3(0, 0, Camp.A.Y + 6f) : new Vector3(0, 0, top - 1f);
        Exit = hasNorth ? new Vector3(0, 0, bottom + 1f) : PathPoints[^1];

        // Reservatório: grande câmara rebaixada com o lago
        if (r.Id == "abyss")
            Lake = TryChamber("lake", "Lago Abissal", 24f, 28f, -4f, baseH + 6f, null);

        // câmara colossal com a formação gigante da região
        if (r.GiantMaterialId != null)
        {
            Giant = TryChamber("giant", "Câmara Colossal", 17f, 21f, PickLevel(), baseH + 12f, null);
            if (Giant != null) Giant.Flat = true;
        }

        // câmaras com minério em destaque (as "galerias" antigas) e câmaras comuns
        foreach (var w in r.Wings) TryChamber(w.Id, w.Name, 14f, 19f, PickLevel(), baseH + 2f, w);
        float area = r.HalfX * r.HalfZ / (95f * 90f);
        int extra = (int)((10 + r.Index * 2) * area);
        for (int k = 0; k < extra; k++) TryChamber("c" + k, null, 8f, 18f, PickLevel(), baseH + R(-5f, 6f), null);

        // túneis em laço ligando câmaras próximas (vários caminhos, menos becos)
        int loops = 3 + (int)(area * 2.5f);
        for (int k = 0, tries = 0; k < loops && tries < 400; tries++)
        {
            var a = Chambers[_rng.Next(Chambers.Count)];
            var b = Chambers[_rng.Next(Chambers.Count)];
            if (a == b || a == Camp || b == Camp || a == Heart || b == Heart) continue;
            float d = a.A.DistanceTo(b.A) - a.RA - b.RA;
            if (d < 8f || d > 55f) continue;
            if (Mathf.Abs(a.Level - b.Level) > d * 0.4f) continue; // rampa de no máximo ~22°
            var dir = (b.A - a.A).Normalized();
            Add(new CaveFeature { Kind = CaveKind.Corridor, Id = "loop" + k, A = a.A + dir * a.RA * 0.6f, B = b.A - dir * b.RA * 0.6f, RA = R(3.2f, 4.5f), RB = R(3.2f, 4.5f), LA = a.Level, LB = b.Level, HA = R(6f, 9f), HB = R(6f, 9f), Noise = 1f });
            k++;
        }

        // poços: buracos fundos dentro de câmaras grandes
        int pits = 2 + r.Index / 2 + (int)area;
        for (int k = 0, tries = 0; k < pits && tries < 300; tries++)
        {
            var host = Chambers[_rng.Next(Chambers.Count)];
            if (host == Camp || host == Heart || host == Lake || host == Giant || host.Radius < 11f) continue;
            float pr = R(3.6f, 5f), depth = R(7f, 13f);
            float ang = R(0, Mathf.Tau);
            var c = host.A + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * R(0f, host.RA - pr - 4f);
            bool clash = false;
            foreach (var o in Pits) if (o.Center.DistanceTo(c) < o.Radius + pr + 6f) clash = true;
            if (clash) continue;
            Pits.Add(new CavePit { Host = host, Center = c, Radius = pr, Depth = depth });
            k++;
        }

        // câmaras secretas por túneis baixos (só agachado)
        int secrets = 3 + (int)(area * 0.6f);
        var hosts = new List<CaveFeature>(Chambers);
        for (int s = 0, tries = 0; s < secrets && tries < 120; tries++)
        {
            var host = hosts[_rng.Next(hosts.Count)];
            if (host == Camp || host == Heart || host.Kind == CaveKind.Secret) continue;
            float ang = R(0, Mathf.Tau);
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            float rs = R(5f, 6.5f);
            float len = R(9f, 13f);
            var edgeA = host.A + dir * (host.RA - 3.5f); // começa por dentro (a borda é irregular)
            var sc = host.A + dir * (host.RA + len + rs);
            if (!Inside(sc, rs + 4f) || Overlaps(sc, rs + 3f, host) || SegmentHits(edgeA, sc, host)) continue;
            // longe dos poços (a entrada do túnel não pode cair num buraco)
            bool nearPit = false;
            foreach (var pit in Pits)
                for (int q = 0; q <= 8; q++)
                    if (edgeA.Lerp(sc, q / 8f).DistanceTo(pit.Center) < pit.Radius + 5f) nearPit = true;
            if (nearPit) continue;
            var secret = Add(new CaveFeature { Kind = CaveKind.Secret, Id = $"secret_{r.Id}_{s}", Name = "Câmara secreta", A = sc, B = sc, RA = rs, RB = rs, LA = host.Level, LB = host.Level, HA = 4.5f, HB = 4.5f, Noise = 0.6f, Flat = true });
            Secrets.Add(secret);
            var crawl = Add(new CaveFeature { Kind = CaveKind.Crawl, A = edgeA, B = sc - dir * (rs - 2.5f), RA = 1.45f, RB = 1.45f, LA = host.Level, LB = host.Level, HA = CrawlHeight, HB = CrawlHeight, Noise = 0.08f, Flat = true });
            Crawls.Add(crawl);
            s++;
        }
    }

    /// <summary>Afunda o chão dos poços (paredes íngremes) e calcula onde fica a corda fixa.</summary>
    void CarvePits()
    {
        if (Pits.Count == 0) return;
        var candidates = new List<CavePit>(Pits);
        Pits.Clear();
        Flood();
        var baseReach = (bool[])Reach.Clone();
        foreach (var pit in candidates)
        {
            int r = Mathf.CeilToInt(pit.Radius + 2f);
            int ci = Mathf.RoundToInt(pit.Center.X - X0), cj = Mathf.RoundToInt(pit.Center.Y - Z0);
            var saved = new List<(int id, float f)>();
            for (int j = cj - r; j <= cj + r; j++)
                for (int i = ci - r; i <= ci + r; i++)
                {
                    if (i < 0 || j < 0 || i >= Nx || j >= Nz) continue;
                    int id = Idx(i, j);
                    if (D[id] >= -0.5f) continue;
                    var p = CellPos(i, j);
                    float d = p.DistanceTo(pit.Center) - pit.Radius + _edge.GetNoise2D(p.X * 2f, p.Y * 2f) * 0.8f;
                    float k = Mathf.SmoothStep(0.3f, -1.4f, d);
                    if (k <= 0f) continue;
                    saved.Add((id, F[id]));
                    float bottom = pit.Floor + _bump.GetNoise2D(p.X, p.Y) * 0.3f;
                    F[id] = Mathf.Lerp(F[id], bottom, k);
                }
            // corda no lado voltado para o centro da câmara
            var toHost = pit.Host.A - pit.Center;
            var dir = toHost.LengthSquared() > 0.5f ? toHost.Normalized() : Vector2.Right;
            var top2 = pit.Center + dir * (pit.Radius + 0.35f);
            var bot2 = pit.Center + dir * (pit.Radius - 1.8f);
            var exit2 = pit.Center + dir * (pit.Radius + 1.9f);
            pit.RopeTop = new Vector3(top2.X, Ground(top2.X, top2.Y), top2.Y);
            pit.RopeBottom = new Vector3(bot2.X, Ground(bot2.X, bot2.Y), bot2.Y);
            pit.TopExit = new Vector3(exit2.X, Ground(exit2.X, exit2.Y), exit2.Y);
            Pits.Add(pit);
            Flood();
            // o poço não pode isolar nada: tudo o que era alcançável continua (fora do próprio buraco)
            bool cuts = !Reachable(pit.RopeTop.X, pit.RopeTop.Z) || !Reachable(pit.TopExit.X, pit.TopExit.Z) || !Reachable(pit.RopeBottom.X, pit.RopeBottom.Z);
            for (int id = 0; id < Reach.Length && !cuts; id++)
                if (baseReach[id] && !Reach[id])
                {
                    var p = CellPos(id % Nx, id / Nx);
                    if (p.DistanceTo(pit.Center) > pit.Radius + 2.5f) cuts = true;
                }
            if (cuts)
            {
                foreach (var (id, f) in saved) F[id] = f;
                Pits.Remove(pit);
            }
        }
    }

    /// <summary>
    /// Garante que nada fica sem volta: cada área aberta isolada (câmara num degrau alto demais)
    /// ganha uma corda fixa no ponto de menor desnível até a parte alcançável.
    /// </summary>
    void ConnectIsolated()
    {
        bool OpenCell(int id) => D[id] < -0.3f && C[id] - F[id] >= 1.15f && (Blocked == null || !Blocked[id]);
        int[] di = { 1, -1, 0, 0 }, dj = { 0, 0, 1, -1 };
        for (int round = 0; round < 40; round++)
        {
            // célula isolada mais "importante": centro de câmara não alcançável, senão qualquer célula grande isolada
            int seed = -1;
            foreach (var c in Chambers.Concat(Secrets))
            {
                int id = NearestOpen(c.A.X, c.A.Y);
                if (id >= 0 && !Reach[id] && OpenCell(id)) { seed = id; break; }
            }
            if (seed < 0) return;
            if (round == 39) GD.PushWarning($"[Caverna] {Region.Id}: área isolada sem conexão");
            // componente aberto (ignorando degraus) a partir da semente, sem atravessar áreas já alcançáveis
            var comp = new HashSet<int> { seed };
            var q = new Queue<int>();
            q.Enqueue(seed);
            int bestA = -1, bestB = -1; float bestCost = float.MaxValue;
            while (q.Count > 0 && comp.Count < 60000)
            {
                int id = q.Dequeue();
                int i = id % Nx, j = id / Nx;
                for (int k = 0; k < 4; k++)
                {
                    int ni = i + di[k], nj = j + dj[k];
                    if (ni < 1 || nj < 1 || ni >= Nx - 1 || nj >= Nz - 1) continue;
                    int nid = Idx(ni, nj);
                    if (!OpenCell(nid)) continue;
                    if (Reach[nid])
                    {
                        if (D[nid] >= -0.8f || D[id] >= -0.8f) continue;
                        // fronteira com a área alcançável: candidata a corda (menor desnível, longe das paredes)
                        float cost = Mathf.Abs(F[nid] - F[id]) + Mathf.Max(0f, D[id] + 1.5f) * 2f + Mathf.Max(0f, D[nid] + 1.5f) * 2f;
                        if (cost < bestCost) { bestCost = cost; bestA = id; bestB = nid; }
                        continue;
                    }
                    if (comp.Add(nid)) q.Enqueue(nid);
                }
            }
            if (bestA < 0)
            {
                // bolsão fechado: escava um túnel até a célula alcançável mais próxima (a próxima rodada põe corda se precisar)
                int target = -1; float bd = float.MaxValue;
                var sp = CellPos(seed % Nx, seed / Nx);
                for (int id = 0; id < Reach.Length; id++)
                    if (Reach[id])
                    {
                        float d = CellPos(id % Nx, id / Nx).DistanceSquaredTo(sp);
                        if (d < bd) { bd = d; target = id; }
                    }
                if (target < 0) return;
                CarveTunnel(seed, target);
                Flood();
                continue;
            }
            var pa = CellPos(bestA % Nx, bestA / Nx); var pb = CellPos(bestB % Nx, bestB / Nx);
            bool aHigh = F[bestA] > F[bestB];
            var hi = aHigh ? pa : pb; var lo = aHigh ? pb : pa;
            var dir = (lo - hi).Normalized();
            if (dir.LengthSquared() < 0.5f) dir = Vector2.Right;
            var top2 = hi; var bot2 = lo; var exit2 = hi - dir * 1.2f;
            if (WallDist(exit2.X, exit2.Y) > -0.7f) exit2 = hi;
            FixedRopes.Add((new Vector3(top2.X, Ground(top2.X, top2.Y), top2.Y), new Vector3(bot2.X, Ground(bot2.X, bot2.Y), bot2.Y), new Vector3(exit2.X, Ground(exit2.X, exit2.Y), exit2.Y)));
            Flood();
        }
    }

    /// <summary>Escava um túnel reto (2,8 m de largura, 3 m de altura) entre duas células, interpolando o chão.</summary>
    void CarveTunnel(int fromId, int toId)
    {
        var a = CellPos(fromId % Nx, fromId / Nx); var b = CellPos(toId % Nx, toId / Nx);
        float fa = F[fromId], fb = F[toId];
        float len = a.DistanceTo(b);
        for (float t = 0; t <= len; t += 0.5f)
        {
            var c = a.Lerp(b, len < 0.01f ? 0 : t / len);
            float fl = Mathf.Lerp(fa, fb, len < 0.01f ? 0 : t / len);
            for (int j = Mathf.FloorToInt(c.Y - Z0 - 2); j <= Mathf.CeilToInt(c.Y - Z0 + 2); j++)
                for (int i = Mathf.FloorToInt(c.X - X0 - 2); i <= Mathf.CeilToInt(c.X - X0 + 2); i++)
                {
                    if (i < 1 || j < 1 || i >= Nx - 1 || j >= Nz - 1) continue;
                    float d = CellPos(i, j).DistanceTo(c);
                    if (d > 1.9f) continue;
                    int id = Idx(i, j);
                    bool wasOpen = D[id] < -0.3f;
                    D[id] = Mathf.Min(D[id], d - 1.9f);
                    if (!wasOpen) { F[id] = fl; C[id] = Mathf.Max(C[id], fl + 3f); }
                    else C[id] = Mathf.Max(C[id], F[id] + 2.2f);
                }
        }
    }

    /// <summary>Poço que contém o ponto (no fundo).</summary>
    public CavePit PitAt(Vector3 p)
    {
        foreach (var pit in Pits)
            if (new Vector2(p.X, p.Z).DistanceTo(pit.Center) < pit.Radius && p.Y < pit.Floor + 2f) return pit;
        return null;
    }

    float PickLevel()
    {
        float[] levels = { -6f, -3f, 0f, 3f, 6f, 10f, 14f };
        return levels[_rng.Next(levels.Length)];
    }

    CaveFeature Add(CaveFeature f) { Features.Add(f); return f; }

    bool Inside(Vector2 c, float margin) =>
        Mathf.Abs(c.X) < Region.HalfX - margin && Mathf.Abs(c.Y - Region.CenterZ) < Region.HalfZ - margin;

    bool Overlaps(Vector2 c, float rad, CaveFeature ignore = null)
    {
        foreach (var f in Features)
        {
            if (f == ignore) continue;
            float d = f.Dist(c, out _);
            if (d < rad) return true;
        }
        return false;
    }

    /// <summary>O segmento atravessa alguma feição (além do hospedeiro)? Evita túneis secretos cruzando salas.</summary>
    bool SegmentHits(Vector2 a, Vector2 b, CaveFeature host)
    {
        for (int i = 1; i < 10; i++)
        {
            var p = a.Lerp(b, i / 10f);
            foreach (var f in Features)
                if (f != host && f.Dist(p, out _) < 2.5f) return true;
        }
        return false;
    }

    /// <summary>Câmara lateral ligada ao caminho por uma rampa (corredor).</summary>
    CaveFeature TryChamber(string id, string name, float rMin, float rMax, float level, float height, WingDef wing)
    {
        var paths = Features.FindAll(f => f.Kind == CaveKind.Path && f.Radius > 5f);
        for (int tries = 0; tries < 300; tries++)
        {
            if (tries == 150) { rMin *= 0.75f; rMax *= 0.8f; } // aperta para caber
            var seg = paths[_rng.Next(paths.Count)];
            float t = R(0.15f, 0.85f);
            var p = seg.A.Lerp(seg.B, t);
            float pr = Mathf.Lerp(seg.RA, seg.RB, t), pl = Mathf.Lerp(seg.LA, seg.LB, t);
            var along = (seg.B - seg.A).Normalized();
            var side = new Vector2(-along.Y, along.X) * (_rng.Next(2) == 0 ? -1 : 1);
            var dir = side.Rotated(R(-0.6f, 0.6f));
            float rad = R(rMin, rMax);
            float corridor = R(10f, 26f);
            var c = p + dir * (pr + corridor + rad);
            if (!Inside(c, rad + 4f) || Overlaps(c, rad + 4f)) continue;
            // rampa com no máximo ~25° de inclinação
            float maxDiff = (corridor + 2f) * 0.45f;
            float lv = Mathf.Clamp(level, pl - maxDiff, pl + maxDiff);
            var ch = Add(new CaveFeature { Kind = wing != null ? CaveKind.Chamber : CaveKind.Chamber, Id = id, Name = name, A = c, B = c, RA = rad, RB = rad, LA = lv, LB = lv, HA = Mathf.Max(6f, height), HB = Mathf.Max(6f, height), Wing = wing });
            Chambers.Add(ch);
            Add(new CaveFeature { Kind = CaveKind.Corridor, A = p + dir * (pr * 0.5f), B = c - dir * (rad * 0.6f), RA = R(4f, 5.5f), RB = R(4.5f, 6f), LA = pl, LB = lv, HA = R(8f, 12f), HB = R(9f, 13f), Noise = 0.9f });
            return ch;
        }
        return null;
    }

    // =========================== Grid ===========================
    public int Idx(int i, int j) => j * Nx + i;
    public Vector2 CellPos(int i, int j) => new(X0 + i, Z0 + j);

    void Rasterize()
    {
        int n = Nx * Nz;
        F = new float[n]; C = new float[n]; D = new float[n]; L = new float[n];
        var dist = new float[Features.Count];
        var ts = new float[Features.Count];
        for (int j = 0; j < Nz; j++)
            for (int i = 0; i < Nx; i++)
            {
                var p = CellPos(i, j);
                float en = _edge.GetNoise2D(p.X, p.Y);
                float dmin = float.MaxValue;
                for (int k = 0; k < Features.Count; k++)
                {
                    var f = Features[k];
                    float d = f.Dist(p, out ts[k]);
                    d += en * 2.4f * f.Noise;
                    dist[k] = d;
                    if (d < dmin) dmin = d;
                }
                // mistura suave dos níveis e alturas das feições próximas
                float ws = 0, lv = 0, hh = 0; bool flat = false; bool crawl = false;
                for (int k = 0; k < Features.Count; k++)
                {
                    float w = Mathf.Exp(-1.3f * (dist[k] - dmin));
                    if (w < 0.002f) continue;
                    var f = Features[k];
                    ws += w;
                    lv += w * Mathf.Lerp(f.LA, f.LB, ts[k]);
                    hh += w * Mathf.Lerp(f.HA, f.HB, ts[k]);
                    if (w > 0.5f && f.Flat) flat = true;
                    if (w > 0.6f && f.Kind == CaveKind.Crawl && dist[k] < 0) crawl = true;
                }
                lv /= ws; hh /= ws;
                int id = Idx(i, j);
                // bordas do grid sempre são parede (exceto as bocas dos túneis)
                int edge = Math.Min(Math.Min(i, Nx - 1 - i), Math.Min(j, Nz - 1 - j));
                if (edge < 2 && Mathf.Abs(p.X) > 3.6f) dmin = Mathf.Max(dmin, 3.5f - edge);
                D[id] = dmin;
                L[id] = lv;
                float ceilTop = lv + hh;
                if (dmin < 0)
                {
                    float bump = flat ? 0f : _bump.GetNoise2D(p.X, p.Y) * 0.55f;
                    float lip = Mathf.SmoothStep(-1.4f, 0f, dmin) * 0.45f;
                    F[id] = lv + bump + lip;
                    float stal = crawl ? 0f : Mathf.Max(0f, _ceil.GetNoise2D(p.X, p.Y)) * Mathf.Min(2.2f, hh * 0.18f);
                    C[id] = crawl ? lv + CrawlHeight : Mathf.Max(F[id] + (hh < 3f ? CrawlHeight : 2.8f), ceilTop - stal);
                }
                else
                {
                    float rise = Mathf.SmoothStep(0f, 2.8f, dmin);
                    float rough = _bump.GetNoise2D(p.X * 2.3f, p.Y * 2.3f) * Mathf.Min(dmin, 1.5f) * 0.7f;
                    F[id] = lv + 0.45f + (ceilTop - lv - 0.45f + 0.6f) * rise + rough;
                    C[id] = Mathf.Max(F[id], ceilTop);
                    if (dmin > 2.8f) { F[id] = ceilTop + 0.6f; C[id] = F[id]; }
                }
            }
    }

    /// <summary>Busca em largura a partir da entrada: células por onde se anda (inclinação, altura livre agachado).</summary>
    /// <summary>Células ocupadas por obstáculos sólidos do cenário (pedras, estalagmites, formações).</summary>
    public bool[] Blocked;

    /// <summary>Marca um obstáculo circular no grid (o caminho a pé e os pontos de mineração passam a evitá-lo).</summary>
    public void Block(Vector3 p, float radius)
    {
        Blocked ??= new bool[Nx * Nz];
        int r = Mathf.CeilToInt(radius);
        int ci = Mathf.RoundToInt(p.X - X0), cj = Mathf.RoundToInt(p.Z - Z0);
        for (int j = cj - r; j <= cj + r; j++)
            for (int i = ci - r; i <= ci + r; i++)
            {
                if (i < 0 || j < 0 || i >= Nx || j >= Nz) continue;
                float dx = X0 + i - p.X, dz = Z0 + j - p.Z;
                if (dx * dx + dz * dz <= radius * radius) Blocked[Idx(i, j)] = true;
            }
    }

    /// <summary>Refaz a área alcançável depois de colocar obstáculos.</summary>
    public void Reflood() => Flood();

    /// <summary>Perto da boca/percurso de um túnel secreto? (lá não pode haver obstáculos).</summary>
    public bool NearCrawl(float x, float z, float radius)
    {
        var p = new Vector2(x, z);
        foreach (var c in Crawls)
        {
            var a = c.A; var b = c.B;
            var ab = b - a;
            float t = Mathf.Clamp((p - a).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-4f), 0, 1);
            if (p.DistanceTo(a + ab * t) < radius + 2f) return true;
        }
        return false;
    }

    /// <summary>Distância andando desde a entrada (células; corda conta como trecho longo). -1 = inalcançável.</summary>
    public float[] PathDist;
    public float MaxPathDist = 1;

    /// <summary>
    /// Quão fundo na exploração da região está o ponto: 0 na entrada, 1 no ponto mais distante que dá para alcançar.
    /// Pontos fora do alcance contam como o mais fundo (só se chega com corda/exploração).
    /// </summary>
    public float Depth(float x, float z)
    {
        if (PathDist == null || !InGrid(x, z)) return 0.5f;
        int id = NearestOpen(x, z);
        if (id < 0 || PathDist[id] < 0) return 1f;
        return Mathf.Clamp(PathDist[id] / Mathf.Max(1f, MaxPathDist), 0f, 1f);
    }

    void Flood()
    {
        Reach = new bool[Nx * Nz];
        PathDist = new float[Nx * Nz];
        System.Array.Fill(PathDist, -1f);
        var q = new Queue<int>();
        int start = NearestOpen(Entrance.X, Entrance.Z);
        if (start < 0) return;
        Reach[start] = true; PathDist[start] = 0; q.Enqueue(start);
        int[] di = { 1, -1, 0, 0 }, dj = { 0, 0, 1, -1 };
        // cordas fixas (poços) e cordas do jogador ligam o alto ao fundo: viram arestas do grafo
        var linked = new HashSet<int>();
        while (true)
        {
            FloodQueue(q, di, dj);
            bool more = false;
            foreach (var (top, bottom) in RopeLinks())
            {
                int t = NearestOpen(top.X, top.Z), b = NearestOpen(bottom.X, bottom.Z);
                if (t < 0 || b < 0) continue;
                foreach (var (from, to) in new[] { (t, b), (b, t) })
                    if (Reach[from] && !Reach[to] && D[to] < -0.3f) { Reach[to] = true; PathDist[to] = PathDist[from] + 12f; q.Enqueue(to); more = true; }
            }
            if (!more) break;
        }
        MaxPathDist = 1;
        foreach (var d in PathDist) if (d > MaxPathDist) MaxPathDist = d;
    }

    /// <summary>Cordas extras (ex.: do jogador) consideradas no cálculo da área alcançável.</summary>
    public readonly List<(Vector3 top, Vector3 bottom)> ExtraRopes = new();

    IEnumerable<(Vector3, Vector3)> RopeLinks()
    {
        foreach (var p in Pits) yield return (p.RopeTop, p.RopeBottom);
        foreach (var f in FixedRopes) yield return (f.top, f.bottom);
        foreach (var r in ExtraRopes) yield return r;
    }

    void FloodQueue(Queue<int> q, int[] di, int[] dj)
    {
        while (q.Count > 0)
        {
            int id = q.Dequeue();
            int i = id % Nx, j = id / Nx;
            for (int k = 0; k < 4; k++)
            {
                int ni = i + di[k], nj = j + dj[k];
                if (ni < 0 || nj < 0 || ni >= Nx || nj >= Nz) continue;
                int nid = Idx(ni, nj);
                if (Reach[nid] || D[nid] > -0.3f || (Blocked != null && Blocked[nid])) continue;
                if (Mathf.Abs(F[nid] - F[id]) > 0.85f) continue;     // ~40° (o jogador sobe até 50°)
                if (C[nid] - F[nid] < 1.15f) continue;                // nem agachado passa
                Reach[nid] = true;
                PathDist[nid] = PathDist[id] + 1f;
                q.Enqueue(nid);
            }
        }
    }

    // =========================== Consultas ===========================
    public bool InGrid(float x, float z) => x >= X0 && z >= Z0 && x <= X0 + Nx - 1 && z <= Z0 + Nz - 1;

    float Sample(float[] a, float x, float z)
    {
        float fx = Mathf.Clamp(x - X0, 0, Nx - 1.001f), fz = Mathf.Clamp(z - Z0, 0, Nz - 1.001f);
        int i = (int)fx, j = (int)fz;
        float tx = fx - i, tz = fz - j;
        float a0 = Mathf.Lerp(a[Idx(i, j)], a[Idx(i + 1, j)], tx);
        float a1 = Mathf.Lerp(a[Idx(i, j + 1)], a[Idx(i + 1, j + 1)], tx);
        return Mathf.Lerp(a0, a1, tz);
    }

    /// <summary>Altura do chão (interpolada) — a mesma superfície da colisão.</summary>
    public float Ground(float x, float z) => Sample(F, x, z);
    public float Ceiling(float x, float z) => Sample(C, x, z);
    public float WallDist(float x, float z) => Sample(D, x, z);
    public Vector3 OnGround(float x, float z, float lift = 0f) => new(x, Ground(x, z) + lift, z);

    public bool Reachable(float x, float z)
    {
        if (!InGrid(x, z)) return false;
        int i = Mathf.RoundToInt(x - X0), j = Mathf.RoundToInt(z - Z0);
        return Reach[Idx(i, j)];
    }

    public int NearestOpen(float x, float z)
    {
        int ci = Mathf.Clamp(Mathf.RoundToInt(x - X0), 0, Nx - 1), cj = Mathf.Clamp(Mathf.RoundToInt(z - Z0), 0, Nz - 1);
        for (int rad = 0; rad < 12; rad++)
            for (int j = cj - rad; j <= cj + rad; j++)
                for (int i = ci - rad; i <= ci + rad; i++)
                {
                    if (i < 0 || j < 0 || i >= Nx || j >= Nz) continue;
                    if (D[Idx(i, j)] < -0.6f) return Idx(i, j);
                }
        return -1;
    }

    /// <summary>Normal da superfície do chão/parede (gradiente do mapa de altura).</summary>
    public Vector3 Normal(float x, float z)
    {
        float dx = Ground(x + 0.5f, z) - Ground(x - 0.5f, z);
        float dz = Ground(x, z + 0.5f) - Ground(x, z - 0.5f);
        return new Vector3(-dx, 1f, -dz).Normalized();
    }

    /// <summary>Direção horizontal que se afasta da parede mais próxima.</summary>
    public Vector3 Inward(float x, float z)
    {
        float dx = WallDist(x + 0.7f, z) - WallDist(x - 0.7f, z);
        float dz = WallDist(x, z + 0.7f) - WallDist(x, z - 0.7f);
        var v = new Vector3(-dx, 0, -dz);
        return v.LengthSquared() < 1e-5f ? Vector3.Forward : v.Normalized();
    }

    /// <summary>Feição dominante num ponto (a mais "por dentro").</summary>
    public CaveFeature FeatureAt(float x, float z)
    {
        CaveFeature best = null; float bd = 0f;
        var p = new Vector2(x, z);
        foreach (var f in Features)
        {
            float d = f.Dist(p, out _);
            if (d < bd) { bd = d; best = f; }
        }
        return best;
    }

    public CaveFeature SecretAt(Vector3 p)
    {
        foreach (var s in Secrets) if (s.Dist(new Vector2(p.X, p.Z), out _) < 0) return s;
        return null;
    }

    /// <summary>Ponto de chão livre, alcançável, com folga das paredes e pouca inclinação.</summary>
    public bool RandomFloor(Random rng, float clearance, float maxSlope, out Vector3 p)
    {
        for (int tries = 0; tries < 60; tries++)
        {
            float x = X0 + 2 + (float)rng.NextDouble() * (Nx - 4);
            float z = Z0 + 2 + (float)rng.NextDouble() * (Nz - 4);
            if (WallDist(x, z) > -clearance || !Reachable(x, z)) continue;
            if (1f - Normal(x, z).Y > maxSlope) continue;
            p = OnGround(x, z);
            return true;
        }
        p = default;
        return false;
    }

    /// <summary>Ponto de chão junto à parede (para rochas, plantas, cachoeiras).</summary>
    public bool RandomNearWall(Random rng, out Vector3 p, float minD = -2.5f, float maxD = -0.6f, bool reachableOnly = false)
    {
        for (int tries = 0; tries < 80; tries++)
        {
            float x = X0 + 2 + (float)rng.NextDouble() * (Nx - 4);
            float z = Z0 + 2 + (float)rng.NextDouble() * (Nz - 4);
            float d = WallDist(x, z);
            if (d < minD || d > maxD) continue;
            if (reachableOnly && !Reachable(x, z)) continue;
            p = OnGround(x, z);
            return true;
        }
        p = default;
        return false;
    }

    /// <summary>
    /// Depósito na parede: escolhe um ponto de apoio alcançável junto à parede e sobe pela rocha
    /// até a altura pedida. Devolve o ponto na superfície, a normal e o ponto onde o jogador fica.
    /// </summary>
    public bool RandomWallSpot(Random rng, float minH, float maxH, out Vector3 spot, out Vector3 normal, out Vector3 stand)
    {
        for (int tries = 0; tries < 80; tries++)
        {
            if (!RandomNearWall(rng, out stand, -2.2f, -0.9f, true)) break;
            var outDir = -Inward(stand.X, stand.Z);
            float targetY = stand.Y + minH + (float)rng.NextDouble() * (maxH - minH);
            Vector3? hit = null;
            for (float s = 0.3f; s < 3.2f; s += 0.15f)
            {
                var q = stand + outDir * s;
                if (Ground(q.X, q.Z) >= targetY) { hit = new Vector3(q.X, targetY, q.Z); break; }
            }
            if (hit == null) continue;
            var h = hit.Value;
            if (Ceiling(h.X, h.Z) < h.Y + 0.5f && WallDist(h.X, h.Z) < 0) continue;
            // a mira precisa alcançar: olho (1,55 m) até o depósito
            var eye = stand + Vector3.Up * 1.55f;
            if (eye.DistanceTo(h) > 3.1f) continue;
            var n = Normal(h.X, h.Z);
            n = new Vector3(n.X, Mathf.Max(n.Y, 0.15f), n.Z).Normalized();
            spot = h; normal = n;
            return true;
        }
        spot = normal = stand = default;
        return false;
    }

    /// <summary>
    /// Caminho a pé (A* no grid de 1 m, 8 vizinhos, só células alcançáveis, degraus ≤ 0,85 m,
    /// teto ≥ 1,3 m). Devolve pontos no chão, ou null se não houver caminho.
    /// </summary>
    public List<Vector3> FindPath(Vector3 from, Vector3 to, int maxExpand = 9000)
    {
        int s0 = NearestReach(from.X, from.Z), g0 = NearestReach(to.X, to.Z);
        if (s0 < 0 || g0 < 0) return null;
        var open = new PriorityQueue<int, float>();
        var came = new Dictionary<int, int>();
        var cost = new Dictionary<int, float> { [s0] = 0 };
        int gi = g0 % Nx, gj = g0 / Nx;
        open.Enqueue(s0, 0);
        int[] di = { 1, -1, 0, 0, 1, 1, -1, -1 }, dj = { 0, 0, 1, -1, 1, -1, 1, -1 };
        int expanded = 0;
        while (open.Count > 0 && expanded++ < maxExpand)
        {
            int cur = open.Dequeue();
            if (cur == g0) break;
            int ci = cur % Nx, cj = cur / Nx;
            for (int k = 0; k < 8; k++)
            {
                int ni = ci + di[k], nj = cj + dj[k];
                if (ni < 0 || nj < 0 || ni >= Nx || nj >= Nz) continue;
                int nid = Idx(ni, nj);
                if (!Reach[nid] || Mathf.Abs(F[nid] - F[cur]) > 0.85f || C[nid] - F[nid] < 1.3f) continue;
                if (k >= 4 && (!Reach[Idx(ci + di[k], cj)] || !Reach[Idx(ci, cj + dj[k])])) continue; // não corta quinas
                float step = k < 4 ? 1f : 1.414f;
                step += Mathf.Max(0f, -0.9f - D[nid]) < 0.01f ? 1.5f : 0f; // prefere longe das paredes
                float nc = cost[cur] + step;
                if (cost.TryGetValue(nid, out float old) && old <= nc) continue;
                cost[nid] = nc;
                came[nid] = cur;
                float h = Mathf.Sqrt((ni - gi) * (ni - gi) + (nj - gj) * (nj - gj));
                open.Enqueue(nid, nc + h);
            }
        }
        if (!came.ContainsKey(g0) && g0 != s0) return null;
        var cells = new List<int>();
        for (int c = g0; c != s0; c = came[c]) cells.Add(c);
        cells.Reverse();
        var path = new List<Vector3>();
        for (int k = 0; k < cells.Count; k++)
            if (k % 2 == 1 || k == cells.Count - 1)
            {
                int c = cells[k];
                path.Add(new Vector3(X0 + c % Nx, F[c], Z0 + c / Nx));
            }
        return path;
    }

    /// <summary>
    /// Caminho para a esteira: só por células alcançáveis, longe das paredes (folga), sem obstáculos
    /// (blocked: células extras ocupadas, ex.: depósitos e construções), com degraus suaves.
    /// Devolve um ponto por célula (o chamador simplifica com linha de visão).
    /// </summary>
    public List<Vector3> FindClearPath(Vector3 from, Vector3 to, System.Func<int, bool> blocked, float clearance = 1.1f, int maxExpand = 150000)
    {
        int s0 = NearestReach(from.X, from.Z), g0 = NearestReach(to.X, to.Z);
        if (s0 < 0 || g0 < 0) return null;
        bool Free(int id) => Reach[id] && (blocked == null || !blocked(id));
        var open = new PriorityQueue<int, float>();
        var came = new Dictionary<int, int>();
        var cost = new Dictionary<int, float> { [s0] = 0 };
        int gi = g0 % Nx, gj = g0 / Nx;
        open.Enqueue(s0, 0);
        int[] di = { 1, -1, 0, 0, 1, 1, -1, -1 }, dj = { 0, 0, 1, -1, 1, -1, 1, -1 };
        int expanded = 0;
        while (open.Count > 0 && expanded++ < maxExpand)
        {
            int cur = open.Dequeue();
            if (cur == g0) break;
            int ci = cur % Nx, cj = cur / Nx;
            for (int k = 0; k < 8; k++)
            {
                int ni = ci + di[k], nj = cj + dj[k];
                if (ni < 1 || nj < 1 || ni >= Nx - 1 || nj >= Nz - 1) continue;
                int nid = Idx(ni, nj);
                bool nearEnd = nid == g0 || cur == s0;
                if (!Free(nid) && !nearEnd) continue;
                if (Mathf.Abs(F[nid] - F[cur]) > 0.5f) continue;              // esteira não sobe degrau
                if (C[nid] - F[nid] < 2.2f) continue;
                // folga da parede (perto do começo/fim pode encostar)
                float d = D[nid];
                if (d > -clearance && !nearEnd && cost[cur] > 3f) continue;
                if (k >= 4 && (!Free(Idx(ci + di[k], cj)) || !Free(Idx(ci, cj + dj[k])))) continue;
                float step = (k < 4 ? 1f : 1.414f) + Mathf.Max(0f, d + 2.5f) * 0.8f; // prefere o meio da passagem
                float nc = cost[cur] + step;
                if (cost.TryGetValue(nid, out float old) && old <= nc) continue;
                cost[nid] = nc;
                came[nid] = cur;
                float h = Mathf.Sqrt((ni - gi) * (ni - gi) + (nj - gj) * (nj - gj));
                open.Enqueue(nid, nc + h);
            }
        }
        if (!came.ContainsKey(g0) && g0 != s0) return null;
        var cells = new List<int>();
        for (int c = g0; c != s0; c = came[c]) cells.Add(c);
        cells.Add(s0);
        cells.Reverse();
        var path = new List<Vector3>();
        foreach (int c in cells) path.Add(new Vector3(X0 + c % Nx, F[c], Z0 + c / Nx));
        return path;
    }

    public int NearestReach(float x, float z)
    {
        int ci = Mathf.Clamp(Mathf.RoundToInt(x - X0), 0, Nx - 1), cj = Mathf.Clamp(Mathf.RoundToInt(z - Z0), 0, Nz - 1);
        for (int rad = 0; rad < 6; rad++)
            for (int j = cj - rad; j <= cj + rad; j++)
                for (int i = ci - rad; i <= ci + rad; i++)
                {
                    if (i < 0 || j < 0 || i >= Nx || j >= Nz) continue;
                    if (Reach[Idx(i, j)]) return Idx(i, j);
                }
        return -1;
    }

    /// <summary>Fração de células livres alcançáveis (diagnóstico/testes).</summary>
    public float ReachableArea()
    {
        int n = 0;
        foreach (var b in Reach) if (b) n++;
        return n;
    }
}
