using System.Collections.Generic;
using DownhillRoguelite.Core;
using DownhillRoguelite.Inventory;
using Godot;

namespace DownhillRoguelite.Ui;

/// <summary>
/// The carrier's figures as the pack page shows them when nothing is chosen: body, load, then each
/// ability as its total with what it is made of (base, what the gear adds, what this run's boons add),
/// and where the carrier is.
/// </summary>
public static class CarrierSheet
{
    public static string Text(VitalsSnapshot? vitals, RunState? run)
    {
        List<string> lines = new();
        RunModifiers? mods = run?.Modifiers;
        if (vitals is VitalsSnapshot v)
        {
            lines.Add($"Health  {v.Health:0} / {v.MaxHealth:0}");
            lines.Add($"Hunger  {v.Hunger:0}");
            lines.Add($"Thirst  {v.Thirst:0}");
            float baseStamina = v.MaxStamina;
            if (mods != null && mods.StaminaMax > 0.01f)
            {
                baseStamina = v.MaxStamina / mods.StaminaMax;
            }
            lines.Add($"Stamina  {v.Stamina:0} / {v.MaxStamina:0}" + Parts(baseStamina, 0.0f, v.MaxStamina - baseStamina));
            lines.Add("");
        }
        GearSnapshot gear = GameEvents.CurrentGear;
        lines.Add($"Load  {CarriedLoad.Mass:0.0} kg");
        if (gear.Backpack.MaxDurability > 0.0f)
        {
            lines.Add($"Pack  {gear.Backpack.Durability:0} / {gear.Backpack.MaxDurability:0}");
        }
        lines.Add($"Pockets  {gear.PocketsUsed} / {gear.Pockets}");
        lines.Add("");
        float boonGrip = 0.0f;
        float boonBalance = 0.0f;
        float boonSpeed = 0.0f;
        float boonCold = 0.0f;
        float boonRecovery = 0.0f;
        float boonNeeds = 0.0f;
        if (mods != null)
        {
            boonGrip = (mods.Grip - 1.0f) * 100.0f;
            boonBalance = (mods.Balance - 1.0f) * 100.0f;
            boonSpeed = (mods.WalkSpeed - 1.0f) * 100.0f;
            boonCold = mods.ColdResistance * 100.0f;
            boonRecovery = (mods.StaminaRecovery - 1.0f) * 100.0f;
            boonNeeds = (mods.NeedsDrain - 1.0f) * 100.0f;
        }
        lines.Add(Stat("Grip", 100.0f, gear.Grip * 100.0f, boonGrip));
        lines.Add(Stat("Balance", 100.0f, 0.0f, boonBalance));
        lines.Add(Stat("Walk speed", 100.0f, 0.0f, boonSpeed));
        lines.Add(Stat("Warmth", 0.0f, gear.Warmth * 100.0f, boonCold));
        lines.Add(Stat("Rain cover", 0.0f, gear.RainProof * 100.0f, 0.0f));
        lines.Add(Stat("Recovery", 100.0f, 0.0f, boonRecovery));
        lines.Add(Stat("Hunger rate", 100.0f, 0.0f, boonNeeds));
        if (gear.WindCatch > 0.0f)
        {
            lines.Add($"Catches wind  +{gear.WindCatch:0.00} m²");
        }
        if (run != null)
        {
            lines.Add("");
            lines.Add($"{run.Depth} m down, {run.Circles.NameOf(run.Circle)}");
            if (run.Boons.Count > 0)
            {
                lines.Add("Boons: " + string.Join(", ", run.Boons));
            }
        }
        return string.Join("\n", lines);
    }

    /// <summary>"Grip 125%  (100 +15 gear +10 boons)": the total, then its parts when there are bonuses.</summary>
    private static string Stat(string name, float baseValue, float gear, float boons)
    {
        float total = baseValue + gear + boons;
        return $"{name}  {total:0}%" + Parts(baseValue, gear, boons);
    }

    private static string Parts(float baseValue, float gear, float boons)
    {
        if (Mathf.Abs(gear) < 0.5f && Mathf.Abs(boons) < 0.5f)
        {
            return "";
        }
        string parts = $"  ({baseValue:0}";
        if (Mathf.Abs(gear) >= 0.5f)
        {
            parts += $" {Signed(gear)} gear";
        }
        if (Mathf.Abs(boons) >= 0.5f)
        {
            parts += $" {Signed(boons)} boons";
        }
        return parts + ")";
    }

    private static string Signed(float value)
    {
        if (value >= 0.0f)
        {
            return $"+{value:0}";
        }
        return $"{value:0}";
    }

    /// <summary>
    /// Splits the sheet into a name column and a value column at each line's first double space;
    /// a "(100 +15 gear)" breakdown goes on its own line under the name, so the totals stay in line.
    /// </summary>
    public static (string Names, string Values) Columns(string text)
    {
        List<string> names = new();
        List<string> values = new();
        foreach (string line in text.Split('\n'))
        {
            int cut = line.IndexOf("  ");
            if (cut < 0)
            {
                names.Add(line);
                values.Add("");
                continue;
            }
            string value = line.Substring(cut).Trim();
            string parts = "";
            int open = value.IndexOf("  (");
            if (open > 0)
            {
                parts = value.Substring(open).Trim();
                value = value.Substring(0, open);
            }
            names.Add(line.Substring(0, cut));
            values.Add(value);
            if (parts.Length > 0)
            {
                names.Add("   " + parts.Trim('(', ')'));
                values.Add("");
            }
        }
        return (string.Join("\n", names), string.Join("\n", values));
    }
}
