using System;
using System.Collections.Generic;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Mining;

/// <summary>
/// Formação de cristal minerável. Tem resistência própria, reage a cada golpe
/// (tremor, flash, partículas, som), quebra liberando fragmentos e renasce após um tempo.
/// </summary>
public partial class Crystal : StaticBody3D
{
    public enum CrystalState { Active, Broken, Growing }
    public const string Group = "crystals";

    public MaterialDef Material { get; private set; }
    public float Hp { get; private set; }
    public float MaxHp { get; private set; }
    public CrystalState State { get; private set; } = CrystalState.Active;
    public float HpRatio => MaxHp <= 0 ? 0 : Mathf.Clamp(Hp / MaxHp, 0, 1);
    public bool IsMineable => State == CrystalState.Active;
    public float SizeScale = 1f;

    /// <summary>Escolhe o material no (re)nascimento. Definido pela região.</summary>
    public Func<Random, MaterialDef> PickMaterial;

    Node3D _visual;
    CollisionShape3D _shape;
    readonly List<ShaderMaterial> _mats = new();
    Random _rng;
    float _respawnTimer;
    /// <summary>Acelera o renascimento quando a região está esgotada (ajustado pelo WorldBuilder).</summary>
    public float RespawnRate = 1f;
    float _flash;
    float _shake;
    float _baseEnergy = 1.1f;
    float _time;
    Vector3 _shakeDir;
    CpuParticles3D _sparkle;
    OmniLight3D _light;

    /// <summary>Onde o jogador fica para minerar este depósito (usado por testes e pelo bot).</summary>
    public Vector3 StandPoint;
    /// <summary>Centro visual do depósito (respeita depósitos inclinados nas paredes).</summary>
    public Vector3 Center => GlobalPosition + GlobalTransform.Basis.Y.Normalized() * Mathf.Min(0.7f * (Material.Special ? 2.6f : SizeScale), _colliderHeight * 0.55f);
    float _colliderHeight = 1f;
    public const float ViewDistance = 55f;

    public void Init(MaterialDef mat, int seed, float sizeScale = 1f)
    {
        _rng = new Random(seed);
        SizeScale = sizeScale;
        AddToGroup(Group);
        CollisionLayer = 4;   // camada 3: cristais
        CollisionMask = 0;
        _visual = new Node3D { Name = "Visual" };
        AddChild(_visual);
        _shape = new CollisionShape3D();
        AddChild(_shape);
        SetMaterial(mat);
    }

    void SetMaterial(MaterialDef mat)
    {
        Material = mat;
        float hpScale = mat.Special ? 1f : SizeScale;
        MaxHp = mat.Hp * hpScale;
        Hp = MaxHp;
        BuildVisual();
    }

    void BuildVisual()
    {
        foreach (Node c in _visual.GetChildren()) c.QueueFree();
        _mats.Clear();
        _sparkle = null;
        _light = null;
        var mat = Material;
        bool special = mat.Special;
        float s = special ? 2.6f : SizeScale;
        // brilho forte (o bloom faz os minérios "acenderem" nas cavernas escuras)
        _baseEnergy = mat.Rare ? 1.5f : special ? 0.35f : 0.95f;

        // formato e textura próprios do tipo de minério
        var built = OreVisuals.Build(_visual, mat, _rng, s);
        // depósitos distantes não são desenhados (a névoa já os esconderia)
        if (!special)
            foreach (Node n in _visual.GetChildren())
                if (n is GeometryInstance3D gi) gi.VisibilityRangeEnd = ViewDistance;
        _mats.AddRange(built.Materials);
        SetGlow(_baseEnergy, 0f, 0f);
        float vh = built.Height;

        if (mat.Rare || special)
        {
            _sparkle = new CpuParticles3D
            {
                Amount = special ? 24 : 8,
                Lifetime = 1.6f,
                Mesh = MeshFactory.CrystalPrism(4, 0.03f, 0.06f, 0.5f),
                EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.6f * s,
                Direction = Vector3.Up,
                Spread = 180,
                Gravity = new Vector3(0, 0.4f, 0),
                InitialVelocityMin = 0.1f,
                InitialVelocityMax = 0.4f,
                Color = mat.Glow.Lightened(0.4f),
                Position = new Vector3(0, 0.6f * s, 0),
            };
            _sparkle.MaterialOverride = MeshFactory.Unshaded(Colors.White);
            _visual.AddChild(_sparkle);
        }
        // todo depósito ilumina a rocha ao redor com a própria cor (some à distância)
        _light = new OmniLight3D
        {
            LightColor = mat.Glow,
            LightEnergy = special ? 1.1f : mat.Rare ? 1.6f : 1.0f,
            OmniRange = special ? 14f : (mat.Rare ? 5.5f : 3.6f) * Mathf.Max(0.8f, SizeScale),
            OmniAttenuation = 1.4f,
            ShadowEnabled = false,
            Position = new Vector3(0, 0.75f * s, 0),
            DistanceFadeEnabled = true,
            DistanceFadeBegin = 30f,
            DistanceFadeLength = 10f,
        };
        _light.Visible = Settings.Quality != GraphicsQuality.Low;
        _visual.AddChild(_light);

        _colliderHeight = vh;
        var shape = new CylinderShape3D { Radius = built.Radius, Height = vh };
        _shape.Shape = shape;
        _shape.Position = new Vector3(0, vh / 2, 0);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        switch (State)
        {
            case CrystalState.Broken:
                _respawnTimer -= dt * RespawnRate;
                if (_respawnTimer <= 0) BeginRegrow();
                return;
            case CrystalState.Growing:
                return;
        }

        if (_flash > 0 || _shake > 0)
        {
            _flash = Mathf.Max(0, _flash - dt * 5f);
            _shake = Mathf.Max(0, _shake - dt * 6f);
            float damageGlow = (1f - HpRatio) * 1.4f;
            SetGlow(_baseEnergy + damageGlow * 0.4f, 1f - HpRatio, _flash);
            float wob = MathF.Sin(_time * 60f) * _shake * 0.06f;
            _visual.Position = _shakeDir * wob;
            float squash = 1f - _shake * 0.08f;
            float scaleXZ = 1f + _shake * 0.05f;
            float shrink = 0.85f + HpRatio * 0.15f;
            _visual.Scale = new Vector3(scaleXZ * shrink, squash * shrink, scaleXZ * shrink);
        }
        else if (Material.Rare || Material.Special)
        {
            // pulsação de cristais raros
            float pulse = 0.35f * (0.5f + 0.5f * MathF.Sin(_time * 3f));
            SetGlow(_baseEnergy + pulse, 1f - HpRatio, 0f);
            if (Material.Special) _visual.RotateY(dt * 0.15f);
        }
    }

    void SetGlow(float energy, float damage, float flash)
    {
        foreach (var m in _mats)
        {
            m.SetShaderParameter("glow_energy", energy);
            m.SetShaderParameter("damage", damage);
            m.SetShaderParameter("flash", flash);
        }
    }

    /// <summary>Aplica dano. Retorna true se este golpe quebrou o cristal.</summary>
    /// <summary>Quem quebrar com um destino (ex.: goblin) recebe os fragmentos direto, sem espalhar no chão.</summary>
    System.Action<MaterialDef, int> _sink;

    public bool Hit(float damage, Vector3 point, Vector3 normal, bool quiet = false, System.Action<MaterialDef, int> sink = null)
    {
        _sink = sink;
        if (State != CrystalState.Active) return false;
        Hp -= damage;
        _flash = 1f;
        _shake = 1f;
        var toCam = normal.LengthSquared() > 0.01f ? normal : Vector3.Up;
        _shakeDir = new Vector3(-toCam.X, 0, -toCam.Z).Normalized();
        if (_shakeDir.LengthSquared() < 0.01f) _shakeDir = Vector3.Right;

        Effects.Instance?.HitFx(Material, point, normal);
        float pitch = 0.85f + (1f - HpRatio) * 0.5f;
        if (!quiet) AudioManager.Instance?.PlayOre(Material, false, -4f, pitch);
        var jitter = new Vector3((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() * 0.6f, (float)_rng.NextDouble() - 0.5f) * 0.5f;
        Effects.Instance?.FloatText(point + normal * 0.25f + jitter, GameState.FormatNumber(Math.Min(damage, Hp + damage)),
            new Color(1, 1, 1).Lerp(Material.Glow, 0.4f), 0.8f);

        if (Hp <= 0)
        {
            Break();
            return true;
        }
        return false;
    }

    void Break()
    {
        State = CrystalState.Broken;
        Hp = 0;
        var center = Center;
        Effects.Instance?.BreakFx(Material, center, Material.Special ? 2.5f : 1f);
        Effects.Instance?.Flash(center, Material.Glow, Material.Special ? 3f : 1f);
        AudioManager.Instance?.PlayOre(Material, true, -2f);
        if (Material.Rare || Material.Special) AudioManager.Instance?.Play("rare", -4f);

        int shards = _rng.Next(Material.ShardsMin, Material.ShardsMax + 1);
        if (!Material.Special) shards = Math.Max(1, (int)MathF.Round(shards * SizeScale));
        shards = (int)MathF.Round(shards * (GameState.Instance?.EventShardMultiplier ?? 1f));
        if (_sink != null) _sink(Material, shards);
        else
        {
            ShardManager.Instance?.SpawnShards(Material, center, shards);
            GameState.Instance?.RecordCrystalMined();
        }
        _sink = null;

        _visual.Visible = false;
        _shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        _respawnTimer = Material.RespawnSeconds * (0.85f + (float)_rng.NextDouble() * 0.3f);
    }

    void BeginRegrow()
    {
        State = CrystalState.Growing;
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null && IsVisibleInTree() && cam.GlobalPosition.DistanceTo(GlobalPosition) < 12f)
            AudioManager.Instance?.Play("respawn", -10f, 1f, 0.1f);
        var next = PickMaterial?.Invoke(_rng) ?? Material;
        SetMaterial(next);
        _visual.Visible = true;
        _visual.Scale = Vector3.One * 0.01f;
        _visual.Position = Vector3.Zero;
        _flash = 0; _shake = 0;
        SetGlow(_baseEnergy, 0f, 0f);
        var tw = CreateTween();
        tw.TweenProperty(_visual, "scale", Vector3.One, 1.2f).SetTrans(Tween.TransitionType.Elastic).SetEase(Tween.EaseType.Out);
        tw.TweenCallback(Callable.From(() =>
        {
            State = CrystalState.Active;
            _shape.Disabled = false;
        }));
    }

    /// <summary>Força o renascimento imediato (usado por testes).</summary>
    public void DebugRespawnNow() { if (State == CrystalState.Broken) _respawnTimer = 0; }
}
