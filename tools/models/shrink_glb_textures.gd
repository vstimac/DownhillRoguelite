extends SceneTree
## Rewrites a .glb with its embedded textures scaled down (generated models, e.g. from Tripo, often
## ship 4K-8K maps that a game character or NPC doesn't need). Mesh, skin and everything else are
## copied byte for byte; only the image buffers change. JPEG stays JPEG, PNG stays PNG.
##
##   godot --headless -s tools/models/shrink_glb_textures.gd -- <in.glb> <out.glb> [max size, default 1024]

const GLB_MAGIC := 0x46546C67
const CHUNK_JSON := 0x4E4F534A
const CHUNK_BIN := 0x004E4942


func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 2:
		printerr("usage: -- <in.glb> <out.glb> [max size]")
		quit(1)
		return
	var max_size := 1024
	if args.size() > 2:
		max_size = int(args[2])
	quit(shrink(args[0], args[1], max_size))


func shrink(in_path: String, out_path: String, max_size: int) -> int:
	var data := FileAccess.get_file_as_bytes(in_path)
	if data.size() < 20 or data.decode_u32(0) != GLB_MAGIC:
		printerr("not a .glb: ", in_path)
		return 1
	var json_length := data.decode_u32(12)
	var gltf: Dictionary = JSON.parse_string(data.slice(20, 20 + json_length).get_string_from_utf8())
	var bin_start := 20 + json_length + 8
	var bin_length := data.decode_u32(20 + json_length)
	var bin := data.slice(bin_start, bin_start + bin_length)

	var views: Array = gltf["bufferViews"]
	var replaced := {}
	for image: Dictionary in gltf.get("images", []):
		if not image.has("bufferView"):
			continue
		var view: Dictionary = views[int(image["bufferView"])]
		var offset := int(view.get("byteOffset", 0))
		var bytes := bin.slice(offset, offset + int(view["byteLength"]))
		var picture := Image.new()
		var png: bool = image.get("mimeType", "") == "image/png"
		var error := OK
		if png:
			error = picture.load_png_from_buffer(bytes)
		else:
			error = picture.load_jpg_from_buffer(bytes)
		if error != OK:
			printerr("can't read image ", image.get("name", "?"))
			return 1
		var size := picture.get_size()
		if maxi(size.x, size.y) > max_size:
			var scale := float(max_size) / float(maxi(size.x, size.y))
			picture.resize(maxi(1, int(size.x * scale)), maxi(1, int(size.y * scale)), Image.INTERPOLATE_LANCZOS)
			if png:
				bytes = picture.save_png_to_buffer()
			else:
				bytes = picture.save_jpg_to_buffer(0.9)
		print("%s: %dx%d -> %dx%d" % [image.get("name", "?"), size.x, size.y, picture.get_width(), picture.get_height()])
		replaced[int(image["bufferView"])] = bytes

	# Lay every buffer view out again, 4-byte aligned, in the original order.
	var out_bin := PackedByteArray()
	for index in views.size():
		var view: Dictionary = views[index]
		var bytes: PackedByteArray
		if replaced.has(index):
			bytes = replaced[index]
		else:
			var offset := int(view.get("byteOffset", 0))
			bytes = bin.slice(offset, offset + int(view["byteLength"]))
		while out_bin.size() % 4 != 0:
			out_bin.append(0)
		view["byteOffset"] = out_bin.size()
		view["byteLength"] = bytes.size()
		out_bin.append_array(bytes)
	while out_bin.size() % 4 != 0:
		out_bin.append(0)
	gltf["buffers"][0]["byteLength"] = out_bin.size()

	var json_bytes := JSON.stringify(whole_numbers(gltf)).to_utf8_buffer()
	while json_bytes.size() % 4 != 0:
		json_bytes.append(0x20)
	var out := PackedByteArray()
	out.resize(12)
	out.encode_u32(0, GLB_MAGIC)
	out.encode_u32(4, 2)
	out.encode_u32(8, 12 + 8 + json_bytes.size() + 8 + out_bin.size())
	var header := PackedByteArray()
	header.resize(8)
	header.encode_u32(0, json_bytes.size())
	header.encode_u32(4, CHUNK_JSON)
	out.append_array(header)
	out.append_array(json_bytes)
	header.encode_u32(0, out_bin.size())
	header.encode_u32(4, CHUNK_BIN)
	out.append_array(header)
	out.append_array(out_bin)

	var file := FileAccess.open(out_path, FileAccess.WRITE)
	if file == null:
		printerr("can't write ", out_path)
		return 1
	file.store_buffer(out)
	file.close()
	print("%s: %.1f MB -> %.1f MB" % [out_path.get_file(), data.size() / 1048576.0, out.size() / 1048576.0])
	return 0


## JSON.parse_string reads every number as a float, and glTF indices must be written as integers:
## whole numbers go back out as ints (a float field holding 1 stays valid as 1).
func whole_numbers(value: Variant) -> Variant:
	if value is Dictionary:
		var result := {}
		for key: Variant in value:
			result[key] = whole_numbers(value[key])
		return result
	if value is Array:
		var list := []
		for item: Variant in value:
			list.append(whole_numbers(item))
		return list
	# Exact whole numbers only: 0.99999994 in a bone matrix must stay a float, not become 0 or 1.
	if value is float and value == roundf(value) and absf(value) < 9.0e15:
		return int(roundf(value))
	return value
