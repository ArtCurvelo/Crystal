using System;
using System.Collections.Generic;
using System.Linq;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>
/// Mapa (tecla M / Back no controle): planta vista de cima da região, só nas partes que o jogador já viu
/// (<see cref="ExploreMap"/>). Mostra o jogador, a entrada, a saída, o acampamento, o Coração e as câmaras de
/// minério já visitadas. Q/R (LB/RB) ou as setas trocam entre as regiões exploradas.
/// </summary>
public partial class MapScreen : ModalScreen
{
    public WorldBuilder World;
    public Func<Vector3> PlayerPos;
    public Func<float> PlayerYaw;
    /// <summary>Índice da região onde o jogador está (-1 = na vila/casa).</summary>
    public Func<int> PlayerRegion;

    Label _name, _info;
    Button _prev, _next;
    MapCanvas _canvas;
    readonly Dictionary<int, Image> _base = new();
    public int Region { get; private set; }
    /// <summary>Regiões que aparecem no mapa (alguma parte vista).</summary>
    public List<int> Regions { get; private set; } = new();

    public override void _Ready()
    {
        BuildFrame("MAPA", new Vector2(980, 640), UiTheme.Cyan);
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 10);
        Body.AddChild(head);
        _prev = UiTheme.Button("◀", () => Step(-1), 56, 20);
        _next = UiTheme.Button("▶", () => Step(1), 56, 20);
        _name = UiTheme.Label("", 24, UiTheme.Text, true);
        _name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        head.AddChild(_prev);
        head.AddChild(_name);
        head.AddChild(_next);

        var frame = new PanelContainer { CustomMinimumSize = new Vector2(920, 470), SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.015f, 0.02f, 0.04f, 0.98f), new Color(UiTheme.Cyan, 0.25f), 10, 1, 8));
        Body.AddChild(frame);
        _canvas = new MapCanvas { Screen = this, MouseFilter = Control.MouseFilterEnum.Ignore };
        frame.AddChild(_canvas);

        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 12);
        Body.AddChild(foot);
        _info = UiTheme.Label("", 16, UiTheme.Muted);
        _info.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _info.VerticalAlignment = VerticalAlignment.Center;
        foot.AddChild(_info);
        foot.AddChild(UiTheme.Button("Fechar", Close, 140, 17));
    }

    public override void Open()
    {
        var gs = GameState.Instance;
        Regions = new List<int>();
        for (int i = 0; i < GameData.Regions.Count && i < World.Layouts.Count; i++)
            if (ExploreMap.Any(gs.Data, GameData.Regions[i], World.Layouts[i])) Regions.Add(i);
        int here = PlayerRegion?.Invoke() ?? -1;
        if (here >= 0 && !Regions.Contains(here)) { Regions.Add(here); Regions.Sort(); }
        if (Regions.Count == 0) Regions.Add(0);
        Region = here >= 0 ? here : Regions.Contains(Region) ? Region : Regions[0];
        base.Open();
    }

    protected override void OnTab(int dir) => Step(dir);

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Visible) return;
        if (e.IsActionPressed("toggle_map")) { GetViewport().SetInputAsHandled(); Close(); return; }
        if (e.IsActionPressed("ui_left")) { Step(-1); GetViewport().SetInputAsHandled(); return; }
        if (e.IsActionPressed("ui_right")) { Step(1); GetViewport().SetInputAsHandled(); return; }
        base._UnhandledInput(e);
    }

    public void Step(int dir)
    {
        int i = Regions.IndexOf(Region);
        int k = Math.Clamp(i + dir, 0, Regions.Count - 1);
        if (k == i) return;
        Region = Regions[k];
        Refresh();
    }

    public override void Refresh()
    {
        var gs = GameState.Instance;
        var r = GameData.Regions[Region];
        var L = World.Layouts[Region];
        _name.Text = r.Name;
        _name.AddThemeColorOverride("font_color", r.AccentColor.Lerp(Colors.White, 0.35f));
        int i = Regions.IndexOf(Region);
        _prev.Disabled = i <= 0;
        _next.Disabled = i >= Regions.Count - 1;
        float frac = ExploreMap.Fraction(gs.Data, r, L);
        bool here = (PlayerRegion?.Invoke() ?? -1) == Region;
        _info.Text = Loc.F("{0}% explorado", Mathf.RoundToInt(frac * 100)) + "   ·   " +
                     (here ? Loc.T("Você está aqui") : (PlayerRegion?.Invoke() ?? -1) < 0 ? Loc.T("Você está na vila") : Loc.T("Você está em outra região")) +
                     (Regions.Count > 1 ? "   ·   " + Loc.F("[{0}/{1}] trocar de região", InputHints.Key("ui_tab_prev"), InputHints.Key("ui_tab_next")) : "");
        _canvas.Texture = ImageTexture.CreateFromImage(Masked(gs.Data, r, L));
        _canvas.View = ViewOf(gs.Data, r, L);
        _canvas.QueueRedraw();
        Accessibility.Speak(Loc.F("Mapa: {0}. {1}% explorado.", r.Name, Mathf.RoundToInt(frac * 100)), true);
        _prev.CallDeferred(Control.MethodName.GrabFocus);
    }

    /// <summary>Planta da região inteira (1 pixel = 1 m): chão sombreado pela altura e paredes contornadas.</summary>
    Image Base(int ri)
    {
        if (_base.TryGetValue(ri, out var img)) return img;
        var L = World.Layouts[ri];
        var r = GameData.Regions[ri];
        float lo = float.MaxValue, hi = float.MinValue;
        for (int k = 0; k < L.D.Length; k++)
            if (L.D[k] < 0f) { lo = Mathf.Min(lo, L.F[k]); hi = Mathf.Max(hi, L.F[k]); }
        if (hi <= lo) hi = lo + 1f;
        img = Image.CreateEmpty(L.Nx, L.Nz, false, Image.Format.Rgba8);
        var deepCol = r.RockColor.Darkened(0.35f).Lerp(new Color(0.08f, 0.1f, 0.16f), 0.4f);
        var highCol = r.RockColor.Lightened(0.35f).Lerp(r.AccentColor, 0.15f);
        var wall = r.AccentColor.Lerp(Colors.White, 0.55f);
        for (int j = 0; j < L.Nz; j++)
            for (int i = 0; i < L.Nx; i++)
            {
                int k = L.Idx(i, j);
                float d = L.D[k];
                Color c;
                if (d < 0f)
                {
                    float h = (L.F[k] - lo) / (hi - lo);
                    c = deepCol.Lerp(highCol, h);
                    // borda clara colada na parede
                    if (d > -1.2f) c = c.Lerp(wall, 0.55f);
                    c.A = 1f;
                }
                else if (d < 1.4f) c = new Color(wall, 0.85f);
                else c = new Color(0, 0, 0, 0);
                img.SetPixel(i, j, c);
            }
        _base[ri] = img;
        return img;
    }

    /// <summary>A planta com só as partes já vistas (o resto fica no escuro).</summary>
    Image Masked(SaveData d, RegionDef r, CaveLayout L)
    {
        var src = Base(r.Index);
        var img = (Image)src.Duplicate();
        int cell = ExploreMap.Cell;
        for (int cj = 0; cj < ExploreMap.Height(L); cj++)
            for (int ci = 0; ci < ExploreMap.Width(L); ci++)
            {
                float x = L.X0 + ci * cell + 0.5f, z = L.Z0 + cj * cell + 0.5f;
                if (ExploreMap.IsExplored(d, r, L, x, z)) continue;
                int w = Math.Min(cell, L.Nx - ci * cell), h = Math.Min(cell, L.Nz - cj * cell);
                img.FillRect(new Rect2I(ci * cell, cj * cell, w, h), new Color(0, 0, 0, 0));
            }
        return img;
    }

    /// <summary>Área do mapa a mostrar (em pixels da planta): o que já foi visto, com margem, no mínimo 110 m.</summary>
    static Rect2 ViewOf(SaveData d, RegionDef r, CaveLayout L)
    {
        int c = ExploreMap.Cell;
        float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
        for (int j = 0; j < ExploreMap.Height(L); j++)
            for (int i = 0; i < ExploreMap.Width(L); i++)
                if (ExploreMap.IsExplored(d, r, L, L.X0 + i * c + 0.5f, L.Z0 + j * c + 0.5f))
                {
                    x0 = Mathf.Min(x0, i * c); z0 = Mathf.Min(z0, j * c);
                    x1 = Mathf.Max(x1, (i + 1) * c); z1 = Mathf.Max(z1, (j + 1) * c);
                }
        if (x0 > x1) return new Rect2(0, 0, L.Nx, L.Nz);
        const float margin = 18f, min = 110f;
        var center = new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f);
        var size = new Vector2(Mathf.Max(min, x1 - x0 + margin * 2), Mathf.Max(min, z1 - z0 + margin * 2));
        return new Rect2(center - size / 2f, size);
    }

    /// <summary>Desenha a planta ajustada à área e os marcadores por cima.</summary>
    sealed partial class MapCanvas : Control
    {
        public MapScreen Screen;
        public Texture2D Texture;
        public Rect2 View;

        public override void _Draw()
        {
            if (Texture == null || Screen?.World == null) return;
            var L = Screen.World.Layouts[Screen.Region];
            var r = GameData.Regions[Screen.Region];
            var gs = GameState.Instance;
            var view = View.Size.X > 1 ? View : new Rect2(0, 0, L.Nx, L.Nz);
            float s = Mathf.Min(Size.X / view.Size.X, Size.Y / view.Size.Y);
            var origin = (Size - view.Size * s) / 2f - view.Position * s;
            Vector2 Map(float x, float z) => origin + new Vector2(x - L.X0, z - L.Z0) * s;
            bool Seen(Vector3 p) => ExploreMap.IsExplored(gs.Data, r, L, p.X, p.Z);
            // só o pedaço visível da planta (o resto ficaria fora do quadro)
            var src = new Rect2(view.Position, view.Size).Intersection(new Rect2(0, 0, L.Nx, L.Nz));
            DrawTextureRectRegion(Texture, new Rect2(origin + src.Position * s, src.Size * s), src);
            var font = UiTheme.Bold;

            void Label(Vector2 at, string text, Color col, int size = 15)
            {
                var w = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
                at = new Vector2(Mathf.Clamp(at.X, w / 2 + 4, Size.X - w / 2 - 4), Mathf.Clamp(at.Y, size + 2, Size.Y - 4));
                DrawStringOutline(font, at + new Vector2(-w / 2, 0), text, HorizontalAlignment.Left, -1, size, 5, new Color(0, 0, 0, 0.85f));
                DrawString(font, at + new Vector2(-w / 2, 0), text, HorizontalAlignment.Left, -1, size, col);
            }
            void Pin(Vector3 p, Color col, string text)
            {
                var m = Map(p.X, p.Z);
                DrawCircle(m, 7f, new Color(0, 0, 0, 0.8f));
                DrawCircle(m, 5f, col);
                Label(m + new Vector2(0, -12), text, col);
            }

            // câmaras de minério já visitadas
            foreach (var c in L.Chambers)
                if (c.Wing != null && Seen(new Vector3(c.Center.X, 0, c.Center.Y)))
                    Label(Map(c.Center.X, c.Center.Y) + new Vector2(0, 5), c.Wing.Name, new Color(0.85f, 0.9f, 1f, 0.85f), 13);
            if (L.Camp != null && Seen(new Vector3(L.Camp.Center.X, 0, L.Camp.Center.Y)))
                Pin(new Vector3(L.Camp.Center.X, 0, L.Camp.Center.Y), UiTheme.Gold, Loc.T("Acampamento"));
            if (L.Heart != null && Seen(new Vector3(L.Heart.Center.X, 0, L.Heart.Center.Y)))
                Pin(new Vector3(L.Heart.Center.X, 0, L.Heart.Center.Y), new Color(1f, 0.35f, 0.3f), Loc.T("Coração Ancestral"));
            if (Screen.Region > 0 && Seen(L.Entrance)) Pin(L.Entrance, new Color(0.55f, 1f, 0.6f), Loc.T("Entrada"));
            bool lastMine = !r.Deep && (Screen.Region + 1 >= GameData.Regions.Count || GameData.Regions[Screen.Region + 1].Deep);
            if (!r.Deep && !lastMine && Seen(L.Exit)) Pin(L.Exit, new Color(1f, 0.75f, 0.35f), Loc.T("Saída para a próxima região"));

            // o jogador: seta na direção em que olha
            if ((Screen.PlayerRegion?.Invoke() ?? -1) == Screen.Region && Screen.PlayerPos != null)
            {
                var p = Screen.PlayerPos();
                var m = Map(p.X, p.Z);
                float yaw = Screen.PlayerYaw?.Invoke() ?? 0f;
                var f = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
                var side = new Vector2(-f.Y, f.X);
                float pulse = 1f + 0.15f * Mathf.Sin(Time.GetTicksMsec() / 180f);
                DrawCircle(m, 13f * pulse, new Color(UiTheme.Cyan, 0.25f));
                var tip = m + f * 13f; var l = m - f * 8f + side * 8f; var rr = m - f * 8f - side * 8f; var back = m - f * 4f;
                DrawColoredPolygon(new[] { tip, l, back, rr }, new Color(0, 0, 0, 0.9f));
                DrawColoredPolygon(new[] { m + f * 10f, m - f * 6f + side * 6f, m - f * 2.5f, m - f * 6f - side * 6f }, UiTheme.Cyan);
                Label(m + new Vector2(0, 28), Loc.T("Você"), UiTheme.Cyan);
            }
        }

        public override void _Process(double delta) { if (IsVisibleInTree()) QueueRedraw(); }
    }
}
