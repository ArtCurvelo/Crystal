using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Desempenho: um MultiMesh só é descartado inteiro, então um MultiMesh com todas as pedrinhas da região
/// era desenhado inteiro sempre que qualquer pedaço da região aparecia (milhões de triângulos fora da tela).
/// Aqui as instâncias são divididas em blocos de <see cref="Cell"/> m: cada bloco tem a própria caixa
/// (o motor descarta os que estão fora da visão) e uma distância máxima de desenho.
/// </summary>
public static class ChunkedMulti
{
    public const float Cell = 32f;
    /// <summary>Distância de desenho padrão da decoração (a névoa da caverna já esconde o que passa disso).</summary>
    public const float DefaultRange = 85f;
    public const string RangeMeta = "base_range";
    /// <summary>Distância de desenho por qualidade (Baixa desenha bem mais perto: menos triângulos na GPU fraca).</summary>
    public static float RangeScale(Core.GraphicsQuality q) => q switch { Core.GraphicsQuality.Low => 0.5f, Core.GraphicsQuality.Medium => 0.8f, _ => 1f };

    public static void Add(Node3D root, Mesh mesh, Material mat, List<Transform3D> xforms, float lowFraction, float range = DefaultRange)
    {
        if (xforms.Count == 0) return;
        var cells = new Dictionary<(int, int), List<Transform3D>>();
        foreach (var x in xforms)
        {
            var key = (Mathf.FloorToInt(x.Origin.X / Cell), Mathf.FloorToInt(x.Origin.Z / Cell));
            if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<Transform3D>();
            list.Add(x);
        }
        foreach (var (key, list) in cells)
        {
            // centro do bloco como origem: a distância de desenho é medida a partir daqui
            var center = Vector3.Zero;
            foreach (var x in list) center += x.Origin;
            center /= list.Count;
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = list.Count };
            for (int i = 0; i < list.Count; i++)
                mm.SetInstanceTransform(i, new Transform3D(list[i].Basis, list[i].Origin - center));
            var mmi = new MultiMeshInstance3D
            {
                Multimesh = mm, MaterialOverride = mat, Position = center,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                VisibilityRangeEnd = range, VisibilityRangeEndMargin = 8f,
                VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled,
            };
            mmi.SetMeta(WorldBuilder.LowFractionMeta, lowFraction);
            mmi.SetMeta(RangeMeta, range);
            root.AddChild(mmi);
        }
    }
}
