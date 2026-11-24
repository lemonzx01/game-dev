shader_type canvas_item;
render_mode unshaded, blend_disabled;
uniform sampler2D screen : hint_screen_texture, repeat_disable, filter_linear;
void fragment() {
    float radius = 2.7 * smoothstep(0.22, 0.50, abs(SCREEN_UV.y - 0.50));
    vec2 d = SCREEN_PIXEL_SIZE * radius;
    vec3 color = texture(screen, SCREEN_UV).rgb * 0.4;
    color += texture(screen, SCREEN_UV + d).rgb * 0.15;
    color += texture(screen, SCREEN_UV - d).rgb * 0.15;
    color += texture(screen, SCREEN_UV + vec2(d.x, -d.y)).rgb * 0.15;
    color += texture(screen, SCREEN_UV + vec2(-d.x, d.y)).rgb * 0.15;
    COLOR = vec4(color, 1.0);
}
