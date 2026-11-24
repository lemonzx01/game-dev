shader_type canvas_item;

// Expects INSTANCE_CUSTOM = vec4(uv_x, uv_y, uv_width, uv_height) in normalized atlas space.
void vertex() {
	UV = INSTANCE_CUSTOM.xy + UV * INSTANCE_CUSTOM.zw;
}
