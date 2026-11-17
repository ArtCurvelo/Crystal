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
    public const uint WorldLayer = 1, PlayerLayer = 2, CrystalLayer = 4, InteractLayer = 8;

    public Camera3D Camera { get; private set; }
    public ToolView Tool { get; private set; }
    public bool InputEnabled { get; set; } = true;
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
        CollisionMask = WorldLayer | CrystalLayer | InteractLayer;
        FloorMaxAngle = Mathf.DegToRad(50);
        var shape = new CollisionShape3D
        {
            Shape = new CapsuleShape3D { Radius = 0.35f, Height = 1.75f },
            Position = new Vector3(0, 0.875f, 0),
        };
        AddChild(shape);
        _head = new Node3D { Position = new Vector3(0, EyeHeight, 0) };
        AddChild(_head);
        Camera = new Camera3D { Current = true, Near = 0.05f, Far = 140f };
        _head.AddChild(Camera);
        Tool = new ToolView();
        Camera.AddChild(Tool);
        Settings.Changed += ApplySettings;
        ApplySettings();
    }

    public override void _ExitTree() => Settings.Changed -= ApplySettings;

    void ApplySettings()
    {
        if (Camera == null) return;
        Camera.Fov = Settings.Data.Fov;
        Camera.Far = Settings.Quality switch { GraphicsQuality.Low => 70f, GraphicsQuality.Medium => 110f, _ => 150f };
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
        else if (e.IsActionPressed("interact"))
        {
            TargetInteractable?.Interact(this);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var gs = GameState.Instance;
        bool active = InputEnabled && MouseCaptured;

        // ---------- Olhar com o controle (analógico direito) ----------
        if (active)
        {
            var look = Input.GetVector("look_left", "look_right", "look_up", "look_down");
            if (look.LengthSquared() > 0.0001f)
            {
                // curva quadrática: precisão perto do centro, rapidez nas bordas
                look *= look.Length();
                float lookSpeed = 3.2f * (Settings.Data.MouseSensitivity / 0.22f);
                RotateY(-look.X * lookSpeed * dt);
                float dy = look.Y * lookSpeed * 0.75f * dt * (Settings.Data.InvertY ? -1 : 1);
                _pitch = Mathf.Clamp(_pitch - dy, Mathf.DegToRad(-88), Mathf.DegToRad(88));
                _head.Rotation = new Vector3(_pitch, 0, 0);
            }
        }

        // ---------- Movimento ----------
        var vel = Velocity;
        if (!IsOnFloor()) vel.Y -= Gravity * dt;
        Vector2 input = active ? Input.GetVector("move_left", "move_right", "move_forward", "move_back") : Vector2.Zero;
        var wish = (Transform.Basis * new Vector3(input.X, 0, input.Y));
        wish.Y = 0;
        if (wish.LengthSquared() > 1) wish = wish.Normalized();
        float speed = gs.MoveSpeed * (active && Input.IsActionPressed("sprint") ? 1.45f : 1f);
        float accel = IsOnFloor() ? 14f : 4f;
        var horiz = new Vector3(vel.X, 0, vel.Z).Lerp(wish * speed, Mathf.Min(1, accel * dt));
        vel.X = horiz.X; vel.Z = horiz.Z;
        if (active && Input.IsActionJustPressed("jump") && IsOnFloor()) vel.Y = JumpVelocity;
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
        if (GlobalPosition.Y < -20) { GlobalPosition = new Vector3(0, 1, 14); Velocity = Vector3.Zero; }

        // ---------- Mira ----------
        UpdateTarget(gs);

        // ---------- Mineração ----------
        _swingInterval = gs.SwingInterval;
        if (_swingTimer > 0) _swingTimer -= dt;
        _sinceLastHit += dt;
        float window = Mathf.Max(Data.GameData.ResonanceWindow, _swingInterval * 2.5f);
        if (_sinceLastHit > window && _resonance > 0) _resonance = Mathf.Max(0, _resonance - dt * 3f);
        bool wantMine = active && Input.IsActionPressed("mine") && TargetInteractable == null;
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
        var rng = new Vector3((float)GD.RandRange(-1.0, 1.0), (float)GD.RandRange(-1.0, 1.0), 0) * _shakeAmount * 0.02f;
        Camera.Position = rng;
        float sprintFov = active && Input.IsActionPressed("sprint") && hs > 1f ? 4f : 0f;
        Camera.Fov = Mathf.Lerp(Camera.Fov, Settings.Data.Fov + sprintFov - _fovKick, Mathf.Min(1, dt * 8f));
        Tool.UpdateView(dt, Mathf.Clamp(hs / Mathf.Max(1, gs.MoveSpeed), 0, 1.5f), IsOnFloor());
    }

    Godot.Collections.Dictionary RayCast(float distance, uint mask)
    {
        var space = GetWorld3D().DirectSpaceState;
        var from = Camera.GlobalPosition;
        var to = from - Camera.GlobalTransform.Basis.Z * distance;
        var q = PhysicsRayQueryParameters3D.Create(from, to, mask, new Godot.Collections.Array<Rid> { GetRid() });
        q.CollideWithAreas = true;
        return space.IntersectRay(q);
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
        var hit = RayCast(gs.MiningReach + 0.6f, WorldLayer | CrystalLayer);
        if (hit.Count == 0) return;
        var collider = hit["collider"].AsGodotObject();
        var pos = hit["position"].AsVector3();
        var normal = hit["normal"].AsVector3();
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
                Effects.Instance?.FloatText(pos + Vector3.Up * 0.4f, "CRÍTICO!", new Color(1f, 0.85f, 0.3f), 1.1f);
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
        var hit = RayCast(reach, WorldLayer | CrystalLayer);
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

    public void AddShake(float amount) => _shakeAmount = Mathf.Min(2f, _shakeAmount + amount);

    public void SetLook(float yaw, float pitch = 0)
    {
        Yaw = yaw;
        _pitch = pitch;
        if (_head != null) _head.Rotation = new Vector3(_pitch, 0, 0);
    }
}
