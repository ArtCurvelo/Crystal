// Superfície de minério com textura procedural própria por tipo, mais rachaduras de dano.
// pattern: 0 rocha simples · 1 veios brilhantes · 2 faixas (malaquita/ágata) · 3 pepitas metálicas
//          4 iridescente (opala/pérola) · 5 magma (rachaduras incandescentes) · 6 gelo fosco · 7 vidro negro (obsidiana)
// Compatível com o renderizador Compatibility. Usa a posição local do vértice (não precisa de UV).
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_lambert, specular_schlick_ggx;

uniform int pattern = 0;
uniform vec4 base_color : source_color = vec4(0.3, 0.27, 0.3, 1.0);
uniform vec4 color2 : source_color = vec4(0.8, 0.5, 0.2, 1.0);
uniform vec4 glow_color : source_color = vec4(1.0, 0.6, 0.2, 1.0);
uniform float glow_energy = 1.0;
uniform float scale = 3.0;
uniform float damage : hint_range(0.0, 1.0) = 0.0;
uniform float flash = 0.0;
uniform float seed = 0.0;

varying vec3 lpos;

void vertex() {
	lpos = VERTEX;
}

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
	           mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

float fbm(vec3 p) {
	float v = 0.0, a = 0.5;
	for (int i = 0; i < 3; i++) { v += a * noise(p); p *= 2.03; a *= 0.5; }
	return v;
}

// distância até a borda de célula de Voronoi 3D (aproximação F2 - F1)
float cells(vec3 p) {
	vec3 n = floor(p);
	vec3 f = fract(p);
	float d1 = 8.0, d2 = 8.0;
	for (int k = -1; k <= 1; k++)
	for (int j = -1; j <= 1; j++)
	for (int i = -1; i <= 1; i++) {
		vec3 g = vec3(float(i), float(j), float(k));
		vec3 o = vec3(hash(n + g), hash(n + g + 7.1), hash(n + g + 13.7));
		float d = length(g + o - f);
		if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
	}
	return d2 - d1;
}

void fragment() {
	vec3 p = lpos * scale + seed;
	vec3 n = normalize(NORMAL);
	float fres = pow(1.0 - clamp(dot(n, normalize(VIEW)), 0.0, 1.0), 2.0);
	vec3 col = base_color.rgb * COLOR.rgb;
	vec3 emi = vec3(0.0);
	float rough = 0.85;
	float metal = 0.0;
	float g = fbm(p);

	if (pattern == 1) {            // veios brilhantes na rocha
		float e = cells(p * 0.9);
		float vein = 1.0 - smoothstep(0.0, 0.07, e);
		col = mix(col * (0.7 + 0.5 * g), color2.rgb, vein);
		emi = glow_color.rgb * vein * glow_energy * 1.2;
		rough = mix(0.85, 0.3, vein);
	} else if (pattern == 2) {     // faixas orgânicas
		float b = sin(lpos.y * scale * 7.0 + g * 7.0 + lpos.x * 2.5) * 0.5 + 0.5;
		float b2 = smoothstep(0.42, 0.58, b);
		col = mix(base_color.rgb, color2.rgb, b2) * (0.75 + 0.35 * g) * COLOR.rgb;
		emi = glow_color.rgb * pow(b, 6.0) * 0.35 * glow_energy;
		rough = 0.35;
	} else if (pattern == 3) {     // pepitas metálicas
		float s = smoothstep(0.55, 0.66, noise(p * 2.4) * 0.65 + g * 0.35);
		col = mix(col * (0.65 + 0.4 * g), color2.rgb, s);
		metal = s * 0.55;
		rough = mix(0.9, 0.25, s);
		emi = color2.rgb * s * 0.55 * glow_energy;
	} else if (pattern == 4) {     // iridescente
		float h = fres * 2.5 + g * 1.5 + TIME * 0.1;
		vec3 rainbow = 0.5 + 0.5 * cos(6.2831 * (h + vec3(0.0, 0.33, 0.67)));
		col = mix(base_color.rgb, rainbow, 0.55) * COLOR.rgb;
		emi = rainbow * (0.25 + fres) * glow_energy * 0.6 + glow_color.rgb * 0.15;
		rough = 0.12;
		metal = 0.3;
	} else if (pattern == 5) {     // magma
		float e = cells(p * 0.8);
		float crack = 1.0 - smoothstep(0.0, 0.12, e);
		float pulse = 0.75 + 0.25 * sin(TIME * 2.0 + g * 6.0);
		col = mix(col * 0.35, color2.rgb, crack);
		emi = glow_color.rgb * crack * glow_energy * 2.2 * pulse;
		rough = 0.9;
	} else if (pattern == 6) {     // gelo fosco
		float frost = smoothstep(0.45, 0.8, noise(p * 3.0) * 0.6 + g * 0.4);
		col = mix(base_color.rgb, vec3(0.92, 0.97, 1.0), frost) * COLOR.rgb;
		emi = glow_color.rgb * (0.25 + fres * 0.8) * glow_energy;
		rough = mix(0.08, 0.6, frost);
	} else if (pattern == 7) {     // vidro negro (obsidiana)
		col = base_color.rgb * COLOR.rgb;
		emi = glow_color.rgb * pow(fres, 3.0) * 1.5 * glow_energy;
		rough = 0.05;
		metal = 0.6;
	} else {                       // rocha simples
		col *= 0.7 + 0.5 * g;
	}

	// rachaduras de dano (mesma linguagem visual dos cristais)
	if (damage > 0.01) {
		float e = cells(p * 1.6 + 3.0);
		float crack = (1.0 - smoothstep(0.0, 0.03 + 0.08 * damage, e)) * step(hash(floor(p * 1.6 + 3.0)), damage * 1.2);
		emi += mix(glow_color.rgb, vec3(1.0), 0.4) * crack * (1.5 + 2.5 * damage);
	}
	ALBEDO = col;
	// leve autoiluminação: o minério continua legível mesmo sem luzes dinâmicas (qualidade Baixa)
	EMISSION = emi + col * 0.22 + vec3(flash) * 0.6;
	ROUGHNESS = rough;
	METALLIC = metal;
	SPECULAR = 0.6;
}
