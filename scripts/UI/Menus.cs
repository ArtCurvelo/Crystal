using System;
using System.Linq;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>Base de janelas modais: fundo escurecido + painel central animado.</summary>
public abstract partial class ModalScreen : CanvasLayer
{
    protected Control Root;
    protected PanelContainer Panel;
    protected VBoxContainer Body;
    public bool IsOpen => Visible;
    public event Action Closed;

    protected void BuildFrame(string title, Vector2 size, Color accent)
    {
        Layer = 10;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        Root = new Control { Theme = UiTheme.Get() };
        Root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(Root);
        var dim = new ColorRect { Color = new Color(0.01f, 0.005f, 0.03f, 0.72f) };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(dim);
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Root.AddChild(center);
        Panel = new PanelContainer { CustomMinimumSize = size };
        Panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Bg, new Color(accent, 0.7f), 16, 2, 24));
        center.AddChild(Panel);
        Body = new VBoxContainer();
        Body.AddThemeConstantOverride("separation", 12);
        Panel.AddChild(Body);
        if (!string.IsNullOrEmpty(title))
        {
            var header = new HBoxContainer();
            Body.AddChild(header);
            var t = UiTheme.Label(title, 30, accent, true);
            t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            header.AddChild(t);
            HeaderExtra(header);
            var sep = new ColorRect { Color = new Color(accent, 0.35f), CustomMinimumSize = new Vector2(0, 2) };
            Body.AddChild(sep);
        }
    }

    protected virtual void HeaderExtra(HBoxContainer header) { }

    public virtual void Open()
    {
        Visible = true;
        Refresh();
        UiTheme.PopIn(Panel);
    }

    public virtual void Close()
    {
        if (!Visible) return;
        Visible = false;
        Closed?.Invoke();
    }

    public virtual void Refresh() { }
}

// =====================================================================
/// <summary>Painel de configurações (usado no menu principal e na pausa).</summary>
public partial class SettingsScreen : ModalScreen
{
    public override void _Ready()
    {
        BuildFrame("CONFIGURAÇÕES", new Vector2(620, 0), UiTheme.Cyan);
        Layer = 20;
        var d = Settings.Data;
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 470), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Body.AddChild(scroll);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 6);
        scroll.AddChild(grid);

        AddSlider(grid, "Sensibilidade do mouse", 0.02f, 1.0f, 0.01f, d.MouseSensitivity, v => d.MouseSensitivity = v, v => $"{v * 100:0}");
        AddSlider(grid, "Campo de visão", 60, 100, 1, d.Fov, v => d.Fov = v, v => $"{v:0}°");
        AddSlider(grid, "Volume geral", 0, 1, 0.01f, d.MasterVolume, v => d.MasterVolume = v, v => $"{v * 100:0}%");
        AddSlider(grid, "Música", 0, 1, 0.01f, d.MusicVolume, v => d.MusicVolume = v, v => $"{v * 100:0}%");
        AddSlider(grid, "Efeitos", 0, 1, 0.01f, d.SfxVolume, v => d.SfxVolume = v, v => $"{v * 100:0}%");
        AddSlider(grid, "Escala de resolução 3D", 0.5f, 1f, 0.05f, d.RenderScale, v => d.RenderScale = v, v => $"{v * 100:0}%");

        grid.AddChild(UiTheme.Label("Qualidade gráfica", 18));
        var q = new OptionButton { CustomMinimumSize = new Vector2(260, 34) };
        q.AddItem("Baixa (PCs modestos)", 0);
        q.AddItem("Média", 1);
        q.AddItem("Alta", 2);
        q.Selected = d.Quality;
        q.ItemSelected += i => { d.Quality = (int)i; Changed(); GameState.Instance.Notify("Qualidade alterada", "Alguns efeitos são aplicados ao reiniciar a partida.", NotifyKind.Info); };
        grid.AddChild(q);

        grid.AddChild(UiTheme.Label("Resolução da janela", 18));
        var res = new OptionButton { CustomMinimumSize = new Vector2(260, 34) };
        foreach (var sz in Settings.WindowSizes) res.AddItem($"{sz.X} × {sz.Y}" + (sz.Y == 720 ? " (recomendado p/ PCs modestos)" : ""));
        res.Selected = d.WindowSize;
        res.ItemSelected += i => { d.WindowSize = (int)i; Changed(); };
        grid.AddChild(res);

        grid.AddChild(UiTheme.Label("Limite de FPS", 18));
        var fps = new OptionButton { CustomMinimumSize = new Vector2(260, 34) };
        int[] fpsVals = { 30, 60, 120, 0 };
        foreach (var f in fpsVals) fps.AddItem(f == 0 ? "Ilimitado" : f.ToString());
        fps.Selected = Math.Max(0, Array.IndexOf(fpsVals, d.MaxFps));
        fps.ItemSelected += i => { d.MaxFps = fpsVals[i]; Changed(); };
        grid.AddChild(fps);

        AddCheck(grid, "Tela cheia", d.Fullscreen, v => d.Fullscreen = v);
        AddCheck(grid, "Sincronização vertical (VSync)", d.VSync, v => d.VSync = v);
        AddCheck(grid, "Inverter eixo Y", d.InvertY, v => d.InvertY = v);
        AddCheck(grid, "Mostrar FPS (F3)", d.ShowFps, v => d.ShowFps = v);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        footer.AddChild(UiTheme.Button("Voltar", Close, 180));
    }

    void Changed()
    {
        Settings.Apply(GetTree());
        Settings.Save();
    }

    void AddSlider(GridContainer g, string label, float min, float max, float step, float value, Action<float> set, Func<float, string> fmt)
    {
        g.AddChild(UiTheme.Label(label, 18));
        var h = new HBoxContainer { CustomMinimumSize = new Vector2(260, 0) };
        var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        var v = UiTheme.Label(fmt(value), 16, UiTheme.Cyan, false, HorizontalAlignment.Right);
        v.CustomMinimumSize = new Vector2(56, 0);
        s.ValueChanged += x => { set((float)x); v.Text = fmt((float)x); Changed(); };
        h.AddChild(s);
        h.AddChild(v);
        g.AddChild(h);
    }

    void AddCheck(GridContainer g, string label, bool value, Action<bool> set)
    {
        g.AddChild(UiTheme.Label(label, 18));
        var c = new CheckButton { ButtonPressed = value, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            c.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
        c.Toggled += x => { set(x); AudioManager.Instance?.Play("click", -6f); Changed(); };
        g.AddChild(c);
    }
}

// =====================================================================
/// <summary>Menu de pausa.</summary>
public partial class PauseScreen : ModalScreen
{
    public Action OnResume, OnSave, OnSettings, OnCodex, OnHelp, OnMainMenu, OnQuit;
    Label _stats;

    public override void _Ready()
    {
        BuildFrame("PAUSA", new Vector2(440, 0), UiTheme.Violet);
        _stats = UiTheme.Label("", 15, UiTheme.Muted);
        Body.AddChild(_stats);
        Body.AddChild(UiTheme.Spacer(4));
        foreach (var (text, act) in new (string, Func<Action>)[]
                 {
                     ("Continuar", () => OnResume), ("Salvar jogo", () => OnSave), ("Coleção", () => OnCodex), ("Como jogar", () => OnHelp), ("Configurações", () => OnSettings),
                     ("Menu principal", () => OnMainMenu), ("Sair do jogo", () => OnQuit)
                 })
        {
            var b = UiTheme.Button(text, () => act()?.Invoke(), 0, 22);
            b.CustomMinimumSize = new Vector2(0, 52);
            Body.AddChild(b);
        }
    }

    public override void Open()
    {
        base.Open();
        (Body.GetChild(4) as Button)?.GrabFocus();
    }

    public override void Refresh()
    {
        var d = GameState.Instance.Data;
        var t = TimeSpan.FromSeconds(d.PlayTimeSeconds);
        _stats.Text = $"Tempo de jogo: {(int)t.TotalHours}h {t.Minutes:00}m   ·   Cristais minerados: {d.CrystalsMined}\n" +
                      $"Total ganho: {GameState.FormatMoney(d.TotalEarned)}   ·   Materiais: {d.Discovered.Count}/{GameData.Materials.Count}";
    }
}

// =====================================================================
/// <summary>Diálogo simples de confirmação.</summary>
public partial class ConfirmScreen : ModalScreen
{
    Label _text;
    Action _onYes;

    public override void _Ready()
    {
        BuildFrame("CONFIRMAR", new Vector2(480, 0), UiTheme.Danger);
        Layer = 30;
        _text = UiTheme.Label("", 18);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(420, 0);
        Body.AddChild(_text);
        var h = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        h.AddThemeConstantOverride("separation", 10);
        Body.AddChild(h);
        h.AddChild(UiTheme.Button("Cancelar", Close, 150));
        var yes = UiTheme.Button("Confirmar", () => { Close(); _onYes?.Invoke(); }, 150);
        yes.AddThemeColorOverride("font_color", UiTheme.Danger);
        h.AddChild(yes);
    }

    public void Ask(string text, Action onYes)
    {
        _text.Text = text;
        _onYes = onYes;
        Open();
    }
}

// =====================================================================
/// <summary>Coleção de materiais: descobertos com detalhes, desconhecidos como silhueta.</summary>
public partial class CodexScreen : ModalScreen
{
    GridContainer _grid;
    Label _progress;

    public override void _Ready()
    {
        BuildFrame("COLEÇÃO DE MATERIAIS", new Vector2(900, 560), UiTheme.Violet);
        Layer = 20;
        _progress = UiTheme.Label("", 16, UiTheme.Muted);
        Body.AddChild(_progress);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 400), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Body.AddChild(scroll);
        _grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("h_separation", 10);
        _grid.AddThemeConstantOverride("v_separation", 10);
        scroll.AddChild(_grid);
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        footer.AddChild(UiTheme.Button("Voltar", Close, 160));
    }

    public override void Refresh()
    {
        var gs = GameState.Instance;
        foreach (Node c in _grid.GetChildren()) c.QueueFree();
        int found = 0;
        foreach (var r in GameData.Regions)
        {
            var ids = r.Spawns.Select(s => s.MaterialId).Append(r.RareMaterialId).ToList();
            if (r.Id == "core") ids.Add("heart");
            foreach (var id in ids)
            {
                var m = GameData.Mat(id);
                bool known = gs.Data.Discovered.Contains(id);
                if (known) found++;
                _grid.AddChild(Card(m, r, known));
            }
        }
        _progress.Text = $"Descobertos: {found}/{GameData.Materials.Count}   ·   Cristais minerados: {gs.Data.CrystalsMined}   ·   Fragmentos vendidos: {gs.Data.ShardsSold}";
    }

    Control Card(MaterialDef m, RegionDef r, bool known)
    {
        var gs = GameState.Instance;
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(410, 0) };
        var border = known ? new Color(RarityInfo.Color(m.Rarity), 0.8f) : new Color(1, 1, 1, 0.1f);
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Card, border, 12, 2, 12));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        card.AddChild(h);
        var gem = UiTheme.Label(m.Special ? "✦" : m.Rare ? "★" : "◆", 34, known ? m.Glow.Lightened(0.2f) : new Color(0.3f, 0.3f, 0.4f), true, HorizontalAlignment.Center);
        gem.CustomMinimumSize = new Vector2(44, 0);
        gem.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(gem);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 1);
        h.AddChild(v);
        var titleRow = new HBoxContainer();
        titleRow.AddThemeConstantOverride("separation", 10);
        v.AddChild(titleRow);
        titleRow.AddChild(UiTheme.Label(known ? m.Name : "???", 18, known ? m.Glow.Lightened(0.35f) : UiTheme.Muted, true));
        var badge = new PanelContainer();
        badge.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.02f, 0.03f, 0.06f, 0.9f), RarityInfo.Color(m.Rarity), 6, 1, 8, 1));
        badge.AddChild(UiTheme.Label(RarityInfo.Badge(m.Rarity), 12, RarityInfo.Color(m.Rarity), true));
        badge.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        titleRow.AddChild(badge);
        var desc = UiTheme.Label(known ? m.Description : $"Encontrado em: {r.Name}", 14, UiTheme.Muted);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.CustomMinimumSize = new Vector2(320, 0);
        v.AddChild(desc);
        if (known)
            v.AddChild(UiTheme.Label($"{GameState.FormatMoney(gs.SellPrice(m))}/frag  ·  resistência {GameState.FormatNumber(m.Hp)}  ·  {r.Name}", 14, UiTheme.Cyan));
        return card;
    }
}

// =====================================================================
/// <summary>Tela "Como jogar": controles, ciclo de jogo e mecânicas especiais.</summary>
public partial class HelpScreen : ModalScreen
{
    public override void _Ready()
    {
        BuildFrame("COMO JOGAR", new Vector2(860, 0), UiTheme.Cyan);
        Layer = 20;
        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 28);
        Body.AddChild(cols);

        var left = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) };
        left.AddThemeConstantOverride("separation", 6);
        cols.AddChild(left);
        left.AddChild(UiTheme.Label("CONTROLES", 20, UiTheme.Gold, true));
        foreach (var (k, a) in new[]
                 {
                     ("W A S D", "Mover"), ("Mouse", "Olhar"), ("Botão esquerdo", "Minerar (segure)"),
                     ("E", "Interagir"), ("Shift", "Correr"), ("Espaço", "Pular"),
                     ("Esc", "Pausa / fechar"), ("F5", "Salvar"), ("F3", "Mostrar FPS"),
                 })
        {
            var row = new HBoxContainer();
            var key = UiTheme.Label(k, 16, UiTheme.Cyan, true);
            key.CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(key);
            row.AddChild(UiTheme.Label(a, 16, UiTheme.Text));
            left.AddChild(row);
        }

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        right.AddThemeConstantOverride("separation", 8);
        cols.AddChild(right);
        Section(right, "CICLO", "Minere cristais → os fragmentos voam até você → venda na Estação (verde) → compre melhorias no Terminal (dourado) → abra os portões para regiões mais ricas.");
        Section(right, "RESSONÂNCIA", "Golpes em ritmo constante enchem o arco violeta da mira. Quando ficar dourado, o próximo golpe libera um pulso que atinge todos os cristais próximos.");
        Section(right, "EVENTOS", "Cada região tem condições próprias: Eco Luminoso (cristais renascem), Maré Gélida (fragmentos x2) e Surto do Núcleo (dano +50%).");
        Section(right, "DICA", "Cristais raros (★) pulsam e brilham mais. Valem muito mais que os comuns!");

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        footer.AddChild(UiTheme.Button("Entendi", Close, 180));
    }

    static void Section(VBoxContainer parent, string title, string text)
    {
        parent.AddChild(UiTheme.Label(title, 18, UiTheme.Gold, true));
        var l = UiTheme.Label(text, 15, UiTheme.Text);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(440, 0);
        parent.AddChild(l);
    }
}
