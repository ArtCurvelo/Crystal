using System;
using System.Collections.Generic;
using CrystalExcavator.Core;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>
/// HQ animada (abertura, flashback do Velho Bram e final). Cada quadro vem em 3 camadas (tools/comic2.py):
/// cenário (_bg), personagens (_fg) e onomatopeias/efeitos (_fx). O cenário desliza devagar, os personagens
/// andam em paralaxe e "respiram", as onomatopeias estouram na tela, cada cena tem partículas (chuva, faíscas,
/// brasas, poeira de luz) e os quadros entram com transições diferentes (empurrão, clarão, cortina, queda).
/// Clique/Espaço/Enter/A avança; Esc/B ou "Pular" encerra.
/// </summary>
public partial class IntroComic : CanvasLayer
{
    public static readonly string[] Captions =
    {
        "Eu era só mais um morador da Vila dos Ferreiros, com um sonho: abrir a minha própria oficina.",
        "Pedi Lúmens emprestados à Guilda dos Ferreiros. Assinei o contrato sem ler as letras miúdas...",
        "A oficina nunca deu certo. E a dívida só crescia, dia após dia.",
        "Quando os ferreiros vieram cobrar, eu não tinha nada. Eles me deram uma escolha... que não era bem uma escolha.",
        "Virei escravo da guilda. Agora cavo cristais nas minas deles e vendo tudo aos ferreiros, até pagar o último Lúmen.",
        "Mas os mineiros mais velhos sussurram sobre o Coração Ancestral, lá no fundo da mina. Quem o quebrar terá riqueza para comprar a própria liberdade.",
    };

    public enum Transition { Fade, Slide, Flash, Wipe, Drop }
    public enum Fx { None, Rain, Sparks, Embers, Motes, Shards, Dust }

    /// <summary>Como cada quadro se mexe: transição de entrada, partículas, tremida e para onde a câmera desliza.</summary>
    public readonly record struct Scene(Transition In, Fx Particles, float Shake = 0f, float Drift = 1f, Color? Tint = null);

    static readonly Dictionary<string, Scene> Scenes = new()
    {
        ["panel_1"] = new(Transition.Fade, Fx.Motes, 0f, 1f, new Color(1f, 0.92f, 0.65f)),
        ["panel_2"] = new(Transition.Slide, Fx.Dust, 0f, -1f),
        ["panel_3"] = new(Transition.Wipe, Fx.Rain, 0f, 1f),
        ["panel_4"] = new(Transition.Flash, Fx.Sparks, 1f, -1f),
        ["panel_5"] = new(Transition.Slide, Fx.Dust, 0.35f, 1f),
        ["panel_6"] = new(Transition.Drop, Fx.Motes, 0f, -1f, new Color(0.55f, 0.95f, 1f)),
        ["bram_1"] = new(Transition.Fade, Fx.Dust, 0f, 1f),
        ["bram_2"] = new(Transition.Flash, Fx.Shards, 1.1f, -1f),
        ["bram_3"] = new(Transition.Wipe, Fx.Embers, 0.25f, 1f),
        ["bram_4"] = new(Transition.Drop, Fx.Dust, 0.5f, -1f),
        ["end_1"] = new(Transition.Fade, Fx.Embers, 0.4f, 1f),
        ["end_2"] = new(Transition.Slide, Fx.Motes, 0f, -1f, new Color(0.6f, 1f, 0.75f)),
        ["end_3"] = new(Transition.Flash, Fx.Shards, 1.2f, 1f, new Color(1f, 0.9f, 0.5f)),
        ["end_4"] = new(Transition.Wipe, Fx.Motes, 0f, -1f, new Color(1f, 0.85f, 0.6f)),
    };

    public static Scene SceneOf(string stem) =>
        Scenes.TryGetValue(stem, out var s) ? s : new Scene(Transition.Slide, Fx.Dust);

    public event Action Finished;
    string[] _caps = Captions;
    string _prefix = "panel_";
    public bool IsPlaying { get; private set; }
    public int Index { get; private set; }
    /// <summary>Transição do quadro atual ainda em andamento (testes).</summary>
    public bool Transitioning => _trans != null && _trans.IsValid() && _trans.IsRunning();
    /// <summary>Quadro atual carregado em camadas (false = imagem única, sem animação de paralaxe).</summary>
    public bool Layered => _cur?.Layered ?? false;
    public Scene Current => _cur?.Scene ?? default;
    /// <summary>"Reduzir tremidas" nas configurações: menos movimento, partículas e tremida.</summary>
    static bool Calm => Settings.Data?.ReduceShake ?? false;

    Control _root, _frame, _shaker, _box;
    ColorRect _flash;
    Label _text, _hint, _counter;
    Tween _typeTween, _trans;
    bool _typing, _ending;
    Page _cur, _old;
    readonly Page[] _pages = new Page[2];
    float _shake;
    readonly RandomNumberGenerator _rng = new();

    /// <summary>Um quadro na tela: as 3 camadas, as partículas e o estado da transição.</summary>
    sealed partial class Page : Control
    {
        public TextureRect Bg, Fg, FxLayer;
        public CpuParticles2D Particles;
        public ColorRect Dim, Edge;
        public Scene Scene;
        public bool Layered;
        public float Time;
        /// <summary>Deslocamento (fração do quadro), largura revelada (cortina), zoom extra e escurecimento.</summary>
        public float SlideX, SlideY, Reveal = 1f, Zoom, Darken;

        public Page()
        {
            ClipContents = true;
            MouseFilter = MouseFilterEnum.Ignore;
            Bg = Layer(); Fg = Layer();
            Particles = new CpuParticles2D { Emitting = false, LocalCoords = true };
            AddChild(Particles);
            FxLayer = Layer();
            Dim = new ColorRect { Color = new Color(0, 0, 0, 0), MouseFilter = MouseFilterEnum.Ignore };
            AddChild(Dim);
            Edge = new ColorRect { Color = new Color(0.06f, 0.04f, 0.07f), MouseFilter = MouseFilterEnum.Ignore, Visible = false };
            AddChild(Edge);
        }

        TextureRect Layer()
        {
            var t = new TextureRect
            {
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            AddChild(t);
            return t;
        }
    }

    public override void _Ready()
    {
        Layer = 40;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        _root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var bg = new ColorRect { Color = new Color(0.02f, 0.015f, 0.03f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(bg);

        // o quadro mantém 16:9 na área acima da legenda
        var area = new AspectRatioContainer { Ratio = 16f / 9f, MouseFilter = Control.MouseFilterEnum.Ignore };
        area.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        area.OffsetTop = 24; area.OffsetBottom = -170; area.OffsetLeft = 40; area.OffsetRight = -40;
        _root.AddChild(area);
        _frame = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, ClipContents = true };
        area.AddChild(_frame);
        _shaker = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _shaker.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _frame.AddChild(_shaker);
        for (int i = 0; i < 2; i++) { _pages[i] = new Page { Visible = false }; _shaker.AddChild(_pages[i]); }
        _flash = new ColorRect { Color = new Color(1, 0.98f, 0.92f, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        _flash.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _shaker.AddChild(_flash);
        // moldura de HQ (fica parada enquanto o conteúdo treme): borda de papel e o traço de tinta por dentro
        _frame.AddChild(Border(new Color(0.98f, 0.96f, 0.92f), 8, 0));
        _frame.AddChild(Border(new Color(0.06f, 0.04f, 0.07f), 3, 8));

        // caixa de fala (legenda) embaixo
        _box = new PanelContainer();
        _box.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.98f, 0.96f, 0.9f, 0.97f), new Color(0.08f, 0.06f, 0.1f), 12, 4, 26, 14));
        _box.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _box.OffsetTop = -158; _box.OffsetBottom = -18; _box.OffsetLeft = 120; _box.OffsetRight = -120;
        _root.AddChild(_box);
        var v = new VBoxContainer();
        _box.AddChild(v);
        _text = UiTheme.Label("", 24, new Color(0.1f, 0.07f, 0.12f), true);
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        v.AddChild(_text);
        var foot = new HBoxContainer();
        v.AddChild(foot);
        _counter = UiTheme.Label("", 15, new Color(0.45f, 0.35f, 0.3f));
        _counter.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        foot.AddChild(_counter);
        _hint = UiTheme.Label("", 15, new Color(0.45f, 0.35f, 0.3f));
        foot.AddChild(_hint);

        var skip = UiTheme.Button("Pular história", Skip, 0, 16);
        skip.FocusMode = Control.FocusModeEnum.None;
        skip.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        skip.OffsetLeft = -230; skip.OffsetRight = -50; skip.OffsetTop = 40; skip.OffsetBottom = 84;
        skip.Name = "Skip";
        _root.AddChild(skip);
    }

    static Panel Border(Color c, int width, int inset)
    {
        var sb = new StyleBoxFlat { DrawCenter = false, BorderColor = c };
        sb.SetBorderWidthAll(width);
        var p = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", sb);
        p.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        p.OffsetLeft = inset; p.OffsetTop = inset; p.OffsetRight = -inset; p.OffsetBottom = -inset;
        return p;
    }

    /// <summary>Toca outra sequência de quadros (ex.: o flashback do Velho Bram): assets/story/{prefix}N.png.</summary>
    public void Play(string prefix, string[] captions)
    {
        _prefix = prefix;
        _caps = captions;
        Start();
    }

    public void Play() { _prefix = "panel_"; _caps = Captions; Start(); }

    void Start()
    {
        IsPlaying = true;
        _ending = false;
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        foreach (var p in _pages) { p.Visible = false; p.Particles.Emitting = false; }
        _cur = null;
        _root.Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(_root, "modulate:a", 1f, 0.5f);
        Show(0);
    }

    static Texture2D Tex(string path) => ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

    void Show(int i)
    {
        _trans?.Kill();
        FinishOld();
        Index = i;
        string stem = $"{_prefix}{i + 1}";
        var scene = SceneOf(stem);
        _old = _cur;
        _cur = _pages[_old == _pages[0] ? 1 : 0];
        var p = _cur;
        p.Scene = scene;
        var bg = Tex($"res://assets/story/{stem}_bg.png");
        var fg = Tex($"res://assets/story/{stem}_fg.png");
        p.Layered = bg != null && fg != null;
        p.Bg.Texture = p.Layered ? bg : Tex($"res://assets/story/{stem}.png");
        p.Fg.Texture = p.Layered ? fg : null;
        p.FxLayer.Texture = p.Layered ? Tex($"res://assets/story/{stem}_fx.png") : null;
        p.Time = 0f;
        p.SlideX = p.SlideY = p.Zoom = p.Darken = 0f;
        p.Reveal = 1f;
        p.Modulate = Colors.White;
        p.Visible = true;
        p.Edge.Visible = false;
        _shaker.MoveChild(p, 1); // a nova página fica por cima da antiga
        SetupParticles(p, scene);
        Layout();
        p.FxLayer.Modulate = new Color(1, 1, 1, 0);
        p.FxLayer.Scale = Vector2.One * 0.55f;

        var kind = i == 0 ? Transition.Fade : scene.In;
        if (_old == null && kind != Transition.Fade) kind = Transition.Fade;
        _trans = CreateTween().SetParallel();
        switch (kind)
        {
            case Transition.Fade:
                p.Modulate = new Color(1, 1, 1, 0);
                p.Zoom = 0.1f;
                _trans.TweenProperty(p, "modulate:a", 1f, 0.6f);
                _trans.TweenMethod(Callable.From<float>(z => p.Zoom = z), 0.1f, 0f, 1.1f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                if (_old != null) _trans.TweenProperty(_old, "modulate:a", 0f, 0.6f);
                break;
            case Transition.Slide:
                // empurrão: o quadro novo entra pela direita e empurra o antigo
                p.SlideX = 1f;
                p.Edge.Visible = true;
                _trans.TweenMethod(Callable.From<float>(x => { p.SlideX = x; if (_old != null) { _old.SlideX = (x - 1f) * 0.4f; _old.Darken = (1f - x) * 0.55f; } }), 1f, 0f, 0.75f)
                    .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
                Audio.AudioManager.Instance?.Play("swing", -14f, 0.7f);
                break;
            case Transition.Flash:
                // clarão: tela branca e o quadro novo "bate" com zoom
                if (_old != null) _old.Visible = false;
                p.Zoom = 0.22f;
                _flash.Color = _flash.Color with { A = 0.95f };
                _trans.TweenProperty(_flash, "color:a", 0f, 0.55f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                _trans.TweenMethod(Callable.From<float>(z => p.Zoom = z), 0.22f, 0f, 0.6f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
                Audio.AudioManager.Instance?.Play("thud", -6f, 0.8f);
                break;
            case Transition.Wipe:
                // cortina: o quadro novo é revelado da esquerda para a direita, com um traço de tinta na ponta
                p.Reveal = 0f;
                p.Edge.Visible = true;
                _trans.TweenMethod(Callable.From<float>(r => { p.Reveal = r; if (_old != null) _old.Darken = r * 0.4f; }), 0f, 1f, 0.7f)
                    .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                Audio.AudioManager.Instance?.Play("swing", -15f, 0.55f);
                break;
            case Transition.Drop:
                // queda: o quadro cai de cima e quica
                p.SlideY = -1f;
                p.Edge.Visible = true;
                _trans.TweenMethod(Callable.From<float>(y => { p.SlideY = y; if (_old != null) _old.Darken = (1f + y) * 0.5f; }), -1f, 0f, 0.85f)
                    .SetTrans(Tween.TransitionType.Bounce).SetEase(Tween.EaseType.Out);
                _trans.TweenCallback(Callable.From(() => Audio.AudioManager.Instance?.Play("thud", -9f, 0.7f))).SetDelay(0.32f);
                break;
        }
        _trans.Chain().TweenCallback(Callable.From(() => { p.Edge.Visible = false; FinishOld(); }));
        // a onomatopeia estoura depois que o quadro entra (com tremida nas cenas fortes)
        _trans.Chain().TweenCallback(Callable.From(() => PopFx(p))).SetDelay(0.15f);

        // a legenda dá um pulinho a cada quadro novo
        _box.PivotOffset = _box.Size / 2;
        _box.Scale = new Vector2(0.97f, 0.97f);
        CreateTween().TweenProperty(_box, "scale", Vector2.One, 0.35f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);

        string line = Loc.T(_caps[i]);
        _text.Text = line;
        _text.VisibleRatio = 0f;
        _typing = true;
        _typeTween?.Kill();
        _typeTween = CreateTween();
        _typeTween.TweenInterval(0.25f);
        _typeTween.TweenProperty(_text, "visible_ratio", 1f, Math.Clamp(line.Length * 0.028f, 0.8f, 4f));
        _typeTween.TweenCallback(Callable.From(() => _typing = false));
        _counter.Text = $"{i + 1} / {_caps.Length}";
        _hint.Text = i == _caps.Length - 1 ? Loc.T("Clique ou Espaço para começar") : Loc.T("Clique ou Espaço para continuar");
        Accessibility.Speak(line, true);
        Audio.AudioManager.Instance?.Play("click", -10f);
    }

    void FinishOld()
    {
        if (_old == null) return;
        _old.Visible = false;
        _old.Particles.Emitting = false;
        _old = null;
    }

    void PopFx(Page p)
    {
        if (p != _cur || p.FxLayer.Texture == null) { if (p.Scene.Shake > 0) Shake(p.Scene.Shake); return; }
        var t = CreateTween().SetParallel();
        t.TweenProperty(p.FxLayer, "modulate:a", 1f, 0.12f);
        t.TweenProperty(p.FxLayer, "scale", Vector2.One, 0.45f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        if (p.Scene.Shake > 0) Shake(p.Scene.Shake);
    }

    /// <summary>Treme a câmera do quadro (0..1+).</summary>
    public void Shake(float amount)
    {
        if (Calm) amount *= 0.25f;
        _shake = Mathf.Max(_shake, amount);
        if (amount > 0.6f) Audio.AudioManager.Instance?.Play("hit", -8f, 0.6f);
    }

    // ---- partículas -------------------------------------------------------------------------------

    static Texture2D _dot, _streak;
    static CanvasItemMaterial _addMat;

    static Texture2D Dot()
    {
        if (_dot != null) return _dot;
        const int n = 16;
        var img = Image.CreateEmpty(n, n, false, Image.Format.Rgba8);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).Length() / (n / 2f);
                float a = Mathf.Clamp(1f - d, 0f, 1f);
                img.SetPixel(x, y, new Color(1, 1, 1, a * a));
            }
        return _dot = ImageTexture.CreateFromImage(img);
    }

    static Texture2D Streak()
    {
        if (_streak != null) return _streak;
        const int w = 4, h = 32;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float a = (1f - Mathf.Abs(x - 1.5f) / 2f) * Mathf.Sin(Mathf.Pi * y / (h - 1));
                img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(a, 0, 1)));
            }
        return _streak = ImageTexture.CreateFromImage(img);
    }

    static Gradient Fade(Color c, float peak = 1f)
    {
        var g = new Gradient();
        g.SetColor(0, c with { A = 0 });
        g.SetColor(1, c with { A = 0 });
        g.AddPoint(0.2f, c with { A = peak });
        g.AddPoint(0.75f, c with { A = peak * 0.8f });
        return g;
    }

    void SetupParticles(Page p, Scene s)
    {
        var e = p.Particles;
        e.Emitting = false;
        e.Restart();
        if (s.Particles == Fx.None) return;
        var size = FrameSize();
        float W = size.X, H = size.Y, k = H / 600f;
        e.EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle;
        e.ParticleFlagAlignY = false;
        e.Gravity = Vector2.Zero;
        e.Spread = 20f;
        e.Direction = Vector2.Up;
        e.ScaleAmountMin = e.ScaleAmountMax = 1f;
        e.AngularVelocityMin = e.AngularVelocityMax = 0f;
        e.Texture = Dot();
        e.ColorRamp = null;
        Color tint = s.Tint ?? new Color(1f, 0.95f, 0.8f);
        bool glow = false;
        switch (s.Particles)
        {
            case Fx.Rain:
                e.Texture = Streak();
                e.ParticleFlagAlignY = true;
                e.Amount = 220;
                e.Lifetime = 0.75f;
                e.Position = new Vector2(W * 0.45f, -30 * k);
                e.EmissionRectExtents = new Vector2(W * 0.65f, 10);
                e.Direction = new Vector2(0.22f, 1f);
                e.Spread = 3f;
                e.InitialVelocityMin = 950 * k; e.InitialVelocityMax = 1250 * k;
                e.ScaleAmountMin = 0.7f * k; e.ScaleAmountMax = 1.3f * k;
                e.ColorRamp = Fade(new Color(0.75f, 0.85f, 1f), 0.55f);
                break;
            case Fx.Sparks:
                e.Texture = Streak();
                e.ParticleFlagAlignY = true;
                e.Amount = 120;
                e.Lifetime = 1.1f;
                e.Position = new Vector2(W * 0.5f, H * 0.78f);
                e.EmissionRectExtents = new Vector2(W * 0.18f, 12 * k);
                e.Direction = Vector2.Up;
                e.Spread = 55f;
                e.Gravity = new Vector2(0, 900 * k);
                e.InitialVelocityMin = 320 * k; e.InitialVelocityMax = 620 * k;
                e.ScaleAmountMin = 0.7f * k; e.ScaleAmountMax = 1.3f * k;
                e.ColorRamp = Fade(new Color(1f, 0.75f, 0.3f));
                glow = true;
                break;
            case Fx.Embers:
                e.Amount = 70;
                e.Lifetime = 4.5f;
                e.Position = new Vector2(W * 0.5f, H * 1.02f);
                e.EmissionRectExtents = new Vector2(W * 0.55f, 10);
                e.Spread = 25f;
                e.Gravity = new Vector2(12 * k, -20 * k);
                e.InitialVelocityMin = 40 * k; e.InitialVelocityMax = 110 * k;
                e.ScaleAmountMin = 0.6f * k; e.ScaleAmountMax = 1.3f * k;
                e.ColorRamp = Fade(new Color(1f, 0.45f, 0.15f), 1f);
                glow = true;
                break;
            case Fx.Shards:
                e.Amount = 60;
                e.Lifetime = 1.6f;
                e.Position = new Vector2(W * 0.5f, H * 0.5f);
                e.EmissionRectExtents = new Vector2(W * 0.12f, H * 0.12f);
                e.Spread = 180f;
                e.Gravity = new Vector2(0, 260 * k);
                e.InitialVelocityMin = 180 * k; e.InitialVelocityMax = 460 * k;
                e.AngularVelocityMin = -360; e.AngularVelocityMax = 360;
                e.ScaleAmountMin = 0.6f * k; e.ScaleAmountMax = 1.4f * k;
                e.ColorRamp = Fade(s.Tint ?? new Color(0.5f, 0.95f, 1f));
                glow = true;
                break;
            case Fx.Motes:
                e.Amount = 55;
                e.Lifetime = 6f;
                e.Position = new Vector2(W * 0.5f, H * 0.55f);
                e.EmissionRectExtents = new Vector2(W * 0.55f, H * 0.5f);
                e.Spread = 180f;
                e.Gravity = new Vector2(0, -6 * k);
                e.InitialVelocityMin = 6 * k; e.InitialVelocityMax = 22 * k;
                e.ScaleAmountMin = 0.7f * k; e.ScaleAmountMax = 1.6f * k;
                e.ColorRamp = Fade(tint, 0.9f);
                glow = true;
                break;
            default: // poeira no ar
                e.Amount = 40;
                e.Lifetime = 7f;
                e.Position = new Vector2(W * 0.5f, H * 0.5f);
                e.EmissionRectExtents = new Vector2(W * 0.55f, H * 0.55f);
                e.Spread = 180f;
                e.Direction = new Vector2(1f, -0.2f);
                e.InitialVelocityMin = 5 * k; e.InitialVelocityMax = 16 * k;
                e.ScaleAmountMin = 0.35f * k; e.ScaleAmountMax = 0.8f * k;
                e.ColorRamp = Fade(new Color(1f, 0.93f, 0.8f), 0.55f);
                break;
        }
        // faíscas, brasas e luz somam cor (brilham); chuva e poeira só cobrem
        e.Material = glow ? (_addMat ??= new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }) : null;
        e.Preprocess = e.Lifetime;
        if (Calm) e.Amount = Math.Max(8, e.Amount / 3);
        e.Emitting = true;
    }

    // ---- câmera: Ken Burns, paralaxe, respiração, tremida -----------------------------------------

    Vector2 FrameSize() => _frame.Size.X > 10 ? _frame.Size : new Vector2(1280, 720);

    public override void _Process(double delta)
    {
        if (!IsPlaying) return;
        float dt = (float)delta;
        foreach (var p in _pages) if (p.Visible) p.Time += dt;
        Layout();
        if (_shake > 0f)
        {
            _shake = Mathf.Max(0f, _shake - dt * 1.8f);
            float a = _shake * _shake * 16f;
            _shaker.Position = new Vector2(_rng.RandfRange(-a, a), _rng.RandfRange(-a, a));
        }
        else if (_shaker.Position != Vector2.Zero) _shaker.Position = Vector2.Zero;
    }

    void Layout()
    {
        var size = FrameSize();
        float W = size.X, H = size.Y;
        bool calm = Calm;
        foreach (var p in _pages)
        {
            if (!p.Visible) continue;
            p.Position = new Vector2(p.SlideX * W, p.SlideY * H);
            p.Size = new Vector2(Mathf.Max(1f, W * p.Reveal), H);
            p.Dim.Size = size;
            p.Dim.Color = new Color(0, 0, 0, p.Darken);
            if (p.Reveal < 1f) { p.Edge.Position = new Vector2(W * p.Reveal - 6, 0); p.Edge.Size = new Vector2(6, H); }
            else if (p.SlideX > 0f) { p.Edge.Position = Vector2.Zero; p.Edge.Size = new Vector2(6, H); }
            else { p.Edge.Position = new Vector2(0, H - 6); p.Edge.Size = new Vector2(W, 6); }

            float k = Mathf.SmoothStep(0f, 1f, Mathf.Min(p.Time / 11f, 1f));
            if (calm) k *= 0.3f;
            float dir = p.Scene.Drift == 0 ? 1f : Mathf.Sign(p.Scene.Drift);
            // pivô na linha do chão: os pés ficam no lugar enquanto a câmera se aproxima
            var pivot = new Vector2(W * 0.5f, H * 0.8f);
            foreach (var l in new[] { p.Bg, p.Fg, p.FxLayer }) { l.Size = size; l.PivotOffset = l == p.FxLayer ? size / 2 : pivot; }
            float bgScale = 1.07f + 0.05f * k + p.Zoom;
            p.Bg.Scale = Vector2.One * bgScale;
            p.Bg.Position = new Vector2(dir * W * 0.018f * (k - 0.5f), 0);
            if (p.Layered)
            {
                // personagens: paralaxe no sentido contrário e uma respiração bem leve
                float breathe = calm ? 0f : Mathf.Sin(p.Time * 1.7f) * 0.004f;
                float fgScale = 1.06f + 0.035f * k + p.Zoom * 1.25f;
                p.Fg.Scale = new Vector2(fgScale, fgScale * (1f + breathe));
                p.Fg.Position = new Vector2(-dir * W * 0.024f * (k - 0.5f), 0);
                p.FxLayer.Position = new Vector2(-dir * W * 0.032f * (k - 0.5f), calm ? 0 : Mathf.Sin(p.Time * 2.3f) * H * 0.003f);
            }
        }
    }

    /// <summary>Avança: termina a transição/frase em andamento ou passa para o próximo quadro.</summary>
    public void Advance()
    {
        if (!IsPlaying || _ending) return;
        if (_typing || Transitioning)
        {
            if (Transitioning) _trans.CustomStep(10.0);
            _typeTween?.Kill();
            _text.VisibleRatio = 1f;
            _typing = false;
            return;
        }
        if (Index + 1 < _caps.Length) Show(Index + 1);
        else End();
    }

    public void Skip() { if (IsPlaying && !_ending) End(); }

    void End()
    {
        _ending = true;
        var t = CreateTween();
        t.TweenProperty(_root, "modulate:a", 0f, 0.45f);
        t.TweenCallback(Callable.From(() =>
        {
            Visible = false;
            IsPlaying = false;
            _typeTween?.Kill();
            _trans?.Kill();
            foreach (var p in _pages) { p.Particles.Emitting = false; p.Visible = false; }
            _shake = 0f;
            _shaker.Position = Vector2.Zero;
            _flash.Color = _flash.Color with { A = 0f };
            Finished?.Invoke();
        }));
    }

    public override void _Input(InputEvent e)
    {
        if (!IsPlaying) return;
        bool next = (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    || e.IsActionPressed("ui_accept") || e.IsActionPressed("interact") || e.IsActionPressed("jump")
                    || (e is InputEventJoypadButton jb && jb.Pressed && jb.ButtonIndex == JoyButton.A);
        bool skip = e.IsActionPressed("pause") || e.IsActionPressed("ui_cancel")
                    || (e is InputEventJoypadButton jb2 && jb2.Pressed && jb2.ButtonIndex == JoyButton.B);
        if (skip) { GetViewport().SetInputAsHandled(); Skip(); }
        else if (next)
        {
            // o botão "Pular" trata o próprio clique
            if (e is InputEventMouseButton m2 && _root.GetNode<Button>("Skip").GetGlobalRect().HasPoint(m2.Position)) return;
            GetViewport().SetInputAsHandled();
            Advance();
        }
    }
}
