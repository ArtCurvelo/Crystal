using System;
using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.World;

/// <summary>
/// Vida da Vila dos Ferreiros: moradores que andam por caminhos livres de obstáculos, param na feira, no poço,
/// no quadro, nos bancos e conversam; à noite vão para casa e um vigia com lanterna faz a ronda. Galinhas
/// ciscando e pássaros voando de dia. Ninguém tem colisão (não atrapalha o jogador); eles param e olham
/// quando o jogador está na frente.
/// </summary>
public partial class TownLife : Node3D
{
    public enum Spot { Path, Talk, Bench, Work, Door, Look }

    public sealed class Node
    {
        public Vector3 Pos;
        public Spot Kind;
        public Vector3 Face;      // para onde olhar ao parar
        public string Name;
        public readonly List<int> Links = new();
    }

    public readonly List<Node> Nodes = new();
    public readonly List<Villager> Villagers = new();
    public Func<Vector3> PlayerPos;
    public Func<bool> IsNight;
    readonly Vector3 _c;
    readonly Random _rng = new(9001);
    public int[] Homes;
    readonly List<Humanoid> _vendors = new();
    Villager _watch;
    bool _lastNight;
    float _t;

    public TownLife(Vector3 plazaCenter) { _c = plazaCenter; Name = "TownLife"; }
    public TownLife() : this(Vector3.Zero) { }

    int Add(string name, float x, float z, Spot kind = Spot.Path, Vector3? face = null)
    {
        Nodes.Add(new Node { Name = name, Pos = _c + new Vector3(x, 0, z), Kind = kind, Face = face.HasValue ? _c + face.Value : _c });
        return Nodes.Count - 1;
    }
    void Link(int a, int b) { Nodes[a].Links.Add(b); Nodes[b].Links.Add(a); }
    public int Find(string name) => Nodes.FindIndex(n => n.Name == name);

    /// <summary>Pontos e ligações (todas as ligações passam longe dos obstáculos; conferido por teste).</summary>
    void BuildGraph()
    {
        var ring = new int[12];
        for (int i = 0; i < 12; i++)
        {
            float a = Mathf.Tau * i / 12f;
            ring[i] = Add("R" + i, Mathf.Cos(a) * 7.2f, Mathf.Sin(a) * 7.2f, i % 3 == 1 ? Spot.Look : Spot.Talk, Vector3.Zero);
        }
        for (int i = 0; i < 12; i++) Link(ring[i], ring[(i + 1) % 12]);
        int station = Add("station", -8.4f, 0.6f, Spot.Look, new Vector3(-12f, 1f, 0)); Link(station, ring[6]);
        int smith = Add("smith", -1.2f, -8.4f, Spot.Talk, new Vector3(-1.2f, 0, -11f)); Link(smith, ring[9]);
        int smith2 = Add("smith2", 2.6f, -8.4f, Spot.Look, new Vector3(2.4f, 0, -11f)); Link(smith2, ring[9]); Link(smith2, ring[10]);
        int board = Add("board", 3.0f, 7.7f, Spot.Look, new Vector3(3f, 1.5f, 9f)); Link(board, ring[2]); Link(board, ring[3]);
        int market = Add("market", -9.6f, 7.4f); Link(market, ring[5]);
        int mA = Add("marketA", -11.3f, 10.4f, Spot.Talk, new Vector3(-11.3f, 1f, 12.6f)); Link(mA, market);
        int mB = Add("marketB", -13.0f, 8.4f, Spot.Talk, new Vector3(-15.3f, 1f, 8.4f)); Link(mB, market); Link(mA, mB);
        int well = Add("well", 8.4f, 9.0f, Spot.Work, new Vector3(10.1f, 0.5f, 10.5f)); Link(well, ring[1]);
        int h1 = Add("home1", -4.0f, 15.2f, Spot.Door, new Vector3(-4f, 1f, 17f)); Link(h1, ring[3]);
        int h2 = Add("home2", 7.4f, 14.8f, Spot.Door, new Vector3(7.4f, 1f, 17f)); Link(h2, well);
        int h3 = Add("home3", -12.4f, -11.4f, Spot.Door, new Vector3(-14f, 1f, -13f)); Link(h3, ring[7]);
        int h4 = Add("home4", 12.2f, -11.2f, Spot.Door, new Vector3(13.8f, 1f, -12.8f)); Link(h4, ring[11]);
        int b1 = Add("bench1", -3.95f, 7.6f, Spot.Bench, new Vector3(0, 0, 0)); Link(b1, ring[4]);
        int b2 = Add("bench2", 4.45f, 7.3f, Spot.Bench, new Vector3(0, 0, 0)); Link(b2, ring[2]);
        Homes = new[] { h1, h2, h3, h4 };
    }

    public override void _Ready()
    {
        BuildGraph();
        // moradores (aparência e casa fixas)
        for (int i = 0; i < 8; i++)
        {
            var v = new Villager(this, Look.Random(100 + i), 100 + i) { Home = Homes[i % Homes.Length], Name = "Villager" + i };
            AddChild(v);
            v.Place(Nodes[RandomSpot()].Pos);
            Villagers.Add(v);
            if (i == 2) v.Body.Hold(Props.Basket());
            if (i == 5) v.Body.Hold(Props.Bucket());
        }
        // feirantes atrás das barracas
        foreach (var (pos, yaw, seed) in new[] { (new Vector3(-11.3f, 0, 13.5f), Mathf.Pi, 210), (new Vector3(-16.2f, 0, 8.4f), Mathf.Pi / 2, 211) })
        {
            var lk = Look.Random(seed); lk.Apron = true;
            var h = new Humanoid(lk, seed) { Position = _c + pos, Rotation = new Vector3(0, yaw, 0), Name = "Vendor" + seed };
            AddChild(h);
            _vendors.Add(h);
        }
        // vigia noturno com lanterna
        var wl = Look.Random(300); wl.Hat = 3; wl.Skirt = false; wl.Shirt = new Color(0.25f, 0.28f, 0.35f);
        _watch = new Villager(this, wl, 300) { Home = Homes[2], Name = "Watchman", Watchman = true };
        AddChild(_watch);
        _watch.Body.Hold(Props.Lantern());
        _watch.Place(Nodes[Homes[2]].Pos);
        // galinhas e pássaros
        var cr = new Random(55);
        for (int i = 0; i < 6; i++)
        {
            var home = _c + (i < 4 ? new Vector3(-13.5f, 0, 14.5f) : new Vector3(-5f, 0, 19.5f));
            AddChild(new Chicken(home, 2.2f, 500 + i) { Position = home + new Vector3((float)cr.NextDouble() * 2 - 1, 0, (float)cr.NextDouble() * 2 - 1) });
        }
        AddChild(new Birds(_c + new Vector3(0, 20f, 0), 24f) { Name = "Birds" });
        _lastNight = IsNight?.Invoke() ?? false;
        ApplyNight(_lastNight, true);
    }

    public int RandomSpot()
    {
        while (true)
        {
            int i = _rng.Next(Nodes.Count);
            if (Nodes[i].Kind != Spot.Path && Nodes[i].Kind != Spot.Door) return i;
        }
    }
    public double Rand() => _rng.NextDouble();

    /// <summary>Caminho mais curto (BFS) entre dois pontos.</summary>
    public List<int> Path(int from, int to)
    {
        var prev = new int[Nodes.Count];
        Array.Fill(prev, -2);
        var q = new Queue<int>();
        q.Enqueue(from); prev[from] = -1;
        while (q.Count > 0)
        {
            int n = q.Dequeue();
            if (n == to) break;
            foreach (int m in Nodes[n].Links) if (prev[m] == -2) { prev[m] = n; q.Enqueue(m); }
        }
        var path = new List<int>();
        if (prev[to] == -2) return path;
        for (int n = to; n != -1; n = prev[n]) path.Add(n);
        path.Reverse();
        return path;
    }

    public int Nearest(Vector3 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < Nodes.Count; i++) { float d = Nodes[i].Pos.DistanceSquaredTo(p); if (d < bd) { bd = d; best = i; } }
        return best;
    }

    /// <summary>Quantos moradores estão parados/indo para este ponto (para conversarem em dupla, sem lotar).</summary>
    public int Occupancy(int node, Villager except)
    {
        int n = 0;
        foreach (var v in Villagers) if (v != except && v.Target == node) n++;
        return n;
    }

    void ApplyNight(bool night, bool instant)
    {
        foreach (var v in Villagers) v.SetNight(night, instant);
        _watch.SetNight(!night, instant); // o vigia trabalha ao contrário
        foreach (var l in _watch.Body.FindChildren("Light", "OmniLight3D", true, false)) ((OmniLight3D)l).Visible = night;
        foreach (var h in _vendors) h.Visible = !night;
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _t += (float)delta;
        bool night = IsNight?.Invoke() ?? false;
        if (night != _lastNight) { _lastNight = night; ApplyNight(night, false); }
        foreach (var h in _vendors)
            h.Mode = ((int)(_t / 6f + h.Position.X) % 3) == 0 ? Humanoid.Pose.Talk : Humanoid.Pose.Work;
    }
}

/// <summary>Um morador: escolhe um lugar, anda até lá pelo grafo, fica um tempo (conversa, senta, olha) e repete.</summary>
public partial class Villager : Node3D
{
    readonly TownLife _life;
    public readonly Humanoid Body;
    public int Home;
    public bool Watchman;
    public int Target = -1;
    readonly List<int> _path = new();
    int _at = -1;
    float _wait, _speed, _blocked;
    bool _goingHome, _hidden;
    Vector3 _face;

    public Villager(TownLife life, Look look, int seed)
    {
        _life = life;
        Body = new Humanoid(look, seed);
        AddChild(Body);
        _speed = 1.05f + (seed % 7) * 0.06f;
        Body.WalkSpeed = _speed;
    }
    public Villager() { }

    public void Place(Vector3 p) { Position = p; _at = _life.Nearest(p); _wait = (float)_life.Rand() * 4f; }

    public bool Walking => _path.Count > 0;
    public bool Hidden => _hidden;

    /// <summary>Noite: vai para casa e entra (some). Dia: sai de casa.</summary>
    public void SetNight(bool goHome, bool instant)
    {
        if (goHome)
        {
            if (instant) { Hide(); return; }
            _goingHome = true;
            GoTo(Home);
        }
        else
        {
            _goingHome = false;
            if (_hidden || instant)
            {
                _hidden = false;
                Visible = true;
                Place(_life.Nodes[Home].Pos);
                _wait = (float)_life.Rand() * 6f; // saem de casa aos poucos
                if (instant) Place(_life.Nodes[Watchman ? Home : _life.RandomSpot()].Pos);
            }
        }
    }

    void Hide() { _hidden = true; Visible = false; _path.Clear(); Target = -1; }

    void GoTo(int node)
    {
        _path.Clear();
        if (_at < 0) _at = _life.Nearest(Position);
        var p = _life.Path(_at, node);
        if (p.Count > 0 && p[0] == _at) p.RemoveAt(0);
        _path.AddRange(p);
        Target = node;
        if (_path.Count == 0) Arrive();
    }

    void PickNext()
    {
        if (Watchman) { GoTo(_life.Find("R" + (int)(_life.Rand() * 12))); return; }
        for (int tries = 0; tries < 8; tries++)
        {
            int n = _life.RandomSpot();
            var kind = _life.Nodes[n].Kind;
            int max = kind == TownLife.Spot.Bench ? 1 : kind == TownLife.Spot.Talk ? 2 : 1;
            if (n == _at || _life.Occupancy(n, this) >= max) continue;
            GoTo(n);
            return;
        }
        _wait = 2f;
    }

    void Arrive()
    {
        Target = _at;
        var node = _life.Nodes[_at];
        if (_goingHome && _at == Home) { Hide(); return; }
        _face = node.Face;
        // ponto já ocupado: fica ao lado (para conversar de frente, sem um dentro do outro)
        foreach (var o in _life.Villagers)
            if (o != this && !o.Hidden && !o.Walking && o.Position.DistanceTo(Position) < 0.6f)
            {
                var f = (_face - Position); f.Y = 0;
                var side = f.LengthSquared() > 0.01f ? f.Normalized().Cross(Vector3.Up) : Vector3.Right;
                Position += side * 0.9f;
                break;
            }
        Body.Mode = node.Kind switch
        {
            TownLife.Spot.Bench => Humanoid.Pose.Sit,
            TownLife.Spot.Work => Humanoid.Pose.Work,
            _ => Humanoid.Pose.Idle,
        };
        _wait = Watchman ? 2f + (float)_life.Rand() * 3f : 5f + (float)_life.Rand() * 9f;
        if (node.Kind == TownLife.Spot.Bench) _wait += 8f;
    }

    public override void _Process(double delta)
    {
        if (_hidden || !IsVisibleInTree()) return;
        float dt = Mathf.Min((float)delta, 0.1f);
        if (_path.Count == 0)
        {
            // parado: vira para o ponto de interesse (ou para quem conversa junto)
            var look = _face;
            if (Body.Mode != Humanoid.Pose.Sit && Body.Mode != Humanoid.Pose.Work)
            {
                foreach (var o in _life.Villagers)
                    if (o != this && !o.Walking && !o.Hidden && o.Position.DistanceTo(Position) < 2.2f) { look = o.Position; Body.Mode = Humanoid.Pose.Talk; break; }
            }
            Face(look, dt * 3f);
            _wait -= dt;
            if (_wait <= 0)
            {
                if (_goingHome) GoTo(Home);
                else PickNext();
            }
            return;
        }
        // andando: para se o jogador estiver bem na frente
        var next = _life.Nodes[_path[0]].Pos;
        var to = next - Position; to.Y = 0;
        float dist = to.Length();
        var dir = dist > 0.001f ? to / dist : Vector3.Zero;
        if (_life.PlayerPos != null)
        {
            var pp = _life.PlayerPos(); pp.Y = Position.Y;
            var toP = pp - Position;
            if (toP.Length() < 1.5f && toP.Normalized().Dot(dir) > 0.3f && _blocked < 4f)
            {
                _blocked += dt;
                Body.Mode = Humanoid.Pose.Idle;
                Face(pp, dt * 4f);
                return;
            }
        }
        // outro morador andando muito perto: o de nome "maior" espera um pouco
        foreach (var o in _life.Villagers)
            if (o != this && o.Walking && o.Position.DistanceTo(Position) < 0.7f && string.CompareOrdinal(Name, o.Name) > 0 && _blocked < 1.5f)
            { _blocked += dt; Body.Mode = Humanoid.Pose.Idle; return; }
        _blocked = Mathf.Max(0, _blocked - dt);
        Body.Mode = Humanoid.Pose.Walk;
        float step = _speed * dt;
        if (step >= dist)
        {
            Position = next;
            _at = _path[0];
            _path.RemoveAt(0);
            if (_path.Count == 0) Arrive();
        }
        else
        {
            Position += dir * step;
            Face(Position + dir, dt * 8f);
        }
    }

    void Face(Vector3 p, float k)
    {
        var d = p - Position; d.Y = 0;
        if (d.LengthSquared() < 0.0001f) return;
        float yaw = Mathf.Atan2(d.X, d.Z);
        Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, yaw, Mathf.Min(1f, k)), 0);
    }
}

/// <summary>Objetos que os moradores carregam.</summary>
public static class Props
{
    static MeshInstance3D M(Node3D p, Mesh m, Color c, Vector3 pos, Vector3? rot = null, bool glow = false)
    {
        var mat = glow ? new StandardMaterial3D { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 2.5f } : Humanoid.Mat(c);
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        p.AddChild(mi);
        return mi;
    }

    public static Node3D Basket()
    {
        var n = new Node3D();
        M(n, new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.11f, Height = 0.16f, RadialSegments = 12 }, new Color(0.7f, 0.52f, 0.3f), new Vector3(0, -0.12f, 0));
        foreach (var (x, z, c) in new[] { (-0.05f, 0.03f, new Color(0.85f, 0.15f, 0.12f)), (0.06f, -0.02f, new Color(0.95f, 0.6f, 0.1f)), (0f, -0.06f, new Color(0.4f, 0.7f, 0.2f)) })
            M(n, new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 8, Rings = 4 }, c, new Vector3(x, -0.03f, z));
        M(n, new TorusMesh { InnerRadius = 0.12f, OuterRadius = 0.14f, Rings = 12, RingSegments = 6 }, new Color(0.6f, 0.44f, 0.25f), new Vector3(0, 0.02f, 0), new Vector3(0, 0, Mathf.Pi / 2));
        return n;
    }

    public static Node3D Bucket()
    {
        var n = new Node3D();
        M(n, new CylinderMesh { TopRadius = 0.13f, BottomRadius = 0.1f, Height = 0.22f, RadialSegments = 12 }, new Color(0.5f, 0.36f, 0.22f), new Vector3(0, -0.2f, 0));
        M(n, new CylinderMesh { TopRadius = 0.12f, BottomRadius = 0.12f, Height = 0.01f, RadialSegments = 12 }, new Color(0.3f, 0.5f, 0.7f), new Vector3(0, -0.1f, 0));
        return n;
    }

    public static Node3D Lantern()
    {
        var n = new Node3D();
        M(n, new BoxMesh { Size = new Vector3(0.12f, 0.16f, 0.12f) }, new Color(0.2f, 0.2f, 0.22f), new Vector3(0, -0.12f, 0));
        M(n, new BoxMesh { Size = new Vector3(0.09f, 0.12f, 0.09f) }, new Color(1f, 0.75f, 0.35f), new Vector3(0, -0.12f, 0), null, true);
        n.AddChild(new OmniLight3D { Name = "Light", LightColor = new Color(1f, 0.72f, 0.4f), LightEnergy = 1.3f, OmniRange = 6f, Position = new Vector3(0, -0.1f, 0.1f) });
        return n;
    }
}

/// <summary>Galinha que cisca em volta de um ponto.</summary>
public partial class Chicken : Node3D
{
    readonly Vector3 _home;
    readonly float _radius;
    readonly Random _r;
    Node3D _head;
    Vector3 _target;
    float _wait, _t, _peck;

    public Chicken(Vector3 home, float radius, int seed)
    {
        _home = home; _radius = radius; _r = new Random(seed);
        bool brown = seed % 3 == 0;
        var body = brown ? new Color(0.6f, 0.36f, 0.18f) : new Color(0.95f, 0.93f, 0.88f);
        Part(this, new SphereMesh { Radius = 0.13f, Height = 0.24f, RadialSegments = 10, Rings = 5 }, body, new Vector3(0, 0.2f, 0), null, new Vector3(1f, 0.9f, 1.25f));
        Part(this, new PrismMesh { Size = new Vector3(0.14f, 0.16f, 0.06f) }, body.Darkened(0.15f), new Vector3(0, 0.3f, -0.15f), new Vector3(-0.5f, 0, 0));
        foreach (float x in new[] { -0.04f, 0.04f })
            Part(this, new CylinderMesh { TopRadius = 0.01f, BottomRadius = 0.01f, Height = 0.1f, RadialSegments = 4 }, new Color(0.95f, 0.65f, 0.15f), new Vector3(x, 0.05f, 0));
        _head = new Node3D { Position = new Vector3(0, 0.3f, 0.13f) };
        AddChild(_head);
        Part(_head, new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 8, Rings = 4 }, body, new Vector3(0, 0.04f, 0.02f));
        Part(_head, new PrismMesh { Size = new Vector3(0.03f, 0.05f, 0.04f) }, new Color(0.95f, 0.65f, 0.15f), new Vector3(0, 0.03f, 0.09f), new Vector3(Mathf.Pi / 2, 0, 0));
        Part(_head, new BoxMesh { Size = new Vector3(0.015f, 0.05f, 0.06f) }, new Color(0.85f, 0.12f, 0.1f), new Vector3(0, 0.1f, 0.02f));
        Part(_head, new SphereMesh { Radius = 0.012f, Height = 0.024f }, new Color(0.05f, 0.05f, 0.05f), new Vector3(0.04f, 0.055f, 0.05f));
        Part(_head, new SphereMesh { Radius = 0.012f, Height = 0.024f }, new Color(0.05f, 0.05f, 0.05f), new Vector3(-0.04f, 0.055f, 0.05f));
        _target = home;
    }
    public Chicken() : this(Vector3.Zero, 1f, 1) { }

    static void Part(Node3D p, Mesh m, Color c, Vector3 pos, Vector3? rot = null, Vector3? scale = null)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = Humanoid.Mat(c), Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scale ?? Vector3.One, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        p.AddChild(mi);
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        float dt = (float)delta;
        _t += dt;
        var to = _target - Position; to.Y = 0;
        if (to.Length() > 0.05f && _wait <= 0)
        {
            var d = to.Normalized();
            Position += d * Mathf.Min(to.Length(), dt * 0.55f);
            Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(d.X, d.Z), dt * 8f), 0);
            _head.Position = new Vector3(0, 0.3f, 0.13f + Mathf.Sin(_t * 14f) * 0.025f);
            _head.Rotation = Vector3.Zero;
        }
        else
        {
            _wait -= dt;
            _peck += dt;
            float k = Mathf.Max(0, Mathf.Sin(_peck * 9f));
            _head.Rotation = new Vector3(k * 1.1f, 0, 0);
            if (_wait <= -(1.5f + (float)_r.NextDouble() * 3f))
            {
                _wait = 0;
                float a = (float)_r.NextDouble() * Mathf.Tau, r = (float)_r.NextDouble() * _radius;
                _target = _home + new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
            }
        }
    }
}

/// <summary>Bando de pássaros voando em círculos sobre a vila (só de dia).</summary>
public partial class Birds : Node3D
{
    readonly Vector3 _center;
    readonly float _radius;
    readonly List<(Node3D body, Node3D wl, Node3D wr, float off, float h)> _birds = new();
    float _t;

    public Birds(Vector3 center, float radius)
    {
        _center = center; _radius = radius;
        var mat = Humanoid.Mat(new Color(0.12f, 0.1f, 0.12f));
        for (int i = 0; i < 7; i++)
        {
            var b = new Node3D();
            AddChild(b);
            b.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 6, Rings = 3 }, MaterialOverride = mat, Scale = new Vector3(0.8f, 0.7f, 1.6f) });
            Node3D Wing(int s)
            {
                var w = new Node3D();
                b.AddChild(w);
                w.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.55f, 0.02f, 0.18f) }, MaterialOverride = mat, Position = new Vector3(s * 0.28f, 0, 0) });
                return w;
            }
            _birds.Add((b, Wing(-1), Wing(1), i * 0.45f + (i % 3) * 0.2f, (i % 4) * 1.3f));
        }
    }
    public Birds() : this(Vector3.Zero, 10f) { }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        _t += (float)delta;
        bool night = GameState_Night();
        foreach (var (body, wl, wr, off, h) in _birds)
        {
            body.Visible = !night;
            float a = _t * 0.22f + off;
            float r = _radius + Mathf.Sin(_t * 0.3f + off * 3f) * 4f;
            var p = _center + new Vector3(Mathf.Cos(a) * r, h + Mathf.Sin(_t * 0.7f + off) * 1.5f, Mathf.Sin(a) * r);
            var tangent = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
            body.Position = p;
            body.Rotation = new Vector3(0, Mathf.Atan2(tangent.X, tangent.Z), -0.25f);
            float flap = Mathf.Sin(_t * 9f + off * 5f) * 0.6f;
            wl.Rotation = new Vector3(0, 0, -flap);
            wr.Rotation = new Vector3(0, 0, flap);
        }
    }

    static bool GameState_Night() => Core.GameState.Instance?.DayOver ?? false;
}
