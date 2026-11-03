// Points in space as round spots that keep their size on the screen, one instance per point. The alpha of a point
// carries its size, as the spots themselves are opaque. The edge goes through alpha to coverage, which needs no sorting.
struct Uniforms {
    camera: mat4x4<f32>,
    size: vec2<f32>,
    width: f32,
    unused: f32,
    cuts: mat4x4<f32>,
}

@group(0) @binding(0) var<uniform> uniforms: Uniforms;

const gap_limit = 1e20;
const size_step = 0.25;

struct Output {
    @builtin(position) position: vec4<f32>,
    @location(0) spot: vec2<f32>,
    @location(1) color: vec3<f32>,
}

@vertex
fn vs_main(@builtin(vertex_index) index: u32, @location(0) place: vec3<f32>, @location(1) color: vec4<f32>) -> Output {
    var output: Output;
    if place.x > gap_limit || any(abs(place) > vec3(1.0001)) || is_cut(place, uniforms.cuts) {
        output.position = vec4(2.0, 2.0, 2.0, 1.0);
        return output;
    }

    var corners = array<vec2<f32>, 6>(vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(1.0, 1.0), vec2(-1.0, -1.0), vec2(1.0, 1.0), vec2(-1.0, 1.0));
    let corner = corners[index];
    let clip = uniforms.camera * vec4(place, 1.0);
    let diameter = color.a * 255.0 * size_step;
    let offset = corner * diameter / uniforms.size;
    output.position = vec4(clip.xy + (offset * clip.w), clip.zw);
    output.spot = corner;
    output.color = color.rgb;
    return output;
}

struct PointOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec3<f32>,
}

// Single pixels for very large clouds: they cost a third of a spot, which is what allows tens of millions per frame.
@vertex
fn vs_point(@location(0) place: vec3<f32>, @location(1) color: vec4<f32>) -> PointOutput {
    var output: PointOutput;
    if place.x > gap_limit || any(abs(place) > vec3(1.0001)) || is_cut(place, uniforms.cuts) {
        output.position = vec4(2.0, 2.0, 2.0, 1.0);
        return output;
    }

    output.position = uniforms.camera * vec4(place, 1.0);
    output.color = color.rgb;
    return output;
}

@fragment
fn fs_point(input: PointOutput) -> @location(0) vec4<f32> {
    return vec4(input.color, 1.0);
}

@fragment
fn fs_main(input: Output) -> @location(0) vec4<f32> {
    let distance = length(input.spot);
    return vec4(input.color, clamp(((1.0 - distance) / max(fwidth(distance), 0.0001)) + 0.5, 0.0, 1.0));
}
