using System;
using System.Collections.Generic;
using System.Linq;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>
/// Árvore de habilidades desenhada: um ramo por coluna, cada um com sua cor e ícone; nós em forma de gema
/// (aprendido = dourado, disponível = cor do ramo pulsando, bloqueado = apagado com cadeado) e as linhas
/// entre os níveis acendem no caminho já aprendido. Os nós são botões (mouse e controle).
/// </summary>
public partial class SkillTreeView : Control
{
    public Action<string> OnSelect;
    public string Selected;

    public static readonly Color[] BranchColors =
    {
        new(0.35f, 0.85f, 1f),   // coleta
        new(1f, 0.78f, 0.3f),    // turno
        new(1f, 0.42f, 0.35f),   // mineração
        new(0.45f, 1f, 0.55f),   // comércio
        new(0.3f, 1f, 0.8f),     // construção
        new(1f, 0.6f, 0.2f),     // ferramentas
        new(0.75f, 0.55f, 1f),   // exploração
    };
    static readonly HudIcon.Kind[] BranchIcons =
    {
        HudIcon.Kind.Bag, HudIcon.Kind.Clock, HudIcon.Kind.Pick, HudIcon.Kind.Coin, HudIcon.Kind.Bolt, HudIcon.Kind.Shard, HudIcon.Kind.Region,
    };

    const float ColW = 156, NodeW = 140, NodeH = 58, TierGap = 84, Top = 54;
    readonly Dictionary<string, Button> _nodes = new();
    readonly Dictionary<string, Vector2> _centers = new();
    float _t;

    public override void _Ready()
    {
        int branches = HomeData.BranchNames.Length;
        int tiers = HomeData.Skills.Max(s => s.Tier);
        CustomMinimumSize = new Vector2(branches * ColW, Top + tiers * TierGap + 4);
        for (int b = 0; b < branches; b++)
        {
            float cx = b * ColW + ColW / 2;
            var head = new HBoxContainer { Position = new Vector2(b * ColW + 4, 4), Size = new Vector2(ColW - 8, 40), Alignment = BoxContainer.AlignmentMode.Center };
            head.AddThemeConstantOverride("separation", 6);
            AddChild(head);
            var icon = HudIcon.Make(BranchIcons[b % BranchIcons.Length], BranchColors[b % BranchColors.Length], 24);
            icon.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            head.AddChild(icon);
            head.AddChild(UiTheme.Label(HomeData.BranchNames[b], 14, BranchColors[b % BranchColors.Length], true));
            foreach (var def in HomeData.Skills.Where(s => s.Branch == b))
            {
                var c = new Vector2(cx, Top + (def.Tier - 1) * TierGap + NodeH / 2);
                _centers[def.Id] = c;
                var id = def.Id;
                var btn = new Button
                {
                    Text = "", FocusMode = FocusModeEnum.All, ClipText = true,
                    Position = c - new Vector2(NodeW / 2, NodeH / 2), Size = new Vector2(NodeW, NodeH), CustomMinimumSize = new Vector2(NodeW, NodeH),
                };
                btn.Pressed += () => OnSelect?.Invoke(id);
                btn.FocusEntered += () => OnSelect?.Invoke(id);
                AddChild(btn);
                var v = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, MouseFilter = MouseFilterEnum.Ignore };
                v.SetAnchorsPreset(LayoutPreset.FullRect);
                v.AddThemeConstantOverride("separation", 0);
                btn.AddChild(v);
                var name = UiTheme.Label(def.Name, 13, UiTheme.Text, true, HorizontalAlignment.Center);
                name.MouseFilter = MouseFilterEnum.Ignore;
                name.Name = "Name";
                v.AddChild(name);
                var sub = UiTheme.Label("", 12, UiTheme.Muted, false, HorizontalAlignment.Center);
                sub.MouseFilter = MouseFilterEnum.Ignore;
                sub.Name = "Sub";
                v.AddChild(sub);
                _nodes[id] = btn;
            }
        }
        Refresh();
    }

    /// <summary>Atualiza estados (aprendida/disponível/bloqueada), cores e preços.</summary>
    public void Refresh()
    {
        var gs = GameState.Instance;
        if (gs == null) return;
        foreach (var def in HomeData.Skills)
        {
            if (!_nodes.TryGetValue(def.Id, out var btn)) continue;
            var col = BranchColors[def.Branch % BranchColors.Length];
            bool owned = gs.HasSkill(def.Id), avail = gs.SkillAvailable(def.Id), afford = gs.CanAfford(def.Cost);
            var fill = owned ? new Color(0.22f, 0.17f, 0.05f, 0.98f) : avail ? new Color(col.R * 0.16f, col.G * 0.16f, col.B * 0.16f, 0.98f) : new Color(0.05f, 0.05f, 0.07f, 0.95f);
            var edge = owned ? UiTheme.Gold : avail ? new Color(col, afford ? 0.95f : 0.55f) : new Color(1, 1, 1, 0.12f);
            bool sel = def.Id == Selected;
            btn.AddThemeStyleboxOverride("normal", Gem(fill, sel ? Colors.White : edge, sel ? 3 : 2));
            btn.AddThemeStyleboxOverride("hover", Gem(fill.Lightened(0.12f), Colors.White, 2));
            btn.AddThemeStyleboxOverride("pressed", Gem(fill.Lightened(0.2f), Colors.White, 3));
            btn.AddThemeStyleboxOverride("focus", Gem(new Color(0, 0, 0, 0), Colors.White, 3));
            var v = btn.GetChild(0);
            var name = v.GetNode<Label>("Name");
            var sub = v.GetNode<Label>("Sub");
            name.AddThemeColorOverride("font_color", owned ? UiTheme.Gold : avail ? UiTheme.Text : UiTheme.Muted);
            sub.Text = owned ? "✓" : GameState.FormatMoney(def.Cost); // bloqueado: cor apagada (a fonte não tem o cadeado)
            sub.AddThemeColorOverride("font_color", owned ? UiTheme.Gold : avail && afford ? UiTheme.Success : UiTheme.Muted);
        }
        QueueRedraw();
    }

    /// <summary>Gema: retângulo com cantos chanfrados largos.</summary>
    static StyleBoxFlat Gem(Color fill, Color edge, int border)
    {
        var sb = new StyleBoxFlat { BgColor = fill, BorderColor = edge, AntiAliasing = true };
        sb.SetBorderWidthAll(border);
        sb.SetCornerRadiusAll(0);
        sb.CornerDetail = 1;
        sb.SetCornerRadiusAll(14);
        sb.ContentMarginLeft = sb.ContentMarginRight = 6;
        return sb;
    }

    public Button NodeOf(string id) => _nodes.TryGetValue(id, out var b) ? b : null;

    public override void _Process(double delta)
    {
        _t += (float)delta;
        // as conexões disponíveis pulsam devagar
        if (IsVisibleInTree()) QueueRedraw();
    }

    public override void _Draw()
    {
        var gs = GameState.Instance;
        if (gs == null) return;
        float pulse = 0.55f + 0.35f * Mathf.Sin(_t * 3f);
        foreach (var def in HomeData.Skills)
        {
            if (def.Requires == null || !_centers.TryGetValue(def.Id, out var to) || !_centers.TryGetValue(def.Requires, out var from)) continue;
            var col = BranchColors[def.Branch % BranchColors.Length];
            bool done = gs.HasSkill(def.Id), open = gs.SkillAvailable(def.Id);
            var a = from + new Vector2(0, NodeH / 2 - 2);
            var b = to - new Vector2(0, NodeH / 2 - 2);
            Color c = done ? UiTheme.Gold : open ? new Color(col, pulse) : new Color(1, 1, 1, 0.1f);
            if (done) DrawLine(a, b, new Color(UiTheme.Gold, 0.25f), 9f, true); // brilho do caminho aprendido
            DrawLine(a, b, c, done ? 3.5f : 2.5f, true);
            // losango no meio da ligação
            var m = (a + b) / 2;
            DrawColoredPolygon(new[] { m + new Vector2(0, -5), m + new Vector2(5, 0), m + new Vector2(0, 5), m + new Vector2(-5, 0) }, c);
        }
        // faixa sutil atrás de cada coluna
        for (int b = 0; b < HomeData.BranchNames.Length; b++)
            DrawRect(new Rect2(b * ColW + 6, 44, ColW - 12, Size.Y - 46), new Color(BranchColors[b % BranchColors.Length], 0.035f));
    }
}
