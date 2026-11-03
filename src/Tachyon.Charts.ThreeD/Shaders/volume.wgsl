// Volumes. The functions of clip.wgsl stand in front of this file. A volume lies in a 3D texture of shares from 0 to 1.
struct Uniforms {
    camera: mat4x4<f32>,
    cuts: mat4x4<f32>,
    // The corners of the volume in the box.
    low: vec4<f32>,
    high: vec4<f32>,
    // The places of the three slices in the box, w holds the threshold below which a slice stays clear.
    slices: vec4<f32>,
    // The length of a step in the box, the reach for the slope, and the power that keeps opacity per voxel.
    march: vec4<f32>,
    // How the rays look: 0 through the transfer function, 1 for the highest value along the ray, 2 for the surface where
    // the share crosses look.y.
    look: vec4<f32>,
    eye: vec4<f32>,
    material: vec4<f32>,
    // Where the three corners of the big triangle over the picture lie in the box, at both ends of the depth.
    near_corners: array<vec4<f32>, 3>,
    far_corners: array<vec4<f32>, 3>,
}

@group(0) @binding(0) var<uniform> uniforms: Uniforms;
@group(0) @binding(1) var values: texture_3d<f32>;
@group(0) @binding(2) var linear: sampler;
@group(0) @binding(3) var<storage, read> colormap: array<vec4<f32>>;

struct SliceOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) place: vec3<f32>,
}

@vertex
fn vs_slice(@builtin(vertex_index) index: u32, @builtin(instance_index) axis: u32) -> SliceOutput {
    var corners = array<vec2<f32>, 6>(vec2(0.0, 0.0), vec2(1.0, 0.0), vec2(1.0, 1.0), vec2(0.0, 0.0), vec2(1.0, 1.0), vec2(0.0, 1.0));
    let corner = corners[index];
    var place = mix(uniforms.low.xyz, uniforms.high.xyz, vec3(corner, 0.0));
    place.z = uniforms.slices.z;
    if axis == 0u {
        place = vec3(uniforms.slices.x, mix(uniforms.low.yz, uniforms.high.yz, corner));
    } else if axis == 1u {
        let across = mix(uniforms.low.xz, uniforms.high.xz, corner);
        place = vec3(across.x, uniforms.slices.y, across.y);
    }

    var output: SliceOutput;
    output.position = uniforms.camera * vec4(place, 1.0);
    output.place = place;
    return output;
}

fn color_of(share: f32) -> vec3<f32> {
    let last = f32(arrayLength(&colormap) - 1u);
    let place = clamp(share, 0.0, 1.0) * last;
    let lower = u32(floor(place));
    let upper = min(lower + 1u, u32(last));
    return mix(colormap[lower].rgb, colormap[upper].rgb, fract(place));
}

@fragment
fn fs_slice(input: SliceOutput) -> @location(0) vec4<f32> {
    let share = textureSampleLevel(values, linear, (input.place - uniforms.low.xyz) / (uniforms.high.xyz - uniforms.low.xyz), 0.0).r;
    if share < uniforms.slices.w || is_cut(input.place, uniforms.cuts) || any(abs(input.place) > vec3(1.0001)) {
        discard;
    }

    return vec4(color_of(share), 1.0);
}

// Looking through the volume: a ray per pixel, in a pass of its own after everything opaque. The depth of the scene ends
// the ray, so that objects inside the volume stay in their place.
@group(0) @binding(4) var<storage, read> transfer: array<vec4<f32>>;
@group(0) @binding(5) var scene_depth: texture_depth_multisampled_2d;

struct MarchOutput {
    @builtin(position) position: vec4<f32>,
    @location(0) near_place: vec3<f32>,
    @location(1) far_place: vec3<f32>,
}

@vertex
fn vs_march(@builtin(vertex_index) index: u32) -> MarchOutput {
    let corner = vec2(f32((index << 1u) & 2u), f32(index & 2u));
    var output: MarchOutput;
    output.position = vec4((corner * 2.0) - 1.0, 0.0, 1.0);
    output.near_place = uniforms.near_corners[index].xyz;
    output.far_place = uniforms.far_corners[index].xyz;
    return output;
}

fn limit(start: f32, direction: f32, low: f32, high: f32, range: vec2<f32>) -> vec2<f32> {
    if abs(direction) < 1e-7 {
        if start < low || start > high {
            return vec2(1.0, 0.0);
        }

        return range;
    }

    let a = (low - start) / direction;
    let b = (high - start) / direction;
    return vec2(max(range.x, min(a, b)), min(range.y, max(a, b)));
}

fn limit_to_plane(start: vec3<f32>, direction: vec3<f32>, plane: vec4<f32>, range: vec2<f32>) -> vec2<f32> {
    let height = dot(plane.xyz, start) + plane.w;
    let climb = dot(plane.xyz, direction);
    if abs(climb) < 1e-7 {
        if height < 0.0 {
            return vec2(1.0, 0.0);
        }

        return range;
    }

    let crossing = -height / climb;
    if climb > 0.0 {
        return vec2(max(range.x, crossing), range.y);
    }

    return vec2(range.x, min(range.y, crossing));
}

// The way along the ray to a depth of the scene. Depth changes as a fraction of two straight lines, which solves at once.
fn distance_to_depth(start: vec3<f32>, direction: vec3<f32>, depth: f32) -> f32 {
    let c = uniforms.camera;
    let z_row = vec4(c[0][2], c[1][2], c[2][2], c[3][2]);
    let w_row = vec4(c[0][3], c[1][3], c[2][3], c[3][3]);
    let z_start = dot(z_row, vec4(start, 1.0));
    let z_climb = dot(z_row, vec4(direction, 0.0));
    let w_start = dot(w_row, vec4(start, 1.0));
    let w_climb = dot(w_row, vec4(direction, 0.0));
    return (z_start - (depth * w_start)) / ((depth * w_climb) - z_climb);
}

fn share_at(place: vec3<f32>, size: vec3<f32>) -> f32 {
    return textureSampleLevel(values, linear, (place - uniforms.low.xyz) / size, 0.0).r;
}

fn shade_voxel(color: vec3<f32>, place: vec3<f32>, share: f32, size: vec3<f32>, direction: vec3<f32>) -> vec3<f32> {
    let reach = uniforms.march.y;
    let slope = vec3(
        share_at(place + vec3(reach, 0.0, 0.0), size),
        share_at(place + vec3(0.0, reach, 0.0), size),
        share_at(place + vec3(0.0, 0.0, reach), size)) - vec3(share);
    let strength = length(slope);
    if strength < 1e-4 {
        return color * 0.8;
    }

    let normal = slope / strength;
    let facing = abs(dot(normal, direction));
    return (color * (0.45 + (0.55 * facing))) + vec3(0.25 * pow(facing, 24.0));
}

// The highest value along the ray, colored through the colormap. Low values stay see-through, so the box does not hide the
// scene behind it.
fn march_maximum(start: vec3<f32>, direction: vec3<f32>, range: vec2<f32>, shift: f32, size: vec3<f32>) -> vec4<f32> {
    let step_length = uniforms.march.x;
    var best = 0.0;
    var along = range.x + (shift * step_length);
    while along < range.y && best < 0.999 {
        best = max(best, share_at(start + (direction * along), size));
        along += step_length;
    }

    if best <= uniforms.slices.w {
        discard;
    }

    return vec4(color_of(best) * best, best);
}

// The first place where the values cross the threshold. Trilinear values run without jumps from cell to cell, and the
// steps stay shorter than a cell, so the surface has no holes at the cell borders. Halving the last step finds it exactly.
fn march_surface(start: vec3<f32>, direction: vec3<f32>, range: vec2<f32>, shift: f32, size: vec3<f32>) -> vec4<f32> {
    let step_length = uniforms.march.x;
    let level = uniforms.look.y;
    var along = range.x + (shift * step_length);
    var before = share_at(start + (direction * along), size) - level;
    var found = -1.0;
    while along < range.y {
        let next = min(along + step_length, range.y);
        let after = share_at(start + (direction * next), size) - level;
        if (before <= 0.0) != (after <= 0.0) {
            var low = along;
            var high = next;
            var low_side = before;
            for (var i = 0; i < 6; i++) {
                let middle = (low + high) * 0.5;
                let value = share_at(start + (direction * middle), size) - level;
                if (low_side <= 0.0) == (value <= 0.0) {
                    low = middle;
                    low_side = value;
                } else {
                    high = middle;
                }
            }

            found = (low + high) * 0.5;
            break;
        }

        before = after;
        along = next;
    }

    if found < 0.0 {
        discard;
    }

    let place = start + (direction * found);
    let reach = uniforms.march.y;
    let slope = vec3(
        share_at(place + vec3(reach, 0.0, 0.0), size) - share_at(place - vec3(reach, 0.0, 0.0), size),
        share_at(place + vec3(0.0, reach, 0.0), size) - share_at(place - vec3(0.0, reach, 0.0), size),
        share_at(place + vec3(0.0, 0.0, reach), size) - share_at(place - vec3(0.0, 0.0, reach), size));
    let normal = select(-direction, slope / max(length(slope), 1e-6), length(slope) > 1e-6);
    return vec4(light_surface(color_of(level), place, normal, uniforms.eye.xyz, uniforms.material), 1.0);
}

@fragment
fn fs_march(input: MarchOutput) -> @location(0) vec4<f32> {
    let span = input.far_place - input.near_place;
    let longest = length(span);
    let direction = span / longest;
    let start = input.near_place;
    var range = vec2(0.0, longest);
    range = limit(start.x, direction.x, max(uniforms.low.x, -1.0), min(uniforms.high.x, 1.0), range);
    range = limit(start.y, direction.y, max(uniforms.low.y, -1.0), min(uniforms.high.y, 1.0), range);
    range = limit(start.z, direction.z, max(uniforms.low.z, -1.0), min(uniforms.high.z, 1.0), range);
    range = limit_to_plane(start, direction, uniforms.cuts[0], range);
    range = limit_to_plane(start, direction, uniforms.cuts[1], range);
    range = limit_to_plane(start, direction, uniforms.cuts[2], range);
    range = limit_to_plane(start, direction, uniforms.cuts[3], range);
    let depth = textureLoad(scene_depth, vec2<i32>(input.position.xy), 0);
    if depth < 1.0 {
        range.y = min(range.y, distance_to_depth(start, direction, depth));
    }

    if range.x >= range.y {
        discard;
    }

    // A small shift per pixel turns the rings of the steps into fine noise, which the eye forgives.
    let shift = fract(52.9829189 * fract(dot(input.position.xy, vec2(0.06711056, 0.00583715))));
    let size = uniforms.high.xyz - uniforms.low.xyz;
    let step_length = uniforms.march.x;
    if uniforms.look.x == 1.0 {
        return march_maximum(start, direction, range, shift, size);
    }

    if uniforms.look.x == 2.0 {
        return march_surface(start, direction, range, shift, size);
    }

    let exponent = uniforms.march.z;
    var sum = vec4(0.0);
    var along = range.x + (shift * step_length);
    while along < range.y && sum.a < 0.98 {
        let place = start + (direction * along);
        let share = share_at(place, size);
        let entry = transfer[u32((clamp(share, 0.0, 1.0) * 255.0) + 0.5)];
        if entry.a > 0.0 {
            // The opacity counts per voxel, so it must not depend on the length of the steps.
            let alpha = 1.0 - pow(1.0 - min(entry.a, 0.999), exponent);
            let color = shade_voxel(entry.rgb, place, share, size, direction);
            sum += vec4(color * alpha, alpha) * (1.0 - sum.a);
        }

        along += step_length;
    }

    if sum.a <= 0.0 {
        discard;
    }

    return sum;
}
