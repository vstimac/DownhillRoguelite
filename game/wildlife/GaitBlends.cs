using System.Collections.Generic;
using Godot;

namespace DownhillRoguelite.Wildlife;

/// <summary>
/// Lopes: gaits between the mannequin's walk and its jog, made once from the two clips. The jog's
/// own pace is far above the walk's (a 2 m enemy jogs at about 6.8 m/s at normal speed), so an enemy
/// running at 3-4 m/s played the jog at half speed, a slow-motion run with its arms out and both feet
/// hanging in the air. A lope mixes the walk and the jog, step for step, so a run at that pace plays
/// at about normal speed with its feet still planted.
/// </summary>
public static class GaitBlends
{
    /// <summary>The walk runs this share of a cycle ahead of the jog: its legs then match the jog's step for step (fitted on the thighs and shins; the sprint needs none).</summary>
    public const float WalkPhase = 0.08f;
    /// <summary>The jog's share in each lope.</summary>
    public static readonly float[] JogShares = { 0.25f, 0.5f, 0.75f };
    private const float KeysPerSecond = 30.0f;

    private static readonly Dictionary<string, Animation> Made = new();
    private static readonly Dictionary<ulong, float> Measured = new();
    /// <summary>The mannequin's toes: the planted one's backward speed is a clip's pace.</summary>
    public const string LeftToe = "DEF-toe.L";
    public const string RightToe = "DEF-toe.R";
    private const int PaceSamples = 160;
    /// <summary>A toe within this height (m) of the lowest any toe goes in the clip is on the ground.</summary>
    public static float GroundBand = 0.03f;

    /// <summary>The name of the lope with that share of the jog.</summary>
    public static string Name(float jogShare) => $"Lope{Mathf.RoundToInt(jogShare * 100.0f)}";

    /// <summary>
    /// Adds the lopes to the player's library (once: the imported library is shared by every driver)
    /// and returns each one's name and pace at normal speed, in m/s for the driver's own size.
    /// </summary>
    public static List<(string Name, float Pace)> AddTo(AnimationPlayer player, Skeleton3D? skeleton, string walkClip, float walkPace, string jogClip, float jogPace)
    {
        List<(string, float)> lopes = new();
        if (!player.HasAnimation(walkClip) || !player.HasAnimation(jogClip))
        {
            return lopes;
        }
        Animation walk = player.GetAnimation(walkClip);
        Animation jog = player.GetAnimation(jogClip);
        AnimationLibrary? library = player.HasAnimationLibrary("") ? player.GetAnimationLibrary("") : null;
        if (library == null)
        {
            return lopes;
        }
        foreach (float share in JogShares)
        {
            string name = Name(share);
            if (!player.HasAnimation(name))
            {
                string key = walk.ResourcePath + "|" + jog.ResourcePath + "|" + walk.GetInstanceId() + "|" + jog.GetInstanceId() + "|" + name;
                if (!Made.TryGetValue(key, out Animation? lope))
                {
                    lope = Blend(walk, jog, share);
                    Made[key] = lope;
                }
                library.AddAnimation(name, lope);
            }
            // Ground covered in one cycle over the cycle's time, as a first guess; measured on the
            // driver where it can be (a mixed stride isn't the mix of the two strides), scaled by how
            // the same measure reads the walk and the jog against their known paces.
            float distance = Mathf.Lerp(walkPace * (float)walk.Length, jogPace * (float)jog.Length, share);
            float length = Mathf.Lerp((float)walk.Length, (float)jog.Length, share);
            float pace = distance / Mathf.Max(length, 0.01f);
            if (skeleton != null)
            {
                float walkRead = StancePace(skeleton, player, walk);
                float jogRead = StancePace(skeleton, player, jog);
                float lopeRead = StancePace(skeleton, player, player.GetAnimation(name));
                if (walkRead > 0.01f && jogRead > 0.01f && lopeRead > 0.01f)
                {
                    pace = lopeRead * 0.5f * (walkPace / walkRead + jogPace / jogRead);
                }
            }
            lopes.Add((name, pace));
        }
        if (skeleton != null)
        {
            skeleton.ResetBonePoses();
        }
        return lopes;
    }

    /// <summary>
    /// How fast the planted toe of the mannequin moves back over a clip's cycle (m/s at normal speed,
    /// the mannequin's own size): the clip's pose is set bone by bone at each sample, and the lower
    /// toe's run along the ground is summed. Kept per clip; -1 if the skeleton has no toes.
    /// </summary>
    public static float StancePace(Skeleton3D skeleton, AnimationPlayer player, Animation clip)
    {
        if (Measured.TryGetValue(clip.GetInstanceId(), out float known))
        {
            return known;
        }
        int left = skeleton.FindBone(LeftToe);
        int right = skeleton.FindBone(RightToe);
        if (left < 0 || right < 0 || clip.Length <= 0.0)
        {
            return -1.0f;
        }
        List<(int Track, Animation.TrackType Type, int Bone)> tracks = new();
        for (int track = 0; track < clip.GetTrackCount(); track++)
        {
            Animation.TrackType type = clip.TrackGetType(track);
            if (type != Animation.TrackType.Rotation3D && type != Animation.TrackType.Position3D && type != Animation.TrackType.Scale3D)
            {
                continue;
            }
            int bone = skeleton.FindBone(clip.TrackGetPath(track).GetConcatenatedSubNames());
            if (bone >= 0)
            {
                tracks.Add((track, type, bone));
            }
        }
        skeleton.ResetBonePoses();
        float dt = (float)clip.Length / PaceSamples;
        Vector3[] lefts = new Vector3[PaceSamples + 1];
        Vector3[] rights = new Vector3[PaceSamples + 1];
        float lowest = float.MaxValue;
        for (int k = 0; k <= PaceSamples; k++)
        {
            double at = k * dt;
            foreach ((int track, Animation.TrackType type, int bone) in tracks)
            {
                switch (type)
                {
                    case Animation.TrackType.Rotation3D:
                        skeleton.SetBonePoseRotation(bone, clip.RotationTrackInterpolate(track, at));
                        break;
                    case Animation.TrackType.Position3D:
                        skeleton.SetBonePosePosition(bone, clip.PositionTrackInterpolate(track, at));
                        break;
                    default:
                        skeleton.SetBonePoseScale(bone, clip.ScaleTrackInterpolate(track, at));
                        break;
                }
            }
            lefts[k] = skeleton.GetBoneGlobalPose(left).Origin;
            rights[k] = skeleton.GetBoneGlobalPose(right).Origin;
            lowest = Mathf.Min(lowest, Mathf.Min(lefts[k].Y, rights[k].Y));
        }
        // Only a toe on the ground counts (within GroundBand of the lowest a toe goes): in a run's
        // flight both are up, and the one coming forward isn't sliding back.
        float distance = 0.0f;
        float time = 0.0f;
        for (int k = 1; k <= PaceSamples; k++)
        {
            foreach (Vector3[] toe in new[] { lefts, rights })
            {
                if (toe[k].Y <= lowest + GroundBand && toe[k - 1].Y <= lowest + GroundBand)
                {
                    distance += new Vector2(toe[k].X - toe[k - 1].X, toe[k].Z - toe[k - 1].Z).Length();
                    time += dt;
                }
            }
        }
        float pace = time > 0.0f ? distance / time : -1.0f;
        Measured[clip.GetInstanceId()] = pace;
        return pace;
    }

    /// <summary>One cycle mixing the walk and the jog step for step, the jog's share <paramref name="jogShare"/>.</summary>
    public static Animation Blend(Animation walk, Animation jog, float jogShare)
    {
        float length = Mathf.Lerp((float)walk.Length, (float)jog.Length, jogShare);
        Animation lope = new() { Length = length, LoopMode = Animation.LoopModeEnum.Linear };
        int keys = Mathf.Max(Mathf.CeilToInt(length * KeysPerSecond), 8);
        HashSet<(NodePath, Animation.TrackType)> done = new();
        foreach ((Animation from, Animation other) in new[] { (jog, walk), (walk, jog) })
        {
            for (int track = 0; track < from.GetTrackCount(); track++)
            {
                NodePath path = from.TrackGetPath(track);
                Animation.TrackType type = from.TrackGetType(track);
                if (type != Animation.TrackType.Rotation3D && type != Animation.TrackType.Position3D && type != Animation.TrackType.Scale3D)
                {
                    continue;
                }
                if (!done.Add((path, type)))
                {
                    continue;
                }
                int jogTrack = jog.FindTrack(path, type);
                int walkTrack = walk.FindTrack(path, type);
                int added = lope.AddTrack(type);
                lope.TrackSetPath(added, path);
                lope.TrackSetInterpolationType(added, Animation.InterpolationType.Linear);
                for (int k = 0; k <= keys; k++)
                {
                    float u = k / (float)keys;
                    double jogTime = u * jog.Length;
                    double walkTime = Mathf.PosMod(u + WalkPhase, 1.0f) * walk.Length;
                    double at = u * length;
                    switch (type)
                    {
                        case Animation.TrackType.Rotation3D:
                        {
                            Quaternion j = jogTrack >= 0 ? jog.RotationTrackInterpolate(jogTrack, jogTime) : walk.RotationTrackInterpolate(walkTrack, walkTime);
                            Quaternion w = walkTrack >= 0 ? walk.RotationTrackInterpolate(walkTrack, walkTime) : j;
                            lope.RotationTrackInsertKey(added, at, w.Normalized().Slerp(j.Normalized(), jogShare).Normalized());
                            break;
                        }
                        case Animation.TrackType.Position3D:
                        {
                            Vector3 j = jogTrack >= 0 ? jog.PositionTrackInterpolate(jogTrack, jogTime) : walk.PositionTrackInterpolate(walkTrack, walkTime);
                            Vector3 w = walkTrack >= 0 ? walk.PositionTrackInterpolate(walkTrack, walkTime) : j;
                            lope.PositionTrackInsertKey(added, at, w.Lerp(j, jogShare));
                            break;
                        }
                        default:
                        {
                            Vector3 j = jogTrack >= 0 ? jog.ScaleTrackInterpolate(jogTrack, jogTime) : walk.ScaleTrackInterpolate(walkTrack, walkTime);
                            Vector3 w = walkTrack >= 0 ? walk.ScaleTrackInterpolate(walkTrack, walkTime) : j;
                            lope.ScaleTrackInsertKey(added, at, w.Lerp(j, jogShare));
                            break;
                        }
                    }
                }
            }
        }
        return lope;
    }
}
