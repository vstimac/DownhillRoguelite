extends SceneTree
## Original, texture-free prototype foliage. Rebuild with Godot --headless --path . --script this-file.
## Frozen meshes keep procedural authoring out of streaming frames. Distances are metres.

const OUTPUT = "res://art/environment/forest/"
var vertices := PackedVector3Array()
var normals := PackedVector3Array()
var colours := PackedColorArray()
var indices := PackedInt32Array()
var mid_indices := PackedInt32Array()
var far_indices := PackedInt32Array()
var random := RandomNumberGenerator.new()

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
	quit()

func reset(seed_value: int) -> void:
	random.seed = seed_value
	vertices.clear()
	normals.clear()
	colours.clear()
	indices.clear()
	mid_indices.clear()
	far_indices.clear()

func triangle(a: Vector3, b: Vector3, c: Vector3, colour: Color, level: int = 2) -> void:
	var index := vertices.size()
	var normal := (c - a).cross(b - a).normalized()
	for point in [a, b, c]:
		vertices.append(point)
		normals.append(normal)
		colours.append(colour)
	for value in [index, index + 1, index + 2]:
		indices.append(value)
		if level >= 1:
			mid_indices.append(value)
		if level >= 2:
			far_indices.append(value)

func branch(a: Vector3, b: Vector3, radius: float, tip_radius: float, colour: Color, sides: int = 5) -> void:
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
		triangle(p, q, r, colour)
		triangle(p, r, s, colour)

func spray(origin: Vector3, direction: Vector3, length: float, width: float, colour: Color, level: int) -> void:
	var along := direction.normalized()
	var across := along.cross(Vector3.UP).normalized()
	if across.length_squared() < 0.1:
		across = Vector3.RIGHT
	across = across.rotated(along, random.randf_range(-0.95, 0.95))
	var middle := origin + along * length * 0.48 + Vector3.UP * length * 0.08
	var tip := origin + along * length
	var left := middle + across * width
	var right := middle - across * width
	triangle(origin, left, middle, colour.darkened(0.12), level)
	triangle(origin, middle, right, colour, level)
	triangle(left, tip, middle, colour.lightened(0.08), level)
	triangle(middle, tip, right, colour, level)

func pine(variant: int) -> void:
	var height := 12.0
	var bark := Color(0.29, 0.22, 0.16)
	var centres: Array[Vector3] = []
	for ring in range(11):
		var t := ring / 10.0
		centres.append(Vector3(sin(t * 3.2 + variant) * t * 0.18, t * height, cos(t * 2.3 + variant) * t * 0.12))
		if ring > 0:
			branch(centres[ring - 1], centres[ring], lerpf(0.15, 0.025, (ring - 1) / 10.0), lerpf(0.15, 0.025, t), bark, 7)
	for layer in range(11):
		var t := layer / 11.0
		var y := height * lerpf(0.25, 0.96, t)
		var length := height * 0.19 * pow(1.0 - t, 0.72)
		var phase := layer * 2.399 + variant * 0.7
		var arms := 6
		if layer > 7:
			arms = 4
		for arm in range(arms):
			var angle := phase + arm * TAU / arms + random.randf_range(-0.18, 0.18)
			var outward := Vector3(cos(angle), 0, sin(angle))
			var root := Vector3(0, y + random.randf_range(-0.15, 0.15), 0)
			var tip := root + outward * length * random.randf_range(0.85, 1.12) + Vector3.DOWN * length * 0.16
			branch(root, tip, 0.035 * (1.0 - t) + 0.009, 0.007, bark, 4)
			for twig in range(5):
				var f := 0.25 + twig * 0.15
				var at := root.lerp(tip, f)
				var side := outward.cross(Vector3.UP)
				var level := 0
				if twig % 2 == 0:
					level = 1
				if twig == 2 or twig == 4:
					level = 2
				for sign_value in [-1.0, 1.0]:
					var direction: Vector3 = (outward * 0.5 + side * sign_value * 0.75 + Vector3.UP * 0.09).normalized()
					var needle_length := length * (0.37 - f * 0.15) + 0.14
					var shade := random.randf_range(0.88, 1.14)
					var green := Color(0.16 * shade, 0.29 * shade, 0.19 * shade)
					spray(at, direction, needle_length, needle_length * 0.38, green, level)
					# Fine side sprays break up the silhouette without transparency textures.
					spray(at + direction * needle_length * 0.45, outward + Vector3.UP * 0.2, needle_length * 0.65,
						needle_length * 0.24, green.lightened(0.06), 0)
			var crown := Color(0.2, 0.34, 0.21)
			spray(tip - outward * 0.2, outward + Vector3.UP * 0.3, 0.4, 0.09, crown, 2)

func grass(variant: int) -> void:
	for blade in range(7 + variant * 2):
		var angle := random.randf() * TAU
		var root := Vector3(cos(angle), 0, sin(angle)) * random.randf_range(0, 0.18)
		var height := random.randf_range(0.55, 1.0)
		var bend := Vector3(cos(angle), 0, sin(angle)) * random.randf_range(0.15, 0.4)
		var across := Vector3(-sin(angle), 0, cos(angle)) * random.randf_range(0.018, 0.04)
		var middle := root + Vector3.UP * height * 0.58 + bend * 0.22
		var tip := root + Vector3.UP * height + bend
		triangle(root - across, middle - across * 0.55, middle + across * 0.55, Color(0.55, 0.66, 0.42))
		triangle(root - across, middle + across * 0.55, root + across, Color(0.55, 0.66, 0.42))
		triangle(middle - across * 0.55, tip, middle + across * 0.55, Color(0.82, 0.86, 0.57))

func save_asset(name_value: String, with_lods: bool) -> void:
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_COLOR] = colours
	arrays[Mesh.ARRAY_INDEX] = indices
	var lods := {}
	if with_lods:
		lods[1.5] = mid_indices
		lods[4.0] = far_indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays, [], lods)
	if with_lods:
		# Keep the trunk at the placement origin even when the crown is asymmetric.
		var box := mesh.get_aabb()
		var half_x := maxf(absf(box.position.x), absf(box.end.x))
		var half_z := maxf(absf(box.position.z), absf(box.end.z))
		mesh.custom_aabb = AABB(Vector3(-half_x, 0, -half_z), Vector3(half_x * 2, box.end.y, half_z * 2))
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
