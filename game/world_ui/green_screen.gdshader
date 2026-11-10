shader_type canvas_item;

// Keys out a green-screen background: pixels near key_color turn transparent, with a soft edge.
uniform vec3 key_color : source_color = vec3(0.0, 1.0, 0.0);
uniform float threshold : hint_range(0.0, 1.0) = 0.32;
uniform float softness : hint_range(0.001, 0.5) = 0.13;
// Pulls the green fringe left on fur edges back toward grey.
uniform float despill : hint_range(0.0, 1.0) = 0.6;

void fragment() {
	vec4 pixel = texture(TEXTURE, UV);
	pixel.a *= smoothstep(threshold, threshold + softness, distance(pixel.rgb, key_color));
	float spill = max(pixel.g - max(pixel.r, pixel.b), 0.0);
	pixel.g -= spill * despill;
	COLOR = pixel;
}
