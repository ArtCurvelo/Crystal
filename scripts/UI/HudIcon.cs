using Godot;

namespace CrystalExcavator.UI;

/// <summary>Ícones vetoriais desenhados por código (sem imagens): moeda, mochila, gema, raio, picareta, região.</summary>
public partial class HudIcon : Control
{
    public enum Kind { Coin, Bag, Gem, Bolt, Pick, Region, Star, Nugget, Orb, Banded, Shard, Ember, Clock,
        RegionCaves, RegionDepths, RegionCore, RegionReservoir, RegionFungal, RegionCathedral }

    /// <summary>Ícone próprio de cada local.</summary>
    public static Kind RegionKind(string regionId) => regionId switch
    {
        "caves" => Kind.RegionCaves,
        "depths" => Kind.RegionDepths,
        "core" => Kind.RegionCore,
        "abyss" => Kind.RegionReservoir,
        "spore" => Kind.RegionFungal,
        "cathedral" => Kind.RegionCathedral,
        _ => Kind.Region,
    };

    /// <summary>Polígono com contorno suavizado (a borda some o serrilhado do preenchimento).</summary>
    void Poly(Vector2[] pts, Color fill, Color? edge = null, float width = 1.2f)
    {
        DrawColoredPolygon(pts, fill);
        var loop = new Vector2[pts.Length + 1];
        pts.CopyTo(loop, 0); loop[^1] = pts[0];
        DrawPolyline(loop, edge ?? fill, width, true);
    }

    static Vector2[] Ring(Vector2 c, float r, int n, float rot = 0f, float sy = 1f)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = rot + Mathf.Tau * i / n; p[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * sy) * r; }
        return p;
    }

    /// <summary>Ícone conforme o formato do minério (cristal, pepita, esfera, faixas, lasca, brasa).</summary>
    public static Kind KindFor(Data.MaterialDef m) => m.Archetype switch
    {
        Data.OreArchetype.Vein => Kind.Nugget,
        Data.OreArchetype.Pearl => Kind.Orb,
        Data.OreArchetype.Geode when m.GeodeSpheres => Kind.Orb,
        Data.OreArchetype.Banded => Kind.Banded,
        Data.OreArchetype.Shards => Kind.Shard,
        Data.OreArchetype.Magma => Kind.Ember,
        _ => Kind.Gem,
    };

    /// <summary>Muda o tipo/cor e redesenha.</summary>
    public void Set(Kind k, Color c) { Type = k; Color = c; Ore = null; QueueRedraw(); }

    /// <summary>Minério mostrado como a foto da forma 3D dele (ícone próprio); null = ícone desenhado.</summary>
    public Data.MaterialDef Ore;
    public void SetOre(Data.MaterialDef m) { Type = KindFor(m); Color = m.Glow.Lightened(0.15f); Ore = m; QueueRedraw(); }

    public static HudIcon MakeOre(Data.MaterialDef m, float size)
    {
        var i = Make(KindFor(m), m.Glow.Lightened(0.15f), size);
        i.Ore = m;
        return i;
    }

    public override void _EnterTree() => OreIcons.Ready += OnIconReady;
    public override void _ExitTree() => OreIcons.Ready -= OnIconReady;
    void OnIconReady() { if (Ore != null) QueueRedraw(); }
    public Kind Type;
    public Color Color = UiTheme.Cyan;

    public HudIcon() { MouseFilter = MouseFilterEnum.Ignore; }

    public static HudIcon Make(Kind k, Color c, float size)
    {
        return new HudIcon { Type = k, Color = c, CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = SizeFlags.ShrinkCenter };
    }

    public override void _Draw()
    {
        if (Ore != null && OreIcons.Get(Ore) is { } tex)
        {
            // a foto vem com margem: amplia um pouco para ocupar o espaço do ícone
            float m = Mathf.Min(Size.X, Size.Y) * 0.12f;
            DrawTextureRect(tex, new Rect2(-m, -m, Size.X + 2 * m, Size.Y + 2 * m), false);
            return;
        }
        var s = Size;
        float w = s.X, h = s.Y;
        var c = new Vector2(w / 2, h / 2);
        float r = Mathf.Min(w, h) / 2;
        switch (Type)
        {
            case Kind.Coin:
            {
                // Lúmen: moeda hexagonal de metal com um cristal facetado brilhando no centro
                var dark = Color.Darkened(0.45f);
                Poly(Ring(c, r * 0.95f, 6, Mathf.Pi / 6), dark, Color.Darkened(0.2f), r * 0.08f);
                Poly(Ring(c, r * 0.78f, 6, Mathf.Pi / 6), Color, Color.Lightened(0.3f), r * 0.05f);
                var gem = UiTheme.Cyan;
                var top = c + new Vector2(0, -r * 0.5f); var bot = c + new Vector2(0, r * 0.52f);
                var lft = c + new Vector2(-r * 0.36f, -r * 0.08f); var rgt = c + new Vector2(r * 0.36f, -r * 0.08f);
                Poly(new[] { top, rgt, bot, lft }, gem.Darkened(0.25f), gem.Lightened(0.2f), r * 0.05f);
                Poly(new[] { top, rgt, c + new Vector2(0, -r * 0.02f) }, gem.Lightened(0.45f));
                Poly(new[] { top, c + new Vector2(0, -r * 0.02f), lft }, gem.Lightened(0.15f));
                DrawCircle(c + new Vector2(-r * 0.12f, -r * 0.28f), r * 0.07f, new Color(1, 1, 1, 0.9f));
                break;
            }
            case Kind.Bag:
            {
                // mochila: corpo arredondado, aba, bolso com fivela e alça
                var body = Color; var shade = Color.Darkened(0.35f);
                DrawArc(c + new Vector2(0, -r * 0.55f), r * 0.32f, Mathf.Pi, Mathf.Tau, 14, shade, r * 0.14f, true);
                var bodyPts = new[] {
                    c + new Vector2(-0.68f, -0.45f) * r, c + new Vector2(0.68f, -0.45f) * r, c + new Vector2(0.78f, 0.75f) * r,
                    c + new Vector2(0.6f, 0.92f) * r, c + new Vector2(-0.6f, 0.92f) * r, c + new Vector2(-0.78f, 0.75f) * r };
                Poly(bodyPts, body, shade, r * 0.07f);
                Poly(new[] { c + new Vector2(-0.7f, -0.47f) * r, c + new Vector2(0.7f, -0.47f) * r, c + new Vector2(0.62f, 0.05f) * r, c + new Vector2(-0.62f, 0.05f) * r }, body.Lightened(0.15f), shade, r * 0.06f);
                Poly(new[] { c + new Vector2(-0.42f, 0.25f) * r, c + new Vector2(0.42f, 0.25f) * r, c + new Vector2(0.42f, 0.72f) * r, c + new Vector2(-0.42f, 0.72f) * r }, shade);
                DrawRect(new Rect2(c + new Vector2(-0.09f, -0.06f) * r, new Vector2(0.18f, 0.2f) * r), UiTheme.Gold);
                break;
            }
            case Kind.Gem:
                DrawGem(c, r, Color);
                break;
            case Kind.Nugget:
            {
                // pepita: polígono irregular com reflexos
                var nug = new Vector2[8];
                float[] k = { 0.9f, 0.7f, 0.95f, 0.75f, 0.85f, 0.65f, 0.9f, 0.7f };
                for (int i = 0; i < 8; i++) { float a = Mathf.Tau * i / 8 + 0.3f; nug[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.8f) * r * k[i]; }
                DrawColoredPolygon(nug, Color.Darkened(0.25f));
                DrawColoredPolygon(new[] { nug[5], nug[6], nug[7], c }, Color.Lightened(0.35f));
                DrawCircle(c + new Vector2(-0.25f, -0.2f) * r, r * 0.14f, Colors.White);
                break;
            }
            case Kind.Orb:
                DrawCircle(c, r * 0.85f, Color);
                DrawCircle(c + new Vector2(0.15f, 0.15f) * r, r * 0.55f, Color.Darkened(0.2f));
                DrawCircle(c + new Vector2(-0.3f, -0.3f) * r, r * 0.22f, new Color(1, 1, 1, 0.9f));
                break;
            case Kind.Banded:
                DrawCircle(c, r * 0.85f, Color);
                for (int i = -1; i <= 1; i++)
                    DrawArc(c + new Vector2(0, r * 0.9f), r * (0.75f + i * 0.28f), Mathf.Pi * 1.15f, Mathf.Pi * 1.85f, 14, Color.Lightened(0.45f), r * 0.12f, true);
                break;
            case Kind.Shard:
                DrawColoredPolygon(new[] { c + new Vector2(-0.1f, -1f) * r, c + new Vector2(0.35f, 0.9f) * r, c + new Vector2(-0.45f, 0.75f) * r }, Color.Darkened(0.4f));
                DrawColoredPolygon(new[] { c + new Vector2(0.45f, -0.6f) * r, c + new Vector2(0.75f, 0.9f) * r, c + new Vector2(0.3f, 0.9f) * r }, Color.Darkened(0.2f));
                DrawPolyline(new[] { c + new Vector2(-0.1f, -1f) * r, c + new Vector2(0.35f, 0.9f) * r }, Color.Lightened(0.5f), 1.5f, true);
                break;
            case Kind.Ember:
            {
                var emb = new Vector2[7];
                for (int i = 0; i < 7; i++) { float a = Mathf.Tau * i / 7; emb[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * (i % 2 == 0 ? 0.9f : 0.7f); }
                DrawColoredPolygon(emb, new Color(0.15f, 0.08f, 0.06f));
                DrawPolyline(new[] { c + new Vector2(-0.6f, -0.2f) * r, c, c + new Vector2(0.3f, 0.6f) * r }, Color, r * 0.15f, true);
                DrawPolyline(new[] { c, c + new Vector2(0.55f, -0.45f) * r }, Color, r * 0.12f, true);
                break;
            }
            case Kind.Clock:
            {
                // dia: mostrador com sol nascendo no horizonte e ponteiro
                DrawCircle(c, r * 0.92f, Color.Darkened(0.5f));
                DrawArc(c, r * 0.86f, 0, Mathf.Tau, 32, Color, r * 0.12f, true);
                var sun = c + new Vector2(0, r * 0.18f);
                DrawCircle(sun, r * 0.28f, Color.Lightened(0.25f));
                for (int i = 0; i < 5; i++)
                {
                    float a = Mathf.Pi + Mathf.Pi * (i + 0.5f) / 5f;
                    var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    DrawLine(sun + d * r * 0.38f, sun + d * r * 0.55f, Color.Lightened(0.25f), r * 0.09f, true);
                }
                DrawRect(new Rect2(c + new Vector2(-0.7f, 0.2f) * r, new Vector2(1.4f, 0.4f) * r), Color.Darkened(0.5f));
                DrawLine(c + new Vector2(-0.68f, 0.2f) * r, c + new Vector2(0.68f, 0.2f) * r, Color, r * 0.08f, true);
                break;
            }
            case Kind.Bolt:
                var pts = new Vector2[]
                {
                    c + new Vector2(0.15f, -1f) * r, c + new Vector2(-0.55f, 0.15f) * r, c + new Vector2(-0.05f, 0.15f) * r,
                    c + new Vector2(-0.2f, 1f) * r, c + new Vector2(0.6f, -0.2f) * r, c + new Vector2(0.08f, -0.2f) * r,
                };
                DrawColoredPolygon(pts, Color);
                break;
            case Kind.Pick:
                DrawLine(c + new Vector2(-0.7f, 0.8f) * r, c + new Vector2(0.4f, -0.4f) * r, Color, r * 0.22f, true);
                DrawArc(c + new Vector2(0.15f, 0.15f) * r, r * 0.95f, -Mathf.Pi * 0.95f, -Mathf.Pi * 0.05f, 14, Color, r * 0.22f, true);
                break;
            case Kind.Region:
                DrawColoredPolygon(new[] { c + new Vector2(0, -1f) * r, c + new Vector2(0.45f, 0.9f) * r, c + new Vector2(-0.45f, 0.9f) * r }, Color);
                DrawColoredPolygon(new[] { c + new Vector2(-0.55f, -0.3f) * r, c + new Vector2(-0.3f, 0.9f) * r, c + new Vector2(-0.85f, 0.9f) * r }, Color.Darkened(0.25f));
                DrawColoredPolygon(new[] { c + new Vector2(0.6f, -0.4f) * r, c + new Vector2(0.9f, 0.9f) * r, c + new Vector2(0.35f, 0.9f) * r }, Color.Darkened(0.25f));
                break;
            case Kind.RegionCaves:
            {
                // cavernas: arco de caverna com três cristais
                DrawArc(c + new Vector2(0, r * 0.9f), r * 0.95f, Mathf.Pi, Mathf.Tau, 20, Color.Darkened(0.45f), r * 0.16f, true);
                Poly(new[] { c + new Vector2(0, -0.6f) * r, c + new Vector2(0.22f, 0.85f) * r, c + new Vector2(-0.22f, 0.85f) * r }, Color, Color.Lightened(0.3f));
                Poly(new[] { c + new Vector2(-0.48f, -0.1f) * r, c + new Vector2(-0.25f, 0.85f) * r, c + new Vector2(-0.62f, 0.85f) * r }, Color.Darkened(0.2f));
                Poly(new[] { c + new Vector2(0.5f, 0.05f) * r, c + new Vector2(0.64f, 0.85f) * r, c + new Vector2(0.28f, 0.85f) * r }, Color.Darkened(0.2f));
                break;
            }
            case Kind.RegionDepths:
            {
                // profundezas prismáticas: prisma que abre a luz em leque
                var a1 = c + new Vector2(-0.15f, -0.75f) * r; var b1 = c + new Vector2(0.45f, 0.55f) * r; var c1 = c + new Vector2(-0.75f, 0.55f) * r;
                DrawLine(c + new Vector2(-1f, -0.1f) * r, c + new Vector2(-0.38f, -0.05f) * r, Colors.White, r * 0.08f, true);
                Color[] rays = { new(1f, 0.35f, 0.4f), new(1f, 0.85f, 0.3f), new(0.35f, 1f, 0.6f), new(0.4f, 0.6f, 1f) };
                for (int i = 0; i < 4; i++) DrawLine(c + new Vector2(0.15f, 0.0f) * r, c + new Vector2(0.95f, -0.35f + i * 0.3f) * r, rays[i], r * 0.08f, true);
                Poly(new[] { a1, b1, c1 }, new Color(Color, 0.85f), Color.Lightened(0.4f), r * 0.07f);
                break;
            }
            case Kind.RegionCore:
            {
                // núcleo ancestral: esfera de magma rachada com chamas
                for (int i = 0; i < 3; i++)
                {
                    float x = -0.4f + i * 0.4f;
                    Poly(new[] { c + new Vector2(x - 0.18f, -0.2f) * r, c + new Vector2(x, -0.95f + (i == 1 ? -0.05f : 0.15f)) * r, c + new Vector2(x + 0.18f, -0.2f) * r }, new Color(1f, 0.6f, 0.15f));
                }
                DrawCircle(c + new Vector2(0, r * 0.2f), r * 0.68f, new Color(0.22f, 0.08f, 0.05f));
                DrawPolyline(new[] { c + new Vector2(-0.5f, 0.0f) * r, c + new Vector2(-0.1f, 0.25f) * r, c + new Vector2(0.15f, 0.05f) * r, c + new Vector2(0.5f, 0.4f) * r }, Color, r * 0.11f, true);
                DrawPolyline(new[] { c + new Vector2(-0.1f, 0.25f) * r, c + new Vector2(-0.2f, 0.75f) * r }, Color, r * 0.09f, true);
                break;
            }
            case Kind.RegionReservoir:
            {
                // reservatório abissal: gota d'água sobre ondas
                var drop = new Vector2[18];
                for (int i = 0; i < 18; i++)
                {
                    float a = Mathf.Tau * i / 18f;
                    float rr = r * 0.48f;
                    var p = new Vector2(Mathf.Sin(a), -Mathf.Cos(a));
                    drop[i] = c + new Vector2(0, r * 0.05f) + new Vector2(p.X * rr * (p.Y < 0 ? 1f + p.Y * 0.85f : 1f), p.Y < 0 ? p.Y * r * 0.95f : p.Y * rr);
                }
                Poly(drop, Color, Color.Lightened(0.35f), r * 0.07f);
                DrawCircle(c + new Vector2(-0.15f, 0.12f) * r, r * 0.1f, new Color(1, 1, 1, 0.85f));
                for (int k = 0; k < 2; k++)
                {
                    var wave = new Vector2[12];
                    for (int i = 0; i < 12; i++) { float x = -0.95f + i * 1.9f / 11f; wave[i] = c + new Vector2(x, 0.72f + k * 0.2f + Mathf.Sin(x * 7f) * 0.06f) * r; }
                    DrawPolyline(wave, Color.Darkened(0.2f * k), r * 0.08f, true);
                }
                break;
            }
            case Kind.RegionFungal:
            {
                // jardim fúngico: cogumelo grande e um pequeno, com pintas
                Poly(new[] { c + new Vector2(-0.14f, 0.1f) * r, c + new Vector2(0.14f, 0.1f) * r, c + new Vector2(0.2f, 0.9f) * r, c + new Vector2(-0.2f, 0.9f) * r }, new Color(0.92f, 0.9f, 0.82f));
                var cap = new Vector2[14];
                for (int i = 0; i < 14; i++) { float a = Mathf.Pi + Mathf.Pi * i / 13f; cap[i] = c + new Vector2(Mathf.Cos(a) * 0.82f, 0.15f + Mathf.Sin(a) * 0.72f) * r; }
                Poly(cap, Color, Color.Lightened(0.3f), r * 0.06f);
                DrawCircle(c + new Vector2(-0.35f, -0.2f) * r, r * 0.11f, new Color(1, 1, 1, 0.85f));
                DrawCircle(c + new Vector2(0.2f, -0.35f) * r, r * 0.08f, new Color(1, 1, 1, 0.85f));
                DrawCircle(c + new Vector2(0.75f, 0.68f) * r, r * 0.17f, Color.Darkened(0.2f));
                DrawLine(c + new Vector2(0.75f, 0.7f) * r, c + new Vector2(0.75f, 0.92f) * r, new Color(0.92f, 0.9f, 0.82f), r * 0.08f, true);
                break;
            }
            case Kind.RegionCathedral:
            {
                // catedral de cristal: janela gótica pontiaguda com vitral
                // ordem: lado esquerdo subindo, direito descendo
                var win = new Vector2[] { c + new Vector2(-0.65f, 0.9f) * r, c + new Vector2(-0.65f, -0.1f) * r, c + new Vector2(-0.45f, -0.6f) * r, c + new Vector2(0, -0.95f) * r, c + new Vector2(0.45f, -0.6f) * r, c + new Vector2(0.65f, -0.1f) * r, c + new Vector2(0.65f, 0.9f) * r };
                Poly(win, Color.Darkened(0.45f), Color.Lightened(0.25f), r * 0.08f);
                DrawLine(c + new Vector2(0, -0.85f) * r, c + new Vector2(0, 0.88f) * r, Color.Lightened(0.25f), r * 0.07f, true);
                DrawLine(c + new Vector2(-0.6f, 0.1f) * r, c + new Vector2(0.6f, 0.1f) * r, Color.Lightened(0.25f), r * 0.07f, true);
                DrawCircle(c + new Vector2(0, -0.35f) * r, r * 0.17f, Color.Lightened(0.4f));
                Poly(new[] { c + new Vector2(-0.4f, 0.25f) * r, c + new Vector2(-0.1f, 0.25f) * r, c + new Vector2(-0.1f, 0.75f) * r, c + new Vector2(-0.4f, 0.75f) * r }, new Color(Color, 0.6f));
                Poly(new[] { c + new Vector2(0.1f, 0.25f) * r, c + new Vector2(0.4f, 0.25f) * r, c + new Vector2(0.4f, 0.75f) * r, c + new Vector2(0.1f, 0.75f) * r }, new Color(UiTheme.Gold, 0.6f));
                break;
            }
            case Kind.Star:
                var star = new Vector2[10];
                for (int i = 0; i < 10; i++)
                {
                    float a = -Mathf.Pi / 2 + i * Mathf.Pi / 5;
                    float rr = i % 2 == 0 ? r : r * 0.45f;
                    star[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * rr;
                }
                DrawColoredPolygon(star, Color);
                break;
        }
    }

    void DrawEllipse(Vector2 center, float rx, float ry, Color col)
    {
        var pts = new Vector2[20];
        for (int i = 0; i < 20; i++)
        {
            float a = Mathf.Tau * i / 20;
            pts[i] = center + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
        }
        DrawColoredPolygon(pts, col);
    }

    /// <summary>Gema facetada (hexagonal com topo claro e laterais escuras).</summary>
    public void DrawGem(Vector2 c, float r, Color col)
    {
        var top = c + new Vector2(0, -r);
        var tl = c + new Vector2(-r * 0.75f, -r * 0.35f);
        var tr = c + new Vector2(r * 0.75f, -r * 0.35f);
        var bl = c + new Vector2(-r * 0.55f, r * 0.55f);
        var br = c + new Vector2(r * 0.55f, r * 0.55f);
        var bottom = c + new Vector2(0, r);
        var mid = c + new Vector2(0, -r * 0.1f);
        DrawColoredPolygon(new[] { top, tr, mid }, col.Lightened(0.45f));
        DrawColoredPolygon(new[] { top, mid, tl }, col.Lightened(0.2f));
        DrawColoredPolygon(new[] { tl, mid, bottom, bl }, col);
        DrawColoredPolygon(new[] { mid, tr, br, bottom }, col.Darkened(0.3f));
        DrawPolyline(new[] { top, tr, br, bottom, bl, tl, top }, col.Lightened(0.6f), 1.2f, true);
    }
}

/// <summary>Moldura de cantoneiras (estilo mira de scanner) desenhada em volta do conteúdo.</summary>
public partial class BracketFrame : Control
{
    public Color Color = UiTheme.Cyan;
    public float Len = 22f;
    public float Width = 3f;

    public BracketFrame() { MouseFilter = MouseFilterEnum.Ignore; }

    public override void _Draw()
    {
        var s = Size;
        void Corner(Vector2 p, Vector2 dx, Vector2 dy)
        {
            DrawLine(p, p + dx * Len, Color, Width, true);
            DrawLine(p, p + dy * Len, Color, Width, true);
        }
        Corner(Vector2.Zero, Vector2.Right, Vector2.Down);
        Corner(new Vector2(s.X, 0), Vector2.Left, Vector2.Down);
        Corner(new Vector2(0, s.Y), Vector2.Right, Vector2.Up);
        Corner(s, Vector2.Left, Vector2.Up);
    }
}
