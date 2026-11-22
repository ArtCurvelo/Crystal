shader_type spatial;
// Campo de força dos portões de região: favos hexagonais com borda acesa, ondas de energia subindo,
// células que piscam devagar e moldura mais forte. "strength" vai a 0 quando o portão abre.
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;

uniform vec4 color : source_color = vec4(0.3, 1.0, 0.75, 1.0);
uniform float strength = 1.0;
uniform vec2 size = vec2(7.0, 5.0);
varying vec2 p;

void vertex() {
	p = UV * size;
}

float hex_dist(vec2 q) {
	q = abs(q);
	return max(dot(q, normalize(vec2(1.0, 1.7320508))), q.x);
}

vec4 hex_coords(vec2 uv) {
	vec2 r = vec2(1.0, 1.7320508);
	vec2 h = r * 0.5;
	vec2 a = mod(uv, r) - h;
	vec2 b = mod(uv - h, r) - h;
	vec2 gv = dot(a, a) < dot(b, b) ? a : b;
	return vec4(gv, uv - gv);
}

void fragment() {
	vec4 h = hex_coords(p * 1.7);
	float d = hex_dist(h.xy);
	float edge = smoothstep(0.40, 0.49, d);
	// ondas de energia subindo pelo campo
	float wave = pow(0.5 + 0.5 * sin(-p.y * 1.6 + TIME * 2.2 + sin(p.x * 0.7) * 0.8), 3.0);
	// cada célula pisca no seu ritmo
	float cell = 0.5 + 0.5 * sin(TIME * 1.3 + h.z * 2.7 + h.w * 1.9);
	// moldura: mais forte perto das bordas do portão
	vec2 e = min(UV, 1.0 - UV) * size;
	float border = 1.0 - smoothstep(0.0, 0.45, min(e.x, e.y));
	float pulse = 0.85 + 0.15 * sin(TIME * 2.0);
	float a = edge * (0.22 + 0.55 * wave) + cell * 0.05 + 0.03 + border * 0.5;
	ALBEDO = color.rgb * a * strength * pulse;
}
