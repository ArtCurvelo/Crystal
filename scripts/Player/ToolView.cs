using System;
using CrystalExcavator.Core;
using CrystalExcavator.World;
using CrystalExcavator.Data;
using Godot;
using OreArchetypeAlias = CrystalExcavator.Data.OreArchetype;

namespace CrystalExcavator.Player;

/// <summary>
/// Escavadeira de feixe em primeira pessoa (corpo laranja/cinza, tela ciano, emissor),
/// com animação procedural (balanço, recuo a cada pulso de dano) e feixe de energia até o ponto atingido.
/// Cores e tamanho do emissor evoluem com o tier de dano.
/// </summary>
public partial class ToolView : Node3D
{
    /// <summary>Camada visual da arma (a lanterna do capacete exclui esta camada).</summary>
    public const uint ToolLayer = 1u << 9;
    Node3D _pivot;
    Node3D _model;
    Node3D _muzzle;
    StandardMaterial3D _screenMat, _coilMat, _beamMat, _haloMat;
    MeshInstance3D _beam, _beamCore, _flare;
    float _recoilT = 1f;
    float _recoilDuration = 0.15f;
    float _bobT;
    float _time;
    Vector3 _restPos = new(0.26f, -0.25f, -0.4f);
    int _tier = -1;
    Vector2 _sway;
    float _beamAlpha;
    float _ringSpin;

    public bool Firing;            // definido pelo jogador a cada quadro
    public Vector3 BeamEnd;        // ponto global atingido
    public Color BeamColor = new(0.4f, 0.9f, 1f);

    public static readonly Color[] TierColors =
    {
        new(0.1f, 0.95f, 1f), new(1f, 0.15f, 0.8f), new(0.45f, 1f, 0.2f), new(1f, 0.55f, 0.05f), new(0.6f, 0.25f, 1f), new(1f, 0.9f, 0.2f)
    };

    public override void _Ready()
    {
        _pivot = new Node3D { Position = _restPos };
        AddChild(_pivot);
        Rebuild(GameState.Instance?.ToolTier ?? 0);
        if (GameState.Instance != null) { GameState.Instance.UpgradesChanged += OnUpgrades; GameState.Instance.ToolChanged += OnUpgrades; }
        BuildBeam();
    }

    public override void _ExitTree()
    {
        if (GameState.Instance != null) { GameState.Instance.UpgradesChanged -= OnUpgrades; GameState.Instance.ToolChanged -= OnUpgrades; }
    }

    void OnUpgrades()
    {
        int t = GameState.Instance.ToolTier;
        if (t != _tier || GameState.Instance.ToolBroken != _broken) Rebuild(t);
    }

    MeshInstance3D Part(Node3D parent, Mesh mesh, Material mat, Vector3 pos, Vector3? rot = null)
    {
        var m = new MeshInstance3D
        {
            Mesh = mesh, MaterialOverride = mat, Position = pos,
            Rotation = rot ?? Vector3.Zero,
            Layers = ToolLayer, // a lanterna do capacete não ilumina a arma (estourava de tão perto)
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        parent.AddChild(m);
        return m;
    }

    static StandardMaterial3D Lit(Color c, float rough = 0.55f) => new() { AlbedoColor = c, Roughness = rough, Metallic = 0.15f, RimEnabled = true, Rim = 0.3f, RimTint = 0.6f };

    Node3D _rings;
    StandardMaterial3D _stripMat, _coreMat;
    OmniLight3D _coreLight, _muzzleLight, _impactLight;
    MeshInstance3D _halo;

    static StandardMaterial3D Metal(Color c, float rough = 0.35f, float metal = 0.75f) => new()
    {
        AlbedoColor = c, Roughness = rough, Metallic = metal,
    };

    static StandardMaterial3D Neon(Color c, float energy) => new()
    {
        AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = energy, Roughness = 0.3f,
    };

    /// <summary>
    /// Ferramenta de feixe "neon": corpo grafite anguloso, faixas neon laterais, tela holográfica,
    /// núcleo de energia em vidro (ilumina a própria arma), anéis giratórios e pontas emissoras.
    /// Cada nível muda a cor, o número de anéis/pontas e o volume.
    /// </summary>
    /// <summary>Tipo do que está na mão: picareta, broca, laser ou as mãos (ferramenta quebrada).</summary>
    public enum HeldKind { Pickaxe, Drill, Laser, Hands }
    public HeldKind Kind { get; private set; } = HeldKind.Pickaxe;
    bool _broken;
    Node3D _bit;
    float _drillSpin;

    void Rebuild(int toolIndex)
    {
        var gs = GameState.Instance;
        _tier = toolIndex;
        _broken = gs?.ToolBroken ?? false;
        if (_model != null) _equipT = 0f; // troca de ferramenta: animação de sacar
        _model?.QueueFree();
        _model = new Node3D();
        _pivot.AddChild(_model);
        _rings = null; _muzzle = null; _bit = null;
        _screenMat = _coilMat = _stripMat = _coreMat = null; _coreLight = null; _muzzleLight = null;
        var t = ToolData.Of(toolIndex);
        Kind = _broken ? HeldKind.Hands : t.Kind switch { ToolKind.Drill => HeldKind.Drill, ToolKind.Laser => HeldKind.Laser, _ => HeldKind.Pickaxe };
        switch (Kind)
        {
            case HeldKind.Pickaxe: BuildPickaxe(t); break;
            case HeldKind.Drill: BuildDrill(t); break;
            case HeldKind.Hands: BuildHands(); break;
            default: BuildGun(toolIndex >= ToolData.All.Count - 1 ? 5 : 3); break;
        }
    }

    static StandardMaterial3D HeadMat(ToolDef t)
    {
        var c = ToolData.HeadColor(t);
        if (t.Ore == null) return Metal(new Color(0.55f, 0.5f, 0.45f), 0.9f, 0f); // madeira: ponta de pedra lascada
        var m = GameData.Mat(t.Ore);
        bool gem = m != null && m.Archetype is OreArchetypeAlias.Crystal or OreArchetypeAlias.Shards or OreArchetypeAlias.Geode;
        var mat = Metal(gem ? c.Darkened(0.25f) : c, gem ? 0.2f : 0.3f, gem ? 0.25f : 0.85f);
        // pedras preciosas: brilho leve só nas bordas (sem virar uma mancha de cor chapada)
        if (gem) { mat.RimEnabled = true; mat.Rim = 0.6f; mat.RimTint = 0.8f; mat.EmissionEnabled = true; mat.Emission = ToolData.GlowColor(t) * 0.5f; mat.EmissionEnergyMultiplier = 0.12f; }
        return mat;
    }

    /// <summary>Mão com luva fechada segurando o cabo: palma, quatro dedos curvados, polegar, punho e manga.</summary>
    void Grip(Vector3 at)
    {
        var glove = Metal(new Color(0.3f, 0.28f, 0.27f), 0.75f, 0.05f);
        var leather = Metal(new Color(0.35f, 0.22f, 0.13f), 0.8f, 0f);
        var sleeve = Metal(new Color(0.18f, 0.14f, 0.26f), 0.7f, 0.05f);
        var stripe = Metal(new Color(0.95f, 0.55f, 0.15f), 0.6f, 0.1f);
        // palma (atrás do cabo) e dedos curvados à frente
        Part(_model, new CapsuleMesh { Radius = 0.035f, Height = 0.1f, RadialSegments = 10, Rings = 3 }, glove, at + new Vector3(0.028f, 0, 0.01f));
        for (int f = 0; f < 4; f++)
            Part(_model, new CapsuleMesh { Radius = 0.014f, Height = 0.055f, RadialSegments = 8, Rings = 2 }, glove,
                at + new Vector3(-0.012f, 0.035f - f * 0.023f, -0.02f), new Vector3(Mathf.Pi / 2, 0, 0.15f));
        Part(_model, new BoxMesh { Size = new Vector3(0.012f, 0.085f, 0.03f) }, leather, at + new Vector3(-0.026f, 0.0f, -0.024f)); // reforço de couro nos nós dos dedos
        Part(_model, new CapsuleMesh { Radius = 0.013f, Height = 0.05f, RadialSegments = 8, Rings = 2 }, glove, at + new Vector3(0.01f, 0.05f, 0.012f), new Vector3(0.3f, 0, -0.9f)); // polegar
        // punho e manga
        Part(_model, new CylinderMesh { TopRadius = 0.036f, BottomRadius = 0.04f, Height = 0.04f, RadialSegments = 10, Rings = 1 }, leather, at + new Vector3(0.03f, -0.065f, 0.04f), new Vector3(-0.45f, 0, 0));
        Part(_model, new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.055f, Height = 0.22f, RadialSegments = 10, Rings = 1 }, sleeve, at + new Vector3(0.035f, -0.12f, 0.13f), new Vector3(-1.1f, 0, 0));
        Part(_model, new CylinderMesh { TopRadius = 0.047f, BottomRadius = 0.05f, Height = 0.02f, RadialSegments = 10, Rings = 1 }, stripe, at + new Vector3(0.033f, -0.095f, 0.085f), new Vector3(-1.1f, 0, 0));
    }

    /// <summary>
    /// "Chifre" da cabeça da picareta: seção quase quadrada (octógono arredondado) varrida ao longo de um
    /// arco que desce para a ponta, afinando. chisel = lâmina larga e achatada (enxó) em vez de ponta.
    /// sign = −1 espelha para o outro lado (com a ordem dos triângulos invertida).
    /// </summary>
    static ArrayMesh Horn(float len, float w0, float h0, float droop, bool chisel, float sign, int segs = 10)
    {
        const int sides = 8;
        var rings = new Vector3[segs + 1][];
        for (int i = 0; i <= segs; i++)
        {
            float t = i / (float)segs;
            var c = new Vector3(sign * t * len, -droop * t * t, 0);
            var tan = new Vector3(sign * len, -2f * droop * t, 0).Normalized();
            var n = new Vector3(-tan.Y * sign, tan.X * sign, 0).Normalized(); // "para cima" ao longo do arco
            float ease = 1f - (1f - t) * (1f - t);
            float h = chisel ? Mathf.Lerp(h0, h0 * 0.12f, ease) : Mathf.Lerp(h0, h0 * 0.06f, t);
            float w = chisel ? Mathf.Lerp(w0, w0 * 1.25f, t) : Mathf.Lerp(w0, w0 * 0.08f, ease);
            var ring = new Vector3[sides];
            for (int k = 0; k < sides; k++)
            {
                float a = (k + 0.5f) * Mathf.Tau / sides;
                float ca = MathF.Cos(a), sa = MathF.Sin(a);
                float r = MathF.Pow(MathF.Pow(MathF.Abs(ca), 4) + MathF.Pow(MathF.Abs(sa), 4), -0.25f); // superelipse
                ring[k] = c + n * (ca * r * h * 0.5f) + Vector3.Back * (sa * r * w * 0.5f);
            }
            rings[i] = ring;
        }
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            if (sign > 0) { st.AddVertex(a); st.AddVertex(b); st.AddVertex(c); }
            else { st.AddVertex(a); st.AddVertex(c); st.AddVertex(b); }
        }
        for (int i = 0; i < segs; i++)
            for (int k = 0; k < sides; k++)
            {
                int k2 = (k + 1) % sides;
                var a = rings[i][k]; var b = rings[i][k2]; var c = rings[i + 1][k2]; var d = rings[i + 1][k];
                Tri(a, c, b); Tri(a, d, c);
            }
        // tampas: base (encostada no miolo) e ponta
        var c0 = Vector3.Zero; var c1 = rings[segs][0] * 0f;
        foreach (var v in rings[segs]) c1 += v / sides;
        for (int k = 0; k < sides; k++)
        {
            int k2 = (k + 1) % sides;
            Tri(c0, rings[0][k], rings[0][k2]);
            Tri(c1, rings[segs][k2], rings[segs][k]);
        }
        st.GenerateNormals();
        return st.Commit();
    }

    /// <summary>
    /// Picareta: cabo de madeira com empunhadura de couro e pomo; cabeça curva com ponta de um lado e
    /// lâmina do outro. Madeira = pedra lascada amarrada com corda; metais = cabeça inteira do minério;
    /// gemas = miolo de aço com pontas de cristal e uma gema encravada.
    /// </summary>
    void BuildPickaxe(ToolDef t)
    {
        float tier = t.Index / (float)Math.Max(1, ToolData.All.Count - 1);
        var wood = Metal(new Color(0.56f, 0.37f, 0.2f), 0.75f, 0f);
        var woodDark = Metal(new Color(0.36f, 0.22f, 0.12f), 0.8f, 0f);
        var leather = Metal(new Color(0.24f, 0.13f, 0.08f), 0.9f, 0f);
        var steel = Metal(new Color(0.6f, 0.62f, 0.67f), 0.42f, 0.5f); // metal claro: na caverna escura o metálico puro ficava preto
        steel.RimEnabled = true; steel.Rim = 0.35f; steel.RimTint = 0.5f;
        var rope = Metal(new Color(0.72f, 0.6f, 0.4f), 0.95f, 0f);
        var head = HeadMat(t);
        bool stone = t.Ore == null;
        var m = stone ? null : GameData.Mat(t.Ore);
        bool gem = m != null && m.Archetype is OreArchetypeAlias.Crystal or OreArchetypeAlias.Shards or OreArchetypeAlias.Geode;
        var core = stone ? Metal(new Color(0.5f, 0.47f, 0.43f), 0.95f, 0f) : gem ? steel : head;
        var tips = stone ? core : head;

        var shaft = new Node3D { Rotation = new Vector3(-0.55f, 0.15f, 0.18f), Position = new Vector3(0.0f, -0.04f, 0) };
        _model.AddChild(shaft);
        var tmp = _model; _model = shaft;
        // cabo levemente afinado com veio (duas tonalidades) e pomo
        Part(_model, new CylinderMesh { TopRadius = 0.017f, BottomRadius = 0.021f, Height = 0.6f, RadialSegments = 10, Rings = 3 }, wood, new Vector3(0, 0.12f, 0));
        for (int i = 0; i < 3; i++)
            Part(_model, new BoxMesh { Size = new Vector3(0.003f, 0.16f + i * 0.05f, 0.004f) }, woodDark, new Vector3(MathF.Cos(i * 2.1f) * 0.018f, 0.06f + i * 0.09f, MathF.Sin(i * 2.1f) * 0.018f));
        Part(_model, new SphereMesh { Radius = 0.026f, Height = 0.04f, RadialSegments = 10, Rings = 5 }, stone ? woodDark : steel, new Vector3(0, -0.185f, 0));
        // empunhadura: tiras de couro em espiral (anéis levemente inclinados)
        for (int i = 0; i < 6; i++)
            Part(_model, new TorusMesh { InnerRadius = 0.017f, OuterRadius = 0.026f, Rings = 10, RingSegments = 6 }, leather,
                new Vector3(0, -0.15f + i * 0.026f, 0), new Vector3(0.18f, 0, 0.12f));
        var top = new Vector3(0, 0.4f, 0);
        if (!stone)
        {
            // colar de metal sob a cabeça e cunha
            Part(_model, new CylinderMesh { TopRadius = 0.026f, BottomRadius = 0.023f, Height = 0.05f, RadialSegments = 10, Rings = 1 }, steel, top + new Vector3(0, -0.055f, 0));
            Part(_model, new BoxMesh { Size = new Vector3(0.012f, 0.012f, 0.03f) }, woodDark, top + new Vector3(0, 0.036f, 0));
        }
        // a cabeça fica num nó próprio girado 90° no eixo do cabo: a ponta aponta para FRENTE (para o alvo)
        // e o golpe acerta com a ponta, não de lado
        var headNode = new Node3D { Position = top, Rotation = new Vector3(0, Mathf.Pi / 2, 0) };
        _model.AddChild(headNode);
        _model = headNode;
        top = Vector3.Zero;
        // miolo (olho) da cabeça
        Part(_model, new BoxMesh { Size = new Vector3(0.062f, 0.064f, 0.058f) }, core, top);
        Part(_model, new BoxMesh { Size = new Vector3(0.07f, 0.014f, 0.064f) }, stone ? core : steel, top + new Vector3(0, 0.028f, 0));
        float len = 0.16f + tier * 0.07f;
        float h0 = 0.05f + tier * 0.01f, w0 = 0.044f + tier * 0.008f;
        Part(_model, Horn(len, w0, h0, 0.05f + tier * 0.02f, false, 1f), tips, top + new Vector3(0.026f, 0, 0));
        Part(_model, Horn(len * 0.72f, w0, h0, 0.04f + tier * 0.015f, !stone, -1f), tips, top + new Vector3(-0.026f, 0, 0));
        if (stone)
        {
            // amarração de corda cruzada prendendo a pedra
            foreach (float a in new[] { 0.7f, -0.7f })
                Part(_model, new CylinderMesh { TopRadius = 0.006f, BottomRadius = 0.006f, Height = 0.11f, RadialSegments = 5, Rings = 1 }, rope, top + new Vector3(0, -0.02f, 0.031f), new Vector3(0, 0, a));
            for (int i = 0; i < 3; i++)
                Part(_model, new TorusMesh { InnerRadius = 0.019f, OuterRadius = 0.027f, Rings = 10, RingSegments = 5 }, rope, top + new Vector3(0, -0.045f - i * 0.014f, 0));
        }
        if (gem)
        {
            // gema encravada no olho, dos dois lados
            var glow = ToolData.GlowColor(t);
            var inlay = new StandardMaterial3D { AlbedoColor = m.Color, Roughness = 0.1f, Metallic = 0.2f, EmissionEnabled = true, Emission = glow, EmissionEnergyMultiplier = 0.5f, RimEnabled = true, Rim = 0.7f };
            foreach (float z in new[] { -1f, 1f })
                Part(_model, new SphereMesh { Radius = 0.014f, Height = 0.016f, RadialSegments = 6, Rings = 2 }, inlay, top + new Vector3(0, 0, z * 0.03f));
        }
        if (t.Index >= 4)
            foreach (float x in new[] { -0.022f, 0.022f }) // rebites
                foreach (float z in new[] { -0.03f, 0.03f })
                    Part(_model, new SphereMesh { Radius = 0.005f, Height = 0.01f, RadialSegments = 6, Rings = 3 }, steel, top + new Vector3(x, -0.018f, z));
        _model = tmp;
        Grip(new Vector3(0.0f, -0.07f, 0.06f));
        _model.Position = new Vector3(0.02f, -0.08f, 0.02f);
        _model.Rotation = new Vector3(0.05f, 0.2f, 0f);
        _model.Scale = Vector3.One * 0.8f;
    }

    /// <summary>
    /// Broca de mão: motor cilíndrico com aletas e grade de ventilação, anel aceso na cor do minério,
    /// empunhadura de pistola com gatilho, alça lateral, mandril e broca longa em espiral (gira ao minerar).
    /// </summary>
    void BuildDrill(ToolDef t)
    {
        // metal claro e pouco metálico: na caverna escura o metálico puro ficava preto
        var body = Metal(new Color(0.55f, 0.57f, 0.62f), 0.4f, 0.4f);
        body.RimEnabled = true; body.Rim = 0.3f; body.RimTint = 0.5f;
        var dark = Metal(new Color(0.18f, 0.18f, 0.21f), 0.6f, 0.2f);
        var steel = Metal(new Color(0.75f, 0.77f, 0.82f), 0.3f, 0.45f);
        var rubber = Metal(new Color(0.12f, 0.12f, 0.13f), 0.9f, 0f);
        var shell = HeadMat(t);
        var glow = ToolData.GlowColor(t);
        var rx = new Vector3(Mathf.Pi / 2, 0, 0);
        // motor (eixo ao longo de Z, broca para −Z)
        Part(_model, new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.06f, Height = 0.2f, RadialSegments = 16, Rings = 1 }, body, new Vector3(0, 0, 0.06f), rx);
        Part(_model, new CylinderMesh { TopRadius = 0.062f, BottomRadius = 0.062f, Height = 0.07f, RadialSegments = 16, Rings = 1 }, shell, new Vector3(0, 0, 0.0f), rx); // capa na cor do minério
        for (int k = 0; k < 4; k++) // aletas de refrigeração
            Part(_model, new CylinderMesh { TopRadius = 0.064f, BottomRadius = 0.064f, Height = 0.008f, RadialSegments = 16, Rings = 1 }, dark, new Vector3(0, 0, 0.09f + k * 0.022f), rx);
        Part(_model, new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.058f, Height = 0.03f, RadialSegments = 16, Rings = 1 }, dark, new Vector3(0, 0, 0.175f), rx); // tampa traseira
        Part(_model, new BoxMesh { Size = new Vector3(0.05f, 0.012f, 0.06f) }, dark, new Vector3(0, 0.058f, 0.12f)); // grade
        Part(_model, new TorusMesh { InnerRadius = 0.058f, OuterRadius = 0.066f, Rings = 16, RingSegments = 6 }, Neon(glow.Darkened(0.2f), 0.6f), new Vector3(0, 0, -0.035f), rx);
        // mandril
        Part(_model, new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.045f, Height = 0.05f, RadialSegments = 12, Rings = 1 }, steel, new Vector3(0, 0, -0.065f), rx);
        // empunhadura de pistola, gatilho e alça lateral
        Part(_model, new BoxMesh { Size = new Vector3(0.04f, 0.12f, 0.05f) }, rubber, new Vector3(0, -0.09f, 0.12f), new Vector3(-0.25f, 0, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.012f, 0.03f, 0.012f) }, Neon(new Color(1f, 0.5f, 0.15f), 0.4f), new Vector3(0, -0.06f, 0.085f));
        Part(_model, new CylinderMesh { TopRadius = 0.014f, BottomRadius = 0.014f, Height = 0.12f, RadialSegments = 8, Rings = 1 }, rubber, new Vector3(-0.1f, -0.01f, -0.01f), new Vector3(0, 0, Mathf.Pi / 2));
        // broca: núcleo cônico + faixas helicoidais (gira)
        _bit = new Node3D { Position = new Vector3(0, 0, -0.09f) };
        _model.AddChild(_bit);
        Part(_bit, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.028f, Height = 0.24f, RadialSegments = 10, Rings = 1 }, shell, new Vector3(0, 0, -0.12f), new Vector3(-Mathf.Pi / 2, 0, 0));
        for (int k = 0; k < 9; k++)
        {
            float u = k / 9f;
            float r = 0.032f * (1f - u * 0.85f);
            Part(_bit, new TorusMesh { InnerRadius = r * 0.55f, OuterRadius = r, Rings = 12, RingSegments = 4 }, steel,
                new Vector3(0, 0, -0.02f - u * 0.2f), new Vector3(Mathf.Pi / 2 + 0.35f, k * 0.7f, 0));
        }
        Grip(new Vector3(0.005f, -0.1f, 0.12f));
        _model.Position = new Vector3(0.03f, 0.02f, -0.02f);
        _model.Rotation = new Vector3(0.12f, 0.3f, 0.06f);
        _model.Scale = Vector3.One * 0.95f;
    }

    /// <summary>Ferramenta quebrou: a mão fechada segurando o toco do cabo, com farpas na ponta quebrada.</summary>
    void BuildHands()
    {
        var wood = Metal(new Color(0.5f, 0.33f, 0.18f), 0.85f, 0f);
        var splinter = Metal(new Color(0.75f, 0.58f, 0.36f), 0.9f, 0f);
        var stump = new Node3D { Position = new Vector3(0.0f, 0.0f, 0.0f), Rotation = new Vector3(-0.45f, 0.2f, 0.25f) };
        _model.AddChild(stump);
        var keep = _model; _model = stump;
        Part(_model, new CylinderMesh { TopRadius = 0.017f, BottomRadius = 0.02f, Height = 0.2f, RadialSegments = 10, Rings = 1 }, wood, new Vector3(0, 0.04f, 0));
        for (int k = 0; k < 5; k++)
        {
            float a = Mathf.Tau * k / 5f;
            Part(_model, new CylinderMesh { TopRadius = 0f, BottomRadius = 0.007f, Height = 0.03f + (k % 3) * 0.015f, RadialSegments = 4, Rings = 1 }, splinter,
                new Vector3(Mathf.Cos(a) * 0.011f, 0.15f + (k % 3) * 0.006f, Mathf.Sin(a) * 0.011f), new Vector3(Mathf.Sin(a) * 0.25f, 0, -Mathf.Cos(a) * 0.25f));
        }
        _model = keep;
        Grip(new Vector3(0.0f, -0.04f, 0.02f));
        _model.Position = new Vector3(0.0f, 0.06f, 0.0f);
        _model.Rotation = new Vector3(0.1f, 0.3f, -0.1f);
        _model.Scale = Vector3.One * 1.3f;
    }

    /// <summary>Laser (fim do jogo): o emissor de feixe com faixas neon, núcleo e anéis.</summary>
    void BuildGun(int tier)
    {
        var accent = TierColors[tier];
        var second = TierColors[(tier + 2) % TierColors.Length];
        var graphite = Metal(new Color(0.09f, 0.09f, 0.12f), 0.32f, 0.8f);
        var plate = Metal(new Color(0.2f, 0.21f, 0.27f), 0.28f, 0.85f);
        var chrome = Metal(new Color(0.75f, 0.78f, 0.85f), 0.12f, 1f);
        var rubber = Metal(new Color(0.06f, 0.06f, 0.07f), 0.9f, 0f);
        var glove = Metal(new Color(0.1f, 0.1f, 0.12f), 0.8f, 0.1f);
        var sleeve = Metal(new Color(0.16f, 0.12f, 0.22f), 0.7f, 0.1f);
        _stripMat = Neon(accent.Darkened(0.35f), 0.45f);
        _coilMat = Neon(accent, 1f);
        _screenMat = new StandardMaterial3D { AlbedoColor = accent.Darkened(0.6f), EmissionEnabled = true, Emission = accent, EmissionEnergyMultiplier = 0.6f };
        _coreMat = Neon(second.Lerp(Colors.White, 0.35f), 1.6f);
        var rx = new Vector3(Mathf.Pi / 2, 0, 0);
        float bulk = 1f + tier * 0.06f;

        // ---- corpo anguloso
        Part(_model, new BoxMesh { Size = new Vector3(0.1f, 0.095f, 0.26f) * bulk }, graphite, new Vector3(0, 0, 0.06f));
        Part(_model, new BoxMesh { Size = new Vector3(0.115f, 0.035f, 0.2f) * bulk }, plate, new Vector3(0, 0.06f * bulk, 0.04f));
        Part(_model, new BoxMesh { Size = new Vector3(0.118f, 0.03f, 0.12f) * bulk }, plate, new Vector3(0, -0.055f * bulk, 0.09f));
        Part(_model, new BoxMesh { Size = new Vector3(0.07f, 0.03f, 0.08f) * bulk }, graphite, new Vector3(0, 0.03f * bulk, 0.2f), new Vector3(0.5f, 0, 0));
        // faixas neon nas laterais e no topo
        foreach (float side in new[] { -1f, 1f })
        {
            Part(_model, new BoxMesh { Size = new Vector3(0.004f, 0.008f, 0.22f) * bulk }, _stripMat, new Vector3(side * 0.0525f * bulk, 0.02f, 0.05f));
            Part(_model, new BoxMesh { Size = new Vector3(0.004f, 0.006f, 0.12f) * bulk }, _stripMat, new Vector3(side * 0.0525f * bulk, -0.025f, 0.09f));
        }
        Part(_model, new BoxMesh { Size = new Vector3(0.01f, 0.004f, 0.18f) * bulk }, _stripMat, new Vector3(0, 0.079f * bulk, 0.04f));
        // tela holográfica inclinada sobre o corpo
        Part(_model, new BoxMesh { Size = new Vector3(0.07f, 0.045f, 0.004f) }, _screenMat, new Vector3(-0.02f, 0.11f * bulk, 0.12f), new Vector3(-0.5f, 0.15f, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.075f, 0.05f, 0.006f) }, graphite, new Vector3(-0.02f, 0.108f * bulk, 0.123f), new Vector3(-0.5f, 0.15f, 0));
        // núcleo de energia em vidro (lado direito), com luz própria
        var glass = new StandardMaterial3D { AlbedoColor = new Color(0.6f, 0.9f, 1f, 0.18f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = 0.02f, Metallic = 0.2f, RimEnabled = true, Rim = 1f };
        Part(_model, new CylinderMesh { TopRadius = 0.026f, BottomRadius = 0.026f, Height = 0.11f, RadialSegments = 14, Rings = 1 }, glass, new Vector3(0.07f * bulk, 0.02f, 0.06f), rx);
        Part(_model, new CylinderMesh { TopRadius = 0.012f, BottomRadius = 0.012f, Height = 0.1f, RadialSegments = 10, Rings = 1 }, _coreMat, new Vector3(0.07f * bulk, 0.02f, 0.06f), rx);
        foreach (float zz in new[] { 0.005f, 0.115f })
            Part(_model, new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.012f, RadialSegments = 14, Rings = 1 }, chrome, new Vector3(0.07f * bulk, 0.02f, zz), rx);
        _coreLight = new OmniLight3D { LightColor = second, LightEnergy = 0.6f, OmniRange = 0.6f, Position = new Vector3(0.08f, 0.03f, 0.06f), ShadowEnabled = false };
        _model.AddChild(_coreLight);
        if (tier >= 2)
        {
            // trilho superior com mira holográfica
            Part(_model, new BoxMesh { Size = new Vector3(0.02f, 0.015f, 0.18f) }, chrome, new Vector3(0.02f, 0.1f * bulk, 0.0f));
            Part(_model, new BoxMesh { Size = new Vector3(0.03f, 0.03f, 0.004f) }, Neon(second, 0.8f), new Vector3(0.02f, 0.125f * bulk, -0.06f));
        }
        if (tier >= 4)
        {
            // aletas de dissipação
            for (int i = 0; i < 4; i++)
                Part(_model, new BoxMesh { Size = new Vector3(0.13f * bulk, 0.004f, 0.012f) }, plate, new Vector3(0, -0.03f + i * 0.02f, 0.17f));
        }
        // empunhadura, luva e manga
        Part(_model, new BoxMesh { Size = new Vector3(0.05f, 0.13f, 0.06f) }, rubber, new Vector3(0, -0.1f, 0.12f), new Vector3(-0.3f, 0, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.085f, 0.095f, 0.1f) }, glove, new Vector3(0.005f, -0.12f, 0.13f), new Vector3(-0.3f, 0, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.1f, 0.12f, 0.22f) }, sleeve, new Vector3(0.02f, -0.2f, 0.28f), new Vector3(-0.55f, 0, 0));
        Part(_model, new BoxMesh { Size = new Vector3(0.104f, 0.012f, 0.04f) }, _stripMat, new Vector3(0.02f, -0.155f, 0.2f), new Vector3(-0.55f, 0, 0));

        // ---- cano: luva cromada, anéis giratórios, pontas emissoras
        Part(_model, new CylinderMesh { TopRadius = 0.045f * bulk, BottomRadius = 0.05f * bulk, Height = 0.08f, RadialSegments = 14, Rings = 1 }, plate, new Vector3(0, 0.005f, -0.1f), rx);
        Part(_model, new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.022f, Height = 0.2f + tier * 0.02f, RadialSegments = 10, Rings = 1 }, chrome, new Vector3(0, 0.005f, -0.2f - tier * 0.01f), rx);
        _rings = new Node3D { Position = new Vector3(0, 0.005f, -0.19f) };
        _model.AddChild(_rings);
        int rings = 2 + Mathf.Min(tier, 3);
        for (int i = 0; i < rings; i++)
        {
            var ring = Part(_rings, new TorusMesh { InnerRadius = 0.036f + i * 0.002f, OuterRadius = 0.046f + i * 0.003f, Rings = 18, RingSegments = 6 }, i % 2 == 0 ? _coilMat : plate, new Vector3(0, 0, -i * 0.028f), rx);
            // marcas para o giro ser visível
            Part(ring, new BoxMesh { Size = new Vector3(0.01f, 0.012f, 0.01f) }, _stripMat, new Vector3(0.045f, 0, 0));
        }
        float z = -0.19f - rings * 0.028f - 0.03f;
        int prongs = tier >= 3 ? 4 : 3;
        for (int i = 0; i < prongs; i++)
        {
            float a = Mathf.Tau * i / prongs;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
            var prong = Part(_model, new BoxMesh { Size = new Vector3(0.012f, 0.012f, 0.07f) }, plate, new Vector3(0, 0.005f, z) + dir * 0.034f);
            prong.Rotation = new Vector3(0, 0, a);
            Part(_model, MeshFactory.Spike(4, 0.008f, 0.03f), _coilMat, new Vector3(0, 0.005f, z - 0.045f) + dir * 0.03f, new Vector3(-Mathf.Pi / 2, 0, 0));
        }
        z -= 0.07f;
        // lente do emissor
        Part(_model, new SphereMesh { Radius = 0.016f, Height = 0.032f, RadialSegments = 12, Rings = 6 }, _coreMat, new Vector3(0, 0.005f, z + 0.03f));
        // luz suave sobre a arma (a lanterna aponta para frente e não a ilumina)
        _model.AddChild(new OmniLight3D { LightColor = new Color(0.85f, 0.9f, 1f), LightEnergy = 0.7f, OmniRange = 0.7f, Position = new Vector3(-0.12f, 0.16f, 0.05f), ShadowEnabled = false });
        _muzzle = new Node3D { Position = new Vector3(0, 0.005f, z) };
        _model.AddChild(_muzzle);
        _muzzleLight = new OmniLight3D { LightColor = accent, LightEnergy = 0f, OmniRange = 2.5f, ShadowEnabled = false };
        _muzzle.AddChild(_muzzleLight);
        _model.Rotation = new Vector3(0.1f, 0.32f, 0.06f);
        _model.Scale = Vector3.One * 0.8f;
    }

    void BuildBeam()
    {
        _beamMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(BeamColor, 0.6f),
            NoDepthTest = false,
        };
        var coreMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = new Color(1, 1, 1, 0.9f),
        };
        // o feixe fica no espaço do mundo (não herda o balanço da arma)
        var holder = new Node3D { TopLevel = true };
        AddChild(holder);
        _beam = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1, RadialSegments = 8, Rings = 1 }, MaterialOverride = _beamMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _beamCore = new MeshInstance3D { Mesh = _beam.Mesh, MaterialOverride = coreMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _flare = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 8, Rings = 4 }, MaterialOverride = _beamMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        // halo em cone: fino no cano (não cobre a tela) e largo no impacto
        _halo = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1, BottomRadius = 0.15f, Height = 1, RadialSegments = 10, Rings = 1 }, MaterialOverride = _haloMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false };
        _impactLight = new OmniLight3D { LightEnergy = 0f, OmniRange = 3.5f, ShadowEnabled = false };
        holder.AddChild(_beam);
        holder.AddChild(_beamCore);
        holder.AddChild(_flare);
        holder.AddChild(_halo);
        holder.AddChild(_impactLight);
    }

    /// <summary>Pulso de dano: recuo curto com duração ligada à cadência.</summary>
    /// <summary>Atraso entre o clique e o acerto (o golpe "cai" na pedra nesse instante).</summary>
    public static float HitDelay(float interval) => Mathf.Min(interval * 0.45f, 0.14f);
    /// <summary>Fração da animação do golpe em que a picareta encosta na pedra.</summary>
    const float ImpactAt = 0.42f;
    float _equipT = 1f;
    /// <summary>Capturas de tela: congela o golpe nesta fração (0..1); negativo = normal.</summary>
    public float DebugPhase = -1f;

    public void Swing(float interval)
    {
        // picareta: duração escolhida para o impacto da animação coincidir com o acerto
        _recoilDuration = Kind is HeldKind.Pickaxe or HeldKind.Hands
            ? Mathf.Clamp(HitDelay(interval) / ImpactAt, 0.12f, interval * 0.95f)
            : Mathf.Clamp(interval * 0.8f, 0.06f, 0.2f);
        _recoilT = 0f;
    }

    static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
    static float EaseIn(float x) => x * x;

    /// <summary>
    /// Golpe de picareta em três fases: puxa para trás (antecipação), desce rápido até o impacto
    /// (com um tranco) e volta suavemente. Retorna (ergue, gira para baixo, avanço, tranco).
    /// </summary>
    static (float raise, float chop, float reach, float jolt) PickPose(float t)
    {
        const float windEnd = 0.24f;
        if (t >= 1f) return (0, 0, 0, 0);
        if (t < windEnd)
        {
            float a = EaseOut(t / windEnd);
            return (a, -a * 0.55f, -a * 0.02f, 0);
        }
        if (t < ImpactAt)
        {
            float a = EaseIn((t - windEnd) / (ImpactAt - windEnd));
            return (1f - a * 1.15f, -0.55f + a * 1.75f, a * 0.1f, 0);
        }
        float r = (t - ImpactAt) / (1f - ImpactAt);
        float back = EaseOut(r);
        float jolt = r < 0.25f ? MathF.Sin(r / 0.25f * MathF.PI) : 0f;
        return (-0.15f * (1f - back), 1.2f * (1f - back), 0.1f * (1f - back), jolt);
    }

    public void AddSway(Vector2 mouseDelta) => _sway += mouseDelta * 0.0006f;

    public void UpdateView(float dt, float moveSpeed01, bool grounded)
    {
        _time += dt;
        if (grounded && moveSpeed01 > 0.05f) _bobT += dt * (6f + moveSpeed01 * 6f);
        float bobX = MathF.Sin(_bobT) * 0.01f * moveSpeed01;
        float bobY = MathF.Abs(MathF.Cos(_bobT)) * 0.012f * moveSpeed01;
        _sway = _sway.Lerp(Vector2.Zero, Mathf.Min(1, dt * 10f));
        _sway = _sway.Clamp(new Vector2(-0.06f, -0.06f), new Vector2(0.06f, 0.06f));

        float kick = 0;
        if (DebugPhase >= 0f) _recoilT = DebugPhase;
        else if (_recoilT < 1f)
        {
            _recoilT = Mathf.Min(1f, _recoilT + dt / _recoilDuration);
            kick = MathF.Sin(_recoilT * MathF.PI) * (1f - _recoilT * 0.5f);
        }
        float idle = MathF.Sin(_time * 1.6f) * 0.004f;
        float vibrate = Firing ? MathF.Sin(_time * 70f) * 0.0015f : 0f;
        // sacar a ferramenta (troca, compra, quebra): sobe de baixo girando
        if (_equipT < 1f) _equipT = Mathf.Min(1f, _equipT + dt / 0.35f);
        float equip = 1f - EaseOut(_equipT);
        // balanço de andar com leve rolagem; respiração parada
        float walkRoll = MathF.Sin(_bobT) * 0.03f * moveSpeed01;
        float breathe = MathF.Sin(_time * 1.3f) * 0.012f;
        if (Kind is HeldKind.Pickaxe or HeldKind.Hands)
        {
            var (raise, chop, reach, jolt) = PickPose(_recoilT);
            float shake = jolt * MathF.Sin(_time * 90f) * 0.006f;
            float strike = Mathf.Max(0f, chop);
            _pivot.Position = _restPos + new Vector3(bobX - _sway.X + shake - raise * 0.01f - strike * 0.05f,
                -bobY + _sway.Y + idle + raise * 0.05f - reach * 0.2f - equip * 0.35f,
                -reach * 0.8f + raise * 0.01f);
            // +X puxa a cabeça para trás (antecipação), −X a leva para frente (golpe);
            // no golpe a ferramenta vira um pouco para o centro da tela (a ponta cai perto da mira)
            _pivot.Rotation = new Vector3(raise * 0.42f - chop * 0.5f + breathe - equip * 0.9f - jolt * 0.04f,
                raise * 0.06f + strike * 0.18f - _sway.X * 2f,
                raise * 0.1f + walkRoll + _sway.X * 1.5f);
        }
        else
        {
            _pivot.Position = _restPos + new Vector3(bobX - _sway.X + vibrate * (Kind == HeldKind.Drill ? 2f : 1f), -bobY + _sway.Y + idle - equip * 0.3f, kick * 0.035f);
            _pivot.Rotation = new Vector3(kick * 0.06f + breathe * 0.5f - equip * 0.8f, -_sway.X, walkRoll);
        }
        _drillSpin = Mathf.Lerp(_drillSpin, Firing ? 40f : 0f, Mathf.Min(1f, dt * 6f));
        _bit?.RotateObjectLocal(Vector3.Forward, _drillSpin * dt);

        // tela e bobinas pulsam com o disparo
        float pulse = Firing ? 1.2f + 0.8f * MathF.Abs(MathF.Sin(_time * 12f)) : 0.6f + 0.3f * MathF.Sin(_time * 2f);
        // brilho contido: só passa do limiar do bloom ao atirar
        if (_screenMat != null) _screenMat.EmissionEnergyMultiplier = 0.4f + pulse * 0.15f;
        if (_coilMat != null) _coilMat.EmissionEnergyMultiplier = 0.7f + pulse * 0.8f + kick * 1.5f;
        if (_stripMat != null) _stripMat.EmissionEnergyMultiplier = 0.35f + pulse * 0.2f;
        if (_coreMat != null) _coreMat.EmissionEnergyMultiplier = 1.3f + pulse * 1.2f;
        if (_coreLight != null) _coreLight.LightEnergy = 0.2f + pulse * 0.2f;
        // anéis giram mais rápido ao disparar
        _ringSpin = Mathf.Lerp(_ringSpin, Firing ? 22f : 1.5f, Mathf.Min(1f, dt * 4f));
        _rings?.RotateObjectLocal(Vector3.Forward, _ringSpin * dt);

        UpdateBeam(dt);
    }

    void UpdateBeam(float dt)
    {
        // só o laser tem feixe; picareta/broca/mãos acertam de perto
        bool charged = GameState.Instance == null || !GameState.Instance.OutOfAmmo;
        _beamAlpha = Mathf.MoveToward(_beamAlpha, Firing && Kind == HeldKind.Laser && charged ? 1f : 0f, dt * (Firing ? 14f : 8f));
        bool show = _beamAlpha > 0.01f && _muzzle != null;
        _beam.Visible = _beamCore.Visible = _flare.Visible = _halo.Visible = show;
        if (_muzzleLight != null) _muzzleLight.LightEnergy = _beamAlpha * 2.2f;
        _impactLight.LightEnergy = _beamAlpha * 3f;
        if (_muzzle == null) { _beam.Visible = _beamCore.Visible = _flare.Visible = _halo.Visible = false; return; }
        if (!show) return;
        var start = _muzzle.GlobalPosition;
        var end = BeamEnd;
        var dir = end - start;
        float len = dir.Length();
        if (len < 0.05f) { _beam.Visible = _beamCore.Visible = false; return; }
        var mid = start + dir * 0.5f;
        var up = dir / len;
        var basis = BasisFromUp(up);
        float flicker = 0.85f + 0.15f * MathF.Sin(_time * 50f) + 0.1f * MathF.Sin(_time * 23f);
        float w = 0.03f * flicker * (1f + _tier * 0.12f) * (0.6f + 0.4f * _beamAlpha);
        _beam.GlobalTransform = new Transform3D(basis.ScaleLocal(new Vector3(w, len, w)), mid);
        _beamCore.GlobalTransform = new Transform3D(basis.ScaleLocal(new Vector3(w * 0.35f, len, w * 0.35f)), mid);
        _flare.GlobalTransform = new Transform3D(Basis.Identity.ScaleLocal(Vector3.One * (0.06f + 0.03f * flicker) * _beamAlpha), end);
        _beamMat.AlbedoColor = new Color(BeamColor.Lightened(0.2f) * 2.2f, 0.6f * _beamAlpha);
        _halo.GlobalTransform = new Transform3D(basis.ScaleLocal(new Vector3(w * 3.2f, len, w * 3.2f)), mid);
        _haloMat.AlbedoColor = new Color(BeamColor * 1.4f, 0.09f * _beamAlpha * flicker);
        _impactLight.GlobalPosition = end - dir / len * 0.3f;
        _impactLight.LightColor = BeamColor;
        if (_muzzleLight != null) _muzzleLight.LightColor = BeamColor;
    }

    static Basis BasisFromUp(Vector3 up)
    {
        var x = up.Cross(MathF.Abs(up.Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized();
        var z = x.Cross(up).Normalized();
        return new Basis(x, up, z);
    }
}
