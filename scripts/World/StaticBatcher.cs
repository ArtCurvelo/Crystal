using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Agrupa, na carga, as malhas estáticas de uma região que compartilham o mesmo material
/// em uma única malha (menos chamadas de desenho em GPUs integradas).
/// Só considera MeshInstance3D "puros" cujos pais até a raiz da região são Node3D simples
/// (ignora cristais, estações, portões e qualquer nó com script/animação).
/// </summary>
public static class StaticBatcher
{
    public static int Batch(Node3D root)
    {
        var groups = new Dictionary<(Material, bool, bool), List<MeshInstance3D>>();
        Collect(root, root, groups);
        int removed = 0;
        var inv = root.GlobalTransform.AffineInverse();
        foreach (var kv in groups)
        {
            var list = kv.Value;
            if (list.Count < 2) continue;
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);
            foreach (var mi in list)
                st.AppendFrom(mi.Mesh, 0, inv * mi.GlobalTransform);
            var merged = st.Commit();
            root.AddChild(new MeshInstance3D
            {
                Name = "Batched",
                Mesh = merged,
                MaterialOverride = kv.Key.Item1,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            });
            foreach (var mi in list)
            {
                mi.GetParent().RemoveChild(mi);
                mi.QueueFree();
                removed++;
            }
            removed--; // uma malha nova no lugar
        }
        return removed;
    }

    static void Collect(Node root, Node node, Dictionary<(Material, bool, bool), List<MeshInstance3D>> groups)
    {
        foreach (Node child in node.GetChildren())
        {
            // só desce por Node3D "puros" (sem script e sem tipo especial)
            if (child.GetType() == typeof(MeshInstance3D))
            {
                var mi = (MeshInstance3D)child;
                if (!mi.Visible || mi.MaterialOverride == null || mi.Mesh == null || mi.Mesh.GetSurfaceCount() != 1) continue;
                var arrays = mi.Mesh.SurfaceGetArrays(0);
                bool hasColor = arrays[(int)Mesh.ArrayType.Color].VariantType != Variant.Type.Nil;
                bool hasUv = arrays[(int)Mesh.ArrayType.TexUV].VariantType != Variant.Type.Nil;
                bool indexed = arrays[(int)Mesh.ArrayType.Index].VariantType != Variant.Type.Nil;
                var key = (mi.MaterialOverride, hasColor, hasUv && !indexed ? true : hasUv);
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<MeshInstance3D>();
                list.Add(mi);
            }
            else if (child.GetType() == typeof(Node3D))
            {
                Collect(root, child, groups);
            }
        }
    }
}
