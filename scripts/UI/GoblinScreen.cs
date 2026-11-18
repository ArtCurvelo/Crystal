using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.World;
using Godot;
using GU = CrystalExcavator.Core.GameState.GoblinUpgrade;

namespace CrystalExcavator.UI;

/// <summary>Conversa com o goblin contratado: estado da cabana, horário de trabalho e melhorias.</summary>
public partial class GoblinScreen : ModalScreen
{
    public GoblinCamp Camp;
    Label _status;
    VBoxContainer _rows;

    public override void _Ready()
    {
        BuildFrame("GOBLIN", new Vector2(760, 0), new Color(0.6f, 1f, 0.45f));
        _status = UiTheme.Label("", 16, UiTheme.Text);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(700, 0);
        Body.AddChild(_status);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 8);
        Body.AddChild(_rows);
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        footer.AddChild(UiTheme.Button("Fechar", Close, 160));
    }

    public void Show(GoblinCamp camp)
    {
        Camp = camp;
        Open();
    }

    public override void Refresh()
    {
        if (Camp == null) return;
        var gs = GameState.Instance;
        string id = Camp.RegionId;
        var def = Camp.Def;
        var g = gs.GoblinData(id);
        float prog = gs.GoblinBuildProgress(id);
        float frac = gs.GoblinWorkFraction(id);
        int dayMin = (int)Mathf.Round(gs.DayLength / 60f);
        string hut = prog < 1f
            ? Loc.F("Montando a cabana: {0:0}% (começa a minerar quando terminar).", prog * 100f)
            : Loc.F("Cabana nível {0}: trabalha {1:0}% do turno ({2:0.#} de {3} min).", g.HutLevel + 1, frac * 100f, frac * dayMin, dayMin);
        _status.Text = $"{def.Name}, {def.Title}\n{hut}\n" +
                       Loc.F("Hoje: {0} minerados · baú: {1} fragmentos · taxa do dia: {2}", GameState.FormatMoney(g.DayValue), gs.GoblinStashCount(id), GameState.FormatMoney(gs.GoblinFee(id)));
        UiTheme.ClearChildren(_rows);
        int L(GU u) => gs.GoblinLevel(id, u);
        Row(GU.Hut, "Cabana", Loc.F("Trabalha mais tempo por dia: {0:0}% → {1:0}% do turno",
            frac * 100f, GameState.GoblinWorkFractions[System.Math.Min(L(GU.Hut) + 1, GameState.GoblinMaxLevel)] * 100f));
        Row(GU.Carry, "Mochila do goblin", Loc.F("Carrega mais por viagem: {0} → {1} fragmentos",
            GameState.GoblinCarryLevels[L(GU.Carry)], GameState.GoblinCarryLevels[System.Math.Min(L(GU.Carry) + 1, GameState.GoblinMaxLevel)]));
        Row(GU.Speed, "Picareta", Loc.F("Quebra mais rápido: {0:0} → {1:0} de dano/s",
            def.MineDps * GameState.GoblinSpeedLevels[L(GU.Speed)], def.MineDps * GameState.GoblinSpeedLevels[System.Math.Min(L(GU.Speed) + 1, GameState.GoblinMaxLevel)]));
        Callable.From(FocusFirst).CallDeferred();
    }

    void Row(GU u, string name, string desc)
    {
        var gs = GameState.Instance;
        string id = Camp.RegionId;
        int lv = gs.GoblinLevel(id, u);
        bool maxed = lv >= GameState.GoblinMaxLevel;
        long cost = gs.GoblinUpgradeCost(id, u);
        bool built = gs.GoblinBuilt(id);
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.05f, 0.08f, 0.12f, 0.9f), new Color(0.5f, 0.9f, 0.4f, 0.6f), 8, 1, 12, 8));
        _rows.AddChild(panel);
        var h = new HBoxContainer();
        h.AddThemeConstantOverride("separation", 12);
        panel.AddChild(h);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        h.AddChild(v);
        v.AddChild(UiTheme.Label(Loc.T(name) + "  " + new string('●', lv + 1) + new string('○', GameState.GoblinMaxLevel - lv), 18, UiTheme.Text, true));
        var d = UiTheme.Label(maxed ? Loc.T("Nível máximo") : desc, 14, UiTheme.Muted);
        v.AddChild(d);
        var b = UiTheme.Button(maxed ? "MÁX" : !built ? "Cabana em montagem" : GameState.FormatMoney(cost), () =>
        {
            if (gs.TryUpgradeGoblin(id, u)) { AudioManager.Instance?.Play("buy", -4f); Camp.SyncState(); }
            else AudioManager.Instance?.Play("error", -6f);
            Refresh();
        }, 220, 18);
        b.Disabled = maxed || !built || !gs.CanAfford(cost);
        h.AddChild(b);
    }
}
