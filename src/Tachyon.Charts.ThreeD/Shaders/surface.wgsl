// A height field. The place follows from the vertex index, the height from a storage buffer, so that a raster of
// 4096 x 4096 needs 64 MB instead of half a gigabyte. The functions of light.wgsl stand in front of this file.
struct Uniforms {
    camera: mat4x4<f32>,
    eye: vec4<f32>,
    material: vec4<f32>,
    // Columns, rows, and the first row of a ring: a waterfall moves it instead of its data.
    grid: vec4<u32>,
    // Left and front edge in the box, then the step per column and row.
    origin: vec4<f32>,
    // The heights in the box that match the ends of the colormap.
    colors: vec4<f32>,
    // Number of height lines, their width in pixels, the distance of the cuts on the walls.
    contour: vec4<f32>,
    contour_color: vec4<f32>,
    // The planes of the two walls and the floor in the box.
    planes: vec4<f32>,
    cuts: mat4x4<f32>,
}

const missing_height = 1e20;

@group(0) @binding(0) var<uniform> uniforms: Uniforms;
@group(0) @binding(1) var<storage, read> heights: array<f32>;
@group(0) @binding(2) var<storage, read> colormap: array<vec4<f32>>;
@group(0) @binding(3) var<storage, read> levels: array<f32>;

struct Output {
    @builtin(position) position: vec4<f32>,
    @location(0) place: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) gap: f32,
}

fn height_at(column: u32, row: u32) -> f32 {
    let stored_row = (row + uniforms.grid.z) % uniforms.grid.y;
    return heights[(stored_row * uniforms.grid.x) + column];
}

fn neighbor_or(column: u32, row: u32, fallback: f32) -> f32 {
    let height = height_at(column, row);
    return select(height, fallback, height > missing_height);
}

@vertex
fn vs_main(@builtin(vertex_index) index: u32) -> Output {
    let column = index % uniforms.grid.x;
    let row = index / uniforms.grid.x;
    let height = height_at(column, row);
    let is_gap = height > missing_height;
    let z = select(height, 0.0, is_gap);
    let last_column = uniforms.grid.x - 1u;
    let last_row = uniforms.grid.y - 1u;
    let left = neighbor_or(select(column - 1u, 0u, column == 0u), row, z);
    let right = neighbor_or(min(column + 1u, last_column), row, z);
    let front = neighbor_or(column, select(row - 1u, 0u, row == 0u), z);
    let back = neighbor_or(column, min(row + 1u, last_row), z);
    var output: Output;
    output.place = vec3(uniforms.origin.x + (f32(column) * uniforms.origin.z), uniforms.origin.y + (f32(row) * uniforms.origin.w), z);
    output.position = uniforms.camera * vec4(output.place, 1.0);
    output.normal = vec3((left - right) / (2.0 * uniforms.origin.z), (front - back) / (2.0 * uniforms.origin.w), 1.0);
    output.gap = select(0.0, 1.0, is_gap);
    return output;
}

fn color_at(z: f32) -> vec3<f32> {
    let last = f32(arrayLength(&colormap) - 1u);
    let place = clamp((z - uniforms.colors.x) / (uniforms.colors.y - uniforms.colors.x), 0.0, 1.0) * last;
    let lower = u32(floor(place));
    let upper = min(lower + 1u, u32(last));
    return mix(colormap[lower].rgb, colormap[upper].rgb, fract(place));
}

// How much a pixel lies on a line. The distance in pixels keeps the width even and stops the lines from flickering.
fn line_share(distance: f32, change: f32) -> f32 {
    let pixels = distance / max(change, 1e-6);
    return 1.0 - smoothstep(uniforms.contour.y - 0.75, uniforms.contour.y + 0.25, pixels);
}

fn level_share(z: f32) -> f32 {
    var nearest = 1e9;
    for (var i = 0u; i < u32(uniforms.contour.x); i++) {
        nearest = min(nearest, abs(z - levels[i]));
    }

    return line_share(nearest, fwidth(z));
}

fn shaded(input: Output) -> vec3<f32> {
    let lit = light_surface(color_at(input.place.z), input.place, input.normal, uniforms.eye.xyz, uniforms.material);
    if (uniforms.contour.x == 0.0) {
        return lit;
    }

    return mix(lit, uniforms.contour_color.rgb, level_share(input.place.z) * uniforms.contour_color.a);
}

struct Projected {
    @builtin(position) position: vec4<f32>,
    // The height on the floor, a place along the wall on the walls.
    @location(0) value: f32,
    @location(1) gap: f32,
    @location(2) @interpolate(flat) plane: u32,
}

// The surface pressed flat: instance 0 onto the floor, 1 and 2 onto the walls.
@vertex
fn vs_projected(@builtin(vertex_index) index: u32, @builtin(instance_index) plane: u32) -> Projected {
    let column = index % uniforms.grid.x;
    let row = index / uniforms.grid.x;
    let height = height_at(column, row);
    let is_gap = height > missing_height;
    let x = uniforms.origin.x + (f32(column) * uniforms.origin.z);
    let y = uniforms.origin.y + (f32(row) * uniforms.origin.w);
    let z = select(height, 0.0, is_gap);
    var place = vec3(x, y, uniforms.planes.z);
    var value = z;
    if (plane == 1u) {
        place = vec3(uniforms.planes.x, y, z);
        value = x;
    } else if (plane == 2u) {
        place = vec3(x, uniforms.planes.y, z);
        value = y;
    }

    var output: Projected;
    output.position = uniforms.camera * vec4(place, 1.0);
    output.value = value;
    output.gap = select(0.0, 1.0, is_gap);
    output.plane = plane;
    return output;
}

@fragment
fn fs_projected(input: Projected) -> @location(0) vec4<f32> {
    if (input.gap > 0.0) {
        discard;
    }

    var share = 0.0;
    if (input.plane == 0u) {
        share = level_share(input.value);
    } else {
        // Cuts through the surface at even distances, seen as curves on the wall.
        let distance = uniforms.contour.z;
        let place = input.value / distance;
        share = line_share(abs(fract(place + 0.5) - 0.5) * distance, fwidth(input.value));
    }

    if (share < 0.01) {
        discard;
    }

    return vec4(uniforms.contour_color.rgb, share * uniforms.contour_color.a * 0.8);
}

@fragment
fn fs_opaque(input: Output) -> @location(0) vec4<f32> {
    if (input.gap > 0.0 || is_cut(input.place, uniforms.cuts) || any(abs(input.place.xy) > vec2(1.0001))) {
        discard;
    }

    return vec4(shaded(input), 1.0);
}

@fragment
fn fs_transparent(input: Output) -> Weighted {
    if (input.gap > 0.0 || is_cut(input.place, uniforms.cuts) || any(abs(input.place.xy) > vec2(1.0001))) {
        discard;
    }

    return weigh(shaded(input), uniforms.material.z, input.position.z);
}
