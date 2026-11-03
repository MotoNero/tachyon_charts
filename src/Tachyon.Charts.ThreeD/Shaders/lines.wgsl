// The grid and frame of the axis box.
@group(0) @binding(0) var<uniform> camera: mat4x4<f32>;

struct Output {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
}

@vertex
fn vs_main(@location(0) position: vec3<f32>, @location(1) color: vec4<f32>) -> Output {
    var output: Output;
    output.position = camera * vec4(position, 1.0);
    output.color = color;
    return output;
}

@fragment
fn fs_main(input: Output) -> @location(0) vec4<f32> {
    return input.color;
}
