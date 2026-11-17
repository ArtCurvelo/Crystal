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
        Key("interact", Godot.Key.E);
        Key("pause", Godot.Key.Escape);
        Key("quick_save", Godot.Key.F5);
        Key("toggle_fps", Godot.Key.F3);
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
        // navegação de menus com o analógico esquerdo, além do D-pad padrão
        Axis("ui_up", JoyAxis.LeftY, -1, false);
        Axis("ui_down", JoyAxis.LeftY, 1, false);
        Axis("ui_left", JoyAxis.LeftX, -1, false);
        Axis("ui_right", JoyAxis.LeftX, 1, false);
    }

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

    public static void Observe(InputEvent e)
    {
        bool pad = e is InputEventJoypadButton || (e is InputEventJoypadMotion m && Mathf.Abs(m.AxisValue) > 0.4f);
        bool kbm = e is InputEventKey || e is InputEventMouseButton || (e is InputEventMouseMotion mm && mm.Relative.LengthSquared() > 4f);
        if (pad && !UsingPad) { UsingPad = true; Changed?.Invoke(); }
        else if (kbm && UsingPad) { UsingPad = false; Changed?.Invoke(); }
    }

    public static string Interact => UsingPad ? "[X]" : "[E]";
    public static string Back => UsingPad ? "[B]" : "[Esc]";
    public static string Footer => UsingPad
        ? "[RT] Minerar   [X] Interagir   [LB] Correr   [A] Pular   [Start] Pausa"
        : "[Botão esq.] Minerar   [E] Interagir   [Shift] Correr   [Esc] Pausa";
}
