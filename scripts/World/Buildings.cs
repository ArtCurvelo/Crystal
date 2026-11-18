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
/// Construções do jogador: postes de luz, funis com esteira até a Estação de Venda e o drone coletor.
/// Também controla o modo construção (prévia, colocar, desmontar).
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
    bool _bulk;
    readonly Dictionary<int, Belt> _belts = new();
    DroneBot _drone;

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
        _bulk = true;
        long refund = 0;
        foreach (var b in gs.Data.Buildings.ToList())
        {
            // o mapa pode mudar entre versões: construção que ficou dentro da rocha é devolvida
            if (!World.IsSafeSpot(new Vector3(b.X, b.Y, b.Z)))
            {
                foreach (var kv in b.Queue)
                    if (GameData.Mat(kv.Key) is { } m) gs.SellFromBelt(m, kv.Value);
                gs.Data.Buildings.Remove(b);
                refund += b.Paid;
                continue;
            }
            Spawn(b);
        }
        _bulk = false;
        foreach (var L in World.Layouts) L.Reflood();
        if (refund > 0)
        {
            gs.AddMoney(refund);
            gs.Notify("Construções devolvidas", Loc.F("O mapa mudou nesta versão; o valor foi devolvido: {0}", GameState.FormatMoney(refund)), NotifyKind.Info);
        }
        SyncDrone();
    }

    void Spawn(BuildingSave b)
    {
        var pos = new Vector3(b.X, b.Y, b.Z);
        Node3D node;
        if (b.Type == BuildIds.Rope)
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
        if (b.Type == BuildIds.Light)
        {
            var L = World.LayoutAt(pos);
            float clearance = L.InGrid(pos.X, pos.Z) ? L.Ceiling(pos.X, pos.Z) - L.Ground(pos.X, pos.Z) : 4f;
            node = new LightPost { BuildingId = b.Id, Height = Mathf.Clamp(clearance - 0.5f, 2.0f, 3.2f), Tint = WorldBuilder.RegionAt(pos).AccentColor };
        }
        else
        {
            var hopper = new Hopper { BuildingId = b.Id };
            node = hopper;
            var route = RouteToSell(pos);
            if (route != null)
            {
                var belt = new Belt { BuildingId = b.Id, Accent = new Color(0.3f, 1f, 0.8f), GroundAt = World.GroundAt };
                AddChild(belt);
                belt.Build(route);
                _belts[b.Id] = belt;
                hopper.BeltLength = belt.Length;
            }
        }
        node.Name = $"Building_{b.Id}";
        AddChild(node);
        node.GlobalPosition = pos;
        node.Rotation = new Vector3(0, b.Yaw, 0);
        node.SetMeta("building_id", b.Id);
        _nodes[b.Id] = node;
    }

    public void SyncDrone()
    {
        bool owned = GameState.Instance.Data.DroneOwned;
        if (owned && _drone == null)
        {
            _drone = new DroneBot { Name = "Drone", Manager = this };
            AddChild(_drone);
            var p = GetPlayer?.Invoke();
            if (p != null) _drone.GlobalPosition = p.GlobalPosition + new Vector3(1, 2.5f, 1);
        }
        else if (!owned && _drone != null) { _drone.QueueFree(); _drone = null; }
    }

    public DroneBot Drone => _drone;
    /// <summary>Posição de referência (a corda fica na origem e usa coordenadas absolutas).</summary>
    static Vector3 PosOf(Node3D n) => n is Rope r ? r.Top : n.GlobalPosition;
    public IEnumerable<Hopper> Hoppers => _nodes.Values.OfType<Hopper>();
    public Belt BeltOf(int id) => _belts.TryGetValue(id, out var b) ? b : null;
    public Node3D NodeOf(int id) => _nodes.TryGetValue(id, out var n) ? n : null;
    public int NodeCount => _nodes.Count;

    /// <summary>Esconde construções longe do jogador (mesma regra das regiões) e tudo dentro da casa.</summary>
    public void UpdateVisibility(int playerRegion, bool inHouse)
    {
        foreach (var n in _nodes.Values)
        {
            bool v = !inHouse && Mathf.Abs(WorldBuilder.RegionAt(PosOf(n)).Index - playerRegion) <= 1;
            if (n.Visible != v) n.Visible = v;
        }
        foreach (var b in _belts.Values) b.Visible = !inHouse;
        if (_drone != null) _drone.Visible = !inHouse;
    }

    /// <summary>Antes de salvar: o que está andando na esteira é gravado para voltar à fila.</summary>
    public void WriteInFlight(SaveData d)
    {
        foreach (var b in d.Buildings)
        {
            b.InFlight = new Dictionary<string, int>();
            if (!_belts.TryGetValue(b.Id, out var belt)) continue;
            foreach (var m in belt.Items) b.InFlight[m.Id] = (b.InFlight.TryGetValue(m.Id, out int c) ? c : 0) + 1;
        }
        // carga do drone volta para a mochila (ou se perde só se não couber)
        if (_drone != null) _drone.FlushCargoTo(d);
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

    // ======================================================================= esteira
    /// <summary>
    /// Trajeto da esteira de um ponto até a Estação de Venda: caminho a pé em cada região,
    /// passando pelos túneis entre elas. Null se não houver caminho.
    /// </summary>
    public List<Vector3> RouteToSell(Vector3 from)
    {
        if (World?.SellStation == null) return null;
        int k = WorldBuilder.RegionAt(from).Index;
        var pts = new List<Vector3>();
        var cur = from;
        for (int r = k; r >= 0; r--)
        {
            var L = World.Layouts[r];
            if (!L.InGrid(cur.X, cur.Z)) return null;
            Vector3 goal = r == 0 ? SellIntake : L.Entrance;
            var occupied = Occupied(L, cur, goal);
            var path = L.FindClearPath(cur, goal, id => occupied.Contains(id));
            if (path == null) return null;
            pts.AddRange(Straighten(L, path, occupied));
            if (r > 0)
            {
                var prev = World.Layouts[r - 1];
                // túnel reto entre a entrada desta região e a saída da anterior (chão em y = 0)
                pts.Add(new Vector3(0, L.Ground(0, L.Entrance.Z), L.Entrance.Z));
                pts.Add(new Vector3(0, 0, L.Z0 + L.Nz - 0.5f));
                pts.Add(new Vector3(0, 0, prev.Z0 + 0.5f));
                cur = prev.Exit;
                pts.Add(prev.OnGround(cur.X, cur.Z));
            }
        }
        var station = World.SellStation.GlobalPosition;
        pts.Add(new Vector3(SellIntake.X, World.Layouts[0].Ground(SellIntake.X, SellIntake.Z), SellIntake.Z));
        pts.Add(station + World.SellStation.GlobalBasis.X * 1.25f);
        return Simplify(pts);
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

    /// <summary>Ponto ao lado da Estação de Venda onde a esteira chega.</summary>
    public Vector3 SellIntake => World.SellStation.GlobalPosition + World.SellStation.GlobalBasis.X * 2.6f;

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

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var gs = GameState.Instance;
        if (gs == null) return;
        foreach (var kv in _belts)
        {
            var belt = kv.Value;
            belt.Feed -= dt;
            if (belt.Feed <= 0 && gs.HopperQueued(kv.Key) > 0)
            {
                belt.Feed = BuildData.BeltInterval;
                var id = gs.HopperTake(kv.Key);
                if (id != null) belt.Push(GameData.Mat(id));
            }
            foreach (var m in belt.Advance(dt * BuildData.BeltSpeed)) gs.SellFromBelt(m);
        }
        if (Active) UpdateBuildMode(dt);
        else if (CanUseBuildMode != null && CanUseBuildMode() && Input.IsActionJustPressed("build")) SetActive(true);
    }

    // ======================================================================= modo construção
    public bool Active { get; private set; }
    public int Selected { get; private set; }
    public BuildDef Current => BuildData.All[Selected];
    Node3D _ghost;
    MeshInstance3D _routeLine;
    StandardMaterial3D _ghostOk, _ghostBad;
    bool _valid;
    string _reason = "";
    Vector3 _point;
    float _yaw;
    List<Vector3> _route;
    Vector3 _routeFrom = new(9999, 0, 9999);
    float _routeTimer;
    int _aimBuilding;
    PhysicsRayQueryParameters3D _ray;

    public void SetActive(bool on)
    {
        if (Active == on) return;
        Active = on;
        var p = GetPlayer?.Invoke();
        if (p != null) p.BuildMode = on;
        if (_ghost != null) _ghost.Visible = false;
        if (_routeLine != null) _routeLine.Visible = false;
        _route = null; _routeFrom = new Vector3(9999, 0, 9999);
        AudioManager.Instance?.Play("click", -4f, on ? 1.2f : 0.9f);
        if (on) Accessibility.Speak(Loc.T("Modo construção") + ". " + Current.Name, true);
        if (!on) BuildBarChanged?.Invoke(null, null, false);
    }

    public void Select(int index)
    {
        Selected = ((index % BuildData.All.Count) + BuildData.All.Count) % BuildData.All.Count;
        if (_ghost != null) { _ghost.QueueFree(); _ghost = null; }
        _route = null; _routeFrom = new Vector3(9999, 0, 9999);
        if (Active) Accessibility.Speak(Current.Name + ", " + GameState.FormatMoney(Current.Cost), true);
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
        if (Input.IsActionJustPressed("interact") && _aimBuilding > 0) Demolish(_aimBuilding);
        else if (Input.IsActionJustPressed("mine")) TryPlaceCurrent(p);
        PublishBar();
    }

    bool _hasPoint;

    void Aim(PlayerController p)
    {
        _aimBuilding = 0;
        _hasPoint = false;
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
        var col = hit["collider"].AsGodotObject() as Node;
        for (var n = col; n != null && n != this; n = n.GetParent())
            if (n.HasMeta("building_id")) { _aimBuilding = (int)n.GetMeta("building_id"); return; }
        _point = hit["position"].AsVector3();
        _hasPoint = hit["normal"].AsVector3().Y > 0.7f;
        // de frente para o jogador
        var flat = new Vector3(from.X - _point.X, 0, from.Z - _point.Z);
        _yaw = flat.LengthSquared() > 0.01f ? Mathf.Atan2(flat.X, flat.Z) : 0;
    }

    void Evaluate(PlayerController p, float dt)
    {
        var gs = GameState.Instance;
        if (_aimBuilding > 0) { _valid = false; _reason = ""; return; }
        if (!_hasPoint) { _valid = false; _reason = Loc.T("Mire no chão"); return; }
        if (Current.Id == BuildIds.Rope)
        {
            _valid = RopeFor(_point, p.GlobalPosition, out _ropeTop, out _ropeBottom, out _ropeExit, out _reason);
            _route = _valid ? new List<Vector3> { _ropeTop + Vector3.Up * 0.3f, _ropeBottom } : null;
            if (_valid && !gs.CanAfford(Current.Cost)) { _valid = false; _reason = Loc.T("Moeda insuficiente"); }
            return;
        }
        _valid = CanPlace(Current.Id, _point, out _reason);
        if (_valid && Current.Id == BuildIds.Hopper)
        {
            _routeTimer -= dt;
            if (_route == null && _routeFrom.DistanceTo(_point) < 0.01f) { _valid = false; _reason = Loc.T("Sem caminho até a Estação de Venda"); }
            else if (_routeFrom.DistanceTo(_point) > 0.8f && _routeTimer <= 0)
            {
                _routeTimer = 0.25f;
                _routeFrom = _point;
                _route = RouteToSell(_point);
            }
            if (_route == null) { _valid = false; _reason = Loc.T("Sem caminho até a Estação de Venda"); }
        }
        if (_valid && !gs.CanAfford(TotalCost())) { _valid = false; _reason = Loc.T("Moeda insuficiente"); }
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

    long TotalCost() => Current.Cost + (Current.Id == BuildIds.Hopper && _route != null ? BeltCost(PathLength(_route)) : 0);

    /// <summary>Regras de posição (sem custo). Usado pela prévia e pelos testes.</summary>
    public bool CanPlace(string type, Vector3 pt, out string reason)
    {
        var gs = GameState.Instance;
        reason = "";
        if (gs.BuildingLimitReached(type))
        {
            reason = type == BuildIds.Drone ? Loc.T("O drone já está trabalhando") : Loc.F("Limite atingido ({0})", BuildData.Of(type).Max);
            return false;
        }
        if (type == BuildIds.Drone) return true; // o drone só precisa ser lançado
        if (PlayerHouse.Contains(pt)) { reason = Loc.T("Só dá para construir na mina"); return false; }
        var L = World.LayoutAt(pt);
        if (!L.InGrid(pt.X, pt.Z) || !L.Reachable(pt.X, pt.Z) || Mathf.Abs(pt.Y - L.Ground(pt.X, pt.Z)) > 0.8f)
        { reason = Loc.T("Lugar inválido"); return false; }
        if (L.NearCrawl(pt.X, pt.Z, 1.5f)) { reason = Loc.T("Lugar inválido"); return false; }
        float clearance = L.Ceiling(pt.X, pt.Z) - L.Ground(pt.X, pt.Z);
        if (clearance < (type == BuildIds.Light ? 2.4f : 2.2f)) { reason = Loc.T("Teto baixo demais"); return false; }
        float minGap = type == BuildIds.Hopper ? 2.4f : 1.3f;
        foreach (var n in _nodes.Values)
            if (PosOf(n).DistanceTo(pt) < minGap) { reason = Loc.T("Muito perto de outra construção"); return false; }
        foreach (var st in new Node3D[] { World.SellStation, World.Elevator })
            if (st != null && st.GlobalPosition.DistanceTo(pt) < 3f) { reason = Loc.T("Muito perto de uma estação"); return false; }
        foreach (var c in World.Crystals)
            if (c.GlobalPosition.DistanceSquaredTo(pt) < 1.2f) { reason = Loc.T("Em cima de um minério"); return false; }
        if (Obstructed(type, pt)) { reason = Loc.T("Tem uma pedra no caminho"); return false; }
        return true;
    }

    PhysicsShapeQueryParameters3D _shapeQuery;

    /// <summary>Pedras, cristais decorativos e outras coisas sólidas ocupando o espaço da construção.</summary>
    bool Obstructed(string type, Vector3 pt)
    {
        if (!IsInsideTree()) return false;
        var size = type == BuildIds.Hopper ? new Vector3(1.2f, 1.3f, 1.2f) : new Vector3(0.4f, 2f, 0.4f);
        _shapeQuery ??= new PhysicsShapeQueryParameters3D { CollisionMask = PlayerController.WorldLayer | PlayerController.CrystalLayer | PlayerController.InteractLayer, CollideWithAreas = false };
        _shapeQuery.Shape = new BoxShape3D { Size = size };
        // começa um pouco acima do chão para não pegar o próprio terreno
        _shapeQuery.Transform = new Transform3D(Basis.Identity, pt + Vector3.Up * (0.45f + size.Y / 2));
        return GetWorld3D().DirectSpaceState.IntersectShape(_shapeQuery, 1).Count > 0;
    }

    void TryPlaceCurrent(PlayerController p)
    {
        if (!_valid)
        {
            AudioManager.Instance?.Play("error", -6f);
            if (!string.IsNullOrEmpty(_reason)) GameState.Instance.Notify("Não dá para construir aqui", _reason, NotifyKind.Warning);
            return;
        }
        if (Current.Id == BuildIds.Rope) { PlaceRope(_ropeTop, _ropeBottom, _ropeExit); return; }
        Place(Current.Id, _point, _yaw, _route);
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

    /// <summary>Constrói (paga e cria). Para o funil, o trajeto pode ser passado (senão é calculado).</summary>
    public BuildingSave Place(string type, Vector3 pt, float yaw, List<Vector3> route = null)
    {
        var gs = GameState.Instance;
        if (type == BuildIds.Rope) return null; // use PlaceRope
        if (type == BuildIds.Hopper) { route ??= RouteToSell(pt); if (route == null) return null; }
        long extra = type == BuildIds.Hopper ? BeltCost(PathLength(route)) : 0;
        if (type == BuildIds.Drone)
        {
            var d = gs.TryBuild(type, pt, yaw);
            if (d == null) return null;
            SyncDrone();
            if (_drone != null) _drone.GlobalPosition = pt + Vector3.Up * 1.2f;
            AudioManager.Instance?.Play("buy", -4f, 1.1f);
            gs.Notify("Drone coletor ativo", "Ele recolhe fragmentos espalhados perto de você.", NotifyKind.Success);
            return d;
        }
        var b = gs.TryBuild(type, pt, yaw, extra);
        if (b == null) return null;
        Spawn(b);
        AudioManager.Instance?.Play("buy", -4f, type == BuildIds.Light ? 1.2f : 0.9f);
        Effects.Instance?.Burst(pt + Vector3.Up * 0.5f, new Color(0.4f, 1f, 0.85f), 0.6f);
        if (type == BuildIds.Hopper)
            gs.Notify("Esteira construída", Loc.F("{0:0} m até a Estação de Venda. Tudo o que entrar no funil é vendido lá.", PathLength(route)), NotifyKind.Success);
        _route = null; _routeFrom = new Vector3(9999, 0, 9999);
        return b;
    }

    public void Demolish(int id)
    {
        var gs = GameState.Instance;
        var b = gs.Building(id);
        if (b == null) return;
        string name = BuildData.Of(b.Type).Name;
        long refund = gs.Demolish(id);
        if (_nodes.TryGetValue(id, out var rn) && rn is Rope rp)
        {
            var Lr = World.LayoutAt(rp.Top);
            Lr.ExtraRopes.Remove((rp.Top, rp.Bottom));
            Lr.Reflood();
        }
        if (_nodes.TryGetValue(id, out var n)) { Effects.Instance?.Burst(PosOf(n) + Vector3.Up * 0.5f, new Color(1f, 0.6f, 0.3f), 0.5f); n.QueueFree(); _nodes.Remove(id); }
        if (_belts.TryGetValue(id, out var belt)) { belt.QueueFree(); _belts.Remove(id); }
        AudioManager.Instance?.Play("thud", -6f, 1.2f);
        gs.Notify(Loc.F("{0} desmontado", name), Loc.F("Devolvido: {0}", GameState.FormatMoney(refund)), NotifyKind.Info);
    }

    void UpdateGhost()
    {
        _ghostOk ??= Ghost(new Color(0.3f, 1f, 0.6f, 0.4f));
        _ghostBad ??= Ghost(new Color(1f, 0.3f, 0.25f, 0.4f));
        if (_ghost == null)
        {
            _ghost = new Node3D { Name = "BuildGhost" };
            AddChild(_ghost);
            switch (Current.Id)
            {
                case BuildIds.Light:
                    _ghost.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.08f, BottomRadius = 0.12f, Height = 3f }, Position = new Vector3(0, 1.5f, 0) });
                    _ghost.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.18f, 0.3f) }, Position = new Vector3(0.35f, 3f, 0) });
                    _ghost.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 6.85f, OuterRadius = 7f, Rings = 64, RingSegments = 4 }, Position = new Vector3(0, 0.15f, 0) });
                    break;
                case BuildIds.Hopper:
                    _ghost.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.4f, 0.9f, 1.4f) }, Position = new Vector3(0, 0.45f, 0) });
                    _ghost.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.9f, BottomRadius = 0.3f, Height = 0.8f }, Position = new Vector3(0, 1.3f, 0) });
                    _ghost.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = BuildData.HopperPullRadius - 0.12f, OuterRadius = BuildData.HopperPullRadius, Rings = 64, RingSegments = 4 }, Position = new Vector3(0, 0.15f, 0) });
                    break;
                case BuildIds.Rope:
                    _ghost.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 0.7f }, Position = new Vector3(0, 0.35f, 0) });
                    break;
                default:
                    _ghost.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.35f, Height = 0.3f }, Position = new Vector3(0, 1.2f, 0) });
                    break;
            }
        }
        _routeLine ??= MakeRouteLine();
        bool show = _hasPoint && _aimBuilding == 0;
        _ghost.Visible = show;
        if (show)
        {
            _ghost.GlobalPosition = Current.Id == BuildIds.Rope && _valid ? _ropeTop : _point;
            _ghost.Rotation = new Vector3(0, _yaw, 0);
            foreach (var c in _ghost.GetChildren().OfType<MeshInstance3D>()) c.MaterialOverride = _valid ? _ghostOk : _ghostBad;
        }
        _routeLine.Visible = show && (Current.Id == BuildIds.Hopper || Current.Id == BuildIds.Rope) && _route != null;
        if (_routeLine.Visible) DrawRoute(_route);
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
        if (_aimBuilding > 0 && gs.Building(_aimBuilding) is { } b)
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
                cost = Loc.F("{0} · {1:0} m", GameState.FormatMoney(def.Cost), _ropeTop.DistanceTo(_ropeBottom));
            if (def.Id == BuildIds.Hopper && _route != null)
                cost = Loc.F("{0} (funil {1} + esteira {2:0} m)", GameState.FormatMoney(TotalCost()), GameState.FormatMoney(def.Cost), PathLength(_route));
            title = $"{def.Name} · {cost}";
            detail = def.Description + (ok || string.IsNullOrEmpty(_reason) ? "" : "\n⚠ " + _reason);
        }
        string keys = Loc.F("[{0}] Construir   [{1}/{2}] Trocar   [{3}] Desmontar   [{4}] Sair",
            InputHints.Key("mine"), InputHints.Key("build_prev"), InputHints.Key("build_next"), InputHints.Key("interact"), InputHints.Key("build"));
        BuildBarChanged?.Invoke(title, detail + "\n" + keys, ok);
    }
}

// =========================================================================== poste de luz
public partial class LightPost : StaticBody3D
{
    public int BuildingId;
    public float Height = 3f;
    public Color Tint = Colors.White;
    OmniLight3D _light;
    MeshInstance3D _lens;
    float _t;

    public override void _Ready()
    {
        CollisionLayer = PlayerController.WorldLayer | PlayerController.InteractLayer;
        CollisionMask = 0;
        AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.18f, Height = Height }, Position = new Vector3(0, Height / 2, 0) });
        var metal = MeshFactory.Metal(new Color(0.16f, 0.17f, 0.2f), 0.4f);
        var dark = MeshFactory.Metal(new Color(0.08f, 0.08f, 0.1f), 0.6f);
        var lightColor = new Color(1f, 0.93f, 0.82f).Lerp(Tint, 0.2f);
        var neon = MeshFactory.Emissive(Tint, Tint, 2.2f);
        Add(new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.32f, Height = 0.18f, RadialSegments = 8 }, dark, new Vector3(0, 0.09f, 0));
        Add(new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.08f, Height = Height, RadialSegments = 8 }, metal, new Vector3(0, Height / 2, 0));
        Add(new BoxMesh { Size = new Vector3(0.025f, Height * 0.7f, 0.025f) }, neon, new Vector3(0, Height * 0.45f, 0.075f));
        Add(new BoxMesh { Size = new Vector3(0.7f, 0.07f, 0.1f) }, metal, new Vector3(0.3f, Height - 0.05f, 0));
        Add(new BoxMesh { Size = new Vector3(0.36f, 0.14f, 0.26f) }, dark, new Vector3(0.58f, Height - 0.1f, 0));
        _lens = Add(new BoxMesh { Size = new Vector3(0.3f, 0.03f, 0.2f) }, MeshFactory.Emissive(lightColor, lightColor, 1.8f), new Vector3(0.58f, Height - 0.18f, 0));
        _light = new OmniLight3D
        {
            LightColor = lightColor, LightEnergy = 2.6f, OmniRange = 16f, OmniAttenuation = 0.9f,
            Position = new Vector3(0.58f, Height - 0.45f, 0), LightVolumetricFogEnergy = 0.6f,
            DistanceFadeEnabled = true, DistanceFadeBegin = 60f, DistanceFadeLength = 15f,
        };
        AddChild(_light);
    }

    MeshInstance3D Add(Mesh m, Material mat, Vector3 pos)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(mi);
        return mi;
    }

    public override void _Process(double delta)
    {
        // acende com um piscar curto ao ser construído
        if (_t > 1f) return;
        _t += (float)delta;
        float f = _t < 0.5f ? (Mathf.Sin(_t * 60f) > 0 ? 1f : 0.2f) : 1f;
        _light.LightEnergy = 2.6f * f;
    }
}

// =========================================================================== funil
public partial class Hopper : StationBase, ShardManager.IShardSink
{
    public int BuildingId;
    public float BeltLength = -1;
    Label3D _count;

    public Vector3 SinkPoint => GlobalPosition + Vector3.Up * 1.5f;

    public bool AcceptShard(MaterialDef m)
    {
        GameState.Instance.HopperAdd(BuildingId, m.Id);
        AudioManager.Instance?.Play3D("pickup", SinkPoint, -14f, 0.8f);
        return true;
    }

    public override void _Ready()
    {
        SetupBody(new Vector3(1.4f, 1.7f, 1.4f));
        var metal = MeshFactory.Metal(new Color(0.15f, 0.16f, 0.19f), 0.4f);
        var dark = MeshFactory.Metal(new Color(0.07f, 0.07f, 0.09f), 0.6f);
        var neon = new Color(0.3f, 1f, 0.8f);
        foreach (var (x, z) in new[] { (-0.55f, -0.55f), (0.55f, -0.55f), (-0.55f, 0.55f), (0.55f, 0.55f) })
            AddMesh(new BoxMesh { Size = new Vector3(0.12f, 0.9f, 0.12f) }, metal, new Vector3(x, 0.45f, z));
        AddMesh(new BoxMesh { Size = new Vector3(1.3f, 0.35f, 1.3f) }, dark, new Vector3(0, 0.3f, 0));
        AddMesh(new BoxMesh { Size = new Vector3(1.32f, 0.05f, 1.32f) }, MeshFactory.Emissive(neon, neon, 0.9f), new Vector3(0, 0.5f, 0));
        var funnelMat = MeshFactory.Metal(new Color(0.32f, 0.34f, 0.38f), 0.3f);
        funnelMat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        AddMesh(new CylinderMesh { TopRadius = 0.85f, BottomRadius = 0.28f, Height = 0.8f, RadialSegments = 16, CapTop = false }, funnelMat, new Vector3(0, 1.3f, 0));
        AddMesh(new CylinderMesh { TopRadius = 0.26f, BottomRadius = 0.26f, Height = 0.05f, RadialSegments = 12 }, MeshFactory.Emissive(neon, neon, 1.4f), new Vector3(0, 0.92f, 0));
        Ring = new Node3D { Position = new Vector3(0, 1.72f, 0) };
        AddChild(Ring);
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.84f, OuterRadius = 0.89f, Rings = 32, RingSegments = 6 }, MaterialOverride = MeshFactory.Emissive(neon, neon, 1.1f) };
        Ring.AddChild(ring);
        AddChild(new OmniLight3D { LightColor = neon, LightEnergy = 1.1f, OmniRange = 6f, Position = new Vector3(0, 1.8f, 0) });
        Label = MakeLabel("FUNIL", neon, 2.45f, 52);
        _count = new Label3D
        {
            FontSize = 34, OutlineSize = 10, PixelSize = 0.005f, Modulate = new Color(0.85f, 1f, 0.95f),
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 2.15f, 0), VisibilityRangeEnd = 20f,
        };
        AddChild(_count);
    }

    public override void _Process(double delta)
    {
        base._Process(delta);
        int q = GameState.Instance?.HopperQueued(BuildingId) ?? 0;
        string t = BeltLength < 0 ? Loc.T("sem esteira") : q > 0 ? Loc.F("na fila: {0}", q) : "";
        if (_count.Text != t) _count.Text = t;
    }

    public override string GetPrompt()
    {
        if (BuildingManager.Instance?.Active == true) return "";
        var gs = GameState.Instance;
        if (gs.InventoryCount > 0) return Loc.F("{0} Colocar {1} fragmentos no funil", InputHints.Interact, gs.InventoryCount);
        return BeltLength < 0 ? Loc.T("Funil sem esteira (sem caminho até a Estação de Venda)") : Loc.F("Funil · esteira de {0:0} m até a Estação de Venda", BeltLength);
    }

    public override void Interact(PlayerController player)
    {
        int n = GameState.Instance.DepositInventoryInHopper(BuildingId);
        if (n > 0) AudioManager.Instance?.Play("sell", -6f, 1.1f);
    }
}

// =========================================================================== esteira
public partial class Belt : Node3D
{
    public int BuildingId;
    public Color Accent;
    public float Length { get; private set; }
    public float Feed;
    List<Vector3> _pts;
    float[] _cum;
    readonly List<float> _s = new();
    readonly List<MaterialDef> _items = new();
    MultiMeshInstance3D _mmi;
    MultiMesh _mm;
    const float Lift = 0.24f, Half = 0.36f;

    public IReadOnlyList<MaterialDef> Items => _items;

    static Shader _beltShader, _itemShader;

    /// <summary>Altura do chão (para a esteira acompanhar o relevo entre os pontos).</summary>
    public System.Func<float, float, float> GroundAt;

    public void Build(List<Vector3> pts)
    {
        // acompanha o relevo: um ponto a cada 0,5 m na altura do chão
        if (GroundAt != null && pts.Count >= 2)
        {
            var dense = new List<Vector3> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                float len = new Vector2(b.X - a.X, b.Z - a.Z).Length();
                int n = Mathf.Max(1, Mathf.CeilToInt(len / 0.5f));
                for (int k = 1; k <= n; k++)
                {
                    var q = a.Lerp(b, k / (float)n);
                    bool last = i == pts.Count - 1 && k == n;
                    if (!last) q.Y = GroundAt(q.X, q.Z);
                    dense.Add(q);
                }
            }
            pts = dense;
        }
        _pts = pts;
        _cum = new float[pts.Count];
        for (int i = 1; i < pts.Count; i++) _cum[i] = _cum[i - 1] + pts[i - 1].DistanceTo(pts[i]);
        Length = _cum[^1];

        _beltShader ??= new Shader
        {
            Code = @"shader_type spatial;
render_mode cull_disabled;
uniform vec4 accent : source_color;
uniform float speed = 5.0;
void fragment() {
	if (!FRONT_FACING) NORMAL = -NORMAL;
	if (UV.x > 1.05) {
		// laterais: metal escuro
		ALBEDO = vec3(0.09, 0.095, 0.11); METALLIC = 0.6; ROUGHNESS = 0.45;
	} else {
		float side = abs(UV.x - 0.5) * 2.0;
		float v = UV.y - TIME * speed;
		float chev = smoothstep(0.86, 0.92, fract(v * 0.7 + side * 0.3)) * (1.0 - smoothstep(0.7, 0.8, side));
		float rail = step(0.8, side);
		float line = step(0.88, side) * (1.0 - step(0.94, side));
		ALBEDO = mix(vec3(0.035, 0.037, 0.045), vec3(0.16, 0.17, 0.2), rail);
		METALLIC = rail * 0.7;
		ROUGHNESS = mix(0.85, 0.35, rail);
		EMISSION = accent.rgb * (chev * 0.35 + line * 0.9);
	}
}"
        };
        var mat = new ShaderMaterial { Shader = _beltShader };
        mat.SetShaderParameter("accent", Accent);
        mat.SetShaderParameter("speed", BuildData.BeltSpeed * 0.8f);

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < pts.Count; i++)
        {
            var d = i == 0 ? pts[1] - pts[0] : i == pts.Count - 1 ? pts[i] - pts[i - 1] : (pts[i + 1] - pts[i - 1]);
            d.Y = 0;
            if (d.LengthSquared() < 0.0001f) d = Vector3.Forward;
            var side = d.Normalized().Cross(Vector3.Up) * Half;
            var c = pts[i] + Vector3.Up * Lift;
            float v = _cum[i];
            Ls[i] = c - side; Rs[i] = c + side;
            if (i == 0) continue;
            var l0 = Ls[i - 1]; var r0 = Rs[i - 1]; var l1 = Ls[i]; var r1 = Rs[i];
            float v0 = _cum[i - 1];
            Quad(st, l0, r0, r1, l1, new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v), new Vector2(0, v), Vector3.Up);
            // laterais (descem até o chão) com a faixa neon
            var down = Vector3.Down * (Lift + 0.08f);
            Quad(st, l0 + down, l0, l1, l1 + down, new Vector2(1.1f, v0), new Vector2(1.1f, v0), new Vector2(1.1f, v), new Vector2(1.1f, v), -side.Normalized());
            Quad(st, r0, r0 + down, r1 + down, r1, new Vector2(1.1f, v0), new Vector2(1.1f, v0), new Vector2(1.1f, v), new Vector2(1.1f, v), side.Normalized());
        }
        var mesh = st.Commit();
        AddChild(new MeshInstance3D { Name = "BeltMesh", Mesh = mesh, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });

        var metal = MeshFactory.Metal(new Color(0.18f, 0.19f, 0.22f), 0.35f);
        // receptor na Estação de Venda
        Sample(Length, out var end, out var endDir);
        var recv = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.0f, 0.7f, 0.6f) }, MaterialOverride = metal };
        AddChild(recv);
        recv.GlobalTransform = new Transform3D(Basis.LookingAt(endDir, Vector3.Up), end + Vector3.Up * 0.35f);
        var mouth = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.8f, 0.4f, 0.02f) }, MaterialOverride = MeshFactory.Emissive(Accent, Accent, 2.5f) };
        recv.AddChild(mouth);
        mouth.Position = new Vector3(0, 0.05f, 0.31f);

        // fragmentos andando na esteira
        _itemShader ??= new Shader
        {
            Code = @"shader_type spatial;
void fragment() { ALBEDO = COLOR.rgb * 0.6; EMISSION = COLOR.rgb * 1.6; ROUGHNESS = 0.3; }"
        };
        int cap = Mathf.Clamp((int)(Length / (BuildData.BeltSpeed * BuildData.BeltInterval)) + 8, 16, 4000);
        _mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = MeshFactory.CrystalPrism(5, 0.09f, 0.24f, 0.4f) };
        _mm.InstanceCount = cap;
        _mm.VisibleInstanceCount = 0;
        _mmi = new MultiMeshInstance3D { Multimesh = _mm, MaterialOverride = new ShaderMaterial { Shader = _itemShader }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_mmi);
    }

    // buffers temporários para montar a malha
    Vector3[] _ls, _rs;
    Vector3[] Ls => _ls ??= new Vector3[_pts.Count];
    Vector3[] Rs => _rs ??= new Vector3[_pts.Count];

    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 n)
    {
        void V(Vector3 p, Vector2 uv) { st.SetNormal(n); st.SetUV(uv); st.AddVertex(p); }
        V(a, ua); V(b, ub); V(c, uc);
        V(a, ua); V(c, uc); V(d, ud);
    }

    public void Sample(float s, out Vector3 pos, out Vector3 dir)
    {
        s = Mathf.Clamp(s, 0, Length);
        int lo = 0, hi = _cum.Length - 1;
        while (hi - lo > 1) { int mid = (lo + hi) / 2; if (_cum[mid] <= s) lo = mid; else hi = mid; }
        float seg = _cum[hi] - _cum[lo];
        float t = seg > 0.0001f ? (s - _cum[lo]) / seg : 0;
        pos = _pts[lo].Lerp(_pts[hi], t);
        dir = _pts[hi] - _pts[lo];
        if (dir.LengthSquared() < 0.0001f) dir = Vector3.Forward;
        else dir = dir.Normalized();
        if (Mathf.Abs(dir.Y) > 0.98f) dir = Vector3.Forward;
    }

    public void Push(MaterialDef m)
    {
        if (m == null) return;
        if (_items.Count >= _mm.InstanceCount) { GameState.Instance.SellFromBelt(m); return; }
        _s.Add(0);
        _items.Add(m);
    }

    /// <summary>Anda a esteira; devolve o que chegou à Estação de Venda.</summary>
    public List<MaterialDef> Advance(float ds)
    {
        var arrived = new List<MaterialDef>();
        for (int i = _s.Count - 1; i >= 0; i--)
        {
            _s[i] += ds;
            if (_s[i] >= Length) { arrived.Add(_items[i]); _s.RemoveAt(i); _items.RemoveAt(i); }
        }
        if (_mm != null && Visible)
        {
            _mm.VisibleInstanceCount = _items.Count;
            for (int i = 0; i < _items.Count; i++)
            {
                Sample(_s[i], out var p, out var dir);
                var basis = Basis.LookingAt(dir, Vector3.Up).Rotated(Vector3.Up, _s[i] * 0.7f) * Basis.FromEuler(new Vector3(Mathf.Pi / 2, 0, 0));
                _mm.SetInstanceTransform(i, new Transform3D(basis, p + Vector3.Up * (Lift + 0.09f)));
                _mm.SetInstanceColor(i, _items[i].Glow);
            }
        }
        return arrived;
    }
}

// =========================================================================== drone
public partial class DroneBot : Node3D
{
    public BuildingManager Manager;
    enum Mode { Follow, Fetch, ToHopper }
    Mode _mode = Mode.Follow;
    ShardManager.Shard _target;
    Hopper _hopper;
    readonly List<MaterialDef> _cargo = new();
    float _scan, _t, _idle;
    readonly List<Node3D> _rotors = new();
    Label3D _cargoLabel;
    PhysicsRayQueryParameters3D _ray;

    public int CargoCount => _cargo.Count;

    public override void _Ready()
    {
        var shell = MeshFactory.Metal(new Color(0.14f, 0.15f, 0.18f), 0.3f);
        var dark = MeshFactory.Metal(new Color(0.06f, 0.06f, 0.08f), 0.5f);
        var neon = new Color(0.45f, 0.85f, 1f);
        var body = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.26f, Height = 0.24f, RadialSegments = 16, Rings = 8 }, MaterialOverride = shell };
        AddChild(body);
        AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.25f, OuterRadius = 0.29f, Rings = 24, RingSegments = 6 }, MaterialOverride = MeshFactory.Emissive(neon, neon, 1.1f) });
        var eye = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.07f, Height = 0.14f }, MaterialOverride = MeshFactory.Emissive(neon, neon, 1.8f), Position = new Vector3(0, -0.02f, -0.24f) };
        AddChild(eye);
        foreach (var (x, z) in new[] { (0.36f, 0.36f), (-0.36f, 0.36f), (0.36f, -0.36f), (-0.36f, -0.36f) })
        {
            var arm = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.04f, 0.5f) }, MaterialOverride = dark, Position = new Vector3(x * 0.5f, 0.03f, z * 0.5f), Rotation = new Vector3(0, Mathf.Atan2(x, z), 0) };
            AddChild(arm);
            var rotor = new Node3D { Position = new Vector3(x, 0.08f, z) };
            AddChild(rotor);
            rotor.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.17f, BottomRadius = 0.17f, Height = 0.01f, RadialSegments = 16 }, MaterialOverride = MeshFactory.Unshaded(new Color(0.5f, 0.75f, 0.9f, 0.08f), true) });
            rotor.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.32f, 0.012f, 0.04f) }, MaterialOverride = dark });
            _rotors.Add(rotor);
        }
        AddChild(new OmniLight3D { LightColor = new Color(0.8f, 0.92f, 1f), LightEnergy = 1.3f, OmniRange = 11f, Position = new Vector3(0, -0.45f, 0) });
        _cargoLabel = new Label3D
        {
            FontSize = 30, OutlineSize = 8, PixelSize = 0.004f, Modulate = new Color(0.8f, 0.95f, 1f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 0.4f, 0), VisibilityRangeEnd = 15f,
        };
        AddChild(_cargoLabel);
    }

    /// <summary>No save, a carga vai para a mochila (se couber) para não se perder.</summary>
    public void FlushCargoTo(SaveData d)
    {
        var gs = GameState.Instance;
        for (int i = _cargo.Count - 1; i >= 0; i--)
            if (gs.AddItem(_cargo[i].Id) > 0) _cargo.RemoveAt(i);
    }

    bool Visible3D(Vector3 from, Vector3 to)
    {
        _ray ??= new PhysicsRayQueryParameters3D { CollisionMask = PlayerController.WorldLayer };
        _ray.From = from; _ray.To = to;
        return GetWorld3D().DirectSpaceState.IntersectRay(_ray).Count == 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _t += dt;
        foreach (var r in _rotors) r.RotateY(dt * 40f);
        var player = Manager?.GetPlayer?.Invoke();
        var gs = GameState.Instance;
        if (player == null || gs == null || !Visible || !gs.IsPlaying) { Release(); return; }
        var ppos = player.GlobalPosition;
        // ficou muito longe (elevador, desmaio): reaparece perto do jogador
        if (GlobalPosition.DistanceTo(ppos) > 45f) { Release(); GlobalPosition = ppos + new Vector3(0.8f, 2.4f, 0.8f); }

        Vector3 goal;
        float speed = 9f;
        switch (_mode)
        {
            case Mode.Fetch:
                if (_target == null || !_target.Alive) { _target = null; _mode = Mode.Follow; goto default; }
                goal = _target.Position + Vector3.Up * 0.35f;
                if (GlobalPosition.DistanceTo(goal) < 0.45f)
                {
                    var m = ShardManager.Instance?.Take(_target);
                    _target = null;
                    if (m != null) { _cargo.Add(m); AudioManager.Instance?.Play3D("pickup", GlobalPosition, -10f, 1.3f); }
                    _mode = Mode.Follow;
                }
                break;
            case Mode.ToHopper:
                if (_hopper == null || !IsInstanceValid(_hopper)) { _hopper = null; _mode = Mode.Follow; goto default; }
                goal = _hopper.SinkPoint + Vector3.Up * 0.5f;
                if (GlobalPosition.DistanceTo(goal) < 0.6f)
                {
                    foreach (var m in _cargo) gs.HopperAdd(_hopper.BuildingId, m.Id);
                    _cargo.Clear();
                    AudioManager.Instance?.Play3D("sell", GlobalPosition, -10f, 1.2f);
                    _hopper = null;
                    _mode = Mode.Follow;
                }
                break;
            default:
            {
                // fica um pouco atrás e acima do jogador, flutuando
                var back = player.GlobalTransform.Basis.Z;
                goal = ppos + back * 1.4f + player.GlobalTransform.Basis.X * 0.9f + Vector3.Up * (2.3f + Mathf.Sin(_t * 2f) * 0.12f);
                speed = 7f;
                // entrega a carga quando está perto do jogador
                if (_cargo.Count > 0 && GlobalPosition.DistanceTo(player.BodyCenter) < 3f)
                {
                    for (int i = _cargo.Count - 1; i >= 0; i--)
                        if (gs.AddItem(_cargo[i].Id) > 0) _cargo.RemoveAt(i);
                }
                // mochila cheia: leva ao funil mais próximo que esteja à vista
                if (_cargo.Count > 0 && gs.FreeSpace == 0)
                {
                    _hopper = Manager.Hoppers.Where(h => h.Visible && h.GlobalPosition.DistanceTo(ppos) < 30f && Visible3D(GlobalPosition, h.SinkPoint + Vector3.Up * 0.5f))
                                             .OrderBy(h => h.GlobalPosition.DistanceSquaredTo(ppos)).FirstOrDefault();
                    if (_hopper != null) { _mode = Mode.ToHopper; break; }
                }
                // procura fragmentos soltos à vista
                _scan -= dt;
                if (_scan <= 0 && _cargo.Count < BuildData.DroneCargo && ShardManager.Instance != null)
                {
                    _scan = 0.35f;
                    var me = GlobalPosition;
                    var s = ShardManager.Instance.NearestLoose(ppos, BuildData.DroneRange, p => Visible3D(me, p + Vector3.Up * 0.3f));
                    if (s != null) { s.Claimed = true; _target = s; _mode = Mode.Fetch; }
                }
                break;
            }
        }
        var to = goal - GlobalPosition;
        float dist = to.Length();
        if (dist > 0.01f)
        {
            var step = to / dist * Mathf.Min(dist, speed * dt * Mathf.Clamp(dist, 0.4f, 1.5f));
            GlobalPosition += step;
            var flat = new Vector3(to.X, 0, to.Z);
            if (flat.LengthSquared() > 0.04f)
            {
                float yaw = Mathf.Atan2(-flat.X, -flat.Z);
                Rotation = new Vector3(Mathf.Clamp(-dist * 0.05f, -0.25f, 0), Mathf.LerpAngle(Rotation.Y, yaw, Mathf.Min(1, dt * 5f)), 0);
            }
        }
        string label = _cargo.Count > 0 ? $"{_cargo.Count}/{BuildData.DroneCargo}" : "";
        if (_cargoLabel.Text != label) _cargoLabel.Text = label;
        _cargoLabel.Modulate = gs.FreeSpace == 0 && _cargo.Count > 0 ? new Color(1f, 0.5f, 0.4f) : new Color(0.8f, 0.95f, 1f);
    }

    void Release()
    {
        if (_target != null && _target.Alive) _target.Claimed = false;
        _target = null;
        _hopper = null;
        _mode = Mode.Follow;
    }
}
