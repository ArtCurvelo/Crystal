using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Marcador 3D do objetivo atual (seta flutuante + distância), visível através das paredes.
/// Guia o jogador nos primeiros minutos: cristal → venda → terminal → portão → Coração.
/// </summary>
public partial class ObjectiveMarker : Node3D
{
    public WorldBuilder World;
    public System.Func<Vector3> PlayerPos;
    public System.Func<RegionDef> CurrentRegion;

    MeshInstance3D _arrow;
    Label3D _label;
    StandardMaterial3D _mat;
    float _t, _retarget;
    Node3D _target;
    float _targetHeight = 2f;

    public Vector3? TargetPosition => _target != null && IsInstanceValid(_target) ? _target.GlobalPosition : null;

    public override void _Ready()
    {
        _mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1f, 0.8f, 0.3f),
            NoDepthTest = true,
            RenderPriority = 10,
        };
        _arrow = new MeshInstance3D
        {
            Mesh = MeshFactory.Spike(4, 0.28f, 0.6f),
            MaterialOverride = _mat,
            Rotation = new Vector3(Mathf.Pi, 0, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_arrow);
        _label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            FontSize = 40,
            OutlineSize = 10,
            PixelSize = 0.006f,
            Modulate = new Color(1f, 0.85f, 0.4f),
            Position = new Vector3(0, 0.55f, 0),
            RenderPriority = 11,
        };
        AddChild(_label);
        Visible = false;
    }

    public override void _Process(double delta)
    {
        long __t = Core.Prof.Start();
        _ProcessInner(delta);
        Core.Prof.End("marker", __t);
    }

    void _ProcessInner(double delta)
    {
        var gs = GameState.Instance;
        if (gs == null || !gs.IsPlaying || PlayerPos == null) { Visible = false; return; }
        _t += (float)delta;
        _retarget -= (float)delta;
        if (_retarget <= 0) { _retarget = 0.4f; PickTarget(gs); }
        if (_target == null || !IsInstanceValid(_target) || !_target.IsVisibleInTree()) { Visible = false; return; }
        var p = PlayerPos();
        var tp = _target.GlobalPosition;
        float dist = new Vector2(tp.X - p.X, tp.Z - p.Z).Length();
        if (dist < 3.5f) { Visible = false; return; }
        Visible = true;
        GlobalPosition = tp + Vector3.Up * (_targetHeight + 0.25f * Mathf.Sin(_t * 3f));
        Scale = Vector3.One * Mathf.Clamp(dist / 6f, 1.4f, 4f); // legível de longe
        _arrow.RotateObjectLocal(Vector3.Up, (float)delta * 2f);
        _label.Text = $"{dist:0} m";
        float pulse = 0.75f + 0.25f * Mathf.Sin(_t * 5f);
        _mat.AlbedoColor = new Color(1f, 0.8f * pulse + 0.1f, 0.3f);
    }

    void PickTarget(GameState gs)
    {
        var d = gs.Data;
        var region = CurrentRegion?.Invoke() ?? GameData.Regions[0];
        var root = World.RegionRoots[region.Index];
        _target = null;
        _targetHeight = 2.2f;
        if (PlayerHouse.Contains(PlayerPos()) && World.House != null)
        {
            // em casa: de noite a cama; de dia a porta (o elevador fica na vila)
            _target = gs.DayOver ? World.House.Bed : World.House.DoorInside;
            _targetHeight = gs.DayOver ? 1.6f : 2.4f;
            return;
        }
        if (Town.Contains(PlayerPos()) && World.Town != null)
        {
            // na vila: de noite, encomendas prontas ou a casa; de dia, o elevador
            // história: o Velho Bram chamando, ou a chave pronta para forjar
            if (World.Town.Bram != null && gs.Story is StoryData.BramCalls or StoryData.BringMold or StoryData.OpenGate) { _target = World.Town.Bram; _targetHeight = 2.4f; return; }
            if (gs.CanForgeKey && World.Town.Anvil != null) { _target = World.Town.Anvil; _targetHeight = 2.2f; return; }
            bool deliver = false;
            for (int i = 0; i < gs.Data.Contracts.Count; i++) deliver |= gs.CanDeliver(i);
            _target = gs.DayOver ? (deliver ? World.Town.Board : World.Town.DoorOutside) : World.Town.Elevator;
            _targetHeight = gs.DayOver ? 2.8f : 6.5f;
            return;
        }
        if (!gs.DayOver && World.SealedGate != null && region.Id == "cathedral" && gs.Story is StoryData.FindGate or StoryData.OpenGate)
        {
            _target = World.SealedGate;
            _targetHeight = 10f;
            return;
        }
        if (gs.DayOver)
        {
            // fim do turno: vender (se houver o que vender) e voltar ao elevador
            _target = gs.InventoryCount > 0 ? World.SellStation : World.Elevator;
            _targetHeight = 4.2f;
        }
        else if (d.CrystalsMined == 0)
        {
            _target = Nearest(root);
            _targetHeight = 2.0f;
        }
        else if (gs.InventoryFull || (d.ShardsSold == 0 && gs.InventoryCount >= 8))
            _target = World.SellStation;
        else if (d.Upgrades.Count == 0 && d.ShardsSold > 0)
            _target = Station<ShopTerminal>(root);
        else
        {
            var next = gs.NextLockedRegion();
            if (next != null && gs.CanUnlock(next))
            {
                foreach (var g in World.Gates) if (g.Region == next) _target = g;
                _targetHeight = 6f;
            }
            else if (gs.IsUnlocked("core") && !d.Discovered.Contains("heart") && gs.DamagePerSecond * 120 > GameData.Mat("heart").Hp)
            {
                foreach (var c in World.Crystals) if (c.Material.Special) _target = c;
                _targetHeight = 7.5f;
            }
        }
        // alvo no acampamento visto de uma região mais funda: aponta o caminho de volta (portão do túnel)
        if (_target != null && region.Index > 0 && (_target == World.SellStation || _target == World.Elevator))
        {
            _target = World.Gates[region.Index - 1];
            _targetHeight = 6f;
        }
    }

    Node3D Nearest(Node3D root)
    {
        Crystal best = null;
        float bd = float.MaxValue;
        var p = PlayerPos();
        foreach (var c in World.Crystals)
        {
            if (c.GetParent() != root || !c.IsMineable || c.Material.Special) continue;
            float dd = c.GlobalPosition.DistanceSquaredTo(p);
            if (dd < bd) { bd = dd; best = c; }
        }
        return best;
    }

    static T Station<T>(Node3D root) where T : Node3D
    {
        foreach (Node c in root.GetChildren()) if (c is T t) return t;
        return null;
    }
}
