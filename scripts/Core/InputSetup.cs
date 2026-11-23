using System.Collections.Generic;
using Godot;

namespace CrystalExcavator.Core;

/// <summary>
/// Registra o mapa de entradas por código (independe do project.godot):
/// teclado/mouse e controle (layout Xbox / XInput).
/// </summary>
public static class InputSetup
{
    public static void Register()
    {
        Key("move_forward", Godot.Key.W, Godot.Key.Up);
        Key("move_back", Godot.Key.S, Godot.Key.Down);
        Key("move_left", Godot.Key.A, Godot.Key.Left);
        Key("move_right", Godot.Key.D, Godot.Key.Right);
        Key("jump", Godot.Key.Space);
        Key("sprint", Godot.Key.Shift);
        Key("crouch", Godot.Key.C, Godot.Key.Ctrl);
        Key("flashlight", Godot.Key.F);
        Key("interact", Godot.Key.E);
        Key("pause", Godot.Key.Escape);
        Key("quick_save", Godot.Key.F5);
        Key("toggle_fps", Godot.Key.F3);
        Key("build", Godot.Key.B);
        Key("debug_panel", Godot.Key.F9);
        Key("build_prev", Godot.Key.Q);
        Key("build_next", Godot.Key.R);
        Key("swap_weapon", Godot.Key.X);
        Mouse("mine", MouseButton.Left);

        // ---- controle ----
        Axis("move_forward", JoyAxis.LeftY, -1);
        Axis("move_back", JoyAxis.LeftY, 1);
        Axis("move_left", JoyAxis.LeftX, -1);
        Axis("move_right", JoyAxis.LeftX, 1);
        Ensure("look_left"); Axis("look_left", JoyAxis.RightX, -1);
        Ensure("look_right"); Axis("look_right", JoyAxis.RightX, 1);
        Ensure("look_up"); Axis("look_up", JoyAxis.RightY, -1);
        Ensure("look_down"); Axis("look_down", JoyAxis.RightY, 1);
        Axis("mine", JoyAxis.TriggerRight, 1);
        Pad("mine", JoyButton.RightShoulder);
        Pad("jump", JoyButton.A);
        Pad("interact", JoyButton.X);
        Pad("sprint", JoyButton.LeftShoulder, JoyButton.LeftStick);
        Pad("pause", JoyButton.Start);
        Pad("crouch", JoyButton.B, JoyButton.RightStick);
        Pad("flashlight", JoyButton.DpadUp);
        Pad("build", JoyButton.DpadDown);
        Pad("debug_panel", JoyButton.Back);
        Pad("build_prev", JoyButton.DpadLeft);
        Pad("build_next", JoyButton.DpadRight);
        Pad("swap_weapon", JoyButton.Y);
        // menus com o controle: o Godot só mapeia teclado nas ações de interface.
        // A confirma, B volta, direcional e analógico navegam, LB/RB trocam de aba.
        Pad("ui_accept", JoyButton.A);
        Pad("ui_cancel", JoyButton.B);
        Pad("ui_up", JoyButton.DpadUp);
        Pad("ui_down", JoyButton.DpadDown);
        Pad("ui_left", JoyButton.DpadLeft);
        Pad("ui_right", JoyButton.DpadRight);
        Ensure("ui_tab_prev"); Key("ui_tab_prev", Godot.Key.Q); Pad("ui_tab_prev", JoyButton.LeftShoulder);
        Ensure("ui_tab_next"); Key("ui_tab_next", Godot.Key.R); Pad("ui_tab_next", JoyButton.RightShoulder);
        // navegação de menus com o analógico esquerdo, além do D-pad
        Axis("ui_up", JoyAxis.LeftY, -1, false);
        Axis("ui_down", JoyAxis.LeftY, 1, false);
        Axis("ui_left", JoyAxis.LeftX, -1, false);
        Axis("ui_right", JoyAxis.LeftX, 1, false);
    }

    // ===================== Remapeamento =====================
    /// <summary>Ações que o jogador pode remapear no teclado/mouse (id, nome em português).</summary>
    public static readonly (string id, string name)[] KeyActions =
    {
        ("move_forward", "Andar para frente"), ("move_back", "Andar para trás"), ("move_left", "Andar para a esquerda"),
        ("move_right", "Andar para a direita"), ("jump", "Pular"), ("sprint", "Correr"), ("crouch", "Agachar"),
        ("interact", "Interagir"), ("mine", "Minerar"), ("flashlight", "Lanterna"), ("pause", "Pausa"),
        ("quick_save", "Salvar rápido"), ("toggle_fps", "Mostrar FPS"),
        ("build", "Modo construção"), ("build_prev", "Construção anterior"), ("build_next", "Próxima construção"),
        ("swap_weapon", "Trocar ferramenta/arma"),
    };

    /// <summary>Ações remapeáveis no controle (botões).</summary>
    public static readonly (string id, string name)[] PadActions =
    {
        ("jump", "Pular"), ("sprint", "Correr"), ("crouch", "Agachar"), ("interact", "Interagir"),
        ("mine", "Minerar"), ("flashlight", "Lanterna"), ("pause", "Pausa"),
        ("build", "Modo construção"), ("build_prev", "Construção anterior"), ("build_next", "Próxima construção"),
        ("swap_weapon", "Trocar ferramenta/arma"),
    };

    static string Serialize(InputEvent e) => e switch
    {
        InputEventKey k => "k:" + (long)(k.PhysicalKeycode != Godot.Key.None ? k.PhysicalKeycode : k.Keycode),
        InputEventMouseButton m => "m:" + (int)m.ButtonIndex,
        _ => null,
    };

    static InputEvent Deserialize(string s)
    {
        if (s == null || s.Length < 3) return null;
        if (!long.TryParse(s.Substring(2), out long v)) return null;
        return s[0] switch
        {
            'k' => new InputEventKey { PhysicalKeycode = (Godot.Key)v },
            'm' => new InputEventMouseButton { ButtonIndex = (MouseButton)v },
            _ => null,
        };
    }

    /// <summary>Aplica os remapeamentos salvos por cima dos padrões.</summary>
    public static void ApplyBindings()
    {
        var d = Settings.Data;
        if (d == null) return;
        foreach (var (id, _) in KeyActions)
        {
            if (!d.KeyBinds.TryGetValue(id, out var list) || list == null || list.Count == 0) continue;
            if (!InputMap.HasAction(id)) continue;
            foreach (var e in InputMap.ActionGetEvents(id))
                if (e is InputEventKey || e is InputEventMouseButton) InputMap.ActionEraseEvent(id, e);
            foreach (var str in list) if (Deserialize(str) is { } ev) InputMap.ActionAddEvent(id, ev);
        }
        foreach (var (id, _) in PadActions)
        {
            if (!d.PadBinds.TryGetValue(id, out var list) || list == null || list.Count == 0) continue;
            if (!InputMap.HasAction(id)) continue;
            foreach (var e in InputMap.ActionGetEvents(id))
                if (e is InputEventJoypadButton) InputMap.ActionEraseEvent(id, e);
            foreach (int b in list) InputMap.ActionAddEvent(id, new InputEventJoypadButton { ButtonIndex = (JoyButton)b, Device = -1 });
        }
    }

    /// <summary>Define uma tecla/botão do mouse para a ação (substitui as anteriores).</summary>
    public static void BindKey(string action, InputEvent e)
    {
        var str = Serialize(e);
        if (str == null) return;
        Settings.Data.KeyBinds[action] = new List<string> { str };
        ApplyBindings();
    }

    public static void BindPad(string action, JoyButton b)
    {
        Settings.Data.PadBinds[action] = new List<int> { (int)b };
        ApplyBindings();
    }

    /// <summary>Volta tudo ao padrão.</summary>
    public static void ResetBindings()
    {
        Settings.Data.KeyBinds.Clear();
        Settings.Data.PadBinds.Clear();
        Register();
    }

    /// <summary>Texto curto da tecla/botão atual de uma ação (teclado/mouse ou controle).</summary>
    public static string Describe(string action, bool pad)
    {
        if (!InputMap.HasAction(action)) return "—";
        var parts = new List<string>();
        foreach (var e in InputMap.ActionGetEvents(action))
        {
            if (pad && e is InputEventJoypadButton jb) parts.Add(PadName(jb.ButtonIndex));
            else if (pad && e is InputEventJoypadMotion jm && (jm.Axis == JoyAxis.TriggerLeft || jm.Axis == JoyAxis.TriggerRight)) parts.Add(jm.Axis == JoyAxis.TriggerRight ? "RT" : "LT");
            else if (!pad && e is InputEventKey k) parts.Add(OS.GetKeycodeString(k.PhysicalKeycode != Godot.Key.None ? k.PhysicalKeycode : k.Keycode));
            else if (!pad && e is InputEventMouseButton m) parts.Add(MouseName(m.ButtonIndex));
        }
        return parts.Count == 0 ? "—" : string.Join(" / ", parts);
    }

    public static string MouseName(MouseButton b) => b switch
    {
        MouseButton.Left => Loc.T("Botão esquerdo"), MouseButton.Right => Loc.T("Botão direito"), MouseButton.Middle => Loc.T("Botão do meio"),
        MouseButton.Xbutton1 => "Mouse 4", MouseButton.Xbutton2 => "Mouse 5", _ => "Mouse " + (int)b,
    };

    public static string PadName(JoyButton b) => b switch
    {
        JoyButton.A => "A", JoyButton.B => "B", JoyButton.X => "X", JoyButton.Y => "Y",
        JoyButton.LeftShoulder => "LB", JoyButton.RightShoulder => "RB", JoyButton.LeftStick => "LS", JoyButton.RightStick => "RS",
        JoyButton.Start => "Start", JoyButton.Back => "Back", JoyButton.DpadUp => "D-pad ↑", JoyButton.DpadDown => "D-pad ↓",
        JoyButton.DpadLeft => "D-pad ←", JoyButton.DpadRight => "D-pad →", _ => Loc.T("Botão ") + (int)b,
    };

    static void Ensure(string action)
    {
        if (!InputMap.HasAction(action)) InputMap.AddAction(action, 0.2f);
        else InputMap.ActionEraseEvents(action);
    }

    static void Key(string action, params Key[] keys)
    {
        Ensure(action);
        foreach (var k in keys)
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
    }

    static void Mouse(string action, MouseButton b)
    {
        Ensure(action);
        InputMap.ActionAddEvent(action, new InputEventMouseButton { ButtonIndex = b });
    }

    static void Pad(string action, params JoyButton[] buttons)
    {
        foreach (var b in buttons)
            InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = b, Device = -1 });
    }

    /// <summary>Adiciona um eixo do controle a uma ação já existente (sem apagar os eventos de teclado).</summary>
    static void Axis(string action, JoyAxis axis, float dir, bool mustExist = true)
    {
        if (!InputMap.HasAction(action))
        {
            if (mustExist) return;
            InputMap.AddAction(action, 0.5f);
        }
        InputMap.ActionAddEvent(action, new InputEventJoypadMotion { Axis = axis, AxisValue = dir, Device = -1 });
    }
}

/// <summary>Detecta o último dispositivo usado para trocar as dicas na tela (teclado ↔ controle).</summary>
public static class InputHints
{
    public static bool UsingPad { get; private set; }
    public static event System.Action Changed;

    // posição de repouso de cada eixo (muitos controles deixam os gatilhos em -1 quando soltos)
    static readonly System.Collections.Generic.Dictionary<(int, JoyAxis), float> _rest = new();
    static float _mouseAccum;
    static ulong _mouseAt;

    public static void Observe(InputEvent e)
    {
        bool pad = false, kbm = false;
        switch (e)
        {
            case InputEventJoypadButton jb:
                pad = jb.Pressed;
                break;
            case InputEventJoypadMotion jm:
            {
                // só conta movimento de verdade, comparado à posição de repouso do eixo
                var key = (jm.Device, jm.Axis);
                if (!_rest.TryGetValue(key, out float rest))
                {
                    rest = Mathf.Abs(jm.AxisValue) > 0.9f ? jm.AxisValue : 0f;
                    _rest[key] = rest;
                }
                pad = Mathf.Abs(jm.AxisValue - rest) > 0.5f;
                break;
            }
            case InputEventKey k:
                kbm = k.Pressed;
                break;
            case InputEventMouseButton mb:
                kbm = mb.Pressed;
                break;
            case InputEventMouseMotion mm:
            {
                // um esbarrão no mouse (ou o mouse emulado pela Steam) não troca para teclado:
                // precisa de um movimento contínuo de alguns pixels
                ulong now = Time.GetTicksMsec();
                if (now - _mouseAt > 250) _mouseAccum = 0;
                _mouseAt = now;
                _mouseAccum += mm.Relative.Length();
                kbm = _mouseAccum > 24f;
                break;
            }
        }
        if (pad && !UsingPad) Set(true);
        else if (kbm && UsingPad) Set(false);
    }

    static void Set(bool pad)
    {
        UsingPad = pad;
        _mouseAccum = 0;
        Changed?.Invoke();
    }

    public static void StartWithPad() { if (!UsingPad) Set(true); }

    /// <summary>Controle conectado/desconectado: troca as dicas na hora e avisa.</summary>
    public static event System.Action<bool, string> PadConnection;

    public static void OnJoyConnection(long device, bool connected)
    {
        foreach (var k in new System.Collections.Generic.List<(int, JoyAxis)>(_rest.Keys))
            if (k.Item1 == (int)device) _rest.Remove(k);
        string name = connected ? Input.GetJoyName((int)device) : "";
        if (connected) Set(true);
        else if (Input.GetConnectedJoypads().Count == 0 && UsingPad) Set(false);
        PadConnection?.Invoke(connected, name);
    }

    /// <summary>Primeira tecla/botão da ação no dispositivo em uso (para dicas na tela).</summary>
    public static string Key(string action)
    {
        var d = InputSetup.Describe(action, UsingPad);
        int cut = d.IndexOf(" / ");
        return cut > 0 ? d.Substring(0, cut) : d;
    }

    public static string Interact => $"[{Key("interact")}]";
    public static string Back => UsingPad ? "[B]" : "[Esc]";
    public static string Footer =>
        $"[{Key("mine")}] {Loc.T("Minerar")}   [{Key("interact")}] {Loc.T("Interagir")}   [{Key("sprint")}] {Loc.T("Correr")}   " +
        $"[{Key("crouch")}] {Loc.T("Agachar")}   [{Key("flashlight")}] {Loc.T("Lanterna")}   [{Key("build")}] {Loc.T("Construir")}   [{Key("pause")}] {Loc.T("Pausa")}";

    /// <summary>Avisa que as dicas mudaram (ex.: remapeamento).</summary>
    public static void Refresh() => Changed?.Invoke();
}
