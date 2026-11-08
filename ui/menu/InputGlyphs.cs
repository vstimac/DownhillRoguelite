using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// Readable names for an input action's bindings, taken from the project's InputMap so the
/// controls reference never drifts from the real keys: "W", "Shift", "Left mouse", "A", "LB".
/// </summary>
public static class InputGlyphs
{
    private static readonly string[] PadButtons =
    {
        "A", "B", "X", "Y", "Back", "Guide", "Start", "L3", "R3", "LB", "RB", "D-pad up", "D-pad down", "D-pad left", "D-pad right",
    };

    /// <summary>The keyboard and mouse bindings of the actions, joined by " / " (empty when none).</summary>
    public static string Keys(params string[] actions)
    {
        List<string> names = new();
        foreach (string action in actions)
        {
            if (!InputMap.HasAction(action))
            {
                continue;
            }
            foreach (InputEvent e in InputMap.ActionGetEvents(action))
            {
                string name = "";
                if (e is InputEventKey key)
                {
                    name = KeyName(key);
                }
                else if (e is InputEventMouseButton mouse)
                {
                    name = MouseName(mouse.ButtonIndex);
                }
                if (name.Length > 0 && !names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }
        return string.Join(" / ", names);
    }

    /// <summary>The controller bindings of the actions, joined by " / " (empty when none).</summary>
    public static string Pad(params string[] actions)
    {
        List<string> names = new();
        foreach (string action in actions)
        {
            if (!InputMap.HasAction(action))
            {
                continue;
            }
            foreach (InputEvent e in InputMap.ActionGetEvents(action))
            {
                string name = "";
                if (e is InputEventJoypadButton button)
                {
                    int index = (int)button.ButtonIndex;
                    if (index >= 0 && index < PadButtons.Length)
                    {
                        name = PadButtons[index];
                    }
                }
                else if (e is InputEventJoypadMotion motion)
                {
                    name = AxisName(motion.Axis);
                }
                if (name.Length > 0 && !names.Contains(name))
                {
                    names.Add(name);
                }
            }
        }
        return string.Join(" / ", names);
    }

    /// <summary>Keys and controller together: "Space · A".</summary>
    public static string Both(params string[] actions)
    {
        string keys = Keys(actions);
        string pad = Pad(actions);
        if (keys.Length > 0 && pad.Length > 0)
        {
            return keys + "  ·  " + pad;
        }
        if (keys.Length > 0)
        {
            return keys;
        }
        return pad;
    }

    private static string KeyName(InputEventKey key)
    {
        Key code = key.PhysicalKeycode;
        if (code != Key.None && DisplayServer.GetName() != "headless")
        {
            // The key as labelled on this keyboard's layout (Z on a German keyboard's Y key).
            Key mapped = DisplayServer.KeyboardGetKeycodeFromPhysical(code);
            if (mapped != Key.None)
            {
                code = mapped;
            }
        }
        else if (code == Key.None)
        {
            code = key.Keycode;
        }
        if (code == Key.None)
        {
            return "";
        }
        string name = OS.GetKeycodeString(code);
        if (key.Location == KeyLocation.Left)
        {
            name = "Left " + name;
        }
        return name;
    }

    private static string MouseName(MouseButton button)
    {
        if (button == MouseButton.Left)
        {
            return "Left mouse";
        }
        if (button == MouseButton.Right)
        {
            return "Right mouse";
        }
        if (button == MouseButton.Middle)
        {
            return "Middle mouse";
        }
        if (button == MouseButton.WheelUp || button == MouseButton.WheelDown)
        {
            return "Mouse wheel";
        }
        return "Mouse " + (int)button;
    }

    private static string AxisName(JoyAxis axis)
    {
        if (axis == JoyAxis.LeftX || axis == JoyAxis.LeftY)
        {
            return "Left stick";
        }
        if (axis == JoyAxis.RightX || axis == JoyAxis.RightY)
        {
            return "Right stick";
        }
        if (axis == JoyAxis.TriggerLeft)
        {
            return "LT";
        }
        if (axis == JoyAxis.TriggerRight)
        {
            return "RT";
        }
        return "";
    }
}
