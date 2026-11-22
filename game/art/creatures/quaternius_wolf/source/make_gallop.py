"""
Adds a gallop to the Quaternius wolf (CC0) and exports it as FBX/Wolf_Gallop.fbx.

The pack's wolf only walks, so every animal and night beast on this model ran at 7-12 m/s with a
sped-up walk and its feet skated. This keys a rotary gallop on the rig's own IK targets (the legs
follow by IK, as in the pack's Walking clip) plus the body's bob, back flex, neck and tail, bakes it
and exports only that clip; the game adds it to the wolf's AnimationPlayer (WildlifeSpecies.ExtraClips).

Run:  blender -b Wolf.blend --python make_gallop.py -- <out.fbx> [<render dir>]
"""
import math
import sys

import bpy
from mathutils import Quaternion, Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = argv[0] if argv else "Wolf_Gallop.fbx"
RENDER = argv[1] if len(argv) > 1 else None

FRAMES = 16  # one stride at 24 fps: 0.67 s
arm = bpy.data.objects["WolfArmature"]
scene = bpy.context.scene
scene.render.fps = 24

action = bpy.data.actions.new("Gallop")
arm.animation_data_create()
arm.animation_data.action = action
if hasattr(arm.animation_data, "action_slot") and action.slots:
    arm.animation_data.action_slot = action.slots[0]

# Rest pose for every bone (the walk's keys would otherwise leak in).
for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"
    pb.location = (0, 0, 0)
    pb.rotation_quaternion = (1, 0, 0, 0)
    pb.scale = (1, 1, 1)

# Feet: (IK bone, touchdown phase, duty, reach forward, reach back, lift, side offset as in the walk).
FEET = [
    ("IKBackLeft", 0.00, 0.27, 0.95, 0.70, 0.42, -0.135),
    ("IKBackRight", 0.09, 0.27, 0.95, 0.70, 0.42, 0.0),
    ("IKFrontLeft", 0.42, 0.24, 0.85, 0.75, 0.55, -0.0741),
    ("IKFrontRight", 0.51, 0.24, 0.85, 0.75, 0.55, 0.0824),
]


def smooth(x):
    return x * x * (3.0 - 2.0 * x)


def foot(phase, t0, duty, ahead, behind, lift):
    """Bone-local offset of an IK target: y is along the body (forward), z is up."""
    s = (phase - t0) % 1.0
    if s < duty:
        # On the ground, sliding back under the body at a constant rate (the body moves over it).
        k = s / duty
        return ahead + (-behind - ahead) * k, 0.0
    k = (s - duty) / (1.0 - duty)
    y = -behind + (ahead + behind) * smooth(k)
    # Lifts early and folds high, then reaches down onto the ground.
    z = lift * math.sin(math.pi * min(1.0, k * 1.1)) ** 0.8
    return y, z


def arm_rot_to_local(bone_name, axis, degrees):
    """A rotation about an armature axis, as the pose bone's local rotation."""
    rest = arm.data.bones[bone_name].matrix_local.to_quaternion()
    q = Quaternion(axis, math.radians(degrees))
    return rest.inverted() @ q @ rest


def arm_vec_to_local(bone_name, v):
    rest = arm.data.bones[bone_name].matrix_local.to_quaternion()
    return rest.inverted() @ Vector(v)


SIDE = Vector((0.0, 1.0, 0.0))  # pitch axis (the wolf faces +X, up is +Z)

for f in range(FRAMES + 1):
    phase = (f % FRAMES) / FRAMES
    scene.frame_set(f)
    for name, t0, duty, ahead, behind, lift, side in FEET:
        y, z = foot(phase, t0, duty, ahead, behind, lift)
        pb = arm.pose.bones[name]
        pb.location = (side, y, z)
        pb.keyframe_insert("location", frame=f)
    # Body: high in the suspension after the front feet push off, low over the front stance.
    w = 2.0 * math.pi * phase
    # Low through both stances, up only in the suspension after the front feet leave (phase ~0.88).
    gap = (phase - 0.88 + 0.5) % 1.0 - 0.5
    # (The small sine keeps every key distinct: the FBX exporter drops runs of equal keys and
    # Godot would then interpolate straight across the flat stretch.)
    bob = -0.14 + 0.16 * math.exp(-(gap / 0.09) ** 2) + 0.01 * math.sin(w)
    root = arm.pose.bones["Bone"]
    root.location = arm_vec_to_local("Bone", (0.0, 0.0, bob))
    root.rotation_quaternion = arm_rot_to_local("Bone", SIDE, 5.0 * math.sin(w - 2.0 * math.pi * 0.15))
    root.keyframe_insert("location", frame=f)
    root.keyframe_insert("rotation_quaternion", frame=f)
    # The back rounds as the hind legs reach forward, stretches as they push.
    flex = 11.0 * math.sin(w - 2.0 * math.pi * 0.65)
    spine = arm.pose.bones["Bone.001"]
    spine.rotation_quaternion = arm_rot_to_local("Bone.001", SIDE, flex)
    spine.keyframe_insert("rotation_quaternion", frame=f)
    # Neck and head steady the gaze against the bob.
    neck = arm.pose.bones["Bone.002"]
    neck.rotation_quaternion = arm_rot_to_local("Bone.002", SIDE, -0.7 * flex - 6.0)
    neck.keyframe_insert("rotation_quaternion", frame=f)
    head = arm.pose.bones["Bone.003"]
    head.rotation_quaternion = arm_rot_to_local("Bone.003", SIDE, 8.0)
    head.keyframe_insert("rotation_quaternion", frame=f)
    # The tail streams out behind and swings with a lag.
    for i, name in enumerate(["Bone.004", "Bone.005", "Bone.006", "Bone.007"]):
        tail = arm.pose.bones[name]
        tail.rotation_quaternion = arm_rot_to_local(name, SIDE, 6.0 + 6.0 * math.sin(w - 0.9 - 0.5 * i))
        tail.keyframe_insert("rotation_quaternion", frame=f)

scene.frame_start = 0
scene.frame_end = FRAMES

# Paws: flat on the ground while the foot is down, curled a little in the air. The toe bones sit
# below the IK chain, so without this they tip with the shin and the paw digs in or hangs.
from mathutils import Matrix

TOES = {"IKBackLeft": "Bone.010", "IKBackRight": "Bone.013", "IKFrontLeft": "Bone.019", "IKFrontRight": "Bone.016"}
for f in range(FRAMES + 1):
    phase = (f % FRAMES) / FRAMES
    scene.frame_set(f)
    bpy.context.view_layer.update()
    for name, t0, duty, ahead, behind, lift, side in FEET:
        toe = arm.pose.bones[TOES[name]]
        s = (phase - t0) % 1.0
        # 1 on the ground, easing to 0.35 at the top of the swing.
        if s < duty:
            level = 1.0
        else:
            k = (s - duty) / (1.0 - duty)
            level = 1.0 - 0.65 * math.sin(math.pi * k)
        natural = toe.matrix.to_quaternion()
        flat = arm.data.bones[TOES[name]].matrix_local.to_quaternion()
        rot = natural.slerp(flat, level)
        toe.matrix = Matrix.LocRotScale(toe.matrix.to_translation(), rot, None)
        bpy.context.view_layer.update()
        toe.keyframe_insert("rotation_quaternion", frame=f)

# Bake the IK into the leg bones so the FBX carries plain bone rotations (as the walk does).
bpy.ops.object.mode_set(mode="OBJECT")
bpy.context.view_layer.objects.active = arm
arm.select_set(True)
bpy.ops.object.mode_set(mode="POSE")
bpy.ops.pose.select_all(action="SELECT")
bpy.ops.nla.bake(frame_start=0, frame_end=FRAMES, only_selected=False, visual_keying=True,
                 clear_constraints=False, use_current_action=True, bake_types={"POSE"})
bpy.ops.object.mode_set(mode="OBJECT")
baked = arm.animation_data.action
baked.name = "Gallop"
for a in list(bpy.data.actions):
    if a != baked:
        a.use_fake_user = False
print("GALLOP baked", baked.name, baked.frame_range[:])

if RENDER:
    import os
    os.makedirs(RENDER, exist_ok=True)
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.render.resolution_x = 480
    scene.render.resolution_y = 300
    scene.render.resolution_percentage = 100
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "MATERIAL"
    cam_data = bpy.data.cameras.new("SideCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 13.0
    cam = bpy.data.objects.new("SideCam", cam_data)
    scene.collection.objects.link(cam)
    cam.location = (0.0, -30.0, 3.0)
    cam.rotation_euler = (math.radians(90), 0.0, 0.0)
    scene.camera = cam
    # A ground line to judge the feet.
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0.0))
    for f in range(0, FRAMES, 2):
        scene.frame_set(f)
        scene.render.filepath = os.path.join(RENDER, f"gallop_{f:02d}.png")
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(bpy.data.objects["Plane"])
    bpy.data.objects.remove(cam)

# Export only the gallop, with the same armature and bones as the pack's FBX.
for a in list(bpy.data.actions):
    if a != baked:
        bpy.data.actions.remove(a)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=False, object_types={"ARMATURE", "MESH"},
                         add_leaf_bones=True, bake_anim=True, bake_anim_use_all_actions=True,
                         bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
                         bake_anim_simplify_factor=0.0)
print("GALLOP exported", OUT)
