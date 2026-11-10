using Godot;

namespace DownhillRoguelite.Environment.Fog;

/// <summary>
/// Finds the ground under a fog cell with the physics world: a ray straight down that passes
/// through anything that isn't ground (a trunk, a rock, a cart: tried again without it), then a
/// small sphere a little above that ground to see whether something stands there and blocks the
/// fog. Ground is a body in the "fog_ground" group or a height-map shape (the terrain's chunks).
/// </summary>
public sealed class PhysicsFogGround
{
    public const string GroundGroup = "fog_ground";
    /// <summary>Things in the way the ray may pass through before it gives up.</summary>
    public const int MaxPassThrough = 4;

    public GroundFogSettings Settings { get; }
    public int Rays { get; private set; }
    public int Queries { get; private set; }

    private readonly Godot.Collections.Array<Rid> _skip = new();
    private readonly PhysicsRayQueryParameters3D _ray = new();
    private readonly PhysicsShapeQueryParameters3D _shape = new();
    private readonly SphereShape3D _sphere = new();
    private readonly Godot.Collections.Array<Rid> _skipGround = new();

    public PhysicsFogGround(GroundFogSettings settings)
    {
        Settings = settings;
        _ray.CollideWithAreas = false;
        _ray.HitBackFaces = false;
        _shape.CollideWithAreas = false;
        _shape.Shape = _sphere;
    }

    /// <summary>
    /// The ground height at world X/Z, looking down from <paramref name="fromY"/>, and whether
    /// something stands on it there. False if there's no ground below (not streamed in yet).
    /// </summary>
    public bool Sample(PhysicsDirectSpaceState3D space, Vector2 at, float fromY, float cell, out float height, out bool solid)
    {
        height = 0.0f;
        solid = false;
        _skip.Clear();
        _ray.CollisionMask = Settings.GroundMask;
        Vector3 from = new(at.X, fromY, at.Y);
        Vector3 to = new(at.X, fromY - Settings.RayLength, at.Y);
        Rid ground = default;
        bool found = false;
        for (int pass = 0; pass <= MaxPassThrough && !found; pass++)
        {
            _ray.From = from;
            _ray.To = to;
            _ray.Exclude = _skip;
            Rays++;
            Godot.Collections.Dictionary hit = space.IntersectRay(_ray);
            if (hit.Count == 0)
            {
                return false;
            }
            GodotObject? collider = hit["collider"].AsGodotObject();
            Rid rid = hit["rid"].AsRid();
            if (IsGround(collider, hit["shape"].AsInt32()))
            {
                height = ((Vector3)hit["position"]).Y;
                ground = rid;
                found = true;
            }
            else
            {
                _skip.Add(rid);
            }
        }
        if (!found)
        {
            return false;
        }
        _sphere.Radius = Mathf.Max(0.05f, Settings.ObstacleSize * cell * 0.5f);
        _skipGround.Clear();
        _skipGround.Add(ground);
        _shape.Exclude = _skipGround;
        _shape.CollisionMask = Settings.GroundMask;
        _shape.Transform = new Transform3D(Basis.Identity, new Vector3(at.X, height + Settings.ObstacleHeight, at.Y));
        Queries++;
        solid = space.IntersectShape(_shape, 1).Count > 0;
        return true;
    }

    /// <summary>The terrain: tagged as fog ground, or shaped as a height map.</summary>
    public static bool IsGround(GodotObject? collider, int shapeIndex)
    {
        if (collider is not CollisionObject3D body)
        {
            return false;
        }
        if (body.IsInGroup(GroundGroup))
        {
            return true;
        }
        if (shapeIndex < 0)
        {
            return false;
        }
        uint owner = body.ShapeFindOwner(shapeIndex);
        for (int i = 0; i < body.ShapeOwnerGetShapeCount(owner); i++)
        {
            if (body.ShapeOwnerGetShape(owner, i) is HeightMapShape3D)
            {
                return true;
            }
        }
        return false;
    }
}
