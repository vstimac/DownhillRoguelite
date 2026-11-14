extends SceneTree
## Original, texture-free prototype foliage. Rebuild with Godot --headless --path . --script this-file.
## Frozen meshes keep procedural authoring out of streaming frames. Distances are metres.
## Pines carry their wind in UV: x is how freely a vertex flexes (0 on the trunk, 1 at a twig's tip),
## y a per-branch phase. They use terrain/foliage_tree.gdshader; the grass uses the shared
## terrain/forest_grass.gdshader (bending by height above the root, so it needs no extra data).

const OUTPUT = "res://art/environment/forest/"
const TREE_SHADER = "res://terrain/foliage_tree.gdshader"
const PINE_HEIGHT = 12.0
## The trunk ends at this share of PINE_HEIGHT, inside the top tuft.
const TRUNK_TOP = 0.93
var vertices := PackedVector3Array()
var normals := PackedVector3Array()
var colours := PackedColorArray()
var uvs := PackedVector2Array()
var indices := PackedInt32Array()
var mid_indices := PackedInt32Array()
var far_indices := PackedInt32Array()
var random := RandomNumberGenerator.new()
var phase := 0.0

func _initialize() -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUTPUT))
	for variant in range(3):
		reset(8401 + variant * 71)
		pine(variant)
		save_asset("pine_" + str(variant), true)
	for variant in range(3):
		reset(391 + variant * 53)
		grass(variant)
		save_asset("grass_" + str(variant), false)
	# Broadleaf trees for the deeper circles: summer green, dark, autumn; and bare snags.
	var palettes := {
		"green": [Color(0.3, 0.5, 0.2), Color(0.36, 0.27, 0.2)],
		"dark": [Color(0.17, 0.3, 0.17), Color(0.24, 0.19, 0.15)],
		"fall": [Color(0.8, 0.44, 0.13), Color(0.33, 0.25, 0.19)],
	}
	var palette_seed := 0
	for palette in palettes.keys():
		palette_seed += 1
		for variant in range(3):
			reset(6100 + palette_seed * 211 + variant * 37)
			broadleaf(variant, palettes[palette][0], palettes[palette][1], palette == "fall")
			save_asset("broadleaf_" + palette + "_" + str(variant), true)
	for variant in range(2):
		reset(7300 + variant * 59)
		snag(variant)
		save_asset("snag_" + str(variant), false, true)
	quit()

func reset(seed_value: int) -> void:
	random.seed = seed_value
	vertices.clear()
	normals.clear()
	colours.clear()
	uvs.clear()
	indices.clear()
	mid_indices.clear()
	far_indices.clear()
	phase = 0.0

## One triangle with a colour and a flex per corner (the phase is the current branch's). `full` false:
## only in the coarser LODs (a stand-in for finer geometry that LOD 0 shows instead).
func tri(a: Vector3, b: Vector3, c: Vector3, ca: Color, cb: Color, cc: Color, fa: float, fb: float, fc: float, level: int = 2, full: bool = true) -> void:
	var index := vertices.size()
	var normal := (c - a).cross(b - a).normalized()
	var points := [a, b, c]
	var tints := [ca, cb, cc]
	var flexes := [fa, fb, fc]
	for k in range(3):
		vertices.append(points[k])
		normals.append(normal)
		colours.append(tints[k])
		uvs.append(Vector2(flexes[k], phase))
	for value in [index, index + 1, index + 2]:
		if full:
			indices.append(value)
		if level >= 1:
			mid_indices.append(value)
		if level >= 2:
			far_indices.append(value)

func triangle(a: Vector3, b: Vector3, c: Vector3, colour: Color, level: int = 2, flex: float = 0.0) -> void:
	tri(a, b, c, colour, colour, colour, flex, flex, flex, level)

## A tapered limb from a to b; flex runs from flex_a at its base to flex_b at its end.
func branch(a: Vector3, b: Vector3, radius: float, tip_radius: float, colour: Color, sides: int = 5,
		flex_a: float = 0.0, flex_b: float = 0.0, level: int = 2) -> void:
	var direction := (b - a).normalized()
	var axis := Vector3.UP
	if absf(direction.dot(axis)) > 0.95:
		axis = Vector3.RIGHT
	var x := direction.cross(axis).normalized()
	var z := direction.cross(x).normalized()
	for segment in range(sides):
		var angle_a := TAU * segment / sides
		var angle_b := TAU * (segment + 1) / sides
		var normal_a := x * cos(angle_a) + z * sin(angle_a)
		var normal_b := x * cos(angle_b) + z * sin(angle_b)
		var p := a + normal_a * radius
		var q := a + normal_b * radius
		var r := b + normal_b * tip_radius
		var s := b + normal_a * tip_radius
		# Bark furrows: every other face a shade darker.
		var shade := colour
		if segment % 2 == 1:
			shade = colour.darkened(0.12)
		tri(p, q, r, shade, shade, shade, flex_a, flex_a, flex_b, level)
		tri(p, r, s, shade, shade, shade, flex_a, flex_b, flex_b, level)

## A needle spray: a leaf-shaped fan of four triangles, darker at its base, flexing more toward its tip.
## full = false keeps it out of the closest LOD (a broad stand-in for the fine sprays that LOD drops).
func spray(origin: Vector3, direction: Vector3, length: float, width: float, colour: Color, level: int, flex: float, full: bool = true) -> void:
	var along := direction.normalized()
	var across := along.cross(Vector3.UP).normalized()
	if across.length_squared() < 0.1:
		across = Vector3.RIGHT
	across = across.rotated(along, random.randf_range(-0.95, 0.95))
	var middle := origin + along * length * 0.48 + Vector3.UP * length * 0.08
	var tip := origin + along * length + Vector3.DOWN * length * 0.06
	var left := middle + across * width
	var right := middle - across * width
	var base := colour.darkened(0.18)
	var edge := colour.lightened(0.05)
	var end := colour.lightened(0.12)
	var f0 := flex
	var f1 := minf(flex + 0.15, 1.0)
	var f2 := minf(flex + 0.3, 1.0)
	tri(origin, left, middle, base, edge, colour, f0, f1, f1, level, full)
	tri(origin, middle, right, base, colour, edge, f0, f1, f1, level, full)
	tri(left, tip, middle, edge, end, colour, f1, f2, f1, level, full)
	tri(middle, tip, right, colour, end, edge, f1, f2, f1, level, full)

func pine(variant: int) -> void:
	var height := PINE_HEIGHT
	var bark := Color(0.29, 0.22, 0.16)
	# Each variant its own green: blue spruce, plain pine, a sunnier yellow-green.
	var hues := [Color(0.13, 0.27, 0.22), Color(0.16, 0.29, 0.19), Color(0.2, 0.31, 0.17)]
	var needles: Color = hues[variant]
	var centres: Array[Vector3] = []
	var rings := 12
	for ring in range(rings + 1):
		var t := float(ring) / rings
		# The trunk stops just inside the top tuft, so no bare stick shows above the crown.
		centres.append(Vector3(sin(t * 3.2 + variant) * t * 0.18, t * height * TRUNK_TOP, cos(t * 2.3 + variant) * t * 0.12))
		if ring > 0:
			var t0 := float(ring - 1) / rings
			var r0 := lerpf(0.15, 0.025, t0)
			var r1 := lerpf(0.15, 0.025, t)
			# The foot flares where the roots go in.
			if ring == 1:
				r0 = 0.26
			var shade := bark.darkened(random.randf_range(-0.06, 0.1))
			branch(centres[ring - 1], centres[ring], r0, r1, shade, 7, t0 * 0.15, t * 0.15)
	# Roots: a few buttresses running out and into the ground.
	for root in range(4):
		var angle := root * TAU / 4.0 + variant + random.randf_range(-0.3, 0.3)
		var outward := Vector3(cos(angle), 0, sin(angle))
		branch(Vector3(0, 0.35, 0) + outward * 0.08, outward * random.randf_range(0.45, 0.6) + Vector3.DOWN * 0.08, 0.1, 0.02, bark.darkened(0.1), 4, 0.0, 0.0, 1)
	var layers := 13
	for layer in range(layers):
		var t := float(layer) / layers
		# Whorls climb right up under the top tuft (the last at about 0.93 of the height).
		var y := height * lerpf(0.22, 0.99, t)
		var length := height * 0.2 * pow(1.0 - t, 0.72) + 0.25
		var turn := layer * 2.399 + variant * 0.7
		var arms := 7
		if layer > 5:
			arms = 6
		if layer > 9:
			arms = 4
		# Lower branches are older and darker; the top is fresh growth.
		var layer_green := needles.darkened(lerpf(0.12, -0.12, t))
		for arm in range(arms):
			phase = random.randf()
			var angle := turn + arm * TAU / arms + random.randf_range(-0.2, 0.2)
			var outward := Vector3(cos(angle), 0, sin(angle))
			var root := centres[0].lerp(centres[rings], minf(y / (height * TRUNK_TOP), 1.0)) + Vector3(0, random.randf_range(-0.15, 0.15), 0)
			root.y = y + random.randf_range(-0.15, 0.15)
			var reach := length * random.randf_range(0.85, 1.12)
			# A drooping limb: out and down to the elbow, then out with the tip turning up a little.
			var elbow := root + outward * reach * 0.55 + Vector3.DOWN * reach * 0.2
			var tip := root + outward * reach + Vector3.DOWN * reach * 0.22
			var thickness := 0.04 * (1.0 - t) + 0.01
			branch(root, elbow, thickness, thickness * 0.55, bark, 4, 0.15, 0.5)
			branch(elbow, tip, thickness * 0.55, 0.006, bark, 4, 0.5, 0.85, 1)
			var twigs := 6
			for twig in range(twigs):
				var f := 0.2 + twig * 0.14
				var at := root.lerp(elbow, minf(f / 0.55, 1.0))
				if f > 0.55:
					at = elbow.lerp(tip, (f - 0.55) / 0.45)
				var side := outward.cross(Vector3.UP)
				var level := 0
				if twig % 2 == 0:
					level = 1
				if twig == 2 or twig == 4:
					level = 2
				var flex := lerpf(0.3, 0.8, f)
				for sign_value in [-1.0, 1.0]:
					var direction: Vector3 = (outward * 0.5 + side * sign_value * 0.75 + Vector3.UP * 0.05).normalized()
					var needle_length := length * (0.36 - f * 0.14) + 0.14
					var shade := random.randf_range(0.88, 1.14)
					var green := Color(layer_green.r * shade, layer_green.g * shade, layer_green.b * shade)
					# Sprays toward the branch's end catch more light.
					green = green.lightened(f * 0.08)
					spray(at, direction, needle_length, needle_length * 0.38, green, level, flex)
					# Fine side sprays break up the silhouette without transparency textures.
					spray(at + direction * needle_length * 0.45, outward + Vector3.UP * 0.2, needle_length * 0.65,
						needle_length * 0.24, green.lightened(0.06), 0, minf(flex + 0.15, 1.0))
			var crown := layer_green.lightened(0.12)
			spray(tip - outward * 0.2, outward + Vector3.UP * 0.35, 0.45, 0.1, crown, 2, 0.9)
			# Far away the fine sprays drop out and the limbs read as thin plates on a bare pole.
			# Two broad drooping fans per limb (mid and far LODs only) fill the tier below it.
			for sign_value in [-1.0, 1.0]:
				var side_far: Vector3 = outward.cross(Vector3.UP) * sign_value * 0.35
				spray(root.lerp(elbow, 0.35), (outward + side_far + Vector3.DOWN * 0.5).normalized(), reach * 0.95,
					reach * 0.42, layer_green.darkened(0.04), 2, 0.55, false)
	# The top: a full cone of fresh sprays round the trunk's end, then a short leader out of it.
	# (It used to be four thin sprays on a bare trunk tip, which read as a stick with an arrow.)
	phase = random.randf()
	var top := centres[rings]
	var fresh := needles.lightened(0.14)
	for k in range(7):
		var angle := k * TAU / 7.0 + variant + random.randf_range(-0.15, 0.15)
		var outward := Vector3(cos(angle), 0, sin(angle))
		spray(top + Vector3.DOWN * 1.15, outward * 0.75 + Vector3.UP, 1.25, 0.24, fresh.darkened(0.06), 2, 0.55)
		spray(top + Vector3.DOWN * 0.55, outward * 0.45 + Vector3.UP, 0.95, 0.18, fresh, 2, 0.65)
	for k in range(3):
		var angle := k * TAU / 3.0 + variant * 0.5
		var outward := Vector3(cos(angle), 0, sin(angle))
		spray(top + Vector3.DOWN * 0.1, outward * 0.12 + Vector3.UP, 0.75, 0.13, needles.lightened(0.2), 2, 0.75)

func grass(variant: int) -> void:
	var blades := 10 + variant * 3
	for blade in range(blades):
		var angle := random.randf() * TAU
		var root := Vector3(cos(angle), 0, sin(angle)) * random.randf_range(0, 0.2)
		var height := random.randf_range(0.5, 1.0)
		var lean := Vector3(cos(angle), 0, sin(angle)) * random.randf_range(0.12, 0.42)
		var face := angle + random.randf_range(-0.6, 0.6)
		var across := Vector3(-sin(face), 0, cos(face)) * random.randf_range(0.02, 0.045)
		# Some blades yellower, some bluer; dark at the root, light at the tip.
		var tint := random.randf_range(-1.0, 1.0)
		var base_colour := Color(0.36, 0.48 + tint * 0.03, 0.3 - tint * 0.03)
		var mid_colour := Color(0.55 + tint * 0.05, 0.66, 0.42 - tint * 0.05)
		var tip_colour := Color(0.8 + tint * 0.06, 0.86, 0.56 - tint * 0.06)
		# Four rows up a curving, tapering blade.
		var rows := [0.0, 0.35, 0.68, 1.0]
		var points := []
		var widths := [1.0, 0.8, 0.5, 0.0]
		for k in range(4):
			var f: float = rows[k]
			points.append(root + Vector3.UP * height * f + lean * f * f)
		var tints := [base_colour, base_colour.lerp(mid_colour, 0.7), mid_colour.lerp(tip_colour, 0.5), tip_colour]
		for k in range(3):
			var a: Vector3 = points[k]
			var b: Vector3 = points[k + 1]
			var wa: Vector3 = across * widths[k]
			var wb: Vector3 = across * widths[k + 1]
			var ca: Color = tints[k]
			var cb: Color = tints[k + 1]
			if k == 2:
				tri(a - wa, b, a + wa, ca, cb, ca, rows[k], rows[k + 1], rows[k])
			else:
				tri(a - wa, b - wb, b + wb, ca, cb, cb, rows[k], rows[k + 1], rows[k + 1])
				tri(a - wa, b + wb, a + wa, ca, cb, ca, rows[k], rows[k + 1], rows[k])

## A leaf cluster: a lumpy, faceted ball (a once-split icosahedron, each corner pushed in or out),
## lit from above (darker underneath), each facet a little different; the coarse LODs use the
## unsplit icosahedron. Flex grows toward the cluster's outside.
func leaf_cluster(centre: Vector3, radius: Vector3, colour: Color, flex: float) -> void:
	var t := (1.0 + sqrt(5.0)) / 2.0
	var corners := [Vector3(-1, t, 0), Vector3(1, t, 0), Vector3(-1, -t, 0), Vector3(1, -t, 0),
		Vector3(0, -1, t), Vector3(0, 1, t), Vector3(0, -1, -t), Vector3(0, 1, -t),
		Vector3(t, 0, -1), Vector3(t, 0, 1), Vector3(-t, 0, -1), Vector3(-t, 0, 1)]
	var faces := [[0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11], [1, 5, 9], [5, 11, 4], [11, 10, 2],
		[10, 7, 6], [7, 1, 8], [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9], [4, 9, 5], [2, 4, 11],
		[6, 2, 10], [8, 6, 7], [9, 8, 1]]
	var unit: Array[Vector3] = []
	for corner in corners:
		unit.append((corner as Vector3).normalized())
	var middles := {}
	var fine := []
	for face in faces:
		var mids := []
		for k in range(3):
			var a: int = face[k]
			var b: int = face[(k + 1) % 3]
			var key := str(mini(a, b)) + ":" + str(maxi(a, b))
			if not middles.has(key):
				middles[key] = unit.size()
				unit.append((unit[a] + unit[b]).normalized())
			mids.append(middles[key])
		fine.append([face[0], mids[0], mids[2]])
		fine.append([face[1], mids[1], mids[0]])
		fine.append([face[2], mids[2], mids[1]])
		fine.append([mids[0], mids[1], mids[2]])
	var points: Array[Vector3] = []
	for direction in unit:
		var lump := random.randf_range(0.82, 1.16)
		points.append(centre + Vector3(direction.x * radius.x, direction.y * radius.y, direction.z * radius.z) * lump)
	for pass_index in range(2):
		var list: Array = fine
		var full := true
		var level := 0
		if pass_index == 1:
			list = faces
			full = false
			level = 2
		for face in list:
			var a: Vector3 = points[face[0]]
			var b: Vector3 = points[face[1]]
			var c: Vector3 = points[face[2]]
			var normal := (b - a).cross(c - a).normalized()
			if normal.dot((a + b + c) / 3.0 - centre) < 0.0:
				normal = -normal
			var light := 0.72 + 0.38 * (normal.y * 0.5 + 0.5) + random.randf_range(-0.05, 0.05)
			var shade := Color(colour.r * light, colour.g * light, colour.b * light)
			var fa := clampf(flex + 0.25 * (a - centre).length() / maxf(radius.x, 0.01), 0.0, 1.0)
			var fb := clampf(flex + 0.25 * (b - centre).length() / maxf(radius.x, 0.01), 0.0, 1.0)
			var fc := clampf(flex + 0.25 * (c - centre).length() / maxf(radius.x, 0.01), 0.0, 1.0)
			tri(a, c, b, shade, shade, shade, fa, fc, fb, level, full)

## A broadleaf tree about 8 m tall: a trunk that splits into three or four limbs, each forking once,
## crowned with leaf clusters (autumn trees mix in reds and yellows).
func broadleaf(variant: int, leaves: Color, bark: Color, autumn: bool) -> void:
	var height := 8.0
	var split := height * random.randf_range(0.26, 0.34)
	var lean := Vector3(random.randf_range(-0.25, 0.25), 0, random.randf_range(-0.25, 0.25))
	var base := Vector3.ZERO
	var top := Vector3(0, split, 0) + lean
	var waist := base.lerp(top, 0.5) + Vector3(0.05, 0, 0)
	phase = random.randf()
	branch(base, waist, 0.3, 0.22, bark, 7, 0.0, 0.03)
	branch(waist, top, 0.22, 0.17, bark, 7, 0.03, 0.08)
	for root in range(4):
		var angle := root * TAU / 4.0 + variant
		var outward := Vector3(cos(angle), 0, sin(angle))
		branch(Vector3(0, 0.4, 0) + outward * 0.1, outward * random.randf_range(0.55, 0.75) + Vector3.DOWN * 0.08, 0.13, 0.03, bark.darkened(0.1), 4, 0.0, 0.0, 1)
	var limbs := 3 + variant % 2
	for limb in range(limbs):
		phase = random.randf()
		var angle := limb * TAU / limbs + random.randf_range(-0.3, 0.3) + variant
		var outward := Vector3(cos(angle), 0, sin(angle))
		var reach := random.randf_range(1.3, 1.9)
		var end := top + outward * reach + Vector3.UP * random.randf_range(1.8, 2.6)
		branch(top, end, 0.15, 0.07, bark, 5, 0.08, 0.35)
		for fork in range(2):
			var side := outward.cross(Vector3.UP).normalized() * (fork * 2 - 1)
			var twig_end := end + (outward * 0.7 + side * 0.9 + Vector3.UP * 0.3).normalized() * random.randf_range(0.9, 1.4)
			branch(end, twig_end, 0.07, 0.025, bark, 4, 0.35, 0.55, 1)
			var colour := leaves
			if autumn:
				var pick := random.randf()
				if pick < 0.3:
					colour = Color(0.72, 0.22, 0.1)
				elif pick < 0.55:
					colour = Color(0.86, 0.66, 0.2)
			colour = colour.lightened(random.randf_range(-0.05, 0.08))
			var size := random.randf_range(1.4, 1.9)
			leaf_cluster(twig_end, Vector3(size, size * 0.8, size), colour, 0.55)
		var crown_size := random.randf_range(1.5, 2.0)
		leaf_cluster(end + Vector3.UP * 0.6, Vector3(crown_size, crown_size * 0.85, crown_size), leaves.lightened(random.randf_range(0.0, 0.1)), 0.5)
	# The inside of the crown, so it reads as one round canopy, not separate puffs.
	for inner in range(4):
		phase = random.randf()
		var angle := inner * TAU / 4.0 + 0.4
		var at := top + Vector3(cos(angle), 0, sin(angle)) * 1.0 + Vector3.UP * random.randf_range(1.2, 1.8)
		leaf_cluster(at, Vector3(1.7, 1.3, 1.7), leaves.darkened(0.12), 0.35)
	# A cap over the middle.
	phase = random.randf()
	leaf_cluster(top + Vector3.UP * 3.4, Vector3(2.3, 1.7, 2.3), leaves.lightened(0.04), 0.45)

## A dead tree: a grey, leaning trunk with a few crooked, bare limbs.
func snag(variant: int) -> void:
	var bark := Color(0.31, 0.29, 0.27)
	var height := 7.0
	var points: Array[Vector3] = [Vector3.ZERO]
	for k in range(1, 6):
		var t := k / 5.0
		points.append(Vector3(sin(t * 2.0 + variant) * 0.35 * t, t * height, cos(t * 1.7 + variant) * 0.25 * t))
	for k in range(1, points.size()):
		phase = 0.0
		branch(points[k - 1], points[k], lerpf(0.24, 0.05, (k - 1) / 5.0), lerpf(0.24, 0.05, k / 5.0), bark.darkened(random.randf_range(-0.05, 0.1)), 6, (k - 1) * 0.04, k * 0.04)
	for limb in range(5 + variant):
		phase = random.randf()
		var at: Vector3 = points[random.randi_range(2, 4)]
		var angle := random.randf() * TAU
		var outward := Vector3(cos(angle), 0, sin(angle))
		var elbow := at + outward * random.randf_range(0.6, 1.0) + Vector3.UP * random.randf_range(0.1, 0.6)
		var bend := Vector3(random.randf_range(-0.5, 0.5), random.randf_range(0.2, 0.9), random.randf_range(-0.5, 0.5))
		var tip := elbow + (outward + bend).normalized() * random.randf_range(0.6, 1.2)
		branch(at, elbow, 0.07, 0.04, bark, 4, 0.2, 0.5)
		branch(elbow, tip, 0.04, 0.012, bark, 4, 0.5, 0.8, 1)

func save_asset(name_value: String, with_lods: bool, tree: bool = with_lods) -> void:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_COLOR] = colours
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	var lods := {}
	if with_lods:
		lods[1.5] = mid_indices
		lods[4.0] = far_indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays, [], lods)
	if tree:
		# Keep the trunk at the placement origin even when the crown is asymmetric.
		var box := mesh.get_aabb()
		var half_x := maxf(absf(box.position.x), absf(box.end.x))
		var half_z := maxf(absf(box.position.z), absf(box.end.z))
		mesh.custom_aabb = AABB(Vector3(-half_x, 0, -half_z), Vector3(half_x * 2, box.end.y, half_z * 2))
	if tree:
		# Trees sway and flutter: the wind shader, reading each vertex's flex from UV.
		var tree_material := ShaderMaterial.new()
		tree_material.shader = load(TREE_SHADER)
		tree_material.set_shader_parameter("flex_in_uv", true)
		tree_material.set_shader_parameter("use_vertex_colour", true)
		tree_material.set_shader_parameter("model_base", 0.0)
		tree_material.set_shader_parameter("model_height", maxf(mesh.get_aabb().end.y, 1.0))
		mesh.surface_set_material(0, tree_material)
	else:
		var material := StandardMaterial3D.new()
		material.vertex_color_use_as_albedo = true
		material.cull_mode = BaseMaterial3D.CULL_DISABLED
		material.roughness = 1.0
		mesh.surface_set_material(0, material)
	var mesh_path := OUTPUT + name_value + ".res"
	var error := ResourceSaver.save(mesh, mesh_path, ResourceSaver.FLAG_COMPRESS)
	if error != OK:
		push_error("Forest mesh save failed: " + str(error))
		quit(1)
	var scene := "[gd_scene format=3]\n[ext_resource type=\"ArrayMesh\" path=\"" + mesh_path + "\" id=\"1_mesh\"]\n"
	scene += "[node name=\"" + name_value + "\" type=\"Node3D\"]\n[node name=\"Geometry\" type=\"MeshInstance3D\" parent=\".\"]\nmesh = ExtResource(\"1_mesh\")\n"
	var file := FileAccess.open(OUTPUT + name_value + ".tscn", FileAccess.WRITE)
	file.store_string(scene)
	print(name_value, ": triangles ", indices.size() / 3, " / ", mid_indices.size() / 3, " / ", far_indices.size() / 3)
