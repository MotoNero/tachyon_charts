// Columns as instances of a box. The functions of light.wgsl stand in front of this file.
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

// Each face points from the middle along its normal, with two axes that span it.
fn face_normal(face: u32) -> vec3<f32> {
    var normals = array<vec3<f32>, 6>(vec3(1.0, 0.0, 0.0), vec3(-1.0, 0.0, 0.0), vec3(0.0, 1.0, 0.0), vec3(0.0, -1.0, 0.0), vec3(0.0, 0.0, 1.0), vec3(0.0, 0.0, -1.0));
    return normals[face];
}

fn corner(index: u32) -> vec3<f32> {
    var spans = array<vec2<f32>, 6>(vec2(-1.0, -1.0), vec2(1.0, -1.0), vec2(1.0, 1.0), vec2(-1.0, -1.0), vec2(1.0, 1.0), vec2(-1.0, 1.0));
    let normal = face_normal(index / 6u);
    let span = spans[index % 6u];
    if abs(normal.x) > 0.5 {
        return vec3(normal.x, span.x, span.y);
    }

    if abs(normal.y) > 0.5 {
        return vec3(span.x, normal.y, span.y);
    }

    return vec3(span.x, span.y, normal.z);
}

// The way to the eye. In the parallel projection w never changes and all rays run along the depth of the matrix.
fn eye_side(place: vec3<f32>) -> vec3<f32> {
    let c = uniforms.camera;
    if c[0][3] == 0.0 && c[1][3] == 0.0 && c[2][3] == 0.0 {
        return -vec3(c[0][2], c[1][2], c[2][2]);
    }

    return uniforms.eye.xyz - place;
}

@vertex
fn vs_main(@builtin(vertex_index) index: u32, @location(0) bar: vec4<f32>, @location(1) ends: vec2<f32>, @location(2) color: vec4<f32>) -> Output {
    var output: Output;
    if ends.y <= ends.x {
        output.position = vec4(2.0, 2.0, 2.0, 1.0);
        return output;
    }

    let c = corner(index);
    let place = vec3(bar.xy + (c.xy * bar.zw), mix(ends.x, ends.y, (c.z + 1.0) * 0.5));

    // Faces that turn away cost a lot at a million columns, so they end here. Transparent columns keep them, and a
    // cut opens the view on them.
    let normal = face_normal(index / 6u);
    let is_closed = uniforms.material.z >= 1.0 && uniforms.cuts[0].w == 1.0;
    if is_closed && dot(normal, eye_side(place)) <= 0.0 {
        output.position = vec4(2.0, 2.0, 2.0, 1.0);
        return output;
    }

    output.position = uniforms.camera * vec4(place, 1.0);
    output.place = place;
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
    return weigh(color, uniforms.material.z, input.position.z);
}
