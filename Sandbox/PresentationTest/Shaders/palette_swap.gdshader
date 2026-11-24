shader_type canvas_item;

uniform bool enable_palette_swap = true;
uniform vec4 tone_light : source_color = vec4(0.95, 0.82, 0.70, 1.0);
uniform vec4 tone_mid : source_color = vec4(0.80, 0.64, 0.52, 1.0);
uniform vec4 tone_dark : source_color = vec4(0.58, 0.43, 0.34, 1.0);
uniform vec4 tone_outline : source_color = vec4(0.15, 0.14, 0.18, 1.0);
uniform float hit_flash : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	vec4 tex = texture(TEXTURE, UV);
	if (tex.a < 0.01) {
		discard;
	}

	vec4 out_col = tex;
	if (enable_palette_swap) {
		// Remap near-grayscale source pixels to the target 4-tone ramp.
		float max_c = max(tex.r, max(tex.g, tex.b));
		float min_c = min(tex.r, min(tex.g, tex.b));
		float chroma = max_c - min_c;
		if (chroma < 0.08) {
			float lum = tex.r;
			if (lum > 0.82) {
				out_col = vec4(tone_light.rgb, tex.a * tone_light.a);
			} else if (lum > 0.66) {
				out_col = vec4(tone_mid.rgb, tex.a * tone_mid.a);
			} else if (lum > 0.40) {
				out_col = vec4(tone_dark.rgb, tex.a * tone_dark.a);
			} else {
				out_col = vec4(tone_outline.rgb, tex.a * tone_outline.a);
			}
		}
	}

	out_col.rgb = mix(out_col.rgb, vec3(1.0, 0.95, 0.95), hit_flash);
	COLOR = out_col * COLOR;
}
