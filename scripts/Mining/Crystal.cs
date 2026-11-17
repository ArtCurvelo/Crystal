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
    static Shader _crystalShader;
    Random _rng;
    float _respawnTimer;
    float _flash;
    float _shake;
    float _baseEnergy = 1.1f;
    float _time;
    Vector3 _shakeDir;
    CpuParticles3D _sparkle;
    OmniLight3D _light;

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
        int count = special ? 9 : _rng.Next(4, 8);
        float s = special ? 2.6f : SizeScale;
        _baseEnergy = mat.Rare ? 1.3f : special ? 1.5f : 0.85f;

        // base rochosa
        var baseRock = new MeshInstance3D
        {
            Mesh = MeshFactory.Rock(_rng.Next(0, 6), 0.3f),
            MaterialOverride = MeshFactory.RockMaterial(new Color(0.22f, 0.2f, 0.28f)),
            Scale = new Vector3(0.55f, 0.28f, 0.55f) * s,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        baseRock.Position = new Vector3(0, 0.05f * s, 0);
        _visual.AddChild(baseRock);

        _crystalShader ??= GD.Load<Shader>("res://shaders/crystal.gdshader");
        var crystalMat = new ShaderMaterial { Shader = _crystalShader };
        crystalMat.SetShaderParameter("base_color", mat.Color);
        crystalMat.SetShaderParameter("glow_color", mat.Glow);
        crystalMat.SetShaderParameter("seed", (float)_rng.NextDouble() * 50f);
        _mats.Add(crystalMat);
        SetGlow(_baseEnergy, 0f, 0f);
        for (int i = 0; i < count; i++)
        {
            float h = (0.6f + (float)_rng.NextDouble() * 0.9f) * s;
            float r = (0.12f + (float)_rng.NextDouble() * 0.1f) * s;
            if (i == 0) { h *= 1.35f; r *= 1.3f; }
            var mi = new MeshInstance3D
            {
                Mesh = MeshFactory.CrystalPrism(6, 1f, 1f, 0.3f),
                MaterialOverride = crystalMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Scale = new Vector3(r, h, r),
            };
            float ang = (float)_rng.NextDouble() * Mathf.Tau;
            float dist = i == 0 ? 0 : (0.12f + (float)_rng.NextDouble() * 0.22f) * s;
            mi.Position = new Vector3(MathF.Cos(ang) * dist, 0.05f * s, MathF.Sin(ang) * dist);
            float tilt = i == 0 ? 0.1f : 0.25f + (float)_rng.NextDouble() * 0.45f;
            // inclina para fora do centro
            mi.Rotation = new Vector3(MathF.Sin(ang) * tilt, (float)_rng.NextDouble() * Mathf.Tau, -MathF.Cos(ang) * tilt);
            _visual.AddChild(mi);
        }

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
            if (Settings.Quality != GraphicsQuality.Low)
            {
                _light = new OmniLight3D
                {
                    LightColor = mat.Glow,
                    LightEnergy = special ? 1.4f : 1.0f,
                    OmniRange = special ? 12f : 4f,
                    ShadowEnabled = false,
                    Position = new Vector3(0, 0.8f * s, 0),
                };
                _visual.AddChild(_light);
            }
        }

        var shape = new CylinderShape3D { Radius = 0.42f * s, Height = 1.5f * s };
        _shape.Shape = shape;
        _shape.Position = new Vector3(0, 0.75f * s, 0);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _time += dt;
        switch (State)
        {
            case CrystalState.Broken:
                _respawnTimer -= dt;
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
    public bool Hit(float damage, Vector3 point, Vector3 normal, bool quiet = false)
    {
        if (State != CrystalState.Active) return false;
        Hp -= damage;
        _flash = 1f;
        _shake = 1f;
        var toCam = normal.LengthSquared() > 0.01f ? normal : Vector3.Up;
        _shakeDir = new Vector3(-toCam.X, 0, -toCam.Z).Normalized();
        if (_shakeDir.LengthSquared() < 0.01f) _shakeDir = Vector3.Right;

        Effects.Instance?.Hit(point, normal, Material.Glow.Lightened(0.3f));
        float pitch = 0.85f + (1f - HpRatio) * 0.5f;
        if (!quiet) AudioManager.Instance?.Play("hit", -4f, pitch, 0.06f);
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
        var center = GlobalPosition + Vector3.Up * 0.7f * (Material.Special ? 2.6f : SizeScale);
        Effects.Instance?.Burst(center, Material.Glow.Lightened(0.2f), Material.Special ? 2.5f : 1f);
        AudioManager.Instance?.Play("break", -2f, Material.Special ? 0.7f : 1f, 0.08f);
        if (Material.Rare || Material.Special) AudioManager.Instance?.Play("rare", -4f);

        int shards = _rng.Next(Material.ShardsMin, Material.ShardsMax + 1);
        if (!Material.Special) shards = Math.Max(1, (int)MathF.Round(shards * SizeScale));
        shards = (int)MathF.Round(shards * (GameState.Instance?.EventShardMultiplier ?? 1f));
        ShardManager.Instance?.SpawnShards(Material, center, shards);
        GameState.Instance?.RecordCrystalMined();

        _visual.Visible = false;
        _shape.SetDeferred(CollisionShape3D.PropertyName.Disabled, true);
        _respawnTimer = Material.RespawnSeconds * (0.85f + (float)_rng.NextDouble() * 0.3f);
    }

    void BeginRegrow()
    {
        State = CrystalState.Growing;
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
