using System;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Mining;
using Godot;

namespace CrystalExcavator.Player;

/// <summary>
/// Controlador em primeira pessoa: WASD, mouse, pulo, corrida, mineração (botão esquerdo)
/// e interação contextual (E). Atributos vêm dos upgrades no GameState.
/// </summary>
public partial class PlayerController : CharacterBody3D
{
    public const uint WorldLayer = 1, PlayerLayer = 2, CrystalLayer = 4, InteractLayer = 8, GoblinLayer = 16;

    // ---------- vida (só importa quando goblins se rebelam) ----------
    public const float MaxHp = 100f;
    public float Hp { get; private set; } = MaxHp;
    float _sinceDamage = 99f;
    public event Action<float> Damaged;
    public event Action Fainted;

    public void TakeDamage(float amount, Vector3 from)
    {
        if (Hp <= 0 || amount <= 0) return;
        Hp = Mathf.Max(0, Hp - amount);
        _sinceDamage = 0;
        _shakeAmount = Mathf.Min(2f, _shakeAmount + 0.9f);
        var push = GlobalPosition - from; push.Y = 0;
        if (push.LengthSquared() > 0.01f) Velocity += push.Normalized() * 4f + Vector3.Up * 2f;
        AudioManager.Instance?.Play("thud", -2f, 0.7f, 0.1f);
        Damaged?.Invoke(amount);
        if (Hp <= 0) Fainted?.Invoke();
    }

    public void RestoreHp() { Hp = MaxHp; _sinceDamage = 99f; }

    public Camera3D Camera { get; private set; }
    public ToolView Tool { get; private set; }
    public bool InputEnabled { get; set; } = true;
    /// <summary>Modo construção: o botão de minerar constrói e E desmonta (tratados pelo BuildingManager).</summary>
    public bool BuildMode { get; set; }
    /// <summary>Permite testes automatizados sem captura real do mouse (modo headless).</summary>
    public bool SimulateCapturedMouse { get; set; }
    bool MouseCaptured => SimulateCapturedMouse || Input.MouseMode == Input.MouseModeEnum.Captured;
    public Crystal TargetCrystal { get; private set; }
    public IInteractable TargetInteractable { get; private set; }
    public float SwingCooldown01 => _swingInterval <= 0 ? 0 : Mathf.Clamp(_swingTimer / _swingInterval, 0, 1);

    Node3D _head;
    float _pitch;
    float _swingTimer;
    float _swingInterval = 0.5f;
    float _pendingHit = -1f;
    float _stepAccum;
    float _shakeAmount;
    float _fovKick;
    const float Gravity = 20f;
    const float JumpVelocity = 6.2f;
    const float EyeHeight = 1.55f;
    const float CrouchEye = 0.85f;
    public const float StandHeight = 1.75f, CrouchHeight = 1.0f;
    /// <summary>Ponto seguro para onde o jogador volta se cair para fora do mapa.</summary>
    public static Vector3 SafePoint = new(0, 1, 14);
    CapsuleShape3D _capsule;
    CollisionShape3D _bodyShape;
    bool _crouched, _wantCrouch;
    float _eye = EyeHeight;
    public bool Crouched => _crouched;

    public event Action<Crystal, bool> CrystalHit; // (cristal, quebrou)
    public event Action<int> ResonancePulse;
    float _workNotice;        // (cristais atingidos)

    /// <summary>Carga de ressonância (0..1). Golpes em ritmo carregam; pausa longa descarrega.</summary>
    public float Resonance01 => Mathf.Clamp(_resonance / Data.GameData.ResonanceHits, 0, 1);
    public bool ResonanceReady => _resonance >= Data.GameData.ResonanceHits;
    float _resonance;
    float _sinceLastHit = 99f;

    public override void _Ready()
    {
        CollisionLayer = PlayerLayer;
        CollisionMask = WorldLayer | CrystalLayer | InteractLayer | GoblinLayer;
        FloorMaxAngle = Mathf.DegToRad(50);
        _capsule = new CapsuleShape3D { Radius = 0.35f, Height = StandHeight };
        _bodyShape = new CollisionShape3D { Shape = _capsule, Position = new Vector3(0, StandHeight / 2, 0) };
        AddChild(_bodyShape);
        _head = new Node3D { Position = new Vector3(0, EyeHeight, 0) };
        AddChild(_head);
        Camera = new Camera3D { Current = true, Near = 0.05f, Far = 140f };
        _head.AddChild(Camera);
        Tool = new ToolView();
        Camera.AddChild(Tool);
        // lanterna do capacete: foco principal (sombras no Alto) + brilho suave ao redor
        Lamp = new SpotLight3D
        {
            Name = "HelmetLamp",
            LightColor = new Color(1f, 0.93f, 0.82f),
            LightEnergy = 4.6f,
            SpotRange = 48f,
            SpotAngle = 40f,
            SpotAngleAttenuation = 0.8f,
            SpotAttenuation = 0.95f,
            LightVolumetricFogEnergy = 1.6f,
            Position = new Vector3(0.12f, 0.1f, 0.05f),
        };
        _head.AddChild(Lamp);
        _lampFill = new OmniLight3D { LightColor = new Color(0.85f, 0.88f, 1f), LightEnergy = 0.9f, OmniRange = 10f, Position = new Vector3(0, 0.2f, 0) };
        _head.AddChild(_lampFill);
        Settings.Changed += ApplySettings;
        ApplySettings();
    }

    public override void _ExitTree() => Settings.Changed -= ApplySettings;

    // ---------------- corda: subir e descer com animação ----------------
    World.Rope _climb;
    float _climbS, _climbPhase, _climbSound, _mantle = -1f;
    Vector3 _mantleFrom;
    bool _climbUp;
    Node3D _hands, _handL, _handR, _handRope;
    public bool Climbing => _climb != null;
    public event Action<bool> ClimbFinished; // (subiu)

    public void StartClimb(World.Rope r, bool up)
    {
        if (r == null || _climb != null) return;
        _climb = r;
        _climbUp = up;
        _climbS = up ? 0f : r.Length;
        _climbPhase = 0;
        _mantle = -1f;
        Velocity = Vector3.Zero;
        _wantCrouch = false;
        SetCrouchedInternal(false);
        Tool.Visible = false;
        EnsureHands();
        _hands.Visible = true;
        // colado na parede a lanterna estouraria a imagem: fica mais fraca durante a escalada
        if (Lamp != null) { Lamp.LightEnergy = 1.2f; _lampFill.LightEnergy = 0.25f; }
    }

    /// <summary>Solta a corda (pulo): cai a partir de onde está.</summary>
    public void StopClimb(bool finished)
    {
        if (_climb == null) return;
        var r = _climb;
        _climb = null;
        Tool.Visible = true;
        if (_hands != null) _hands.Visible = false;
        _head.Position = new Vector3(0, _eye, 0);
        if (Lamp != null) { Lamp.LightEnergy = 4.6f; _lampFill.LightEnergy = 0.9f; }
        if (finished)
        {
            GlobalPosition = (_climbUp ? r.TopExit : r.Bottom + Away(r) * 0.6f) + Vector3.Up * 0.1f;
            Velocity = Vector3.Zero;
            ClimbFinished?.Invoke(_climbUp);
        }
        else Velocity = Away(r) * 2f;
    }

    static Vector3 Away(World.Rope r)
    {
        var a = r.Bottom - r.Top; a.Y = 0;
        return a.LengthSquared() > 0.01f ? a.Normalized() : Vector3.Forward;
    }

    void UpdateClimb(float dt, bool active)
    {
        var r = _climb;
        if (!IsInstanceValid(r)) { _climb = null; Tool.Visible = true; if (_hands != null) _hands.Visible = false; return; }
        // subindo na borda: puxa o corpo para cima e para frente (sem teleporte)
        if (_mantle >= 0f)
        {
            _mantle += dt / 0.45f;
            float m = Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, _mantle));
            var target = r.TopExit + Vector3.Up * 0.1f;
            var arc = Vector3.Up * Mathf.Sin(m * Mathf.Pi) * 0.35f;
            GlobalPosition = _mantleFrom.Lerp(target, m) + arc;
            _hands.Position = new Vector3(0, -m * 0.4f, 0);
            if (_mantle >= 1f) { _hands.Position = Vector3.Zero; StopClimb(true); }
            return;
        }
        if (active && Input.IsActionJustPressed("jump")) { StopClimb(false); return; }
        float speed = 2.3f;
        _climbS = Mathf.Clamp(_climbS + (_climbUp ? speed : -speed) * dt, 0f, r.Length);
        var along = (r.Top - r.Bottom).Normalized();
        var hands = r.Bottom + along * _climbS;
        // corpo pendurado: mãos na corda na altura do peito, um pouco afastado da parede
        GlobalPosition = hands - Vector3.Up * 1.25f + Away(r) * 0.65f;
        Velocity = Vector3.Zero;
        // animação: mãos alternam a cada nó (0,6 m); a cabeça balança junto
        _climbPhase += speed * dt / 0.6f;
        float ph = _climbPhase * Mathf.Pi;
        float l = Mathf.Sin(ph), rr = Mathf.Sin(ph + Mathf.Pi);
        float dirSign = _climbUp ? 1f : -1f;
        _handL.Position = new Vector3(-0.012f, -0.1f + l * 0.11f * dirSign, -0.47f);
        _handR.Position = new Vector3(0.012f, -0.1f + rr * 0.11f * dirSign, -0.475f);
        _handL.Rotation = new Vector3(0, 0, Mathf.Max(0, l) * 0.12f);
        _handR.Rotation = new Vector3(0, 0, -Mathf.Max(0, rr) * 0.12f);
        _head.Position = new Vector3(Mathf.Sin(ph) * 0.03f, _eye + Mathf.Abs(Mathf.Cos(ph)) * 0.04f, 0);
        _climbSound -= dt;
        if (_climbSound <= 0) { _climbSound = 0.6f / speed; AudioManager.Instance?.Play("step", -12f, 0.55f, 0.08f); }
        if (_climbUp && _climbS >= r.Length - 0.01f) { _mantle = 0f; _mantleFrom = GlobalPosition; AudioManager.Instance?.Play("step", -8f, 0.8f); }
        else if (!_climbUp && _climbS <= 0.01f) StopClimb(true);
    }

    void EnsureHands()
    {
        if (_hands != null) return;
        _hands = new Node3D { Name = "ClimbHands", Visible = false };
        Camera.AddChild(_hands);
        var glove = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.11f, 0.12f), Roughness = 0.85f };
        var sleeve = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.28f, 0.1f), Roughness = 0.9f };
        var neon = World.MeshFactory.Emissive(new Color(0.2f, 0.6f, 0.55f), new Color(0.3f, 1f, 0.9f), 0.35f);
        Node3D Hand(float side)
        {
            // punho fechado em volta da corda + antebraço saindo do canto de baixo da tela
            var h = new Node3D();
            _hands.AddChild(h);
            h.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.075f, 0.07f, 0.065f) }, MaterialOverride = glove, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            h.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.078f, 0.012f, 0.02f) }, MaterialOverride = neon, Position = new Vector3(0, 0.02f, 0.03f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            var arm = new Node3D { Position = new Vector3(side * 0.02f, -0.03f, 0.02f), Rotation = new Vector3(0.75f, 0, side * 0.55f) };
            h.AddChild(arm);
            arm.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.032f, BottomRadius = 0.042f, Height = 0.42f, RadialSegments = 8 }, MaterialOverride = sleeve, Position = new Vector3(0, -0.21f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            return h;
        }
        _handL = Hand(-1); _handR = Hand(1);
        // trecho da corda à frente (acompanha a câmera; longo para aparecer olhando para cima ou para baixo)
        _handRope = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.018f, BottomRadius = 0.018f, Height = 4f, RadialSegments = 6 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.42f, 0.26f), Roughness = 0.95f }, Position = new Vector3(0, 0, -0.5f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _hands.AddChild(_handRope);
    }

    public SpotLight3D Lamp;
    OmniLight3D _lampFill;
    public bool LampOn = true;
    public void SetLamp(bool on) { LampOn = on; ApplySettings(); }

    void ApplySettings()
    {
        if (Camera == null) return;
        if (Lamp != null)
        {
            Lamp.ShadowEnabled = Settings.Quality == GraphicsQuality.High;
            Lamp.Visible = LampOn;
            _lampFill.Visible = LampOn && Settings.Quality != GraphicsQuality.Low;
        }
        Camera.Fov = Settings.Data.Fov;
        Camera.Far = Settings.Quality switch { GraphicsQuality.Low => 80f, GraphicsQuality.Medium => 140f, _ => 240f };
    }

    public float Yaw
    {
        get => Rotation.Y;
        set => Rotation = new Vector3(0, value, 0);
    }

    public Vector3 BodyCenter => GlobalPosition + Vector3.Up * 0.9f;

    public override void _UnhandledInput(InputEvent e)
    {
        if (!InputEnabled || !MouseCaptured) return;
        if (e is InputEventMouseMotion mm)
        {
            float sens = Settings.Data.MouseSensitivity * 0.01f;
            RotateY(-mm.Relative.X * sens);
            float dy = mm.Relative.Y * sens * (Settings.Data.InvertY ? -1 : 1);
            _pitch = Mathf.Clamp(_pitch - dy, Mathf.DegToRad(-88), Mathf.DegToRad(88));
            _head.Rotation = new Vector3(_pitch, 0, 0);
            Tool.AddSway(mm.Relative);
        }
        else if (e.IsActionPressed("interact") && !BuildMode)
        {
            TargetInteractable?.Interact(this);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        long __t = Core.Prof.Start();
        _PhysicsInner(delta);
        Core.Prof.End("player", __t);
    }

    void _PhysicsInner(double delta)
    {
        float dt = (float)delta;
        var gs = GameState.Instance;
        if (_suppress > 0) _suppress -= dt;
        bool active = InputEnabled && MouseCaptured && _suppress <= 0;

        // ---------- Olhar com o controle (analógico direito) ----------
        if (active)
        {
            var look = Input.GetVector("look_left", "look_right", "look_up", "look_down");
            if (look.LengthSquared() > 0.0001f)
            {
                // curva quadrática: precisão perto do centro, rapidez nas bordas
                look *= look.Length();
                float lookSpeed = 3.2f * Settings.Data.PadLookSensitivity;
                RotateY(-look.X * lookSpeed * dt);
                float dy = look.Y * lookSpeed * 0.75f * dt * (Settings.Data.InvertY ? -1 : 1);
                _pitch = Mathf.Clamp(_pitch - dy, Mathf.DegToRad(-88), Mathf.DegToRad(88));
                _head.Rotation = new Vector3(_pitch, 0, 0);
            }
        }

        // regeneração depois de alguns segundos sem levar dano
        _sinceDamage += dt;
        if (_sinceDamage > 4f && Hp < MaxHp && Hp > 0) Hp = Mathf.Min(MaxHp, Hp + 6f * dt);

        if (_climb != null) { UpdateClimb(dt, active); UpdateTarget(gs); return; }

        // ---------- Movimento ----------
        var vel = Velocity;
        if (!IsOnFloor()) vel.Y -= Gravity * dt;
        Vector2 input = active ? Input.GetVector("move_left", "move_right", "move_forward", "move_back") : Vector2.Zero;
        var wish = (Transform.Basis * new Vector3(input.X, 0, input.Y));
        wish.Y = 0;
        if (wish.LengthSquared() > 1) wish = wish.Normalized();
        if (active && Input.IsActionJustPressed("flashlight"))
        {
            LampOn = !LampOn;
            ApplySettings();
            AudioManager.Instance?.Play("click", -6f);
        }
        // agachar (alterna): passa por túneis baixos; só levanta se houver espaço
        if (active && Input.IsActionJustPressed("crouch")) _wantCrouch = !_wantCrouch;
        if (active && Input.IsActionJustPressed("jump") && _crouched) _wantCrouch = false;
        SetCrouchedInternal(_wantCrouch || (_crouched && !CanStand()));
        _eye = Mathf.MoveToward(_eye, _crouched ? CrouchEye : EyeHeight, dt * 4f);
        _head.Position = new Vector3(0, _eye, 0);
        float speed = gs.MoveSpeed * (_crouched ? 0.5f : active && Input.IsActionPressed("sprint") ? 1.45f : 1f);
        float accel = IsOnFloor() ? 14f : 4f;
        var horiz = new Vector3(vel.X, 0, vel.Z).Lerp(wish * speed, Mathf.Min(1, accel * dt));
        vel.X = horiz.X; vel.Z = horiz.Z;
        if (active && Input.IsActionJustPressed("jump") && IsOnFloor() && !_crouched) vel.Y = JumpVelocity;
        Velocity = vel;
        MoveAndSlide();

        // passos
        float hs = new Vector2(Velocity.X, Velocity.Z).Length();
        if (IsOnFloor() && hs > 0.5f)
        {
            _stepAccum += hs * dt;
            if (_stepAccum > 2.1f) { _stepAccum = 0; AudioManager.Instance?.Play("step", -14f, 1f, 0.15f); }
        }
        // segurança: se cair fora do mapa, volta ao ponto seguro
        if (GlobalPosition.Y < -20) { GlobalPosition = SafePoint; Velocity = Vector3.Zero; }

        // ---------- Mira ----------
        UpdateTarget(gs);

        // ---------- Mineração ----------
        _swingInterval = gs.SwingInterval;
        if (_swingTimer > 0) _swingTimer -= dt;
        _sinceLastHit += dt;
        float window = Mathf.Max(Data.GameData.ResonanceWindow, _swingInterval * 2.5f);
        if (_sinceLastHit > window && _resonance > 0) _resonance = Mathf.Max(0, _resonance - dt * 3f);
        bool wantMine = active && Input.IsActionPressed("mine") && TargetInteractable == null && !BuildMode;
        if (_workNotice > 0) _workNotice -= dt;
        if (wantMine && !gs.CanWork)
        {
            // fora do turno: só vender e voltar para casa
            if (_workNotice <= 0) { gs.RequireWork(); _workNotice = 4f; }
            wantMine = false;
        }
        if (wantMine && _swingTimer <= 0)
        {
            _swingTimer = _swingInterval;
            Tool.Swing(_swingInterval);
            AudioManager.Instance?.Play("swing", -16f, 1f, 0.1f);
            _pendingHit = Mathf.Min(_swingInterval * 0.45f, 0.12f);
        }
        UpdateBeam(gs, wantMine);
        if (_pendingHit >= 0)
        {
            _pendingHit -= dt;
            if (_pendingHit < 0) ResolveHit(gs);
        }

        // ---------- Câmera (tremor e FOV) ----------
        _shakeAmount = Mathf.Max(0, _shakeAmount - dt * 4f);
        _fovKick = Mathf.Max(0, _fovKick - dt * 6f);
        var rng = new Vector3((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0), 0) * _shakeAmount * 0.02f * (Settings.Data.ReduceShake ? 0.15f : 1f);
        Camera.Position = rng;
        float sprintFov = active && Input.IsActionPressed("sprint") && hs > 1f ? 4f : 0f;
        Camera.Fov = Mathf.Lerp(Camera.Fov, Settings.Data.Fov + sprintFov - _fovKick * (Settings.Data.ReduceShake ? 0.2f : 1f), Mathf.Min(1, dt * 8f));
        Tool.UpdateView(dt, Mathf.Clamp(hs / Mathf.Max(1, gs.MoveSpeed), 0, 1.5f), IsOnFloor());
    }

    PhysicsRayQueryParameters3D _rayQuery;
    PhysicsShapeQueryParameters3D _standQuery;

    Godot.Collections.Dictionary RayCast(float distance, uint mask)
    {
        var space = GetWorld3D().DirectSpaceState;
        var from = Camera.GlobalPosition;
        var to = from - Camera.GlobalTransform.Basis.Z * distance;
        // consulta reaproveitada: evita criar objetos nativos a cada quadro
        _rayQuery ??= new PhysicsRayQueryParameters3D { CollideWithAreas = true, Exclude = new Godot.Collections.Array<Rid> { GetRid() } };
        _rayQuery.From = from;
        _rayQuery.To = to;
        _rayQuery.CollisionMask = mask;
        return space.IntersectRay(_rayQuery);
    }

    void UpdateTarget(GameState gs)
    {
        TargetCrystal = null;
        TargetInteractable = null;
        var hit = RayCast(Mathf.Max(gs.MiningReach, 3.6f), WorldLayer | CrystalLayer | InteractLayer);
        if (hit.Count == 0) return;
        var collider = hit["collider"].AsGodotObject();
        var pos = hit["position"].AsVector3();
        float dist = pos.DistanceTo(Camera.GlobalPosition);
        if (collider is Crystal c && c.IsMineable && dist <= gs.MiningReach + 0.6f) TargetCrystal = c;
        else if (FindInteractable(collider as Node) is { } it && dist <= 3.6f && !string.IsNullOrEmpty(it.GetPrompt())) TargetInteractable = it;
    }

    static IInteractable FindInteractable(Node n)
    {
        for (int i = 0; i < 3 && n != null; i++, n = n.GetParent())
            if (n is IInteractable it) return it;
        return null;
    }

    void ResolveHit(GameState gs)
    {
        var hit = RayCast(gs.MiningReach + 0.6f, WorldLayer | CrystalLayer | GoblinLayer);
        if (hit.Count == 0) return;
        var collider = hit["collider"].AsGodotObject();
        var pos = hit["position"].AsVector3();
        var normal = hit["normal"].AsVector3();
        if (collider is World.GoblinNpc gob && gob.Hostile)
        {
            // o feixe atordoa e empurra goblins rebelados
            gob.TakeHit(GlobalPosition, 1);
            Effects.Instance?.Hit(pos, normal, new Color(1f, 0.9f, 0.4f));
            _shakeAmount = Mathf.Min(1.2f, _shakeAmount + 0.4f);
            return;
        }
        if (collider is Crystal c && c.IsMineable)
        {
            _sinceLastHit = 0;
            if (ResonanceReady)
            {
                _resonance = 0;
                TriggerPulse(gs, c, pos);
            }
            else
            {
                _resonance += 1;
                if (ResonanceReady) AudioManager.Instance?.Play("charged", -6f);
            }
            float dmg = gs.Damage;
            bool crit = gs.CritChance > 0 && GD.Randf() < gs.CritChance;
            if (crit)
            {
                dmg *= GameState.CritMultiplier;
                Effects.Instance?.FloatText(pos + Vector3.Up * 0.4f, Core.Loc.T("CRÍTICO!"), new Color(1f, 0.85f, 0.3f), 1.1f);
            }
            bool broke = c.IsMineable && c.Hit(dmg, pos, normal);
            _shakeAmount = Mathf.Min(1.5f, _shakeAmount + (broke ? 1.2f : 0.45f));
            _fovKick = broke ? 3f : 1f;
            CrystalHit?.Invoke(c, broke);
        }
        else
        {
            AudioManager.Instance?.Play("thud", -10f, 1f, 0.1f);
            Effects.Instance?.Hit(pos, normal, new Color(0.6f, 0.55f, 0.65f));
            _shakeAmount = Mathf.Min(1f, _shakeAmount + 0.2f);
        }
    }

    /// <summary>Pulso de ressonância: dano em área em todos os cristais dentro do raio.</summary>
    void TriggerPulse(GameState gs, Crystal origin, Vector3 point)
    {
        float radius = gs.ResonanceRadius;
        float dmg = gs.Damage * gs.ResonanceMultiplier;
        int count = 0;
        var center = origin.GlobalPosition;
        foreach (var node in GetTree().GetNodesInGroup(Crystal.Group))
        {
            if (node is not Crystal c || !c.IsMineable || !c.IsVisibleInTree()) continue;
            float d = c.GlobalPosition.DistanceTo(center);
            if (d > radius) continue;
            var hitPos = c.GlobalPosition + Vector3.Up * 0.8f;
            c.Hit(dmg, hitPos, (BodyCenter - hitPos).Normalized(), true);
            count++;
        }
        World.GoblinCamp.PulseHitAll(center, radius, GlobalPosition);
        Effects.Instance?.Shockwave(center + Vector3.Up * 0.3f, radius, origin.Material.Glow);
        AudioManager.Instance?.Play("pulse", -1f);
        _shakeAmount = Mathf.Min(2f, _shakeAmount + 1.4f);
        _fovKick = 5f;
        ResonancePulse?.Invoke(count);
    }

    /// <summary>Alimenta o feixe visual da ferramenta: ponto atingido e cor do alvo.</summary>
    void UpdateBeam(GameState gs, bool firing)
    {
        Tool.Firing = firing;
        AudioManager.Instance?.SetBeam(firing, TargetCrystal != null ? 1f - TargetCrystal.HpRatio : 0f);
        if (!firing) return;
        float reach = gs.MiningReach + 0.6f;
        var hit = RayCast(reach, WorldLayer | CrystalLayer | GoblinLayer);
        if (hit.Count > 0)
        {
            Tool.BeamEnd = hit["position"].AsVector3();
            Tool.BeamColor = hit["collider"].AsGodotObject() is Crystal c ? c.Material.Glow : ToolView.TierColors[gs.ToolTier];
        }
        else
        {
            Tool.BeamEnd = Camera.GlobalPosition - Camera.GlobalTransform.Basis.Z * reach;
            Tool.BeamColor = ToolView.TierColors[gs.ToolTier];
        }
    }

    /// <summary>Pede para agachar/levantar (testes e bot). Levantar só acontece com espaço livre.</summary>
    public void SetCrouch(bool crouch) => _wantCrouch = crouch;

    void SetCrouchedInternal(bool c)
    {
        if (c == _crouched) return;
        _crouched = c;
        float h = c ? CrouchHeight : StandHeight;
        _capsule.Height = h;
        _bodyShape.Position = new Vector3(0, h / 2, 0);
    }

    /// <summary>Há espaço acima para ficar de pé?</summary>
    bool CanStand()
    {
        var space = GetWorld3D().DirectSpaceState;
        _standQuery ??= new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D { Radius = 0.33f, Height = StandHeight - 0.1f },
            CollisionMask = WorldLayer,
            Exclude = new Godot.Collections.Array<Rid> { GetRid() },
        };
        _standQuery.Transform = new Transform3D(Basis.Identity, GlobalPosition + Vector3.Up * (StandHeight / 2 + 0.06f));
        return space.IntersectShape(_standQuery, 1).Count == 0;
    }

    float _suppress;
    /// <summary>Ignora a entrada por um instante (o botão que fechou um menu não deve pular/agachar).</summary>
    public void SuppressInput(float seconds = 0.2f) => _suppress = seconds;

    public void AddShake(float amount) => _shakeAmount = Mathf.Min(2f, _shakeAmount + amount);

    public void SetLook(float yaw, float pitch = 0)
    {
        Yaw = yaw;
        _pitch = pitch;
        if (_head != null) _head.Rotation = new Vector3(_pitch, 0, 0);
    }
}
