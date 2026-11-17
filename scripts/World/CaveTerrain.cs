using System.Collections.Generic;
using CrystalExcavator.Data;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Malhas e colisão da caverna de uma região, a partir do CaveLayout:
/// chão+paredes (mapa de altura F) e teto (C), divididos em blocos de 32 m
/// (recorte por visão e luzes só afetam blocos próximos). Colisão: HeightMapShape3D
/// do chão e caixas sob o teto dos túneis baixos.
/// É uma subclasse de Node3D, então o StaticBatcher não a mescla.
/// </summary>
public partial class CaveTerrain : Node3D
{
    public const int Chunk = 32;
    static Texture2D _noiseTex;
    static Shader _shader;

    public static Texture2D NoiseTexture
    {
        get
        {
            if (_noiseTex != null) return _noiseTex;
            var n = new FastNoiseLite { Seed = 7, Frequency = 0.02f, FractalOctaves = 4, NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth };
            var img = n.GetSeamlessImage(256, 256);
            img.GenerateMipmaps();
            _noiseTex = ImageTexture.CreateFromImage(img);
            return _noiseTex;
        }
    }

    public static void ClearCaches() { _noiseTex = null; _shader = null; }

    public void Build(CaveLayout L, RegionDef r)
    {
        _shader ??= GD.Load<Shader>("res://shaders/cave.gdshader");
        var floorMat = new ShaderMaterial { Shader = _shader };
        floorMat.SetShaderParameter("rock_color", r.RockColor);
        floorMat.SetShaderParameter("floor_color", r.RockColor.Darkened(0.3f).Lerp(new Color(0.25f, 0.22f, 0.2f), 0.2f));
        floorMat.SetShaderParameter("accent", r.AccentColor);
        floorMat.SetShaderParameter("noise_tex", NoiseTexture);
        var ceilMat = (ShaderMaterial)floorMat.Duplicate();
        ceilMat.SetShaderParameter("rock_color", r.RockColor.Darkened(0.35f));
        ceilMat.SetShaderParameter("speckle", 0.35f);

        for (int cj = 0; cj < L.Nz - 1; cj += Chunk)
            for (int ci = 0; ci < L.Nx - 1; ci += Chunk)
            {
                int i1 = Mathf.Min(ci + Chunk, L.Nx - 1), j1 = Mathf.Min(cj + Chunk, L.Nz - 1);
                var floor = Surface(L, L.F, ci, cj, i1, j1, false);
                if (floor != null) AddChild(new MeshInstance3D { Name = $"Floor_{ci}_{cj}", Mesh = floor, MaterialOverride = floorMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                var ceil = Surface(L, L.C, ci, cj, i1, j1, true);
                if (ceil != null) AddChild(new MeshInstance3D { Name = $"Ceil_{ci}_{cj}", Mesh = ceil, MaterialOverride = ceilMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            }

        // colisão do chão e das paredes: um mapa de altura por região
        var hm = new HeightMapShape3D { MapWidth = L.Nx, MapDepth = L.Nz, MapData = L.F };
        var body = new StaticBody3D { Name = "TerrainBody", CollisionLayer = PlayerController.WorldLayer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = hm, Position = new Vector3(L.X0 + (L.Nx - 1) * 0.5f, 0, L.Z0 + (L.Nz - 1) * 0.5f) });
        AddChild(body);

        // teto baixo dos túneis secretos: só passa agachado
        foreach (var c in L.Crawls)
        {
            // a caixa cobre só o trecho em que o teto é realmente baixo (não invade as câmaras)
            var a = new Vector3(c.A.X, 0, c.A.Y);
            var b = new Vector3(c.B.X, 0, c.B.Y);
            float total = a.DistanceTo(b);
            float t0 = -1, t1 = -1;
            for (float t = 0; t <= total; t += 0.25f)
            {
                var q = a.Lerp(b, t / total);
                if (L.Ceiling(q.X, q.Z) - L.Ground(q.X, q.Z) < 2.0f) { if (t0 < 0) t0 = t; t1 = t; }
            }
            if (t0 < 0) continue;
            var dir = (b - a).Normalized();
            var p0 = a + dir * t0; var p1 = a + dir * t1;
            var mid = (p0 + p1) * 0.5f;
            mid.Y = c.LA + CaveLayout.CrawlHeight + 0.75f;
            var box = new StaticBody3D { CollisionLayer = PlayerController.WorldLayer, CollisionMask = 0 };
            box.Transform = new Transform3D(Basis.LookingAt(dir, Vector3.Up), mid);
            box.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(c.RA * 2 + 3f, 1.5f, p0.DistanceTo(p1) + 0.5f) } });
            AddChild(box);
        }
    }

    /// <summary>Superfície de um bloco do grid. Pula quadrados escondidos dentro da rocha.</summary>
    static ArrayMesh Surface(CaveLayout L, float[] h, int i0, int j0, int i1, int j1, bool ceiling)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var idx = new List<int>();
        var map = new Dictionary<int, int>();
        int Vert(int i, int j)
        {
            int id = L.Idx(i, j);
            if (map.TryGetValue(id, out int v)) return v;
            v = verts.Count;
            map[id] = v;
            float y = h[id];
            verts.Add(new Vector3(L.X0 + i, y, L.Z0 + j));
            int ia = Mathf.Max(i - 1, 0), ib = Mathf.Min(i + 1, L.Nx - 1), ja = Mathf.Max(j - 1, 0), jb = Mathf.Min(j + 1, L.Nz - 1);
            float dx = (h[L.Idx(ib, j)] - h[L.Idx(ia, j)]) / (ib - ia);
            float dz = (h[L.Idx(i, jb)] - h[L.Idx(i, ja)]) / (jb - ja);
            var n = new Vector3(-dx, 1, -dz).Normalized();
            norms.Add(ceiling ? -n : n);
            return v;
        }
        for (int j = j0; j < j1; j++)
            for (int i = i0; i < i1; i++)
            {
                float d00 = L.D[L.Idx(i, j)], d10 = L.D[L.Idx(i + 1, j)], d01 = L.D[L.Idx(i, j + 1)], d11 = L.D[L.Idx(i + 1, j + 1)];
                float dmin = Mathf.Min(Mathf.Min(d00, d10), Mathf.Min(d01, d11));
                if (dmin > 3.4f) continue; // totalmente dentro da rocha selada
                if (ceiling)
                {
                    // teto só onde ele não coincide com o chão
                    int a0 = L.Idx(i, j), a1 = L.Idx(i + 1, j), a2 = L.Idx(i, j + 1), a3 = L.Idx(i + 1, j + 1);
                    if (L.C[a0] - L.F[a0] < 0.01f && L.C[a1] - L.F[a1] < 0.01f && L.C[a2] - L.F[a2] < 0.01f && L.C[a3] - L.F[a3] < 0.01f) continue;
                }
                int v00 = Vert(i, j), v10 = Vert(i + 1, j), v01 = Vert(i, j + 1), v11 = Vert(i + 1, j + 1);
                if (!ceiling)
                {
                    idx.Add(v00); idx.Add(v10); idx.Add(v11);
                    idx.Add(v00); idx.Add(v11); idx.Add(v01);
                }
                else
                {
                    idx.Add(v00); idx.Add(v11); idx.Add(v10);
                    idx.Add(v00); idx.Add(v01); idx.Add(v11);
                }
            }
        if (idx.Count == 0) return null;
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
