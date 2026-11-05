// One instanced quad per scatter point. Shapes and their inward borders are analytic signed
// distance fields, so their edges stay smooth without MSAA.

struct Params {
    target_size: vec2f,
    border_width: f32,
    border_color: vec4f,
    scale: vec2f,
    offset: vec2f,
    size_scale: f32,
    shape: u32,
    // The fields of the other pipelines in between; the layout is shared.
    unused_a: vec4f,
    unused_b: vec4f,
    unused_c: vec4f,
    unused_d: vec4f,
    turn: vec4f,
}

@group(0) @binding(0) var<uniform> params: Params;

// A quarter turn clockwise for a chart whose X axis runs down: (x, y) lands at (sum - y, x).
fn turned(point: vec2f) -> vec2f {
    if (params.turn.x > 0.5) {
        return vec2f(params.turn.y - point.y, point.x);
    }

    return point;
}

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) local: vec2f,
    @location(1) color: vec4f,
    @location(2) @interpolate(flat) half_size: f32,
}

@vertex
fn vs_main(
    @builtin(vertex_index) vertex_index: u32,
    @location(0) marker_position: vec2f,
    @location(1) marker_size: f32,
    @location(2) color: vec4f,
) -> VertexOutput {
    var corners = array<vec2f, 6>(
        vec2f(-1.0, -1.0), vec2f(1.0, -1.0), vec2f(1.0, 1.0),
        vec2f(-1.0, -1.0), vec2f(1.0, 1.0), vec2f(-1.0, 1.0),
    );

    let half_size = marker_size * params.size_scale * 0.5;
    let local = corners[vertex_index];
    let pixel = marker_position * params.scale + params.offset + local * (half_size + 1.0);
    let ndc = turned(pixel) / params.target_size * 2.0 - 1.0;

    var output: VertexOutput;
    output.position = vec4f(ndc.x, -ndc.y, 0.0, 1.0);
    output.local = local * (1.0 + 1.0 / max(half_size, 0.001));
    output.color = color;
    output.half_size = half_size;
    return output;
}

fn box_distance(point: vec2f, half_extent: vec2f) -> f32 {
    let delta = abs(point) - half_extent;
    return length(max(delta, vec2f(0.0))) + min(max(delta.x, delta.y), 0.0);
}

fn plus_distance(point: vec2f) -> f32 {
    return min(box_distance(point, vec2f(0.34, 1.0)), box_distance(point, vec2f(1.0, 0.34)));
}

fn triangle_distance(value: vec2f) -> f32 {
    let root_three = 1.7320508;
    var point = vec2f(abs(value.x) - 1.0, value.y + 0.57735027);
    if (point.x + root_three * point.y > 0.0) {
        point = vec2f(point.x - root_three * point.y, -root_three * point.x - point.y) * 0.5;
    }
    point.x = point.x - clamp(point.x, -2.0, 0.0);
    return -length(point) * sign(point.y);
}

fn shape_distance(point: vec2f) -> f32 {
    switch params.shape {
        case 0u: { return length(point) - 1.0; }
        case 1u: { return box_distance(point, vec2f(1.0)); }
        case 2u: { return triangle_distance(vec2f(point.x, -point.y)); }
        case 3u: { return (abs(point.x) + abs(point.y) - 1.0) * 0.70710678; }
        case 4u: {
            let rotated = vec2f(point.x + point.y, point.y - point.x) * 0.70710678;
            return plus_distance(rotated);
        }
        default: { return plus_distance(point); }
    }
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4f {
    if (input.half_size <= 0.0) {
        discard;
    }

    let distance_pixels = shape_distance(input.local) * input.half_size;
    let outer_coverage = clamp(0.5 - distance_pixels, 0.0, 1.0);
    let fill_coverage = clamp(0.5 - distance_pixels - params.border_width, 0.0, 1.0);
    let border_coverage = outer_coverage - fill_coverage;
    let alpha = input.color.a * fill_coverage + params.border_color.a * border_coverage;
    let rgb = select(
        vec3f(0.0),
        (input.color.rgb * input.color.a * fill_coverage
            + params.border_color.rgb * params.border_color.a * border_coverage) / alpha,
        alpha > 0.0,
    );
    return vec4f(rgb, alpha);
}
