using System.Collections.Generic;
using System.Linq;

namespace CrystalExcavator.Core.Rendering;

/// <summary>Decisão escolhida a partir dos testes.</summary>
public sealed record RenderDecision(string Method, int Quality, float RenderScale, bool Recovery, string Reason);

/// <summary>
/// Regras de escolha (sem dependência do motor: testável isoladamente).
///  1. Um renderizador é APROVADO quando inicia o que pediu, desenha a cena, não tem erros críticos
///     e segura a meta (média ≥ 55 FPS e 99% dos quadros em até 33 ms) na qualidade Média.
///  2. Entre os aprovados, prefere Forward+ → Mobile → Compatibility.
///  3. Sem aprovado na Média, vale o primeiro (na mesma ordem) aprovado na Baixa.
///  4. Nenhum aprovado: o que funcionou com mais FPS, em perfil de recuperação (Baixa + resolução dinâmica).
///  5. Nada funcionou: Compatibility na Baixa (o mais seguro).
/// </summary>
public static class RendererSelector
{
    /// <summary>Versão das regras/teste: subir quando os requisitos do jogo mudarem (força nova avaliação).</summary>
    public const int EvalVersion = 2;
    public const double TargetFps = 55, MaxP99Ms = 33.4, HighFps = 100;

    public static ProbeStatus Classify(ProbeResult r)
    {
        if (r == null || !r.Started || !r.Rendered || r.CriticalErrors > 0) return ProbeStatus.Incompatible;
        if (!string.IsNullOrEmpty(r.ActualMethod) && r.ActualMethod != r.Method) return ProbeStatus.Incompatible; // a Godot caiu para outro
        if (r.FpsMedium <= 0) return ProbeStatus.Incompatible;
        return Meets(r.FpsMedium, r.P99MsMedium) ? ProbeStatus.Approved : ProbeStatus.Limited;
    }

    static bool Meets(double fps, double p99) => fps >= TargetFps && p99 > 0 && p99 <= MaxP99Ms;

    /// <summary>Próximo renderizador a testar (null = já dá para decidir). Para assim que um preferido é aprovado.</summary>
    public static string NextToProbe(IReadOnlyList<ProbeResult> done, IEnumerable<string> skip = null)
    {
        var skipSet = new HashSet<string>(skip ?? Enumerable.Empty<string>());
        foreach (var m in RenderMethods.Preference)
        {
            if (skipSet.Contains(m)) continue;
            var r = done.FirstOrDefault(x => x.Method == m);
            if (r == null) return m;
            if (r.Status == ProbeStatus.Approved) return null;
        }
        return null;
    }

    public static RenderDecision Choose(IReadOnlyList<ProbeResult> results, IEnumerable<string> excluded = null)
    {
        var ex = new HashSet<string>(excluded ?? Enumerable.Empty<string>());
        var usable = RenderMethods.Preference
            .Select(m => results.FirstOrDefault(r => r.Method == m))
            .Where(r => r != null && !ex.Contains(r.Method) && r.Status != ProbeStatus.Incompatible)
            .ToList();
        foreach (var r in usable)
            if (r.Status == ProbeStatus.Approved)
                return new RenderDecision(r.Method, r.FpsMedium >= HighFps ? 2 : 1, 1f, false,
                    $"{r.Method} aprovado na Média ({r.FpsMedium:0} FPS, p99 {r.P99MsMedium:0.0} ms)");
        foreach (var r in usable)
            if (Meets(r.FpsLow, r.P99MsLow))
                return new RenderDecision(r.Method, 0, 1f, false, $"{r.Method} aprovado na Baixa ({r.FpsLow:0} FPS)");
        var best = usable.OrderByDescending(r => System.Math.Max(r.FpsLow, r.FpsMedium)).FirstOrDefault();
        if (best != null)
            return new RenderDecision(best.Method, 0, 0.75f, true, $"nenhum na meta; melhor: {best.Method} ({System.Math.Max(best.FpsLow, best.FpsMedium):0} FPS) em recuperação");
        return new RenderDecision(RenderMethods.Compatibility, 0, 0.75f, true, "nenhum renderizador validado; modo seguro");
    }

    /// <summary>A avaliação salva ainda vale para este computador e esta versão do motor?</summary>
    public static bool IsValid(RenderProfile p, HardwareFingerprint hw, string engine)
    {
        if (p == null || p.EvalVersion != EvalVersion || p.Engine != engine) return false;
        if (!RenderMethods.IsKnown(p.Method) || p.FailedMethods.Contains(p.Method)) return false;
        return p.Hardware != null && hw != null && p.Hardware.Key == hw.Key;
    }
}
