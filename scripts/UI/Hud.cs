using System;
using System.Collections.Generic;
using System.Linq;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>
/// HUD sci-fi: título, região atual, créditos e capacidade, objetivo, painel do alvo com cantoneiras,
/// barra de fragmentos, energia de ressonância, notificações, eventos e prompts.
/// </summary>
public partial class Hud : CanvasLayer
{
    public PlayerController Player;
    public World.RegionEvents Events;

    Control _root;
    PanelContainer _clockPanel, _hpPanel, _bossPanel;
    Label _bossName;
    ProgressBar _bossBar;
    Label _hpLabel;
    ProgressBar _hpBar;
    ColorRect _damageFlash;

    /// <summary>Clarão vermelho nas bordas ao levar dano.</summary>
    public void FlashDamage()
    {
        if (_damageFlash == null)
        {
            _damageFlash = new ColorRect { Color = new Color(0.9f, 0.05f, 0.05f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
            _damageFlash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _root.AddChild(_damageFlash);
        }
        _damageFlash.Color = new Color(0.9f, 0.05f, 0.05f, 0.35f);
        var tw = _damageFlash.CreateTween();
        tw.TweenProperty(_damageFlash, "color:a", 0f, 0.5f);
    }

    bool _measuring;
    float _fpsTimer;

    /// <summary>Contador F3: FPS, escala 3D, renderizador, qualidade e quanto o processador e a placa de vídeo levam por quadro
    /// (diz qual dos dois segura o jogo).</summary>
    void UpdateFpsText(float dt)
    {
        var rid = GetViewport().GetViewportRid();
        if (!_measuring) { RenderingServer.ViewportSetMeasureRenderTime(rid, true); _measuring = true; }
        if ((_fpsTimer -= dt) > 0) return;
        _fpsTimer = 0.5f;
        double cpu = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0 + RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid)
                     + RenderingServer.GetFrameSetupTimeCpu();
        double gpu = RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid);
        string method = Core.Rendering.RenderMethods.Current switch { Core.Rendering.RenderMethods.Compatibility => "OpenGL", Core.Rendering.RenderMethods.Mobile => "Vulkan Mobile", _ => "Vulkan Forward+" };
        string q = Loc.T(Settings.Quality switch { GraphicsQuality.Low => "Baixa", GraphicsQuality.Medium => "Média", _ => "Alta" });
        _fps.Text = Loc.F("{0:0} FPS · 3D {1:0}%\n{2} · {3}\nCPU {4:0.0} ms · GPU {5:0.0} ms", Engine.GetFramesPerSecond(), GetViewport().Scaling3DScale * 100, method, q, cpu, gpu);
    }

    void UpdateHp()
    {
        if (Player == null || !IsInstanceValid(Player)) return;
        bool danger = World.GoblinCamp.AnyRebellion;
        bool show = danger || Player.Hp < PlayerController.MaxHp - 0.5f;
        if (_hpPanel.Visible != show) _hpPanel.Visible = show;
        if (!show) return;
        _hpBar.Value = Player.Hp / PlayerController.MaxHp;
        _hpLabel.Text = Loc.F("Vida: {0:0} / {1:0}", Player.Hp, PlayerController.MaxHp) + (danger ? Loc.T("   ⚠ REBELIÃO") : "");
    }
    HudIcon _clockIcon;
    Label _clock;
    ProgressBar _clockBar;
    string _clockText;
    bool _clockOver, _inHouse;

    void UpdateClock()
    {
        var gs = GameState.Instance;
        bool over = gs.DayOver;
        string t = over ? Loc.F("Dia {0} · FIM DO TURNO", gs.Data.Day) : Loc.F("Dia {0} · {1}", gs.Data.Day, gs.ClockText);
        if (t != _clockText) { _clockText = t; _clock.Text = t; }
        _clockBar.Value = gs.DayProgress;
        bool late = !over && gs.DayRemaining <= GameState.DayWarningSeconds;
        bool inHouse = Player != null && IsInstanceValid(Player) && (World.PlayerHouse.Contains(Player.GlobalPosition) || World.Town.Contains(Player.GlobalPosition));
        if (over != _clockOver || inHouse != _inHouse) { _clockOver = over; _inHouse = inHouse; RefreshObjective(); }
        var col = over ? UiTheme.Danger : late ? new Color(1f, 0.6f, 0.25f) : UiTheme.Text;
        _clock.Modulate = (over || late) ? new Color(1, 1, 1, 0.75f + 0.25f * Mathf.Abs(Mathf.Sin(Time.GetTicksMsec() / 250f))) : Colors.White;
        _clock.AddThemeColorOverride("font_color", col);
    }

    Label _debt;
    Label _money, _moneyGain, _capLabel, _prompt, _regionName, _regionSub, _fps, _objective, _keys;
    ProgressBar _capBar;
    PanelContainer _promptPanel, _objectivePanel, _regionTag, _capPanel;
    HudIcon _regionIcon;
    Crosshair _crosshair;
    VBoxContainer _notifs;
    // alvo
    Control _targetBox;
    BracketFrame _brackets;
    Label _targetName, _targetRarity, _targetPct, _targetYield;
    HudIcon _targetGem;
    ProgressBar _targetHp;
    // barra de fragmentos
    HBoxContainer _hotbar;
    readonly List<(PanelContainer panel, HudIcon gem, Label count)> _slots = new();
    // energia / ressonância
    Label _toolName, _toolStats, _energyPct, _pulseInfo;
    PanelContainer _energyPanel;
    int _lastWear = -1;
    bool _lastBroken;
    ProgressBar _energyBar;
    // eventos
    PanelContainer _eventPanel;
    Label _eventTitle, _eventDesc;
    ProgressBar _eventBar;
    World.RegionEvents.EventDef _shownEvent;

    Label _saveIcon;
    long _displayMoney, _shownMoney, _gainAccum;
    float _gainTimer, _fullFlash;
    double _lastFullWarn = -100;
    readonly Queue<(string, string, NotifyKind)> _pending = new();
    const int SlotCount = 6;

    public override void _Ready()
    {
        Layer = 5;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Get() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        BuildTopLeft();
        BuildTopCenter();
        BuildTopRight();
        BuildCenter();
        BuildBottom();

        var gs = GameState.Instance;
        gs.MoneyChanged += OnMoney;
        gs.InventoryChanged += OnInventory;
        gs.UpgradesChanged += OnInventory;
        gs.UpgradesChanged += RefreshStats;
        gs.RegionsChanged += RefreshObjective;
        gs.ContractsChanged += RefreshObjective;
        gs.Notified += OnNotified;
        gs.BigMessage += OnBigMessage;
        gs.Saved += OnSaved;
        ResetMoneyDisplay();
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs == null) return;
        gs.MoneyChanged -= OnMoney;
        gs.InventoryChanged -= OnInventory;
        gs.UpgradesChanged -= OnInventory;
        gs.UpgradesChanged -= RefreshStats;
        gs.RegionsChanged -= RefreshObjective; GameState.Instance.ContractsChanged -= RefreshObjective;
        gs.Notified -= OnNotified;
        gs.BigMessage -= OnBigMessage;
        gs.Saved -= OnSaved;
        InputHints.Changed -= OnHintsChanged;
    }

    void OnHintsChanged() => _keys.Text = InputHints.Footer;

    void OnSaved(bool auto)
    {
        if (!IsInsideTree()) return;
        var tw = _saveIcon.CreateTween();
        tw.TweenProperty(_saveIcon, "modulate:a", 1f, 0.2f);
        tw.TweenInterval(1.2f);
        tw.TweenProperty(_saveIcon, "modulate:a", 0f, 0.5f);
    }

    // ============================ Construção ============================
    void BuildTopLeft()
    {
        // (o selo "Crystal Excavator" saiu: a tela fica mais limpa)
        _objectivePanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _objectivePanel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.09f, 0.62f), new Color(UiTheme.Cyan, 0.3f), 8, 1, 12, 8));
        UiTheme.Place(_objectivePanel, Control.LayoutPreset.TopLeft, new Vector2(18, 16));
        _root.AddChild(_objectivePanel);
        _objective = UiTheme.Label("", 14, UiTheme.Text);
        _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _objective.CustomMinimumSize = new Vector2(250, 0);
        _objectivePanel.AddChild(_objective);
    }

    void BuildTopCenter()
    {
        _regionTag = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) }; // só aparece ao entrar num local
        _regionTag.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(null, null, 14, 2, 22, 6));
        UiTheme.Place(_regionTag, Control.LayoutPreset.CenterTop, new Vector2(-190, 12), new Vector2(380, 0));
        _root.AddChild(_regionTag);
        var h = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        h.AddThemeConstantOverride("separation", 12);
        _regionTag.AddChild(h);
        _regionIcon = HudIcon.Make(HudIcon.Kind.Region, UiTheme.Cyan, 38);
        h.AddChild(_regionIcon);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", -2);
        h.AddChild(v);
        _regionName = UiTheme.Label("", 24, UiTheme.Text, true);
        _regionSub = UiTheme.Label("", 13, UiTheme.Muted);
        v.AddChild(_regionName);
        v.AddChild(_regionSub);

        // evento de região logo abaixo
        _eventPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiTheme.Place(_eventPanel, Control.LayoutPreset.CenterTop, new Vector2(-170, 86), new Vector2(340, 0));
        _root.AddChild(_eventPanel);
        var ev = new VBoxContainer();
        ev.AddThemeConstantOverride("separation", 2);
        _eventPanel.AddChild(ev);
        _eventTitle = UiTheme.Label("", 18, UiTheme.Text, true, HorizontalAlignment.Center);
        _eventDesc = UiTheme.Label("", 13, UiTheme.Text, false, HorizontalAlignment.Center);
        _eventBar = UiTheme.Bar(UiTheme.Cyan, 310, 6);
        ev.AddChild(_eventTitle);
        ev.AddChild(_eventDesc);
        ev.AddChild(_eventBar);
    }

    void BuildTopRight()
    {
        // painel compacto: só ícone + valor (Lúmens, mochila, dia)
        var col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", 6);
        UiTheme.Place(col, Control.LayoutPreset.TopRight, new Vector2(-300, 14), new Vector2(282, 0));
        col.GrowHorizontal = Control.GrowDirection.Begin; // avisos mais largos crescem para a esquerda, nunca para fora da tela
        _root.AddChild(col);
        var stats = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd, CustomMinimumSize = new Vector2(210, 0) };
        stats.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.09f, 0.7f), new Color(UiTheme.Cyan, 0.35f), 10, 1, 12, 8));
        col.AddChild(stats);
        var sv = new VBoxContainer();
        sv.AddThemeConstantOverride("separation", 5);
        stats.AddChild(sv);

        var mh = new HBoxContainer();
        mh.AddThemeConstantOverride("separation", 8);
        sv.AddChild(mh);
        mh.AddChild(HudIcon.Make(HudIcon.Kind.Coin, UiTheme.Gold, 24));
        _money = UiTheme.Label("0", 21, UiTheme.Gold, true);
        _money.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _money.HorizontalAlignment = HorizontalAlignment.Right;
        _money.TooltipText = Loc.T("Lúmens");
        mh.AddChild(_money);
        _debt = UiTheme.Label("", 14, new Color(1f, 0.45f, 0.4f), true, HorizontalAlignment.Right);
        _debt.Visible = false;
        sv.AddChild(_debt);
        _moneyGain = UiTheme.Shadowed(UiTheme.Label("", 15, UiTheme.Success, true, HorizontalAlignment.Right));
        _moneyGain.Visible = false;

        _capPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _capPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        sv.AddChild(_capPanel);
        var ch = new HBoxContainer();
        ch.AddThemeConstantOverride("separation", 8);
        _capPanel.AddChild(ch);
        ch.AddChild(HudIcon.Make(HudIcon.Kind.Bag, new Color(0.62f, 0.72f, 0.85f), 24));
        var cv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cv.AddThemeConstantOverride("separation", 2);
        ch.AddChild(cv);
        _capLabel = UiTheme.Label("0 / 30", 15, UiTheme.Text, true, HorizontalAlignment.Right);
        cv.AddChild(_capLabel);
        _capBar = UiTheme.Bar(UiTheme.Cyan, 150, 5);
        cv.AddChild(_capBar);

        _clockPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _clockPanel.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        sv.AddChild(_clockPanel);
        var kh = new HBoxContainer();
        kh.AddThemeConstantOverride("separation", 8);
        _clockPanel.AddChild(kh);
        _clockIcon = HudIcon.Make(HudIcon.Kind.Clock, UiTheme.Gold, 24);
        kh.AddChild(_clockIcon);
        var kv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        kv.AddThemeConstantOverride("separation", 2);
        kh.AddChild(kv);
        _clock = UiTheme.Label("", 15, UiTheme.Text, true, HorizontalAlignment.Right);
        kv.AddChild(_clock);
        _clockBar = UiTheme.Bar(UiTheme.Gold, 150, 4);
        kv.AddChild(_clockBar);
        col.AddChild(_moneyGain);

        // barra do chefão (Mestre Corvo), no alto e no centro
        _bossPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _bossPanel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.08f, 0.02f, 0.03f, 0.88f), new Color(1f, 0.25f, 0.2f, 0.9f), 12, 2, 18, 8));
        UiTheme.Place(_bossPanel, Control.LayoutPreset.CenterTop, new Vector2(-330, 96), new Vector2(660, 0));
        _root.AddChild(_bossPanel);
        var bv = new VBoxContainer();
        bv.AddThemeConstantOverride("separation", 4);
        _bossPanel.AddChild(bv);
        _bossName = UiTheme.Label("", 20, new Color(1f, 0.55f, 0.45f), true, HorizontalAlignment.Center);
        bv.AddChild(_bossName);
        _bossBar = UiTheme.Bar(new Color(1f, 0.25f, 0.2f), 620, 14);
        bv.AddChild(_bossBar);

        // vida (aparece quando há perigo ou dano)
        _hpPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _hpPanel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(null, new Color(UiTheme.Danger, 0.8f), 12, 2, 16, 6));
        col.AddChild(_hpPanel);
        var hv = new VBoxContainer();
        hv.AddThemeConstantOverride("separation", 4);
        _hpPanel.AddChild(hv);
        _hpLabel = UiTheme.Label("Vida", 16, UiTheme.Text, true);
        hv.AddChild(_hpLabel);
        _hpBar = UiTheme.Bar(UiTheme.Danger, 250, 9);
        hv.AddChild(_hpBar);

        _notifs = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _notifs.AddThemeConstantOverride("separation", 8);
        col.AddChild(_notifs);
    }

    void BuildCenter()
    {
        _crosshair = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiTheme.Place(_crosshair, Control.LayoutPreset.Center, new Vector2(-36, -36), new Vector2(72, 72));
        _root.AddChild(_crosshair);

        // painel do alvo acima da mira, com cantoneiras
        _targetBox = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiTheme.Place(_targetBox, Control.LayoutPreset.Center, new Vector2(-175, -205), new Vector2(350, 150));
        _root.AddChild(_targetBox);
        _brackets = new BracketFrame();
        _brackets.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _targetBox.AddChild(_brackets);
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.1f, 0.82f), new Color(1, 1, 1, 0.12f), 10, 1, 14, 8));
        UiTheme.Place(panel, Control.LayoutPreset.FullRect, new Vector2(12, 12));
        panel.OffsetRight = -12; panel.OffsetBottom = -12;
        _targetBox.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 4);
        panel.AddChild(v);
        var nameRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        nameRow.AddThemeConstantOverride("separation", 8);
        v.AddChild(nameRow);
        nameRow.AddChild(HudIcon.Make(HudIcon.Kind.Pick, UiTheme.Text, 20));
        _targetName = UiTheme.Label("", 20, UiTheme.Text, true);
        nameRow.AddChild(_targetName);
        _targetRarity = UiTheme.Label("", 14, UiTheme.Gold, false, HorizontalAlignment.Center);
        v.AddChild(_targetRarity);
        var barRow = new PanelContainer();
        barRow.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.02f, 0.03f, 0.06f, 0.9f), new Color(1, 1, 1, 0.25f), 6, 1, 6, 3));
        v.AddChild(barRow);
        var bh = new HBoxContainer();
        bh.AddThemeConstantOverride("separation", 8);
        barRow.AddChild(bh);
        _targetHp = UiTheme.Bar(UiTheme.Violet, 230, 14);
        _targetHp.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        _targetHp.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bh.AddChild(_targetHp);
        _targetPct = UiTheme.Label("0%", 18, UiTheme.Text, true, HorizontalAlignment.Right);
        _targetPct.CustomMinimumSize = new Vector2(52, 0);
        bh.AddChild(_targetPct);
        var yRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        yRow.AddThemeConstantOverride("separation", 6);
        v.AddChild(yRow);
        yRow.AddChild(UiTheme.Label("Rendimento est.:", 14, UiTheme.Muted));
        _targetGem = HudIcon.Make(HudIcon.Kind.Gem, UiTheme.Cyan, 20);
        yRow.AddChild(_targetGem);
        _targetYield = UiTheme.Label("", 15, UiTheme.Text, true);
        yRow.AddChild(_targetYield);

        // prompt de interação
        _promptPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _promptPanel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.1f, 0.85f), UiTheme.Gold, 10, 2, 18, 8));
        UiTheme.Place(_promptPanel, Control.LayoutPreset.Center, new Vector2(-150, 70));
        _root.AddChild(_promptPanel);
        _prompt = UiTheme.Label("", 19, UiTheme.Text, true, HorizontalAlignment.Center);
        _promptPanel.AddChild(_prompt);
    }

    void BuildBottom()
    {
        // barra de fragmentos (canto inferior esquerdo)
        _hotbar = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hotbar.AddThemeConstantOverride("separation", 6);
        UiTheme.Place(_hotbar, Control.LayoutPreset.BottomLeft, new Vector2(20, -86));
        _root.AddChild(_hotbar);
        for (int i = 0; i < SlotCount; i++)
        {
            var p = new PanelContainer { CustomMinimumSize = new Vector2(66, 66), MouseFilter = Control.MouseFilterEnum.Ignore };
            p.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.1f, 0.75f), new Color(UiTheme.Cyan, 0.35f), 10, 2, 4, 4));
            var ov = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            p.AddChild(ov);
            var gem = HudIcon.Make(HudIcon.Kind.Gem, UiTheme.Cyan, 40);
            UiTheme.Place(gem, Control.LayoutPreset.Center, new Vector2(-20, -24), new Vector2(40, 40));
            ov.AddChild(gem);
            var count = UiTheme.Shadowed(UiTheme.Label("", 17, UiTheme.Text, true, HorizontalAlignment.Right), 5);
            UiTheme.Place(count, Control.LayoutPreset.BottomRight, new Vector2(-50, -24), new Vector2(46, 0));
            ov.AddChild(count);
            _hotbar.AddChild(p);
            _slots.Add((p, gem, count));
        }

        // energia de ressonância e ferramenta (canto inferior direito)
        var energy = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        energy.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(null, null, 12, 2, 16, 8));
        UiTheme.Place(energy, Control.LayoutPreset.BottomRight, new Vector2(-342, -132), new Vector2(320, 0));
        _root.AddChild(energy);
        _energyPanel = energy;
        var ev = new VBoxContainer();
        ev.AddThemeConstantOverride("separation", 4);
        energy.AddChild(ev);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        ev.AddChild(row);
        row.AddChild(HudIcon.Make(HudIcon.Kind.Bolt, UiTheme.Cyan, 30));
        var rv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        rv.AddThemeConstantOverride("separation", 3);
        row.AddChild(rv);
        var title = new HBoxContainer();
        rv.AddChild(title);
        var t = UiTheme.Label("Ressonância:", 18, UiTheme.Text, true);
        t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        title.AddChild(t);
        _energyPct = UiTheme.Label("0%", 18, UiTheme.Text, true);
        title.AddChild(_energyPct);
        _energyBar = UiTheme.Bar(UiTheme.Cyan, 230, 9);
        rv.AddChild(_energyBar);
        _pulseInfo = UiTheme.Label("", 13, UiTheme.Muted);
        ev.AddChild(_pulseInfo);

        var tool = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        tool.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(null, new Color(UiTheme.Cyan, 0.5f), 10, 2, 14, 6));
        UiTheme.Place(tool, Control.LayoutPreset.BottomRight, new Vector2(-342, -56), new Vector2(320, 0));
        _root.AddChild(tool);
        var th = new HBoxContainer();
        th.AddThemeConstantOverride("separation", 8);
        tool.AddChild(th);
        _toolName = UiTheme.Label("", 16, UiTheme.Cyan, true);
        _toolName.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        th.AddChild(_toolName);
        _toolStats = UiTheme.Label("", 14, UiTheme.Text);
        th.AddChild(_toolStats);

        _keys = UiTheme.Shadowed(UiTheme.Label(InputHints.Footer, 13, UiTheme.Muted, false, HorizontalAlignment.Center));
        // a linha de controles ajuda no começo e depois some (volta no menu de pausa / ajuda)
        var kt = _keys.CreateTween();
        kt.TweenInterval(150f);
        kt.TweenProperty(_keys, "modulate:a", 0f, 2f);
        InputHints.Changed += OnHintsChanged;
        UiTheme.Place(_keys, Control.LayoutPreset.CenterBottom, new Vector2(-300, -30), new Vector2(600, 0));
        _root.AddChild(_keys);

        // barra do modo construção
        _buildPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiTheme.Place(_buildPanel, Control.LayoutPreset.CenterBottom, new Vector2(-320, -190), new Vector2(640, 0));
        _root.AddChild(_buildPanel);
        var bv = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        bv.AddThemeConstantOverride("separation", 2);
        _buildPanel.AddChild(bv);
        _buildHead = UiTheme.Label("MODO CONSTRUÇÃO", 13, UiTheme.Muted, true);
        bv.AddChild(_buildHead);
        _buildTitle = UiTheme.Label("", 20, UiTheme.Text, true);
        bv.AddChild(_buildTitle);
        _buildDetail = UiTheme.Label("", 14, UiTheme.Text);
        _buildDetail.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _buildDetail.CustomMinimumSize = new Vector2(610, 0);
        bv.AddChild(_buildDetail);

        _saveIcon = UiTheme.Shadowed(UiTheme.Label("◆ Salvando…", 14, UiTheme.Cyan));
        _saveIcon.Modulate = new Color(1, 1, 1, 0);
        UiTheme.Place(_saveIcon, Control.LayoutPreset.BottomLeft, new Vector2(24, -112));
        _root.AddChild(_saveIcon);

        _fps = UiTheme.Shadowed(UiTheme.Label("", 14, UiTheme.Success));
        UiTheme.Place(_fps, Control.LayoutPreset.BottomRight, new Vector2(-170, -164), new Vector2(150, 0));
        _fps.HorizontalAlignment = HorizontalAlignment.Right;
        _fps.Position += new Vector2(-150, -40);
        _fps.Size = new Vector2(300, 0);
        _root.AddChild(_fps);
    }

    // ============================ Atualizações ============================
    public void ResetMoneyDisplay()
    {
        _displayMoney = _shownMoney = GameState.Instance.Data.Money;
        _money.Text = GameState.FormatNumber(_displayMoney);
        OnInventory();
        RefreshStats();
        RefreshObjective();
    }

    void OnMoney()
    {
        long m = GameState.Instance.Data.Money;
        if (m > _shownMoney)
        {
            _gainAccum += m - _shownMoney;
            _gainTimer = 2.2f;
            _moneyGain.Text = "+" + GameState.FormatNumber(_gainAccum);
            _moneyGain.Visible = true;
            _moneyGain.Modulate = Colors.White;
            _money.PivotOffset = _money.Size / 2;
            var tw = _money.CreateTween();
            tw.TweenProperty(_money, "scale", new Vector2(1.12f, 1.12f), 0.08f);
            tw.TweenProperty(_money, "scale", Vector2.One, 0.18f);
        }
        _shownMoney = m;
        RefreshObjective();
    }

    void OnInventory()
    {
        var gs = GameState.Instance;
        int cnt = gs.InventoryCount, cap = gs.Capacity;
        bool full = cnt >= cap;
        _capLabel.Text = full ? Loc.F("{0} / {1} · CHEIA", cnt, cap) : $"{cnt} / {cap}";
        _capLabel.AddThemeColorOverride("font_color", full ? UiTheme.Danger : UiTheme.Text);
        _capBar.Value = cap > 0 ? (double)cnt / cap : 0;
        UiTheme.SetBarColor(_capBar, full ? UiTheme.Danger : cnt > cap * 0.75f ? UiTheme.Gold : UiTheme.Cyan);

        // barra de fragmentos: materiais mais valiosos primeiro
        var items = gs.Data.Inventory.Where(kv => kv.Value > 0 && GameData.Mat(kv.Key) != null)
            .OrderByDescending(kv => GameData.Mat(kv.Key).Value).Take(SlotCount).ToList();
        for (int i = 0; i < SlotCount; i++)
        {
            var (panel, gem, count) = _slots[i];
            if (i < items.Count)
            {
                var m = GameData.Mat(items[i].Key);
                gem.Visible = true;
                gem.SetOre(m);
                count.Text = items[i].Value.ToString();
                panel.AddThemeStyleboxOverride("panel", UiTheme.ChamferCached(new Color(0.03f, 0.05f, 0.1f, 0.8f), new Color(m.Glow, 0.6f), 10, 2, 4, 4));
                panel.TooltipText = m.Name;
            }
            else
            {
                gem.Visible = false;
                count.Text = "";
                panel.AddThemeStyleboxOverride("panel", UiTheme.ChamferCached(new Color(0.03f, 0.05f, 0.1f, 0.55f), new Color(UiTheme.Cyan, 0.2f), 10, 2, 4, 4));
            }
        }
        RefreshObjective();
    }

    bool _lastWeaponMode;
    void RefreshStats()
    {
        var gs = GameState.Instance;
        _lastWeaponMode = Player != null && IsInstanceValid(Player) && Player.WeaponMode;
        if (_lastWeaponMode && gs.CurrentWeapon is { } w)
        {
            _toolName.Text = w.Name;
            _toolName.AddThemeColorOverride("font_color", new Color(1f, 0.6f, 0.45f));
            _toolStats.Text = Loc.F("{0} dano · {1:0.0}/s · [{2}] ferramenta", GameState.FormatNumber(gs.WeaponDamage), w.Speed, InputHints.Key("swap_weapon"));
            _lastWear = gs.ToolDurabilityLeft + gs.Data.Ammo * 100000;
            _lastBroken = gs.ToolBroken;
            return;
        }
        _toolName.Text = gs.ToolLabel + (gs.Data.Essence > 0 ? $"  ✦{gs.Data.Essence}" : "");
        _toolName.AddThemeColorOverride("font_color", gs.UsingHands ? UiTheme.Danger : (gs.IsLaser ? gs.Data.Ammo <= 20 : gs.ToolDurability01 < 0.2f) ? UiTheme.Gold : UiTheme.Cyan);
        _toolStats.Text = gs.ToolBroken
            ? Loc.F("mãos: {0} dano · compre outra no Terminal", GameState.FormatNumber(gs.Damage))
            : gs.OutOfAmmo ? Loc.F("mãos: {0} dano · compre munição no Terminal", GameState.FormatNumber(gs.Damage))
            : gs.IsLaser ? Loc.F("{0} dano · {1:0.0}/s · {2} células", GameState.FormatNumber(gs.Damage), gs.SwingsPerSecond, gs.Data.Ammo)
            : Loc.F("{0} dano · {1:0.0}/s · resistência {2}", GameState.FormatNumber(gs.Damage), gs.SwingsPerSecond, gs.ToolDurabilityLeft);
        _lastWear = gs.ToolDurabilityLeft + gs.Data.Ammo * 100000;
        _lastBroken = gs.ToolBroken;
        _pulseInfo.Text = Loc.F("Pulso x{0:0.0} do dano · raio {1:0.0} m", gs.ResonanceMultiplier, gs.ResonanceRadius);
    }

    /// <summary>Guia de objetivos para a experiência inicial e metas de longo prazo.</summary>
    /// <summary>Texto da missão do ato 2 (null quando não há nada da história para fazer).</summary>
    static string StoryObjective(GameState gs) => gs.Story switch
    {
        StoryData.BramCalls => Loc.T("HISTÓRIA\nO VELHO BRAM quer falar com você. Ele mora atrás da oficina do ferreiro, na vila."),
        StoryData.FindGate => gs.IsUnlocked("cathedral")
            ? Loc.T("HISTÓRIA\nAche o PORTÃO SELADO no ponto mais fundo da Catedral de Cristal e tire um molde do selo.")
            : Loc.T("HISTÓRIA\nAbra caminho até a Catedral de Cristal: o portão selado fica lá no fundo."),
        StoryData.BringMold => Loc.T("HISTÓRIA\nLeve o molde do selo ao VELHO BRAM, na vila."),
        StoryData.ForgeKey => gs.CanForgeKey
            ? Loc.T("HISTÓRIA\nForje a CHAVE FALSA na forja do ferreiro: ele está dormindo.")
            : Loc.F("HISTÓRIA\nChave falsa: {0}/{1} Ferro Meteórico, {2}/{3} Diamantes, cadernos do Aldric {4}/2. Forje de noite, na vila.",
                Math.Min(gs.InventoryOf(StoryData.KeyOre1), StoryData.KeyOre1Amount), StoryData.KeyOre1Amount, Math.Min(gs.InventoryOf(StoryData.KeyOre2), StoryData.KeyOre2Amount), StoryData.KeyOre2Amount,
                StoryData.AldricDiaries.Count(x => gs.Data.Diaries.Contains(x))),
        StoryData.OpenGate => Loc.T("HISTÓRIA\nUse a chave falsa no PORTÃO SELADO, no fundo da Catedral de Cristal."),
        StoryData.SeedFound => Loc.T("HISTÓRIA\nLeve a Semente do Povo Lúmen ao VELHO BRAM, na vila."),
        StoryData.PlantSeed => Loc.T("HISTÓRIA\nPlante a Semente no MONUMENTO DE CRISTAL, no meio da praça da vila."),
        _ => null,
    };

    void RefreshObjective()
    {
        var gs = GameState.Instance;
        string text;
        var d = gs.Data;
        bool inHouse = Player != null && IsInstanceValid(Player) && World.PlayerHouse.Contains(Player.GlobalPosition);
        bool inTown = !inHouse && Player != null && IsInstanceValid(Player) && World.Town.Contains(Player.GlobalPosition);
        string story = StoryObjective(gs);
        bool talkNow = gs.Story is StoryData.BramCalls or StoryData.BringMold or StoryData.OpenGate or StoryData.SeedFound or StoryData.PlantSeed;
        var mission = gs.ActiveContractData;
        if (inTown && story != null && (talkNow || gs.CanForgeKey))
            text = story;
        else if (mission != null && !inHouse)
        {
            int have = gs.InventoryOf(mission.Mat);
            string name = GameData.Mat(mission.Mat)?.Name ?? mission.Mat;
            text = have >= mission.Amount
                ? Loc.F("MISSÃO ({0}/{1})\nEntregue {2} no QUADRO da vila antes de dormir.", Math.Min(have, mission.Amount), mission.Amount, name)
                : Loc.F("MISSÃO ({0}/{1})\nMinere {2} e entregue no QUADRO da vila antes de dormir. Multa: {3}", have, mission.Amount, name, GameState.FormatMoney(GameState.ContractFine(mission)));
        }
        else if (inHouse)
            text = gs.DayOver
                ? Loc.T("CASA\nUse o PAINEL para melhorias e habilidades. Durma na CAMA para começar o próximo dia.")
                : Loc.T("NOVO DIA\nSaia de casa e pegue o ELEVADOR da vila para voltar à mina.");
        else if (inTown)
            text = gs.DayOver
                ? Loc.T("VILA DOS FERREIROS\nVenda ao FERREIRO, entregue as ENCOMENDAS do quadro e durma em CASA.")
                : Loc.T("NOVO DIA\nPegue o ELEVADOR na estação da vila para descer à mina.");
        else if (gs.DayOver)
            text = gs.InventoryCount > 0
                ? Loc.T("FIM DO TURNO\nVenda os minérios na ESTAÇÃO DE VENDA do acampamento (ou ao ferreiro da vila) e pegue o ELEVADOR.")
                : Loc.T("FIM DO TURNO\nPegue o ELEVADOR no acampamento para subir à vila.");
        else if (d.CrystalsMined == 0) text = Loc.T("OBJETIVO\nMire em um cristal e clique com o botão esquerdo para minerar (um golpe por clique).");
        else if (d.ShardsSold == 0 && gs.InventoryCount < 8) text = Loc.T("OBJETIVO\nColete os fragmentos. Eles são atraídos até você.");
        else if (d.ShardsSold == 0) text = Loc.T("OBJETIVO\nVá até a ESTAÇÃO DE VENDA (verde) e pressione E.");
        else if (d.Upgrades.Count == 0) text = Loc.T("OBJETIVO\nCompre uma melhoria no TERMINAL (dourado).");
        else if (gs.IsUnlocked("core") && !d.Discovered.Contains("heart") && gs.Damage * gs.SwingsPerSecond * 120 > GameData.Mat("heart").Hp)
            text = Loc.T("META\nDestrua o Coração Ancestral no centro do Núcleo.");
        else if (story != null && d.Discovered.Contains("heart") && (gs.IsUnlocked("cathedral") || gs.Story is StoryData.BramCalls or StoryData.BringMold or StoryData.OpenGate or StoryData.SeedFound or StoryData.PlantSeed))
            text = story;
        else
        {
            var next = gs.NextLockedRegion();
            if (next != null)
            {
                long missing = Math.Max(0, next.UnlockCost - d.Money);
                text = missing > 0
                    ? Loc.F("PRÓXIMA META\nDesbloquear {0}: faltam {1}", next.Name, GameState.FormatMoney(missing))
                    : Loc.F("PRÓXIMA META\nVocê pode desbloquear {0}! Vá até o portão.", next.Name);
            }
            else if (!d.Discovered.Contains("heart")) text = Loc.T("META FINAL\nDestrua o Coração Ancestral no centro do Núcleo.");
            else if (story != null) text = story;
            else text = Loc.T("MESTRE ESCAVADOR\nMaximize suas melhorias e encontre todos os materiais raros.");
        }
        _objective.Text = text;
    }

    void OnNotified(string title, string body, NotifyKind kind)
    {
        Accessibility.Speak($"{title}. {body}");
        if (_notifs.GetChildCount() >= 3) { _pending.Enqueue((title, body, kind)); return; }
        ShowNotification(title, body, kind);
    }

    void ShowNotification(string title, string body, NotifyKind kind)
    {
        var color = kind switch
        {
            NotifyKind.Success => UiTheme.Success,
            NotifyKind.Warning => UiTheme.Danger,
            NotifyKind.Unlock => UiTheme.Gold,
            NotifyKind.Rare => UiTheme.Violet,
            _ => UiTheme.Cyan,
        };
        if (kind == NotifyKind.Unlock) AudioManager.Instance?.Play("unlock", -6f);
        var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.1f, 0.88f), color, 10, 2, 14, 8));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        panel.AddChild(v);
        v.AddChild(UiTheme.Label(title, 17, color, true));
        var b = UiTheme.Label(body, 14, UiTheme.Text);
        b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        b.CustomMinimumSize = new Vector2(268, 0);
        v.AddChild(b);
        _notifs.AddChild(panel);
        panel.Modulate = new Color(1, 1, 1, 0);
        var tw = panel.CreateTween();
        tw.TweenProperty(panel, "modulate:a", 1f, 0.2f);
        tw.TweenInterval(kind == NotifyKind.Unlock || kind == NotifyKind.Rare ? 4.5f : 3f);
        tw.TweenProperty(panel, "modulate:a", 0f, 0.4f);
        tw.TweenCallback(Callable.From(() =>
        {
            panel.QueueFree();
            if (_pending.Count > 0)
            {
                var (t, bb, k) = _pending.Dequeue();
                ShowNotification(t, bb, k);
            }
        }));
    }

    public void ShowPulse(int count) =>
        Pop(count > 1 ? Loc.F("PULSO RESSONANTE ×{0}", count) : Loc.T("PULSO RESSONANTE"), UiTheme.Gold, 30, 60);

    void OnBigMessage(string text) => Pop(Loc.T(text), UiTheme.Cyan, 40, -260, 2.2f);
    public void ShowBigMessage(string text) => OnBigMessage(text);

    void Pop(string text, Color color, int size, float y, float hold = 0.5f)
    {
        var l = UiTheme.Shadowed(UiTheme.Label(text, size, color, true, HorizontalAlignment.Center), 10);
        UiTheme.Place(l, Control.LayoutPreset.Center, new Vector2(-450, y), new Vector2(900, 0));
        l.PivotOffset = new Vector2(450, size * 0.7f);
        l.Scale = new Vector2(0.6f, 0.6f);
        _root.AddChild(l);
        var tw = l.CreateTween();
        tw.TweenProperty(l, "scale", Vector2.One * 1.1f, 0.15f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(l, "scale", Vector2.One, 0.1f);
        tw.TweenInterval(hold);
        tw.Parallel().TweenProperty(l, "position:y", l.Position.Y - 30, 0.9f);
        tw.TweenProperty(l, "modulate:a", 0f, 0.4f);
        tw.TweenCallback(Callable.From(l.QueueFree));
    }

    /// <summary>Atualiza a etiqueta de região e anima a entrada.</summary>
    public void ShowTownBanner()
    {
        var town = new RegionDef { Name = "Vila dos Ferreiros", Subtitle = "Venda, encomendas e a estação da mina", AccentColor = new Color(1f, 0.65f, 0.35f) };
        ShowRegionBanner(town);
        _regionSub.Text = "◇ " + Loc.T(town.Subtitle);
    }

    public void ShowHouseBanner()
    {
        var house = new RegionDef { Name = "Casa do Escavador", Subtitle = "Descanso, melhorias e habilidades", AccentColor = UiTheme.Gold };
        ShowRegionBanner(house);
        _regionSub.Text = "◇ " + house.Subtitle;
    }

    Tween _regionTween;

    public void ShowRegionBanner(RegionDef r)
    {
        _regionName.Text = r.Name;
        _regionSub.Text = r.Deep ? Loc.F("◇ {0}", r.Subtitle) : Loc.F("◇ Região {0} de {1} · {2}", r.Index + 1, GameData.MineRegions, r.Subtitle);
        _regionIcon.Set(r.Id == null ? HudIcon.Kind.Star : HudIcon.RegionKind(r.Id), r.AccentColor);
        _regionTag.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(null, new Color(r.AccentColor, 0.85f), 14, 2, 22, 6));
        _regionName.AddThemeColorOverride("font_color", r.AccentColor.Lightened(0.45f));
        _regionTag.PivotOffset = new Vector2(190, 30);
        _regionTag.Scale = new Vector2(1.25f, 1.25f);
        _regionTag.Modulate = new Color(1.6f, 1.6f, 1.6f);
        var tw = _regionTag.CreateTween().SetParallel();
        _regionTween?.Kill();
        _regionTween = tw;
        tw.TweenProperty(_regionTag, "scale", Vector2.One, 0.6f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(_regionTag, "modulate", Colors.White, 1.2f);
        // depois de alguns segundos o nome do local some (a tela fica limpa)
        tw.Chain().TweenInterval(3.2f);
        tw.Chain().TweenProperty(_regionTag, "modulate:a", 0f, 1.0f);
    }

    public void FlashFull()
    {
        double now = Godot.Time.GetTicksMsec() / 1000.0;
        _fullFlash = 1f;
        if (now - _lastFullWarn < 6.0) return;
        _lastFullWarn = now;
        AudioManager.Instance?.Play("full", -6f);
        GameState.Instance.Notify("Mochila cheia!", "Venda seus fragmentos ou compre a Mochila Reforçada.", NotifyKind.Warning);
    }

    public override void _Process(double delta)
    {
        // durabilidade muda a cada golpe: atualiza o texto da ferramenta só quando mudou
        bool wm = Player != null && IsInstanceValid(Player) && Player.WeaponMode;
        if (GameState.Instance is { } gsw && (gsw.ToolDurabilityLeft + gsw.Data.Ammo * 100000 != _lastWear || gsw.ToolBroken != _lastBroken || wm != _lastWeaponMode) && _toolName != null) RefreshStats();
        long __t = Core.Prof.Start();
        _ProcessInner(delta);
        Core.Prof.End("hud", __t);
    }

    void _ProcessInner(double delta)
    {
        float dt = (float)delta;
        var gs = GameState.Instance;
        // chefão em luta: barra no topo com a fase
        var boss = Player != null && IsInstanceValid(Player) ? Player.Enemies?.Boss : null;
        bool showBoss = Player != null && IsInstanceValid(Player) && (Player.Enemies?.BossEngaged ?? false);
        if (_bossPanel.Visible != showBoss) _bossPanel.Visible = showBoss;
        if (showBoss)
        {
            _bossBar.Value = boss.Hp / boss.Def.Hp;
            _bossName.Text = boss.Def.Name.ToUpper() + (boss.Phase == 3 ? "  ·  " + Loc.T("ENFURECIDO") : boss.Phase == 2 ? "  ·  " + Loc.T("FASE 2") : "");
        }
        long debt = gs.Data.Debt;
        if (_debt.Visible != debt > 0) _debt.Visible = debt > 0;
        if (debt > 0) _debt.Text = Loc.F("Dívida: {0}", GameState.FormatMoney(debt));
        long target = gs.Data.Money;
        if (_displayMoney != target)
        {
            long diff = target - _displayMoney;
            long step = (long)Math.Ceiling(Math.Abs(diff) * Math.Min(1.0, dt * 8.0));
            _displayMoney += Math.Sign(diff) * Math.Max(1, step);
            if (Math.Sign(target - _displayMoney) != Math.Sign(diff)) _displayMoney = target;
            _money.Text = GameState.FormatNumber(_displayMoney);
        }
        if (_gainTimer > 0)
        {
            _gainTimer -= dt;
            if (_gainTimer < 0.6f) _moneyGain.Modulate = new Color(1, 1, 1, Mathf.Max(0, _gainTimer / 0.6f));
            if (_gainTimer <= 0) { _gainAccum = 0; _moneyGain.Text = ""; _moneyGain.Visible = false; }
        }
        _capPanel.Modulate = _fullFlash > 0
            ? new Color(1, 0.6f + 0.4f * Mathf.Abs(Mathf.Cos((_fullFlash -= dt) * 12f)), 0.6f + 0.4f * Mathf.Abs(Mathf.Cos(_fullFlash * 12f)))
            : Colors.White;

        UpdateEventPanel();
        UpdateClock();
        UpdateHp();
        _fps.Visible = Settings.Data.ShowFps;
        if (_fps.Visible) UpdateFpsText(dt);
        else if (_measuring) { RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), false); _measuring = false; }

        if (Player == null || !IsInstanceValid(Player)) return;
        // ressonância (só com ferramenta a laser)
        _energyPanel.Visible = GameState.Instance.ResonanceActive;
        float res = Player.Resonance01;
        _energyBar.Value = res;
        _energyPct.Text = Player.ResonanceReady ? Loc.T("PRONTO") : $"{res * 100:0}%";
        UiTheme.SetBarColor(_energyBar, Player.ResonanceReady ? UiTheme.Gold : UiTheme.Cyan);

        // alvo
        var c = Player.TargetCrystal;
        _crosshair.Target = c != null;
        _crosshair.Progress = c != null ? 1f - c.HpRatio : 0f;
        _crosshair.Cooldown = Player.SwingCooldown01;
        _crosshair.Resonance = res;
        _crosshair.Time += dt;
        _crosshair.TargetColor = c?.Material.Glow ?? UiTheme.Cyan;
        _crosshair.QueueRedraw();
        _targetBox.Visible = c != null;
        if (c != null)
        {
            var m = c.Material;
            _targetName.Text = m.Name;
            _targetRarity.Text = RarityInfo.Badge(m.Rarity) + (m.Special ? Loc.T(" · Formação Especial") : m.Rare ? Loc.T(" · Veio raro") : "");
            _targetRarity.AddThemeColorOverride("font_color", RarityInfo.Color(m.Rarity));
            _brackets.Color = m.Glow.Lightened(0.3f);
            _brackets.QueueRedraw();
            float pct = 1f - c.HpRatio;
            _targetHp.Value = pct;
            UiTheme.SetBarColor(_targetHp, m.Glow);
            _targetPct.Text = $"{pct * 100:0}%";
            _targetGem.SetOre(m);
            float scale = m.Special ? 1f : c.SizeScale;
            int lo = Math.Max(1, (int)MathF.Round(m.ShardsMin * scale * gs.EventShardMultiplier));
            int hi = Math.Max(lo, (int)MathF.Round(m.ShardsMax * scale * gs.EventShardMultiplier));
            _targetYield.Text = (lo == hi ? $"×{lo}" : $"×{lo}–{hi}") + Loc.F("   ·   {0}/frag", GameState.FormatMoney(gs.SellPrice(m)));
        }
        // prompt
        string prompt = Player.Climbing
            ? (InputHints.UsingPad
                ? Loc.F("[Analógico ↑/↓] Subir/descer   [{0}] Soltar a corda", InputHints.Key("jump"))
                : Loc.F("[{0}/{1}] Subir/descer   [{2}] Soltar a corda", InputHints.Key("move_forward"), InputHints.Key("move_back"), InputHints.Key("jump")))
            : Player.TargetInteractable?.GetPrompt();
        _promptPanel.Visible = !string.IsNullOrEmpty(prompt);
        if (_promptPanel.Visible)
        {
            _prompt.Text = prompt;
            _promptPanel.ResetSize();
            _promptPanel.OffsetLeft = -_promptPanel.Size.X / 2;
            _promptPanel.OffsetRight = _promptPanel.Size.X / 2;
        }
    }

    public void SetGameplayVisible(bool v) => Visible = v;

    PanelContainer _buildPanel;
    Label _buildHead, _buildTitle, _buildDetail;
    bool _buildOk = true, _buildStyled;

    /// <summary>Mostra/esconde a barra do modo construção (title null = esconder).</summary>
    public void SetBuildBar(string title, string detail, bool ok)
    {
        if (_buildPanel == null) return;
        bool show = title != null;
        if (_buildPanel.Visible != show) { _buildPanel.Visible = show; _keys.Visible = !show; if (show) UiTheme.PopIn(_buildPanel); }
        if (!show) return;
        if (_buildTitle.Text != title) _buildTitle.Text = title;
        if (_buildDetail.Text != detail) _buildDetail.Text = detail;
        if (ok != _buildOk || !_buildStyled)
        {
            _buildOk = ok; _buildStyled = true;
            _buildPanel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.1f, 0.88f), ok ? new Color(0.3f, 1f, 0.75f) : new Color(1f, 0.4f, 0.3f), 10, 2, 14, 8));
        }
    }

    void UpdateEventPanel()
    {
        var a = Events?.Active;
        if (a == null) { _eventPanel.Visible = false; _shownEvent = null; return; }
        if (a != _shownEvent)
        {
            _shownEvent = a;
            _eventPanel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.1f, 0.85f), a.Color, 10, 2, 12, 6));
            _eventTitle.Text = Loc.T(a.Name).ToUpper();
            _eventTitle.AddThemeColorOverride("font_color", a.Color);
            _eventDesc.Text = Loc.T(a.Description);
            UiTheme.SetBarColor(_eventBar, a.Color);
            _eventPanel.Visible = true;
            UiTheme.PopIn(_eventPanel);
        }
        _eventBar.Value = a.Duration > 0 ? Events.Remaining / a.Duration : 0;
    }
}

/// <summary>Mira de scanner: círculo tracejado, progresso do alvo, cadência e ressonância.</summary>
public partial class Crosshair : Control
{
    public bool Target;
    public float Progress;
    public float Cooldown;
    public float Resonance;
    public float Time;
    public Color TargetColor = UiTheme.Cyan;

    public override void _Draw()
    {
        var c = Size / 2;
        var white = new Color(1, 1, 1, 0.9f);
        DrawCircle(c, 2.5f, white);
        // círculo tracejado (4 segmentos) que gira devagar
        float rot = Target ? Time * 1.5f : 0f;
        var ringCol = Target ? TargetColor.Lightened(0.4f) : new Color(1, 1, 1, 0.75f);
        float rad = Target ? 20 : 14;
        for (int i = 0; i < 4; i++)
        {
            float a0 = rot + i * Mathf.Pi / 2 + 0.2f;
            DrawArc(c, rad, a0, a0 + Mathf.Pi / 2 - 0.4f, 10, ringCol, 2.5f, true);
        }
        if (Target)
        {
            if (Progress > 0.001f)
                DrawArc(c, 26, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Progress, 40, TargetColor.Lightened(0.2f), 4, true);
            if (Cooldown > 0.01f)
                DrawArc(c, 9, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Cooldown, 20, new Color(1, 1, 1, 0.5f), 2, true);
        }
        if (Resonance > 0.001f)
        {
            bool ready = Resonance >= 0.999f;
            var col = ready ? UiTheme.Gold.Lerp(Colors.White, 0.5f + 0.5f * Mathf.Sin(Time * 14f)) : new Color(UiTheme.Violet, 0.85f);
            float r = ready ? 32 + Mathf.Sin(Time * 14f) * 1.5f : 31;
            DrawArc(c, r, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Resonance, 48, col, ready ? 4 : 3, true);
        }
    }
}
