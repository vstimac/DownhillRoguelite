extends SceneTree

## Prints a model's node tree, skeleton bones (with rest positions) and mesh bounds.
## godot --headless --path . --script res://tools/dump_model.gd -- res://path/model.fbx
func _init() -> void:
	var path: String = OS.get_cmdline_user_args()[0]
	var root: Node = (load(path) as PackedScene).instantiate()
	get_root().add_child(root)
	_walk(root, "")
	quit()

func _walk(node: Node, indent: String) -> void:
	var line := indent + node.name + " (" + node.get_class() + ")"
	if node is MeshInstance3D and node.mesh != null:
		line += " aabb " + str(node.mesh.get_aabb()) + " surfaces " + str(node.mesh.get_surface_count())
		for i in node.mesh.get_surface_count():
			var m: Material = node.mesh.surface_get_material(i)
			if m is BaseMaterial3D:
				line += " [" + str(i) + ": " + str((m as BaseMaterial3D).albedo_color) + "]"
	if node is Node3D:
		line += " pos " + str((node as Node3D).position) + " scale " + str((node as Node3D).scale)
	print(line)
	if node is Skeleton3D:
		var skeleton := node as Skeleton3D
		for b in skeleton.get_bone_count():
			print(indent + "  bone " + str(b) + " " + skeleton.get_bone_name(b) + " parent " + str(skeleton.get_bone_parent(b)) + " global " + str(skeleton.get_bone_global_rest(b).origin))
	if node is AnimationPlayer:
		print(indent + "  clips " + str((node as AnimationPlayer).get_animation_list()))
	for child in node.get_children():
		_walk(child, indent + "  ")
