using Godot;

namespace CrystalExcavator.UI;

/// <summary>Ícones vetoriais desenhados por código (sem imagens): moeda, mochila, gema, raio, picareta, região.</summary>
public partial class HudIcon : Control
{
    public enum Kind { Coin, Bag, Gem, Bolt, Pick, Region, Star, Nugget, Orb, Banded, Shard, Ember }

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
    public void Set(Kind k, Color c) { Type = k; Color = c; QueueRedraw(); }
    public Kind Type;
    public Color Color = UiTheme.Cyan;

    public HudIcon() { MouseFilter = MouseFilterEnum.Ignore; }

    public static HudIcon Make(Kind k, Color c, float size)
    {
        return new HudIcon { Type = k, Color = c, CustomMinimumSize = new Vector2(size, size), SizeFlagsVertical = SizeFlags.ShrinkCenter };
    }

    public override void _Draw()
    {
        var s = Size;
        float w = s.X, h = s.Y;
        var c = new Vector2(w / 2, h / 2);
        float r = Mathf.Min(w, h) / 2;
        switch (Type)
        {
            case Kind.Coin:
                for (int i = 2; i >= 0; i--)
                {
                    var center = c + new Vector2(0, (i - 1) * r * 0.42f);
                    DrawEllipse(center, r * 0.85f, r * 0.32f, Color.Darkened(0.35f));
                    DrawEllipse(center - new Vector2(0, r * 0.08f), r * 0.85f, r * 0.32f, Color);
                    DrawEllipse(center - new Vector2(0, r * 0.08f), r * 0.5f, r * 0.17f, Color.Lightened(0.35f));
                }
                break;
            case Kind.Bag:
                var body = new Rect2(c.X - r * 0.7f, c.Y - r * 0.35f, r * 1.4f, r * 1.2f);
                DrawRect(body, Color);
                DrawRect(new Rect2(body.Position.X + r * 0.2f, body.Position.Y + r * 0.45f, r, r * 0.45f), Color.Darkened(0.35f));
                DrawArc(c - new Vector2(0, r * 0.35f), r * 0.42f, Mathf.Pi, Mathf.Tau, 12, Color, r * 0.18f, true);
                break;
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
