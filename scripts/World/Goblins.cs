using System;
using System.Collections.Generic;
using System.Linq;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Goblin (Grub ou um comparsa na rebelião). Corpo estático movido por código ao longo de
/// caminhos A* da caverna, sempre colado ao chão do mapa de altura.
/// Amigável: camada de interação (contratar/conversar). Rebelado: camada de goblin (o feixe acerta).
/// </summary>
public partial class GoblinNpc : StaticBody3D, IInteractable
{
    public enum Mode { Idle, ToCrystal, Mining, ToChest, Sleeping, Chasing, Attacking, Stunned, Defeated, Building, ToHome }
    public Mode State = Mode.Idle;
    public GoblinCamp Camp;
    public bool IsGrub;
    public bool Hostile { get; private set; }
    public int HitsLeft;
    public string DisplayName = "Grub";
    public Color Skin = new(0.42f, 0.66f, 0.3f);
    public Crystal Target;
    public int Carrying;
    public const int CarryMax = 8;
    public const float MineDps = 4f;
    public const float WalkSpeed = 2.6f, ChaseSpeed = 3.3f;
    public const float AttackDamage = 12f, AttackRange = 1.7f, AttackCooldown = 1.3f;

    List<Vector3> _path;
    int _pathIdx;
    float _t, _swing, _stun, _attackCd, _repath;
    Node3D _rig, _armR, _pick;
    MeshInstance3D _sack, _pickHead;
    float _buildTimer;
    int _buildSpot;
    StandardMaterial3D _eyeMat, _skinMat;
    Label3D _label;

    public override void _Ready()
    {
        CollisionMask = 0;
        AddChild(new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.2f }, Position = new Vector3(0, 0.6f, 0) });
        BuildModel();
        _label = new Label3D
        {
            Text = DisplayName, FontSize = 40, PixelSize = 0.005f, OutlineSize = 10,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 1.75f, 0),
            Modulate = new Color(0.6f, 1f, 0.5f), OutlineModulate = new Color(0, 0, 0, 0.8f), VisibilityRangeEnd = 25f,
        };
        AddChild(_label);
        SetHostile(Hostile);
    }

    void BuildModel()
    {
        _rig = new Node3D { Name = "Rig" };
        AddChild(_rig);
        _skinMat = new StandardMaterial3D { AlbedoColor = Skin, Roughness = 0.8f };
        var vest = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.3f, 0.18f), Roughness = 0.9f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.18f, 0.16f, 0.14f), Roughness = 0.9f };
        var helmet = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.72f, 0.12f), Roughness = 0.6f };
        _eyeMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.9f, 0.3f), EmissionEnabled = true, Emission = new Color(1f, 0.85f, 0.2f), EmissionEnergyMultiplier = 2f };
        void M(Mesh mesh, Material mat, Vector3 pos, Node3D parent = null, Vector3? rot = null)
            => (parent ?? _rig).AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        // pernas curtas, corpo barrigudo com colete, cabeça grande
        M(new CapsuleMesh { Radius = 0.09f, Height = 0.45f }, dark, new Vector3(-0.13f, 0.22f, 0));
        M(new CapsuleMesh { Radius = 0.09f, Height = 0.45f }, dark, new Vector3(0.13f, 0.22f, 0));
        M(new SphereMesh { Radius = 0.3f, Height = 0.6f, RadialSegments = 12, Rings = 6 }, vest, new Vector3(0, 0.62f, 0));
        M(new SphereMesh { Radius = 0.2f, Height = 0.32f, RadialSegments = 10, Rings = 5 }, _skinMat, new Vector3(0, 0.62f, -0.12f));
        M(new SphereMesh { Radius = 0.27f, Height = 0.5f, RadialSegments = 12, Rings = 6 }, _skinMat, new Vector3(0, 1.05f, 0));
        // orelhas pontudas
        M(MeshFactory.Spike(4, 0.08f, 0.38f), _skinMat, new Vector3(-0.28f, 1.1f, 0), null, new Vector3(0, 0, 1.35f));
        M(MeshFactory.Spike(4, 0.08f, 0.38f), _skinMat, new Vector3(0.28f, 1.1f, 0), null, new Vector3(0, 0, -1.35f));
        // nariz comprido e olhos que brilham
        M(MeshFactory.Spike(5, 0.06f, 0.2f), _skinMat, new Vector3(0, 1.02f, -0.27f), null, new Vector3(-Mathf.Pi / 2, 0, 0));
        M(new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 8, Rings = 4 }, _eyeMat, new Vector3(-0.1f, 1.1f, -0.22f));
        M(new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 8, Rings = 4 }, _eyeMat, new Vector3(0.1f, 1.1f, -0.22f));
        // capacete de mineiro com lanterna
        M(new SphereMesh { Radius = 0.29f, Height = 0.29f, IsHemisphere = true, RadialSegments = 12, Rings = 4 }, helmet, new Vector3(0, 1.17f, 0));
        M(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 0.06f, RadialSegments = 8, Rings = 1 }, _eyeMat, new Vector3(0, 1.3f, -0.24f), null, new Vector3(Mathf.Pi / 2, 0, 0));
        // braço direito com picareta (anima o golpe)
        _armR = new Node3D { Position = new Vector3(0.32f, 0.78f, 0) };
        _rig.AddChild(_armR);
        M(new CapsuleMesh { Radius = 0.07f, Height = 0.4f }, _skinMat, new Vector3(0, -0.15f, 0), _armR);
        _pick = new Node3D { Position = new Vector3(0, -0.32f, -0.05f) };
        _armR.AddChild(_pick);
        M(new BoxMesh { Size = new Vector3(0.04f, 0.04f, 0.55f) }, vest, new Vector3(0, 0, -0.25f), _pick);
        _pickHead = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.05f, 0.06f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.62f, 0.66f), Metallic = 0.6f, Roughness = 0.4f }, Position = new Vector3(0, 0, -0.52f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _pick.AddChild(_pickHead);
        M(new CapsuleMesh { Radius = 0.07f, Height = 0.4f }, _skinMat, new Vector3(-0.32f, 0.63f, 0));
        // saco de minério nas costas
        _sack = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.18f, Height = 0.3f, RadialSegments = 8, Rings = 4 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.45f, 0.3f) }, Position = new Vector3(0, 0.75f, 0.28f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _rig.AddChild(_sack);
    }

    /// <summary>Equipamento evolui com as melhorias: mochila maior e picareta melhor (até neon).</summary>
    public void SetGear(int carryLevel, int speedLevel)
    {
        if (_sack == null) return;
        float s = 1f + carryLevel * 0.22f;
        _sack.Scale = new Vector3(s, s, s);
        _sack.Position = new Vector3(0, 0.75f + carryLevel * 0.03f, 0.28f + carryLevel * 0.03f);
        var sackColors = new[] { new Color(0.55f, 0.45f, 0.3f), new Color(0.5f, 0.4f, 0.28f), new Color(0.35f, 0.38f, 0.42f), new Color(0.25f, 0.28f, 0.33f), new Color(0.18f, 0.2f, 0.25f) };
        ((StandardMaterial3D)_sack.MaterialOverride).AlbedoColor = sackColors[carryLevel];
        Color[] heads = { new(0.6f, 0.62f, 0.66f), new(0.45f, 0.47f, 0.52f), new(1f, 0.8f, 0.3f), new(0.3f, 1f, 0.9f), new(1f, 0.35f, 0.9f) };
        var m = (StandardMaterial3D)_pickHead.MaterialOverride;
        m.AlbedoColor = heads[speedLevel];
        m.EmissionEnabled = speedLevel >= 3;
        m.Emission = heads[speedLevel];
        m.EmissionEnergyMultiplier = speedLevel >= 3 ? 1.8f : 0f;
        _pickHead.Scale = Vector3.One * (1f + speedLevel * 0.08f);
    }

    public void SetHostile(bool hostile)
    {
        Hostile = hostile;
        CollisionLayer = hostile ? PlayerController.GoblinLayer : PlayerController.InteractLayer;
        if (_eyeMat != null)
        {
            var c = hostile ? new Color(1f, 0.15f, 0.1f) : new Color(1f, 0.85f, 0.2f);
            _eyeMat.AlbedoColor = c; _eyeMat.Emission = c;
        }
        if (_label != null)
            _label.Modulate = hostile ? new Color(1f, 0.4f, 0.35f) : new Color(0.6f, 1f, 0.5f);
    }

    // ---------------- interação (amigável) ----------------
    public string GetPrompt() => Hostile || State == Mode.Defeated ? null : Camp?.GrubPrompt();
    public void Interact(PlayerController player) => Camp?.TalkToGrub();

    // ---------------- combate ----------------
    /// <summary>Atingido pelo feixe: perde um "ponto", fica atordoado e é empurrado.</summary>
    public void TakeHit(Vector3 from, int amount)
    {
        if (!Hostile || State == Mode.Defeated) return;
        HitsLeft -= amount;
        var push = GlobalPosition - from; push.Y = 0;
        if (push.LengthSquared() > 0.01f)
        {
            var dest = GlobalPosition + push.Normalized() * 1.8f;
            if (Camp != null && Camp.L.Reachable(dest.X, dest.Z)) GlobalPosition = Camp.L.OnGround(dest.X, dest.Z);
        }
        Effects.Instance?.FloatText(GlobalPosition + Vector3.Up * 1.6f, HitsLeft > 0 ? Loc.T("ATORDOADO") : Loc.T("DERROTADO"), new Color(1f, 0.85f, 0.35f), 1f);
        AudioManager.Instance?.Play("thud", -4f, 1.6f, 0.1f);
        if (HitsLeft <= 0) { State = Mode.Defeated; _t = 0; Camp?.OnRebelDefeated(this); return; }
        _stun = 1.1f;
        State = Mode.Stunned;
    }

    // ---------------- movimento ----------------
    public void GoTo(Vector3 dest)
    {
        _path = Camp?.L.FindPath(GlobalPosition, dest);
        _pathIdx = 0;
    }

    /// <summary>Anda pelo caminho; devolve true ao chegar.</summary>
    bool FollowPath(float speed, float dt)
    {
        if (_path == null || _pathIdx >= _path.Count) return true;
        var target = _path[_pathIdx];
        var pos = GlobalPosition;
        var d = new Vector3(target.X - pos.X, 0, target.Z - pos.Z);
        float dist = d.Length();
        if (dist < 0.3f) { _pathIdx++; return _pathIdx >= _path.Count; }
        var step = d / dist * Mathf.Min(dist, speed * dt);
        var np = pos + step;
        np.Y = Camp.L.Ground(np.X, np.Z);
        GlobalPosition = np;
        Face(d);
        return false;
    }

    void Face(Vector3 dir)
    {
        if (dir.LengthSquared() < 1e-4f) return;
        float yaw = Mathf.Atan2(-dir.X, -dir.Z);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, yaw, 0.25f), 0);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        if (Camp == null) return;
        bool walking = false;
        switch (State)
        {
            case Mode.Sleeping:
                _rig.Rotation = new Vector3(0, 0, Mathf.Lerp(_rig.Rotation.Z, 1.4f, dt * 3f));
                return;
            case Mode.Defeated:
                // foge encolhendo
                _rig.Scale = Vector3.One * Mathf.Max(0.01f, 1f - _t * 0.8f);
                if (_t > 1.3f) Visible = false;
                return;
            case Mode.Stunned:
                _stun -= dt;
                _rig.Rotation = new Vector3(0, _t * 12f, 0);
                if (_stun <= 0) { _rig.Rotation = Vector3.Zero; State = Mode.Chasing; _repath = 0; }
                return;
            case Mode.ToCrystal:
                walking = true;
                if (Target == null || !Target.IsMineable) { State = Mode.Idle; break; }
                if (FollowPath(WalkSpeed, dt)) State = Mode.Mining;
                break;
            case Mode.Mining:
                if (Target == null || !Target.IsMineable) { State = Mode.Idle; break; }
                Face(Target.Center - GlobalPosition);
                _swing += dt;
                _armR.Rotation = new Vector3(-Mathf.Abs(Mathf.Sin(_swing * 3.2f)) * 2.2f, 0, 0);
                if (_swing >= 1.0f)
                {
                    _swing = 0;
                    var gob = this;
                    Target.Hit(Camp.Dps, Target.Center, (GlobalPosition - Target.Center).Normalized(), true,
                        (m, n) => { gob.Carrying += n; gob.Camp.Deliver(m, n); });
                    if (!Target.IsMineable) { Target = null; State = Carrying >= Camp.CarryMax ? Mode.ToChest : Mode.Idle; if (State == Mode.ToChest) GoTo(Camp.ChestPos); }
                }
                break;
            case Mode.ToChest:
                walking = true;
                if (FollowPath(WalkSpeed, dt)) { Carrying = 0; State = Mode.Idle; }
                break;
            case Mode.Chasing:
                walking = true;
                _repath -= dt;
                var pp = Camp.PlayerPos();
                if (_repath <= 0) { _repath = 0.8f; GoTo(pp); }
                FollowPath(ChaseSpeed, dt);
                if (GlobalPosition.DistanceTo(pp) < AttackRange) State = Mode.Attacking;
                break;
            case Mode.Attacking:
                var p2 = Camp.PlayerPos();
                Face(p2 - GlobalPosition);
                _attackCd -= dt;
                _armR.Rotation = new Vector3(-Mathf.Max(0, _attackCd / AttackCooldown) * 2.4f, 0, 0);
                if (GlobalPosition.DistanceTo(p2) > AttackRange + 0.6f) { State = Mode.Chasing; _repath = 0; break; }
                if (_attackCd <= 0) { _attackCd = AttackCooldown; Camp.HitPlayer(this, AttackDamage); }
                break;
            case Mode.Idle:
                _armR.Rotation = Vector3.Zero;
                break;
            case Mode.ToHome:
                walking = true;
                if (FollowPath(WalkSpeed, dt)) State = Mode.Idle;
                break;
            case Mode.Building:
                // montando a cabana: anda entre pontos ao redor do terreno e martela
                if (_path != null && _pathIdx < _path.Count) { walking = true; FollowPath(WalkSpeed, dt); break; }
                Face(Camp.HutPos - GlobalPosition);
                _swing += dt;
                _armR.Rotation = new Vector3(-Mathf.Abs(Mathf.Sin(_swing * 6f)) * 1.8f, 0, 0);
                _buildTimer -= dt;
                if (_buildTimer <= 0)
                {
                    _buildTimer = 2.5f + (float)GD.RandRange(0.0, 1.5);
                    _buildSpot++;
                    GoTo(Camp.BuildSpot(_buildSpot));
                    if ((_buildSpot & 1) == 0) AudioManager.Instance?.Play3D("thud", GlobalPosition, -10f, 1.5f);
                }
                break;
        }
        // balanço da caminhada
        _rig.Position = new Vector3(0, walking ? Mathf.Abs(Mathf.Sin(_t * 9f)) * 0.06f : 0, 0);
        if (walking) _armR.Rotation = new Vector3(Mathf.Sin(_t * 9f) * 0.5f, 0, 0);
        if (State != Mode.Sleeping) _rig.Rotation = new Vector3(0, _rig.Rotation.Y, 0);
    }

    public void Sleep(Vector3 bedPos)
    {
        State = Mode.Sleeping;
        GlobalPosition = bedPos;
        Target = null;
    }

    public void WakeUp()
    {
        _rig.Rotation = Vector3.Zero;
        State = Mode.Idle;
    }
}

/// <summary>Baú do goblin: onde ele guarda o que minerou; o jogador pega para vender.</summary>
public partial class GoblinChest : StationBase
{
    public GoblinDef Def;

    public override void _Ready()
    {
        SetupBody(new Vector3(1.1f, 0.8f, 0.7f));
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.27f, 0.15f), Roughness = 0.9f };
        var band = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.34f), Metallic = 0.5f, Roughness = 0.5f };
        AddMesh(new BoxMesh { Size = new Vector3(1.0f, 0.5f, 0.6f) }, wood, new Vector3(0, 0.25f, 0));
        AddMesh(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 1.0f, RadialSegments = 12, Rings = 1 }, wood, new Vector3(0, 0.5f, 0)).Rotation = new Vector3(0, 0, Mathf.Pi / 2);
        foreach (float x in new[] { -0.35f, 0.35f })
            AddMesh(new BoxMesh { Size = new Vector3(0.06f, 0.82f, 0.64f) }, band, new Vector3(x, 0.4f, 0));
        var glow = Def?.Skin.Lightened(0.3f) ?? new Color(0.5f, 1f, 0.6f);
        AddMesh(new BoxMesh { Size = new Vector3(0.9f, 0.03f, 0.04f) }, MeshFactory.Emissive(glow, glow, 2f), new Vector3(0, 0.5f, 0.3f));
        Label = MakeLabel(Loc.F("BAÚ DO {0}", Def?.Name.ToUpperInvariant() ?? Loc.T("GOBLIN")), glow, 1.3f, 48);
    }

    public override string GetPrompt()
    {
        var gs = GameState.Instance;
        string id = Def.RegionId;
        int n = gs.GoblinStashCount(id);
        if (n == 0) return Loc.F("Baú do {0} (vazio)", Def.Name);
        if (gs.FreeSpace == 0) return Loc.F("Baú do {0}: {1} fragmentos — mochila cheia", Def.Name, n);
        return Loc.F("{0} Pegar {1} fragmentos do baú ({2})", InputHints.Interact, Mathf.Min(n, gs.FreeSpace), GameState.FormatMoney(gs.GoblinStashValue(id)));
    }

    public override void Interact(PlayerController player)
    {
        int n = GameState.Instance.CollectGoblinStash(Def.RegionId);
        if (n > 0) AudioManager.Instance?.Play("pickup", -4f, 0.9f);
    }
}

/// <summary>
/// Goblin minerador de uma região. Antes de ser encontrado ele vive escondido numa câmara secreta
/// (o jogador o acha explorando — sem aviso). Contratado, monta acampamento perto da entrada da
/// região (barraca, fogueira, baú), minera devagar e leva os fragmentos ao baú.
/// Sem pagamento: perdoa, avisa, depois foge (volta ao esconderijo) ou se rebela com comparsas.
/// </summary>
public partial class GoblinCamp : Node3D
{
    /// <summary>Todos os acampamentos (um por região).</summary>
    public static readonly List<GoblinCamp> All = new();
    public static GoblinCamp Instance => All.Count > 0 ? All[0] : null;
    public GoblinDef Def;
    public string RegionId => Def.RegionId;
    public int RegionIndex;
    public CaveLayout L;
    public WorldBuilder World;
    public Vector3 HutPos, ChestPos, BedPos, GrubHome, HideoutPos;
    public GoblinNpc Grub;
    public GoblinChest Chest;
    Node3D _hut, _hutVisual;
    StaticBody3D _hutBody;
    Label3D _hutLabel;
    int _hutShown = -99;
    float _yaw;
    public float Dps => GameState.Instance.GoblinDps(RegionId);
    public int CarryMax => GameState.Instance.GoblinCarryMax(RegionId);
    /// <summary>Pontos ao redor do terreno onde ele trabalha montando a cabana.</summary>
    public Vector3 BuildSpot(int i)
    {
        float a = _yaw + i * 2.1f;
        var p = HutPos + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 2.2f;
        return L.Reachable(p.X, p.Z) ? L.OnGround(p.X, p.Z) : GrubHome;
    }
    public readonly List<GoblinNpc> Rebels = new();
    public Func<Vector3> PlayerPos = () => Vector3.Zero;
    public Action<float, Vector3> DamagePlayer;
    public Func<bool> PlayerInMine = () => true;
    public event Action RebellionWon;
    public event Action<GoblinCamp> Discovered;
    float _think;
    public const float DiscoverDistance = 7f;

    public override void _EnterTree() { if (!All.Contains(this)) All.Add(this); }
    public override void _ExitTree() => All.Remove(this);

    public void Setup(GoblinDef def, CaveLayout layout, int regionIndex, Vector3 hut, float yaw, Vector3 hideout)
    {
        Def = def;
        L = layout;
        RegionIndex = regionIndex;
        HutPos = hut;
        HideoutPos = hideout;
        var right = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
        var fwd = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
        ChestPos = L.OnGround(hut.X + right.X * 2.2f + fwd.X * 1.4f, hut.Z + right.Z * 2.2f + fwd.Z * 1.4f);
        BedPos = L.OnGround(hut.X + fwd.X * 1.5f, hut.Z + fwd.Z * 1.5f, 0.05f);
        GrubHome = L.OnGround(hut.X + fwd.X * 2.4f, hut.Z + fwd.Z * 2.4f);
        _yaw = yaw;
        BuildHut(hut, yaw);
        Chest = new GoblinChest { Name = "GoblinChest", Def = def };
        AddChild(Chest);
        Chest.GlobalPosition = ChestPos;
        Chest.Rotation = new Vector3(0, yaw, 0);
        Grub = new GoblinNpc { Name = def.Name, Camp = this, IsGrub = true, DisplayName = def.Name, Skin = def.Skin };
        AddChild(Grub);
        Grub.GlobalPosition = HideoutPos;
        GameState.Instance.GoblinChanged += SyncState;
        GameState.Instance.DayEnded += SyncState;
        GameState.Instance.DayStarted += SyncState;
        SyncState();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && GameState.Instance != null)
        {
            GameState.Instance.GoblinChanged -= SyncState;
            GameState.Instance.DayEnded -= SyncState;
            GameState.Instance.DayStarted -= SyncState;
        }
    }

    void BuildHut(Vector3 p, float yaw)
    {
        _hut = new Node3D { Name = "GoblinHut" };
        AddChild(_hut);
        _hut.GlobalPosition = p;
        _hut.Rotation = new Vector3(0, yaw, 0);
        _hutBody = new StaticBody3D { CollisionLayer = PlayerController.WorldLayer, CollisionMask = 0, Position = new Vector3(0, 0.8f, 0.4f) };
        _hutBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.2f, 1.6f, 2.2f) } });
        _hut.AddChild(_hutBody);
        _hutLabel = new Label3D
        {
            FontSize = 44, PixelSize = 0.006f, OutlineSize = 10,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 2.5f, 0.4f),
            Modulate = Def.Skin.Lightened(0.4f), OutlineModulate = new Color(0, 0, 0, 0.8f), VisibilityRangeEnd = 30f,
        };
        _hut.AddChild(_hutLabel);
    }

    /// <summary>
    /// Visual da cabana: estágios da montagem (estacas → estrutura → lona) e, pronta, 5 níveis
    /// (barraca → barraca equipada → cabana de madeira → cabana com chaminé → cabana neon).
    /// </summary>
    void RefreshHut()
    {
        var gs = GameState.Instance;
        float prog = gs.GoblinBuildProgress(RegionId);
        int stage = prog < 1f ? (int)(prog * 3f) - 10 : gs.GoblinData(RegionId).HutLevel; // -10..-8 = montando
        if (stage == _hutShown) { if (prog < 1f) _hutLabel.Text = Loc.F("{0} MONTANDO A CABANA · {1:0}%", Def.Name.ToUpperInvariant(), prog * 100f); return; }
        _hutShown = stage;
        _hutVisual?.QueueFree();
        _hutVisual = new Node3D();
        _hut.AddChild(_hutVisual);
        var cloth = new StandardMaterial3D { AlbedoColor = Def.Skin.Darkened(0.35f).Lerp(new Color(0.45f, 0.42f, 0.3f), 0.5f), Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.38f, 0.25f, 0.14f), Roughness = 0.9f };
        var darkWood = new StandardMaterial3D { AlbedoColor = new Color(0.26f, 0.17f, 0.1f), Roughness = 0.9f };
        var metal = MeshFactory.Metal(new Color(0.3f, 0.32f, 0.36f), 0.35f);
        var neon = MeshFactory.Emissive(Def.Skin.Lightened(0.3f), Def.Skin.Lightened(0.2f), 2f);
        void M(Mesh mesh, Material mat, Vector3 pos, Vector3? rot = null, Vector3? scale = null)
            => _hutVisual.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        void Fire(Vector3 at)
        {
            M(MeshFactory.Spike(5, 0.25f, 0.45f), MeshFactory.Emissive(new Color(1f, 0.5f, 0.15f), new Color(1f, 0.45f, 0.1f), 2.5f), at);
            for (int i = 0; i < 6; i++)
            {
                float a = Mathf.Tau * i / 6;
                M(MeshFactory.Rock(40 + i, 0.3f), MeshFactory.RockMaterial(new Color(0.35f, 0.32f, 0.3f)), at + new Vector3(Mathf.Cos(a) * 0.45f, 0, Mathf.Sin(a) * 0.45f), null, Vector3.One * 0.16f);
            }
            _hutVisual.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 1.2f, OmniRange = 6f, Position = at + Vector3.Up * 0.9f });
        }
        void Planks(Vector3 at)
        {
            for (int i = 0; i < 4; i++) M(new BoxMesh { Size = new Vector3(1.4f, 0.06f, 0.18f) }, wood, at + new Vector3(0, 0.04f + i * 0.07f, i * 0.05f), new Vector3(0, i * 0.15f, 0));
        }
        var fireAt = new Vector3(-1.9f, 0.05f, -1.2f);
        bool solid = stage >= 0;
        _hutBody.CollisionLayer = solid ? PlayerController.WorldLayer : 0;
        if (stage < 0)
        {
            // montando: 0 = estacas e corda no chão; 1 = estrutura; 2 = quase pronta
            int st = stage + 10;
            foreach (var (x, z) in new[] { (-1.1f, -0.8f), (1.1f, -0.8f), (-1.1f, 1.6f), (1.1f, 1.6f) })
                M(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.05f, Height = 0.5f, RadialSegments = 6 }, wood, new Vector3(x, 0.25f, z));
            M(new BoxMesh { Size = new Vector3(2.3f, 0.01f, 0.02f) }, cloth, new Vector3(0, 0.45f, -0.8f));
            M(new BoxMesh { Size = new Vector3(2.3f, 0.01f, 0.02f) }, cloth, new Vector3(0, 0.45f, 1.6f));
            Planks(new Vector3(1.9f, 0, 0.6f));
            M(new BoxMesh { Size = new Vector3(0.7f, 0.4f, 0.5f) }, cloth, new Vector3(1.8f, 0.2f, -1.1f), new Vector3(0, 0.4f, 0)); // fardo de lona
            if (st >= 1)
            {
                foreach (float x in new[] { -1.05f, 1.05f })
                    M(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.05f, Height = 1.9f, RadialSegments = 6 }, wood, new Vector3(x, 0.95f, 0.4f), new Vector3(0, 0, x > 0 ? -0.55f : 0.55f));
                M(new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 2.5f, RadialSegments = 6 }, wood, new Vector3(0, 1.62f, 0.4f), new Vector3(Mathf.Pi / 2, 0, 0));
            }
            if (st >= 2) M(new PrismMesh { Size = new Vector3(2.4f, 1.7f, 1.3f) }, cloth, new Vector3(0, 0.85f, 1.0f));
            _hutLabel.Text = Loc.F("{0} MONTANDO A CABANA · {1:0}%", Def.Name.ToUpperInvariant(), prog * 100f);
            return;
        }
        int lv = stage;
        _hutLabel.Text = lv >= 2 ? Loc.F("CABANA DO {0}", Def.Name.ToUpperInvariant()) : Loc.F("BARRACA DO {0}", Def.Name.ToUpperInvariant());
        _hutLabel.Position = new Vector3(0, lv >= 2 ? (lv >= 3 ? 4.3f : 3.2f) : 2.3f, 0.4f);
        if (lv <= 1)
        {
            // barraca: lona com remendos, cumeeira, varas da frente, porta aberta e cordas até as estacas
            M(new PrismMesh { Size = new Vector3(2.4f, 1.7f, 2.4f) }, cloth, new Vector3(0, 0.85f, 0.4f));
            var flap = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.06f, 0.05f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            M(new PrismMesh { Size = new Vector3(0.9f, 1.05f, 0.02f) }, flap, new Vector3(0, 0.52f, -0.81f)); // abertura escura da porta
            M(new PrismMesh { Size = new Vector3(0.5f, 0.6f, 0.02f) }, cloth, new Vector3(-0.42f, 0.32f, -0.86f), new Vector3(0, 0.5f, 0)); // aba dobrada
            M(new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 2.7f, RadialSegments = 6 }, darkWood, new Vector3(0, 1.72f, 0.4f), new Vector3(Mathf.Pi / 2, 0, 0)); // cumeeira
            foreach (float z in new[] { -0.82f, 1.62f })
                M(new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.045f, Height = 1.8f, RadialSegments = 6 }, darkWood, new Vector3(0, 0.88f, z));
            var patch = new StandardMaterial3D { AlbedoColor = cloth.AlbedoColor.Darkened(0.3f).Lerp(new Color(0.6f, 0.45f, 0.3f), 0.3f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            M(new BoxMesh { Size = new Vector3(0.35f, 0.3f, 0.01f) }, patch, new Vector3(0.75f, 0.55f, 0.2f), new Vector3(0, Mathf.Pi / 2, -0.6f));
            M(new BoxMesh { Size = new Vector3(0.25f, 0.22f, 0.01f) }, patch, new Vector3(-0.6f, 0.9f, 0.9f), new Vector3(0, Mathf.Pi / 2, 0.6f));
            var rope = new StandardMaterial3D { AlbedoColor = new Color(0.7f, 0.62f, 0.45f), Roughness = 1f };
            foreach (var (x, z) in new[] { (-1.6f, -1.3f), (1.6f, -1.3f), (-1.6f, 2.1f), (1.6f, 2.1f) })
            {
                var top = new Vector3(0, 1.7f, z < 0 ? -0.82f : 1.62f);
                var peg = new Vector3(x, 0.05f, z);
                var mid = (top + peg) * 0.5f;
                var dir = (top - peg).Normalized();
                var lx = dir.Cross(Vector3.Forward).Normalized();
                _hutVisual.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.008f, BottomRadius = 0.008f, Height = top.DistanceTo(peg), RadialSegments = 4 }, MaterialOverride = rope, Transform = new Transform3D(new Basis(lx, dir, lx.Cross(dir)), mid), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
                M(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.035f, Height = 0.25f, RadialSegments = 5 }, darkWood, peg + new Vector3(0, 0.07f, 0), new Vector3(0.3f, 0, 0));
            }
            Fire(fireAt);
            if (lv == 1)
            {
                // tapete, caixote e lampião
                M(new BoxMesh { Size = new Vector3(1.3f, 0.02f, 0.9f) }, new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.2f, 0.18f) }, new Vector3(0, 0.02f, -1.4f));
                M(new BoxMesh { Size = new Vector3(0.6f, 0.5f, 0.6f) }, wood, new Vector3(1.6f, 0.25f, -0.6f), new Vector3(0, 0.3f, 0));
                M(new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.04f, Height = 1.6f, RadialSegments = 6 }, darkWood, new Vector3(1.5f, 0.8f, 1.6f));
                M(new SphereMesh { Radius = 0.1f, Height = 0.2f }, MeshFactory.Emissive(new Color(1f, 0.8f, 0.4f), new Color(1f, 0.75f, 0.35f), 3f), new Vector3(1.5f, 1.65f, 1.6f));
                _hutVisual.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.4f), LightEnergy = 0.9f, OmniRange = 5f, Position = new Vector3(1.5f, 1.6f, 1.6f) });
            }
            return;
        }
        // cabanas (nível 2+): paredes, telhado, porta, janela acesa
        // paredes: troncos empilhados (madeira) ou chapas com nervuras (neon); quinas com pontas de tronco
        if (lv >= 4)
        {
            M(new BoxMesh { Size = new Vector3(2.6f, 1.8f, 2.4f) }, metal, new Vector3(0, 0.9f, 0.4f));
            for (int k = 0; k < 6; k++)
                M(new BoxMesh { Size = new Vector3(0.05f, 1.8f, 0.05f) }, MeshFactory.Metal(new Color(0.2f, 0.22f, 0.26f), 0.4f), new Vector3(-1.1f + k * 0.44f, 0.9f, -0.82f));
        }
        else
        {
            M(new BoxMesh { Size = new Vector3(2.5f, 1.8f, 2.3f) }, darkWood, new Vector3(0, 0.9f, 0.4f)); // miolo (fecha as frestas)
            for (int k = 0; k < 7; k++)
            {
                float y = 0.13f + k * 0.26f;
                var log = k % 2 == 0 ? wood : new StandardMaterial3D { AlbedoColor = new Color(0.43f, 0.29f, 0.17f), Roughness = 0.9f };
                foreach (float z in new[] { -0.8f, 1.6f })
                    M(new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 2.9f, RadialSegments = 8 }, log, new Vector3(0, y, z), new Vector3(0, 0, Mathf.Pi / 2));
                foreach (float x in new[] { -1.25f, 1.25f })
                    M(new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.13f, Height = 2.75f, RadialSegments = 8 }, log, new Vector3(x, y + 0.13f, 0.4f), new Vector3(Mathf.Pi / 2, 0, 0));
            }
        }
        // telhado de tábuas: duas águas com beiral e cumeeira
        var roofMat = lv >= 4 ? MeshFactory.Metal(new Color(0.18f, 0.2f, 0.24f), 0.4f) : darkWood;
        foreach (float sgn in new[] { -1f, 1f })
        {
            M(new BoxMesh { Size = new Vector3(1.75f, 0.08f, 3.1f) }, roofMat, new Vector3(sgn * 0.72f, 2.25f, 0.4f), new Vector3(0, 0, sgn * -0.55f));
            if (lv < 4)
                for (int k = 0; k < 4; k++) // ripas
                    M(new BoxMesh { Size = new Vector3(0.04f, 0.03f, 3.12f) }, wood, new Vector3(sgn * (0.25f + k * 0.38f), 2.55f - k * 0.23f, 0.4f), new Vector3(0, 0, sgn * -0.55f));
        }
        M(new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 3.2f, RadialSegments = 8 }, wood, new Vector3(0, 2.72f, 0.4f), new Vector3(Mathf.Pi / 2, 0, 0));
        M(new PrismMesh { Size = new Vector3(2.5f, 0.85f, 0.04f) }, darkWood, new Vector3(0, 2.2f, -0.82f)); // empena da frente
        // porta com batente e maçaneta; janela com moldura em cruz; degrau
        M(new BoxMesh { Size = new Vector3(0.84f, 1.42f, 0.06f) }, wood, new Vector3(0.5f, 0.71f, -0.9f));
        M(new BoxMesh { Size = new Vector3(0.7f, 1.3f, 0.06f) }, darkWood, new Vector3(0.5f, 0.65f, -0.93f));
        M(new SphereMesh { Radius = 0.04f, Height = 0.08f }, metal, new Vector3(0.75f, 0.68f, -0.97f));
        M(new BoxMesh { Size = new Vector3(0.62f, 0.52f, 0.06f) }, wood, new Vector3(-0.6f, 1.1f, -0.9f));
        M(new BoxMesh { Size = new Vector3(0.5f, 0.4f, 0.04f) }, MeshFactory.Emissive(new Color(1f, 0.8f, 0.45f), new Color(1f, 0.75f, 0.4f), 2f), new Vector3(-0.6f, 1.1f, -0.92f));
        M(new BoxMesh { Size = new Vector3(0.04f, 0.42f, 0.03f) }, darkWood, new Vector3(-0.6f, 1.1f, -0.95f));
        M(new BoxMesh { Size = new Vector3(0.52f, 0.04f, 0.03f) }, darkWood, new Vector3(-0.6f, 1.1f, -0.95f));
        M(new BoxMesh { Size = new Vector3(1.0f, 0.12f, 0.4f) }, wood, new Vector3(0.5f, 0.06f, -1.12f));
        _hutVisual.AddChild(new OmniLight3D { LightColor = new Color(1f, 0.75f, 0.45f), LightEnergy = 1.0f, OmniRange = 6f, Position = new Vector3(-0.6f, 1.2f, -1.5f) });
        Fire(fireAt);
        if (lv >= 3)
        {
            // chaminé com fumaça, placa e bancada
            M(new BoxMesh { Size = new Vector3(0.4f, 1.2f, 0.4f) }, MeshFactory.RockMaterial(new Color(0.4f, 0.37f, 0.34f)), new Vector3(1.0f, 2.6f, 1.3f));
            var fade = new Gradient();
            fade.SetColor(0, new Color(0.7f, 0.7f, 0.72f, 0.35f));
            fade.SetColor(1, new Color(0.7f, 0.7f, 0.72f, 0f));
            var smoke = new CpuParticles3D
            {
                Amount = 14, Lifetime = 3.5f, Mesh = new SphereMesh { Radius = 0.18f, Height = 0.36f, RadialSegments = 8, Rings = 4 }, Direction = Vector3.Up, Spread = 12,
                Gravity = new Vector3(0.12f, 0.35f, 0), InitialVelocityMin = 0.25f, InitialVelocityMax = 0.5f,
                ScaleAmountMin = 0.6f, ScaleAmountMax = 1.6f, ColorRamp = fade, Position = new Vector3(1.0f, 3.25f, 1.3f),
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true },
            };
            _hutVisual.AddChild(smoke);
            M(new BoxMesh { Size = new Vector3(1.2f, 0.08f, 0.6f) }, wood, new Vector3(1.9f, 0.8f, -0.4f));
            M(new BoxMesh { Size = new Vector3(0.08f, 0.8f, 0.08f) }, wood, new Vector3(1.4f, 0.4f, -0.4f));
            M(new BoxMesh { Size = new Vector3(0.08f, 0.8f, 0.08f) }, wood, new Vector3(2.4f, 0.4f, -0.4f));
            M(new BoxMesh { Size = new Vector3(1.0f, 0.35f, 0.05f) }, darkWood, new Vector3(0, 2.0f, -0.86f));
        }
        if (lv >= 4)
        {
            // neon: contorno do telhado, antena piscando e letreiro
            M(new BoxMesh { Size = new Vector3(3.05f, 0.05f, 0.05f) }, neon, new Vector3(0, 1.82f, -1.0f));
            M(new BoxMesh { Size = new Vector3(3.05f, 0.05f, 0.05f) }, neon, new Vector3(0, 1.82f, 1.8f));
            M(new BoxMesh { Size = new Vector3(0.05f, 0.05f, 2.85f) }, neon, new Vector3(-1.52f, 1.82f, 0.4f));
            M(new BoxMesh { Size = new Vector3(0.05f, 0.05f, 2.85f) }, neon, new Vector3(1.52f, 1.82f, 0.4f));
            M(new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.04f, Height = 1.4f, RadialSegments = 6 }, metal, new Vector3(-0.9f, 3.3f, 1.2f));
            M(new SphereMesh { Radius = 0.08f, Height = 0.16f }, MeshFactory.Emissive(new Color(1f, 0.2f, 0.3f), new Color(1f, 0.2f, 0.3f), 3f), new Vector3(-0.9f, 4.05f, 1.2f));
            _hutVisual.AddChild(new OmniLight3D { LightColor = Def.Skin.Lightened(0.3f), LightEnergy = 1.2f, OmniRange = 7f, Position = new Vector3(0, 2.2f, -1.4f) });
        }
    }

    /// <summary>O acampamento (barraca + baú) só existe depois de contratado.</summary>
    void SetCampVisible(bool on)
    {
        if (_hut.Visible != on) _hut.Visible = on;
        if (on) RefreshHut();
        else _hutBody.CollisionLayer = 0;
        // o baú só aparece com a cabana pronta (ou se ainda tem coisa guardada)
        bool chest = on && (GameState.Instance.GoblinBuilt(RegionId) || GameState.Instance.GoblinStashCount(RegionId) > 0);
        Chest.Visible = chest;
        Chest.CollisionLayer = chest ? PlayerController.WorldLayer | PlayerController.InteractLayer : 0;
    }

    /// <summary>Ajusta presença e comportamento ao estado salvo e ao horário.</summary>
    public void SyncState()
    {
        var gs = GameState.Instance;
        if (Grub == null) return;
        var mood = gs.Goblin(RegionId);
        bool hired = mood == GameState.GoblinMood.Working || mood == GameState.GoblinMood.Rebel;
        SetCampVisible(hired || gs.GoblinStashCount(RegionId) > 0);
        bool inHideout = mood == GameState.GoblinMood.Available || (mood == GameState.GoblinMood.Fled && gs.CanRehireGoblin(RegionId));
        Grub.Visible = hired || inHideout;
        Grub.CollisionLayer = Grub.Visible ? (Grub.Hostile ? PlayerController.GoblinLayer : PlayerController.InteractLayer) : 0;
        if (mood == GameState.GoblinMood.Rebel) return; // a rebelião começa quando o jogador entra na região
        if (Grub.Hostile) Grub.SetHostile(false);
        ClearRebels(false);
        if (inHideout)
        {
            // escondido na câmara secreta, "minerando" a parede
            Grub.WakeUp();
            Grub.Target = null;
            Grub.State = GoblinNpc.Mode.Idle;
            Grub.GlobalPosition = HideoutPos;
            return;
        }
        if (!hired) return;
        Grub.SetGear(gs.GoblinData(RegionId).CarryLevel, gs.GoblinData(RegionId).SpeedLevel);
        if (gs.DayOver) Grub.Sleep(BedPos);
        else if (Grub.State == GoblinNpc.Mode.Sleeping || Grub.GlobalPosition.DistanceTo(HideoutPos) < 1f) { Grub.WakeUp(); Grub.GlobalPosition = GrubHome; }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Grub == null) return;
        var gs = GameState.Instance;
        _think -= (float)delta;
        if (_think > 0) return;
        _think = 0.5f;
        var mood = gs.Goblin(RegionId);
        // descoberta: só quem explora até a câmara secreta encontra o goblin
        if (!gs.GoblinFound(RegionId) && Grub.Visible && PlayerInMine() && PlayerPos().DistanceTo(Grub.GlobalPosition) < DiscoverDistance)
        {
            if (gs.MarkGoblinFound(RegionId))
            {
                gs.Notify(Loc.F("Você encontrou {0}, {1}!", Def.Name, Def.Title), Loc.F("Ele estava escondido aqui. Fale com ele para contratá-lo ({0}).", GameState.FormatMoney(gs.GoblinHirePrice(RegionId))), NotifyKind.Rare);
                AudioManager.Instance?.Play("rare", -2f);
                Discovered?.Invoke(this);
            }
        }
        if (mood == GameState.GoblinMood.Rebel)
        {
            if (gs.DayOver) { if (Rebels.Count > 0) EndRebellion(false); return; }
            if (Rebels.Count == 0 && PlayerInMine() && WorldBuilder.RegionAt(PlayerPos()).Index == RegionIndex) StartRebellion();
            return;
        }
        if (mood != GameState.GoblinMood.Working || gs.DayOver || !gs.IsPlaying) return;
        // cabana: estágios da montagem e nível
        bool built = gs.GoblinBuilt(RegionId);
        if (_hut.Visible) SetCampVisible(true);
        if (!built)
        {
            if (Grub.State != GoblinNpc.Mode.Building) { Grub.Target = null; Grub.State = GoblinNpc.Mode.Building; }
            return;
        }
        if (Grub.State == GoblinNpc.Mode.Building) Grub.State = GoblinNpc.Mode.Idle;
        // fora do horário de trabalho (a cabana define quanto do dia ele trabalha): volta e descansa
        if (!gs.GoblinOnShift(RegionId))
        {
            if (Grub.State is GoblinNpc.Mode.ToCrystal or GoblinNpc.Mode.Mining)
            {
                Grub.Target = null;
                if (Grub.Carrying > 0) { Grub.State = GoblinNpc.Mode.ToChest; Grub.GoTo(ChestPos); }
                else { Grub.State = GoblinNpc.Mode.ToHome; Grub.GoTo(GrubHome); }
            }
            else if (Grub.State == GoblinNpc.Mode.Idle && Grub.GlobalPosition.DistanceTo(GrubHome) > 1.5f) { Grub.State = GoblinNpc.Mode.ToHome; Grub.GoTo(GrubHome); }
            return;
        }
        if (Grub.State == GoblinNpc.Mode.Idle)
        {
            if (Grub.Carrying >= CarryMax) { Grub.State = GoblinNpc.Mode.ToChest; Grub.GoTo(ChestPos); return; }
            var target = PickCrystal(Grub.GlobalPosition);
            if (target == null) return;
            Grub.Target = target;
            Grub.GoTo(target.StandPoint);
            Grub.State = GoblinNpc.Mode.ToCrystal;
        }
    }

    /// <summary>Depósito de chão mais próximo, na região dele, perto do acampamento.</summary>
    Crystal PickCrystal(Vector3 from)
    {
        Crystal best = null; float bd = float.MaxValue;
        foreach (var c in World.Crystals)
        {
            if (!c.IsMineable || c.Material.Special || WorldBuilder.RegionAt(c.GlobalPosition).Index != RegionIndex) continue;
            if (c.GlobalTransform.Basis.Y.Normalized().Y < 0.9f) continue; // goblin não alcança parede
            if (c.StandPoint.DistanceTo(HutPos) > 55f) continue;
            float d = c.StandPoint.DistanceSquaredTo(from);
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    public void Deliver(MaterialDef m, int n) => GameState.Instance.GoblinMined(RegionId, m, n);

    // ---------------- conversa / contratação ----------------
    public string GrubPrompt()
    {
        var gs = GameState.Instance;
        string i = InputHints.Interact;
        string price = GameState.FormatMoney(gs.GoblinHirePrice(RegionId));
        return gs.Goblin(RegionId) switch
        {
            GameState.GoblinMood.Available => Loc.F("{0} Contratar {1}, {2} ({3})", i, Def.Name, Def.Title, price),
            GameState.GoblinMood.Fled when gs.CanRehireGoblin(RegionId) => Loc.F("{0} {1} voltou ao esconderijo, desconfiado. Recontratar ({2})", i, Def.Name, price),
            GameState.GoblinMood.Working => gs.DayOver ? Loc.F("{0} está dormindo (zzz)", Def.Name)
                : !gs.GoblinBuilt(RegionId) ? Loc.F("{0} Falar com {1} (montando a cabana: {2:0}%)", i, Def.Name, gs.GoblinBuildProgress(RegionId) * 100f)
                : Loc.F("{0} Falar com {1} (hoje: {2} minerados)", i, Def.Name, GameState.FormatMoney(gs.GoblinData(RegionId).DayValue)),
            _ => null,
        };
    }

    public void TalkToGrub()
    {
        var gs = GameState.Instance;
        gs.MarkGoblinFound(RegionId);
        if (gs.Goblin(RegionId) == GameState.GoblinMood.Available || gs.CanRehireGoblin(RegionId))
        {
            if (!gs.TryHireGoblin(RegionId)) gs.Notify(Def.Name, Loc.F("\"Sem {0}, sem picareta!\"", GameState.FormatMoney(gs.GoblinHirePrice(RegionId))), NotifyKind.Warning);
            else { SyncState(); AudioManager.Instance?.Play("buy", -4f); }
            return;
        }
        if (gs.Goblin(RegionId) == GameState.GoblinMood.Working)
        {
            // conversa: painel com a cabana, o horário e as melhorias
            UiBus.RequestGoblin(this);
        }
    }

    // ---------------- rebelião ----------------
    public void StartRebellion()
    {
        var gs = GameState.Instance;
        ClearRebels(false);
        SetCampVisible(true);
        Grub.WakeUp();
        Grub.Visible = true;
        Grub.SetHostile(true);
        Grub.HitsLeft = 6;
        Grub.GlobalPosition = GrubHome;
        Rebels.Add(Grub);
        for (int i = 0; i < Def.Comrades.Length; i++)
        {
            var g = new GoblinNpc { Name = "Rebel_" + Def.Comrades[i], Camp = this, DisplayName = Def.Comrades[i], HitsLeft = 4, Skin = Def.Skin.Darkened(0.15f) };
            AddChild(g);
            // espalha os comparsas ao redor, em pontos alcançáveis
            var p = GrubHome;
            for (int k = 0; k < 12; k++)
            {
                float ang = (i == 0 ? 0.6f : -0.6f) + k * 0.5f * (i == 0 ? 1 : -1);
                var q = GrubHome + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * 2.6f;
                if (L.Reachable(q.X, q.Z) && L.WallDist(q.X, q.Z) < -0.8f) { p = q; break; }
            }
            g.GlobalPosition = L.OnGround(p.X, p.Z);
            g.SetHostile(true);
            Rebels.Add(g);
        }
        foreach (var g in Rebels) g.State = GoblinNpc.Mode.Chasing;
        gs.Notify("REBELIÃO GOBLIN!", Loc.F("{0} e os comparsas querem o pagamento à força. Use o feixe para atordoá-los!", Def.Name), NotifyKind.Warning);
        AudioManager.Instance?.Play("error", 0f);
    }

    public void OnRebelDefeated(GoblinNpc g)
    {
        if (Rebels.All(r => r.State == GoblinNpc.Mode.Defeated)) EndRebellion(true);
    }

    public void EndRebellion(bool playerWon)
    {
        var gs = GameState.Instance;
        ClearRebels(true);
        gs.EndGoblinRebellion(RegionId, playerWon);
        Grub.SetHostile(false);
        if (playerWon)
        {
            Grub.Visible = true;
            Grub.State = GoblinNpc.Mode.Idle;
            Grub.HitsLeft = 0;
            Grub.GlobalPosition = GrubHome;
            Grub.Scale = Vector3.One;
            gs.Notify("Rebelião contida!", Loc.F("Os comparsas fugiram. {0}, envergonhado, volta ao trabalho — e promete não brigar mais.", Def.Name), NotifyKind.Success);
            RebellionWon?.Invoke();
        }
        else gs.Notify("Os goblins fugiram", Loc.F("Levaram o que havia no baú. {0} talvez volte ao esconderijo em alguns dias.", Def.Name), NotifyKind.Warning);
        SyncState();
    }

    void ClearRebels(bool includeGrubReset)
    {
        foreach (var r in Rebels)
            if (r != Grub) r.QueueFree();
        Rebels.Clear();
        if (includeGrubReset && Grub != null)
        {
            var rig = Grub.GetNodeOrNull<Node3D>("Rig");
            if (rig != null) rig.Scale = Vector3.One;
        }
    }

    /// <summary>Pulso de ressonância atinge goblins rebelados no raio (2 pontos cada), em todos os acampamentos.</summary>
    public static void PulseHitAll(Vector3 center, float radius, Vector3 from)
    {
        foreach (var camp in All)
            foreach (var g in camp.Rebels.ToList())
                if (g.Hostile && g.State != GoblinNpc.Mode.Defeated && g.GlobalPosition.DistanceTo(center) <= radius + 1f)
                    g.TakeHit(from, 2);
    }

    public static bool AnyRebellion => All.Any(c => c.Rebels.Count > 0);

    public void HitPlayer(GoblinNpc g, float dmg) => DamagePlayer?.Invoke(dmg, g.GlobalPosition);
}
