using System.Collections.Generic;
using DownhillRoguelite.Core;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// A short list under the dials of what is running low (hurt, hungry, thirsty, exhausted, cold),
/// flashing red → yellow → white so it catches the eye without a look at the dials. Off with
/// GameOptions.LowStatAlerts for those who'd rather read the dials.
/// </summary>
public partial class LowStatAlerts : Control
{
    /// <summary>A stat shows here below this share of its maximum.</summary>
    [Export] public float Below { get; set; } = 0.25f;
    [Export] public float FlashSpeed { get; set; } = 3.0f;

    public IReadOnlyList<string> Shown => _alerts;

    private readonly List<string> _alerts = new();
    private float _time;
    private float _cold;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        GameEvents.VitalsUpdated += OnVitals;
        GameEvents.WeatherUpdated += OnWeather;
    }

    public override void _ExitTree()
    {
        GameEvents.VitalsUpdated -= OnVitals;
        GameEvents.WeatherUpdated -= OnWeather;
    }

    private void OnWeather(WeatherSnapshot weather)
    {
        _cold = weather.Snow;
    }

    private void OnVitals(VitalsSnapshot vitals)
    {
        _alerts.Clear();
        if (vitals.Dead)
        {
            return;
        }
        if (vitals.MaxHealth > 0.0f && vitals.Health / vitals.MaxHealth < Below)
        {
            _alerts.Add("Badly hurt");
        }
        if (vitals.Hunger < 100.0f * Below)
        {
            _alerts.Add("Hungry");
        }
        if (vitals.Thirst < 100.0f * Below)
        {
            _alerts.Add("Thirsty");
        }
        if (vitals.Exhausted || (vitals.MaxStamina > 0.0f && vitals.Stamina / vitals.MaxStamina < Below))
        {
            _alerts.Add("Exhausted");
        }
        if (_cold > 0.6f && GameEvents.CurrentGear.Warmth < 0.2f)
        {
            _alerts.Add("Freezing: put something warm on");
        }
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        Visible = GameOptions.LowStatAlerts && _alerts.Count > 0;
        if (Visible)
        {
            QueueRedraw();
        }
    }

    /// <summary>Red, then yellow, then white, round and round.</summary>
    private Color Flash(float offset)
    {
        float t = Mathf.PosMod(_time * FlashSpeed + offset, 3.0f);
        Color red = UiStyle.Warning;
        Color yellow = new(1.0f, 0.85f, 0.3f);
        if (t < 1.0f)
        {
            return red.Lerp(yellow, t);
        }
        if (t < 2.0f)
        {
            return yellow.Lerp(Colors.White, t - 1.0f);
        }
        return Colors.White.Lerp(red, t - 2.0f);
    }

    public override void _Draw()
    {
        Font font = UiStyle.Heading;
        float y = 0.0f;
        for (int i = 0; i < _alerts.Count; i++)
        {
            string text = _alerts[i];
            Vector2 size = font.GetStringSize(text, HorizontalAlignment.Left, -1, UiStyle.SmallSize);
            DrawRect(new Rect2(new Vector2(0.0f, y), size + new Vector2(16.0f, 8.0f)), new Color(0.1f, 0.07f, 0.05f, 0.7f));
            DrawString(font, new Vector2(8.0f, y + size.Y), text, HorizontalAlignment.Left, -1, UiStyle.SmallSize, Flash(i * 0.4f));
            y += size.Y + 12.0f;
        }
    }
}
