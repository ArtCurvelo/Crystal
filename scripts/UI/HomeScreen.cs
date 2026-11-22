using System;
using System.Collections.Generic;
using System.Linq;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>
/// Casa do escavador (fim do dia): maquete 3D da casa, resumo do dia,
/// melhorias da casa, árvore de habilidades e "Dormir" para começar o próximo dia.
/// </summary>
public partial class HomeScreen : ModalScreen
{
    public Action OnSleep;
    Label _title, _money, _stats, _warn;
    Button _sleep, _tabHouse, _tabSkills;
    VBoxContainer _housePane, _skillPane;
    SubViewport _viewport;
    HouseDiorama _diorama;
    readonly List<(HouseUpgradeDef def, Label level, Label effect, Button buy)> _houseRows = new();
    SkillTreeView _tree;
    Control _left;
    Label _skillName, _skillDesc;
    Button _skillBuy;
    string _selectedSkill = SkillIds.Magnet;
    bool _showSkills;

    public override void _Ready()
    {
        BuildFrame("", new Vector2(1180, 0), UiTheme.Gold);
        Layer = 22;
        Panel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.05f, 0.04f, 0.07f, 0.99f), UiTheme.Gold, 18, 3, 22, 16));
        Body.AddThemeConstantOverride("separation", 10);

        // cabeçalho
        var head = new HBoxContainer();
        Body.AddChild(head);
        head.AddChild(HudIcon.Make(HudIcon.Kind.Star, UiTheme.Gold, 34));
        var hv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        hv.AddThemeConstantOverride("separation", -4);
        head.AddChild(hv);
        hv.AddChild(UiTheme.Label("CASA", 14, UiTheme.Muted, true));
        _title = UiTheme.Label("", 28, UiTheme.Gold, true);
        hv.AddChild(_title);
        head.AddChild(HudIcon.Make(HudIcon.Kind.Coin, UiTheme.Gold, 30));
        _money = UiTheme.Label("", 26, UiTheme.Gold, true);
        _money.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        head.AddChild(_money);
        Body.AddChild(new ColorRect { Color = new Color(UiTheme.Gold, 0.35f), CustomMinimumSize = new Vector2(0, 2) });

        var cols = new HBoxContainer();
        cols.AddThemeConstantOverride("separation", 18);
        Body.AddChild(cols);

        // ---- esquerda: maquete + resumo do dia
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(500, 0) };
        _left = left;
        left.AddThemeConstantOverride("separation", 8);
        cols.AddChild(left);
        var vpc = new SubViewportContainer { Stretch = true, CustomMinimumSize = new Vector2(500, 300) };
        left.AddChild(vpc);
        _viewport = new SubViewport { OwnWorld3D = true, Size = new Vector2I(500, 300), Msaa3D = Viewport.Msaa.Disabled, RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled };
        vpc.AddChild(_viewport);
        _diorama = new HouseDiorama { Name = "HouseDiorama" };
        _viewport.AddChild(_diorama);
        var summary = new PanelContainer();
        summary.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.08f, 0.07f, 0.1f, 0.95f), new Color(UiTheme.Gold, 0.4f), 10, 1, 14, 10));
        left.AddChild(summary);
        var sv = new VBoxContainer();
        summary.AddChild(sv);
        _stats = UiTheme.Label("", 17, UiTheme.Text);
        sv.AddChild(_stats);
        _warn = UiTheme.Label("", 15, new Color(1f, 0.7f, 0.35f));
        _warn.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _warn.CustomMinimumSize = new Vector2(460, 0);
        sv.AddChild(_warn);

        // ---- direita: abas
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(600, 470) };
        right.AddThemeConstantOverride("separation", 8);
        cols.AddChild(right);
        var tabs = new HBoxContainer();
        tabs.AddThemeConstantOverride("separation", 8);
        right.AddChild(tabs);
        _tabHouse = UiTheme.Button("Melhorias da casa", () => SetTab(false), 260, 18);
        _tabSkills = UiTheme.Button("Árvore de habilidades", () => SetTab(true), 260, 18);
        tabs.AddChild(_tabHouse);
        tabs.AddChild(_tabSkills);

        _housePane = new VBoxContainer();
        _housePane.AddThemeConstantOverride("separation", 6);
        right.AddChild(_housePane);
        foreach (var def in HomeData.House) _housePane.AddChild(HouseRow(def));

        _skillPane = new VBoxContainer { Visible = false };
        _skillPane.AddThemeConstantOverride("separation", 8);
        right.AddChild(_skillPane);
        BuildSkillTree();

        var bottom = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        bottom.AddThemeConstantOverride("separation", 10);
        Body.AddChild(bottom);
        bottom.AddChild(UiTheme.Button("Fechar", Close, 160, 20));
        _sleep = UiTheme.Button("Dormir", () => OnSleep?.Invoke(), 360, 22);
        _sleep.AddThemeColorOverride("font_color", UiTheme.Gold);
        bottom.AddChild(_sleep);

        GameState.Instance.MoneyChanged += OnChanged;
        GameState.Instance.HomeChanged += OnChanged;
    }

    public override void _ExitTree()
    {
        if (GameState.Instance == null) return;
        GameState.Instance.MoneyChanged -= OnChanged;
        GameState.Instance.HomeChanged -= OnChanged;
    }

    void OnChanged() { if (Visible) Refresh(); }

    Control HouseRow(HouseUpgradeDef def)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(UiTheme.Card, new Color(UiTheme.Gold, 0.25f), 8, 1, 12, 6));
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 10);
        card.AddChild(h);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 0);
        h.AddChild(v);
        var top = new HBoxContainer();
        v.AddChild(top);
        top.AddChild(UiTheme.Label(def.Name, 18, UiTheme.Text, true));
        var level = UiTheme.Label("", 16, UiTheme.Gold, true);
        top.AddChild(level);
        // a descrição quebra a linha (textos longos passavam da borda da tela)
        var desc = UiTheme.Label(def.Description, 14, UiTheme.Muted);
        desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        desc.CustomMinimumSize = new Vector2(200, 0);
        v.AddChild(desc);
        var effect = UiTheme.Label("", 14, UiTheme.Success);
        v.AddChild(effect);
        Button buy = null;
        buy = UiTheme.Button("", () => { if (GameState.Instance.TryBuyHouse(def.Id)) Audio.AudioManager.Instance?.Play("buy", -4f); }, 150, 18);
        buy.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(buy);
        _houseRows.Add((def, level, effect, buy));
        return card;
    }

    void BuildSkillTree()
    {
        _tree = new SkillTreeView { OnSelect = Select, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        _skillPane.AddChild(_tree);
        var detail = new PanelContainer();
        detail.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(UiTheme.Card, new Color(UiTheme.Cyan, 0.35f), 8, 1, 14, 8));
        _skillPane.AddChild(detail);
        var dh = new HBoxContainer();
        dh.AddThemeConstantOverride("separation", 10);
        detail.AddChild(dh);
        var dv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        dh.AddChild(dv);
        _skillName = UiTheme.Label("", 19, UiTheme.Text, true);
        dv.AddChild(_skillName);
        _skillDesc = UiTheme.Label("", 15, UiTheme.Muted);
        _skillDesc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _skillDesc.CustomMinimumSize = new Vector2(380, 0);
        dv.AddChild(_skillDesc);
        _skillBuy = UiTheme.Button("", BuySelected, 170, 18);
        _skillBuy.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        dh.AddChild(_skillBuy);
    }

    void Select(string id)
    {
        _selectedSkill = id;
        Refresh();
    }

    void BuySelected()
    {
        if (GameState.Instance.TryBuySkill(_selectedSkill)) Audio.AudioManager.Instance?.Play("unlock", -4f);
    }

    protected override void OnTab(int dir) => SetTab(!_showSkills);

    void SetTab(bool skills)
    {
        _showSkills = skills;
        Refresh();
    }

    public override void Open()
    {
        base.Open();
        _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        _diorama.Rebuild();
        Audio.AudioManager.Instance?.Play("unlock", -6f);
    }

    public override void _Process(double delta)
    {
        // a maquete só é desenhada com a casa aberta
        if (!Visible && _viewport != null && _viewport.RenderTargetUpdateMode != SubViewport.UpdateMode.Disabled)
            _viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
    }

    public override void Refresh()
    {
        var gs = GameState.Instance;
        var d = gs.Data;
        _title.Text = gs.DayOver ? Loc.F("FIM DO DIA {0}", d.Day) : Loc.F("DIA {0}", d.Day);
        _sleep.Visible = gs.DayOver;
        _money.Text = GameState.FormatNumber(d.Money);
        _stats.Text =
            Loc.F("Ganho no dia: {0}", GameState.FormatMoney(d.DayEarned)) +
            (d.DayBonus > 0 ? Loc.F("   ·   Prêmio do turno: +{0}", GameState.FormatMoney(d.DayBonus)) : "") +
            Loc.F("\nDepósitos quebrados: {0}   ·   Turno: {1:0.#} min", GameState.FormatNumber(d.DayCrystals), gs.DayLength / 60f);
        _warn.Text = gs.InventoryCount > 0 ? Loc.F("Você trouxe {0} fragmentos sem vender. Eles ficam na mochila para amanhã.", gs.InventoryCount) : "";
        _warn.Visible = gs.InventoryCount > 0;
        _sleep.Text = Loc.F("Dormir e começar o Dia {0}", d.Day + 1);

        _housePane.Visible = !_showSkills;
        _skillPane.Visible = _showSkills;
        _left.Visible = !_showSkills; // a árvore usa a largura toda
        _tabHouse.AddThemeColorOverride("font_color", _showSkills ? UiTheme.Muted : UiTheme.Gold);
        _tabSkills.AddThemeColorOverride("font_color", _showSkills ? UiTheme.Gold : UiTheme.Muted);

        foreach (var (def, level, effect, buy) in _houseRows)
        {
            int lv = gs.HouseLevel(def.Id);
            level.Text = "   " + new string('●', lv) + new string('○', def.MaxLevel - lv);
            effect.Text = lv == 0 ? Loc.T("Ainda não construído") : Loc.T("Atual: ") + HouseEffect(def, lv);
            bool maxed = gs.HouseMaxed(def.Id);
            long cost = gs.HouseCost(def.Id);
            buy.Text = maxed ? Loc.T("MÁXIMO") : (lv == 0 ? Loc.T("Construir ") : "") + GameState.FormatMoney(cost);
            buy.Disabled = maxed || !gs.CanAfford(cost);
        }

        _tree.Selected = _selectedSkill;
        _tree.Refresh();
        var sel = HomeData.Skill(_selectedSkill);
        _skillName.Text = sel.Name + (gs.HasSkill(sel.Id) ? "  ✓" : "");
        string req = sel.Requires != null && !gs.HasSkill(sel.Requires) ? Loc.F("\nRequer: {0}", HomeData.Skill(sel.Requires).Name) : "";
        _skillDesc.Text = sel.Description + req;
        _skillBuy.Text = gs.HasSkill(sel.Id) ? Loc.T("ADQUIRIDA") : Loc.T("Aprender ") + GameState.FormatMoney(sel.Cost);
        _skillBuy.Disabled = !gs.SkillAvailable(sel.Id) || !gs.CanAfford(sel.Cost);
    }

    static string HouseEffect(HouseUpgradeDef def, int lv) => def.Id switch
    {
        HouseIds.Bed => Loc.F("+{0} min de turno", lv),
        _ => $"+{lv * def.PerLevel * 100:0}%",
    };

    // usado pelo bot/testes
    public void ShowSkillsTab() => SetTab(true);
    public void ShowHouseTab() => SetTab(false);
    public void SelectSkill(string id) => Select(id);
}
