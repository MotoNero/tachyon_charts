// Counts points per pixel. bin_points runs once per block of points, resolve turns the counts into a
// texture of values and finds the highest one.

struct Params {
    scale: vec2f,
    offset: vec2f,
    size: vec2u,
    count: u32,
}

@group(0) @binding(0) var<uniform> params: Params;
@group(0) @binding(1) var<storage, read> points: array<vec2f>;
@group(0) @binding(2) var<storage, read_write> bins: array<atomic<u32>>;
@group(0) @binding(3) var target_texture: texture_storage_2d<r32float, write>;
@group(0) @binding(4) var<storage, read_write> maximum: atomic<u32>;

// A dimension of a dispatch is limited to 65535 groups, so large clouds run in rows of groups.
const groups_per_row = 1024u;
const points_per_group = 256u;

@compute @workgroup_size(256)
fn bin_points(@builtin(global_invocation_id) id: vec3u) {
    let index = id.y * groups_per_row * points_per_group + id.x;
    if index >= params.count {
        return;
    }

    let pixel = points[index] * params.scale + params.offset;
    if pixel.x >= 0.0 && pixel.y >= 0.0 && pixel.x < f32(params.size.x) && pixel.y < f32(params.size.y) {
        atomicAdd(&bins[u32(pixel.y) * params.size.x + u32(pixel.x)], 1u);
    }
}

@compute @workgroup_size(16, 16)
fn resolve(@builtin(global_invocation_id) id: vec3u) {
    if id.x >= params.size.x || id.y >= params.size.y {
        return;
    }

    let count = atomicLoad(&bins[id.y * params.size.x + id.x]);
    atomicMax(&maximum, count);
    textureStore(target_texture, vec2i(id.xy), vec4f(f32(count), 0.0, 0.0, 1.0));
}
