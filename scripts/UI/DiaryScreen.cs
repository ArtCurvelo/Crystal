using CrystalExcavator.Core;
using CrystalExcavator.Data;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>Leitura de um diário: página de papel envelhecido com título, autor e texto; avança para os outros já achados.</summary>
public partial class DiaryScreen : ModalScreen
{
    Label _title, _author, _text, _count;
    Button _prev, _next, _close;
    string _id;

    public override void _Ready()
    {
        BuildFrame("DIÁRIO ABANDONADO", new Vector2(820, 600), new Color(0.95f, 0.78f, 0.5f));
        Layer = 20;
        var page = new PanelContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        page.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.93f, 0.87f, 0.72f), new Color(0.45f, 0.32f, 0.2f), 6, 3, 28));
        Body.AddChild(page);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        page.AddChild(v);
        _title = UiTheme.Label("", 28, new Color(0.3f, 0.15f, 0.08f), true);
        v.AddChild(_title);
        _author = UiTheme.Label("", 16, new Color(0.45f, 0.32f, 0.22f));
        v.AddChild(_author);
        v.AddChild(new ColorRect { Color = new Color(0.45f, 0.32f, 0.2f, 0.5f), CustomMinimumSize = new Vector2(0, 2) });
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 330), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        v.AddChild(scroll);
        _text = UiTheme.Label("", 19, new Color(0.16f, 0.1f, 0.06f));
        _text.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_text);
        var foot = new HBoxContainer();
        foot.AddThemeConstantOverride("separation", 10);
        Body.AddChild(foot);
        _prev = UiTheme.Button("◀ Anterior", () => Step(-1), 150, 17);
        _next = UiTheme.Button("Próximo ▶", () => Step(1), 150, 17);
        _count = UiTheme.Label("", 15, UiTheme.Muted);
        _count.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _count.HorizontalAlignment = HorizontalAlignment.Center;
        _count.VerticalAlignment = VerticalAlignment.Center;
        foot.AddChild(_prev);
        foot.AddChild(_count);
        foot.AddChild(_next);
        _close = UiTheme.Button("Fechar", Close, 140, 17);
        foot.AddChild(_close);
    }

    public void Show(string id)
    {
        _id = id;
        Open();
    }

    public override void Refresh()
    {
        var d = DiaryData.Of(_id);
        if (d == null) return;
        var gs = GameState.Instance;
        _title.Text = d.Title;
        _author.Text = Loc.F("{0} · {1}", d.Author, GameData.Regions[d.Region].Name);
        _text.Text = d.Text;
        var found = gs.FoundDiaries();
        int i = found.FindIndex(x => x.Id == _id);
        _count.Text = Loc.F("Diários encontrados: {0}/{1}", found.Count, DiaryData.All.Count);
        _prev.Disabled = i <= 0;
        _next.Disabled = i < 0 || i >= found.Count - 1;
        Accessibility.Speak(d.Title + ". " + d.Text, true);
        (_next.Disabled ? _close : _next).CallDeferred(Control.MethodName.GrabFocus);
    }

    void Step(int dir)
    {
        var found = GameState.Instance.FoundDiaries();
        int i = found.FindIndex(x => x.Id == _id) + dir;
        if (i < 0 || i >= found.Count) return;
        _id = found[i].Id;
        Refresh();
    }
}
