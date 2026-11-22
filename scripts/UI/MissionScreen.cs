using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>Quadro de encomendas da Vila dos Ferreiros: entregar N fragmentos de um minério por uma recompensa maior que a venda.</summary>
public partial class MissionScreen : ModalScreen
{
    VBoxContainer _list;
    Label _info;

    public override void _Ready()
    {
        BuildFrame("ENCOMENDAS DA VILA", new Vector2(760, 520), UiTheme.Gold);
        Layer = 20;
        _info = UiTheme.Label("Os ferreiros pagam o dobro do valor de venda (e um bônus) por estas encomendas. Encomendas entregues são trocadas por novas no dia seguinte.", 15, UiTheme.Muted);
        _info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Body.AddChild(_info);
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

    void OnChanged() { if (IsOpen) Refresh(); }

    public override void Refresh()
    {
        var gs = GameState.Instance;
        gs.EnsureContracts();
        UiTheme.ClearChildren(_list);
        Button first = null;
        for (int i = 0; i < gs.Data.Contracts.Count; i++)
        {
            int slot = i;
            var c = gs.Data.Contracts[i];
            var m = GameData.Mat(c.Mat);
            if (m == null) continue;
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(UiTheme.Card, new Color(c.Done ? UiTheme.Success : UiTheme.Gold, 0.35f), 8, 1, 14, 8));
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 14);
            card.AddChild(h);
            h.AddChild(HudIcon.MakeOre(m, 52));
            var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            h.AddChild(v);
            v.AddChild(UiTheme.Label(Loc.F("Entregar {0}x {1}", c.Amount, m.Name), 19, UiTheme.Text, true));
            int have = gs.InventoryOf(c.Mat);
            v.AddChild(UiTheme.Label(c.Done ? Loc.T("Entregue! Nova encomenda amanhã.") : Loc.F("Na mochila: {0}/{1}", have, c.Amount), 15, c.Done ? UiTheme.Success : have >= c.Amount ? UiTheme.Success : UiTheme.Muted));
            v.AddChild(UiTheme.Label(Loc.F("Recompensa: {0}", GameState.FormatMoney(c.Reward)), 15, UiTheme.Gold));
            var btn = UiTheme.Button(c.Done ? "Entregue" : "Entregar", () =>
            {
                if (GameState.Instance.TryDeliver(slot)) Audio.AudioManager.Instance?.Play("buy", -4f);
            }, 150, 18);
            btn.Disabled = c.Done || !gs.CanDeliver(slot);
            btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            btn.SetMeta("contract", slot);
            h.AddChild(btn);
            if (first == null && !btn.Disabled) first = btn;
            _list.AddChild(card);
        }
        if (first != null) first.CallDeferred(Control.MethodName.GrabFocus);
    }
}
