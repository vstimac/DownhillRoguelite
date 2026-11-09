using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Player;

/// <summary>
/// The sword's Mixamo animations, played on a hidden Mixamo skeleton that MixamoOverlay lays over
/// the carrier. The first clip's scene gives the skeleton and its AnimationPlayer; every clip's
/// animation (each its own FBX, same skeleton) goes into one library under a short name. Only the
/// bones' rotations are used, so the clips' root motion is dropped. Nothing is drawn.
/// </summary>
public partial class SwordAnimator : Node3D
{
    public const string Library = "sword";

    /// <summary>Short names and the FBX each clip comes from; the first one's scene is the skeleton.</summary>
    [Export] public Godot.Collections.Dictionary<string, string> Clips { get; set; } = new()
    {
        { "idle", "res://art/characters/mixamo_sword/sword_idle.fbx" },
        { "block_idle", "res://art/characters/mixamo_sword/sword_block_idle.fbx" },
        { "block", "res://art/characters/mixamo_sword/sword_block.fbx" },
        { "block_impact", "res://art/characters/mixamo_sword/sword_block_impact.fbx" },
        { "impact", "res://art/characters/mixamo_sword/sword_impact.fbx" },
        { "impact_2", "res://art/characters/mixamo_sword/sword_impact_2.fbx" },
        { "attack_1", "res://art/characters/mixamo_sword/sword_attack_1.fbx" },
        { "attack_2", "res://art/characters/mixamo_sword/sword_attack_2.fbx" },
        { "attack_3", "res://art/characters/mixamo_sword/sword_attack_3.fbx" },
        { "attack_4", "res://art/characters/mixamo_sword/sword_attack_4.fbx" },
        { "kick", "res://art/characters/mixamo_sword/sword_kick.fbx" },
        { "draw", "res://art/characters/mixamo_sword/sword_draw.fbx" },
        { "sheathe", "res://art/characters/mixamo_sword/sword_sheathe.fbx" },
    };
    /// <summary>Clips that loop (stances).</summary>
    [Export] public string[] Looping { get; set; } = { "idle", "block_idle" };

    public Skeleton3D? Skeleton { get; private set; }
    public AnimationPlayer? Player { get; private set; }
    public string Current { get; private set; } = "";
    public bool Loaded => Skeleton != null && Player != null;

    /// <summary>Loads the clips and builds the hidden skeleton; false if the files are missing.</summary>
    public bool Build()
    {
        if (Loaded)
        {
            return true;
        }
        AnimationLibrary library = new();
        Node3D? rig = null;
        foreach (KeyValuePair<string, string> clip in Clips)
        {
            if (!ResourceLoader.Exists(clip.Value) || ResourceLoader.Load(clip.Value) is not PackedScene scene)
            {
                GD.PushWarning("SwordAnimator: missing " + clip.Value);
                continue;
            }
            Node3D instance = scene.Instantiate<Node3D>();
            AnimationPlayer? player = FindPlayer(instance);
            Animation? animation = null;
            if (player != null)
            {
                foreach (StringName name in player.GetAnimationList())
                {
                    if (name != "RESET")
                    {
                        animation = player.GetAnimation(name).Duplicate() as Animation;
                        break;
                    }
                }
            }
            if (animation != null)
            {
                DropPositions(animation);
                animation.LoopMode = Animation.LoopModeEnum.None;
                if (System.Array.IndexOf(Looping, clip.Key) >= 0)
                {
                    animation.LoopMode = Animation.LoopModeEnum.Linear;
                }
                library.AddAnimation(clip.Key, animation);
            }
            if (rig == null)
            {
                rig = instance;
            }
            else
            {
                instance.Free();
            }
        }
        if (rig == null)
        {
            return false;
        }
        rig.Name = "MixamoRig";
        rig.Visible = false;
        AddChild(rig);
        Player = FindPlayer(rig);
        foreach (Node node in rig.FindChildren("*", "Skeleton3D", true, false))
        {
            Skeleton = node as Skeleton3D;
            break;
        }
        if (Player == null || Skeleton == null)
        {
            Player = null;
            Skeleton = null;
            return false;
        }
        Player.AddAnimationLibrary(Library, library);
        // Hidden but still animated (a hidden node's AnimationPlayer keeps playing).
        foreach (Node node in rig.FindChildren("*", "MeshInstance3D", true, false))
        {
            (node as MeshInstance3D)?.Hide();
        }
        return true;
    }

    /// <summary>Plays a clip by its short name, blending from the last over <paramref name="blend"/> s.</summary>
    public void Play(string clip, float blend, float speed)
    {
        if (Player == null || !Player.HasAnimation(Library + "/" + clip))
        {
            return;
        }
        Current = clip;
        Player.Play(Library + "/" + clip, blend, speed);
    }

    /// <summary>How far through the current clip, 0–1.</summary>
    public float Progress
    {
        get
        {
            if (Player == null || !Player.IsPlaying() || Player.CurrentAnimationLength <= 0.0)
            {
                return 1.0f;
            }
            return (float)(Player.CurrentAnimationPosition / Player.CurrentAnimationLength);
        }
    }

    /// <summary>Length of a clip in seconds (at speed 1), or 0.</summary>
    public float Length(string clip)
    {
        if (Player == null || !Player.HasAnimation(Library + "/" + clip))
        {
            return 0.0f;
        }
        return (float)Player.GetAnimation(Library + "/" + clip).Length;
    }

    private static AnimationPlayer? FindPlayer(Node root)
    {
        foreach (Node node in root.FindChildren("*", "AnimationPlayer", true, false))
        {
            return node as AnimationPlayer;
        }
        return null;
    }

    /// <summary>Removes position and scale tracks: only rotations are laid over the carrier.</summary>
    private static void DropPositions(Animation animation)
    {
        for (int i = animation.GetTrackCount() - 1; i >= 0; i--)
        {
            Animation.TrackType type = animation.TrackGetType(i);
            if (type == Animation.TrackType.Position3D || type == Animation.TrackType.Scale3D)
            {
                animation.RemoveTrack(i);
            }
        }
    }
}
