using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>
/// Quadro de encomendas da Vila dos Ferreiros. O jogador ACEITA uma missão por vez (só de dia): ela fica marcada
/// na mina e precisa ser entregue aqui antes de dormir. Se não for, a Guilda cobra uma multa (o que faltar vira dívida).
/// </summary>
public partial class MissionScreen : ModalScreen
{
    VBoxContainer _list;
    Label _info, _status;

    public override void _Ready()
    {
        BuildFrame("ENCOMENDAS DA VILA", new Vector2(760, 520), UiTheme.Gold);
        Layer = 20;
        _info = UiTheme.Label("Aceite UMA missão por vez: ela fica marcada na mina e precisa ser entregue aqui antes de você dormir. Os ferreiros pagam o dobro do valor de venda, mas quem falha paga multa à Guilda.", 15, UiTheme.Muted);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Body.AddChild(_info);
        _status = UiTheme.Label("", 16, UiTheme.Gold, true);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Body.AddChild(_status);
        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 10);
        Body.AddChild(_list);
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        footer.AddChild(UiTheme.Button("Voltar", Close, 160));
        if (GameState.Instance != null) { GameState.Instance.ContractsChanged += OnChanged; GameState.Instance.InventoryChanged += OnChanged; }
    }

    public override void _ExitTree()
    {
        if (GameState.Instance != null) { GameState.Instance.ContractsChanged -= OnChanged; GameState.Instance.InventoryChanged -= OnChanged; }
    }

    bool _refreshing;
    void OnChanged() { if (IsOpen && !_refreshing) Refresh(); }

    public override void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try { Build(); } finally { _refreshing = false; }
    }

    void Build()
    {
        var gs = GameState.Instance;
        gs.EnsureContracts();
        UiTheme.ClearChildren(_list);
        Button first = null;
        int active = gs.ActiveContract;
        _status.Text = (active >= 0 ? Loc.T("Missão em andamento: entregue antes de dormir.") : gs.Data.DayOver ? Loc.T("O turno acabou: de noite só dá para aceitar a missão que você já pode entregar.") : Loc.T("Nenhuma missão aceita."))
                       + (gs.Data.Debt > 0 ? "   " + Loc.F("Dívida com a Guilda: {0}", GameState.FormatMoney(gs.Data.Debt)) : "");
        _status.AddThemeColorOverride("font_color", gs.Data.Debt > 0 ? new Color(1f, 0.45f, 0.4f) : UiTheme.Gold);
        for (int i = 0; i < gs.Data.Contracts.Count; i++)
        {
            int slot = i;
            var c = gs.Data.Contracts[i];
            var m = GameData.Mat(c.Mat);
            if (m == null) continue;
            var card = new PanelContainer();
            var edge = c.Failed ? new Color(1f, 0.4f, 0.35f) : c.Done ? UiTheme.Success : c.Accepted ? UiTheme.Cyan : UiTheme.Gold;
            card.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(UiTheme.Card, new Color(edge, c.Accepted && !c.Done ? 0.8f : 0.35f), 8, c.Accepted && !c.Done ? 2 : 1, 14, 8));
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 14);
            card.AddChild(h);
            h.AddChild(HudIcon.MakeOre(m, 52));
            var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            h.AddChild(v);
            v.AddChild(UiTheme.Label(Loc.F("Entregar {0}x {1}", c.Amount, m.Name), 19, UiTheme.Text, true));
            int have = gs.InventoryOf(c.Mat);
            string line = c.Failed ? Loc.T("Fracassada: a Guilda cobrou a multa. Nova missão amanhã.")
                : c.Done ? Loc.T("Entregue! Nova encomenda amanhã.")
                : Loc.F("Na mochila: {0}/{1}", have, c.Amount) + (c.Accepted ? "   ·   " + Loc.T("ACEITA: entregue antes de dormir") : "");
            v.AddChild(UiTheme.Label(line, 15, c.Failed ? new Color(1f, 0.5f, 0.45f) : c.Done ? UiTheme.Success : c.Accepted ? UiTheme.Cyan : have >= c.Amount ? UiTheme.Success : UiTheme.Muted));
            v.AddChild(UiTheme.Label(Loc.F("Recompensa: {0}   ·   Multa se falhar: {1}", GameState.FormatMoney(c.Reward), GameState.FormatMoney(GameState.ContractFine(c))), 15, UiTheme.Gold));
            bool accepted = c.Accepted && !c.Done;
            var btn = UiTheme.Button(c.Failed ? "Fracassada" : c.Done ? "Entregue" : accepted ? "Entregar" : "Aceitar", () =>
            {
                var g = GameState.Instance;
                if (g.Data.Contracts[slot].Accepted) { if (g.TryDeliver(slot)) Audio.AudioManager.Instance?.Play("buy", -4f); }
                else if (g.TryAccept(slot)) Audio.AudioManager.Instance?.Play("unlock", -6f);
            }, 150, 18);
            btn.Disabled = c.Done || (accepted ? !gs.CanDeliver(slot) : !gs.CanAccept(slot));
            btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            btn.SetMeta("contract", slot);
            h.AddChild(btn);
            if (first == null && !btn.Disabled) first = btn;
            _list.AddChild(card);
        }
        if (first != null) first.CallDeferred(Control.MethodName.GrabFocus);
    }
}
