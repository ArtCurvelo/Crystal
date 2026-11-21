using System;
using System.Linq;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>Barra de nível segmentada (estilo terminal).</summary>
public partial class SegmentBar : Control
{
    public int Segments = 10;
    public float Fill;          // 0..1
    public Color Color = UiTheme.Cyan;

    public SegmentBar() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(150, 12); }

    public override void _Draw()
    {
        float gap = 3;
        float w = (Size.X - gap * (Segments - 1)) / Segments;
        int filled = Mathf.RoundToInt(Fill * Segments);
        if (Fill > 0 && filled == 0) filled = 1;
        for (int i = 0; i < Segments; i++)
        {
            var r = new Rect2(i * (w + gap), 0, w, Size.Y);
            var skew = Size.Y * 0.35f;
            var pts = new[] { r.Position + new Vector2(skew, 0), r.Position + new Vector2(r.Size.X, 0), r.Position + new Vector2(r.Size.X - skew, r.Size.Y), r.Position + new Vector2(0, r.Size.Y) };
            DrawColoredPolygon(pts, i < filled ? Color : new Color(1, 1, 1, 0.08f));
        }
    }
}

/// <summary>Base dos terminais: abas Melhorias/Vender e painel de inventário à esquerda.</summary>
public abstract partial class TerminalScreen : ModalScreen
{
    public Action SwitchToShop, SwitchToSell;
    /// <summary>
    /// Aberto pela Estação de Venda (a única do mapa)? Só então dá para vender. Nos terminais de
    /// melhoria das regiões a aba Vender e o botão de venda ficam escondidos.
    /// </summary>
    public static bool AtSellStation;
    Button _sellTab;
    protected Label MoneyLabel;
    protected VBoxContainer InvList;
    protected Label InvTotal;
    protected Button InvSell;
    protected HBoxContainer Content;
    protected abstract bool IsShopTab { get; }

    protected void BuildTerminal(Vector2 size)
    {
        BuildFrame(null, size, UiTheme.Cyan);
        Panel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.03f, 0.05f, 0.1f, 0.96f), new Color(UiTheme.Cyan, 0.8f), 18, 3, 22, 18));

        // cabeçalho: abas + créditos
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 10);
        Body.AddChild(header);
        header.AddChild(Tab("⬆  Melhorias", IsShopTab, () => { if (!IsShopTab) SwitchToShop?.Invoke(); }));
        _sellTab = Tab("◆  Vender", !IsShopTab, () => { if (IsShopTab && AtSellStation) SwitchToSell?.Invoke(); });
        header.AddChild(_sellTab);
        var spacer = new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddChild(spacer);
        var money = new PanelContainer();
        money.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(null, new Color(UiTheme.Gold, 0.6f), 10, 2, 14, 6));
        header.AddChild(money);
        var mh = new HBoxContainer();
        mh.AddThemeConstantOverride("separation", 8);
        money.AddChild(mh);
        mh.AddChild(HudIcon.Make(HudIcon.Kind.Coin, UiTheme.Gold, 26));
        MoneyLabel = UiTheme.Label("", 22, UiTheme.Gold, true);
        MoneyLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        mh.AddChild(MoneyLabel);

        Content = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        Content.AddThemeConstantOverride("separation", 16);
        Body.AddChild(Content);

        // painel de inventário
        var inv = new PanelContainer { CustomMinimumSize = new Vector2(270, 0) };
        inv.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.05f, 0.09f, 0.15f, 0.95f), new Color(UiTheme.Cyan, 0.6f), 14, 2, 14, 12));
        Content.AddChild(inv);
        var iv = new VBoxContainer();
        iv.AddThemeConstantOverride("separation", 8);
        inv.AddChild(iv);
        var ih = new HBoxContainer();
        ih.AddThemeConstantOverride("separation", 8);
        iv.AddChild(ih);
        ih.AddChild(HudIcon.Make(HudIcon.Kind.Bag, new Color(0.75f, 0.85f, 0.95f), 26));
        ih.AddChild(UiTheme.Label("Inventário", 22, UiTheme.Text, true));
        iv.AddChild(new ColorRect { Color = new Color(UiTheme.Cyan, 0.3f), CustomMinimumSize = new Vector2(0, 2) });
        var scroll = new ScrollContainer { FollowFocus = true, CustomMinimumSize = new Vector2(0, 250), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        iv.AddChild(scroll);
        InvList = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        InvList.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(InvList);
        iv.AddChild(new ColorRect { Color = new Color(UiTheme.Cyan, 0.3f), CustomMinimumSize = new Vector2(0, 2) });
        var th = new HBoxContainer();
        iv.AddChild(th);
        var tl = UiTheme.Label("Valor total:", 17, UiTheme.Text);
        tl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        th.AddChild(tl);
        InvTotal = UiTheme.Label("", 19, UiTheme.Gold, true);
        th.AddChild(InvTotal);
        InvSell = UiTheme.Button("◆ ➜  Vender", DoSell, 0, 20);
        InvSell.CustomMinimumSize = new Vector2(0, 46);
        InvSell.AddThemeStyleboxOverride("normal", UiTheme.Chamfer(new Color(0.05f, 0.25f, 0.3f), UiTheme.Cyan, 10, 2, 12, 6));
        InvSell.AddThemeStyleboxOverride("hover", UiTheme.Chamfer(new Color(0.08f, 0.35f, 0.42f), UiTheme.Cyan.Lightened(0.3f), 10, 2, 12, 6));
        iv.AddChild(InvSell);
    }

    Button Tab(string text, bool active, Action a)
    {
        var b = UiTheme.Button(text, a, 190, 21);
        var bg = active ? new Color(0.1f, 0.45f, 0.55f, 0.95f) : new Color(0.05f, 0.1f, 0.16f, 0.9f);
        var border = active ? UiTheme.Cyan.Lightened(0.3f) : new Color(UiTheme.Cyan, 0.4f);
        b.AddThemeStyleboxOverride("normal", UiTheme.Chamfer(bg, border, 10, 2, 14, 6));
        b.AddThemeStyleboxOverride("hover", UiTheme.Chamfer(bg.Lightened(0.15f), UiTheme.Cyan, 10, 2, 14, 6));
        b.AddThemeStyleboxOverride("pressed", UiTheme.Chamfer(bg.Lightened(0.2f), UiTheme.Cyan, 10, 2, 14, 6));
        b.CustomMinimumSize = new Vector2(190, 50);
        return b;
    }

    protected void RefreshInventory()
    {
        var gs = GameState.Instance;
        MoneyLabel.Text = GameState.FormatNumber(gs.Data.Money);
        UiTheme.ClearChildren(InvList);
        if (gs.InventoryCount == 0)
        {
            var e = UiTheme.Label("Mochila vazia.\nMinere cristais e volte!", 15, UiTheme.Muted);
            InvList.AddChild(e);
        }
        foreach (var kv in gs.Data.Inventory.OrderByDescending(k => GameData.Mat(k.Key)?.Value ?? 0))
        {
            var m = GameData.Mat(kv.Key);
            if (m == null) continue;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            row.AddChild(HudIcon.MakeOre(m, 26));
            var q = UiTheme.Label($"×{kv.Value}", 17, UiTheme.Text, true);
            q.CustomMinimumSize = new Vector2(56, 0);
            row.AddChild(q);
            row.AddChild(HudIcon.Make(HudIcon.Kind.Coin, UiTheme.Gold, 18));
            var v = UiTheme.Label(GameState.FormatNumber(gs.SellPrice(m) * kv.Value), 17, UiTheme.Text, false, HorizontalAlignment.Right);
            v.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(v);
            row.TooltipText = m.Name;
            InvList.AddChild(row);
        }
        InvTotal.Text = GameState.FormatMoney(gs.InventoryValue());
        InvSell.Disabled = gs.InventoryCount == 0;
    }

    protected override void OnTab(int dir)
    {
        if (IsShopTab && AtSellStation) SwitchToSell?.Invoke();
        else if (!IsShopTab) SwitchToShop?.Invoke();
    }

    public override void Open()
    {
        base.Open();
        if (_sellTab != null) _sellTab.Visible = AtSellStation;
        if (InvSell != null) InvSell.Visible = AtSellStation;
    }

    protected void DoSell()
    {
        if (!AtSellStation) return;
        var gs = GameState.Instance;
        int count = gs.InventoryCount;
        long earned = gs.SellAll();
        if (earned <= 0) { AudioManager.Instance?.Play("error", -6f); return; }
        AudioManager.Instance?.Play("sell", -2f);
        gs.Notify("Venda concluída", Loc.F("{0} fragmentos por {1}", count, GameState.FormatMoney(earned)), NotifyKind.Success);
        Refresh();
        InvTotal.Text = "+" + GameState.FormatMoney(earned);
        InvTotal.PivotOffset = InvTotal.Size / 2;
        var tw = InvTotal.CreateTween();
        tw.TweenProperty(InvTotal, "scale", new Vector2(1.25f, 1.25f), 0.1f);
        tw.TweenProperty(InvTotal, "scale", Vector2.One, 0.25f);
        gs.SaveGame(true);
    }

}

// =====================================================================
/// <summary>Aba Melhorias: upgrades em linhas com barra segmentada e preço em destaque; expedições abaixo.</summary>
public partial class ShopScreen : TerminalScreen
{
    protected override bool IsShopTab => true;
    GridContainer _grid;
    VBoxContainer _toolBox;
    VBoxContainer _regionBox;
    VBoxContainer _prestigeBox;
    string _lastBought;

    public override void _Ready()
    {
        BuildTerminal(new Vector2(1060, 600));
        var right = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.04f, 0.07f, 0.12f, 0.9f), new Color(UiTheme.Cyan, 0.4f), 14, 2, 12, 10));
        Content.AddChild(right);
        var scroll = new ScrollContainer { FollowFocus = true, CustomMinimumSize = new Vector2(0, 440), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _scroll = scroll;
        right.AddChild(scroll);
        var inner = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        inner.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(inner);
        inner.AddChild(UiTheme.Label("FERRAMENTAS", 20, UiTheme.Gold, true));
        _toolBox = new VBoxContainer();
        _toolBox.AddThemeConstantOverride("separation", 8);
        inner.AddChild(_toolBox);
        inner.AddChild(UiTheme.Label("MELHORIAS", 20, UiTheme.Cyan, true));
        _grid = new GridContainer { Columns = 1, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _grid.AddThemeConstantOverride("v_separation", 8);
        inner.AddChild(_grid);
        inner.AddChild(UiTheme.Label("EXPEDIÇÕES", 20, UiTheme.Cyan, true));
        _regionBox = new VBoxContainer();
        _regionBox.AddThemeConstantOverride("separation", 8);
        inner.AddChild(_regionBox);
        inner.AddChild(UiTheme.Label("RENASCIMENTO", 20, UiTheme.Gold, true));
        _prestigeBox = new VBoxContainer();
        inner.AddChild(_prestigeBox);

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        var hint = UiTheme.Label("[Esc] / [B] ou [E] / [X] Fechar", 14, UiTheme.Muted);
        hint.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(hint);
        footer.AddChild(UiTheme.Button("Fechar", Close, 160));

        GameState.Instance.MoneyChanged += OnChanged;
        GameState.Instance.UpgradesChanged += OnChanged;
        GameState.Instance.RegionsChanged += OnChanged;
        GameState.Instance.InventoryChanged += OnChanged;
        GameState.Instance.ToolChanged += OnChanged;
    }

    public override void _ExitTree()
    {
        if (GameState.Instance == null) return;
        GameState.Instance.MoneyChanged -= OnChanged;
        GameState.Instance.UpgradesChanged -= OnChanged;
        GameState.Instance.RegionsChanged -= OnChanged;
        GameState.Instance.InventoryChanged -= OnChanged;
        GameState.Instance.ToolChanged -= OnChanged;
    }

    void OnChanged() { if (Visible) Refresh(); }

    string _focusId;
    bool _focusOnOpen;
    ScrollContainer _scroll;
    bool _openingFocus;
    /// <summary>Para testes: rolagem atual da lista.</summary>
    public int ScrollPos => _scroll?.ScrollVertical ?? 0;

    public override void Open()
    {
        _focusOnOpen = true;
        base.Open();
    }

    public override void Refresh()
    {
        // controle: guarda qual melhoria estava em foco para reencontrá-la depois da reconstrução
        var owner = GetViewport()?.GuiGetFocusOwner();
        if (owner != null && owner.HasMeta("uid")) _focusId = (string)owner.GetMeta("uid");
        _openingFocus = _focusOnOpen;
        if (_focusOnOpen)
        {
            _focusOnOpen = false;
            var gs = GameState.Instance;
            // ferramenta quebrada: o foco já vai para comprar outra
            // ao abrir: ferramenta quebrada → comprar outra; senão a próxima ferramenta melhor (fica no topo)
            int next = gs.Data.Tool + 1;
            _focusId = gs.ToolBroken ? "tool_" + gs.Data.Tool
                : gs.OutOfAmmo ? "ammo"
                : next < ToolData.All.Count && gs.ToolAvailable(next) && gs.CanAfford(ToolData.All[next].Cost) ? "tool_" + next
                : UpgradeIds.All.FirstOrDefault(id => gs.UpgradeShown(id) && !gs.IsMaxed(id) && gs.CanAfford(gs.UpgradeCost(id))) ?? "tool_" + gs.Data.Tool;
        }
        // a rolagem fica onde estava (comprar uma melhoria não pode levar a tela lá para baixo)
        int keepScroll = _focusOnOpen ? 0 : _scroll?.ScrollVertical ?? 0;
        RefreshInventory();
        UiTheme.ClearChildren(_toolBox);
        _toolBox.AddChild(BuildCurrentTool());
        if (GameState.Instance.AnyLaserAvailable) _toolBox.AddChild(BuildAmmoRow());
        int shownLocked = 0;
        foreach (var t in ToolData.All)
        {
            bool avail = GameState.Instance.ToolAvailable(t.Index);
            if (!avail && shownLocked++ > 0) break; // só a próxima bloqueada aparece (sem estragar a surpresa)
            _toolBox.AddChild(BuildToolRow(t, avail));
        }
        UiTheme.ClearChildren(_grid);
        foreach (var id in UpgradeIds.All.Where(GameState.Instance.UpgradeShown)) _grid.AddChild(BuildCard(GameData.Upgrades[id]));
        UiTheme.ClearChildren(_regionBox);
        foreach (var r in GameData.Regions.Skip(1)) _regionBox.AddChild(BuildRegionRow(r));
        UiTheme.ClearChildren(_prestigeBox);
        _prestigeBox.AddChild(BuildPrestigeRow());
        if (_scroll != null) Callable.From(() => { if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = keepScroll; }).CallDeferred();
    }

    Control BuildCard(UpgradeDef u)
    {
        var gs = GameState.Instance;
        int lvl = gs.Data.Level(u.Id);
        bool maxed = gs.IsMaxed(u.Id);
        long cost = gs.UpgradeCost(u.Id);
        bool afford = gs.CanAfford(cost);
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.06f, 0.11f, 0.18f, 0.95f), new Color(UiTheme.Cyan, afford && !maxed ? 0.55f : 0.2f), 12, 2, 14, 8));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 14);
        card.AddChild(h);
        var icon = UiTheme.Label(u.Icon, 30, UiTheme.Cyan, true, HorizontalAlignment.Center);
        icon.CustomMinimumSize = new Vector2(40, 0);
        icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(icon);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 3);
        h.AddChild(v);
        v.AddChild(UiTheme.Label(u.Name, 19, UiTheme.Text, true));
        var bar = new SegmentBar { Segments = 10, Fill = (float)lvl / u.MaxLevel, Color = maxed ? UiTheme.Success : UiTheme.Cyan, CustomMinimumSize = new Vector2(170, 11) };
        bar.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        v.AddChild(bar);
        string now = u.Format(u.ValueAt(lvl));
        v.AddChild(UiTheme.Label(maxed ? now : $"{now}  →  {u.Format(u.ValueAt(lvl + 1))}", 14, UiTheme.Muted));

        var lv = UiTheme.Label(maxed ? Loc.T("MÁX") : Loc.F("Nv {0}  ▶  Nv {1}", lvl, lvl + 1), 16, maxed ? UiTheme.Success : UiTheme.Text, true);
        lv.CustomMinimumSize = new Vector2(130, 0);
        lv.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(lv);

        var btn = UiTheme.Button(maxed ? Loc.T("Máximo") : GameState.FormatMoney(cost), () => Buy(u.Id), 150, 18);
        btn.Disabled = maxed || !afford;
        btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        var pill = UiTheme.Chamfer(new Color(0.12f, 0.1f, 0.04f, 0.95f), afford && !maxed ? UiTheme.Gold : new Color(UiTheme.Gold, 0.3f), 8, 2, 12, 6);
        btn.AddThemeStyleboxOverride("normal", pill);
        btn.AddThemeStyleboxOverride("hover", UiTheme.Chamfer(new Color(0.22f, 0.17f, 0.05f, 1f), UiTheme.Gold.Lightened(0.3f), 8, 2, 12, 6));
        btn.AddThemeStyleboxOverride("disabled", UiTheme.Chamfer(new Color(0.06f, 0.06f, 0.08f, 0.8f), new Color(1, 1, 1, 0.1f), 8, 2, 12, 6));
        if (afford && !maxed) btn.AddThemeColorOverride("font_color", UiTheme.Gold);
        h.AddChild(btn);
        btn.SetMeta("uid", u.Id);
        if (u.Id == _focusId && (InputHints.UsingPad || _openingFocus)) Callable.From(() => { if (IsInstanceValid(btn)) btn.GrabFocus(); }).CallDeferred();
        if (u.Id == _lastBought)
        {
            _lastBought = null;
            card.Modulate = new Color(1.8f, 1.6f, 1.0f);
            card.CreateTween().TweenProperty(card, "modulate", Colors.White, 0.45f);
            if (InputHints.UsingPad) Callable.From(() => { if (IsInstanceValid(btn)) btn.GrabFocus(); }).CallDeferred();
        }
        return card;
    }

    /// <summary>Ferramenta na mão: nome, números e durabilidade.</summary>
    Control BuildCurrentTool()
    {
        var gs = GameState.Instance;
        var t = gs.CurrentTool;
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var edge = gs.ToolBroken ? UiTheme.Danger : gs.ToolDurability01 < 0.2f ? UiTheme.Gold : UiTheme.Success;
        card.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.07f, 0.09f, 0.08f, 0.95f), new Color(edge, 0.7f), 12, 2, 14, 8));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 3);
        card.AddChild(v);
        v.AddChild(UiTheme.Label(gs.ToolBroken ? Loc.F("Na mão: {0} — QUEBRADA (minerando com as mãos)", t.Name) : Loc.F("Na mão: {0}", t.Name), 18, gs.ToolBroken ? UiTheme.Danger : UiTheme.Text, true));
        var bar = new SegmentBar { Segments = 20, Fill = gs.ToolDurability01, Color = edge, CustomMinimumSize = new Vector2(260, 10) };
        bar.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        v.AddChild(bar);
        string info = gs.ToolBroken ? Loc.T("Compre outra abaixo (a mesma ou uma melhor).")
            : t.Kind == ToolKind.Laser ? Loc.F("Células de energia: {0} (1 por disparo) · {1} de dano · {2:0.0} disparos/s", gs.Data.Ammo, GameState.FormatNumber(t.Damage), t.Swings)
            : Loc.F("Durabilidade: {0}/{1} golpes · {2} de dano · {3:0.0} golpes/s", gs.ToolDurabilityLeft, gs.ToolMaxDurability, GameState.FormatNumber(t.Damage), t.Swings);
        if (t.Kind == ToolKind.Laser && !gs.ToolBroken) bar.Fill = Mathf.Clamp(gs.Data.Ammo / (float)ToolData.AmmoPack, 0f, 1f);
        v.AddChild(UiTheme.Label(info, 14, gs.OutOfAmmo ? UiTheme.Danger : UiTheme.Muted));
        return card;
    }

    /// <summary>Pacote de células de energia para os lasers.</summary>
    Control BuildAmmoRow()
    {
        var gs = GameState.Instance;
        bool afford = gs.CanAfford(ToolData.AmmoPackCost);
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.08f, 0.07f, 0.03f, 0.95f), new Color(UiTheme.Gold, afford ? 0.6f : 0.25f), 12, 2, 14, 8));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 14);
        card.AddChild(h);
        h.AddChild(HudIcon.Make(HudIcon.Kind.Bolt, UiTheme.Gold, 30));
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        h.AddChild(v);
        v.AddChild(UiTheme.Label(Loc.F("Células de energia ×{0}", ToolData.AmmoPack), 18, UiTheme.Text, true));
        v.AddChild(UiTheme.Label(Loc.F("Munição dos lasers · você tem {0}", gs.Data.Ammo), 14, UiTheme.Muted));
        var btn = UiTheme.Button(GameState.FormatMoney(ToolData.AmmoPackCost), BuyAmmo, 170, 17);
        btn.Disabled = !afford;
        btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (afford) btn.AddThemeColorOverride("font_color", UiTheme.Gold);
        btn.SetMeta("uid", "ammo");
        if (_focusId == "ammo" && (InputHints.UsingPad || _openingFocus) && !btn.Disabled) Callable.From(() => { if (IsInstanceValid(btn)) btn.GrabFocus(); }).CallDeferred();
        h.AddChild(btn);
        return card;
    }

    void BuyAmmo()
    {
        _focusId = "ammo";
        if (GameState.Instance.TryBuyAmmo()) AudioManager.Instance?.Play("buy", -3f);
        else AudioManager.Instance?.Play("error", -6f);
    }

    Control BuildToolRow(ToolDef t, bool avail)
    {
        var gs = GameState.Instance;
        bool current = t.Index == gs.Data.Tool;
        bool afford = gs.CanAfford(t.Cost);
        var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        card.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.06f, 0.11f, 0.18f, 0.95f), new Color(current ? UiTheme.Gold : UiTheme.Cyan, avail && afford ? 0.55f : 0.2f), 12, 2, 14, 8));
        if (!avail) card.Modulate = new Color(1, 1, 1, 0.6f);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 14);
        card.AddChild(h);
        var dot = new ColorRect { Color = ToolData.HeadColor(t), CustomMinimumSize = new Vector2(16, 16), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        h.AddChild(dot);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 2);
        h.AddChild(v);
        string kind = t.Kind switch { ToolKind.Drill => Loc.T("broca"), ToolKind.Laser => Loc.T("laser"), _ => Loc.T("picareta") };
        v.AddChild(UiTheme.Label(t.Name + (current ? "  ★" : ""), 18, UiTheme.Text, true));
        v.AddChild(UiTheme.Label(avail ? Loc.F("{0} de dano · {1:0.0} golpes/s · {2} golpes · {3}", GameState.FormatNumber(t.Damage), t.Swings, t.Durability, kind)
                                       : Loc.F("Libere a região {0} para comprar", GameData.Regions[Math.Clamp(t.Region, 0, GameData.Regions.Count - 1)].Name), 14, UiTheme.Muted));
        if (avail) v.AddChild(UiTheme.Label(t.Description, 13, UiTheme.Muted));
        string label = !avail ? Loc.T("Bloqueada") : current && !gs.ToolBroken ? Loc.F("Outra · {0}", GameState.FormatMoney(t.Cost)) : GameState.FormatMoney(t.Cost);
        var btn = UiTheme.Button(label, () => BuyTool(t.Index), 170, 17);
        btn.Disabled = !avail || !afford;
        btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        if (avail && afford) btn.AddThemeColorOverride("font_color", UiTheme.Gold);
        h.AddChild(btn);
        string uid = "tool_" + t.Index;
        btn.SetMeta("uid", uid);
        if (uid == _focusId && (InputHints.UsingPad || _openingFocus) && !btn.Disabled) Callable.From(() => { if (IsInstanceValid(btn)) btn.GrabFocus(); }).CallDeferred();
        return card;
    }

    void BuyTool(int i)
    {
        _focusId = "tool_" + i;
        if (GameState.Instance.TryBuyTool(i)) AudioManager.Instance?.Play("buy", -3f);
        else AudioManager.Instance?.Play("error", -6f);
    }

    void Buy(string id)
    {
        var gs = GameState.Instance;
        _lastBought = id;
        if (gs.TryBuyUpgrade(id))
        {
            AudioManager.Instance?.Play("buy", -3f);
            var u = GameData.Upgrades[id];
            gs.Notify("Melhoria adquirida", Loc.F("{0} — Nível {1}", u.Name, gs.Data.Level(id)), NotifyKind.Success);
        }
        else AudioManager.Instance?.Play("error", -6f);
    }

    Control BuildPrestigeRow()
    {
        var gs = GameState.Instance;
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.08f, 0.07f, 0.03f, 0.95f), new Color(UiTheme.Gold, gs.CanPrestige ? 0.8f : 0.3f), 12, 2, 14, 8));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        row.AddChild(h);
        h.AddChild(HudIcon.Make(HudIcon.Kind.Star, UiTheme.Gold, 34));
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        h.AddChild(v);
        v.AddChild(UiTheme.Label(Loc.F("Essência Ancestral: {0}   ·   bônus x{1:0.0} em dano e venda", gs.Data.Essence, gs.EssenceMultiplier), 17, UiTheme.Gold, true));
        string info = gs.Data.GameCompleted
            ? Loc.F("Renascer recomeça a jornada e concede +{0} Essência (+10% permanente por ponto). Coleção e estatísticas são mantidas.", gs.PendingEssence)
            : Loc.T("Disponível após concluir a jornada (4 regiões + Coração Ancestral).");
        var l = UiTheme.Label(info, 13, UiTheme.Muted);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(380, 0);
        v.AddChild(l);
        var btn = UiTheme.Button(gs.CanPrestige ? Loc.F("Renascer (+{0})", gs.PendingEssence) : Loc.T("Bloqueado"), () => UiBus.RequestPrestige(), 200, 17);
        btn.Disabled = !gs.CanPrestige;
        btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(btn);
        return row;
    }

    Control BuildRegionRow(RegionDef r)
    {
        var gs = GameState.Instance;
        bool unlocked = gs.IsUnlocked(r.Id);
        bool can = gs.CanUnlock(r);
        bool prev = gs.IsUnlocked(GameData.Regions[r.Index - 1].Id);
        var row = new PanelContainer();
        row.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.06f, 0.1f, 0.16f, 0.95f), new Color(r.AccentColor, unlocked ? 0.25f : 0.65f), 12, 2, 14, 8));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        row.AddChild(h);
        h.AddChild(HudIcon.Make(HudIcon.Kind.Region, r.AccentColor, 34));
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        h.AddChild(v);
        v.AddChild(UiTheme.Label(r.Name, 19, r.AccentColor.Lightened(0.25f), true));
        var mats = string.Join(", ", r.Spawns.Select(s => GameData.Mat(s.MaterialId).Name)) + Loc.F(" · raro: {0}", GameData.Mat(r.RareMaterialId).Name);
        var ml = UiTheme.Label(r.Subtitle + "  " + mats, 13, UiTheme.Muted);
        ml.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ml.CustomMinimumSize = new Vector2(380, 0);
        v.AddChild(ml);
        string label = unlocked ? Loc.T("Desbloqueada") : !prev ? Loc.T("Requer região anterior") : GameState.FormatMoney(r.UnlockCost);
        var btn = UiTheme.Button(label, () =>
        {
            if (gs.TryUnlockRegion(r.Id)) AudioManager.Instance?.Play("buy", -3f);
            else AudioManager.Instance?.Play("error", -6f);
        }, 220, 17);
        btn.Disabled = unlocked || !can;
        btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(btn);
        return row;
    }
}

// =====================================================================
/// <summary>Aba Vender: detalhamento por material e venda total.</summary>
public partial class SellScreen : TerminalScreen
{
    protected override bool IsShopTab => false;
    VBoxContainer _list;
    Label _total, _mult;
    Button _sellBtn;

    public override void _Ready()
    {
        BuildTerminal(new Vector2(1060, 600));
        var right = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        right.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.04f, 0.07f, 0.12f, 0.9f), new Color(UiTheme.Success, 0.45f), 14, 2, 16, 12));
        Content.AddChild(right);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        right.AddChild(v);
        v.AddChild(UiTheme.Label("ESTAÇÃO DE VENDA", 24, UiTheme.Success, true));
        _mult = UiTheme.Label("", 15, UiTheme.Muted);
        v.AddChild(_mult);
        var scroll = new ScrollContainer { FollowFocus = true, CustomMinimumSize = new Vector2(0, 280), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        v.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(_list);
        var totalRow = new HBoxContainer();
        v.AddChild(totalRow);
        var tl = UiTheme.Label("TOTAL", 22, UiTheme.Text, true);
        tl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        totalRow.AddChild(tl);
        _total = UiTheme.Label("", 30, UiTheme.Gold, true);
        totalRow.AddChild(_total);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 10);
        v.AddChild(buttons);
        buttons.AddChild(UiTheme.Button("Fechar", Close, 140));
        _sellBtn = UiTheme.Button("VENDER TUDO", DoSell, 260, 22);
        _sellBtn.AddThemeColorOverride("font_color", UiTheme.Success);
        _sellBtn.AddThemeStyleboxOverride("normal", UiTheme.Chamfer(new Color(0.05f, 0.25f, 0.18f), UiTheme.Success, 10, 2, 14, 8));
        _sellBtn.AddThemeStyleboxOverride("hover", UiTheme.Chamfer(new Color(0.08f, 0.35f, 0.25f), UiTheme.Success.Lightened(0.3f), 10, 2, 14, 8));
        buttons.AddChild(_sellBtn);
    }

    public override void Open()
    {
        base.Open();
        _sellBtn.GrabFocus();
    }

    public override void Refresh()
    {
        RefreshInventory();
        var gs = GameState.Instance;
        UiTheme.ClearChildren(_list);
        _mult.Text = Loc.F("Multiplicador de lapidação: x{0:0.00}   ·   Mochila {1}/{2}", gs.ValueMultiplier, gs.InventoryCount, gs.Capacity);
        if (gs.InventoryCount == 0)
            _list.AddChild(UiTheme.Label("Sua mochila está vazia. Minere cristais e volte aqui!", 17, UiTheme.Muted));
        foreach (var kv in gs.Data.Inventory.OrderByDescending(k => GameData.Mat(k.Key)?.Value ?? 0))
        {
            var m = GameData.Mat(kv.Key);
            if (m == null) continue;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            row.AddChild(HudIcon.MakeOre(m, 24));
            var n = UiTheme.Label(m.Name, 18, m.Glow.Lightened(0.4f), true);
            row.AddChild(n);
            var rb = UiTheme.Label(RarityInfo.Badge(m.Rarity), 13, RarityInfo.Color(m.Rarity), true);
            rb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            rb.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(rb);
            row.AddChild(UiTheme.Label($"{kv.Value} × {GameState.FormatMoney(gs.SellPrice(m))}", 16, UiTheme.Muted));
            var sub = UiTheme.Label(GameState.FormatMoney(gs.SellPrice(m) * kv.Value), 18, UiTheme.Gold, true, HorizontalAlignment.Right);
            sub.CustomMinimumSize = new Vector2(130, 0);
            row.AddChild(sub);
            _list.AddChild(row);
        }
        _total.Text = GameState.FormatMoney(gs.InventoryValue());
        _sellBtn.Disabled = gs.InventoryCount == 0;
    }
}
