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

// =========================================================================== esteira
public partial class Belt : Node3D
{
    public int BuildingId;
    public Color Accent;
    /// <summary>Termina na Estação de Venda (receptor grande) ou em outra esteira (emenda).</summary>
    public bool ToStation = true;
    public float Length { get; private set; }
    public IReadOnlyList<Vector3> Points => _pts;
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
        // receptor na Estação de Venda (ou emenda baixa quando despeja em outra esteira)
        Sample(Length, out var end, out var endDir);
        var recv = new MeshInstance3D { Mesh = new BoxMesh { Size = ToStation ? new Vector3(1.0f, 0.7f, 0.6f) : new Vector3(0.8f, 0.3f, 0.5f) }, MaterialOverride = metal };
        AddChild(recv);
        recv.GlobalTransform = new Transform3D(Basis.LookingAt(endDir, Vector3.Up), end + Vector3.Up * (ToStation ? 0.35f : 0.45f));
        var mouth = new MeshInstance3D { Mesh = new BoxMesh { Size = ToStation ? new Vector3(0.8f, 0.4f, 0.02f) : new Vector3(0.6f, 0.14f, 0.02f) }, MaterialOverride = MeshFactory.Emissive(Accent, Accent, 2.5f) };
        recv.AddChild(mouth);
        mouth.Position = new Vector3(0, 0.05f, ToStation ? 0.31f : 0.26f);

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

    /// <summary>Põe um fragmento na esteira, a <paramref name="s"/> metros do início. Devolve false se está lotada.</summary>
    public bool Push(MaterialDef m, float s = 0)
    {
        if (m == null) return true;
        if (_items.Count >= _mm.InstanceCount) return false;
        _s.Add(Mathf.Clamp(s, 0, Length));
        _items.Add(m);
        return true;
    }

    /// <summary>Ponto da esteira mais perto de p (no plano): distância, metros desde o início e posição.</summary>
    public float Nearest(Vector3 p, out float s, out Vector3 q)
    {
        float best = float.MaxValue; s = 0; q = _pts[0];
        for (int i = 1; i < _pts.Count; i++)
        {
            var a = _pts[i - 1]; var b = _pts[i];
            var ab = new Vector2(b.X - a.X, b.Z - a.Z);
            float l2 = ab.LengthSquared();
            float t = l2 < 1e-6f ? 0 : Mathf.Clamp(new Vector2(p.X - a.X, p.Z - a.Z).Dot(ab) / l2, 0, 1);
            var c = a.Lerp(b, t);
            float d = new Vector2(p.X - c.X, p.Z - c.Z).Length();
            if (Mathf.Abs(p.Y - c.Y) > 2.5f) d += 50f; // outro andar
            if (d < best) { best = d; q = c; s = _cum[i - 1] + (_cum[i] - _cum[i - 1]) * t; }
        }
        return best;
    }

    /// <summary>Anda a esteira; devolve o que chegou ao fim.</summary>
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

