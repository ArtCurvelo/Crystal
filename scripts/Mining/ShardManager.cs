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
    /// <summary>Altura do chão (relevo das cavernas). Sem ela, usa y = 0.</summary>
    public Func<float, float, float> GroundAt;
    float Floor(float x, float z) => (GroundAt?.Invoke(x, z) ?? 0f) + FloorY;
    public const float Lifetime = 150f;

    /// <summary>Destino de fragmentos soltos além do jogador (ex.: funil).</summary>
    public interface IShardSink
    {
        Vector3 SinkPoint { get; }
        bool AcceptShard(MaterialDef m);
    }

    /// <summary>Procura um destino (funil) perto de um ponto.</summary>
    public Func<Vector3, IShardSink> FindSink;

    public sealed class Shard
    {
        public MeshInstance3D Node;
        public MaterialDef Mat;
        public bool Alive = true;
        public bool Claimed;   // reservado pelo drone
        public IShardSink Sink;
        public Vector3 Position => Node.GlobalPosition;
        public Vector3 Vel;
        public float Age;
        public bool Homing;
        public bool Direct; // habilidade Coleta direta: vem até o jogador de qualquer distância
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
        switch (m.Archetype)
        {
            case OreArchetype.Vein: mat.Metallic = 0.8f; mat.Roughness = 0.3f; mat.EmissionEnergyMultiplier = 0.9f; break;
            case OreArchetype.Pearl: mat.Roughness = 0.1f; mat.RimEnabled = true; mat.Rim = 0.8f; mat.EmissionEnergyMultiplier = 1.1f; break;
            case OreArchetype.Banded: mat.EmissionEnergyMultiplier = 0.7f; break;
            case OreArchetype.Magma: mat.AlbedoColor = new Color(0.15f, 0.08f, 0.06f); mat.EmissionEnergyMultiplier = 2.4f; break;
        }
        _mats[m.Id] = mat;
        return mat;
    }

    static readonly Dictionary<OreArchetype, Mesh> _meshes = new();

    /// <summary>Formato do fragmento que cai, conforme o tipo de minério.</summary>
    public static Mesh DropMesh(MaterialDef m)
    {
        if (_meshes.TryGetValue(m.Archetype, out var mesh)) return mesh;
        mesh = m.Archetype switch
        {
            OreArchetype.Vein => Bake(MeshFactory.Rock(31, 0.45f), 0.11f),                       // pepita
            OreArchetype.Geode => new SphereMesh { Radius = 0.1f, Height = 0.14f, RadialSegments = 7, Rings = 3, IsHemisphere = true },
            OreArchetype.Banded => new BoxMesh { Size = new Vector3(0.24f, 0.06f, 0.15f) },     // placa de pedra
            OreArchetype.Shards => MeshFactory.CrystalPrism(3, 0.04f, 0.34f, 0.65f),            // lasca afiada
            OreArchetype.Ice => new BoxMesh { Size = new Vector3(0.14f, 0.14f, 0.14f) },        // cubo de gelo
            OreArchetype.Magma => Bake(MeshFactory.Rock(17, 0.5f), 0.1f),                       // brasa
            OreArchetype.Coral => new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.04f, Height = 0.28f, RadialSegments = 6, Rings = 1 },
            OreArchetype.Pearl => new SphereMesh { Radius = 0.075f, Height = 0.15f, RadialSegments = 12, Rings = 6 },
            _ => MeshFactory.CrystalPrism(5, 0.09f, 0.26f, 0.4f),
        };
        _meshes[m.Archetype] = mesh;
        return mesh;
    }

    static Mesh Bake(Mesh src, float scale)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.AppendFrom(src, 0, new Transform3D(Basis.Identity.Scaled(Vector3.One * scale), Vector3.Zero));
        return st.Commit();
    }

    public static void ClearCaches() => _meshes.Clear();

    public void SpawnShards(MaterialDef mat, Vector3 origin, int count)
    {
        bool direct = GameState.Instance?.DirectCollect == true;
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
            node.Mesh = DropMesh(mat);
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
                Direct = direct,
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
        s.Alive = false;
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

        _frame++;
        for (int i = _shards.Count - 1; i >= 0; i--)
        {
            var s = _shards[i];
            s.Age += dt;
            var pos = s.Node.GlobalPosition;
            float dist = hasPlayer ? pos.DistanceTo(ppos) : 999f;
            if (s.Claimed) { s.Node.RotateY(s.Spin * dt); continue; } // o drone está vindo buscar
            // funil por perto puxa fragmentos que o jogador não está puxando
            if (s.Sink == null && !s.Homing && FindSink != null && s.Age > 0.6f && (i + _frame) % 6 == 0 && !(hasPlayer && dist < magnet && gs.FreeSpace > 0))
                s.Sink = FindSink(pos);
            if (s.Sink != null)
            {
                var target = s.Sink.SinkPoint;
                var dir = (target - pos).Normalized();
                s.Vel = s.Vel.Lerp(dir * (5f + s.Age * 2f), Mathf.Min(1, dt * 6f));
                pos += s.Vel * dt;
                s.Node.GlobalPosition = pos;
                s.Node.RotateY(s.Spin * dt);
                if (pos.DistanceTo(target) < 0.5f)
                {
                    if (s.Sink.AcceptShard(s.Mat)) { Recycle(i); continue; }
                    s.Sink = null;
                }
                continue;
            }

            if (s.Age > (s.Direct ? 0.35f : 0.45f) && hasPlayer && (dist < magnet || s.Direct))
            {
                if (gs.FreeSpace > 0) s.Homing = true;
                else { blocked = true; s.Homing = false; }
            }
            else if (s.Homing && !s.Direct && dist > magnet * 1.6f) s.Homing = false;

            if (s.Homing)
            {
                var dir = (ppos - pos).Normalized();
                float speed = s.Direct ? 10f + s.Age * 14f : 6f + s.Age * 4f;
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
                var old = pos;
                pos += s.Vel * dt;
                // bateu na parede (o "chão" ali é a rocha alta): volta e quica para dentro da caverna
                float wallTop = Floor(pos.X, pos.Z);
                if (wallTop > pos.Y + 0.3f && wallTop > old.Y)
                {
                    pos.X = old.X; pos.Z = old.Z;
                    s.Vel = new Vector3(-s.Vel.X * 0.4f, s.Vel.Y, -s.Vel.Z * 0.4f);
                }
                float floor = Floor(pos.X, pos.Z);
                if (pos.Y < floor)
                {
                    pos.Y = floor;
                    s.Vel = new Vector3(s.Vel.X * 0.55f, -s.Vel.Y * 0.35f, s.Vel.Z * 0.55f);
                    if (MathF.Abs(s.Vel.Y) < 0.4f) s.Vel.Y = 0;
                }
            }
            s.Node.GlobalPosition = pos;
            s.Node.RotateY(s.Spin * dt);
            // flutuação leve quando parado no chão
            if (!s.Homing && s.Age > 2f && s.Vel.LengthSquared() < 0.01f)
                s.Node.Position = new Vector3(pos.X, Floor(pos.X, pos.Z) + 0.06f + MathF.Sin(s.Age * 3f + i) * 0.04f, pos.Z);

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

    int _frame;

    /// <summary>Fragmento solto mais próximo (não reservado), dentro do raio e que passe no filtro.</summary>
    public Shard NearestLoose(Vector3 from, float radius, Func<Vector3, bool> ok = null)
    {
        Shard best = null;
        float bd = radius * radius;
        foreach (var s in _shards)
        {
            if (s.Claimed || s.Homing || s.Sink != null || s.Age < 0.8f) continue;
            float d = s.Node.GlobalPosition.DistanceSquaredTo(from);
            if (d >= bd || (ok != null && !ok(s.Node.GlobalPosition))) continue;
            bd = d; best = s;
        }
        return best;
    }

    /// <summary>Remove um fragmento reservado (o drone pegou). Devolve o material.</summary>
    public MaterialDef Take(Shard s)
    {
        if (s == null || !s.Alive) return null;
        int i = _shards.IndexOf(s);
        if (i < 0) return null;
        var m = s.Mat;
        Recycle(i);
        return m;
    }

    public IReadOnlyList<Shard> Loose => _shards;

    public void ClearAll()
    {
        for (int i = _shards.Count - 1; i >= 0; i--) Recycle(i);
    }
}
