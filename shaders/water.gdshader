// Água das cavernas: ondulações em várias escalas, reflexo (fresnel + SSR do ambiente),
// refração do fundo, cor pela profundidade, espuma nas bordas e cáusticas luminosas.
shader_type spatial;
render_mode blend_mix, cull_back, depth_draw_always, specular_schlick_ggx;

uniform vec4 deep_color : source_color = vec4(0.01, 0.06, 0.1, 1.0);
uniform vec4 shallow_color : source_color = vec4(0.08, 0.4, 0.5, 1.0);
uniform vec4 glow_color : source_color = vec4(0.2, 0.9, 1.0, 1.0);
uniform float depth_fade = 2.5;
uniform float foam_width = 0.35;
uniform float wave_scale = 0.35;
uniform float wave_speed = 0.35;
uniform float refraction = 0.035;
uniform float caustics = 1.0;
// poças: a borda se dissolve num contorno orgânico (malha com raio 1, escalada)
uniform float puddle = 0.0;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest;

varying vec3 wpos;
varying vec2 lpos;

void vertex() {
	wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	lpos = VERTEX.xz;
	// marolas leves (malha subdividida)
	float t = TIME * wave_speed;
	VERTEX.y += (sin(wpos.x * 0.6 + t * 2.0) + sin(wpos.z * 0.75 - t * 1.6)) * 0.025;
}

vec3 wave_normal(vec2 p, float t) {
	float e = 0.02;
	vec2 a = p * wave_scale;
	float h0 = texture(noise_tex, a + vec2(t * 0.05, t * 0.03)).r + texture(noise_tex, a * 2.3 - vec2(t * 0.04, -t * 0.06)).r * 0.6;
	float hx = texture(noise_tex, a + vec2(e, 0) + vec2(t * 0.05, t * 0.03)).r + texture(noise_tex, (a + vec2(e, 0)) * 2.3 - vec2(t * 0.04, -t * 0.06)).r * 0.6;
	float hz = texture(noise_tex, a + vec2(0, e) + vec2(t * 0.05, t * 0.03)).r + texture(noise_tex, (a + vec2(0, e)) * 2.3 - vec2(t * 0.04, -t * 0.06)).r * 0.6;
	return normalize(vec3((h0 - hx) * 6.0, 1.0, (h0 - hz) * 6.0));
}

void fragment() {
	float t = TIME * wave_speed;
	vec3 wn = wave_normal(wpos.xz, t);
	// normal em espaço de visão para a iluminação/reflexos
	NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);

	// profundidade da água atrás deste pixel (distância até o fundo)
	float raw = texture(depth_tex, SCREEN_UV).r;
	vec4 ndc = vec4(SCREEN_UV * 2.0 - 1.0, raw, 1.0);
	vec4 bottom = INV_PROJECTION_MATRIX * ndc;
	bottom.xyz /= bottom.w;
	float thickness = max(0.0, -bottom.z - (-VERTEX.z));
	float depth01 = clamp(1.0 - exp(-thickness / depth_fade), 0.0, 1.0);

	// refração: o fundo aparece distorcido pelas ondas (menos perto das bordas)
	vec2 off = wn.xz * refraction * depth01;
	vec3 under = textureLod(screen_tex, SCREEN_UV + off, 1.0).rgb;
	vec3 water = mix(shallow_color.rgb, deep_color.rgb, depth01);
	vec3 col = mix(under * shallow_color.rgb * 2.2, water, smoothstep(0.0, 0.9, depth01));

	// cáusticas: rede luminosa que se move devagar
	vec2 cp = wpos.xz * 0.45;
	float c1 = texture(noise_tex, cp + vec2(t * 0.11, t * 0.07)).r;
	float c2 = texture(noise_tex, cp * 1.7 - vec2(t * 0.09, -t * 0.12)).r;
	float caus = pow(1.0 - abs(c1 - c2), 12.0) * caustics;

	// espuma onde a água encosta na rocha
	float foam = 1.0 - smoothstep(0.0, foam_width, thickness);
	foam *= 0.6 + 0.4 * texture(noise_tex, wpos.xz * 1.3 + vec2(t * 0.2)).r;

	float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 4.0);
	ALBEDO = col * (1.0 - fres * 0.6) + vec3(foam * 0.55);
	ROUGHNESS = mix(0.03, 0.25, foam);
	METALLIC = 0.0;
	SPECULAR = 0.6;
	EMISSION = glow_color.rgb * (0.08 + caus * 0.55 * (1.0 - foam)) + vec3(foam) * glow_color.rgb * 0.15;
	float a = clamp(0.55 + depth01 * 0.4 + foam * 0.3 + fres * 0.3, 0.0, 1.0);
	if (puddle > 0.5) {
		// poça rasa: espelho escuro, contorno irregular que some no chão
		float n = texture(noise_tex, wpos.xz * 0.12).r;
		float r = length(lpos) + (n - 0.5) * 0.55;
		float edge = smoothstep(1.0, 0.7, r);
		float rim = smoothstep(0.7, 0.9, r) * edge;
		ALBEDO = mix(under * 0.55, shallow_color.rgb, 0.4);
		EMISSION = glow_color.rgb * (0.07 + caus * 0.3 + rim * 0.25) + vec3(fres * 0.06);
		ROUGHNESS = 0.02;
		SPECULAR = 0.9;
		a = edge * (0.7 + fres * 0.3);
	}
	ALPHA = a;
}
