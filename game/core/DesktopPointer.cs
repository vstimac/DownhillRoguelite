using System;
using System.Runtime.InteropServices;
using Godot;

namespace DownhillRoguelite.Core;

/// <summary>
/// The desktop's own pointer settings, for mouse look with Raw input off: Windows' pointer speed
/// (Control Panel → Mouse → Pointer Options, 1–20) and "Enhance pointer precision" (its pointer
/// acceleration). Captured mouse look in Godot reads raw counts, which skip both; with Raw input
/// off the camera applies them itself, approximating Windows' ballistics. Elsewhere there is
/// nothing to read, so the counts stay raw.
/// </summary>
public static class DesktopPointer
{
    /// <summary>Whether the desktop settings could be read (Windows).</summary>
    public static bool Available { get; private set; }
    /// <summary>Windows pointer speed, 1–20 (10 is the default, 1:1).</summary>
    public static int Speed { get; private set; } = 10;
    /// <summary>Windows "Enhance pointer precision".</summary>
    public static bool EnhancePrecision { get; private set; }

    private const uint GetMouse = 0x0003;
    private const uint GetMouseSpeed = 0x0070;

    // Windows' pointer speed slider as a multiplier (positions 1–20) without pointer precision.
    private static readonly float[] SpeedScale =
    {
        0.03125f, 0.0625f, 0.125f, 0.25f, 0.375f, 0.5f, 0.625f, 0.75f, 0.875f, 1.0f,
        1.25f, 1.5f, 1.75f, 2.0f, 2.25f, 2.5f, 2.75f, 3.0f, 3.25f, 3.5f,
    };

    // Windows' default "Enhance pointer precision" curve: mouse speed (in/s) to pointer speed.
    private static readonly float[] CurveIn = { 0.0f, 0.43f, 1.25f, 3.86f, 40.0f };
    private static readonly float[] CurveOut = { 0.0f, 1.07f, 4.08f, 29.6f, 568.0f };
    // Windows assumes a 400 counts-per-inch mouse and a 96 dpi screen for the curve.
    private const float CountsPerInch = 400.0f;
    private const float ScreenDpi = 96.0f;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoInt(uint action, uint param, ref int value, uint winIni);

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoArray(uint action, uint param, [In, Out] int[] value, uint winIni);

    /// <summary>Reads the desktop's settings again (call when Raw input is turned off).</summary>
    public static void Refresh()
    {
        Available = false;
        if (OS.GetName() != "Windows")
        {
            return;
        }
        try
        {
            int speed = 10;
            int[] mouse = new int[3];
            if (SystemParametersInfoInt(GetMouseSpeed, 0, ref speed, 0) && SystemParametersInfoArray(GetMouse, 0, mouse, 0))
            {
                Speed = Math.Clamp(speed, 1, 20);
                EnhancePrecision = mouse[2] != 0;
                Available = true;
            }
        }
        catch (Exception error)
        {
            GD.PushWarning($"DesktopPointer: could not read the Windows mouse settings ({error.Message}).");
        }
    }

    /// <summary>
    /// The desktop's gain for a movement of <paramref name="counts"/> over <paramref name="seconds"/>:
    /// pointer pixels per raw count (1 when the settings aren't available).
    /// </summary>
    public static float Gain(float counts, float seconds)
    {
        if (!Available)
        {
            return 1.0f;
        }
        float slider = SpeedScale[Speed - 1];
        if (!EnhancePrecision)
        {
            return slider;
        }
        // With pointer precision on, the slider scales the curve's output linearly (10 → 1).
        float inches = counts / CountsPerInch;
        float speed = inches / Mathf.Max(seconds, 0.0001f);
        if (speed <= 0.0001f)
        {
            return slider;
        }
        float pointer = Curve(speed);
        return pointer / speed * ScreenDpi / CountsPerInch * Speed / 10.0f;
    }

    private static float Curve(float speed)
    {
        for (int i = 1; i < CurveIn.Length; i++)
        {
            if (speed <= CurveIn[i] || i == CurveIn.Length - 1)
            {
                float t = (speed - CurveIn[i - 1]) / (CurveIn[i] - CurveIn[i - 1]);
                return Mathf.Lerp(CurveOut[i - 1], CurveOut[i], t);
            }
        }
        return speed;
    }
}
