// Rocha natural da caverna (sem repetição visível):
// - projeção triplanar suave com distorção de domínio (3 escalas de ruído);
// - camadas sedimentares nas paredes, variação quente/fria, placas e rachaduras (ruído celular);
// - chão com terra e cascalho; minerais e veios só como detalhes raros.
shader_type spatial;
render_mode cull_back;

uniform vec3 rock_color : source_color = vec3(0.36, 0.3, 0.32);
uniform vec3 floor_color : source_color = vec3(0.28, 0.24, 0.22);
uniform vec3 accent : source_color = vec3(0.35, 0.9, 1.0);
uniform float speckle = 1.0;
uniform float wetness = 1.0;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D cell_tex : filter_linear_mipmap, repeat_enable;

varying vec3 wpos;
varying vec3 wnorm;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

float tri(sampler2D t, vec3 p, vec3 w, float s) {
	return texture(t, p.zy * s).r * w.x + texture(t, p.xz * s + vec2(0.37, 0.11)).r * w.y + texture(t, p.xy * s + vec2(0.71, 0.53)).r * w.z;
}

void fragment() {
	vec3 w = pow(abs(wnorm), vec3(4.0));
	w /= (w.x + w.y + w.z);
	// distorção de domínio: quebra a grade da textura
	vec3 p = wpos + vec3(tri(noise_tex, wpos, w, 0.011) - 0.5, tri(noise_tex, wpos.zxy, w, 0.014) - 0.5, tri(noise_tex, wpos.yzx, w, 0.009) - 0.5) * 9.0;
	float big = tri(noise_tex, p, w, 0.019);
	float mid = tri(noise_tex, p, w, 0.083);
	float fine = tri(noise_tex, p, w, 0.37);
	float cells = tri(cell_tex, p, w, 0.07);
	float up = smoothstep(0.55, 0.85, wnorm.y);

	// paredes: camadas sedimentares onduladas + manchas claras/escuras
	float strata = sin(p.y * 1.3 + big * 10.0 + mid * 3.0) * 0.5 + 0.5;
	vec3 dark = rock_color * 0.5;
	vec3 light = rock_color * 1.45 + vec3(0.03);
	vec3 wall = mix(dark, light, smoothstep(0.2, 0.85, mid * 0.55 + strata * 0.45));
	wall *= 0.78 + 0.4 * big;
	wall = mix(wall, wall * vec3(1.12, 1.0, 0.86), smoothstep(0.45, 0.75, big));   // tons quentes
	wall = mix(wall, wall * vec3(0.9, 0.97, 1.08), smoothstep(0.55, 0.3, big));    // tons frios

	// chão: terra com cascalho claro
	vec3 flr = mix(floor_color * 0.75, floor_color * 1.25, mid) * (0.85 + 0.3 * fine);
	flr = mix(flr, rock_color * 1.5, smoothstep(0.7, 0.78, fine) * 0.55);

	vec3 col = mix(wall, flr, up);
	// rachaduras e juntas das placas de rocha
	float crack = 1.0 - smoothstep(0.015, 0.06, cells);
	col *= 1.0 - crack * 0.6;
	col *= 0.88 + 0.24 * fine;
	// poças e rocha molhada no chão: escurece e fica lisa (reflete com o SSR)
	float wet = smoothstep(0.62, 0.7, tri(noise_tex, p * 0.6 + vec3(13.0), w, 0.05)) * up * wetness;
	float film = smoothstep(0.45, 0.6, big) * up * wetness * 0.6;
	col = mix(col, col * 0.55, max(wet, film * 0.5));
	ALBEDO = col;
	ROUGHNESS = mix(mix(0.95, 0.7, smoothstep(0.65, 0.9, fine)), 0.06, max(wet, film * 0.6));
	SPECULAR = mix(0.35, 0.6, wet);

	// minerais: pontinhos raros e veios finos só em algumas manchas
	float sp = smoothstep(0.86, 0.9, tri(noise_tex, p, w, 1.17)) * (1.0 - up * 0.8);
	float vein = (1.0 - smoothstep(0.0, 0.006, abs(mid - 0.5))) * smoothstep(0.72, 0.82, big);
	float pulse = 0.8 + 0.2 * sin(TIME * 1.1 + wpos.x * 0.05 + wpos.z * 0.04);
	EMISSION = accent * (sp * 0.55 + vein * 0.7 * pulse) * speckle;
}
