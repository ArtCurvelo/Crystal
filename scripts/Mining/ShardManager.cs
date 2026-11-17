using System;
using System.Collections.Generic;
using CrystalExcavator.Audio;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using CrystalExcavator.World;
using Godot;

namespace CrystalExcavator.Mining;

/// <summary>
/// Gerencia todos os fragmentos soltos em um único loop (mais barato que um nó com script por fragmento).
/// Fragmentos caem, quicam e são atraídos pelo jogador dentro do raio do ímã.
/// </summary>
public partial class ShardManager : Node3D
{
    public static ShardManager Instance { get; private set; }
    public const int MaxShards = 220;
    public const float FloorY = 0.08f;
    public const float Lifetime = 150f;

    sealed class Shard
    {
        public MeshInstance3D Node;
        public MaterialDef Mat;
        public Vector3 Vel;
        public float Age;
        public bool Homing;
        public float Spin;
    }

    readonly List<Shard> _shards = new();
    readonly Stack<MeshInstance3D> _free = new();
    readonly Dictionary<string, StandardMaterial3D> _mats = new();
    readonly Random _rng = new(99);
    public Func<Vector3> PlayerPosition; // centro do corpo do jogador
    float _pickupCombo;
    float _comboTimer;
    float _fullWarnCooldown;

    public int ActiveCount => _shards.Count;
    public event Action InventoryFullBlocked;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    StandardMaterial3D MatFor(MaterialDef m)
    {
        if (_mats.TryGetValue(m.Id, out var mat)) return mat;
        mat = MeshFactory.Emissive(m.Color, m.Glow, 1.6f);
        _mats[m.Id] = mat;
        return mat;
    }

    public void SpawnShards(MaterialDef mat, Vector3 origin, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (_shards.Count >= MaxShards)
            {
                // coleta automática do mais antigo para não perder valor
                var old = _shards[0];
                GameState.Instance?.AddItem(old.Mat.Id);
                Recycle(0);
            }
            MeshInstance3D node = _free.Count > 0 ? _free.Pop() : CreateNode();
            node.MaterialOverride = MatFor(mat);
            node.Visible = true;
            node.Scale = Vector3.One * (mat.Rare || mat.Special ? 1.4f : 1f);
            node.GlobalPosition = origin + new Vector3(Rand(-0.2f, 0.2f), Rand(0, 0.3f), Rand(-0.2f, 0.2f));
            float a = Rand(0, Mathf.Tau);
            float sp = Rand(1.5f, mat.Special ? 6f : 3.5f);
            _shards.Add(new Shard
            {
                Node = node, Mat = mat,
                Vel = new Vector3(MathF.Cos(a) * sp, Rand(3f, 6f), MathF.Sin(a) * sp),
                Spin = Rand(-8f, 8f),
            });
        }
    }

    MeshInstance3D CreateNode()
    {
        var n = new MeshInstance3D
        {
            Mesh = MeshFactory.CrystalPrism(5, 0.09f, 0.26f, 0.4f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(n);
        return n;
    }

    float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

    void Recycle(int index)
    {
        var s = _shards[index];
        s.Node.Visible = false;
        _free.Push(s.Node);
        _shards.RemoveAt(index);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _comboTimer -= dt;
        _fullWarnCooldown -= dt;
        if (_comboTimer <= 0) _pickupCombo = 0;
        var gs = GameState.Instance;
        if (gs == null) return;
        bool hasPlayer = PlayerPosition != null;
        Vector3 ppos = hasPlayer ? PlayerPosition() : Vector3.Zero;
        float magnet = gs.MagnetRadius;
        bool blocked = false;

        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            var s = _shards[i];
            s.Age += dt;
            var pos = s.Node.GlobalPosition;
            float dist = hasPlayer ? pos.DistanceTo(ppos) : 999f;

            if (s.Age > 0.45f && hasPlayer && dist < magnet)
            {
                if (gs.FreeSpace > 0) s.Homing = true;
                else { blocked = true; s.Homing = false; }
            }
            else if (s.Homing && dist > magnet * 1.6f) s.Homing = false;

            if (s.Homing)
            {
                var dir = (ppos - pos).Normalized();
                float speed = 6f + s.Age * 4f;
                s.Vel = s.Vel.Lerp(dir * speed, Mathf.Min(1, dt * 8f));
                pos += s.Vel * dt;
                if (pos.DistanceTo(ppos) < 0.65f)
                {
                    if (gs.AddItem(s.Mat.Id) > 0)
                    {
                        _pickupCombo += 1;
                        _comboTimer = 0.6f;
                        AudioManager.Instance?.Play("pickup", -10f, 1f + Mathf.Min(_pickupCombo, 16) * 0.05f, 0.02f);
                        Recycle(i);
                        continue;
                    }
                    s.Homing = false;
                    blocked = true;
                }
            }
            else
            {
                s.Vel += Vector3.Down * 14f * dt;
                pos += s.Vel * dt;
                if (pos.Y < FloorY)
                {
                    pos.Y = FloorY;
                    s.Vel = new Vector3(s.Vel.X * 0.55f, -s.Vel.Y * 0.35f, s.Vel.Z * 0.55f);
                    if (MathF.Abs(s.Vel.Y) < 0.4f) s.Vel.Y = 0;
                }
            }
            s.Node.GlobalPosition = pos;
            s.Node.RotateY(s.Spin * dt);
            // flutuação leve quando parado no chão
            if (!s.Homing && s.Age > 2f && s.Vel.LengthSquared() < 0.01f)
                s.Node.Position = new Vector3(pos.X, FloorY + 0.06f + MathF.Sin(s.Age * 3f + i) * 0.04f, pos.Z);

            if (s.Age > Lifetime)
            {
                Recycle(i);
            }
        }

        if (blocked && _fullWarnCooldown <= 0)
        {
            _fullWarnCooldown = 4f;
            InventoryFullBlocked?.Invoke();
        }
    }

    public void ClearAll()
    {
        for (int i = _shards.Count - 1; i >= 0; i--) Recycle(i);
    }
}
