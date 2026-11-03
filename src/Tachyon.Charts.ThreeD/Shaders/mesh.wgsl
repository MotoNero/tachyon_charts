// Surfaces from triangles. The functions of light.wgsl stand in front of this file.
struct Uniforms {
    camera: mat4x4<f32>,
    eye: vec4<f32>,
    material: vec4<f32>,
    cuts: mat4x4<f32>,
}

@group(0) @binding(0) var<uniform> uniforms: Uniforms;

struct Output {
    @builtin(position) position: vec4<f32>,
    @location(0) place: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) color: vec4<f32>,
}

@vertex
fn vs_main(@location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) color: vec4<f32>) -> Output {
    var output: Output;
    output.position = uniforms.camera * vec4(position, 1.0);
    output.place = position;
    output.normal = normal;
    output.color = color;
    return output;
}

@fragment
fn fs_opaque(input: Output) -> @location(0) vec4<f32> {
    if is_cut(input.place, uniforms.cuts) || any(abs(input.place) > vec3(1.0001)) {
        discard;
    }

    return vec4(light_surface(input.color.rgb, input.place, input.normal, uniforms.eye.xyz, uniforms.material), 1.0);
}

@fragment
fn fs_transparent(input: Output) -> Weighted {
    if is_cut(input.place, uniforms.cuts) || any(abs(input.place) > vec3(1.0001)) {
        discard;
    }

    let color = light_surface(input.color.rgb, input.place, input.normal, uniforms.eye.xyz, uniforms.material);
    return weigh(color, uniforms.material.z * input.color.a, input.position.z);
}
