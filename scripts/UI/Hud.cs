using System;
using System.Collections.Generic;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>HUD de jogo: dinheiro, mochila, mira com progresso, alvo, prompts, notificações e objetivos.</summary>
public partial class Hud : CanvasLayer
{
    public PlayerController Player;

    Label _money, _moneyGain, _capLabel, _prompt, _targetName, _targetInfo, _regionTitle, _regionSub, _fps, _objective, _stats;
    ProgressBar _capBar, _targetHp;
    PanelContainer _promptPanel, _targetPanel, _objectivePanel;
    Control _regionBanner;
    Crosshair _crosshair;
    VBoxContainer _notifs;
    long _displayMoney;
    long _shownMoney;
    float _gainTimer;
    long _gainAccum;
    readonly Queue<(string, string, NotifyKind)> _pending = new();
    float _fullFlash;

    public override void _Ready()
    {
        Layer = 5;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Theme = UiTheme.Get() };
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(root);

        // ---------- Canto superior esquerdo: dinheiro + mochila ----------
        var tl = new PanelContainer { Position = new Vector2(20, 18), MouseFilter = Control.MouseFilterEnum.Ignore };
        tl.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.04f, 0.03f, 0.08f, 0.72f), new Color(UiTheme.Gold, 0.35f), 12, 2, 16));
        root.AddChild(tl);
        var tlv = new VBoxContainer { CustomMinimumSize = new Vector2(250, 0) };
        tlv.AddThemeConstantOverride("separation", 6);
        tl.AddChild(tlv);
        var moneyRow = new HBoxContainer();
        tlv.AddChild(moneyRow);
        _money = UiTheme.Shadowed(UiTheme.Label("◈ 0", 34, UiTheme.Gold, true));
        moneyRow.AddChild(_money);
        _moneyGain = UiTheme.Shadowed(UiTheme.Label("", 20, UiTheme.Success, true));
        _moneyGain.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        moneyRow.AddChild(_moneyGain);
        _capLabel = UiTheme.Label("Mochila 0/30", 16, UiTheme.Muted);
        tlv.AddChild(_capLabel);
        _capBar = new ProgressBar { CustomMinimumSize = new Vector2(250, 14), ShowPercentage = false, MaxValue = 1, Step = 0.001 };
        tlv.AddChild(_capBar);

        // ---------- Objetivo (abaixo) ----------
        _objectivePanel = new PanelContainer { Position = new Vector2(20, 140), MouseFilter = Control.MouseFilterEnum.Ignore };
        _objectivePanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.04f, 0.03f, 0.08f, 0.6f), new Color(UiTheme.Cyan, 0.25f), 10, 1, 12));
        root.AddChild(_objectivePanel);
        _objective = UiTheme.Label("", 16, UiTheme.Text);
        _objective.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _objective.CustomMinimumSize = new Vector2(270, 0);
        _objectivePanel.AddChild(_objective);

        // ---------- Mira ----------
        _crosshair = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiTheme.Place(_crosshair, Control.LayoutPreset.Center, new Vector2(-32, -32), new Vector2(64, 64));
        root.AddChild(_crosshair);

        // ---------- Painel do alvo (abaixo da mira) ----------
        _targetPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _targetPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.04f, 0.03f, 0.08f, 0.7f), new Color(1, 1, 1, 0.15f), 10, 1, 12));
        UiTheme.Place(_targetPanel, Control.LayoutPreset.Center, new Vector2(-130, 52));
        _targetPanel.CustomMinimumSize = new Vector2(260, 0);
        root.AddChild(_targetPanel);
        var tv = new VBoxContainer();
        tv.AddThemeConstantOverride("separation", 3);
        _targetPanel.AddChild(tv);
        _targetName = UiTheme.Label("", 18, UiTheme.Text, true, HorizontalAlignment.Center);
        tv.AddChild(_targetName);
        _targetHp = new ProgressBar { CustomMinimumSize = new Vector2(236, 10), ShowPercentage = false, MaxValue = 1, Step = 0.001 };
        tv.AddChild(_targetHp);
        _targetInfo = UiTheme.Label("", 14, UiTheme.Muted, false, HorizontalAlignment.Center);
        tv.AddChild(_targetInfo);

        // ---------- Prompt de interação ----------
        _promptPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _promptPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.04f, 0.03f, 0.08f, 0.8f), UiTheme.Gold, 10, 2, 16));
        UiTheme.Place(_promptPanel, Control.LayoutPreset.CenterBottom, new Vector2(-150, -150));
        root.AddChild(_promptPanel);
        _prompt = UiTheme.Label("", 20, UiTheme.Text, true, HorizontalAlignment.Center);
        _promptPanel.AddChild(_prompt);

        // ---------- Banner de região ----------
        _regionBanner = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        UiTheme.Place(_regionBanner, Control.LayoutPreset.CenterTop, new Vector2(-300, 100), new Vector2(600, 0));
        _regionBanner.CustomMinimumSize = new Vector2(600, 0);
        root.AddChild(_regionBanner);
        _regionTitle = UiTheme.Shadowed(UiTheme.Label("", 36, UiTheme.Cyan, true, HorizontalAlignment.Center), 10);
        _regionSub = UiTheme.Shadowed(UiTheme.Label("", 20, UiTheme.Text, false, HorizontalAlignment.Center), 6);
        _regionBanner.AddChild(_regionTitle);
        _regionBanner.AddChild(_regionSub);

        // ---------- Notificações (direita) ----------
        _notifs = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _notifs.AddThemeConstantOverride("separation", 8);
        UiTheme.Place(_notifs, Control.LayoutPreset.TopRight, new Vector2(-340, 18));
        _notifs.CustomMinimumSize = new Vector2(320, 0);
        root.AddChild(_notifs);

        // ---------- Rodapé: estatísticas e atalhos ----------
        _stats = UiTheme.Shadowed(UiTheme.Label("", 15, UiTheme.Muted));
        UiTheme.Place(_stats, Control.LayoutPreset.BottomLeft, new Vector2(20, -62));
        root.AddChild(_stats);
        var keys = UiTheme.Shadowed(UiTheme.Label("[Botão esq.] Minerar   [E] Interagir   [Shift] Correr   [Esc] Pausa", 15, UiTheme.Muted, false, HorizontalAlignment.Right));
        UiTheme.Place(keys, Control.LayoutPreset.BottomRight, new Vector2(-640, -36));
        keys.CustomMinimumSize = new Vector2(620, 0);
        root.AddChild(keys);

        _fps = UiTheme.Shadowed(UiTheme.Label("", 14, UiTheme.Success));
        UiTheme.Place(_fps, Control.LayoutPreset.BottomRight, new Vector2(-120, -62));
        root.AddChild(_fps);

        var gs = GameState.Instance;
        gs.MoneyChanged += OnMoney;
        gs.InventoryChanged += OnInventory;
        gs.UpgradesChanged += OnInventory;
        gs.UpgradesChanged += RefreshStats;
        gs.RegionsChanged += RefreshObjective;
        gs.Notified += OnNotified;
        gs.BigMessage += OnBigMessage;
        _displayMoney = _shownMoney = gs.Data.Money;
        OnInventory();
        RefreshStats();
        RefreshObjective();
        _money.Text = GameState.FormatMoney(_displayMoney);
    }

    public override void _ExitTree()
    {
        var gs = GameState.Instance;
        if (gs == null) return;
        gs.MoneyChanged -= OnMoney;
        gs.InventoryChanged -= OnInventory;
        gs.UpgradesChanged -= OnInventory;
        gs.UpgradesChanged -= RefreshStats;
        gs.RegionsChanged -= RefreshObjective;
        gs.Notified -= OnNotified;
        gs.BigMessage -= OnBigMessage;
    }

    public void ResetMoneyDisplay()
    {
        _displayMoney = _shownMoney = GameState.Instance.Data.Money;
        _money.Text = GameState.FormatMoney(_displayMoney);
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
            _moneyGain.Text = "  +" + GameState.FormatNumber(_gainAccum);
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
        _capLabel.Text = full
            ? $"Mochila {cnt}/{cap}  —  CHEIA! Venda na estação"
            : $"Mochila {cnt}/{cap}   ·   {GameState.FormatMoney(gs.InventoryValue())}";
        _capBar.Value = cap > 0 ? (double)cnt / cap : 0;
        var fill = UiTheme.Box(full ? UiTheme.Danger : cnt > cap * 0.75f ? UiTheme.Gold : UiTheme.Cyan, new Color(0, 0, 0, 0), 6, 0, 0);
        fill.ShadowSize = 0;
        _capBar.AddThemeStyleboxOverride("fill", fill);
        _capLabel.AddThemeColorOverride("font_color", full ? UiTheme.Danger : UiTheme.Muted);
        RefreshObjective();
    }

    void RefreshStats()
    {
        var gs = GameState.Instance;
        _stats.Text = $"{GameState.ToolTierNames[gs.ToolTier]}   ·   {GameState.FormatNumber(gs.Damage)} dano   ·   {gs.SwingsPerSecond:0.0} golpes/s   ·   {GameState.FormatNumber(gs.DamagePerSecond)} DPS";
    }

    /// <summary>Guia de objetivos para a experiência inicial e metas de longo prazo.</summary>
    void RefreshObjective()
    {
        var gs = GameState.Instance;
        string text;
        var d = gs.Data;
        if (d.CrystalsMined == 0) text = "OBJETIVO\nMire em um cristal e segure o botão esquerdo para minerar.";
        else if (d.ShardsSold == 0 && gs.InventoryCount < 8) text = "OBJETIVO\nColete os fragmentos. Eles são atraídos até você.";
        else if (d.ShardsSold == 0) text = "OBJETIVO\nVá até a ESTAÇÃO DE VENDA (verde) e pressione E.";
        else if (d.Upgrades.Count == 0) text = "OBJETIVO\nCompre uma melhoria no TERMINAL (dourado).";
        else
        {
            var next = gs.NextLockedRegion();
            if (next != null)
            {
                long missing = Math.Max(0, next.UnlockCost - d.Money);
                text = missing > 0
                    ? $"PRÓXIMA META\nDesbloquear {next.Name}: faltam {GameState.FormatMoney(missing)}"
                    : $"PRÓXIMA META\nVocê pode desbloquear {next.Name}! Vá até o portão.";
            }
            else if (!d.Discovered.Contains("heart")) text = "META FINAL\nDestrua o Coração Ancestral no centro do Núcleo.";
            else text = "MESTRE ESCAVADOR\nMaximize suas melhorias e encontre todos os materiais raros.";
        }
        _objective.Text = text;
    }

    void OnNotified(string title, string body, NotifyKind kind)
    {
        if (_notifs.GetChildCount() >= 4) { _pending.Enqueue((title, body, kind)); return; }
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
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.04f, 0.1f, 0.9f), color, 10, 2, 14));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 2);
        panel.AddChild(v);
        v.AddChild(UiTheme.Label(title, 18, color, true));
        var b = UiTheme.Label(body, 15, UiTheme.Text);
        b.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        b.CustomMinimumSize = new Vector2(290, 0);
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
        Pop(count > 1 ? $"PULSO RESSONANTE ×{count}" : "PULSO RESSONANTE", UiTheme.Gold, 30, -110);

    void OnBigMessage(string text) => Pop(text, UiTheme.Cyan, 40, -190, 2.2f);

    void Pop(string text, Color color, int size, float y, float hold = 0.5f)
    {
        var l = UiTheme.Shadowed(UiTheme.Label(text, size, color, true, HorizontalAlignment.Center), 10);
        UiTheme.Place(l, Control.LayoutPreset.Center, new Vector2(-450, y), new Vector2(900, 0));
        l.PivotOffset = new Vector2(450, size * 0.7f);
        l.Scale = new Vector2(0.6f, 0.6f);
        _crosshair.GetParent().AddChild(l);
        var tw = l.CreateTween();
        tw.TweenProperty(l, "scale", Vector2.One * 1.1f, 0.15f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(l, "scale", Vector2.One, 0.1f);
        tw.TweenInterval(hold);
        tw.Parallel().TweenProperty(l, "position:y", l.Position.Y - 30, 0.9f);
        tw.TweenProperty(l, "modulate:a", 0f, 0.4f);
        tw.TweenCallback(Callable.From(l.QueueFree));
    }

    public void ShowRegionBanner(RegionDef r)
    {
        _regionTitle.Text = r.Name.ToUpper();
        _regionTitle.AddThemeColorOverride("font_color", r.AccentColor.Lightened(0.2f));
        _regionSub.Text = r.Subtitle;
        var tw = _regionBanner.CreateTween();
        tw.TweenProperty(_regionBanner, "modulate:a", 1f, 0.6f);
        tw.TweenInterval(2.8f);
        tw.TweenProperty(_regionBanner, "modulate:a", 0f, 1.2f);
    }

    double _lastFullWarn = -100;

    public void FlashFull()
    {
        double now = Godot.Time.GetTicksMsec() / 1000.0;
        if (now - _lastFullWarn < 6.0) { _fullFlash = 1f; return; }
        _lastFullWarn = now;
        _fullFlash = 1f;
        AudioManager.Instance?.Play("full", -6f);
        GameState.Instance.Notify("Mochila cheia!", "Venda seus fragmentos ou compre a Mochila Reforçada.", NotifyKind.Warning);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var gs = GameState.Instance;
        // contador de dinheiro animado
        long target = gs.Data.Money;
        if (_displayMoney != target)
        {
            long diff = target - _displayMoney;
            long step = (long)Math.Ceiling(Math.Abs(diff) * Math.Min(1.0, dt * 8.0));
            _displayMoney += Math.Sign(diff) * Math.Max(1, step);
            if (Math.Sign(target - _displayMoney) != Math.Sign(diff)) _displayMoney = target;
            _money.Text = GameState.FormatMoney(_displayMoney);
        }
        if (_gainTimer > 0)
        {
            _gainTimer -= dt;
            if (_gainTimer < 0.6f) _moneyGain.Modulate = new Color(1, 1, 1, Mathf.Max(0, _gainTimer / 0.6f));
            if (_gainTimer <= 0) { _gainAccum = 0; _moneyGain.Text = ""; }
        }
        if (_fullFlash > 0)
        {
            _fullFlash -= dt;
            _capBar.Modulate = new Color(1, 1, 1, 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(_fullFlash * 12f)));
        }
        else _capBar.Modulate = Colors.White;

        _fps.Visible = Settings.Data.ShowFps;
        if (_fps.Visible) _fps.Text = $"{Engine.GetFramesPerSecond():0} FPS";

        if (Player == null || !IsInstanceValid(Player)) return;
        // alvo
        var c = Player.TargetCrystal;
        _crosshair.Target = c != null;
        _crosshair.Progress = c != null ? 1f - c.HpRatio : 0f;
        _crosshair.Cooldown = Player.SwingCooldown01;
        _crosshair.Resonance = Player.Resonance01;
        _crosshair.Time += dt;
        _crosshair.TargetColor = c?.Material.Glow ?? UiTheme.Cyan;
        _crosshair.QueueRedraw();
        _targetPanel.Visible = c != null;
        if (c != null)
        {
            var m = c.Material;
            _targetName.Text = m.Name + (m.Rare ? "  ★ RARO" : m.Special ? "  ✦ ESPECIAL" : "");
            _targetName.AddThemeColorOverride("font_color", m.Glow.Lightened(0.35f));
            _targetHp.Value = c.HpRatio;
            var fill = UiTheme.Box(m.Glow, new Color(0, 0, 0, 0), 5, 0, 0);
            fill.ShadowSize = 0;
            _targetHp.AddThemeStyleboxOverride("fill", fill);
            float hitsLeft = Mathf.Ceil(c.Hp / Mathf.Max(0.01f, gs.Damage));
            _targetInfo.Text = $"{GameState.FormatNumber(c.Hp)} / {GameState.FormatNumber(c.MaxHp)}  ·  {GameState.FormatMoney(gs.SellPrice(m))}/frag  ·  {hitsLeft:0} golpes";
        }
        // prompt
        string prompt = Player.TargetInteractable?.GetPrompt();
        _promptPanel.Visible = !string.IsNullOrEmpty(prompt);
        if (_promptPanel.Visible)
        {
            _prompt.Text = prompt;
            _promptPanel.ResetSize();
            _promptPanel.OffsetLeft = -_promptPanel.Size.X / 2; _promptPanel.OffsetRight = _promptPanel.Size.X / 2;
        }
    }

    public void SetGameplayVisible(bool v) => Visible = v;
}

/// <summary>Mira desenhada por código: anel de progresso de dano e indicador de cadência.</summary>
public partial class Crosshair : Control
{
    public bool Target;
    public float Resonance;
    public float Time;
    public float Progress;
    public float Cooldown;
    public Color TargetColor = UiTheme.Cyan;

    public override void _Draw()
    {
        var c = Size / 2;
        var white = new Color(1, 1, 1, 0.9f);
        DrawCircle(c, 2.5f, white);
        if (Resonance > 0.001f)
        {
            bool ready = Resonance >= 0.999f;
            var col = ready ? UiTheme.Gold.Lerp(Colors.White, 0.5f + 0.5f * Mathf.Sin(Time * 14f)) : new Color(UiTheme.Violet, 0.85f);
            float r = ready ? 28 + Mathf.Sin(Time * 14f) * 1.5f : 27;
            DrawArc(c, r, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Resonance, 48, col, ready ? 4 : 3, true);
        }
        if (Target)
        {
            DrawArc(c, 18, 0, Mathf.Tau, 40, new Color(0, 0, 0, 0.45f), 5, true);
            if (Progress > 0.001f)
                DrawArc(c, 18, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Progress, 40, TargetColor.Lightened(0.3f), 4, true);
            if (Cooldown > 0.01f)
                DrawArc(c, 11, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * Cooldown, 24, new Color(1, 1, 1, 0.5f), 2, true);
        }
        else
        {
            for (int i = 0; i < 4; i++)
            {
                var dir = Vector2.Right.Rotated(i * Mathf.Pi / 2);
                DrawLine(c + dir * 6, c + dir * 11, new Color(1, 1, 1, 0.7f), 2, true);
            }
        }
    }
}
