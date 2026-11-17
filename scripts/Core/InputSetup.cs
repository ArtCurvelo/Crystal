using Godot;

namespace CrystalExcavator.Core;

/// <summary>Registra o mapa de entradas por código (independe do project.godot).</summary>
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
}
