// Bubbles as true spheres. One square per bubble faces the eye, and each pixel follows its ray to the sphere: the
// sphere needs no triangles, and its depth is exact where it meets other objects. The functions of clip.wgsl and
// light.wgsl stand in front of this file.
struct Uniforms {
    camera: mat4x4<f32>,
    eye: vec4<f32>,
    material: vec4<f32>,
    cuts: mat4x4<f32>,
    size: vec4<f32>,
}

@group(0) @binding(0) var<uniform> uniforms: Uniforms;

struct Output {
    @builtin(position) position: vec4<f32>,
    @location(0) place: vec3<f32>,
    @location(1) @interpolate(flat) center: vec3<f32>,
    @location(2) @interpolate(flat) radius: f32,
    @location(3) @interpolate(flat) color: vec3<f32>,
}

struct Fragment {
    @location(0) color: vec4<f32>,
    @builtin(frag_depth) depth: f32,
}

// In the parallel projection w never changes and all rays run along the depth row of the matrix.
fn is_parallel() -> bool {
    let c = uniforms.camera;
    return c[0][3] == 0.0 && c[1][3] == 0.0 && c[2][3] == 0.0;
}

fn depth_row() -> vec3<f32> {
    let c = uniforms.camera;
    return normalize(vec3(c[0][2], c[1][2], c[2][2]));
}

@vertex
fn vs_main(@builtin(vertex_index) index: u32, @location(0) center: vec3<f32>, @location(1) size: f32, @location(2) color: vec4<f32>) -> Output {
    var output: Output;
    output.position = vec4(2.0, 2.0, 2.0, 1.0);
    if any(abs(center) > vec3(1.0001)) {
        return output;
    }

    // A negative size is a diameter in pixels: the row of the matrix for the height holds the scale of the view.
    var radius = size;
    if size < 0.0 {
        let c = uniforms.camera;
        let w = (c * vec4(center, 1.0)).w;
        radius = -size * w / (uniforms.size.y * length(vec3(c[0][1], c[1][1], c[2][1])));
    }

    var toward = -depth_row();
    var widen = 1.0;
    if !is_parallel() {
        let way = uniforms.eye.xyz - center;
        let distance = length(way);
        if distance <= radius * 1.001 {
            return output;
        }

        // Seen from near, a sphere covers more than its radius on the plane through its middle.
        toward = way / distance;
        widen = distance / sqrt((distance * distance) - (radius * radius));
    }

    var up = vec3(0.0, 0.0, 1.0);
    if abs(toward.z) > 0.99 {
        up = vec3(0.0, 1.0, 0.0);
    }

    let right = normalize(cross(up, toward));
    let top = cross(toward, right);
    var corners = array<vec2<f32>, 6>(vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(1.0, 1.0), vec2(-1.0, -1.0), vec2(1.0, 1.0), vec2(-1.0, 1.0));
    let corner = corners[index];

    // A little more than the outline leaves room for the soft edge.
    let place = center + (((right * corner.x) + (top * corner.y)) * radius * widen * 1.04);
    output.position = uniforms.camera * vec4(place, 1.0);
    output.place = place;
    output.center = center;
    output.radius = radius;
    output.color = color.rgb;
    return output;
}

@fragment
fn fs_main(input: Output) -> Fragment {
    var direction = depth_row();
    var start = input.place - (direction * 8.0);
    if !is_parallel() {
        start = uniforms.eye.xyz;
        direction = normalize(input.place - start);
    }

    let offset = start - input.center;
    let along = dot(offset, direction);
    let miss = sqrt(max(dot(offset, offset) - (along * along), 0.0));
    let coverage = clamp(((input.radius - miss) / max(fwidth(miss), 1e-7)) + 0.5, 0.0, 1.0);
    if coverage <= 0.0 {
        discard;
    }

    let inside = sqrt(max((input.radius * input.radius) - (miss * miss), 0.0));
    let hit = start + (direction * (-along - inside));
    if is_cut(hit, uniforms.cuts) || any(abs(hit) > vec3(1.0001)) {
        discard;
    }

    let normal = (hit - input.center) / input.radius;
    let clip = uniforms.camera * vec4(hit, 1.0);
    var output: Fragment;
    output.color = vec4(light_surface(input.color, hit, normal, uniforms.eye.xyz, uniforms.material), coverage);
    output.depth = clip.z / clip.w;
    return output;
}
