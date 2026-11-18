using System;
using Godot;

namespace CrystalExcavator.Core;

/// <summary>
/// Acessibilidade:
/// - filtro de daltonismo (correção de cores na tela inteira: protanopia, deuteranopia, tritanopia);
/// - narração por voz (texto para fala do sistema) dos controles em foco e de avisos;
/// - sons-guia para jogar sem enxergar: "ping" posicional vindo do minério mais próximo,
///   mais rápido quanto mais perto, e um tom quando a mira está num minério ou numa estação.
/// </summary>
public static class Accessibility
{
    static CanvasLayer _filterLayer;
    static ColorRect _filterRect;
    static ShaderMaterial _filterMat;
    static bool _focusHooked;
    static string _lastSpoken = "";
    static ulong _lastSpokenAt;

    const string FilterShader = @"
shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_linear;
uniform int mode = 0;
uniform float strength = 1.0;
// simulação (Machado et al.) e correção por redistribuição do erro (daltonização)
void fragment() {
	vec3 c = texture(screen, SCREEN_UV).rgb;
	mat3 sim;
	if (mode == 1) sim = mat3(vec3(0.152286, 0.114503, -0.003882), vec3(1.052583, 0.786281, -0.048116), vec3(-0.204868, 0.099216, 1.051998));
	else if (mode == 2) sim = mat3(vec3(0.367322, 0.280085, -0.011820), vec3(0.860646, 0.672501, 0.042940), vec3(-0.227968, 0.047413, 0.968881));
	else sim = mat3(vec3(1.255528, -0.078411, 0.004733), vec3(-0.076749, 0.930809, 0.691367), vec3(-0.178779, 0.147602, 0.303900));
	vec3 s = sim * c;
	vec3 err = c - s;
	vec3 shift = vec3(0.0, err.r * 0.7 + err.g, err.r * 0.7 + err.b);
	if (mode == 3) shift = vec3(err.b * 0.7 + err.r, err.b * 0.7 + err.g, 0.0);
	COLOR = vec4(clamp(c + shift * strength, 0.0, 1.0), 1.0);
}";

    public static void Apply()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree?.Root == null) return;
        var d = Settings.Data;
        if (d.Colorblind > 0 && _filterLayer == null)
        {
            _filterLayer = new CanvasLayer { Layer = 120, Name = "ColorblindFilter" };
            _filterMat = new ShaderMaterial { Shader = new Shader { Code = FilterShader } };
            _filterRect = new ColorRect { Material = _filterMat, MouseFilter = Control.MouseFilterEnum.Ignore };
            _filterRect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _filterLayer.AddChild(_filterRect);
            tree.Root.CallDeferred(Node.MethodName.AddChild, _filterLayer);
        }
        if (_filterLayer != null)
        {
            _filterLayer.Visible = d.Colorblind > 0;
            _filterMat.SetShaderParameter("mode", d.Colorblind);
            _filterMat.SetShaderParameter("strength", d.ColorblindStrength);
        }
        if (!_focusHooked)
        {
            _focusHooked = true;
            tree.Root.GuiFocusChanged += OnFocus;
        }
    }

    static void OnFocus(Control c)
    {
        if (!Settings.Data.Narration || c == null) return;
        string text = c switch
        {
            Button b => b.Text,
            Slider s => $"{Loc.T("Controle deslizante")} {s.Value:0.##}",
            _ => c.TooltipText,
        };
        Speak(text, true);
    }

    /// <summary>Fala um texto (se a narração estiver ligada e houver voz no sistema).</summary>
    public static void Speak(string text, bool interrupt = false)
    {
        if (!Settings.Data.Narration || string.IsNullOrWhiteSpace(text)) return;
        // evita repetir a mesma fala em sequência
        ulong now = Time.GetTicksMsec();
        if (text == _lastSpoken && now - _lastSpokenAt < 1500) return;
        _lastSpoken = text; _lastSpokenAt = now;
        try
        {
            var voices = DisplayServer.TtsGetVoicesForLanguage(Loc.Lang);
            if (voices.Length == 0) voices = DisplayServer.TtsGetVoicesForLanguage("en");
            if (voices.Length == 0) return;
            DisplayServer.TtsSpeak(Clean(text), voices[0], 60, 1f, 1.1f, 0, interrupt);
        }
        catch (Exception) { /* sem suporte a voz neste sistema */ }
    }

    static string Clean(string t) => t.Replace("◈", "").Replace("[", "").Replace("]", "").Replace("·", ",").Replace("✓", "");
}
