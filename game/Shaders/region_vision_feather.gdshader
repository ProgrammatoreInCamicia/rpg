shader_type canvas_item;
render_mode unshaded;

uniform sampler2D seen_map : filter_nearest;
uniform vec2 map_size;
uniform vec2 crop_origin;
uniform vec2 crop_size;
uniform vec2 tile_half;
uniform vec2 eye_cell;
uniform vec2 facing;
uniform bool directional;

float seen(ivec2 cell) {
	if (cell.x < 0 || cell.y < 0 || cell.x >= int(map_size.x) || cell.y >= int(map_size.y)) {
		return 0.0;
	}
	return texelFetch(seen_map, cell, 0).r;
}

void fragment() {
	vec2 point = crop_origin + UV * crop_size;
	vec2 projected = point / tile_half;
	vec2 cell = vec2((projected.x + projected.y) * 0.5,
		(projected.y - projected.x) * 0.5);
	if (seen(ivec2(round(cell))) < 0.5) discard;
	ivec2 lower = ivec2(floor(cell));
	vec2 fraction = fract(cell);
	float upper = mix(seen(lower), seen(lower + ivec2(1, 0)), fraction.x);
	float bottom = mix(seen(lower + ivec2(0, 1)), seen(lower + ivec2(1, 1)), fraction.x);
	float field = mix(upper, bottom, fraction.y);
	float edge_dark = 1.0 - smoothstep(0.5, 1.0, field);
	float angle_dark = 0.0;
	if (directional) {
		vec2 away = cell - eye_cell;
		if (dot(away, away) > 2.25) {
			float cosine = dot(facing, normalize(away));
			angle_dark = 1.0 - smoothstep(0.10, 0.24, cosine);
		}
	}
	COLOR = vec4(6.0 / 255.0, 6.0 / 255.0, 9.0 / 255.0,
		242.0 / 255.0 * max(edge_dark, angle_dark));
}