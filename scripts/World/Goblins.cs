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
    public enum Mode { Idle, ToCrystal, Mining, ToChest, Sleeping, Chasing, Attacking, Stunned, Defeated }
    public Mode State = Mode.Idle;
    public GoblinCamp Camp;
    public bool IsGrub;
    public bool Hostile { get; private set; }
    public int HitsLeft;
    public string DisplayName = "Grub";
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
        _skinMat = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.66f, 0.3f), Roughness = 0.8f };
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
        M(new BoxMesh { Size = new Vector3(0.36f, 0.05f, 0.06f) }, new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.62f, 0.66f), Metallic = 0.6f, Roughness = 0.4f }, new Vector3(0, 0, -0.52f), _pick);
        M(new CapsuleMesh { Radius = 0.07f, Height = 0.4f }, _skinMat, new Vector3(-0.32f, 0.63f, 0));
        // saco de minério nas costas
        M(new SphereMesh { Radius = 0.18f, Height = 0.3f, RadialSegments = 8, Rings = 4 }, new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.45f, 0.3f) }, new Vector3(0, 0.75f, 0.28f));
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
        Effects.Instance?.FloatText(GlobalPosition + Vector3.Up * 1.6f, HitsLeft > 0 ? "ATORDOADO" : "DERROTADO", new Color(1f, 0.85f, 0.35f), 1f);
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
                    Target.Hit(MineDps, Target.Center, (GlobalPosition - Target.Center).Normalized(), true,
                        (m, n) => { gob.Carrying += n; gob.Camp.Deliver(m, n); });
                    if (!Target.IsMineable) { Target = null; State = Carrying >= CarryMax ? Mode.ToChest : Mode.Idle; if (State == Mode.ToChest) GoTo(Camp.ChestPos); }
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

/// <summary>Baú do Grub: onde ele guarda o que minerou; o jogador pega para vender.</summary>
public partial class GoblinChest : StationBase
{
    public override void _Ready()
    {
        SetupBody(new Vector3(1.1f, 0.8f, 0.7f));
        var wood = new StandardMaterial3D { AlbedoColor = new Color(0.42f, 0.27f, 0.15f), Roughness = 0.9f };
        var band = new StandardMaterial3D { AlbedoColor = new Color(0.3f, 0.3f, 0.34f), Metallic = 0.5f, Roughness = 0.5f };
        AddMesh(new BoxMesh { Size = new Vector3(1.0f, 0.5f, 0.6f) }, wood, new Vector3(0, 0.25f, 0));
        AddMesh(new CylinderMesh { TopRadius = 0.3f, BottomRadius = 0.3f, Height = 1.0f, RadialSegments = 12, Rings = 1 }, wood, new Vector3(0, 0.5f, 0)).Rotation = new Vector3(0, 0, Mathf.Pi / 2);
        foreach (float x in new[] { -0.35f, 0.35f })
            AddMesh(new BoxMesh { Size = new Vector3(0.06f, 0.82f, 0.64f) }, band, new Vector3(x, 0.4f, 0));
        var glow = new Color(0.5f, 1f, 0.6f);
        AddMesh(new BoxMesh { Size = new Vector3(0.9f, 0.03f, 0.04f) }, MeshFactory.Emissive(glow, glow, 2f), new Vector3(0, 0.5f, 0.3f));
        Label = MakeLabel("BAÚ DO GRUB", new Color(0.6f, 1f, 0.5f), 1.3f, 48);
    }

    public override string GetPrompt()
    {
        var gs = GameState.Instance;
        int n = gs.GoblinStashCount;
        if (n == 0) return "Baú do Grub (vazio)";
        if (gs.FreeSpace == 0) return $"Baú do Grub: {n} fragmentos — mochila cheia";
        return $"{InputHints.Interact} Pegar {Mathf.Min(n, gs.FreeSpace)} fragmentos do baú ({GameState.FormatMoney(gs.GoblinStashValue)})";
    }

    public override void Interact(PlayerController player)
    {
        int n = GameState.Instance.CollectGoblinStash();
        if (n > 0) AudioManager.Instance?.Play("pickup", -4f, 0.9f);
    }
}

/// <summary>
/// Acampamento do goblin na caverna inicial: barraca, cama, baú e o próprio Grub.
/// Controla o trabalho (escolhe depósitos, leva ao baú), o sono à noite e a rebelião
/// (Grub + comparsas perseguem e atacam o jogador; o feixe os atordoa).
/// </summary>
public partial class GoblinCamp : Node3D
{
    public static GoblinCamp Instance { get; private set; }
    public CaveLayout L;
    public WorldBuilder World;
    public Vector3 HutPos, ChestPos, BedPos, GrubHome;
    public GoblinNpc Grub;
    public GoblinChest Chest;
    public readonly List<GoblinNpc> Rebels = new();
    public Func<Vector3> PlayerPos = () => Vector3.Zero;
    public Action<float, Vector3> DamagePlayer;
    public Func<bool> PlayerInMine = () => true;
    public event Action RebellionWon;
    float _think;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    public void Setup(CaveLayout layout, Vector3 hut, float yaw)
    {
        L = layout;
        HutPos = hut;
        var right = new Vector3(Mathf.Cos(yaw), 0, -Mathf.Sin(yaw));
        var fwd = new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));
        ChestPos = L.OnGround(hut.X + right.X * 2.2f + fwd.X * 1.4f, hut.Z + right.Z * 2.2f + fwd.Z * 1.4f);
        BedPos = L.OnGround(hut.X + fwd.X * 1.5f, hut.Z + fwd.Z * 1.5f, 0.05f);
        BuildHut(hut, yaw);
        Chest = new GoblinChest { Name = "GoblinChest" };
        AddChild(Chest);
        Chest.GlobalPosition = ChestPos;
        Chest.Rotation = new Vector3(0, yaw, 0);
        // a frente do baú (+Z local) fica para o lado da barraca; o jogador interage pela frente
        Grub = new GoblinNpc { Name = "Grub", Camp = this, IsGrub = true, DisplayName = "Grub" };
        AddChild(Grub);
        GrubHome = L.OnGround(hut.X + fwd.X * 2.4f, hut.Z + fwd.Z * 2.4f);
        Grub.GlobalPosition = GrubHome;
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
        var node = new Node3D { Name = "GoblinHut" };
        AddChild(node);
        node.GlobalPosition = p;
        node.Rotation = new Vector3(0, yaw, 0);
        var cloth = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.5f, 0.28f), Roughness = 0.95f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        var pole = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.2f, 0.12f) };
        // barraca em "A" de lona remendada
        var tent = new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(2.4f, 1.7f, 2.4f) }, MaterialOverride = cloth, Position = new Vector3(0, 0.85f, 0.4f) };
        node.AddChild(tent);
        node.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.5f, 0.02f) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.35f, 0.2f) }, Position = new Vector3(0.4f, 0.7f, -0.82f) });
        foreach (float x in new[] { -1.1f, 1.1f })
            node.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 1.9f, RadialSegments = 6, Rings = 1 }, MaterialOverride = pole, Position = new Vector3(x * 0.05f, 0.95f, x > 0 ? 1.55f : -0.75f) });
        // fogueira com brasas
        var fire = MeshFactory.Emissive(new Color(1f, 0.5f, 0.15f), new Color(1f, 0.45f, 0.1f), 2.5f);
        node.AddChild(new MeshInstance3D { Mesh = MeshFactory.Spike(5, 0.25f, 0.45f), MaterialOverride = fire, Position = new Vector3(-1.9f, 0.05f, -1.2f) });
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Tau * i / 6;
            node.AddChild(new MeshInstance3D { Mesh = MeshFactory.Rock(40 + i, 0.3f), MaterialOverride = MeshFactory.RockMaterial(new Color(0.35f, 0.32f, 0.3f)), Position = new Vector3(-1.9f + Mathf.Cos(a) * 0.45f, 0.05f, -1.2f + Mathf.Sin(a) * 0.45f), Scale = Vector3.One * 0.16f });
        }
        var light = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 1.2f, OmniRange = 6f, Position = new Vector3(-1.9f, 0.9f, -1.2f) };
        node.AddChild(light);
        var col = new StaticBody3D { CollisionLayer = PlayerController.WorldLayer, CollisionMask = 0, Position = new Vector3(0, 0.8f, 0.4f) };
        col.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.2f, 1.6f, 2.2f) } });
        node.AddChild(col);
        node.AddChild(new Label3D
        {
            Text = "BARRACA DO GRUB", FontSize = 44, PixelSize = 0.006f, OutlineSize = 10,
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 2.3f, 0.4f),
            Modulate = new Color(0.6f, 1f, 0.5f), OutlineModulate = new Color(0, 0, 0, 0.8f), VisibilityRangeEnd = 30f,
        });
    }

    /// <summary>Ajusta presença e comportamento do Grub ao estado salvo e ao horário.</summary>
    public void SyncState()
    {
        var gs = GameState.Instance;
        if (Grub == null) return;
        var mood = gs.Goblin;
        Grub.Visible = mood != GameState.GoblinMood.Fled || gs.CanRehireGoblin;
        Grub.CollisionLayer = Grub.Visible ? (Grub.Hostile ? PlayerController.GoblinLayer : PlayerController.InteractLayer) : 0;
        if (mood == GameState.GoblinMood.Rebel) return; // a rebelião começa quando o jogador aparece na mina
        if (Grub.Hostile) Grub.SetHostile(false);
        ClearRebels(false);
        if (gs.DayOver || mood != GameState.GoblinMood.Working)
        {
            if (mood == GameState.GoblinMood.Working || mood == GameState.GoblinMood.Available) Grub.Sleep(BedPos);
            if (mood == GameState.GoblinMood.Available && !gs.DayOver) { Grub.WakeUp(); Grub.GlobalPosition = GrubHome; }
        }
        else if (Grub.State == GoblinNpc.Mode.Sleeping) { Grub.WakeUp(); Grub.GlobalPosition = GrubHome; }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Grub == null) return;
        var gs = GameState.Instance;
        _think -= (float)delta;
        if (_think > 0) return;
        _think = 0.5f;
        if (gs.Goblin == GameState.GoblinMood.Rebel)
        {
            if (gs.DayOver) { if (Rebels.Count > 0) EndRebellion(false); return; }
            if (Rebels.Count == 0 && PlayerInMine() && WorldBuilder.RegionAt(PlayerPos()).Index == 0) StartRebellion();
            return;
        }
        if (gs.Goblin != GameState.GoblinMood.Working || gs.DayOver || !gs.IsPlaying) return;
        if (Grub.State == GoblinNpc.Mode.Idle)
        {
            if (Grub.Carrying >= GoblinNpc.CarryMax) { Grub.State = GoblinNpc.Mode.ToChest; Grub.GoTo(ChestPos); return; }
            var target = PickCrystal(Grub.GlobalPosition);
            if (target == null) return;
            Grub.Target = target;
            Grub.GoTo(target.StandPoint);
            Grub.State = GoblinNpc.Mode.ToCrystal;
        }
    }

    /// <summary>Depósito no chão mais próximo da barraca, na caverna inicial, que ninguém reservou.</summary>
    Crystal PickCrystal(Vector3 from)
    {
        Crystal best = null; float bd = float.MaxValue;
        foreach (var c in World.Crystals)
        {
            if (!c.IsMineable || c.Material.Special || WorldBuilder.RegionAt(c.GlobalPosition).Index != 0) continue;
            if (c.GlobalTransform.Basis.Y.Normalized().Y < 0.9f) continue; // goblin não alcança parede
            if (c.StandPoint.DistanceTo(HutPos) > 45f) continue;
            float d = c.StandPoint.DistanceSquaredTo(from) + c.StandPoint.DistanceSquaredTo(PlayerPos()) * 0.0f;
            if (d < bd) { bd = d; best = c; }
        }
        return best;
    }

    public void Deliver(MaterialDef m, int n) => GameState.Instance.GoblinMined(m, n);

    // ---------------- conversa / contratação ----------------
    public string GrubPrompt()
    {
        var gs = GameState.Instance;
        string i = InputHints.Interact;
        return gs.Goblin switch
        {
            GameState.GoblinMood.Available => $"{i} Contratar Grub, o goblin minerador ({GameState.FormatMoney(gs.GoblinHirePrice)})",
            GameState.GoblinMood.Fled when gs.CanRehireGoblin => $"{i} Grub voltou, desconfiado. Recontratar ({GameState.FormatMoney(gs.GoblinHirePrice)})",
            GameState.GoblinMood.Working => gs.DayOver ? "Grub está dormindo (zzz)" : $"{i} Falar com Grub (hoje: {GameState.FormatMoney(gs.Data.GoblinDayValue)} minerados)",
            _ => null,
        };
    }

    public void TalkToGrub()
    {
        var gs = GameState.Instance;
        if (gs.Goblin == GameState.GoblinMood.Available || gs.CanRehireGoblin)
        {
            if (!gs.TryHireGoblin()) gs.Notify("Grub", $"\"Sem {GameState.FormatMoney(gs.GoblinHirePrice)}, sem picareta!\"", NotifyKind.Warning);
            else { SyncState(); AudioManager.Instance?.Play("buy", -4f); }
            return;
        }
        if (gs.Goblin == GameState.GoblinMood.Working)
        {
            string mood = gs.Data.GoblinStrikes switch
            {
                0 => "\"Pedra boa hoje, chefe! Grub trabalha, chefe paga.\"",
                1 => "\"Chefe esqueceu de pagar Grub ontem... Grub lembra.\"",
                _ => "\"Grub está de olho, chefe. Mais um dia sem moeda e Grub... hmm.\"",
            };
            gs.Notify("Grub", mood + $" Baú: {gs.GoblinStashCount} fragmentos.", NotifyKind.Info);
        }
    }

    // ---------------- rebelião ----------------
    public void StartRebellion()
    {
        var gs = GameState.Instance;
        ClearRebels(false);
        Grub.WakeUp();
        Grub.Visible = true;
        Grub.SetHostile(true);
        Grub.HitsLeft = 6;
        Rebels.Add(Grub);
        string[] names = { "Snik", "Brugo" };
        for (int i = 0; i < 2; i++)
        {
            var g = new GoblinNpc { Name = "Rebel_" + names[i], Camp = this, DisplayName = names[i], HitsLeft = 4 };
            AddChild(g);
            // espalha os comparsas ao redor do Grub, em pontos alcançáveis
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
        foreach (var g in Rebels) { g.State = GoblinNpc.Mode.Chasing; }
        Grub.GlobalPosition = GrubHome;
        gs.Notify("REBELIÃO GOBLIN!", "Grub e seus comparsas querem o pagamento à força. Use o feixe para atordoá-los!", NotifyKind.Warning);
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
        gs.EndGoblinRebellion(playerWon);
        if (playerWon)
        {
            Grub.SetHostile(false);
            Grub.Visible = true;
            Grub.State = GoblinNpc.Mode.Idle;
            Grub.HitsLeft = 0;
            Grub.GlobalPosition = GrubHome;
            Grub.Scale = Vector3.One;
            gs.Notify("Rebelião contida!", "Os comparsas fugiram. Grub, envergonhado, volta ao trabalho — e promete não brigar mais.", NotifyKind.Success);
            RebellionWon?.Invoke();
        }
        else
        {
            Grub.SetHostile(false);
            gs.Notify("Os goblins fugiram", "Levaram o que havia no baú. Grub talvez volte em alguns dias.", NotifyKind.Warning);
        }
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

    /// <summary>Pulso de ressonância atinge goblins rebelados no raio (2 pontos cada).</summary>
    public void PulseHit(Vector3 center, float radius, Vector3 from)
    {
        foreach (var g in Rebels.ToList())
            if (g.Hostile && g.State != GoblinNpc.Mode.Defeated && g.GlobalPosition.DistanceTo(center) <= radius + 1f)
                g.TakeHit(from, 2);
    }

    public void HitPlayer(GoblinNpc g, float dmg) => DamagePlayer?.Invoke(dmg, g.GlobalPosition);
}
