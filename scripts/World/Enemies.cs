using System;
using System.Collections.Generic;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.Mining;
using CrystalExcavator.Player;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Inimigo das Profundezas Proibidas: anda pela caverna (física de verdade), persegue o jogador quando o vê,
/// avisa antes de golpear (brilho + pose) e larga fragmentos ao morrer. Volta para casa se for longe demais e
/// renasce depois de um tempo, quando o jogador não está olhando.
/// </summary>
public partial class Enemy : CharacterBody3D
{
    public const uint EnemyLayer = 32;
    public enum State { Idle, Chase, Windup, Recover, Hurt, Dead }

    public EnemyDef Def;
    public Vector3 Home;
    public float Hp { get; private set; }
    public State Mode { get; private set; } = State.Idle;
    public bool Alive => Mode != State.Dead;
    public EnemyManager Manager;

    Node3D _body, _armL, _armR, _head;
    readonly List<Node3D> _legs = new();
    StandardMaterial3D _glowMat, _skinMat;
    MeshInstance3D _ring;
    Node3D _bar;
    MeshInstance3D _barFill;
    float _t, _timer, _flash, _respawn, _wanderT, _barShow;
    Vector3 _wander, _knock;
    readonly Random _rng;
    static readonly Color FlashColor = new(1f, 1f, 1f);

    public Enemy(EnemyDef def, Vector3 home, int seed)
    {
        Def = def; Home = home; _rng = new Random(seed);
        Hp = def.Hp;
        CollisionLayer = EnemyLayer;
        CollisionMask = PlayerController.WorldLayer;
        FloorMaxAngle = Mathf.DegToRad(50);
        var shape = def.Kind == EnemyKind.Crawler ? (Shape3D)new CapsuleShape3D { Radius = 0.45f, Height = 0.9f } : new CapsuleShape3D { Radius = 0.6f * def.Scale, Height = 2.2f * def.Scale };
        AddChild(new CollisionShape3D { Shape = shape, Position = new Vector3(0, def.Kind == EnemyKind.Crawler ? 0.45f : 1.1f * def.Scale, 0), Rotation = def.Kind == EnemyKind.Crawler ? new Vector3(0, 0, Mathf.Pi / 2) : Vector3.Zero });
        BuildBody();
    }
    public Enemy() : this(EnemyData.All[0], Vector3.Zero, 1) { }

    public override void _Ready() { GlobalPosition = Home; }

    // ------------------------------------------------------------------ aparência
    MeshInstance3D Part(Node3D p, Mesh m, Material mat, Vector3 pos, Vector3? rot = null, Vector3? scale = null)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One };
        p.AddChild(mi);
        return mi;
    }

    void BuildBody()
    {
        var glow = GameData.Mat(Def.Drop)?.Glow ?? new Color(0.5f, 1f, 0.9f);
        _skinMat = new StandardMaterial3D { AlbedoColor = Def.Kind == EnemyKind.Crawler ? new Color(0.16f, 0.14f, 0.2f) : new Color(0.3f, 0.3f, 0.34f), Roughness = 0.7f, EmissionEnabled = true, Emission = FlashColor, EmissionEnergyMultiplier = 0f };
        _glowMat = new StandardMaterial3D { AlbedoColor = glow, EmissionEnabled = true, Emission = glow, EmissionEnergyMultiplier = 0.8f };
        var eyeMat = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.2f, 0.25f), EmissionEnabled = true, Emission = new Color(1f, 0.15f, 0.2f), EmissionEnergyMultiplier = 3.5f };
        _body = new Node3D { Scale = Vector3.One * Def.Scale };
        AddChild(_body);
        if (Def.Kind == EnemyKind.Crawler)
        {
            // corpo baixo e achatado, seis patas, espinhos de cristal nas costas
            Part(_body, new SphereMesh { Radius = 0.45f, Height = 0.6f, RadialSegments = 14, Rings = 7 }, _skinMat, new Vector3(0, 0.5f, -0.1f), null, new Vector3(1.0f, 0.75f, 1.35f));
            _head = new Node3D { Position = new Vector3(0, 0.52f, 0.5f) };
            _body.AddChild(_head);
            Part(_head, new SphereMesh { Radius = 0.26f, Height = 0.44f, RadialSegments = 12, Rings = 6 }, _skinMat, Vector3.Zero, null, new Vector3(1.1f, 0.85f, 1f));
            foreach (int s in new[] { -1, 1 })
            {
                Part(_head, new SphereMesh { Radius = 0.05f, Height = 0.1f }, eyeMat, new Vector3(s * 0.1f, 0.06f, 0.22f));
                Part(_head, new PrismMesh { Size = new Vector3(0.06f, 0.24f, 0.06f) }, _glowMat, new Vector3(s * 0.12f, -0.1f, 0.25f), new Vector3(Mathf.Pi * 0.6f, 0, s * 0.3f));
            }
            for (int i = 0; i < 5; i++)
                Part(_body, MeshFactory.CrystalPrism(5, 1, 1, 0.25f), _glowMat, new Vector3(((i % 2) * 2 - 1) * 0.12f, 0.75f, -0.45f + i * 0.17f), new Vector3(-0.35f + i * 0.08f, 0, ((i % 2) * 2 - 1) * 0.35f), new Vector3(0.1f, 0.38f + (i % 3) * 0.1f, 0.1f));
            for (int i = 0; i < 6; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                var leg = new Node3D { Position = new Vector3(side * 0.35f, 0.5f, -0.35f + (i / 2) * 0.33f) };
                _body.AddChild(leg);
                Part(leg, new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.025f, Height = 0.6f, RadialSegments = 6 }, _skinMat, new Vector3(side * 0.22f, -0.12f, 0), new Vector3(0, 0, side * 1.05f));
                Part(leg, new CylinderMesh { TopRadius = 0.025f, BottomRadius = 0.01f, Height = 0.45f, RadialSegments = 6 }, _glowMat, new Vector3(side * 0.47f, -0.3f, 0), new Vector3(0, 0, -side * 0.35f));
                _legs.Add(leg);
            }
        }
        else
        {
            // golem de pedra com núcleo de Lúmen no peito, braços enormes
            var stone = MeshFactory.RockMaterial(new Color(0.38f, 0.38f, 0.42f));
            Part(_body, MeshFactory.Rock(91, 0.3f), stone, new Vector3(0, 1.35f, 0), null, new Vector3(0.75f, 0.9f, 0.55f));
            Part(_body, new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 12, Rings = 6 }, _glowMat, new Vector3(0, 1.45f, 0.34f));
            _head = new Node3D { Position = new Vector3(0, 2.15f, 0.05f) };
            _body.AddChild(_head);
            Part(_head, MeshFactory.Rock(92, 0.25f), stone, Vector3.Zero, null, new Vector3(0.34f, 0.3f, 0.32f));
            foreach (int s in new[] { -1, 1 })
                Part(_head, new BoxMesh { Size = new Vector3(0.1f, 0.05f, 0.05f) }, eyeMat, new Vector3(s * 0.1f, 0.02f, 0.27f));
            Part(_head, MeshFactory.CrystalPrism(6, 1, 1, 0.3f), _glowMat, new Vector3(0, 0.25f, -0.05f), null, new Vector3(0.12f, 0.5f, 0.12f));
            foreach (int s in new[] { -1, 1 })
            {
                var arm = new Node3D { Position = new Vector3(s * 0.75f, 1.85f, 0) };
                _body.AddChild(arm);
                Part(arm, MeshFactory.Rock(93 + s, 0.3f), stone, new Vector3(0, -0.45f, 0), null, new Vector3(0.28f, 0.5f, 0.28f));
                Part(arm, MeshFactory.Rock(95 + s, 0.3f), stone, new Vector3(0, -1.05f, 0.1f), null, new Vector3(0.36f, 0.36f, 0.36f));
                Part(arm, MeshFactory.CrystalPrism(5, 1, 1, 0.3f), _glowMat, new Vector3(s * 0.12f, -1.0f, 0.3f), new Vector3(1.0f, 0, 0), new Vector3(0.08f, 0.35f, 0.08f));
                if (s < 0) _armL = arm; else _armR = arm;
            }
            foreach (int s in new[] { -1, 1 })
            {
                var leg = new Node3D { Position = new Vector3(s * 0.35f, 0.75f, 0) };
                _body.AddChild(leg);
                Part(leg, MeshFactory.Rock(97 + s, 0.3f), stone, new Vector3(0, -0.4f, 0), null, new Vector3(0.3f, 0.45f, 0.3f));
                _legs.Add(leg);
            }
            // anel no chão que avisa a área do golpe
            _ring = Part(this, new TorusMesh { InnerRadius = Def.Area - 0.12f, OuterRadius = Def.Area, Rings = 32, RingSegments = 4 },
                new StandardMaterial3D { AlbedoColor = new Color(1f, 0.25f, 0.2f, 0.8f), EmissionEnabled = true, Emission = new Color(1f, 0.2f, 0.15f), EmissionEnergyMultiplier = 2.5f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
                new Vector3(0, 0.08f, Def.Range * 0.55f));
            _ring.Visible = false;
        }
        // barra de vida (aparece depois de levar dano)
        _bar = new Node3D { Position = new Vector3(0, Def.Kind == EnemyKind.Crawler ? 1.3f : 2.9f * Def.Scale, 0), Visible = false };
        AddChild(_bar);
        var bg = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.05f, 0.08f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true };
        var fg = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.3f, 0.3f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, RenderPriority = 1 };
        Part(_bar, new QuadMesh { Size = new Vector2(1.1f, 0.13f) }, bg, Vector3.Zero);
        _barFill = Part(_bar, new QuadMesh { Size = new Vector2(1.0f, 0.08f) }, fg, Vector3.Zero);
    }

    // ------------------------------------------------------------------ dano
    /// <summary>Leva um golpe: pisca, é empurrado e, ao zerar, morre largando fragmentos.</summary>
    public bool TakeHit(float dmg, Vector3 from, float knockback)
    {
        if (!Alive) return false;
        Hp -= dmg;
        _flash = 1f;
        _barShow = 6f;
        var push = GlobalPosition - from; push.Y = 0;
        if (push.LengthSquared() > 0.001f) _knock = push.Normalized() * knockback / Mathf.Max(1f, Def.Scale);
        Effects.Instance?.Hit(GlobalPosition + Vector3.Up * 0.8f * Def.Scale, push.Normalized(), _glowMat.AlbedoColor);
        Effects.Instance?.FloatText(GlobalPosition + Vector3.Up * (1.4f * Def.Scale + 0.4f), GameState.FormatNumber(dmg), new Color(1f, 0.9f, 0.5f), 1.1f);
        Audio.AudioManager.Instance?.Play("thud", -6f, 1.2f, 0.15f);
        if (Hp <= 0) { Die(); return true; }
        if (Mode != State.Windup) { Mode = State.Hurt; _timer = 0.25f; }
        return true;
    }

    void Die()
    {
        Mode = State.Dead;
        Hp = 0;
        Visible = false;
        CollisionLayer = 0;
        _respawn = 150f;
        Effects.Instance?.Burst(GlobalPosition + Vector3.Up * 0.6f * Def.Scale, _glowMat.AlbedoColor, 1.4f * Def.Scale);
        Audio.AudioManager.Instance?.Play("break", -2f, 0.8f, 0.1f);
        var drop = GameData.Mat(Def.Drop);
        if (drop != null) Manager?.SpawnShards?.Invoke(drop, GlobalPosition + Vector3.Up * 0.6f, _rng.Next(Def.DropMin, Def.DropMax + 1));
        if (Def.RareDrop != null && _rng.NextDouble() < Def.RareChance && GameData.Mat(Def.RareDrop) is { } rare)
            Manager?.SpawnShards?.Invoke(rare, GlobalPosition + Vector3.Up * 0.8f, 1);
        Manager?.OnKilled(this);
    }

    void Respawn()
    {
        Hp = Def.Hp;
        Mode = State.Idle;
        GlobalPosition = Home;
        Velocity = Vector3.Zero;
        CollisionLayer = EnemyLayer;
        Visible = true;
        _bar.Visible = false;
    }

    // ------------------------------------------------------------------ comportamento
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var player = Manager?.Player;
        if (Mode == State.Dead)
        {
            _respawn -= dt;
            if (_respawn <= 0 && (player == null || player.GlobalPosition.DistanceTo(Home) > 30f)) Respawn();
            return;
        }
        // longe do jogador: dorme (economiza CPU)
        float pd = player != null ? player.GlobalPosition.DistanceTo(GlobalPosition) : 999f;
        if (pd > 60f || !IsVisibleInTree()) return;
        _t += dt;
        _flash = Mathf.Max(0, _flash - dt * 5f);
        _skinMat.EmissionEnergyMultiplier = _flash * 2.5f;
        if (_barShow > 0) { _barShow -= dt; _bar.Visible = true; _barFill.Scale = new Vector3(Mathf.Max(0.01f, Hp / Def.Hp), 1, 1); _barFill.Position = new Vector3(-(1f - Hp / Def.Hp) * 0.5f, 0, 0.001f); }
        else _bar.Visible = false;

        var vel = Velocity;
        vel.Y = IsOnFloor() ? -0.5f : vel.Y - 18f * dt;
        Vector3 want = Vector3.Zero;
        var toP = player != null ? player.GlobalPosition - GlobalPosition : Vector3.Zero;
        toP.Y = 0;
        bool sees = player != null && player.Hp > 0 && pd < Def.Aggro && Manager.CanSee(this, player);
        float home = GlobalPosition.DistanceTo(Home);
        switch (Mode)
        {
            case State.Idle:
                if (sees && home < 28f) { Mode = State.Chase; break; }
                // passeia perto de casa
                _wanderT -= dt;
                if (_wanderT <= 0) { _wanderT = 2f + (float)_rng.NextDouble() * 3f; float a = (float)_rng.NextDouble() * Mathf.Tau; _wander = Home + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (float)_rng.NextDouble() * 4f; }
                var tw = _wander - GlobalPosition; tw.Y = 0;
                if (tw.Length() > 0.4f) want = tw.Normalized() * Def.Speed * 0.35f;
                break;
            case State.Chase:
                if (home > 30f || !sees && pd > Def.Aggro * 1.6f) { Mode = State.Idle; _wander = Home; Hp = Mathf.Min(Def.Hp, Hp + Def.Hp * 0.5f); break; }
                if (toP.Length() <= Def.Range) { Mode = State.Windup; _timer = Def.Windup; break; }
                want = toP.Normalized() * Def.Speed;
                break;
            case State.Windup:
                _timer -= dt;
                if (_timer <= 0) { Strike(player); Mode = State.Recover; _timer = Def.Cooldown; }
                break;
            case State.Recover:
                _timer -= dt;
                if (_timer <= 0) Mode = State.Chase;
                break;
            case State.Hurt:
                _timer -= dt;
                if (_timer <= 0) Mode = State.Chase;
                break;
        }
        // vira para quem persegue / para onde anda
        var face = Mode is State.Chase or State.Windup or State.Recover or State.Hurt ? toP : want;
        if (face.LengthSquared() > 0.01f)
            Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(face.X, face.Z), Mathf.Min(1f, dt * (Mode == State.Windup ? 2f : 8f))), 0);
        vel.X = want.X + _knock.X;
        vel.Z = want.Z + _knock.Z;
        _knock = _knock.Lerp(Vector3.Zero, Mathf.Min(1f, dt * 6f));
        // parede na frente: tenta contornar de lado
        if (IsOnWall() && want.LengthSquared() > 0.01f)
        {
            var side = want.Cross(Vector3.Up).Normalized() * Def.Speed * (((int)(_t * 0.5f)) % 2 == 0 ? 1 : -1);
            vel.X += side.X * 0.8f; vel.Z += side.Z * 0.8f;
        }
        Velocity = vel;
        MoveAndSlide();
        if (GlobalPosition.Y < Home.Y - 30f) Respawn();
        Animate(dt, new Vector2(vel.X, vel.Z).Length());
    }

    void Strike(PlayerController player)
    {
        if (player == null || player.Hp <= 0) return;
        var center = Def.Area > 0 ? GlobalPosition + GlobalTransform.Basis.Z * Def.Range * 0.55f : GlobalPosition;
        var d = player.GlobalPosition - center; d.Y = 0;
        float reach = Def.Area > 0 ? Def.Area : Def.Range * 1.25f;
        float dy = Mathf.Abs(player.GlobalPosition.Y - GlobalPosition.Y);
        if (Def.Area > 0)
        {
            Effects.Instance?.Burst(center + Vector3.Up * 0.2f, new Color(1f, 0.5f, 0.3f), 1.6f);
            Audio.AudioManager.Instance?.Play("thud", -1f, 0.6f, 0.05f);
        }
        if (d.Length() <= reach && dy < 2.5f)
            player.TakeDamage(Def.Damage * (Manager?.DamageTakenMultiplier?.Invoke() ?? 1f), GlobalPosition);
    }

    void Animate(float dt, float speed)
    {
        bool windup = Mode == State.Windup;
        float k = windup ? 1f - _timer / Mathf.Max(0.01f, Def.Windup) : 0f;
        _glowMat.EmissionEnergyMultiplier = 0.8f + (windup ? k * 3f : 0f);
        if (_ring != null) _ring.Visible = windup;
        if (Def.Kind == EnemyKind.Crawler)
        {
            float ph = _t * (6f + speed * 3f);
            for (int i = 0; i < _legs.Count; i++)
                _legs[i].Rotation = new Vector3(Mathf.Sin(ph + i * 1.7f) * 0.35f * Mathf.Min(1f, speed), 0, 0);
            // agacha no preparo e dá o bote
            _body.Position = new Vector3(0, windup ? -0.12f * k : Mathf.Abs(Mathf.Sin(ph)) * 0.03f, Mode == State.Recover && _timer > Def.Cooldown - 0.2f ? 0.35f : 0f);
            _head.Rotation = new Vector3(windup ? -0.4f * k : 0f, 0, 0);
        }
        else
        {
            float ph = _t * 3f;
            for (int i = 0; i < _legs.Count; i++)
                _legs[i].Rotation = new Vector3(Mathf.Sin(ph + i * Mathf.Pi) * 0.4f * Mathf.Min(1f, speed), 0, 0);
            // levanta os dois braços e bate no chão
            float arm = windup ? -2.6f * k : Mode == State.Recover && _timer > Def.Cooldown - 0.3f ? 0.4f : Mathf.Sin(ph) * 0.2f * Mathf.Min(1f, speed);
            _armL.Rotation = new Vector3(arm, 0, 0.15f);
            _armR.Rotation = new Vector3(arm, 0, -0.15f);
            _body.Position = new Vector3(0, Mathf.Abs(Mathf.Sin(ph)) * 0.04f * Mathf.Min(1f, speed), 0);
        }
    }
}

/// <summary>Cuida dos inimigos: nasce nos salões das Profundezas, recebe os golpes do jogador, conta abates.</summary>
public partial class EnemyManager : Node3D
{
    public readonly List<Enemy> Enemies = new();
    public PlayerController Player;
    public Action<MaterialDef, Vector3, int> SpawnShards;
    public Func<float> DamageTakenMultiplier;
    public event Action<Enemy> Killed;
    PhysicsRayQueryParameters3D _ray;

    public Enemy Spawn(EnemyDef def, Vector3 pos, int seed)
    {
        var e = new Enemy(def, pos, seed) { Manager = this, Name = $"Enemy_{def.Id}_{Enemies.Count}" };
        AddChild(e);
        Enemies.Add(e);
        return e;
    }

    public void OnKilled(Enemy e) => Killed?.Invoke(e);

    /// <summary>Linha de visão (sem parede no meio) do inimigo até o jogador.</summary>
    public bool CanSee(Enemy e, PlayerController p)
    {
        var space = GetWorld3D()?.DirectSpaceState;
        if (space == null) return true;
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = PlayerController.WorldLayer };
        _ray.From = e.GlobalPosition + Vector3.Up * 1.0f;
        _ray.To = p.GlobalPosition + Vector3.Up * 1.2f;
        _ray.Exclude = new Godot.Collections.Array<Rid> { e.GetRid(), p.GetRid() };
        return space.IntersectRay(_ray).Count == 0;
    }

    /// <summary>Golpe de arma: acerta todos os inimigos vivos no cone à frente. Devolve quantos acertou.</summary>
    public int HitCone(Vector3 origin, Vector3 forward, float reach, float arcDeg, float damage, float knockback)
    {
        int n = 0;
        forward.Y = 0;
        if (forward.LengthSquared() < 0.001f) return 0;
        forward = forward.Normalized();
        float cos = Mathf.Cos(Mathf.DegToRad(arcDeg));
        foreach (var e in Enemies)
        {
            if (!e.Alive || !e.Visible) continue;
            var d = e.GlobalPosition - origin;
            float dy = Mathf.Abs(d.Y);
            d.Y = 0;
            float r = reach + 0.45f * e.Def.Scale;
            if (d.Length() > r || dy > 2.6f) continue;
            if (d.Length() > 0.4f && d.Normalized().Dot(forward) < cos) continue;
            if (e.TakeHit(damage, origin, knockback)) n++;
        }
        return n;
    }

    public Enemy NearestAlive(Vector3 p, float max)
    {
        Enemy best = null; float bd = max;
        foreach (var e in Enemies) { if (!e.Alive) continue; float d = e.GlobalPosition.DistanceTo(p); if (d < bd) { bd = d; best = e; } }
        return best;
    }
}

/// <summary>Portal de volta, na entrada de cada Profundeza: leva ao portão selado da Catedral.</summary>
public partial class DeepPortal : StationBase
{
    public RegionDef Region;
    OmniLight3D _light;
    MeshInstance3D _swirl;

    public override void _Ready()
    {
        SetupBody(new Vector3(3.6f, 4.2f, 0.8f));
        var stone = MeshFactory.RockMaterial(new Color(0.5f, 0.5f, 0.56f));
        foreach (int s in new[] { -1, 1 })
            AddMesh(new BoxMesh { Size = new Vector3(0.7f, 4.0f, 0.9f) }, stone, new Vector3(s * 1.6f, 2.0f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(4.0f, 0.7f, 1.0f) }, stone, new Vector3(0, 4.2f, 0));
        _swirl = AddMesh(new BoxMesh { Size = new Vector3(2.5f, 3.6f, 0.05f) }, new StandardMaterial3D
        {
            AlbedoColor = new Color(0.4f, 0.9f, 1f, 0.55f), EmissionEnabled = true, Emission = new Color(0.4f, 0.85f, 1f), EmissionEnergyMultiplier = 1.8f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        }, new Vector3(0, 1.9f, 0));
        _light = new OmniLight3D { LightColor = new Color(0.45f, 0.85f, 1f), LightEnergy = 1.6f, OmniRange = 9f, Position = new Vector3(0, 2f, 1f) };
        AddChild(_light);
        Label = MakeLabel("SAÍDA · CATEDRAL", new Color(0.6f, 0.9f, 1f), 5.0f, 52);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        if (!IsVisibleInTree()) return;
        _light.LightEnergy = 1.6f + 0.4f * Mathf.Sin(Time * 2.5f);
        _swirl.Scale = new Vector3(1f + 0.03f * Mathf.Sin(Time * 3f), 1f, 1f);
    }

    public override string GetPrompt() => Loc.F("{0} Voltar ao portão da Catedral", InputHints.Interact);
    public override void Interact(PlayerController player) => UiBus.RequestDeepExit(Region?.Id);
}
