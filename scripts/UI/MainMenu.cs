using System;
using CrystalExcavator.Core;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>Menu principal sobre o cenário 3D (câmera orbitando a caverna inicial).</summary>
public partial class MainMenu : CanvasLayer
{
    public Action OnContinue, OnNewGame, OnSettings, OnHelp, OnBackups, OnQuit;
    Button _backups;
    Label _version;
    string _versionBase;

    /// <summary>Mostra o estado da verificação de atualização ao lado da versão.</summary>
    public void SetUpdateStatus(string text) => _version.Text = string.IsNullOrEmpty(text) ? _versionBase : $"{_versionBase}   ·   {text}";
    Button _continue;
    VBoxContainer _buttons;
    Control _root;
    Label _title;
    float _t;

    public override void _Ready()
    {
        Layer = 8;
        ProcessMode = ProcessModeEnum.Always;
        _root = new Control { Theme = UiTheme.Get() };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);

        // gradiente lateral para legibilidade
        var grad = new Gradient();
        grad.SetColor(0, new Color(0.02f, 0.01f, 0.05f, 0.95f));
        grad.SetColor(1, new Color(0.02f, 0.01f, 0.05f, 0f));
        var tex = new GradientTexture2D { Gradient = grad, Width = 256, Height = 4, FillFrom = new Vector2(0.15f, 0), FillTo = new Vector2(1f, 0) };
        var shade = new TextureRect { Texture = tex, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(shade);

        var left = new VBoxContainer();
        UiTheme.Place(left, Control.LayoutPreset.CenterLeft, new Vector2(90, -305));
        left.AddThemeConstantOverride("separation", 6);
        _root.AddChild(left);

        _title = UiTheme.Shadowed(UiTheme.Label("CRYSTAL", 76, UiTheme.Cyan, true), 14);
        left.AddChild(_title);
        var t2 = UiTheme.Shadowed(UiTheme.Label("EXCAVATOR", 52, UiTheme.Violet, true), 12);
        left.AddChild(t2);
        left.AddChild(UiTheme.Shadowed(UiTheme.Label("Minere. Venda. Evolua. Desça mais fundo.", 20, UiTheme.Muted), 6));
        left.AddChild(UiTheme.Spacer(14));

        _buttons = new VBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 8);
        left.AddChild(_buttons);
        _continue = Make("Continuar", () => OnContinue?.Invoke());
        Make("Novo jogo", () => OnNewGame?.Invoke());
        Make("Como jogar", () => OnHelp?.Invoke());
        _backups = Make("Restaurar progresso", () => OnBackups?.Invoke());
        Make("Configurações", () => OnSettings?.Invoke());
        Make("Sair", () => OnQuit?.Invoke());

        _version = UiTheme.Shadowed(UiTheme.Label(Loc.F("v{0}  ·  Feito com Godot e C#", ProjectSettings.GetSetting("application/config/version")), 14, UiTheme.Muted));
        UiTheme.Place(_version, Control.LayoutPreset.BottomLeft, new Vector2(24, -40));
        _root.AddChild(_version);
        _versionBase = _version.Text;
        var credit = UiTheme.Shadowed(UiTheme.Label("Todos os gráficos e sons são gerados proceduralmente.", 14, UiTheme.Muted, false, HorizontalAlignment.Right));
        UiTheme.Place(credit, Control.LayoutPreset.BottomRight, new Vector2(-520, -40), new Vector2(496, 0));
        _root.AddChild(credit);
    }

    Button Make(string text, Action a)
    {
        var b = UiTheme.Button(text, a, 320, 24);
        b.CustomMinimumSize = new Vector2(340, 50);
        b.Alignment = HorizontalAlignment.Left;
        _buttons.AddChild(b);
        return b;
    }

    public void Show(bool hasSave)
    {
        Visible = true;
        _continue.Visible = hasSave;
        _backups.Visible = SaveSystem.ListSnapshots().Count > 0;
        _continue.Text = Loc.T("Continuar");
        if (hasSave)
        {
            var d = SaveSystem.Load(out _);
            if (d != null)
            {
                var t = TimeSpan.FromSeconds(d.PlayTimeSeconds);
                _continue.Text = Loc.F("Continuar   ·   {0}   ·   {1}h{2:00}", GameState.FormatMoney(d.Money), (int)t.TotalHours, t.Minutes);
            }
        }
        _root.Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(_root, "modulate:a", 1f, 0.6f);
        (hasSave ? _continue : (Button)_buttons.GetChild(1)).GrabFocus();
    }

    public new void Hide() => Visible = false;

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _t += (float)delta;
        float g = 0.5f + 0.5f * Mathf.Sin(_t * 1.4f);
        _title.AddThemeColorOverride("font_color", UiTheme.Cyan.Lerp(new Color(0.75f, 0.97f, 1f), g * 0.5f));
    }
}
