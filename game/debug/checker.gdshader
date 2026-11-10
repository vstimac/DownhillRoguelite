shader_type spatial;

// Debug checker in the mesh's own space, so squares stay 1 m along a tilted
// surface. Major lines every few metres make distance and speed easy to read.

uniform vec3 color_a : source_color = vec3(0.56, 0.57, 0.6);
uniform vec3 color_b : source_color = vec3(0.4, 0.41, 0.44);
uniform vec3 line_color : source_color = vec3(0.95, 0.72, 0.25);
uniform float cell_size = 1.0;
uniform float line_spacing = 10.0;
uniform float line_width = 0.1;
uniform float surface_roughness : hint_range(0.0, 1.0) = 0.9;

varying vec3 local_pos;
varying vec3 local_normal;

void vertex() {
	local_pos = VERTEX;
	local_normal = NORMAL;
}

void fragment() {
	// Project onto the axis plane the face points along.
	vec3 n = abs(local_normal);
	vec2 uv;
	if (n.y >= n.x && n.y >= n.z) {
		uv = local_pos.xz;
	} else if (n.x >= n.z) {
		uv = local_pos.zy;
	} else {
		uv = local_pos.xy;
	}

	vec2 cell = floor(uv / cell_size);
	vec3 color = mix(color_a, color_b, mod(cell.x + cell.y, 2.0));

	// Distance in metres to the nearest major line on each axis.
	vec2 to_line = abs(fract(uv / line_spacing + 0.5) - 0.5) * line_spacing;
	if (min(to_line.x, to_line.y) < line_width * 0.5) {
		color = line_color;
	}

	ALBEDO = color;
	ROUGHNESS = surface_roughness;
}
