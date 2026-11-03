// Lays the transparent surfaces over the picture.
@group(0) @binding(0) var accumulated: texture_2d<f32>;
@group(0) @binding(1) var revealed: texture_2d<f32>;

@vertex
fn vs_main(@builtin(vertex_index) index: u32) -> @builtin(position) vec4<f32> {
    let corner = vec2(f32((index << 1u) & 2u), f32(index & 2u));
    return vec4((corner * 2.0) - 1.0, 0.0, 1.0);
}

@fragment
fn fs_main(@builtin(position) position: vec4<f32>) -> @location(0) vec4<f32> {
    let pixel = vec2<i32>(position.xy);
    let sum = textureLoad(accumulated, pixel, 0);
    let alpha = 1.0 - textureLoad(revealed, pixel, 0).r;
    if (alpha < 0.001) {
        discard;
    }

    return vec4(sum.rgb / max(sum.a, 1e-5), alpha);
}
