using System;
using System.Collections.Generic;
using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>
/// Conversa com retrato desenhado (estilo da HQ): caixa embaixo com nome, fala letra por letra e retrato de quem
/// fala. Clique/Espaço/E/A avança; Esc/B pula a conversa inteira.
/// </summary>
public partial class DialogueScreen : CanvasLayer
{
    public bool IsPlaying { get; private set; }
    public int Index { get; private set; }
    Action _done;
    IReadOnlyList<Line> _lines;
    Control _root;
    TextureRect _portrait;
    Label _name, _text, _hint;
    PanelContainer _portraitFrame;
    Tween _type;
    bool _typing;

    public override void _Ready()
    {
        Layer = 38;
        ProcessMode = ProcessModeEnum.Always;
        Visible = false;
        _root = new Control { Theme = UiTheme.Get(), MouseFilter = Control.MouseFilterEnum.Stop };
        _root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_root);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.35f), MouseFilter = Control.MouseFilterEnum.Ignore };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(dim);
        var row = new HBoxContainer();
        row.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        row.OffsetTop = -250; row.OffsetBottom = -24; row.OffsetLeft = 90; row.OffsetRight = -90;
        row.AddThemeConstantOverride("separation", 0);
        _root.AddChild(row);
        _portraitFrame = new PanelContainer { CustomMinimumSize = new Vector2(226, 226) };
        _portraitFrame.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.98f, 0.96f, 0.9f), new Color(0.08f, 0.06f, 0.1f), 8, 4, 4));
        _portraitFrame.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
        row.AddChild(_portraitFrame);
        _portrait = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, CustomMinimumSize = new Vector2(216, 216) };
        _portraitFrame.AddChild(_portrait);
        var box = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkEnd, CustomMinimumSize = new Vector2(0, 170) };
        box.AddThemeStyleboxOverride("panel", UiTheme.Chamfer(new Color(0.98f, 0.96f, 0.9f, 0.98f), new Color(0.08f, 0.06f, 0.1f), 12, 4, 26, 14));
        row.AddChild(box);
        var v = new VBoxContainer();
        box.AddChild(v);
        _name = UiTheme.Label("", 22, new Color(0.6f, 0.25f, 0.12f), true);
        v.AddChild(_name);
        _text = UiTheme.Label("", 22, new Color(0.1f, 0.07f, 0.12f));
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        v.AddChild(_text);
        _hint = UiTheme.Label("", 14, new Color(0.45f, 0.35f, 0.3f));
        _hint.HorizontalAlignment = HorizontalAlignment.Right;
        v.AddChild(_hint);
    }

    public void Play(IReadOnlyList<Line> lines, Action done)
    {
        _lines = lines;
        _done = done;
        IsPlaying = true;
        Visible = true;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        ShowLine(0);
    }

    void ShowLine(int i)
    {
        Index = i;
        var l = _lines[i];
        bool hero = l.Speaker == "hero";
        _name.Text = StoryData.SpeakerName(l.Speaker);
        _name.AddThemeColorOverride("font_color", hero ? new Color(0.15f, 0.35f, 0.65f) : new Color(0.6f, 0.25f, 0.12f));
        _portrait.Texture = GD.Load<Texture2D>($"res://assets/story/portrait_{l.Speaker}.png");
        // retrato sempre de quem fala; o herói fica à direita da caixa
        var row = _portraitFrame.GetParent();
        row.MoveChild(_portraitFrame, hero ? row.GetChildCount() - 1 : 0);
        string t = Loc.T(l.Text);
        _text.Text = t;
        _text.VisibleRatio = 0;
        _typing = true;
        _type?.Kill();
        _type = CreateTween();
        _type.TweenProperty(_text, "visible_ratio", 1f, Math.Clamp(t.Length * 0.025f, 0.5f, 3.5f));
        _type.TweenCallback(Callable.From(() => _typing = false));
        _hint.Text = Loc.T(i == _lines.Count - 1 ? "Clique ou Espaço para terminar" : "Clique ou Espaço para continuar");
        Accessibility.Speak(_name.Text + ": " + t, true);
        Audio.AudioManager.Instance?.Play("click", -12f);
    }

    public void Advance()
    {
        if (!IsPlaying) return;
        if (_typing) { _type?.Kill(); _text.VisibleRatio = 1; _typing = false; return; }
        if (Index + 1 < _lines.Count) ShowLine(Index + 1);
        else Finish();
    }

    public void Skip() { if (IsPlaying) Finish(); }

    void Finish()
    {
        IsPlaying = false;
        Visible = false;
        _type?.Kill();
        var d = _done;
        _done = null;
        d?.Invoke();
    }

    public override void _Input(InputEvent e)
    {
        if (!IsPlaying) return;
        bool next = (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    || e.IsActionPressed("ui_accept") || e.IsActionPressed("interact") || e.IsActionPressed("jump")
                    || (e is InputEventJoypadButton jb && jb.Pressed && jb.ButtonIndex == JoyButton.A);
        bool skip = e.IsActionPressed("pause") || (e is InputEventJoypadButton jb2 && jb2.Pressed && jb2.ButtonIndex == JoyButton.B);
        if (skip) { GetViewport().SetInputAsHandled(); Skip(); }
        else if (next) { GetViewport().SetInputAsHandled(); Advance(); }
    }
}
