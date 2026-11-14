using System;
using System.Linq;
using System.Threading.Tasks;
using DownhillRoguelite.Ui;
using DownhillRoguelite.WorldUi;
using Godot;

namespace DownhillRoguelite.Debug;

/// <summary>The network browser must leave hosting and joining reachable at common window sizes.</summary>
public partial class CoopPageTests : Node
{
    private int _failures;

    public override async void _Ready()
    {
        try
        {
            await CheckSize(new Vector2I(1280, 720));
            await CheckSize(new Vector2I(1920, 1080));
        }
        catch (Exception error)
        {
            Check("co-op page completes layout", false, error.ToString());
        }
        GD.Print($"COOP PAGE TESTS: {_failures} failures");
        GetTree().Quit(Math.Min(_failures, 1));
    }

    private async Task CheckSize(Vector2I screen)
    {
        SubViewport viewport = new() { Size = screen };
        AddChild(viewport);
        CoopPage page = new();
        viewport.AddChild(page);
        page.FitInto(new Rect2(new Vector2(20, 20), (Vector2)screen - new Vector2(40, 40)));
        for (int frame = 0; frame < 30; frame++) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
        Rect2 bounds = page.GetGlobalRect();
        InkButton[] actions = page.FindChildren("*", "", true, false).OfType<InkButton>()
            .Where(button => button.Text == "Host Expedition" || button.Text == "Join" || button.Text == "Direct Connect").ToArray();
        Check($"{screen}: host and join stay above Back", actions.Length == 3 && actions.All(button =>
            bounds.Encloses(button.GetGlobalRect()) && button.GetGlobalRect().End.Y <= bounds.End.Y - 56),
            string.Join(", ", actions.Select(button => button.Text + " " + button.GetGlobalRect())));
        Check($"{screen}: address and port remain visible", page.AddressField != null && page.PortField != null
            && bounds.Encloses(page.AddressField.GetGlobalRect()) && bounds.Encloses(page.PortField.GetGlobalRect()));
        ScrollContainer browser = page.FindChildren("*", "", true, false).OfType<ScrollContainer>()
            .Single(control => control.Name == "ExpeditionBrowser");
        Check($"{screen}: empty LAN browser fits without hiding the form", browser.Size.Y >= page.MinimumListHeight
            && bounds.Encloses(browser.GetGlobalRect()) && page.AddressField != null
            && browser.GetGlobalRect().End.Y <= page.AddressField.GetGlobalRect().Position.Y);
        viewport.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(string name, bool ok, string detail = "")
    {
        if (ok) { GD.Print($"PASS {name}"); }
        else { _failures++; GD.Print($"FAIL {name}: {detail}"); }
    }
}
