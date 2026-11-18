using System;
using Godot;

namespace CrystalExcavator.UI;

/// <summary>Painel de administrador (F9 / Back): atalhos para testar conteúdo sem jogar a campanha.</summary>
public partial class DebugScreen : ModalScreen
{
    public Func<string, string> Run;
    Label _out;
    LineEdit _input;

    static readonly (string label, string cmd)[] Quick =
    {
        ("+◈ 100 mil", "dinheiro 100000"), ("+◈ 10 mi", "dinheiro 10000000"), ("Liberar tudo", "tudo"), ("Regiões", "regioes"),
        ("Melhorias máx.", "melhorias"), ("Habilidades", "habilidades"), ("Casa máx.", "casa"), ("Encher mochila", "mochila"),
        ("Ir: Cavernas", "ir 1"), ("Ir: Profundezas", "ir 2"), ("Ir: Núcleo", "ir 3"), ("Ir: Reservatório", "ir 4"),
        ("Ir: Coração", "ir coracao"), ("Ir: goblin", "ir goblin"), ("Achar goblins", "goblins"), ("Rebelião", "rebeliao"),
        ("Hora 17h", "hora 17"), ("Fim do dia", "fimdia"), ("Novo dia", "novodia"), ("Evento", "evento"),
        ("Tempo x1", "velocidade 1"), ("Tempo x5", "velocidade 5"), ("Drone", "drone"), ("Curar", "curar"),
        ("Renascer pronto", "renascer"),
    };

    public override void _Ready()
    {
        BuildFrame("ADMIN / DEBUG", new Vector2(980, 600), new Color(1f, 0.45f, 0.3f));
        Layer = 30;
        Body.AddChild(UiTheme.Label("Altera o save atual. Use para testar, não para jogar de verdade.", 14, UiTheme.Muted));
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 6);
        Body.AddChild(grid);
        foreach (var (label, cmd) in Quick)
        {
            var b = new Button { Text = label, FocusMode = Control.FocusModeEnum.All, CustomMinimumSize = new Vector2(228, 38) };
            b.AddThemeFontSizeOverride("font_size", 16);
            string c = cmd;
            b.Pressed += () => Exec(c);
            grid.AddChild(b);
        }
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        Body.AddChild(row);
        _input = new LineEdit { PlaceholderText = "comando (ex.: dinheiro 5m, ir 3, hora 12) — Enter", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _input.TextSubmitted += t => { Exec(t); _input.Clear(); };
        row.AddChild(_input);
        row.AddChild(UiTheme.Button("Fechar", Close, 140, 18));
        _out = UiTheme.Label(Core.Main.DebugHelp, 14, UiTheme.Text);
        _out.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _out.CustomMinimumSize = new Vector2(930, 0);
        Body.AddChild(_out);
    }

    void Exec(string cmd)
    {
        string r = Run?.Invoke(cmd) ?? "";
        _out.Text = "> " + cmd + "\n" + r;
        GD.Print($"[Admin] {cmd} → {r}");
    }

    /// <summary>Para testes: executa como se o botão fosse apertado.</summary>
    public string Press(string label)
    {
        foreach (var (l, c) in Quick) if (l == label) { Exec(c); return _out.Text; }
        return null;
    }
}
