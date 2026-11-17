using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Ambiente global (WorldEnvironment). Faz transição suave de cor ambiente e névoa
/// entre regiões e aplica o nível de qualidade gráfica.
/// </summary>
public partial class EnvironmentController : WorldEnvironment
{
    RegionDef _target;
    Color _ambient, _fog;
    float _energy, _density;

    public override void _Ready()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.01f, 0.01f, 0.02f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            TonemapExposure = 1.05f,
            FogEnabled = true,
            FogLightEnergy = 1f,
            GlowIntensity = 0.7f,
            GlowStrength = 1.0f,
            GlowBloom = 0.08f,
            GlowHdrThreshold = 0.9f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
        };
        Environment = env;
        SetRegion(GameData.Regions[0], true);
        Settings.Changed += ApplyQuality;
        ApplyQuality();
    }

    public override void _ExitTree() => Settings.Changed -= ApplyQuality;

    void ApplyQuality()
    {
        var q = Settings.Quality;
        Environment.GlowEnabled = q != GraphicsQuality.Low;
        Environment.GlowIntensity = q == GraphicsQuality.High ? 0.85f : 0.65f;
    }

    /// <summary>Sobreposição temporária de evento (névoa/cor/energia). Intensidade 0 = desligado.</summary>
    public float OverlayAmount;
    public Color OverlayFog = Colors.White;
    public float OverlayFogDensity;
    public Color OverlayAmbient = Colors.White;
    float _overlay;

    public void SetRegion(RegionDef r, bool instant = false)
    {
        _target = r;
        if (instant)
        {
            _ambient = r.AmbientColor; _fog = r.FogColor; _energy = r.AmbientEnergy; _density = r.FogDensity;
            Push();
        }
    }

    public override void _Process(double delta)
    {
        if (_target == null) return;
        float k = Mathf.Min(1f, (float)delta * 1.5f);
        _ambient = _ambient.Lerp(_target.AmbientColor, k);
        _fog = _fog.Lerp(_target.FogColor, k);
        _energy = Mathf.Lerp(_energy, _target.AmbientEnergy, k);
        _density = Mathf.Lerp(_density, _target.FogDensity, k);
        _overlay = Mathf.MoveToward(_overlay, OverlayAmount, (float)delta * 0.6f);
        Push();
    }

    void Push()
    {
        var e = Environment;
        e.AmbientLightColor = _ambient.Lerp(OverlayAmbient, _overlay * 0.6f);
        // na Baixa não há luzes dinâmicas: a luz ambiente compensa
        e.AmbientLightEnergy = _energy * (Settings.Quality == GraphicsQuality.Low ? 1.9f : 1f);
        e.FogLightColor = _fog.Lerp(OverlayFog, _overlay);
        e.FogDensity = Mathf.Lerp(_density, OverlayFogDensity, _overlay);
        e.BackgroundColor = _fog.Darkened(0.5f);
    }
}
