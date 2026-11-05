// One instanced quad per line segment. The fragment shader computes the distance to the segment,
// which gives analytic anti-aliasing and round joins and caps without MSAA.

struct Params {
    target_size: vec2f,
    width: f32,
    color: vec4f,
    scale: vec2f,
    offset: vec2f,
    glow_radius: f32,
    glow_intensity: f32,
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
    // Position in the segment frame: x along the segment from its start, y across it.
    @location(0) local: vec2f,
    @location(1) @interpolate(flat) segment_length: f32,
}

// Lines below one pixel render one pixel wide with reduced coverage.
fn half_width() -> f32 {
    return max(params.width, 1.0) * 0.5;
}

@vertex
fn vs_main(
    @builtin(vertex_index) vertex_index: u32,
    @location(0) start: vec2f,
    @location(1) end: vec2f,
) -> VertexOutput {
    var corners = array<vec2f, 6>(
        vec2f(0.0, -1.0), vec2f(1.0, -1.0), vec2f(1.0, 1.0),
        vec2f(0.0, -1.0), vec2f(1.0, 1.0), vec2f(0.0, 1.0),
    );

    // A gap point interrupts the line. All corners collapse, so the quad covers nothing.
    if (start.x >= 1e30 || end.x >= 1e30) {
        var hidden: VertexOutput;
        hidden.position = vec4f(0.0, 0.0, 0.0, 1.0);
        hidden.local = vec2f(0.0);
        hidden.segment_length = 0.0;
        return hidden;
    }

    let first = start * params.scale + params.offset;
    let segment = (end - start) * params.scale;
    let segment_length = length(segment);
    let direction = select(vec2f(1.0, 0.0), segment / segment_length, segment_length > 0.0);
    let normal = vec2f(-direction.y, direction.x);

    // One extra pixel leaves room for the anti-aliased edge and the round caps.
    let extent = half_width() + 1.0 + params.glow_radius;
    let corner = corners[vertex_index];
    let along = corner.x * (segment_length + 2.0 * extent) - extent;
    let across = corner.y * extent;

    let position = first + direction * along + normal * across;
    let ndc = turned(position) / params.target_size * 2.0 - 1.0;

    var output: VertexOutput;
    output.position = vec4f(ndc.x, -ndc.y, 0.0, 1.0);
    output.local = vec2f(along, across);
    output.segment_length = segment_length;
    return output;
}

fn segment_distance(input: VertexOutput) -> f32 {
    let nearest = clamp(input.local.x, 0.0, input.segment_length);
    return length(vec2f(input.local.x - nearest, input.local.y));
}

// Drawn with a maximum blend: neighboring segments overlap, and their glow must not add up.
@fragment
fn fs_glow(input: VertexOutput) -> @location(0) vec4f {
    let reach = segment_distance(input) / params.glow_radius;
    let alpha = params.color.a * params.glow_intensity * exp(-2.0 * reach * reach);
    return vec4f(params.color.rgb * alpha, alpha);
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4f {
    let distance = segment_distance(input);
    let coverage = clamp(half_width() + 0.5 - distance, 0.0, 1.0) * min(params.width, 1.0);
    return vec4f(params.color.rgb, params.color.a * coverage);
}
