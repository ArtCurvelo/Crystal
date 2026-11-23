using System;
using System.Collections.Generic;
using System.Linq;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>Forja do ferreiro: forjar capacete, luvas, botas e bolsa com minérios, e consertar a ferramenta.</summary>
public partial class ForgeScreen : ModalScreen
{
    VBoxContainer _list;
    bool _refreshing;
    /// <summary>Bancada de combinação: 0 espada, 1 martelo, 2 armadura; minérios escolhidos em cada espaço.</summary>
    int _craftKind;
    readonly List<string> _picks = new() { null, null };

    public override void _Ready()
    {
        BuildFrame("FORJA DO FERREIRO", new Vector2(860, 600), new Color(1f, 0.55f, 0.25f));
        Layer = 20;
        var info = UiTheme.Label("Traga os minérios na mochila e pague a taxa: o ferreiro forja peças que melhoram você para sempre (até renascer). Cada nível usa minérios de uma região.", 15, UiTheme.Muted);
        info.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Body.AddChild(info);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 430), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Body.AddChild(scroll);
        _list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        Body.AddChild(footer);
        footer.AddChild(UiTheme.Button("Voltar", Close, 160));
        if (GameState.Instance != null) { GameState.Instance.GearChanged += OnChanged; GameState.Instance.InventoryChanged += OnChanged; GameState.Instance.MoneyChanged += OnChanged; }
    }

    public override void _ExitTree()
    {
        if (GameState.Instance != null) { GameState.Instance.GearChanged -= OnChanged; GameState.Instance.InventoryChanged -= OnChanged; GameState.Instance.MoneyChanged -= OnChanged; }
    }

    void OnChanged() { if (IsOpen && !_refreshing) Refresh(); }

    public override void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try { Build(); } finally { _refreshing = false; }
    }

    static string CostLine(IEnumerable<ForgeCost> costs, long fee, out bool ok)
    {
        var gs = GameState.Instance;
        ok = gs.CanAfford(fee);
        var parts = new List<string>();
        foreach (var c in costs)
        {
            int have = gs.InventoryOf(c.Mat);
            if (have < c.Amount) ok = false;
            parts.Add(Loc.F("{0}x {1} ({2}/{0})", c.Amount, GameData.Mat(c.Mat)?.Name ?? c.Mat, have));
        }
        parts.Add(GameState.FormatMoney(fee));
        return string.Join("  ·  ", parts);
    }

    PanelContainer Card(Color accent, out HBoxContainer row)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(UiTheme.Card, new Color(accent, 0.35f), 8, 1, 14, 8));
        row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 14);
        card.AddChild(row);
        _list.AddChild(card);
        return card;
    }

    void Build()
    {
        var gs = GameState.Instance;
        UiTheme.ClearChildren(_list);
        Button first = null;
        // receita escondida do Velho Bram (ato 2): a chave falsa da Guilda
        if (gs.Story == StoryData.ForgeKey)
        {
            Card(new Color(1f, 0.4f, 0.3f), out var kh);
            kh.AddChild(HudIcon.MakeOre(GameData.Mat(StoryData.KeyOre2), 48));
            var kv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            kh.AddChild(kv);
            kv.AddChild(UiTheme.Label("Chave falsa da Guilda (receita do Velho Bram)", 18, new Color(1f, 0.7f, 0.55f), true));
            int diaries = StoryData.AldricDiaries.Count(d => gs.Data.Diaries.Contains(d));
            kv.AddChild(UiTheme.Label(Loc.F("Cadernos do Aldric lidos: {0}/2  ·  {1}", diaries, gs.DayOver ? Loc.T("o ferreiro está dormindo") : Loc.T("só de noite, quando o ferreiro dorme")), 14, diaries == 2 && gs.DayOver ? UiTheme.Success : UiTheme.Muted));
            var kline = UiTheme.Label(CostLine(new[] { new ForgeCost(StoryData.KeyOre1, StoryData.KeyOre1Amount), new ForgeCost(StoryData.KeyOre2, StoryData.KeyOre2Amount) }, gs.KeyFee, out bool kok), 14, kok ? UiTheme.Success : UiTheme.Muted);
            kline.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            kv.AddChild(kline);
            var kb = UiTheme.Button("Forjar escondido", () => { if (GameState.Instance.TryForgeKey()) Audio.AudioManager.Instance?.Play("unlock", -4f); }, 170, 18);
            kb.Disabled = !gs.CanForgeKey;
            kb.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            kb.SetMeta("key", 1);
            kh.AddChild(kb);
            if (!kb.Disabled) first = kb;
        }
        // armas e armaduras (ato 3): bancada de combinação de minérios
        if (gs.WeaponsUnlocked) BuildCombiner(gs, ref first);

        foreach (var slot in GearData.Slots)
        {
            var cur = GearData.Of(slot, gs.GearTier(slot));
            var next = gs.NextGear(slot);
            Card(next == null ? UiTheme.Success : UiTheme.Gold, out var h);
            var iconMat = GameData.Mat((next ?? cur).Costs[0].Mat);
            h.AddChild(HudIcon.MakeOre(iconMat, 48));
            var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            h.AddChild(v);
            v.AddChild(UiTheme.Label(cur != null ? Loc.F("{0}: {1}", GearData.SlotName(slot), cur.Name) : Loc.F("{0}: nenhum", GearData.SlotName(slot)), 18, UiTheme.Text, true));
            v.AddChild(UiTheme.Label(cur != null ? GearData.BonusText(cur) : Loc.T("Ainda não forjado."), 14, cur != null ? UiTheme.Success : UiTheme.Muted));
            Button btn;
            if (next == null)
            {
                v.AddChild(UiTheme.Label("Nível máximo.", 14, UiTheme.Gold));
                btn = UiTheme.Button("Máximo", null, 150, 18);
                btn.Disabled = true;
            }
            else if (!gs.GearTierAvailable(next))
            {
                v.AddChild(UiTheme.Label(Loc.F("Próximo: {0} — libere {1} para achar os minérios.", next.Name, GameData.Regions[next.Tier].Name), 14, UiTheme.Muted));
                btn = UiTheme.Button("Bloqueado", null, 150, 18);
                btn.Disabled = true;
            }
            else
            {
                v.AddChild(UiTheme.Label(Loc.F("Próximo: {0} ({1})", next.Name, GearData.BonusText(next)), 14, UiTheme.Gold));
                var line = UiTheme.Label(CostLine(next.Costs, next.Fee, out bool ok), 14, ok ? UiTheme.Success : UiTheme.Muted);
                line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                v.AddChild(line);
                var s = slot;
                btn = UiTheme.Button("Forjar", () => { if (GameState.Instance.TryForge(s)) Audio.AudioManager.Instance?.Play("buy", -4f); }, 150, 18);
                btn.Disabled = !gs.CanForge(slot);
            }
            btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            btn.SetMeta("forge", (int)slot);
            h.AddChild(btn);
            if (first == null && !btn.Disabled) first = btn;
        }

        // conserto da ferramenta atual
        {
            var t = gs.CurrentTool;
            Card(UiTheme.Cyan, out var h);
            h.AddChild(HudIcon.Make(HudIcon.Kind.Pick, ToolData.HeadColor(t), 48));
            var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            h.AddChild(v);
            v.AddChild(UiTheme.Label(Loc.F("Consertar: {0}", t.Name), 18, UiTheme.Text, true));
            Button btn;
            if (t.Kind == ToolKind.Laser)
            {
                v.AddChild(UiTheme.Label("Ferramentas a laser não se desgastam: usam células de energia.", 14, UiTheme.Muted));
                btn = UiTheme.Button("Consertar", null, 150, 18);
                btn.Disabled = true;
            }
            else
            {
                v.AddChild(UiTheme.Label(gs.ToolBroken ? Loc.T("Quebrada: o ferreiro deixa como nova.") : Loc.F("Resistência: {0}/{1}", gs.ToolDurabilityLeft, gs.ToolMaxDurability), 14, gs.ToolBroken ? UiTheme.Danger : UiTheme.Muted));
                if (gs.RepairAvailable)
                {
                    var line = UiTheme.Label(CostLine(gs.RepairCosts, gs.RepairFee, out bool ok), 14, ok ? UiTheme.Success : UiTheme.Muted);
                    line.AutowrapMode = TextServer.AutowrapMode.WordSmart;
                    v.AddChild(line);
                }
                else v.AddChild(UiTheme.Label("Sua ferramenta está inteira.", 14, UiTheme.Success));
                btn = UiTheme.Button("Consertar", () => { if (GameState.Instance.TryRepair()) Audio.AudioManager.Instance?.Play("buy", -4f); }, 150, 18);
                btn.Disabled = !gs.CanRepair;
            }
            btn.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            btn.SetMeta("repair", 1);
            h.AddChild(btn);
            if (first == null && !btn.Disabled) first = btn;
        }
        if (first != null) first.CallDeferred(Control.MethodName.GrabFocus);
        else if (FindBack() is { } back) back.CallDeferred(Control.MethodName.GrabFocus);
    }

    Button FindBack()
    {
        foreach (var n in Body.GetChildren())
            if (n is HBoxContainer hb) foreach (var c in hb.GetChildren()) if (c is Button b) return b;
        return null;
    }

    // ------------------------------------------------------------------ bancada de combinação
    void BuildCombiner(GameState gs, ref Button first)
    {
        _list.AddChild(UiTheme.Label("COMBINAR MINÉRIOS: ARMAS E ARMADURAS", 18, new Color(1f, 0.6f, 0.45f), true));
        bool armor = _craftKind == 2;
        int min = armor ? WeaponData.ArmorMinOres : WeaponData.WeaponMinOres, max = armor ? WeaponData.ArmorMaxOres : WeaponData.WeaponMaxOres;
        int need = armor ? WeaponData.ArmorOreAmount : WeaponData.WeaponOreAmount;
        while (_picks.Count < min) _picks.Add(null);
        while (_picks.Count > max) _picks.RemoveAt(_picks.Count - 1);

        Card(new Color(1f, 0.55f, 0.4f), out var row);
        var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        v.AddThemeConstantOverride("separation", 6);
        row.AddChild(v);
        // o que forjar
        var kinds = new HBoxContainer();
        kinds.AddThemeConstantOverride("separation", 8);
        v.AddChild(kinds);
        string[] kindNames = { "Espada", "Martelo", "Couraça" };
        for (int k = 0; k < 3; k++)
        {
            int kk = k;
            var kb = UiTheme.Button(kindNames[k], () => { _craftKind = kk; Refresh(); }, 130, 16);
            if (k == _craftKind) kb.AddThemeColorOverride("font_color", UiTheme.Gold);
            kb.SetMeta("craft_kind", k);
            kinds.AddChild(kb);
        }
        v.AddChild(UiTheme.Label(armor
            ? Loc.F("Escolha de {0} a {1} minérios ({2} de cada). Quanto melhores, mais proteção e vida.", min, max, need)
            : Loc.F("Escolha de {0} a {1} minérios ({2} de cada). Quanto melhores, mais dano.", min, max, need), 14, UiTheme.Muted));
        // minérios disponíveis: os já descobertos, do mais forte para o mais fraco
        var ores = GameData.Materials.Values.Where(m => WeaponData.Forgeable(m) && gs.Data.Discovered.Contains(m.Id))
            .OrderByDescending(WeaponData.OrePower).ToList();
        for (int i = 0; i < _picks.Count; i++)
        {
            int slot = i;
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 8);
            v.AddChild(h);
            h.AddChild(UiTheme.Label(Loc.F("Minério {0}", i + 1), 15, UiTheme.Text));
            var opt = new OptionButton { CustomMinimumSize = new Vector2(420, 40), FocusMode = Control.FocusModeEnum.All };
            opt.AddItem(Loc.T("— escolher —"), 0);
            int sel = 0;
            for (int k = 0; k < ores.Count; k++)
            {
                var m = ores[k];
                int have = gs.InventoryOf(m.Id);
                opt.AddItem(Loc.F("{0}  ({1}/{2})", m.Name, have, need), k + 1);
                if (_picks[i] == m.Id) sel = k + 1;
            }
            opt.Selected = sel;
            opt.ItemSelected += idx => { _picks[slot] = idx <= 0 ? null : ores[(int)idx - 1].Id; Refresh(); };
            opt.SetMeta("craft_slot", i);
            h.AddChild(opt);
            if (_picks[i] != null && GameData.Mat(_picks[i]) is { } pm) h.AddChild(HudIcon.MakeOre(pm, 32));
        }
        var addRow = new HBoxContainer();
        addRow.AddThemeConstantOverride("separation", 8);
        v.AddChild(addRow);
        var plus = UiTheme.Button("+ minério", () => { if (_picks.Count < max) _picks.Add(null); Refresh(); }, 140, 15);
        plus.Disabled = _picks.Count >= max;
        var less = UiTheme.Button("− minério", () => { if (_picks.Count > min) _picks.RemoveAt(_picks.Count - 1); Refresh(); }, 140, 15);
        less.Disabled = _picks.Count <= min;
        addRow.AddChild(plus);
        addRow.AddChild(less);

        // prévia do resultado
        var chosen = _picks.Where(p => p != null).ToList();
        string preview;
        bool can;
        Action forge;
        if (armor)
        {
            var a = WeaponData.MakeArmor(chosen);
            var cur = gs.CurrentArmor;
            preview = a == null ? Loc.F("Escolha pelo menos {0} minérios diferentes.", min)
                : Loc.F("{0}: {1}", a.Name, a.Description) + (cur != null ? "\n" + Loc.F("Atual: {0} ({1})", cur.Name, cur.Description) : "");
            can = gs.CanForge(a);
            forge = () => { if (GameState.Instance.TryForgeArmor(a)) { Audio.AudioManager.Instance?.Play("buy", -4f); _picks.Clear(); } };
            if (a != null) v.AddChild(UiTheme.Label(CostLine(a.Costs, a.Fee, out _), 14, can ? UiTheme.Success : UiTheme.Muted));
        }
        else
        {
            var w = WeaponData.Make((WeaponKind)_craftKind, chosen);
            var cur = gs.CurrentWeapon;
            preview = w == null ? Loc.F("Escolha pelo menos {0} minérios diferentes.", min)
                : Loc.F("{0}: {1}", w.Name, w.Description) + (cur != null ? "\n" + Loc.F("Atual: {0} ({1})", cur.Name, cur.Description) : "");
            can = gs.CanForge(w);
            forge = () => { if (GameState.Instance.TryForgeWeapon(w)) { Audio.AudioManager.Instance?.Play("buy", -4f); _picks.Clear(); } };
            if (w != null) v.AddChild(UiTheme.Label(CostLine(w.Costs, w.Fee, out _), 14, can ? UiTheme.Success : UiTheme.Muted));
        }
        var pl = UiTheme.Label(preview, 16, new Color(1f, 0.8f, 0.55f), true);
        pl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(pl);
        var fb = UiTheme.Button("Forjar", () => forge(), 150, 18);
        fb.Disabled = !can;
        fb.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        fb.SetMeta("craft_forge", 1);
        row.AddChild(fb);
        if (first == null && can) first = fb;
    }

    /// <summary>Capturas de tela: escolhe o tipo e os minérios na bancada.</summary>
    public void SelectForTour(int kind, string[] ores)
    {
        _craftKind = kind;
        _picks.Clear();
        _picks.AddRange(ores);
        Refresh();
    }
}
