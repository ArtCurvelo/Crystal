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
            BackgroundColor = new Color(0.005f, 0.005f, 0.012f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 1.0f,
            TonemapWhite = 6f,
            FogEnabled = true,
            FogLightEnergy = 1f,
            // bloom forte: minérios, neons e feixe "estouram" de luz
            GlowEnabled = true,
            GlowIntensity = 0.8f,
            GlowStrength = 1.0f,
            GlowBloom = 0.03f,
            GlowHdrThreshold = 1.25f,
            GlowHdrScale = 1.4f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.25f,
            AdjustmentContrast = 1.08f,
        };
        float[] levels = { 0f, 1f, 1f, 0.8f, 0.45f, 0.15f, 0f };
        for (int i = 1; i <= 6; i++) env.Set($"glow_levels/{i}", levels[i]);
        Environment = env;
        SetRegion(GameData.Regions[0], true);
        Settings.Changed += ApplyQuality;
        ApplyQuality();
    }

    public override void _ExitTree() => Settings.Changed -= ApplyQuality;

    void ApplyQuality()
    {
        var q = Settings.Quality;
        bool adv = Settings.Advanced;
        var e = Environment;
        e.GlowEnabled = true;
        e.GlowIntensity = q == GraphicsQuality.Low ? 0.6f : 0.8f;
        // névoa volumétrica: feixes de luz da lanterna e brilho dos cristais no ar
        e.VolumetricFogEnabled = adv && q == GraphicsQuality.High;
        e.VolumetricFogDensity = 0.022f;
        e.VolumetricFogLength = 48f;
        e.VolumetricFogDetailSpread = 1.5f;
        e.VolumetricFogGIInject = 0f;
        e.VolumetricFogAnisotropy = 0.45f;
        e.SsaoEnabled = adv && q != GraphicsQuality.Low;
        e.SsaoRadius = 1.6f;
        e.SsaoIntensity = 2.2f;
        e.SsilEnabled = adv && q == GraphicsQuality.High;
        e.SsilIntensity = 1.2f;
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
        // cavernas escuras: quem ilumina é a lanterna do capacete, os minérios e os neons.
        // Na Baixa (sem luzes dinâmicas) a luz ambiente compensa.
        float amb = Settings.Quality == GraphicsQuality.Low ? 1.5f : (Settings.Advanced ? 0.45f : 0.6f);
        e.AmbientLightEnergy = _energy * amb;
        e.FogLightColor = _fog.Lerp(OverlayFog, _overlay);
        e.FogDensity = Mathf.Lerp(_density, OverlayFogDensity, _overlay);
        e.BackgroundColor = _fog.Darkened(0.7f);
        e.VolumetricFogAlbedo = _fog.Lightened(0.55f);
        e.VolumetricFogEmission = _fog * 0.35f;
    }
}
