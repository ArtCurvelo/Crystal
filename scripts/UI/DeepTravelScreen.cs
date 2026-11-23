using System;
using System.Linq;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>No portão da Catedral: escolher para qual Profundeza já aberta descer.</summary>
public partial class DeepTravelScreen : ModalScreen
{
    VBoxContainer _list;
    public Action<RegionDef> Chosen;

    public override void _Ready()
    {
        BuildFrame("PROFUNDEZAS PROIBIDAS", new Vector2(620, 520), new Color(1f, 0.45f, 0.4f));
        Layer = 20;
        var info = UiTheme.Label("Escolha para onde descer. Cada área tem um Guardião que guarda a passagem para a próxima.", 15, UiTheme.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Body.AddChild(info);
        _list = new VBoxContainer();
        _list.AddThemeConstantOverride("separation", 8);
        Body.AddChild(_list);
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        footer.AddChild(UiTheme.Button("Voltar", Close, 160));
    }

    public override void Refresh()
    {
        var gs = GameState.Instance;
        UiTheme.ClearChildren(_list);
        Button first = null;
        int n = 0;
        foreach (var r in GameData.Deep)
        {
            n++;
            bool open = n == 1 || gs.IsUnlocked(r.Id);
            string state = gs.GuardianDefeated(r.Id) ? Loc.T("Guardião derrotado") : Loc.T("Guardião vivo");
            var b = UiTheme.Button(open ? Loc.F("{0}. {1}  ·  {2}", n, r.Name, state) : Loc.F("{0}. ???  ·  derrote o Guardião anterior", n), () => { var rr = r; Close(); Chosen?.Invoke(rr); }, 520, 18);
            b.Disabled = !open;
            b.SetMeta("deep", r.Id);
            _list.AddChild(b);
            if (open) first = b;
        }
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }
}
