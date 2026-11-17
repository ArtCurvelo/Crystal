using CrystalExcavator.Audio;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>Identidade visual da interface: paleta, estilos e fábricas de controles.</summary>
public static class UiTheme
{
    public static readonly Color Bg = new(0.05f, 0.035f, 0.09f, 0.94f);
    public static readonly Color BgSoft = new(0.09f, 0.07f, 0.15f, 0.9f);
    public static readonly Color Card = new(0.12f, 0.1f, 0.2f, 0.95f);
    public static readonly Color CardHover = new(0.17f, 0.14f, 0.28f, 0.98f);
    public static readonly Color Border = new(0.37f, 0.9f, 1f, 0.55f);
    public static readonly Color Cyan = new(0.37f, 0.91f, 1f);
    public static readonly Color Violet = new(0.62f, 0.45f, 1f);
    public static readonly Color Gold = new(1f, 0.79f, 0.34f);
    public static readonly Color Text = new(0.91f, 0.95f, 1f);
    public static readonly Color Muted = new(0.6f, 0.64f, 0.78f);
    public static readonly Color Success = new(0.43f, 1f, 0.62f);
    public static readonly Color Danger = new(1f, 0.42f, 0.45f);

    static Theme _theme;
    static FontVariation _bold;

    public static FontVariation Bold
    {
        get
        {
            if (_bold == null)
            {
                _bold = new FontVariation { VariationEmbolden = 0.9f, BaseFont = ThemeDB.FallbackFont };
            }
            return _bold;
        }
    }

    public static StyleBoxFlat Box(Color bg, Color border, int radius = 10, int borderW = 2, int pad = 12)
    {
        var s = new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            ShadowColor = new Color(0, 0, 0, 0.35f),
            ShadowSize = 6,
            AntiAliasing = true,
        };
        s.SetCornerRadiusAll(radius);
        s.SetBorderWidthAll(borderW);
        s.ContentMarginLeft = s.ContentMarginRight = pad;
        s.ContentMarginTop = s.ContentMarginBottom = pad * 0.66f;
        return s;
    }

    public static Theme Get()
    {
        if (_theme != null) return _theme;
        var t = new Theme { DefaultFontSize = 18 };
        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_outline_color", "Label", new Color(0, 0, 0, 0.6f));
        t.SetConstant("outline_size", "Label", 0);

        // botões
        var normal = Box(new Color(0.14f, 0.11f, 0.24f, 0.95f), new Color(Cyan, 0.45f), 8, 2, 16);
        var hover = Box(new Color(0.2f, 0.16f, 0.34f, 1f), Cyan, 8, 2, 16);
        var pressed = Box(new Color(0.1f, 0.3f, 0.38f, 1f), Cyan, 8, 2, 16);
        var disabled = Box(new Color(0.1f, 0.09f, 0.14f, 0.8f), new Color(0.4f, 0.4f, 0.5f, 0.3f), 8, 2, 16);
        var focus = Box(new Color(0, 0, 0, 0), Gold, 8, 2, 16);
        focus.ShadowSize = 0;
        t.SetStylebox("normal", "Button", normal);
        t.SetStylebox("hover", "Button", hover);
        t.SetStylebox("pressed", "Button", pressed);
        t.SetStylebox("disabled", "Button", disabled);
        t.SetStylebox("focus", "Button", focus);
        t.SetColor("font_color", "Button", Text);
        t.SetColor("font_hover_color", "Button", Color.Color8(255, 255, 255));
        t.SetColor("font_pressed_color", "Button", Cyan);
        t.SetColor("font_disabled_color", "Button", new Color(0.5f, 0.5f, 0.6f));
        t.SetFontSize("font_size", "Button", 20);

        // painéis
        t.SetStylebox("panel", "PanelContainer", Box(Bg, Border, 14, 2, 18));
        t.SetStylebox("panel", "Panel", Box(Bg, Border, 14, 2, 18));

        // barras de progresso
        var barBg = Box(new Color(0.05f, 0.04f, 0.08f, 0.9f), new Color(1, 1, 1, 0.15f), 6, 1, 0);
        var barFill = Box(Cyan, new Color(0, 0, 0, 0), 6, 0, 0);
        barFill.ShadowSize = 0; barBg.ShadowSize = 0;
        t.SetStylebox("background", "ProgressBar", barBg);
        t.SetStylebox("fill", "ProgressBar", barFill);
        t.SetColor("font_color", "ProgressBar", Text);

        // sliders
        var sliderBg = Box(new Color(0.08f, 0.06f, 0.14f), new Color(1, 1, 1, 0.15f), 4, 1, 0);
        sliderBg.ContentMarginTop = sliderBg.ContentMarginBottom = 3;
        sliderBg.ShadowSize = 0;
        var sliderFill = Box(new Color(Cyan, 0.8f), new Color(0, 0, 0, 0), 4, 0, 0);
        sliderFill.ContentMarginTop = sliderFill.ContentMarginBottom = 3;
        sliderFill.ShadowSize = 0;
        t.SetStylebox("slider", "HSlider", sliderBg);
        t.SetStylebox("grabber_area", "HSlider", sliderFill);
        t.SetStylebox("grabber_area_highlight", "HSlider", sliderFill);

        // check / option
        t.SetColor("font_color", "CheckBox", Text);
        t.SetColor("font_color", "CheckButton", Text);
        t.SetStylebox("normal", "OptionButton", normal);
        t.SetStylebox("hover", "OptionButton", hover);
        t.SetStylebox("pressed", "OptionButton", pressed);
        t.SetStylebox("focus", "OptionButton", focus);
        t.SetStylebox("panel", "PopupMenu", Box(Bg, Border, 8, 2, 8));
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", Cyan);
        t.SetStylebox("hover", "PopupMenu", Box(CardHover, new Color(0, 0, 0, 0), 6, 0, 6));

        // scroll
        var scrollBg = Box(new Color(0, 0, 0, 0.2f), new Color(0, 0, 0, 0), 4, 0, 0);
        var grab = Box(new Color(Cyan, 0.5f), new Color(0, 0, 0, 0), 4, 0, 0);
        scrollBg.ShadowSize = grab.ShadowSize = 0;
        t.SetStylebox("scroll", "VScrollBar", scrollBg);
        t.SetStylebox("grabber", "VScrollBar", grab);
        t.SetStylebox("grabber_highlight", "VScrollBar", grab);
        t.SetStylebox("grabber_pressed", "VScrollBar", grab);
        _theme = t;
        return t;
    }

    public static Label Label(string text, int size = 18, Color? color = null, bool bold = false, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align };
        l.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue) l.AddThemeColorOverride("font_color", color.Value);
        if (bold) l.AddThemeFontOverride("font", Bold);
        return l;
    }

    public static Label Shadowed(Label l, int outline = 6)
    {
        l.AddThemeConstantOverride("outline_size", outline);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.75f));
        return l;
    }

    public static Button Button(string text, System.Action onPressed, int minWidth = 0, int fontSize = 20)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.All };
        if (minWidth > 0) b.CustomMinimumSize = new Vector2(minWidth, 48);
        b.AddThemeFontSizeOverride("font_size", fontSize);
        b.Pressed += () => { AudioManager.Instance?.Play("click", -6f); onPressed?.Invoke(); };
        b.MouseEntered += () => { if (!b.Disabled) AudioManager.Instance?.Play("hover", -12f); };
        return b;
    }

    /// <summary>Animação de entrada padrão (fade + leve escala).</summary>
    public static void PopIn(Control c)
    {
        c.PivotOffset = c.Size / 2;
        c.Modulate = new Color(1, 1, 1, 0);
        c.Scale = new Vector2(0.96f, 0.96f);
        var tw = c.CreateTween().SetParallel();
        tw.TweenProperty(c, "modulate:a", 1f, 0.18f);
        tw.TweenProperty(c, "scale", Vector2.One, 0.22f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    public static Control Spacer(float h) => new Control { CustomMinimumSize = new Vector2(0, h) };

    /// <summary>Ancora um controle a um preset e define deslocamentos explícitos (independe do tamanho atual do pai).</summary>
    public static void Place(Control c, Control.LayoutPreset preset, Vector2 offset, Vector2? size = null)
    {
        c.SetAnchorsPreset(preset);
        var s = size ?? Vector2.Zero;
        c.OffsetLeft = offset.X;
        c.OffsetTop = offset.Y;
        c.OffsetRight = offset.X + s.X;
        c.OffsetBottom = offset.Y + s.Y;
    }
}
