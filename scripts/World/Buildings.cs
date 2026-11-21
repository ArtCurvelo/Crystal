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
/// Construções do jogador: cordas, postes de luz, esteiras (do início marcado até a Estação de Venda
/// ou até outra esteira), funis ao lado das esteiras, drones e robôs mineradores que trabalham numa área.
/// Também controla o modo construção (prévia, colocar, arrastar a esteira, desmontar).
/// </summary>
public partial class BuildingManager : Node3D
{
    public static BuildingManager Instance { get; private set; }
    public WorldBuilder World;
    public Func<PlayerController> GetPlayer;
    /// <summary>Pode construir agora (jogando, na mina, sem menus abertos).</summary>
    public Func<bool> CanUseBuildMode;
    /// <summary>Texto do modo construção para o HUD (null = esconder).</summary>
    public event Action<string, string, bool> BuildBarChanged;

    readonly Dictionary<int, Node3D> _nodes = new();
    readonly Dictionary<int, Belt> _belts = new();
    bool _bulk;

    public override void _EnterTree() => Instance = this;
    public override void _ExitTree() { if (Instance == this) Instance = null; }

    // ======================================================================= estado salvo → cena
    /// <summary>Recria todas as construções a partir do save (início da partida, renascimento).</summary>
    public void Rebuild()
    {
        SetActive(false);
        foreach (var n in _nodes.Values) n.QueueFree();
        _nodes.Clear();
        foreach (var b in _belts.Values) b.QueueFree();
        _belts.Clear();
        var gs = GameState.Instance;
        foreach (var L in World.Layouts) L.ExtraRopes.Clear();
        Migrate(gs);
        _bulk = true;
        long refund = 0;
        // esteiras primeiro: funis e outras esteiras se ligam a elas
        foreach (var b in gs.Data.Buildings.OrderBy(b => b.Type == BuildIds.Belt ? 0 : 1).ThenBy(b => b.Id).ToList())
        {
            // o mapa pode mudar entre versões: construção que ficou dentro da rocha é devolvida
            bool bad = !World.IsSafeSpot(new Vector3(b.X, b.Y, b.Z)) || (b.Type == BuildIds.Belt && (b.Path == null || b.Path.Count < 6));
            if (bad)
            {
                foreach (var kv in b.Queue)
                    if (GameData.Mat(kv.Key) is { } m) gs.SellFromBelt(m, kv.Value);
                gs.Data.Buildings.Remove(b);
                refund += b.Paid;
                continue;
            }
            Spawn(b);
        }
        // quem despejava numa esteira que não existe mais fica sem saída
        foreach (var b in gs.Data.Buildings)
            if (b.Feeds > 0 && !_belts.ContainsKey(b.Feeds)) b.Feeds = -1;
        _bulk = false;
        foreach (var L in World.Layouts) L.Reflood();
        if (refund > 0)
        {
            gs.AddMoney(refund);
            gs.Notify("Construções devolvidas", Loc.F("O mapa mudou nesta versão; o valor foi devolvido: {0}", GameState.FormatMoney(refund)), NotifyKind.Info);
        }
    }

    /// <summary>
    /// Saves antigos: o funil tinha esteira automática até a venda (vira funil + esteira separados)
    /// e o drone seguia o jogador (agora fica numa base; o valor é devolvido para colocar de novo).
    /// </summary>
    void Migrate(GameState gs)
    {
        var d = gs.Data;
        bool hoppers = false;
        foreach (var h in d.Buildings.Where(b => b.Ver == 0).ToList())
        {
            h.Ver = 1;
            if (h.Type != BuildIds.Hopper) continue;
            hoppers = true;
            h.Feeds = -1;
            var route = RouteToSell(new Vector3(h.X, h.Y, h.Z));
            if (route == null) continue;
            long beltPaid = Math.Min(h.Paid, BeltCost(PathLength(route)));
            h.Paid -= beltPaid;
            var belt = new BuildingSave
            {
                Id = d.NextBuildingId++, Type = BuildIds.Belt, X = route[0].X, Y = route[0].Y, Z = route[0].Z,
                Path = Flatten(route), Feeds = 0, Paid = beltPaid, Ver = 1,
            };
            d.Buildings.Add(belt);
            h.Feeds = belt.Id;
            h.JoinS = 0;
        }
        bool drone = d.DroneOwned;
        if (drone)
        {
            d.DroneOwned = false;
            d.Money += BuildData.Of(BuildIds.Drone).Cost;
        }
        void Grant(string id) { if (!d.Skills.Contains(id)) d.Skills.Add(id); }
        if (hoppers || drone) { Grant(SkillIds.BuildBelt); Grant(SkillIds.BuildHopper); }
        if (drone) Grant(SkillIds.BuildDrone);
        if (hoppers || drone)
            gs.Notify("Construções atualizadas", drone
                ? Loc.T("O drone agora trabalha numa área: coloque a base dele de novo (B). O valor foi devolvido.")
                : Loc.T("Funil e esteira agora são separados: a esteira foi mantida."), NotifyKind.Info);
    }

    public static List<float> Flatten(List<Vector3> pts)
    {
        var f = new List<float>(pts.Count * 3);
        foreach (var p in pts) { f.Add(p.X); f.Add(p.Y); f.Add(p.Z); }
        return f;
    }

    public static List<Vector3> Unflatten(List<float> f)
    {
        var pts = new List<Vector3>();
        if (f == null) return pts;
        for (int i = 0; i + 2 < f.Count; i += 3) pts.Add(new Vector3(f[i], f[i + 1], f[i + 2]));
        return pts;
    }

    void Spawn(BuildingSave b)
    {
        var pos = new Vector3(b.X, b.Y, b.Z);
        Node3D node;
        switch (b.Type)
        {
            case BuildIds.Rope:
            {
                var rope = new Rope { BuildingId = b.Id, Top = pos, Bottom = new Vector3(b.BX, b.BY, b.BZ), TopExit = new Vector3(b.EX, b.EY, b.EZ), Name = $"Building_{b.Id}" };
                AddChild(rope);
                rope.SetMeta("building_id", b.Id);
                _nodes[b.Id] = rope;
                var Lr = World.LayoutAt(pos);
                Lr.ExtraRopes.Add((rope.Top, rope.Bottom));
                if (!_bulk) Lr.Reflood();
                return;
            }
            case BuildIds.Belt:
            {
                var pts = Unflatten(b.Path);
                var belt = new Belt { BuildingId = b.Id, Accent = new Color(0.3f, 1f, 0.8f), GroundAt = World.GroundAt, ToStation = b.Feeds == 0, Name = $"Belt_{b.Id}" };
                AddChild(belt);
                belt.Build(pts);
                _belts[b.Id] = belt;
                var dir = pts[1] - pts[0];
                b.Yaw = Mathf.Atan2(dir.X, dir.Z);
                node = new BeltIntake { BuildingId = b.Id, Manager = this };
                break;
            }
            case BuildIds.Light:
            {
                var L = World.LayoutAt(pos);
                float clearance = L.InGrid(pos.X, pos.Z) ? L.Ceiling(pos.X, pos.Z) - L.Ground(pos.X, pos.Z) : 4f;
                node = new LightPost { BuildingId = b.Id, Height = Mathf.Clamp(clearance - 0.5f, 2.0f, 3.2f), Tint = WorldBuilder.RegionAt(pos).AccentColor };
                break;
            }
            case BuildIds.Drone:
            case BuildIds.Robot:
                node = new Dock { BuildingId = b.Id, Manager = this, IsRobot = b.Type == BuildIds.Robot, RegionIndex = WorldBuilder.RegionAt(pos).Index };
                break;
            default:
                node = new Hopper { BuildingId = b.Id, Manager = this };
                break;
        }
        node.Name = $"Building_{b.Id}";
        AddChild(node);
        node.GlobalPosition = pos;
        node.Rotation = new Vector3(0, b.Yaw, 0);
        node.SetMeta("building_id", b.Id);
        _nodes[b.Id] = node;
    }

    /// <summary>Posição de referência (a corda fica na origem e usa coordenadas absolutas).</summary>
    static Vector3 PosOf(Node3D n) => n is Rope r ? r.Top : n.GlobalPosition;
    public IEnumerable<Hopper> Hoppers => _nodes.Values.OfType<Hopper>();
    public IEnumerable<Dock> Docks => _nodes.Values.OfType<Dock>();
    public Belt BeltOf(int id) => _belts.TryGetValue(id, out var b) ? b : null;
    public Node3D NodeOf(int id) => _nodes.TryGetValue(id, out var n) ? n : null;
    public int NodeCount => _nodes.Count;
    /// <summary>Primeiro drone (testes e atalhos).</summary>
    public DroneBot Drone => Docks.Select(d => d.Unit).OfType<DroneBot>().FirstOrDefault();

    /// <summary>Esconde construções longe do jogador (mesma regra das regiões) e tudo dentro da casa.</summary>
    public void UpdateVisibility(int playerRegion, bool inHouse)
    {
        foreach (var n in _nodes.Values)
        {
            bool v = !inHouse && Mathf.Abs(WorldBuilder.RegionAt(PosOf(n)).Index - playerRegion) <= 1;
            if (n.Visible != v) n.Visible = v;
        }
        foreach (var b in _belts.Values) b.Visible = !inHouse;
    }

    /// <summary>Antes de salvar: o que está andando na esteira e a carga dos drones/robôs vão para as filas.</summary>
    public void WriteInFlight(SaveData d)
    {
        foreach (var b in d.Buildings)
        {
            b.InFlight = new Dictionary<string, int>();
            if (_belts.TryGetValue(b.Id, out var belt))
                foreach (var m in belt.Items) b.InFlight[m.Id] = (b.InFlight.TryGetValue(m.Id, out int c) ? c : 0) + 1;
            if (_nodes.TryGetValue(b.Id, out var n) && n is Dock dock)
                foreach (var m in dock.Unit?.TakeCargo() ?? new List<MaterialDef>()) b.Queue[m.Id] = (b.Queue.TryGetValue(m.Id, out int c) ? c : 0) + 1;
        }
    }

    /// <summary>Funil mais próximo que puxa fragmentos soltos neste ponto.</summary>
    public ShardManager.IShardSink FindSink(Vector3 p)
    {
        Hopper best = null;
        float bd = BuildData.HopperPullRadius * BuildData.HopperPullRadius;
        foreach (var n in _nodes.Values)
            if (n is Hopper h && h.Visible)
            {
                float d = h.GlobalPosition.DistanceSquaredTo(p);
                if (d < bd) { bd = d; best = h; }
            }
        return best;
    }

    /// <summary>
    /// Onde um drone/robô entrega: o funil ou a entrada de esteira mais perto da base, dentro da área.
    /// Devolve o id da construção (0 = nenhum) e o ponto de entrega.
    /// </summary>
    public int NearestIntake(Vector3 basePos, float range, out Vector3 at)
    {
        at = default;
        int best = 0; float bd = (range + 4f) * (range + 4f);
        foreach (var n in _nodes.Values)
        {
            if (n is not (Hopper or BeltIntake)) continue;
            if (n is Hopper h && !HasOutlet(h.BuildingId)) continue;
            float d = n.GlobalPosition.DistanceSquaredTo(basePos);
            if (d < bd) { bd = d; best = (int)n.GetMeta("building_id"); at = n.GlobalPosition; }
        }
        return best;
    }

    /// <summary>A construção (esteira ou funil) tem para onde mandar os fragmentos?</summary>
    public bool HasOutlet(int id)
    {
        var b = GameState.Instance?.Building(id);
        if (b == null) return false;
        if (b.Type == BuildIds.Belt) return b.Feeds == 0 || (b.Feeds > 0 && _belts.ContainsKey(b.Feeds));
        return b.Feeds > 0 && _belts.ContainsKey(b.Feeds);
    }

    /// <summary>Metros de esteira até a Estação de Venda a partir desta construção (-1 = sem saída).</summary>
    public float DistanceToSell(int id)
    {
        var gs = GameState.Instance;
        var b = gs?.Building(id);
        if (b == null) return -1;
        float total = 0, from = 0;
        if (b.Type != BuildIds.Belt)
        {
            // funil: entra na esteira dele no ponto JoinS
            if (b.Feeds <= 0) return -1;
            from = b.JoinS;
            b = gs.Building(b.Feeds);
        }
        for (int guard = 0; guard < 64 && b != null; guard++)
        {
            if (!_belts.TryGetValue(b.Id, out var belt)) return -1;
            total += Mathf.Max(0, belt.Length - from);
            if (b.Feeds == 0) return total;
            if (b.Feeds < 0) return -1;
            from = b.JoinS;
            b = gs.Building(b.Feeds);
        }
        return -1;
    }

    // ======================================================================= trajeto da esteira
    /// <summary>Ponto ao lado da Estação de Venda onde a esteira chega.</summary>
    public Vector3 SellIntake => World.SellStation.GlobalPosition + World.SellStation.GlobalBasis.X * 2.6f;
    Vector3 SellMouth => World.SellStation.GlobalPosition + World.SellStation.GlobalBasis.X * 1.25f;

    /// <summary>Trajeto de um ponto até a Estação de Venda (atravessa as regiões pelos túneis).</summary>
    public List<Vector3> RouteToSell(Vector3 from)
    {
        if (World?.SellStation == null) return null;
        var pts = RouteBetween(from, SellIntake);
        if (pts == null) return null;
        pts.Add(SellMouth);
        return Simplify(pts);
    }

    /// <summary>
    /// Caminho livre para a esteira entre dois pontos: em cada região contorna paredes, depósitos,
    /// construções e pedras; entre regiões segue reto pelo túnel (chão em y = 0).
    /// </summary>
    public List<Vector3> RouteBetween(Vector3 from, Vector3 to)
    {
        int r = WorldBuilder.RegionAt(from).Index, kb = WorldBuilder.RegionAt(to).Index;
        if (r < 0 || r >= World.Layouts.Count || kb < 0 || kb >= World.Layouts.Count) return null;
        var pts = new List<Vector3>();
        var cur = from;
        for (int guard = 0; guard < 16; guard++)
        {
            var L = World.Layouts[r];
            if (!L.InGrid(cur.X, cur.Z)) return null;
            Vector3 goal = r == kb ? to : kb < r ? L.Entrance : L.Exit;
            if (!L.InGrid(goal.X, goal.Z)) return null;
            var occupied = Occupied(L, cur, goal);
            var path = L.FindClearPath(cur, goal, id => occupied.Contains(id));
            if (path == null) return null;
            pts.AddRange(Straighten(L, path, occupied));
            if (r == kb)
            {
                if (to.DistanceTo(pts[^1]) > 0.3f) pts.Add(new Vector3(to.X, L.Ground(to.X, to.Z), to.Z));
                // começa exatamente no ponto marcado (a entrada fica ali)
                var L0 = World.LayoutAt(from);
                if (from.DistanceTo(pts[0]) > 0.05f) pts.Insert(0, new Vector3(from.X, L0.Ground(from.X, from.Z), from.Z));
                return Simplify(pts);
            }
            if (kb < r)
            {
                var prev = World.Layouts[r - 1];
                pts.Add(new Vector3(0, L.Ground(0, L.Entrance.Z), L.Entrance.Z));
                pts.Add(new Vector3(0, 0, L.Z0 + L.Nz - 0.5f));
                pts.Add(new Vector3(0, 0, prev.Z0 + 0.5f));
                cur = prev.Exit;
                pts.Add(prev.OnGround(cur.X, cur.Z));
                r--;
            }
            else
            {
                var next = World.Layouts[r + 1];
                pts.Add(new Vector3(0, L.Ground(0, L.Exit.Z), L.Exit.Z));
                pts.Add(new Vector3(0, 0, L.Z0 + 0.5f));
                pts.Add(new Vector3(0, 0, next.Z0 + next.Nz - 0.5f));
                cur = next.Entrance;
                pts.Add(next.OnGround(cur.X, cur.Z));
                r++;
            }
        }
        return null;
    }

    /// <summary>
    /// Onde a esteira termina mirando em <paramref name="aim"/>: na Estação de Venda (feeds 0),
    /// encostada em outra esteira (feeds = id dela, joinS = ponto de entrada) ou solta (feeds -1).
    /// </summary>
    public void BeltEnd(Vector3 aim, out Vector3 end, out int feeds, out float joinS, int ignore = 0)
    {
        joinS = 0;
        var st = World.SellStation;
        if (st != null && (aim.DistanceTo(SellIntake) < BuildData.StationSnap || aim.DistanceTo(st.GlobalPosition) < BuildData.StationSnap))
        {
            end = SellIntake; feeds = 0;
            return;
        }
        feeds = -1; end = aim;
        float bd = BuildData.BeltSnap;
        foreach (var kv in _belts)
        {
            if (kv.Key == ignore) continue;
            float d = kv.Value.Nearest(aim, out float s, out var q);
            if (d < bd) { bd = d; feeds = kv.Key; joinS = s; end = q; }
        }
    }

    /// <summary>Esteira mais perto (no plano) de um ponto, até <paramref name="maxDist"/>. 0 = nenhuma.</summary>
    public int NearestBelt(Vector3 p, float maxDist, out float dist, out float s)
    {
        int best = 0; dist = maxDist; s = 0;
        foreach (var kv in _belts)
        {
            float d = kv.Value.Nearest(p, out float ss, out _);
            if (d < dist) { dist = d; best = kv.Key; s = ss; }
        }
        return best;
    }

    /// <summary>Células ocupadas para a esteira: depósitos, construções, estações e acampamentos (menos perto das pontas).</summary>
    HashSet<int> Occupied(CaveLayout L, Vector3 start, Vector3 goal)
    {
        var set = new HashSet<int>();
        void Mark(Vector3 p, float rad)
        {
            if (!L.InGrid(p.X, p.Z)) return;
            int r = Mathf.CeilToInt(rad);
            int ci = Mathf.RoundToInt(p.X - L.X0), cj = Mathf.RoundToInt(p.Z - L.Z0);
            for (int j = cj - r; j <= cj + r; j++)
                for (int i = ci - r; i <= ci + r; i++)
                {
                    if (i < 0 || j < 0 || i >= L.Nx || j >= L.Nz) continue;
                    var c = new Vector3(L.X0 + i, 0, L.Z0 + j);
                    if (new Vector2(c.X - p.X, c.Z - p.Z).Length() > rad) continue;
                    if (new Vector2(c.X - start.X, c.Z - start.Z).Length() < 1.6f || new Vector2(c.X - goal.X, c.Z - goal.Z).Length() < 1.6f) continue;
                    set.Add(L.Idx(i, j));
                }
        }
        foreach (var c in World.Crystals) Mark(c.GlobalPosition, c.Material.Special ? c.Material.SpecialScale * 0.6f + 1f : 1.3f);
        foreach (var n in _nodes.Values) Mark(PosOf(n), 1.6f);
        foreach (var st in new Node3D[] { World.SellStation, World.Elevator }) if (st != null) Mark(st.GlobalPosition, 2.2f);
        foreach (var camp in World.GoblinCamps) { Mark(camp.HutPos, 2.6f); Mark(camp.ChestPos, 1.4f); }
        if (L.Blocked != null)
            for (int id = 0; id < L.Blocked.Length; id++)
                if (L.Blocked[id])
                {
                    // obstáculo do cenário: folga extra em volta
                    int i = id % L.Nx, j = id / L.Nx;
                    for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                    {
                        int ni = i + di, nj = j + dj;
                        if (ni >= 0 && nj >= 0 && ni < L.Nx && nj < L.Nz) set.Add(L.Idx(ni, nj));
                    }
                }
        return set;
    }

    /// <summary>Junta pontos em retas só quando a reta inteira está livre (sem cortar quinas, obstáculos ou relevo).</summary>
    static List<Vector3> Straighten(CaveLayout L, List<Vector3> path, HashSet<int> occupied)
    {
        bool Clear(Vector3 a, Vector3 b)
        {
            float len = a.DistanceTo(b);
            for (float t = 0; t <= len; t += 0.25f)
            {
                var q = a.Lerp(b, len < 0.01f ? 0 : t / len);
                if (L.WallDist(q.X, q.Z) > -0.9f) return false;
                int ci = Mathf.RoundToInt(q.X - L.X0), cj = Mathf.RoundToInt(q.Z - L.Z0);
                if (ci < 0 || cj < 0 || ci >= L.Nx || cj >= L.Nz) return false;
                int id = L.Idx(ci, cj);
                if (!L.Reach[id] || occupied.Contains(id)) return false;
                if (Mathf.Abs(L.Ground(q.X, q.Z) - q.Y) > 0.3f) return false; // a reta não pode entrar no chão nem flutuar
            }
            return true;
        }
        var outp = new List<Vector3> { path[0] };
        int i0 = 0;
        while (i0 < path.Count - 1)
        {
            int best = i0 + 1;
            for (int j = Mathf.Min(path.Count - 1, i0 + 24); j > i0 + 1; j--)
                if (Clear(path[i0], path[j])) { best = j; break; }
            outp.Add(path[best]);
            i0 = best;
        }
        return outp;
    }


    static List<Vector3> Simplify(List<Vector3> pts)
    {
        var outp = new List<Vector3>();
        foreach (var p in pts)
        {
            if (outp.Count > 0 && outp[^1].DistanceTo(p) < 0.3f) continue;
            if (outp.Count >= 2)
            {
                var a = outp[^2]; var b = outp[^1];
                var d1 = new Vector2(b.X - a.X, b.Z - a.Z); var d2 = new Vector2(p.X - b.X, p.Z - b.Z);
                // segue reto: junta pontos alinhados (sem esticar demais para seguir o relevo)
                if (d1.LengthSquared() > 0.01f && d2.LengthSquared() > 0.01f && d1.Normalized().Dot(d2.Normalized()) > 0.995f
                    && a.DistanceTo(p) < 5f && Mathf.Abs((b.Y - a.Y) / d1.Length() - (p.Y - b.Y) / d2.Length()) < 0.08f)
                { outp[^1] = p; continue; }
            }
            outp.Add(p);
        }
        return outp;
    }

    public static float PathLength(List<Vector3> pts)
    {
        float len = 0;
        for (int i = 1; i < pts.Count; i++) len += pts[i - 1].DistanceTo(pts[i]);
        return len;
    }


    public static long BeltCost(float length) => (long)Mathf.Ceil(length) * BuildData.BeltCostPerMeter;

    // ======================================================================= esteiras andando
    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var gs = GameState.Instance;
        if (gs == null) return;
        foreach (var kv in _belts)
        {
            var belt = kv.Value;
            var b = gs.Building(kv.Key);
            if (b == null) continue;
            Belt target = b.Feeds > 0 && _belts.TryGetValue(b.Feeds, out var t) ? t : null;
            bool open = b.Feeds == 0 || target != null;
            belt.Feed -= dt;
            if (open && belt.Feed <= 0 && b.Queue.Count > 0)
            {
                belt.Feed = BuildData.BeltInterval;
                var id = gs.HopperTake(kv.Key);
                if (id != null && !belt.Push(GameData.Mat(id))) gs.HopperAdd(kv.Key, id);
            }
            foreach (var m in belt.Advance(dt * BuildData.BeltSpeed))
            {
                if (b.Feeds == 0) gs.SellFromBelt(m);
                else if (target == null || !target.Push(m, b.JoinS)) gs.HopperAdd(kv.Key, m.Id); // sem saída: espera na fila
            }
        }
        // funis despejam na esteira ao lado
        foreach (var n in _nodes.Values)
        {
            if (n is not Hopper h) continue;
            var b = gs.Building(h.BuildingId);
            if (b == null || b.Feeds <= 0 || b.Queue.Count == 0 || !_belts.TryGetValue(b.Feeds, out var belt)) continue;
            h.Feed -= dt;
            if (h.Feed > 0) continue;
            h.Feed = BuildData.BeltInterval;
            var id = gs.HopperTake(h.BuildingId);
            if (id != null && !belt.Push(GameData.Mat(id), b.JoinS)) gs.HopperAdd(h.BuildingId, id);
        }
        if (Active) UpdateBuildMode(dt);
        else if (CanUseBuildMode != null && CanUseBuildMode() && Input.IsActionJustPressed("build")) SetActive(true);
    }

    // ======================================================================= modo construção
    public bool Active { get; private set; }
    public int Selected { get; private set; }
    /// <summary>Itens liberados (corda e poste sempre; o resto pelo ramo Construção da árvore).</summary>
    public List<BuildDef> Available
    {
        get
        {
            var gs = GameState.Instance;
            return BuildData.All.Where(d => gs == null || gs.BuildUnlocked(d.Id)).ToList();
        }
    }
    public BuildDef Current { get { var a = Available; return a[Mathf.Clamp(Selected, 0, a.Count - 1)]; } }
    Node3D _ghost;
    string _ghostType;
    MeshInstance3D _routeLine;
    StandardMaterial3D _ghostOk, _ghostBad;
    bool _valid;
    string _reason = "";
    Vector3 _point, _rawPoint;
    bool _hasRaw;
    float _yaw;
    List<Vector3> _route;
    Vector3 _routeFrom = new(9999, 0, 9999);
    float _routeTimer;
    int _aimBuilding;
    PhysicsRayQueryParameters3D _ray;
    // esteira: início marcado e ponta
    Vector3? _beltStart;
    bool _startedThisPress;
    Vector3 _beltEnd;
    int _beltFeeds = -1;
    float _beltJoin;
    // funil: esteira ao lado
    int _hopperBelt;
    float _hopperJoin;

    public Vector3? BeltStart => _beltStart;
    /// <summary>Por que a prévia atual não pode ser construída (vazio = pode).</summary>
    public string Reason => _valid ? "" : _reason;
    public int BeltEndFeeds => _beltFeeds;

    public void SetActive(bool on)
    {
        if (Active == on) return;
        Active = on;
        var p = GetPlayer?.Invoke();
        if (p != null) p.BuildMode = on;
        if (_ghost != null) _ghost.Visible = false;
        if (_routeLine != null) _routeLine.Visible = false;
        ResetBelt();
        foreach (var d in Docks) d.ShowArea(on);
        AudioManager.Instance?.Play("click", -4f, on ? 1.2f : 0.9f);
        if (on) Accessibility.Speak(Loc.T("Modo construção") + ". " + Current.Name, true);
        if (!on) BuildBarChanged?.Invoke(null, null, false);
    }

    void ResetBelt()
    {
        _beltStart = null;
        _startedThisPress = false;
        _route = null; _routeFrom = new Vector3(9999, 0, 9999);
    }

    public void Select(int index)
    {
        int n = Available.Count;
        Selected = ((index % n) + n) % n;
        if (_ghost != null) { _ghost.QueueFree(); _ghost = null; }
        ResetBelt();
        if (Active) Accessibility.Speak(Current.Name + ", " + GameState.FormatMoney(Current.Cost), true);
    }

    /// <summary>Seleciona um item pelo id (se estiver liberado).</summary>
    public bool SelectId(string id)
    {
        int i = Available.FindIndex(d => d.Id == id);
        if (i < 0) return false;
        Select(i);
        return true;
    }

    void UpdateBuildMode(float dt)
    {
        var p = GetPlayer?.Invoke();
        if (p == null || CanUseBuildMode == null || !CanUseBuildMode()) { SetActive(false); return; }
        if (Input.IsActionJustPressed("build") || Input.IsActionJustPressed("pause")) { SetActive(false); return; }
        if (Input.IsActionJustPressed("build_next")) { Select(Selected + 1); AudioManager.Instance?.Play("click", -8f, 1.1f); }
        if (Input.IsActionJustPressed("build_prev")) { Select(Selected - 1); AudioManager.Instance?.Play("click", -8f, 0.95f); }
        Aim(p);
        Evaluate(p, dt);
        UpdateGhost();
        bool belt = Current.Id == BuildIds.Belt;
        if (belt && _beltStart != null && Input.IsActionJustPressed("interact"))
        {
            ResetBelt();
            AudioManager.Instance?.Play("click", -6f, 0.8f);
        }
        else if (Input.IsActionJustPressed("interact") && _aimBuilding > 0) Demolish(_aimBuilding);
        else if (Input.IsActionJustPressed("mine")) TryPlaceCurrent(p, true);
        else if (belt && _startedThisPress && Input.IsActionJustReleased("mine"))
        {
            // arrastou com o botão apertado: solta e a esteira é construída
            _startedThisPress = false;
            if (_route != null && PathLength(_route) >= BuildData.BeltMinLength + 1f) TryPlaceCurrent(p, false);
        }
        PublishBar();
    }

    bool _hasPoint;

    void Aim(PlayerController p)
    {
        _aimBuilding = 0;
        _hasPoint = false;
        _hasRaw = false;
        var space = GetWorld3D().DirectSpaceState;
        var from = p.Camera.GlobalPosition;
        var dir = -p.Camera.GlobalTransform.Basis.Z;
        _ray ??= new PhysicsRayQueryParameters3D { CollideWithAreas = true };
        _ray.From = from;
        _ray.To = from + dir * 9f;
        _ray.CollisionMask = PlayerController.WorldLayer | PlayerController.InteractLayer;
        _ray.Exclude = new Godot.Collections.Array<Rid> { p.GetRid() };
        var hit = space.IntersectRay(_ray);
        if (hit.Count == 0) return;
        _rawPoint = hit["position"].AsVector3();
        _hasRaw = true;
        var col = hit["collider"].AsGodotObject() as Node;
        for (var n = col; n != null && n != this; n = n.GetParent())
            if (n.HasMeta("building_id")) { _aimBuilding = (int)n.GetMeta("building_id"); return; }
        _point = _rawPoint;
        _hasPoint = hit["normal"].AsVector3().Y > 0.7f;
        // de frente para o jogador
        var flat = new Vector3(from.X - _point.X, 0, from.Z - _point.Z);
        _yaw = flat.LengthSquared() > 0.01f ? Mathf.Atan2(flat.X, flat.Z) : 0;
    }

    void Evaluate(PlayerController p, float dt)
    {
        var gs = GameState.Instance;
        if (Current.Id == BuildIds.Belt && _beltStart != null) { EvaluateBeltEnd(dt); return; }
        if (_aimBuilding > 0) { _valid = false; _reason = ""; return; }
        if (!_hasPoint) { _valid = false; _reason = Loc.T("Mire no chão"); return; }
        if (Current.Id == BuildIds.Rope)
        {
            _valid = RopeFor(_point, p.GlobalPosition, out _ropeTop, out _ropeBottom, out _ropeExit, out _reason);
            _route = _valid ? new List<Vector3> { _ropeTop + Vector3.Up * 0.3f, _ropeBottom } : null;
            if (_valid && !gs.CanAfford(gs.BuildCost(Current.Id))) { _valid = false; _reason = Loc.T("Moeda insuficiente"); }
            return;
        }
        _route = null;
        _valid = CanPlace(Current.Id, _point, out _reason);
        if (_valid && Current.Id == BuildIds.Hopper && !HopperSpot(_point, out _hopperBelt, out _hopperJoin, out _reason)) _valid = false;
        if (_valid && !gs.CanAfford(TotalCost())) { _valid = false; _reason = Loc.T("Moeda insuficiente"); }
    }

    /// <summary>O funil precisa ficar ao lado de uma esteira (sem ficar em cima dela).</summary>
    public bool HopperSpot(Vector3 pt, out int beltId, out float joinS, out string reason)
    {
        reason = "";
        beltId = NearestBelt(pt, BuildData.HopperBeltDist, out float d, out joinS);
        if (beltId == 0) { reason = Loc.T("Coloque o funil ao lado de uma esteira"); return false; }
        if (d < 1.0f) { reason = Loc.T("Em cima da esteira: coloque ao lado"); return false; }
        return true;
    }

    void EvaluateBeltEnd(float dt)
    {
        var gs = GameState.Instance;
        var start = _beltStart.Value;
        if (!_hasRaw) { _valid = false; _reason = Loc.T("Mire onde a esteira termina"); return; }
        BeltEnd(_rawPoint, out var end, out int feeds, out float join);
        _routeTimer -= dt;
        bool moved = _routeFrom.DistanceTo(end) > 0.6f || feeds != _beltFeeds;
        if (moved && _routeTimer <= 0)
        {
            _routeTimer = 0.25f;
            _routeFrom = end;
            _beltEnd = end; _beltFeeds = feeds; _beltJoin = join;
            _route = RouteBetween(start, end);
            if (_route != null && feeds == 0) { _route.Add(SellMouth); _route = Simplify(_route); }
        }
        _point = _beltEnd;
        _hasPoint = true;
        if (_route == null) { _valid = false; _reason = Loc.T("Sem caminho livre até aqui"); return; }
        if (PathLength(_route) < BuildData.BeltMinLength) { _valid = false; _reason = Loc.T("Esteira curta demais"); return; }
        if (_beltFeeds < 0) { _valid = false; _reason = Loc.T("Termine na Estação de Venda ou em outra esteira"); return; }
        _valid = gs.CanAfford(TotalCost());
        _reason = _valid ? "" : Loc.T("Moeda insuficiente");
    }

    Vector3 _ropeTop, _ropeBottom, _ropeExit;

    /// <summary>
    /// Onde a corda fica: mirando na beira de um buraco (desce) ou numa borda acima (sobe).
    /// Devolve o ponto de cima, o de baixo e onde o jogador sai em cima.
    /// </summary>
    public bool RopeFor(Vector3 aim, Vector3 playerPos, out Vector3 top, out Vector3 bottom, out Vector3 exit, out string reason)
    {
        top = bottom = exit = default;
        reason = Loc.T("Mire na beira de um buraco ou numa borda acima");
        var gs = GameState.Instance;
        if (gs.BuildingLimitReached(BuildIds.Rope)) { reason = Loc.F("Limite atingido ({0})", BuildData.Of(BuildIds.Rope).Max); return false; }
        var L = World.LayoutAt(aim);
        if (!L.InGrid(aim.X, aim.Z) || PlayerHouse.Contains(aim)) return false;
        var dir = new Vector3(aim.X - playerPos.X, 0, aim.Z - playerPos.Z);
        if (dir.LengthSquared() < 0.04f) return false;
        dir = dir.Normalized();
        float aimG = L.Ground(aim.X, aim.Z);
        bool Open(Vector3 q) => L.InGrid(q.X, q.Z) && L.WallDist(q.X, q.Z) < -0.5f && L.Ceiling(q.X, q.Z) - L.Ground(q.X, q.Z) > 1.9f;
        Vector3 G(Vector3 q) => new(q.X, L.Ground(q.X, q.Z), q.Z);
        // descendo: depois do ponto mirado o chão cai
        for (float k = 0.3f; k < 3.6f; k += 0.25f)
        {
            var q = aim + dir * k;
            if (!L.InGrid(q.X, q.Z)) break;
            float drop = aimG - L.Ground(q.X, q.Z);
            if (drop < 2.2f) continue;
            var b = G(aim + dir * (k + 1.0f));
            var t = G(aim + dir * Mathf.Max(0, k - 0.4f));
            var e = G(t - dir * 1.1f);
            if (!Open(b) || !Open(e) || t.Y - b.Y < 2.2f) break;
            if (t.Y - b.Y > 26f) { reason = Loc.T("Fundo demais para a corda (máx. 25 m)"); return false; }
            top = t; bottom = b; exit = e;
            return NotNearOtherRope(top, ref reason);
        }
        // subindo: mirando numa borda acima do jogador
        if (aimG - playerPos.Y >= 2.2f)
        {
            for (float k = 0f; k < 3.6f; k += 0.25f)
            {
                var q = aim - dir * k;
                if (!L.InGrid(q.X, q.Z)) break;
                if (aimG - L.Ground(q.X, q.Z) < 1.5f) continue;
                var t = G(aim - dir * Mathf.Max(0, k - 0.4f));
                var b = G(aim - dir * (k + 1.0f));
                var e = G(t + dir * 1.1f);
                if (!Open(b) || !Open(e) || t.Y - b.Y < 2.2f) break;
                if (t.Y - b.Y > 26f) { reason = Loc.T("Alto demais para a corda (máx. 25 m)"); return false; }
                top = t; bottom = b; exit = e;
                return NotNearOtherRope(top, ref reason);
            }
        }
        return false;
    }

    bool NotNearOtherRope(Vector3 top, ref string reason)
    {
        foreach (var n in _nodes.Values)
            if (n is Rope r && r.Top.DistanceTo(top) < 1.5f) { reason = Loc.T("Já tem uma corda aqui"); return false; }
        foreach (var r in World.PitRopes)
            if (r.Top.DistanceTo(top) < 1.5f) { reason = Loc.T("Já tem uma corda aqui"); return false; }
        return true;
    }


    long TotalCost() => GameState.Instance.BuildCost(Current.Id) + (Current.Id == BuildIds.Belt && _route != null ? BeltCost(PathLength(_route)) : 0);

    /// <summary>Regras de posição (sem custo). Usado pela prévia e pelos testes.</summary>
    public bool CanPlace(string type, Vector3 pt, out string reason)
    {
        var gs = GameState.Instance;
        reason = "";
        if (!gs.BuildUnlocked(type)) { reason = Loc.T("Libere na árvore de habilidades (ramo Construção)"); return false; }
        if (gs.BuildingLimitReached(type)) { reason = Loc.F("Limite atingido ({0})", BuildData.Of(type).Max); return false; }
        if (PlayerHouse.Contains(pt)) { reason = Loc.T("Só dá para construir na mina"); return false; }
        var L = World.LayoutAt(pt);
        if (!L.InGrid(pt.X, pt.Z) || !L.Reachable(pt.X, pt.Z) || Mathf.Abs(pt.Y - L.Ground(pt.X, pt.Z)) > 0.8f)
        { reason = Loc.T("Lugar inválido"); return false; }
        if (L.NearCrawl(pt.X, pt.Z, 1.5f)) { reason = Loc.T("Lugar inválido"); return false; }
        float clearance = L.Ceiling(pt.X, pt.Z) - L.Ground(pt.X, pt.Z);
        if (clearance < (type == BuildIds.Light ? 2.4f : 2.2f)) { reason = Loc.T("Teto baixo demais"); return false; }
        float minGap = type is BuildIds.Hopper or BuildIds.Drone or BuildIds.Robot ? 2.4f : 1.3f;
        foreach (var n in _nodes.Values)
            if (PosOf(n).DistanceTo(pt) < minGap) { reason = Loc.T("Muito perto de outra construção"); return false; }
        foreach (var st in new Node3D[] { World.SellStation, World.Elevator })
            if (st != null && st.GlobalPosition.DistanceTo(pt) < 3f) { reason = Loc.T("Muito perto de uma estação"); return false; }
        foreach (var c in World.Crystals)
            if (c.GlobalPosition.DistanceSquaredTo(pt) < 1.2f) { reason = Loc.T("Em cima de um minério"); return false; }
        if (type is BuildIds.Drone or BuildIds.Robot or BuildIds.Light or BuildIds.Belt)
            foreach (var belt in _belts.Values)
                if (belt.Nearest(pt, out _, out _) < 0.9f) { reason = Loc.T("Em cima da esteira: coloque ao lado"); return false; }
        if (Obstructed(type, pt)) { reason = Loc.T("Tem uma pedra no caminho"); return false; }
        return true;
    }

    PhysicsShapeQueryParameters3D _shapeQuery;

    /// <summary>Pedras, cristais decorativos e outras coisas sólidas ocupando o espaço da construção.</summary>
    bool Obstructed(string type, Vector3 pt)
    {
        if (!IsInsideTree()) return false;
        var size = type switch
        {
            BuildIds.Hopper => new Vector3(1.2f, 1.3f, 1.2f),
            BuildIds.Drone or BuildIds.Robot => new Vector3(1.1f, 0.8f, 1.1f),
            BuildIds.Belt => new Vector3(0.8f, 0.8f, 0.8f),
            _ => new Vector3(0.4f, 2f, 0.4f),
        };
        _shapeQuery ??= new PhysicsShapeQueryParameters3D { CollisionMask = PlayerController.WorldLayer | PlayerController.CrystalLayer | PlayerController.InteractLayer, CollideWithAreas = false };
        _shapeQuery.Shape = new BoxShape3D { Size = size };
        // começa um pouco acima do chão para não pegar o próprio terreno
        _shapeQuery.Transform = new Transform3D(Basis.Identity, pt + Vector3.Up * (0.45f + size.Y / 2));
        return GetWorld3D().DirectSpaceState.IntersectShape(_shapeQuery, 1).Count > 0;
    }

    void TryPlaceCurrent(PlayerController p, bool press)
    {
        if (Current.Id == BuildIds.Belt && _beltStart == null)
        {
            // 1º passo: marca o início (segurando = arrastar até a ponta)
            if (!_valid) { Fail(); return; }
            _beltStart = _point;
            _startedThisPress = press;
            _route = null; _routeFrom = new Vector3(9999, 0, 9999); _routeTimer = 0;
            AudioManager.Instance?.Play("click", -4f, 1.3f);
            Accessibility.Speak(Loc.T("Início marcado. Leve até a Estação de Venda ou outra esteira."), true);
            return;
        }
        if (!_valid) { Fail(); return; }
        switch (Current.Id)
        {
            case BuildIds.Rope: PlaceRope(_ropeTop, _ropeBottom, _ropeExit); break;
            case BuildIds.Belt:
                PlaceBeltRoute(_beltStart.Value, _route, _beltFeeds, _beltJoin);
                ResetBelt();
                break;
            default: Place(Current.Id, _point, _yaw); break;
        }
    }

    void Fail()
    {
        AudioManager.Instance?.Play("error", -6f);
        if (!string.IsNullOrEmpty(_reason)) GameState.Instance.Notify("Não dá para construir aqui", _reason, NotifyKind.Warning);
    }

    /// <summary>Coloca uma corda já calculada (pontas e saída).</summary>
    public BuildingSave PlaceRope(Vector3 top, Vector3 bottom, Vector3 exit)
    {
        var gs = GameState.Instance;
        var b = gs.TryBuild(BuildIds.Rope, top, 0);
        if (b == null) return null;
        b.BX = bottom.X; b.BY = bottom.Y; b.BZ = bottom.Z;
        b.EX = exit.X; b.EY = exit.Y; b.EZ = exit.Z;
        Spawn(b);
        AudioManager.Instance?.Play("swing", -2f, 0.6f);
        AudioManager.Instance?.Play("thud", -8f, 1.4f);
        _route = null;
        return b;
    }


    /// <summary>Constrói (paga e cria) poste, funil, drone ou robô. O funil precisa de uma esteira ao lado.</summary>
    public BuildingSave Place(string type, Vector3 pt, float yaw)
    {
        var gs = GameState.Instance;
        if (type is BuildIds.Rope or BuildIds.Belt) return null; // use PlaceRope / PlaceBelt
        int beltId = 0; float join = 0;
        if (type == BuildIds.Hopper && !HopperSpot(pt, out beltId, out join, out _)) return null;
        var b = gs.TryBuild(type, pt, yaw);
        if (b == null) return null;
        if (type == BuildIds.Hopper) { b.Feeds = beltId; b.JoinS = join; }
        Spawn(b);
        if (_nodes.TryGetValue(b.Id, out var n) && n is Dock d) d.ShowArea(Active);
        AudioManager.Instance?.Play("buy", -4f, type == BuildIds.Light ? 1.2f : 0.9f);
        Effects.Instance?.Burst(pt + Vector3.Up * 0.5f, new Color(0.4f, 1f, 0.85f), 0.6f);
        if (type == BuildIds.Drone) gs.Notify("Drone de área ativo", Loc.F("Recolhe fragmentos soltos num raio de {0:0} m da base.", BuildData.DroneRange), NotifyKind.Success);
        if (type == BuildIds.Robot) gs.Notify("Robô mineiro ativo", Loc.F("Minera os depósitos num raio de {0:0} m da base.", BuildData.RobotRange), NotifyKind.Success);
        return b;
    }

    /// <summary>Esteira do início até onde se mira (calcula o trajeto e a ligação). Usado por testes e atalhos.</summary>
    public BuildingSave PlaceBelt(Vector3 start, Vector3 aimEnd)
    {
        if (!CanPlace(BuildIds.Belt, start, out _)) return null;
        BeltEnd(aimEnd, out var end, out int feeds, out float join);
        if (feeds < 0) return null;
        var route = RouteBetween(start, end);
        if (route == null) return null;
        if (feeds == 0) { route.Add(SellMouth); route = Simplify(route); }
        return PlaceBeltRoute(start, route, feeds, join);
    }

    BuildingSave PlaceBeltRoute(Vector3 start, List<Vector3> route, int feeds, float join)
    {
        var gs = GameState.Instance;
        if (route == null || route.Count < 2 || feeds < 0 || PathLength(route) < BuildData.BeltMinLength) return null;
        var b = gs.TryBuild(BuildIds.Belt, start, 0, BeltCost(PathLength(route)));
        if (b == null) return null;
        b.Path = Flatten(route);
        b.Feeds = feeds;
        b.JoinS = join;
        Spawn(b);
        // funis sem esteira por perto passam a despejar nesta
        foreach (var h in gs.Data.Buildings.Where(x => x.Type == BuildIds.Hopper && (x.Feeds <= 0 || !_belts.ContainsKey(x.Feeds))))
            if (HopperSpot(new Vector3(h.X, h.Y, h.Z), out int bid, out float js, out _) && bid == b.Id) { h.Feeds = bid; h.JoinS = js; }
        AudioManager.Instance?.Play("buy", -4f, 0.9f);
        Effects.Instance?.Burst(start + Vector3.Up * 0.5f, new Color(0.4f, 1f, 0.85f), 0.6f);
        float toSell = DistanceToSell(b.Id);
        gs.Notify("Esteira construída", feeds == 0
            ? Loc.F("{0:0} m até a Estação de Venda. Deposite fragmentos na entrada.", PathLength(route))
            : Loc.F("{0:0} m, emendada em outra esteira ({1:0} m até a venda).", PathLength(route), toSell), NotifyKind.Success);
        return b;
    }

    public void Demolish(int id)
    {
        var gs = GameState.Instance;
        var b = gs.Building(id);
        if (b == null) return;
        string name = BuildData.Of(b.Type).Name;
        if (_nodes.TryGetValue(id, out var dn) && dn is Dock dock)
            foreach (var m in dock.Unit?.TakeCargo() ?? new List<MaterialDef>()) gs.HopperAdd(id, m.Id);
        long refund = gs.Demolish(id);
        if (_nodes.TryGetValue(id, out var rn) && rn is Rope rp)
        {
            var Lr = World.LayoutAt(rp.Top);
            Lr.ExtraRopes.Remove((rp.Top, rp.Bottom));
            Lr.Reflood();
        }
        if (_nodes.TryGetValue(id, out var n)) { Effects.Instance?.Burst(PosOf(n) + Vector3.Up * 0.5f, new Color(1f, 0.6f, 0.3f), 0.5f); n.QueueFree(); _nodes.Remove(id); }
        if (_belts.TryGetValue(id, out var belt))
        {
            // o que estava andando é vendido como na fila
            foreach (var m in belt.Items) gs.SellFromBelt(m);
            belt.QueueFree();
            _belts.Remove(id);
            foreach (var o in gs.Data.Buildings) if (o.Feeds == id) o.Feeds = -1;
        }
        AudioManager.Instance?.Play("thud", -6f, 1.2f);
        gs.Notify(Loc.F("{0} desmontado", name), Loc.F("Devolvido: {0}", GameState.FormatMoney(refund)), NotifyKind.Info);
    }

    void UpdateGhost()
    {
        _ghostOk ??= Ghost(new Color(0.3f, 1f, 0.6f, 0.4f));
        _ghostBad ??= Ghost(new Color(1f, 0.3f, 0.25f, 0.4f));
        if (_ghost != null && _ghostType != Current.Id) { _ghost.QueueFree(); _ghost = null; }
        if (_ghost == null)
        {
            _ghost = new Node3D { Name = "BuildGhost" };
            _ghostType = Current.Id;
            AddChild(_ghost);
            MeshInstance3D M(Mesh m, Vector3 pos) { var mi = new MeshInstance3D { Mesh = m, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }; _ghost.AddChild(mi); return mi; }
            switch (Current.Id)
            {
                case BuildIds.Light:
                    M(new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = 3f }, new Vector3(0, 1.5f, 0));
                    M(new BoxMesh { Size = new Vector3(0.9f, 0.18f, 0.3f) }, new Vector3(0.35f, 3f, 0));
                    M(new TorusMesh { InnerRadius = 6.85f, OuterRadius = 7f, Rings = 64, RingSegments = 4 }, new Vector3(0, 0.15f, 0));
                    break;
                case BuildIds.Hopper:
                    M(new BoxMesh { Size = new Vector3(1.4f, 0.9f, 1.4f) }, new Vector3(0, 0.45f, 0));
                    M(new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.3f, Height = 0.8f }, new Vector3(0, 1.3f, 0));
                    M(new TorusMesh { InnerRadius = BuildData.HopperPullRadius - 0.12f, OuterRadius = BuildData.HopperPullRadius, Rings = 64, RingSegments = 4 }, new Vector3(0, 0.15f, 0));
                    break;
                case BuildIds.Belt:
                    M(new BoxMesh { Size = new Vector3(0.9f, 0.7f, 0.9f) }, new Vector3(0, 0.35f, 0));
                    break;
                case BuildIds.Drone:
                case BuildIds.Robot:
                {
                    float r = Current.Id == BuildIds.Drone ? BuildData.DroneRange : BuildData.RobotRange;
                    M(new CylinderMesh { TopRadius = 0.55f, BottomRadius = 0.6f, Height = 0.25f }, new Vector3(0, 0.12f, 0));
                    M(new TorusMesh { InnerRadius = r - 0.15f, OuterRadius = r, Rings = 96, RingSegments = 4 }, new Vector3(0, 0.15f, 0));
                    break;
                }
                case BuildIds.Rope:
                    M(new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 0.7f }, new Vector3(0, 0.35f, 0));
                    break;
            }
        }
        _routeLine ??= MakeRouteLine();
        bool dragging = Current.Id == BuildIds.Belt && _beltStart != null;
        bool show = (_hasPoint && _aimBuilding == 0) || dragging;
        _ghost.Visible = show;
        if (show)
        {
            _ghost.GlobalPosition = Current.Id == BuildIds.Rope && _valid ? _ropeTop : dragging ? _beltStart.Value : _point;
            _ghost.Rotation = new Vector3(0, _yaw, 0);
            foreach (var c in _ghost.GetChildren().OfType<MeshInstance3D>()) c.MaterialOverride = _valid ? _ghostOk : _ghostBad;
        }
        _routeLine.Visible = show && (Current.Id == BuildIds.Belt || Current.Id == BuildIds.Rope) && _route != null;
        if (_routeLine.Visible)
        {
            ((StandardMaterial3D)_routeLine.MaterialOverride).AlbedoColor = _valid ? new Color(0.3f, 1f, 0.85f) : new Color(1f, 0.45f, 0.3f);
            DrawRoute(_route);
        }
    }

    static StandardMaterial3D Ghost(Color c) => new()
    {
        AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
    };

    MeshInstance3D MakeRouteLine()
    {
        var mi = new MeshInstance3D { Name = "RoutePreview", Mesh = new ImmediateMesh(), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        mi.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.3f, 1f, 0.85f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            NoDepthTest = true,
        };
        AddChild(mi);
        return mi;
    }

    List<Vector3> _drawn;
    void DrawRoute(List<Vector3> route)
    {
        if (_drawn == route) return;
        _drawn = route;
        var im = (ImmediateMesh)_routeLine.Mesh;
        im.ClearSurfaces();
        if (route.Count < 2 || PathLength(route) < 0.05f) return; // nada para desenhar
        im.SurfaceBegin(Mesh.PrimitiveType.Lines);
        // tracejado: 1 m sim, 0,6 m não
        float acc = 0;
        for (int i = 1; i < route.Count; i++)
        {
            var a = route[i - 1] + Vector3.Up * 0.35f; var b = route[i] + Vector3.Up * 0.35f;
            float len = a.DistanceTo(b);
            for (float t = 0; t < len; t += 0.4f)
            {
                float u = Mathf.Min(t + 0.4f, len);
                if ((acc + t) % 1.6f < 1f) { im.SurfaceAddVertex(a.Lerp(b, t / len)); im.SurfaceAddVertex(a.Lerp(b, u / len)); }
            }
            acc += len;
        }
        im.SurfaceEnd();
        _routeLine.GlobalPosition = Vector3.Zero;
    }

    void PublishBar()
    {
        var gs = GameState.Instance;
        string title;
        string detail;
        bool ok = _valid;
        bool dragging = Current.Id == BuildIds.Belt && _beltStart != null;
        if (!dragging && _aimBuilding > 0 && gs.Building(_aimBuilding) is { } b)
        {
            title = Loc.F("{0} — desmontar?", BuildData.Of(b.Type).Name);
            detail = Loc.F("[{0}] Desmontar (+{1})", InputHints.Key("interact"), GameState.FormatMoney((long)(b.Paid * BuildData.RefundFraction)));
            ok = true;
        }
        else
        {
            var def = Current;
            string cost = GameState.FormatMoney(TotalCost());
            if (def.Id == BuildIds.Rope && _valid)
                cost = Loc.F("{0} · {1:0} m", GameState.FormatMoney(gs.BuildCost(def.Id)), _ropeTop.DistanceTo(_ropeBottom));
            if (def.Id == BuildIds.Belt && _route != null && dragging)
                cost = Loc.F("{0} ({1:0} m)", GameState.FormatMoney(TotalCost()), PathLength(_route));
            title = $"{def.Name} · {cost}";
            string hint = def.Id != BuildIds.Belt ? def.Description
                : !dragging ? Loc.T("Marque o início da esteira (segure e arraste, ou clique e depois clique na ponta).")
                : _beltFeeds == 0 ? Loc.T("Ponta na Estação de Venda: tudo o que andar nela é vendido.")
                : _beltFeeds > 0 ? Loc.T("Ponta emendada em outra esteira.")
                : Loc.T("Leve a ponta até a Estação de Venda ou até outra esteira.");
            detail = hint + (ok || string.IsNullOrEmpty(_reason) ? "" : "\n⚠ " + _reason);
        }
        string keys = dragging
            ? Loc.F("[{0}] Terminar aqui   [{1}] Cancelar início   [{2}] Sair", InputHints.Key("mine"), InputHints.Key("interact"), InputHints.Key("build"))
            : Loc.F("[{0}] Construir   [{1}/{2}] Trocar   [{3}] Desmontar   [{4}] Sair",
                InputHints.Key("mine"), InputHints.Key("build_prev"), InputHints.Key("build_next"), InputHints.Key("interact"), InputHints.Key("build"));
        BuildBarChanged?.Invoke(title, detail + "\n" + keys, ok);
    }
}
