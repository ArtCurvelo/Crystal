using System.Collections.Generic;
using System.Linq;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

// =========================================================================== funil
public partial class Hopper : StationBase, ShardManager.IShardSink
{
    public int BuildingId;
    public BuildingManager Manager;
    public float Feed;
    Label3D _count;

    public Vector3 SinkPoint => GlobalPosition + Vector3.Up * 1.5f;

    public bool AcceptShard(MaterialDef m)
    {
        GameState.Instance.HopperAdd(BuildingId, m.Id);
        AudioManager.Instance?.Play3D("pickup", SinkPoint, -14f, 0.8f);
        return true;
    }

    public override void _Ready()
    {
        SetupBody(new Vector3(1.4f, 1.7f, 1.4f));
        var metal = MeshFactory.Metal(new Color(0.15f, 0.16f, 0.19f), 0.4f);
        var dark = MeshFactory.Metal(new Color(0.07f, 0.07f, 0.09f), 0.6f);
        var neon = new Color(0.3f, 1f, 0.8f);
        foreach (var (x, z) in new[] { (-0.55f, -0.55f), (0.55f, -0.55f), (-0.55f, 0.55f), (0.55f, 0.55f) })
            AddMesh(new BoxMesh { Size = new Vector3(0.12f, 0.9f, 0.12f) }, metal, new Vector3(x, 0.45f, z));
        AddMesh(new BoxMesh { Size = new Vector3(1.3f, 0.35f, 1.3f) }, dark, new Vector3(0, 0.3f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(1.32f, 0.05f, 1.32f) }, MeshFactory.Emissive(neon, neon, 0.9f), new Vector3(0, 0.5f, 0));
        var funnelMat = MeshFactory.Metal(new Color(0.32f, 0.34f, 0.38f), 0.3f);
        funnelMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        AddMesh(new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.28f, Height = 0.8f, RadialSegments = 16, CapTop = false }, funnelMat, new Vector3(0, 1.3f, 0));
        AddMesh(new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.26f, Height = 0.05f, RadialSegments = 12 }, MeshFactory.Emissive(neon, neon, 1.4f), new Vector3(0, 0.92f, 0));
        Ring = new Node3D { Position = new Vector3(0, 1.72f, 0) };
        AddChild(Ring);
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.84f, OuterRadius = 0.89f, Rings = 32, RingSegments = 6 }, MaterialOverride = MeshFactory.Emissive(neon, neon, 1.1f) };
        Ring.AddChild(ring);
        AddChild(new OmniLight3D { LightColor = neon, LightEnergy = 1.1f, OmniRange = 6f, Position = new Vector3(0, 1.8f, 0) });
        Label = MakeLabel("FUNIL", neon, 2.45f, 52);
        _count = new Label3D
        {
            FontSize = 34, OutlineSize = 10, PixelSize = 0.005f, Modulate = new Color(0.85f, 1f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 2.15f, 0), VisibilityRangeEnd = 20f,
        };
        AddChild(_count);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        int q = GameState.Instance?.HopperQueued(BuildingId) ?? 0;
        bool outlet = Manager?.HasOutlet(BuildingId) ?? false;
        string t = !outlet ? Loc.T("sem esteira") : q > 0 ? Loc.F("na fila: {0}", q) : "";
        if (_count.Text != t) _count.Text = t;
    }

    public override string GetPrompt()
    {
        if (BuildingManager.Instance?.Active == true) return "";
        var gs = GameState.Instance;
        if (gs.InventoryCount > 0) return Loc.F("{0} Colocar {1} fragmentos no funil", InputHints.Interact, gs.InventoryCount);
        float d = Manager?.DistanceToSell(BuildingId) ?? -1;
        return d < 0 ? Loc.T("Funil sem esteira ao lado (coloque uma esteira encostada nele)") : Loc.F("Funil · {0:0} m de esteira até a Estação de Venda", d);
    }

    public override void Interact(PlayerController player)
    {
        int n = GameState.Instance.DepositInventoryInHopper(BuildingId);
        if (n > 0) AudioManager.Instance?.Play("sell", -6f, 1.1f);
    }
}

// =========================================================================== entrada da esteira
/// <summary>Começo da esteira: o jogador deposita a mochila aqui; drones e robôs também entregam aqui.</summary>
public partial class BeltIntake : StationBase
{
    public int BuildingId;
    public BuildingManager Manager;
    Label3D _count;

    public override void _Ready()
    {
        SetupBody(new Vector3(0.9f, 0.75f, 0.9f));
        var metal = MeshFactory.Metal(new Color(0.17f, 0.18f, 0.21f), 0.4f);
        var dark = MeshFactory.Metal(new Color(0.07f, 0.07f, 0.09f), 0.6f);
        var neon = new Color(0.3f, 1f, 0.8f);
        AddMesh(new BoxMesh { Size = new Vector3(0.9f, 0.5f, 0.9f) }, dark, new Vector3(0, 0.25f, 0));
        var mouth = MeshFactory.Metal(new Color(0.3f, 0.32f, 0.36f), 0.3f);
        mouth.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        AddMesh(new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.22f, Height = 0.32f, RadialSegments = 12, CapTop = false }, mouth, new Vector3(0, 0.66f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(0.92f, 0.04f, 0.92f) }, MeshFactory.Emissive(neon, neon, 1.0f), new Vector3(0, 0.5f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(0.94f, 0.08f, 0.94f) }, metal, new Vector3(0, 0.04f, 0));
        Label = MakeLabel("ESTEIRA", neon, 1.35f, 40);
        _count = new Label3D
        {
            FontSize = 30, OutlineSize = 10, PixelSize = 0.005f, Modulate = new Color(0.85f, 1f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 1.1f, 0), VisibilityRangeEnd = 18f,
        };
        AddChild(_count);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        int q = GameState.Instance?.HopperQueued(BuildingId) ?? 0;
        bool outlet = Manager?.HasOutlet(BuildingId) ?? false;
        string t = !outlet ? Loc.T("sem saída") : q > 0 ? Loc.F("na fila: {0}", q) : "";
        if (_count.Text != t) _count.Text = t;
    }

    public override string GetPrompt()
    {
        if (BuildingManager.Instance?.Active == true) return "";
        var gs = GameState.Instance;
        if (gs.InventoryCount > 0) return Loc.F("{0} Colocar {1} fragmentos na esteira", InputHints.Interact, gs.InventoryCount);
        float d = Manager?.DistanceToSell(BuildingId) ?? -1;
        return d < 0 ? Loc.T("Esteira sem saída (a esteira de destino foi desmontada)") : Loc.F("Esteira · {0:0} m até a Estação de Venda", d);
    }

    public override void Interact(PlayerController player)
    {
        int n = GameState.Instance.DepositInventoryInHopper(BuildingId);
        if (n > 0) AudioManager.Instance?.Play("sell", -6f, 1.1f);
    }
}

// =========================================================================== base de drone / robô
/// <summary>Base fixa de um drone ou robô minerador: define a área de trabalho e guarda a carga quando não há funil/esteira por perto.</summary>
public partial class Dock : StationBase
{
    public int BuildingId;
    public BuildingManager Manager;
    public bool IsRobot;
    public int RegionIndex;
    public WorkUnit Unit;
    public float Range => IsRobot ? BuildData.RobotRange : BuildData.DroneRange;
    MeshInstance3D _area;
    Label3D _info;

    public override void _Ready()
    {
        SetupBody(new Vector3(1.2f, 0.3f, 1.2f));
        var dark = MeshFactory.Metal(new Color(0.07f, 0.07f, 0.09f), 0.6f);
        var metal = MeshFactory.Metal(new Color(0.2f, 0.21f, 0.24f), 0.35f);
        var neon = IsRobot ? new Color(1f, 0.7f, 0.25f) : new Color(0.45f, 0.85f, 1f);
        AddMesh(new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.66f, Height = 0.18f, RadialSegments = 20 }, dark, new Vector3(0, 0.09f, 0));
        AddMesh(new CylinderMesh { TopRadius = 0.48f, BottomRadius = 0.48f, Height = 0.03f, RadialSegments = 20 }, metal, new Vector3(0, 0.19f, 0));
        AddMesh(new TorusMesh { InnerRadius = 0.5f, OuterRadius = 0.56f, Rings = 28, RingSegments = 6 }, MeshFactory.Emissive(neon, neon, 1.4f), new Vector3(0, 0.19f, 0));
        // poste com antena
        AddMesh(new BoxMesh { Size = new Vector3(0.1f, 0.9f, 0.1f) }, metal, new Vector3(0.62f, 0.45f, 0.62f));
        AddMesh(new SphereMesh { Radius = 0.07f, Height = 0.14f }, MeshFactory.Emissive(neon, neon, 2f), new Vector3(0.62f, 0.95f, 0.62f));
        // área de trabalho (aparece no modo construção)
        var areaMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(neon, 0.55f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _area = AddMesh(new TorusMesh { InnerRadius = Range - 0.12f, OuterRadius = Range, Rings = 96, RingSegments = 4 }, areaMat, new Vector3(0, 0.15f, 0));
        _area.Visible = false;
        Label = MakeLabel(IsRobot ? "ROBÔ" : "DRONE", neon, 1.25f, 40);
        _info = new Label3D
        {
            FontSize = 28, OutlineSize = 10, PixelSize = 0.005f, Modulate = new Color(0.9f, 0.95f, 1f),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 1.0f, 0), VisibilityRangeEnd = 18f,
        };
        AddChild(_info);
        Unit = IsRobot ? new MinerBot { Dock = this, Name = "Robot" } : new DroneBot { Dock = this, Name = "Drone" };
        Unit.TopLevel = true;
        AddChild(Unit);
    }

    public void ShowArea(bool on) { if (_area != null) _area.Visible = on; }

    public int Stored => GameState.Instance?.HopperQueued(BuildingId) ?? 0;

    public override void _Process(double delta)
    {
        base._Process(delta);
        int s = Stored;
        string t = s > 0 ? Loc.F("guardado: {0}", s) : "";
        if (_info.Text != t) _info.Text = t;
    }

    public override string GetPrompt()
    {
        if (BuildingManager.Instance?.Active == true) return "";
        int s = Stored;
        if (s > 0) return Loc.F("{0} Pegar {1} fragmentos guardados na base", InputHints.Interact, s);
        string what = IsRobot ? Loc.T("Robô mineiro") : Loc.T("Drone de área");
        return Loc.F("{0} · área de {1:0} m · {2}", what, Range, Unit?.Status ?? "");
    }

    public override void Interact(PlayerController player)
    {
        var gs = GameState.Instance;
        int n = 0;
        while (gs.FreeSpace > 0)
        {
            var id = gs.HopperTake(BuildingId);
            if (id == null) break;
            if (gs.AddItem(id) <= 0) { gs.HopperAdd(BuildingId, id); break; }
            n++;
        }
        if (n > 0) AudioManager.Instance?.Play("pickup", -6f, 1.1f);
        else if (Stored > 0) gs.Notify("Mochila cheia!", Loc.T("Esvazie a mochila para pegar o que está na base."), NotifyKind.Warning);
    }
}

// =========================================================================== unidades de trabalho
/// <summary>Drone ou robô ligado a uma base: carrega fragmentos e entrega no funil/esteira mais perto (ou na base).</summary>
public abstract partial class WorkUnit : Node3D
{
    public Dock Dock;
    protected readonly List<MaterialDef> Cargo = new();
    protected bool Placed;
    public int CargoCount => Cargo.Count;
    /// <summary>O que está fazendo (texto curto para o jogador).</summary>
    public string Status { get; protected set; } = "";

    public List<MaterialDef> TakeCargo()
    {
        var l = new List<MaterialDef>(Cargo);
        Cargo.Clear();
        return l;
    }

    /// <summary>Trabalha só durante o turno, com a região carregada.</summary>
    protected bool Working
    {
        get
        {
            var gs = GameState.Instance;
            return gs != null && gs.IsPlaying && !gs.DayOver && Dock != null && Dock.IsVisibleInTree();
        }
    }

    /// <summary>Para onde levar a carga: funil/esteira na área (id, ponto) ou a base, se ainda couber.</summary>
    protected bool DeliveryTarget(out int id, out Vector3 at)
    {
        id = Dock.Manager.NearestIntake(Dock.GlobalPosition, Dock.Range, out at);
        if (id > 0) return true;
        if (Dock.Stored + Cargo.Count <= BuildData.DockStorage) { id = Dock.BuildingId; at = Dock.GlobalPosition; return true; }
        return false;
    }

    protected void Unload(int id)
    {
        var gs = GameState.Instance;
        foreach (var m in Cargo) gs.HopperAdd(id, m.Id);
        Cargo.Clear();
        AudioManager.Instance?.Play3D("sell", GlobalPosition, -12f, 1.2f);
    }
}

/// <summary>Drone de área: recolhe fragmentos soltos dentro da área da base e entrega.</summary>
public partial class DroneBot : WorkUnit
{
    enum Mode { Patrol, Fetch, Deliver }
    Mode _mode = Mode.Patrol;
    ShardManager.Shard _target;
    int _deliverId;
    Vector3 _deliverAt;
    float _scan, _t, _idle;
    readonly List<Node3D> _rotors = new();
    Label3D _cargoLabel;
    PhysicsRayQueryParameters3D _ray;

    public override void _Ready()
    {
        var shell = MeshFactory.Metal(new Color(0.14f, 0.15f, 0.18f), 0.3f);
        var dark = MeshFactory.Metal(new Color(0.06f, 0.06f, 0.08f), 0.5f);
        var neon = new Color(0.45f, 0.85f, 1f);
        var body = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.26f, Height = 0.24f, RadialSegments = 16, Rings = 8 }, MaterialOverride = shell };
        AddChild(body);
        AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.25f, OuterRadius = 0.29f, Rings = 24, RingSegments = 6 }, MaterialOverride = MeshFactory.Emissive(neon, neon, 1.1f) });
        var eye = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f }, MaterialOverride = MeshFactory.Emissive(neon, neon, 1.8f), Position = new Vector3(0, -0.02f, -0.24f) };
        AddChild(eye);
        foreach (var (x, z) in new[] { (0.36f, 0.36f), (-0.36f, 0.36f), (0.36f, -0.36f), (-0.36f, -0.36f) })
        {
            var arm = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.04f, 0.5f) }, MaterialOverride = dark, Position = new Vector3(x * 0.5f, 0.03f, z * 0.5f), Rotation = new Vector3(0, Mathf.Atan2(x, z), 0) };
            AddChild(arm);
            var rotor = new Node3D { Position = new Vector3(x, 0.08f, z) };
            AddChild(rotor);
            rotor.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.01f, RadialSegments = 16 }, MaterialOverride = MeshFactory.Unshaded(new Color(0.5f, 0.75f, 0.9f, 0.08f), true) });
            rotor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.32f, 0.012f, 0.04f) }, MaterialOverride = dark });
            _rotors.Add(rotor);
        }
        AddChild(new OmniLight3D { LightColor = new Color(0.8f, 0.92f, 1f), LightEnergy = 1.3f, OmniRange = 11f, Position = new Vector3(0, -0.45f, 0) });
        _cargoLabel = new Label3D
        {
            FontSize = 30, OutlineSize = 8, PixelSize = 0.004f, Modulate = new Color(0.8f, 0.95f, 1f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 0.4f, 0), VisibilityRangeEnd = 15f,
        };
        AddChild(_cargoLabel);
    }

    bool Visible3D(Vector3 from, Vector3 to)
    {
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = PlayerController.WorldLayer };
        _ray.From = from; _ray.To = to;
        return GetWorld3D().DirectSpaceState.IntersectRay(_ray).Count == 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        foreach (var r in _rotors) r.RotateY(dt * 40f);
        if (Dock == null) return;
        var home = Dock.GlobalPosition + Vector3.Up * 2.4f;
        if (!Placed || GlobalPosition.DistanceTo(Dock.GlobalPosition) > Dock.Range + 25f) { GlobalPosition = home; Placed = true; Release(); }
        Vector3 goal;
        float speed = 8f;
        if (!Working)
        {
            Release();
            goal = Dock.GlobalPosition + Vector3.Up * 0.45f; // pousa na base
            speed = 4f;
            Status = Loc.T("parado (fora do turno)");
        }
        else switch (_mode)
        {
            case Mode.Fetch:
                if (_target == null || !_target.Alive) { _target = null; _mode = Mode.Patrol; goto default; }
                goal = _target.Position + Vector3.Up * 0.35f;
                if (GlobalPosition.DistanceTo(goal) < 0.45f)
                {
                    var m = ShardManager.Instance?.Take(_target);
                    _target = null;
                    if (m != null) { Cargo.Add(m); AudioManager.Instance?.Play3D("pickup", GlobalPosition, -10f, 1.3f); }
                    _mode = Mode.Patrol;
                }
                break;
            case Mode.Deliver:
                goal = _deliverAt + Vector3.Up * 1.6f;
                if (GlobalPosition.DistanceTo(goal) < 0.6f)
                {
                    Unload(_deliverId);
                    _mode = Mode.Patrol;
                }
                break;
            default:
            {
                // ronda em volta da base
                goal = home + new Vector3(Mathf.Cos(_t * 0.5f) * 1.5f, Mathf.Sin(_t * 2f) * 0.12f, Mathf.Sin(_t * 0.5f) * 1.5f);
                speed = 5f;
                Status = Cargo.Count > 0 ? Loc.F("carregando {0}", Cargo.Count) : Loc.T("procurando fragmentos");
                _scan -= dt;
                if (_scan <= 0 && Cargo.Count < BuildData.DroneCargo && ShardManager.Instance != null)
                {
                    _scan = 0.35f;
                    var me = GlobalPosition;
                    var s = ShardManager.Instance.NearestLoose(Dock.GlobalPosition, Dock.Range, p => Visible3D(me, p + Vector3.Up * 0.3f));
                    if (s != null) { s.Claimed = true; _target = s; _mode = Mode.Fetch; _idle = 0; break; }
                    _idle += 0.35f;
                }
                // cheio, ou nada mais para pegar: entrega
                if (Cargo.Count > 0 && (Cargo.Count >= BuildData.DroneCargo || _idle > 1.5f))
                {
                    if (DeliveryTarget(out _deliverId, out _deliverAt)) { _mode = Mode.Deliver; _idle = 0; Status = Loc.T("entregando"); }
                    else Status = Loc.T("base cheia");
                }
                break;
            }
        }
        var to = goal - GlobalPosition;
        float dist = to.Length();
        if (dist > 0.01f)
        {
            var step = to / dist * Mathf.Min(dist, speed * dt * Mathf.Clamp(dist, 0.4f, 1.5f));
            GlobalPosition += step;
            var flat = new Vector3(to.X, 0, to.Z);
            if (flat.LengthSquared() > 0.04f)
            {
                float yaw = Mathf.Atan2(-flat.X, -flat.Z);
                Rotation = new Vector3(Mathf.Clamp(-dist * 0.05f, -0.25f, 0), Mathf.LerpAngle(Rotation.Y, yaw, Mathf.Min(1, dt * 5f)), 0);
            }
        }
        string label = Cargo.Count > 0 ? $"{Cargo.Count}/{BuildData.DroneCargo}" : "";
        if (_cargoLabel.Text != label) _cargoLabel.Text = label;
    }

    void Release()
    {
        if (_target != null && _target.Alive) _target.Claimed = false;
        _target = null;
        _mode = Mode.Patrol;
    }
}

/// <summary>
/// Robô mineiro: anda até os depósitos dentro da área da base, minera (metade da força do goblin
/// da região) e leva os fragmentos ao funil/esteira mais perto. Para fora do turno.
/// </summary>
public partial class MinerBot : WorkUnit
{
    enum Mode { Idle, ToCrystal, Mining, ToDeliver, Home }
    Mode _mode = Mode.Idle;
    Crystal _target;
    List<Vector3> _path;
    int _pathIdx, _deliverId;
    float _swing, _think, _t;
    Node3D _arm, _bit, _body;
    readonly List<MeshInstance3D> _treads = new();
    MeshInstance3D _eye;
    Label3D _cargoLabel;

    CaveLayout L => Dock.Manager.World.LayoutAt(Dock.GlobalPosition);
    float Dps
    {
        get
        {
            var r = GameData.Regions[Mathf.Clamp(Dock.RegionIndex, 0, GameData.Regions.Count - 1)];
            return (GoblinData.Of(r.Id)?.MineDps ?? 4f) * BuildData.RobotDpsFactor;
        }
    }

    public override void _Ready()
    {
        var shell = MeshFactory.Metal(new Color(0.85f, 0.55f, 0.18f), 0.35f);
        var dark = MeshFactory.Metal(new Color(0.08f, 0.08f, 0.1f), 0.55f);
        var steel = MeshFactory.Metal(new Color(0.45f, 0.47f, 0.5f), 0.25f);
        var neon = new Color(1f, 0.7f, 0.25f);
        _body = new Node3D();
        AddChild(_body);
        MeshInstance3D M(Node3D parent, Mesh m, Material mat, Vector3 pos, Vector3? rot = null)
        {
            var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            parent.AddChild(mi);
            return mi;
        }
        // esteiras (lagartas)
        foreach (float x in new[] { -0.28f, 0.28f })
        {
            _treads.Add(M(_body, new BoxMesh { Size = new Vector3(0.16f, 0.2f, 0.7f) }, dark, new Vector3(x, 0.1f, 0)));
            M(_body, new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.1f, Height = 0.17f, RadialSegments = 10 }, steel, new Vector3(x, 0.1f, 0.33f), new Vector3(0, 0, Mathf.Pi / 2));
            M(_body, new CylinderMesh { TopRadius = 0.1f, BottomRadius = 0.1f, Height = 0.17f, RadialSegments = 10 }, steel, new Vector3(x, 0.1f, -0.33f), new Vector3(0, 0, Mathf.Pi / 2));
        }
        // chassi e cabeça
        M(_body, new BoxMesh { Size = new Vector3(0.5f, 0.32f, 0.62f) }, shell, new Vector3(0, 0.36f, 0));
        M(_body, new BoxMesh { Size = new Vector3(0.52f, 0.05f, 0.64f) }, dark, new Vector3(0, 0.53f, 0));
        M(_body, new SphereMesh { Radius = 0.18f, Height = 0.26f, RadialSegments = 14, Rings = 7 }, shell, new Vector3(0, 0.62f, 0.08f));
        _eye = M(_body, new BoxMesh { Size = new Vector3(0.2f, 0.05f, 0.04f) }, MeshFactory.Emissive(neon, neon, 2.2f), new Vector3(0, 0.66f, -0.08f));
        M(_body, new BoxMesh { Size = new Vector3(0.03f, 0.25f, 0.03f) }, steel, new Vector3(0.14f, 0.82f, 0.12f));
        M(_body, new SphereMesh { Radius = 0.035f, Height = 0.07f }, MeshFactory.Emissive(neon, neon, 2f), new Vector3(0.14f, 0.95f, 0.12f));
        // caçamba de carga atrás
        M(_body, new BoxMesh { Size = new Vector3(0.42f, 0.16f, 0.22f) }, dark, new Vector3(0, 0.6f, 0.26f));
        // braço com broca
        _arm = new Node3D { Position = new Vector3(0, 0.42f, -0.3f) };
        _body.AddChild(_arm);
        M(_arm, new BoxMesh { Size = new Vector3(0.1f, 0.1f, 0.34f) }, steel, new Vector3(0, 0, -0.17f));
        _bit = new Node3D { Position = new Vector3(0, 0, -0.36f) };
        _arm.AddChild(_bit);
        M(_bit, new CylinderMesh { TopRadius = 0.0f, BottomRadius = 0.08f, Height = 0.24f, RadialSegments = 8 }, steel, new Vector3(0, 0, -0.1f), new Vector3(-Mathf.Pi / 2, 0, 0));
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.85f, 0.6f), LightEnergy = 0.9f, OmniRange = 4f, Position = new Vector3(0, 0.7f, -0.4f), ShadowEnabled = false });
        _cargoLabel = new Label3D
        {
            FontSize = 28, OutlineSize = 8, PixelSize = 0.004f, Modulate = new Color(1f, 0.92f, 0.8f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 1.05f, 0), VisibilityRangeEnd = 15f,
        };
        AddChild(_cargoLabel);
    }

    void GoTo(Vector3 dest)
    {
        _path = L.FindPath(GlobalPosition, dest);
        _pathIdx = 0;
    }

    /// <summary>Anda pelo caminho; devolve true ao chegar (ou se não há caminho).</summary>
    bool Follow(float dt)
    {
        if (_path == null || _pathIdx >= _path.Count) return true;
        var target = _path[_pathIdx];
        var pos = GlobalPosition;
        var d = new Vector3(target.X - pos.X, 0, target.Z - pos.Z);
        float dist = d.Length();
        if (dist < 0.3f) { _pathIdx++; return _pathIdx >= _path.Count; }
        var np = pos + d / dist * Mathf.Min(dist, BuildData.RobotSpeed * dt);
        np.Y = L.Ground(np.X, np.Z);
        GlobalPosition = np;
        Face(d, dt);
        foreach (var t in _treads) t.Position = new Vector3(t.Position.X, 0.1f + Mathf.Sin(_t * 30f) * 0.008f, 0);
        return false;
    }

    void Face(Vector3 dir, float dt)
    {
        if (dir.LengthSquared() < 1e-4f) return;
        float yaw = Mathf.Atan2(-dir.X, -dir.Z);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, yaw, Mathf.Min(1f, dt * 8f)), 0);
    }

    /// <summary>Depósito de chão mais perto do robô, dentro da área da base.</summary>
    Crystal PickCrystal()
    {
        Crystal best = null; float bd = float.MaxValue;
        var basePos = Dock.GlobalPosition;
        foreach (var c in Dock.Manager.World.Crystals)
        {
            if (!c.IsMineable || c.Material.Special) continue;
            if (c.GlobalTransform.Basis.Y.Normalized().Y < 0.9f) continue; // não alcança parede/teto
            var sp = c.StandPoint;
            if (new Vector2(sp.X - basePos.X, sp.Z - basePos.Z).Length() > Dock.Range || Mathf.Abs(sp.Y - basePos.Y) > 4f) continue;
            if (WorldBuilder.RegionAt(sp).Index != Dock.RegionIndex) continue;
            float d = sp.DistanceSquaredTo(GlobalPosition);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        if (Dock == null) return;
        if (!Placed) { GlobalPosition = Dock.GlobalPosition + Dock.GlobalBasis.Z * 1.2f; GlobalPosition = new Vector3(GlobalPosition.X, L.Ground(GlobalPosition.X, GlobalPosition.Z), GlobalPosition.Z); Placed = true; }
        if (!Working)
        {
            _target = null;
            if (_mode != Mode.Home && _mode != Mode.Idle) { _mode = Mode.Home; GoTo(Dock.GlobalPosition + Dock.GlobalBasis.Z * 1.2f); }
            if (_mode == Mode.Home && Follow(dt)) _mode = Mode.Idle;
            Status = Loc.T("parado (fora do turno)");
            UpdateLabel();
            return;
        }
        switch (_mode)
        {
            case Mode.Idle:
                _think -= dt;
                if (_think > 0) break;
                _think = 0.6f;
                if (Cargo.Count >= BuildData.RobotCargo) { StartDeliver(); break; }
                _target = PickCrystal();
                if (_target != null) { GoTo(_target.StandPoint); _mode = Mode.ToCrystal; Status = Loc.T("indo minerar"); break; }
                if (Cargo.Count > 0) { StartDeliver(); break; }
                Status = Loc.T("sem depósitos na área");
                _think = 2f;
                break;
            case Mode.ToCrystal:
                if (_target == null || !_target.IsMineable) { _mode = Mode.Idle; break; }
                if (Follow(dt)) { _mode = Mode.Mining; _swing = 0; }
                break;
            case Mode.Mining:
                if (_target == null || !_target.IsMineable) { _target = null; _mode = Mode.Idle; break; }
                Face(_target.Center - GlobalPosition, dt);
                Status = Loc.T("minerando");
                _swing += dt;
                _bit.RotateZ(dt * 30f);
                _arm.Rotation = new Vector3(-0.25f + Mathf.Sin(_t * 9f) * 0.08f, 0, 0);
                if (_swing >= 1f)
                {
                    _swing = 0;
                    var cargo = Cargo;
                    _target.Hit(Dps, _target.Center, (GlobalPosition - _target.Center).Normalized(), true,
                        (m, n) => { for (int i = 0; i < n; i++) cargo.Add(m); });
                    if (!_target.IsMineable) { _target = null; _arm.Rotation = Vector3.Zero; _mode = Mode.Idle; _think = 0.3f; }
                }
                break;
            case Mode.ToDeliver:
                Status = Loc.T("entregando");
                if (Follow(dt)) { Unload(_deliverId); _mode = Mode.Idle; _think = 0.4f; }
                break;
            case Mode.Home:
                if (Follow(dt)) _mode = Mode.Idle;
                break;
        }
        UpdateLabel();
    }

    void StartDeliver()
    {
        if (!DeliveryTarget(out _deliverId, out var at)) { Status = Loc.T("base cheia"); _think = 3f; _mode = Mode.Idle; return; }
        // para ao lado do ponto de entrega
        var dir = new Vector3(GlobalPosition.X - at.X, 0, GlobalPosition.Z - at.Z);
        if (dir.LengthSquared() < 0.01f) dir = Vector3.Back;
        var stop = at + dir.Normalized() * 1.3f;
        GoTo(L.OnGround(stop.X, stop.Z));
        _mode = Mode.ToDeliver;
    }

    void UpdateLabel()
    {
        string label = Cargo.Count > 0 ? $"{Cargo.Count}" : "";
        if (_cargoLabel.Text != label) _cargoLabel.Text = label;
    }
}
