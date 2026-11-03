// A texture on a rectangle. The rectangle comes from the uniforms, so that no vertex buffer is needed.
// textureLoad picks the nearest texel, like the Skia backend, so that both show the same pixels.

struct Params {
    target_size: vec2f,
    width: f32,
    color: vec4f,
    scale: vec2f,
    offset: vec2f,
}

@group(0) @binding(0) var<uniform> params: Params;
@group(1) @binding(0) var image: texture_2d<f32>;

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
}

@vertex
fn vs_main(@builtin(vertex_index) vertex_index: u32) -> VertexOutput {
    var corners = array<vec2f, 6>(
        vec2f(0.0, 0.0), vec2f(1.0, 0.0), vec2f(1.0, 1.0),
        vec2f(0.0, 0.0), vec2f(1.0, 1.0), vec2f(0.0, 1.0),
    );

    let corner = corners[vertex_index];
    let position = params.offset + params.scale * corner;
    let ndc = position / params.target_size * 2.0 - 1.0;

    var output: VertexOutput;
    output.position = vec4f(ndc.x, -ndc.y, 0.0, 1.0);
    output.uv = corner;
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4f {
    let size = vec2i(textureDimensions(image));
    let texel = clamp(vec2i(input.uv * vec2f(size)), vec2i(0), size - 1);
    return textureLoad(image, texel, 0);
}
