// Lines in space with a constant width on the screen. Each piece is a quad of six corners that reads four neighbouring
// points, one instance per piece. Both pieces at a bend compute the same corner, so they meet without a gap.
struct Uniforms {
    camera: mat4x4<f32>,
    size: vec2<f32>,
    width: f32,
    unused: f32,
    cuts: mat4x4<f32>,
}

@group(0) @binding(0) var<uniform> uniforms: Uniforms;

// A gap in the data. NaN is not reliable in shaders, so the gap is a place far outside.
const gap_limit = 1e20;
// How far a corner may reach, in half widths. Sharper bends end square instead.
const longest_corner = 2.5;
// The smallest w that a point in front of the eye keeps.
const near_w = 0.0001;

struct Output {
    @builtin(position) position: vec4<f32>,
    @location(0) place: vec3<f32>,
    @location(1) color: vec4<f32>,
}

fn is_gap(place: vec3<f32>) -> bool {
    return place.x > gap_limit;
}

fn to_screen(clip: vec4<f32>) -> vec2<f32> {
    return clip.xy / clip.w * uniforms.size * 0.5;
}

// Moves a point behind the eye onto the near side, along its line piece.
fn in_front(clip: vec4<f32>, other: vec4<f32>) -> vec4<f32> {
    if clip.w >= near_w {
        return clip;
    }

    return mix(clip, other, (near_w - clip.w) / (other.w - clip.w));
}

fn direction(from_point: vec2<f32>, to_point: vec2<f32>, fallback: vec2<f32>) -> vec2<f32> {
    let way = to_point - from_point;
    let length_squared = dot(way, way);
    if length_squared < 1e-6 {
        return fallback;
    }

    return way / sqrt(length_squared);
}

// The way from a point to the corner of the line, in half widths.
fn corner(normal: vec2<f32>, along: vec2<f32>, neighbour: vec4<f32>, here: vec2<f32>, neighbour_is_ahead: bool) -> vec2<f32> {
    if neighbour.w <= 0.0 {
        return normal;
    }

    var other = direction(to_screen(neighbour), here, along);
    var outward = -along;
    if neighbour_is_ahead {
        other = -other;
        outward = along;
    }

    let tangent = along + other;
    let length_squared = dot(tangent, tangent);
    if length_squared < 1e-4 {
        return normal;
    }

    let t = tangent / sqrt(length_squared);
    let across = vec2(-t.y, t.x);
    let reach = 1.0 / max(dot(across, normal), 0.01);

    // A sharp bend would thin the line on its way to the corner. Both pieces then keep their width and overlap.
    if reach > longest_corner {
        return normal + outward * 0.5;
    }

    return across * reach;
}

@vertex
fn vs_main(
    @builtin(vertex_index) index: u32,
    @location(0) before: vec3<f32>,
    @location(1) start: vec3<f32>,
    @location(2) start_color: vec4<f32>,
    @location(3) end: vec3<f32>,
    @location(4) end_color: vec4<f32>,
    @location(5) after: vec3<f32>,
) -> Output {
    var output: Output;
    if is_gap(start) || is_gap(end) {
        output.position = vec4(2.0, 2.0, 2.0, 1.0);
        return output;
    }

    var ends = array<f32, 6>(0.0, 1.0, 1.0, 0.0, 1.0, 0.0);
    var sides = array<f32, 6>(-1.0, -1.0, 1.0, -1.0, 1.0, 1.0);
    let at_end = ends[index] > 0.5;
    let side = sides[index];

    let first = uniforms.camera * vec4(start, 1.0);
    let last = uniforms.camera * vec4(end, 1.0);
    let clip_first = in_front(first, last);
    let clip_last = in_front(last, first);
    let screen_first = to_screen(clip_first);
    let screen_last = to_screen(clip_last);
    let along = direction(screen_first, screen_last, vec2(1.0, 0.0));
    let normal = vec2(-along.y, along.x);

    var way = normal;
    var clip = clip_first;
    if at_end {
        clip = clip_last;
        if !is_gap(after) {
            way = corner(normal, along, uniforms.camera * vec4(after, 1.0), screen_last, true);
        }
    } else if !is_gap(before) {
        way = corner(normal, along, uniforms.camera * vec4(before, 1.0), screen_first, false);
    }

    let offset = way * side * uniforms.width * 0.5 / (uniforms.size * 0.5);
    output.position = vec4(clip.xy + (offset * clip.w), clip.zw);
    if at_end {
        output.place = end;
        output.color = end_color;
    } else {
        output.place = start;
        output.color = start_color;
    }

    return output;
}

@fragment
fn fs_main(input: Output) -> @location(0) vec4<f32> {
    // What lies beyond the axes stays out of the box.
    if any(abs(input.place) > vec3(1.0001)) || is_cut(input.place, uniforms.cuts) {
        discard;
    }

    return input.color;
}
