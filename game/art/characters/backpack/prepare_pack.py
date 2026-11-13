"""
Builds trail_pack.glb (the carrier's backpack) from the team's Tripo model "3d backpack.glb", a
parts sheet: the pack with its lid and bedroll in the middle, spare parts laid out around it.

    python -I prepare_pack.py "<path to 3d backpack.glb>"

What it does (only the scene graph and one index list change; vertices and textures stay as made):
- keeps the pack, its lid, the bedroll and the front straps and buckles;
- hangs two of the spare pouches on the pack's sides, like the concept art;
- drops the other spare parts (shoulder straps, belts, chest strap, bottle) and the pack's own hip
  belt: the game draws the harness itself (player/BackpackHarness.cs), so it follows the body;
- cuts the pack's own rigid shoulder straps (the triangles behind its back panel, at its sides);
- gives the material and textures plain names.
The pack's front faces +Z and its back panel -Z, the way BackpackAttachment expects (the body side
is the lowest Z).
"""
import json
import math
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "trail_pack.glb")

PACK = "tripo_part_0"
KEEP = {PACK, "tripo_part_4",  # lid
        "tripo_part_5", "tripo_part_14", "tripo_part_20", "tripo_part_21", "tripo_part_25", "tripo_part_26",  # front straps, buckles
        "tripo_part_8", "tripo_part_15", "tripo_part_16", "tripo_part_17", "tripo_part_18"}  # bedroll and its straps
# Spare pouches hung on the pack's sides: name -> (side, height of its centre).
SIDE_POUCHES = {"tripo_part_1": (-1, 0.47), "tripo_part_7": (1, 0.47)}
# The pack's shoulder straps: triangles centred behind this Z, outside this distance from the
# pack's middle (its back panel is further forward there).
STRAP_BEHIND_Z = -0.10
STRAP_SIDE_X = 0.08
# Above this height the back panel leans forward, so everything that far back is strap.
STRAP_TOP_Y = 0.62
# Nothing of the pack itself is this far back (a loose buckle of its straps is).
STRAP_LOOSE_Z = -0.15
# Parts laid out apart on the sheet that belong on the pack: name -> (where, overlap in metres).
LID = ["tripo_part_4"]
BEDROLL = ["tripo_part_8", "tripo_part_15", "tripo_part_16", "tripo_part_17", "tripo_part_18"]
SEAT_OVERLAP = 0.03


def main(source):
    data = open(source, "rb").read()
    json_len = struct.unpack_from("<I", data, 12)[0]
    gltf = json.loads(data[20:20 + json_len])
    bin_start = 20 + json_len + 8
    blob = bytearray(data[bin_start:])

    def view(accessor_index):
        a = gltf["accessors"][accessor_index]
        bv = gltf["bufferViews"][a["bufferView"]]
        return a, bv.get("byteOffset", 0) + a.get("byteOffset", 0)

    def read(accessor_index):
        a, offset = view(accessor_index)
        count = {"VEC3": 3, "VEC2": 2, "SCALAR": 1}[a["type"]]
        fmt = {5126: "f", 5125: "I", 5123: "H"}[a["componentType"]]
        size = struct.calcsize(fmt) * count
        return [struct.unpack_from("<" + fmt * count, blob, offset + k * size) for k in range(a["count"])]

    nodes = gltf["nodes"]
    by_name = {n.get("name"): i for i, n in enumerate(nodes)}

    def bounds(node):
        t = node.get("translation", [0, 0, 0])
        lo, hi = [9.0] * 3, [-9.0] * 3
        for p in gltf["meshes"][node["mesh"]]["primitives"]:
            a = gltf["accessors"][p["attributes"]["POSITION"]]
            for k in range(3):
                lo[k] = min(lo[k], a["min"][k] + t[k])
                hi[k] = max(hi[k], a["max"][k] + t[k])
        return lo, hi

    pack = nodes[by_name[PACK]]
    pack_lo, pack_hi = bounds(pack)
    middle_x = (pack_lo[0] + pack_hi[0]) / 2

    # Cut the pack's own shoulder straps.
    t = pack.get("translation", [0, 0, 0])
    for primitive in gltf["meshes"][pack["mesh"]]["primitives"]:
        positions = read(primitive["attributes"]["POSITION"])
        accessor, offset = view(primitive["indices"])
        indices = [i[0] for i in read(primitive["indices"])]
        kept = []
        for k in range(0, len(indices), 3):
            tri = indices[k:k + 3]
            cx = sum(positions[i][0] for i in tri) / 3 + t[0]
            cy = sum(positions[i][1] for i in tri) / 3 + t[1]
            cz = sum(positions[i][2] for i in tri) / 3 + t[2]
            strap = cz < STRAP_LOOSE_Z or (cz < STRAP_BEHIND_Z and (abs(cx - middle_x) > STRAP_SIDE_X or cy > STRAP_TOP_Y))
            if not strap:
                kept.extend(tri)
        fmt = {5125: "I", 5123: "H"}[accessor["componentType"]]
        struct.pack_into("<" + fmt * len(kept), blob, offset, *kept)
        print(f"pack: {len(indices) // 3} triangles, {len(kept) // 3} after cutting its shoulder straps")
        accessor["count"] = len(kept)
        accessor.pop("min", None)
        accessor.pop("max", None)

    # Hang the spare pouches on the pack's sides (turned so their fronts face out).
    for name, (side, height) in SIDE_POUCHES.items():
        node = nodes[by_name[name]]
        lo, hi = bounds(node)
        centre = [(lo[k] + hi[k]) / 2 for k in range(3)]
        depth = hi[2] - lo[2]
        angle = side * math.pi / 2  # +X side: front (+Z) turns to +X
        rotation = [0.0, math.sin(angle / 2), 0.0, math.cos(angle / 2)]
        edge = pack_hi[0] if side > 0 else pack_lo[0]
        target = [edge + side * (depth / 2 - 0.04), height, (pack_lo[2] + pack_hi[2]) / 2 + 0.03]
        old = node.get("translation", [0, 0, 0])
        rel = [old[k] - centre[k] for k in range(3)]
        # Rotate rel about Y.
        c, s = math.cos(angle), math.sin(angle)
        turned = [c * rel[0] + s * rel[2], rel[1], -s * rel[0] + c * rel[2]]
        node["translation"] = [target[k] + turned[k] for k in range(3)]
        node["rotation"] = rotation
        KEEP.add(name)

    # Seat the lid on the pack's top and the bedroll under its bottom, both centred on the pack.
    def group_bounds(names):
        los, his = zip(*(bounds(nodes[by_name[n]]) for n in names))
        return [min(v[k] for v in los) for k in range(3)], [max(v[k] for v in his) for k in range(3)]

    pack_mid_z = (pack_lo[2] + pack_hi[2]) / 2
    for names, top in ((LID, False), (BEDROLL, True)):
        lo, hi = group_bounds(names)
        if top:
            dy = pack_lo[1] + SEAT_OVERLAP - hi[1]
        else:
            dy = pack_hi[1] - SEAT_OVERLAP - lo[1] - 0.04
        dx = middle_x - (lo[0] + hi[0]) / 2
        dz = pack_mid_z - (lo[2] + hi[2]) / 2
        for n in names:
            node = nodes[by_name[n]]
            tr = node.get("translation", [0, 0, 0])
            node["translation"] = [tr[0] + dx, tr[1] + dy, tr[2] + dz]

    root = [n for n in nodes if "children" in n][0]
    root["children"] = [i for i in root["children"] if nodes[i].get("name") in KEEP]
    print(f"kept {len(root['children'])} of {len(nodes) - 1} parts")

    for image in gltf.get("images", []):
        name = image.get("name") or ""
        if "basecolor" in name:
            image["name"] = "trail_pack_albedo"
        elif "normal" in name:
            image["name"] = "trail_pack_normal"
    for texture in gltf.get("textures", []):
        texture.pop("name", None)
    for material in gltf.get("materials", []):
        material["name"] = "trail_pack"

    text = json.dumps(gltf, separators=(",", ":")).encode()
    text += b" " * ((4 - len(text) % 4) % 4)
    bin_chunk = data[20 + json_len:20 + json_len + 8] + bytes(blob)
    out = struct.pack("<III", 0x46546C67, 2, 12 + 8 + len(text) + len(bin_chunk))
    out += struct.pack("<II", len(text), 0x4E4F534A) + text + bin_chunk
    open(OUT, "wb").write(out)
    print("wrote", OUT)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(sys.argv[1])
