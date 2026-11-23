using Godot;

namespace CrystalExcavator.Core.Rendering;

/// <summary>Os três renderizadores da Godot usados pelo jogo (nomes oficiais do motor).</summary>
public static class RenderMethods
{
    public const string ForwardPlus = "forward_plus";
    public const string Mobile = "mobile";
    public const string Compatibility = "gl_compatibility";

    /// <summary>Ordem de preferência entre os renderizadores APROVADOS.</summary>
    public static readonly string[] Preference = { ForwardPlus, Mobile, Compatibility };

    /// <summary>Renderizador em uso neste processo ("" em modo headless).</summary>
    public static string Current
    {
        get
        {
            try { return RenderingServer.GetCurrentRenderingMethod() ?? ""; }
            catch { return ""; }
        }
    }

    public static bool IsKnown(string m) => m == ForwardPlus || m == Mobile || m == Compatibility;
}

/// <summary>
/// O que cada perfil gráfico liga, por renderizador × qualidade (9 combinações).
/// Recursos que um renderizador não tem são trocados por alternativas que ele suporta
/// (ex.: sem SSAO/SSR no Mobile e no Compatibility → névoa e brilho calibrados, MSAA no lugar do SMAA).
/// </summary>
public sealed record RenderFeatures(
    string Method, GraphicsQuality Quality,
    bool Ssao, bool Ssr, int SsrSteps, bool Ssil, bool VolumetricFog,
    bool LampShadows, bool Glow, float GlowIntensity,
    bool Smaa, bool Fxaa, Viewport.Msaa Msaa, bool Fsr1, bool Fsr2, bool Debanding,
    float AmbientBoost)
{
    public static RenderFeatures For(string method, GraphicsQuality q)
    {
        bool low = q == GraphicsQuality.Low, high = q == GraphicsQuality.High;
        float glow = low ? 0.35f : 0.45f;
        switch (method)
        {
            case RenderMethods.ForwardPlus:
                return new RenderFeatures(method, q,
                    Ssao: !low, Ssr: !low, SsrSteps: high ? 56 : 40, Ssil: high, VolumetricFog: high,
                    LampShadows: high, Glow: true, GlowIntensity: glow,
                    Smaa: true, Fxaa: false, Msaa: Viewport.Msaa.Disabled, Fsr1: true, Fsr2: true, Debanding: true,
                    AmbientBoost: low ? 2.3f : 1.0f);
            case RenderMethods.Mobile:
                // sem SSAO/SSR/SSIL/névoa volumétrica: compensa com um pouco mais de luz ambiente;
                // sombras da lanterna só na Alta (custo alto em GPU integrada)
                return new RenderFeatures(method, q,
                    Ssao: false, Ssr: false, SsrSteps: 0, Ssil: false, VolumetricFog: false,
                    LampShadows: high, Glow: true, GlowIntensity: glow,
                    Smaa: true, Fxaa: false, Msaa: Viewport.Msaa.Disabled, Fsr1: true, Fsr2: false, Debanding: true,
                    AmbientBoost: low ? 2.3f : 1.1f);
            default: // Compatibility (OpenGL)
                return new RenderFeatures(RenderMethods.Compatibility, q,
                    Ssao: false, Ssr: false, SsrSteps: 0, Ssil: false, VolumetricFog: false,
                    LampShadows: high, Glow: true, GlowIntensity: glow,
                    Smaa: false, Fxaa: false, Msaa: low ? Viewport.Msaa.Disabled : Viewport.Msaa.Msaa2X, Fsr1: false, Fsr2: false, Debanding: false,
                    AmbientBoost: low ? 2.3f : 1.1f);
        }
    }

    /// <summary>Perfil do renderizador em uso, com a qualidade escolhida.</summary>
    public static RenderFeatures Current => For(RenderMethods.Current, Settings.Quality);
}
