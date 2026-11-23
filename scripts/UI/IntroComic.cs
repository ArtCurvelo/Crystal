using System;
using CrystalExcavator.Core;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>
/// HQ de abertura (novo jogo): quadros desenhados em cartoon (assets/story, gerados por tools/comic.py) com a
/// fala do escavador embaixo, letra por letra. Clique/Espaço/Enter/A avança; Esc/B ou "Pular" encerra.
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

    public event Action Finished;
    string[] _caps = Captions;
    string _prefix = "panel_";
    public bool IsPlaying { get; private set; }
    public int Index { get; private set; }

    Control _root;
    TextureRect _art;
    Label _text, _hint, _counter;
    Tween _typeTween, _zoomTween;
    bool _typing, _ending;

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
        _art = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _art.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _art.OffsetTop = 24; _art.OffsetBottom = -170; _art.OffsetLeft = 40; _art.OffsetRight = -40;
        _root.AddChild(_art);

        // caixa de fala (legenda) embaixo
        var box = new PanelContainer();
        box.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.98f, 0.96f, 0.9f, 0.97f), new Color(0.08f, 0.06f, 0.1f), 12, 4, 26, 14));
        box.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        box.OffsetTop = -158; box.OffsetBottom = -18; box.OffsetLeft = 120; box.OffsetRight = -120;
        _root.AddChild(box);
        var v = new VBoxContainer();
        box.AddChild(v);
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

    /// <summary>Toca outra sequência de quadros (ex.: o flashback do Velho Bram): assets/story/{prefix}N.png.</summary>
    public void Play(string prefix, string[] captions)
    {
        _prefix = prefix;
        _caps = captions;
        Play(false);
    }

    public void Play() { _prefix = "panel_"; _caps = Captions; Play(false); }

    void Play(bool _)
    {
        IsPlaying = true;
        _ending = false;
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _root.Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(_root, "modulate:a", 1f, 0.5f);
        Show(0);
    }

    void Show(int i)
    {
        Index = i;
        _art.Texture = GD.Load<Texture2D>($"res://assets/story/{_prefix}{i + 1}.png");
        // leve aproximação (efeito de câmera sobre o quadro)
        _art.PivotOffset = _art.Size / 2;
        _art.Scale = Vector2.One;
        _zoomTween?.Kill();
        _zoomTween = CreateTween();
        _zoomTween.TweenProperty(_art, "scale", new Vector2(1.04f, 1.04f), 9f).SetTrans(Tween.TransitionType.Sine);
        _art.Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(_art, "modulate:a", 1f, 0.35f);

        string line = Loc.T(_caps[i]);
        _text.Text = line;
        _text.VisibleRatio = 0f;
        _typing = true;
        _typeTween?.Kill();
        _typeTween = CreateTween();
        _typeTween.TweenProperty(_text, "visible_ratio", 1f, Math.Clamp(line.Length * 0.028f, 0.8f, 4f));
        _typeTween.TweenCallback(Callable.From(() => _typing = false));
        _counter.Text = $"{i + 1} / {_caps.Length}";
        _hint.Text = i == _caps.Length - 1 ? Loc.T("Clique ou Espaço para começar") : Loc.T("Clique ou Espaço para continuar");
        Accessibility.Speak(line, true);
        Audio.AudioManager.Instance?.Play("click", -10f);
    }

    /// <summary>Avança: completa a frase que está sendo escrita ou passa para o próximo quadro.</summary>
    public void Advance()
    {
        if (!IsPlaying || _ending) return;
        if (_typing)
        {
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
            _zoomTween?.Kill();
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
