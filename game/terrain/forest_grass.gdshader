// Opaque geometry: no alpha overdraw, no collision, shared by all streamed grass batches.
shader_type spatial;
render_mode cull_disabled, diffuse_burley;
uniform vec4 grass_colour : source_color = vec4(0.5, 0.62, 0.4, 1.0);
uniform float draw_distance = 45.0;
uniform float breeze_metres = 0.025;
void vertex() {
	vec3 world = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float distance_xz = length(world.xz - INV_VIEW_MATRIX[3].xz);
	float visible_height = 1.0 - smoothstep(draw_distance * 0.8, draw_distance, distance_xz);
	float tip = clamp(VERTEX.y, 0.0, 1.0);
	VERTEX.x += sin(TIME * 1.4 + world.x * 0.4 + world.z * 0.3) * tip * breeze_metres;
	VERTEX.y *= visible_height;
}
void fragment() {
	ALBEDO = grass_colour.rgb * COLOR.rgb;
	ROUGHNESS = 1.0;
}
