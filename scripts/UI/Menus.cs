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
        Callable.From(FocusFirst).CallDeferred();
    }

    /// <summary>Garante um controle focado para navegação por controle/teclado.</summary>
    protected void FocusFirst()
    {
        if (!Visible) return;
        var current = Root.GetViewport().GuiGetFocusOwner();
        if (current != null && current.IsVisibleInTree() && IsAncestorOf(current)) return;
        var b = FindFocusable(Panel);
        b?.GrabFocus();
    }

    static Control FindFocusable(Node n)
    {
        foreach (Node c in n.GetChildren())
        {
            if (c is BaseButton bb && !bb.Disabled && bb.IsVisibleInTree() && bb.FocusMode != Control.FocusModeEnum.None) return bb;
            var r = FindFocusable(c);
            if (r != null) return r;
        }
        return null;
    }

    public virtual void Close()
    {
        if (!Visible) return;
        Visible = false;
        Closed?.Invoke();
    }

    public virtual void Refresh() { }

    /// <summary>Esconde sem disparar Closed (troca de tela sem devolver o controle ao jogo).</summary>
    public void CloseSilently() => Visible = false;
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
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 520), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Body.AddChild(scroll);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 24);
        grid.AddThemeConstantOverride("v_separation", 3);
        scroll.AddChild(grid);

        AddSlider(grid, "Sensibilidade do mouse", 0.02f, 1.0f, 0.01f, d.MouseSensitivity, v => d.MouseSensitivity = v, v => $"{v * 100:0}");
        AddSlider(grid, "Campo de visão", 60, 100, 1, d.Fov, v => d.Fov = v, v => $"{v:0}°");
        AddSlider(grid, "Volume geral", 0, 1, 0.01f, d.MasterVolume, v => d.MasterVolume = v, v => $"{v * 100:0}%");
        AddSlider(grid, "Música", 0, 1, 0.01f, d.MusicVolume, v => d.MusicVolume = v, v => $"{v * 100:0}%");
        AddSlider(grid, "Efeitos", 0, 1, 0.01f, d.SfxVolume, v => d.SfxVolume = v, v => $"{v * 100:0}%");
        _scale = AddSlider(grid, "Escala de resolução 3D", 0.5f, 1f, 0.05f, d.RenderScale, v =>
        {
            d.RenderScale = v;
            // escolha manual: desliga a resolução dinâmica para respeitar o valor escolhido
            if (d.DynamicResolution) { d.DynamicResolution = false; _dyn?.SetPressedNoSignal(false); }
        }, v => $"{v * 100:0}%");

        grid.AddChild(UiTheme.Label("Qualidade gráfica", 18));
        var q = new OptionButton { CustomMinimumSize = new Vector2(260, 30) };
        q.AddItem("Baixa (PCs modestos)", 0);
        q.AddItem("Média", 1);
        q.AddItem("Alta", 2);
        q.Selected = d.Quality;
        q.ItemSelected += i =>
        {
            // o preset ajusta escala 3D e resolução dinâmica juntos (podem ser alterados depois)
            d.Quality = (int)i;
            bool low = i == 0;
            d.RenderScale = low ? 0.75f : 1f;
            d.DynamicResolution = low;
            _scale.SetValueNoSignal(d.RenderScale);
            _scaleLabel.Text = $"{d.RenderScale * 100:0}%";
            _dyn.SetPressedNoSignal(d.DynamicResolution);
            Changed();
            GameState.Instance.Notify("Qualidade aplicada", $"{q.GetItemText((int)i)} · 3D {d.RenderScale * 100:0}% · resolução dinâmica {(low ? "ligada" : "desligada")}", NotifyKind.Info);
        };
        grid.AddChild(q);

        grid.AddChild(UiTheme.Label("Resolução da janela", 18));
        var res = new OptionButton { CustomMinimumSize = new Vector2(260, 30) };
        foreach (var sz in Settings.WindowSizes) res.AddItem($"{sz.X} × {sz.Y}" + (sz.Y == 720 ? " (recomendado)" : ""));
        res.Selected = d.WindowSize;
        res.ItemSelected += i => { d.WindowSize = (int)i; Changed(); };
        grid.AddChild(res);

        grid.AddChild(UiTheme.Label("Limite de FPS", 18));
        var fps = new OptionButton { CustomMinimumSize = new Vector2(260, 30) };
        int[] fpsVals = { 30, 60, 120, 0 };
        foreach (var f in fpsVals) fps.AddItem(f == 0 ? "Ilimitado" : f.ToString());
        fps.Selected = Math.Max(0, Array.IndexOf(fpsVals, d.MaxFps));
        fps.ItemSelected += i => { d.MaxFps = fpsVals[i]; Changed(); };
        grid.AddChild(fps);

        AddCheck(grid, "Tela cheia", d.Fullscreen, v => d.Fullscreen = v);
        AddCheck(grid, "Sincronização vertical (VSync)", d.VSync, v => d.VSync = v);
        AddCheck(grid, "Inverter eixo Y", d.InvertY, v => d.InvertY = v);
        AddCheck(grid, "Mostrar FPS (F3)", d.ShowFps, v => d.ShowFps = v);
        _dyn = AddCheck(grid, "Resolução dinâmica (~30 FPS)", d.DynamicResolution, v => d.DynamicResolution = v);
        AddCheck(grid, "Atualizações automáticas", d.CheckUpdates, v => d.CheckUpdates = v);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        footer.AddChild(UiTheme.Button("Voltar", Close, 180));
    }

    HSlider _scale;
    Label _scaleLabel;
    CheckButton _dyn;

    void Changed()
    {
        Settings.Apply(GetTree());
        Settings.Save();
    }

    HSlider AddSlider(GridContainer g, string label, float min, float max, float step, float value, Action<float> set, Func<float, string> fmt)
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
        _scaleLabel = v; // o último criado é o da escala 3D (usado pelo preset)
        return s;
    }

    CheckButton AddCheck(GridContainer g, string label, bool value, Action<bool> set)
    {
        g.AddChild(UiTheme.Label(label, 18));
        var c = new CheckButton { ButtonPressed = value, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin };
        foreach (var st in new[] { "normal", "hover", "pressed", "hover_pressed", "focus", "disabled" })
            c.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
        c.Toggled += x => { set(x); AudioManager.Instance?.Play("click", -6f); Changed(); };
        g.AddChild(c);
        return c;
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
                      $"Total ganho: {GameState.FormatMoney(d.TotalEarned)}   ·   Materiais: {d.Discovered.Count}/{GameData.Materials.Count}" +
                      (d.PrestigeCount > 0 ? $"\nRenascimentos: {d.PrestigeCount}   ·   Essência: ✦{d.Essence} (x{GameState.Instance.EssenceMultiplier:0.0})" : "");
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
        var gem = HudIcon.Make(HudIcon.KindFor(m), known ? m.Glow.Lightened(0.2f) : new Color(0.3f, 0.3f, 0.4f), 40);
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
                     ("C / Ctrl", "Agachar"), ("Esc", "Pausa / fechar"), ("F5", "Salvar"), ("F3", "Mostrar FPS"),
                 })
        {
            var row = new HBoxContainer();
            var key = UiTheme.Label(k, 16, UiTheme.Cyan, true);
            key.CustomMinimumSize = new Vector2(150, 0);
            row.AddChild(key);
            row.AddChild(UiTheme.Label(a, 16, UiTheme.Text));
            left.AddChild(row);
        }

        left.AddChild(UiTheme.Spacer(6));
        left.AddChild(UiTheme.Label("CONTROLE", 20, UiTheme.Gold, true));
        var pad = UiTheme.Label("Analógico esq.: mover · Analógico dir.: olhar\nRT: minerar · X: interagir · A: pular · B: agachar\nLB: correr · Start: pausa · B: voltar (menus)", 15, UiTheme.Text);
        left.AddChild(pad);

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(440, 0) };
        right.AddThemeConstantOverride("separation", 8);
        cols.AddChild(right);
        Section(right, "CICLO", "Minere no chão e nas paredes → venda na Estação do acampamento → compre melhorias → abra portões. Explore o relevo e os túneis baixos: agachado (C) você acha câmaras secretas.");
        Section(right, "O TURNO", "Dia de 06:00 às 18:00 (10 min). No fim, só vender e pegar o Elevador. Em casa: melhorias, habilidades e dormir.");
        Section(right, "RESSONÂNCIA", "Golpes em ritmo constante enchem o arco violeta da mira. Quando ficar dourado, o próximo golpe libera um pulso que atinge todos os cristais próximos.");
        Section(right, "EVENTOS", "Cada região tem condições próprias: Eco Luminoso (cristais renascem), Maré Gélida (fragmentos x2) e Surto do Núcleo (dano +50%).");
        Section(right, "RENASCIMENTO", "Depois de concluir a jornada, renasça no Terminal: recomece com Essência Ancestral (+10% de dano e venda por ponto, para sempre).");

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

// =====================================================================
/// <summary>Tela de conclusão da jornada com estatísticas da partida.</summary>
public partial class CompletionScreen : ModalScreen
{
    Label _stats;

    public override void _Ready()
    {
        BuildFrame("", new Vector2(640, 0), UiTheme.Gold);
        Layer = 25;
        Panel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.1f, 0.99f), UiTheme.Gold, 18, 3, 30, 24));
        var icon = HudIcon.Make(HudIcon.Kind.Star, UiTheme.Gold, 64);
        icon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        Body.AddChild(icon);
        Body.AddChild(UiTheme.Shadowed(UiTheme.Label("MESTRE ESCAVADOR", 40, UiTheme.Gold, true, HorizontalAlignment.Center), 8));
        var sub = UiTheme.Label("Você atravessou as quatro regiões e destruiu o Coração Ancestral.", 18, UiTheme.Text, false, HorizontalAlignment.Center);
        sub.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        sub.CustomMinimumSize = new Vector2(560, 0);
        Body.AddChild(sub);
        Body.AddChild(new ColorRect { Color = new Color(UiTheme.Gold, 0.35f), CustomMinimumSize = new Vector2(0, 2) });
        _stats = UiTheme.Label("", 17, UiTheme.Text, false, HorizontalAlignment.Center);
        Body.AddChild(_stats);
        var note = UiTheme.Label("O jogo continua: complete a Coleção e maximize suas melhorias.", 15, UiTheme.Muted, false, HorizontalAlignment.Center);
        Body.AddChild(note);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        Body.AddChild(row);
        var b = UiTheme.Button("Continuar minerando", Close, 280, 22);
        row.AddChild(b);
        _prestigeBtn = UiTheme.Button("Renascer agora", () => UiBus.RequestPrestige(), 280, 22);
        _prestigeBtn.AddThemeColorOverride("font_color", UiTheme.Gold);
        row.AddChild(_prestigeBtn);
        row.AddThemeConstantOverride("separation", 12);
    }

    Button _prestigeBtn;

    public override void Open()
    {
        base.Open();
        Audio.AudioManager.Instance?.Play("unlock", 0f);
        Audio.AudioManager.Instance?.Play("rare", -3f);
    }

    public override void Refresh()
    {
        var d = GameState.Instance.Data;
        var t = TimeSpan.FromSeconds(d.CompletedAtSeconds > 0 ? d.CompletedAtSeconds : d.PlayTimeSeconds);
        int maxed = UpgradeIds.All.Count(id => GameState.Instance.IsMaxed(id));
        _prestigeBtn.Text = $"Renascer (+{GameState.Instance.PendingEssence} Essência)";
        _prestigeBtn.Disabled = !GameState.Instance.CanPrestige;
        _stats.Text =
            $"Tempo de jornada: {(int)t.TotalHours}h {t.Minutes:00}min\n" +
            $"Cristais minerados: {GameState.FormatNumber(d.CrystalsMined)}   ·   Fragmentos vendidos: {GameState.FormatNumber(d.ShardsSold)}\n" +
            $"Total ganho: {GameState.FormatMoney(d.TotalEarned)}\n" +
            $"Materiais descobertos: {d.Discovered.Count}/{GameData.Materials.Count}   ·   Melhorias no máximo: {maxed}/{UpgradeIds.All.Length}\n" +
            $"Ferramenta: {GameState.ToolTierNames[GameState.Instance.ToolTier]}";
    }
}

// =====================================================================
/// <summary>Pergunta com duas escolhas (ex.: pagar o goblin). Esc conta como a segunda opção.</summary>
public partial class ChoiceScreen : ModalScreen
{
    Label _title, _text;
    Button _yes, _no;
    Action<bool> _cb;

    public override void _Ready()
    {
        BuildFrame("", new Vector2(560, 0), new Color(0.6f, 1f, 0.5f));
        Layer = 31;
        _title = UiTheme.Label("", 26, new Color(0.6f, 1f, 0.5f), true);
        Body.AddChild(_title);
        _text = UiTheme.Label("", 18);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.CustomMinimumSize = new Vector2(500, 0);
        Body.AddChild(_text);
        var h = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        h.AddThemeConstantOverride("separation", 10);
        Body.AddChild(h);
        _no = UiTheme.Button("", () => Choose(false), 200);
        _yes = UiTheme.Button("", () => Choose(true), 220);
        _yes.AddThemeColorOverride("font_color", UiTheme.Gold);
        h.AddChild(_no);
        h.AddChild(_yes);
    }

    void Choose(bool yes)
    {
        var cb = _cb;
        _cb = null;
        CloseSilently();
        cb?.Invoke(yes);
    }

    public void Ask(string title, string text, string yes, string no, bool yesEnabled, Action<bool> cb)
    {
        _title.Text = title;
        _text.Text = text;
        _yes.Text = yes;
        _no.Text = no;
        _yes.Disabled = !yesEnabled;
        _cb = cb;
        Open();
    }

    public void Decline() => Choose(false);
}

// =====================================================================
/// <summary>Aviso de nova versão com download e aplicação automáticos.</summary>
public partial class UpdateScreen : ModalScreen
{
    public UpdateManager Updater;
    Label _title, _notes, _status;
    ProgressBar _bar;
    Button _updateBtn, _laterBtn;

    public override void _Ready()
    {
        BuildFrame("", new Vector2(600, 0), UiTheme.Cyan);
        Layer = 40;
        var icon = HudIcon.Make(HudIcon.Kind.Gem, UiTheme.Cyan, 54);
        icon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
        Body.AddChild(icon);
        _title = UiTheme.Label("NOVA VERSÃO DISPONÍVEL", 30, UiTheme.Cyan, true, HorizontalAlignment.Center);
        Body.AddChild(_title);
        _notes = UiTheme.Label("", 16, UiTheme.Text, false, HorizontalAlignment.Center);
        _notes.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _notes.CustomMinimumSize = new Vector2(540, 0);
        Body.AddChild(_notes);
        _bar = UiTheme.Bar(UiTheme.Cyan, 540, 12);
        _bar.Visible = false;
        Body.AddChild(_bar);
        _status = UiTheme.Label("", 14, UiTheme.Muted, false, HorizontalAlignment.Center);
        Body.AddChild(_status);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 12);
        Body.AddChild(row);
        _laterBtn = UiTheme.Button("Depois", Close, 200, 20);
        _updateBtn = UiTheme.Button("Atualizar agora", OnUpdate, 260, 20);
        _updateBtn.AddThemeColorOverride("font_color", UiTheme.Success);
        row.AddChild(_laterBtn);
        row.AddChild(_updateBtn);
    }

    public void Show(UpdateManifest m)
    {
        string notes = string.IsNullOrWhiteSpace(m.Notes) ? "" : "\n\n" + m.Notes;
        _notes.Text = $"Versão instalada: v{UpdateManager.CurrentVersion}   →   nova: v{m.Version}{notes}";
        if (Updater.NeedsFullInstaller)
        {
            _status.Text = "Esta versão usa um motor novo e precisa do instalador completo.";
            _updateBtn.Text = "Baixar instalador";
        }
        else
        {
            long total = 0; foreach (var f in m.Files) total += f.Size;
            _status.Text = $"Download de {total / 1048576.0:0.0} MB. O jogo fecha, atualiza e reabre sozinho. Seus saves são mantidos.";
            _updateBtn.Text = "Atualizar agora";
        }
        _bar.Visible = false;
        _updateBtn.Disabled = false;
        _laterBtn.Disabled = false;
        Open();
        _updateBtn.GrabFocus();
    }

    void OnUpdate()
    {
        if (Updater.NeedsFullInstaller)
        {
            OS.ShellOpen(string.IsNullOrEmpty(Updater.Available.SetupUrl) ? "https://github.com/ArtCurvelo/Crystal/tree/main/dist" : Updater.Available.SetupUrl);
            return;
        }
        _updateBtn.Disabled = true;
        _laterBtn.Disabled = true;
        _bar.Visible = true;
        Updater.DownloadAndApply();
    }

    public void OnProgress(float p, string text) { _bar.Value = p; _status.Text = text; }

    public void OnFailed(string msg)
    {
        _status.Text = msg;
        _status.AddThemeColorOverride("font_color", UiTheme.Danger);
        _updateBtn.Disabled = false;
        _laterBtn.Disabled = false;
        _updateBtn.Text = "Tentar de novo";
    }
}

// =====================================================================
/// <summary>Lista os backups do save e permite restaurar um deles.</summary>
public partial class BackupScreen : ModalScreen
{
    public Action<SaveSystem.SnapshotInfo> OnRestore;
    VBoxContainer _list;

    public override void _Ready()
    {
        BuildFrame("RESTAURAR PROGRESSO", new Vector2(760, 0), UiTheme.Gold);
        Layer = 22;
        var info = UiTheme.Label("Backups são criados ao iniciar cada partida, antes de atualizações e antes de um novo jogo. Os 10 mais recentes ficam guardados.", 14, UiTheme.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        info.CustomMinimumSize = new Vector2(700, 0);
        Body.AddChild(info);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 380), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Body.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        footer.AddChild(UiTheme.Button("Voltar", Close, 160));
    }

    public override void Refresh()
    {
        foreach (Node c in _list.GetChildren()) c.QueueFree();
        var snaps = SaveSystem.ListSnapshots();
        if (snaps.Count == 0) _list.AddChild(UiTheme.Label("Nenhum backup ainda.", 16, UiTheme.Muted));
        foreach (var s in snaps)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(UiTheme.Card, new Color(UiTheme.Gold, 0.4f), 10, 2, 14, 8));
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 12);
            row.AddChild(h);
            var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            h.AddChild(v);
            var t = TimeSpan.FromSeconds(s.Data.PlayTimeSeconds);
            string when = s.When == default ? "data desconhecida" : s.When.ToString("dd/MM/yyyy HH:mm");
            v.AddChild(UiTheme.Label($"{when}   ·   {s.Reason.Replace('-', ' ')}", 16, UiTheme.Text, true));
            v.AddChild(UiTheme.Label($"{GameState.FormatMoney(s.Data.Money)}   ·   {(int)t.TotalHours}h{t.Minutes:00} de jogo   ·   {s.Data.UnlockedRegions.Count}/{GameData.Regions.Count} regiões   ·   ✦{s.Data.Essence}", 14, UiTheme.Muted));
            var snap = s;
            var b = UiTheme.Button("Restaurar", () => OnRestore?.Invoke(snap), 150, 17);
            b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            h.AddChild(b);
            _list.AddChild(row);
        }
    }
}
