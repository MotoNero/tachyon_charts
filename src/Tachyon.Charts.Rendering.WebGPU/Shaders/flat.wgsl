// Solid triangles with per-vertex color. Positions are pixels relative to the top left of the target.

struct Params {
    target_size: vec2f,
    width: f32,
    color: vec4f,
}

@group(0) @binding(0) var<uniform> params: Params;

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) color: vec4f,
}

@vertex
fn vs_main(@location(0) position: vec2f, @location(1) color: vec4f) -> VertexOutput {
    var output: VertexOutput;
    let ndc = position / params.target_size * 2.0 - 1.0;
    output.position = vec4f(ndc.x, -ndc.y, 0.0, 1.0);
    output.color = color;
    return output;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4f {
    return input.color;
}
