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
            TonemapExposure = 1.15f,
            TonemapWhite = 6f,
            FogEnabled = true,
            FogLightEnergy = 1f,
            // bloom forte: minérios, neons e feixe "estouram" de luz
            GlowEnabled = true,
            GlowIntensity = 0.45f,
            GlowStrength = 1.0f,
            GlowBloom = 0.0f,
            GlowHdrThreshold = 1.6f,
            GlowHdrScale = 1.4f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.1f,
            AdjustmentContrast = 1.08f,
        };
        float[] levels = { 0f, 0.8f, 1f, 0.55f, 0.2f, 0f, 0f };
        for (int i = 1; i <= 6; i++) env.Set($"glow_levels/{i}", levels[i]);
        Environment = env;
        SetRegion(GameData.Regions[0], true);
        Settings.Changed += ApplyQuality;
        ApplyQuality();
    }

    public override void _ExitTree() => Settings.Changed -= ApplyQuality;

    bool _town, _townNight;
    Sky _sky;
    ProceduralSkyMaterial _skyMat;

    /// <summary>Superfície (vila/casa): céu aberto, noite estrelada depois do turno e manhã depois de dormir.</summary>
    public void SetTown(bool on, bool night)
    {
        _town = on; _townNight = night;
        var e = Environment;
        if (on)
        {
            _skyMat ??= new ProceduralSkyMaterial();
            _sky ??= new Sky { SkyMaterial = _skyMat };
            _skyMat.SkyTopColor = night ? new Color(0.02f, 0.03f, 0.09f) : new Color(0.22f, 0.45f, 0.85f);
            _skyMat.SkyHorizonColor = night ? new Color(0.12f, 0.1f, 0.2f) : new Color(0.72f, 0.82f, 0.92f);
            _skyMat.GroundBottomColor = new Color(0.03f, 0.03f, 0.04f);
            _skyMat.GroundHorizonColor = night ? new Color(0.1f, 0.09f, 0.14f) : new Color(0.55f, 0.45f, 0.4f);
            _skyMat.SunAngleMax = night ? 0f : 30f;
            e.BackgroundMode = Godot.Environment.BGMode.Sky;
            e.Sky = _sky;
        }
        else e.BackgroundMode = Godot.Environment.BGMode.Color;
        Push();
    }

    void ApplyQuality()
    {
        // perfil central: renderizador em uso × qualidade (cada um liga só o que suporta)
        var f = Core.Rendering.RenderFeatures.Current;
        var e = Environment;
        e.GlowEnabled = f.Glow;
        e.TonemapExposure = 1.15f * Settings.Data.Brightness;
        e.GlowIntensity = f.GlowIntensity;
        // névoa volumétrica: feixes de luz da lanterna e brilho dos cristais no ar
        e.VolumetricFogEnabled = f.VolumetricFog;
        e.VolumetricFogDensity = _town ? (_townNight ? 0.004f : 0.0012f) : 0.012f;
        e.VolumetricFogLength = 48f;
        e.VolumetricFogDetailSpread = 1.5f;
        e.VolumetricFogGIInject = 0f;
        e.VolumetricFogAnisotropy = 0.45f;
        e.SsaoEnabled = f.Ssao;
        e.SsaoRadius = 1.6f;
        e.SsaoIntensity = 2.2f;
        // reflexos em tempo real (água, poças e rocha molhada)
        e.SsrEnabled = f.Ssr;
        e.SsrMaxSteps = System.Math.Max(8, f.SsrSteps);
        e.SsrFadeIn = 0.15f;
        e.SsrFadeOut = 2.0f;
        e.SsrDepthTolerance = 0.3f;
        e.SsilEnabled = f.Ssil;
        e.SsilIntensity = 1.2f;
        ApplyEffectCosts();
    }

    /// <summary>
    /// Desempenho (meta 120 FPS na Alta): os efeitos de tela usavam a qualidade padrão da Godot (cara).
    /// Aqui cada um usa a versão barata que quase não muda a imagem: SSAO/SSIL em meia resolução,
    /// SSR com rugosidade simplificada, névoa volumétrica em grade menor sem filtro extra e sombra da
    /// lanterna com filtro suave mais barato.
    /// </summary>
    public static void ApplyEffectCosts()
    {
        RenderingServer.EnvironmentSetSsaoQuality(RenderingServer.EnvironmentSsaoQuality.Medium, true, 0.5f, 2, 50f, 300f);
        RenderingServer.EnvironmentSetSsilQuality(RenderingServer.EnvironmentSsilQuality.Low, true, 0.5f, 2, 50f, 300f);
        RenderingServer.EnvironmentSetSsrRoughnessQuality(RenderingServer.EnvironmentSsrRoughnessQuality.Low);
        RenderingServer.EnvironmentSetVolumetricFogVolumeSize(48, 48);
        RenderingServer.EnvironmentSetVolumetricFogFilterActive(false);
        RenderingServer.PositionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
        RenderingServer.ScreenSpaceRoughnessLimiterSetActive(false, 0.25f, 0.18f);
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
        // tom neutro: cores das regiões aparecem sem deixar a cena roxa/azul demais
        e.AmbientLightColor = _ambient.Lerp(OverlayAmbient, _overlay * 0.6f).Lerp(new Color(0.85f, 0.85f, 0.9f), 0.45f);
        // cavernas escuras: quem ilumina é a lanterna do capacete, os minérios e os neons.
        // Na Baixa (sem luzes dinâmicas) a luz ambiente compensa.
        float amb = Core.Rendering.RenderFeatures.Current.AmbientBoost;
        e.AmbientLightEnergy = _energy * amb;
        e.FogLightColor = _fog.Lerp(OverlayFog, _overlay);
        e.FogDensity = Mathf.Lerp(_density, OverlayFogDensity, _overlay);
        e.BackgroundColor = _fog.Darkened(0.7f);
        if (_town)
        {
            // ar livre: luz ambiente do céu, névoa leve de vale
            e.AmbientLightColor = _townNight ? new Color(0.35f, 0.4f, 0.6f) : new Color(0.75f, 0.72f, 0.7f);
            e.AmbientLightEnergy = (_townNight ? 0.55f : 0.9f) * amb;
            e.FogLightColor = _townNight ? new Color(0.12f, 0.12f, 0.2f) : new Color(0.68f, 0.76f, 0.86f);
            e.FogDensity = _townNight ? 0.004f : 0.0025f;
        }
        // ao ar livre a névoa volumétrica da caverna virava uma neblina bege: bem mais rala na vila
        e.VolumetricFogDensity = _town ? (_townNight ? 0.004f : 0.0012f) : 0.012f;
        e.VolumetricFogAlbedo = _town ? new Color(0.8f, 0.85f, 0.9f) : _fog.Lightened(0.55f);
        e.VolumetricFogEmission = _fog * 0.35f;
    }
}
