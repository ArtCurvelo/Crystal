using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Corda presa numa borda: liga um ponto alto a um ponto baixo. Interagir sobe ou desce
/// (o jogador escala com animação). Pode ser fixa (poços) ou comprada pelo jogador.
/// </summary>
public partial class Rope : Node3D, IInteractable
{
    public Vector3 Top, Bottom, TopExit;
    public int BuildingId;       // 0 = corda fixa do mapa
    public bool Fixed => BuildingId == 0;
    /// <summary>Posição do jogador (para saber se ele está em cima ou embaixo).</summary>
    public static System.Func<Vector3> PlayerPos;

    Node3D _strand;
    float _t;
    public float Length => Top.DistanceTo(Bottom);

    public override void _Ready()
    {
        // área de interação ao longo da corda (não bloqueia o jogador)
        var area = new Area3D { CollisionLayer = PlayerController.InteractLayer, CollisionMask = 0, Monitoring = false };
        AddChild(area);
        var mid = (Top + Bottom) * 0.5f;
        var dir = (Top - Bottom).Normalized();
        // o nó da corda fica na origem: posições locais = globais
        area.AddChild(new CollisionShape3D
        {
            Shape = new CylinderShape3D { Radius = 0.45f, Height = Length + 1.2f },
            Transform = new Transform3D(BasisFromUp(dir), mid),
        });

        var ropeMat = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.48f, 0.3f), Roughness = 0.9f };
        var knotMat = new StandardMaterial3D { AlbedoColor = new Color(0.5f, 0.38f, 0.22f), Roughness = 0.95f };
        var metal = MeshFactory.Metal(new Color(0.3f, 0.32f, 0.36f), 0.35f);
        var neon = Fixed ? new Color(1f, 0.75f, 0.3f) : new Color(0.3f, 1f, 0.8f);

        // estaca e argola no alto
        Add(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 0.7f, RadialSegments = 8 }, metal, Top + new Vector3(0, 0.15f, 0), Basis.Identity);
        Add(new TorusMesh { InnerRadius = 0.07f, OuterRadius = 0.11f, Rings = 12, RingSegments = 6 }, MeshFactory.Emissive(neon, neon, 1.4f), Top + new Vector3(0, 0.45f, 0), Basis.Identity);
        // a corda: um nó (pivô) no alto que balança levemente
        _strand = new Node3D();
        AddChild(_strand);
        _strand.GlobalPosition = Top + new Vector3(0, 0.4f, 0);
        var down = Bottom - _strand.GlobalPosition;
        float len = down.Length();
        var local = new Node3D();
        _strand.AddChild(local);
        local.Basis = BasisFromUp(-down.Normalized());
        var rope = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.028f, BottomRadius = 0.028f, Height = len, RadialSegments = 6 }, MaterialOverride = ropeMat, Position = new Vector3(0, -len / 2, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        local.AddChild(rope);
        // nós a cada 0,6 m (onde as mãos seguram) e faixas refletivas
        for (float s = 0.5f; s < len - 0.2f; s += 0.6f)
        {
            local.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.05f, Height = 0.09f, RadialSegments = 6, Rings = 3 }, MaterialOverride = knotMat, Position = new Vector3(0, -s, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            if (((int)(s / 0.6f)) % 4 == 0)
                local.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.034f, BottomRadius = 0.034f, Height = 0.06f, RadialSegments = 6 }, MaterialOverride = MeshFactory.Emissive(neon, neon, 1.2f), Position = new Vector3(0, -s - 0.3f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        // sobra enrolada no chão
        Add(new TorusMesh { InnerRadius = 0.12f, OuterRadius = 0.22f, Rings = 12, RingSegments = 6 }, ropeMat, Bottom + new Vector3(0.15f, 0.04f, 0.1f), Basis.Identity);
        // luz fraca na argola (ajuda a achar a corda no escuro)
        AddChild(new OmniLight3D { LightColor = neon, LightEnergy = 0.5f, OmniRange = 3.5f, Position = Top + new Vector3(0, 0.6f, 0) });
    }

    void Add(Mesh m, Material mat, Vector3 globalPos, Basis b)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(mi);
        mi.GlobalTransform = new Transform3D(b, globalPos);
    }

    public static Basis BasisFromUp(Vector3 up)
    {
        up = up.Normalized();
        var x = up.Cross(Vector3.Forward);
        if (x.LengthSquared() < 0.001f) x = up.Cross(Vector3.Right);
        x = x.Normalized();
        var z = x.Cross(up).Normalized();
        return new Basis(x, up, z);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_strand != null) _strand.Rotation = new Vector3(Mathf.Sin(_t * 0.9f) * 0.015f, 0, Mathf.Cos(_t * 0.7f) * 0.015f);
    }

    /// <summary>O jogador está mais perto do alto?</summary>
    public bool PlayerAtTop()
    {
        var p = PlayerPos?.Invoke() ?? Bottom;
        return p.DistanceTo(Top) < p.DistanceTo(Bottom);
    }

    public string GetPrompt()
    {
        if (BuildingManager.Instance?.Active == true) return "";
        return PlayerAtTop() ? Loc.F("{0} Descer pela corda ({1:0} m)", InputHints.Interact, Length) : Loc.F("{0} Subir pela corda ({1:0} m)", InputHints.Interact, Length);
    }

    public void Interact(PlayerController player)
    {
        player.StartClimb(this, !PlayerAtTop());
        AudioManager.Instance?.Play("click", -8f, 0.6f);
    }
}
