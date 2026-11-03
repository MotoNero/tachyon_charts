// Marching squares. Each cell of the grid and each level appends up to two line segments.
// The rules are the same as in MarchingSquares.cs, so that both find the same segments.

struct Params {
    level_count: u32,
    capacity: u32,
}

@group(0) @binding(0) var<uniform> params: Params;
@group(0) @binding(1) var values: texture_2d<f32>;
@group(0) @binding(2) var<storage, read> levels: array<f32>;
@group(0) @binding(3) var<storage, read_write> segments: array<vec2f>;
@group(0) @binding(4) var<storage, read_write> counter: atomic<u32>;

// The point that interrupts a line, PolylineStyle.Gap.
const gap = vec2f(1e30, 1e30);

fn is_nan(value: f32) -> bool {
    return (bitcast<u32>(value) & 0x7fffffffu) > 0x7f800000u;
}

fn append(start: vec2f, end: vec2f) {
    let index = atomicAdd(&counter, 1u);
    if index < params.capacity {
        segments[index * 3u] = start;
        segments[index * 3u + 1u] = end;
        segments[index * 3u + 2u] = gap;
    }
}

fn crossing(first: f32, second: f32, level: f32) -> f32 {
    return (level - first) / (second - first);
}

// The crossing of an edge: 0 bottom, 1 right, 2 top, 3 left.
fn edge_point(edge: i32, cell: vec2f, a: f32, b: f32, c: f32, d: f32, level: f32) -> vec2f {
    switch edge {
        case 0: { return cell + vec2f(crossing(a, b, level), 0.0); }
        case 1: { return cell + vec2f(1.0, crossing(b, c, level)); }
        case 2: { return cell + vec2f(crossing(d, c, level), 1.0); }
        default: { return cell + vec2f(0.0, crossing(a, d, level)); }
    }
}

fn append_edges(first: i32, second: i32, cell: vec2f, a: f32, b: f32, c: f32, d: f32, level: f32) {
    append(edge_point(first, cell, a, b, c, d, level), edge_point(second, cell, a, b, c, d, level));
}

fn march(cell: vec2f, a: f32, b: f32, c: f32, d: f32, level: f32) {
    var mask = 0u;
    if a >= level { mask |= 1u; }
    if b >= level { mask |= 2u; }
    if c >= level { mask |= 4u; }
    if d >= level { mask |= 8u; }

    switch mask {
        case 1u, 14u: { append_edges(3, 0, cell, a, b, c, d, level); }
        case 2u, 13u: { append_edges(0, 1, cell, a, b, c, d, level); }
        case 3u, 12u: { append_edges(3, 1, cell, a, b, c, d, level); }
        case 4u, 11u: { append_edges(1, 2, cell, a, b, c, d, level); }
        case 6u, 9u: { append_edges(0, 2, cell, a, b, c, d, level); }
        case 7u, 8u: { append_edges(3, 2, cell, a, b, c, d, level); }
        case 5u, 10u: {
            // Two opposite corners: the middle decides which of them are connected.
            let center_is_high = (a + b + c + d) / 4.0 >= level;
            if (mask == 5u) == center_is_high {
                append_edges(0, 1, cell, a, b, c, d, level);
                append_edges(2, 3, cell, a, b, c, d, level);
            } else {
                append_edges(3, 0, cell, a, b, c, d, level);
                append_edges(1, 2, cell, a, b, c, d, level);
            }
        }
        default: { }
    }
}

@compute @workgroup_size(16, 16)
fn main(@builtin(global_invocation_id) id: vec3u) {
    let size = textureDimensions(values);
    if id.x + 1u >= size.x || id.y + 1u >= size.y {
        return;
    }

    let position = vec2i(id.xy);
    let a = textureLoad(values, position, 0).r;
    let b = textureLoad(values, position + vec2i(1, 0), 0).r;
    let c = textureLoad(values, position + vec2i(1, 1), 0).r;
    let d = textureLoad(values, position + vec2i(0, 1), 0).r;
    if is_nan(a) || is_nan(b) || is_nan(c) || is_nan(d) {
        return;
    }

    for (var i = 0u; i < params.level_count; i++) {
        march(vec2f(id.xy), a, b, c, d, levels[i]);
    }
}
