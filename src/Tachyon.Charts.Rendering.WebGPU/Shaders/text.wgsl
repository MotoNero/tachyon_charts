// Instanced glyph quads, sampled from a multi-channel signed distance field. The median of the
// channels restores sharp corners that a single distance channel would round off.

struct Params {
    target_size: vec2f,
    width: f32,
    color: vec4f,
}

@group(0) @binding(0) var<uniform> params: Params;
@group(1) @binding(0) var atlas: texture_2d<f32>;

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) uv: vec2f,
    @location(1) color: vec4f,
    @location(2) @interpolate(flat) distance_scale: f32,
}

@vertex
fn vs_main(
    @builtin(vertex_index) vertex_index: u32,
    @location(0) glyph_position: vec2f,
    @location(1) glyph_size: vec2f,
    @location(2) uv_min: vec2f,
    @location(3) uv_max: vec2f,
    @location(4) color: vec4f,
    @location(5) distance_scale: f32,
    @location(6) rotation: f32,
    @location(7) pivot: vec2f,
) -> VertexOutput {
    var corners = array<vec2f, 6>(
        vec2f(0.0, 0.0), vec2f(1.0, 0.0), vec2f(1.0, 1.0),
        vec2f(0.0, 0.0), vec2f(1.0, 1.0), vec2f(0.0, 1.0),
    );

    let corner = corners[vertex_index];
    let offset = glyph_position + glyph_size * corner - pivot;
    let turn = vec2f(cos(rotation), sin(rotation));
    let position = pivot + vec2f(offset.x * turn.x - offset.y * turn.y, offset.x * turn.y + offset.y * turn.x);
    let ndc = position / params.target_size * 2.0 - 1.0;

    var output: VertexOutput;
    output.position = vec4f(ndc.x, -ndc.y, 0.0, 1.0);
    output.uv = uv_min + (uv_max - uv_min) * corner;
    output.color = color;
    output.distance_scale = distance_scale;
    return output;
}

fn median(a: f32, b: f32, c: f32) -> f32 {
    return max(min(a, b), min(max(a, b), c));
}

// Bilinear in the shader: hardware filters differ in precision and the field amplifies every difference.
fn sample_atlas(uv: vec2f) -> vec3f {
    let size = vec2i(textureDimensions(atlas));
    let position = uv * vec2f(size) - 0.5;
    let base = clamp(vec2i(floor(position)), vec2i(0), size - 1);
    let next = min(base + 1, size - 1);
    let weight = clamp(position - vec2f(base), vec2f(0.0), vec2f(1.0));

    let upper = mix(
        textureLoad(atlas, base, 0).rgb,
        textureLoad(atlas, vec2i(next.x, base.y), 0).rgb,
        weight.x);
    let lower = mix(
        textureLoad(atlas, vec2i(base.x, next.y), 0).rgb,
        textureLoad(atlas, next, 0).rgb,
        weight.x);
    return mix(upper, lower, weight.y);
}

// Small text gets slightly stronger strokes: a stroke of one pixel or less that falls between two pixels would
// otherwise show as two faint columns. The em size in pixels is the distance scale times 40 / (2 * 4), the em size
// and range of the field. The same rule is in the glyph rasterizer of the software renderer.
fn embolden(distance_scale: f32) -> f32 {
    let em_pixels = distance_scale * 5.0;
    return clamp((16.0 - em_pixels) / 8.0, 0.0, 1.0) * 0.15;
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4f {
    let distance = sample_atlas(input.uv);
    let signed_distance = median(distance.r, distance.g, distance.b) - 0.5;
    let coverage = clamp(signed_distance * input.distance_scale + 0.5 + embolden(input.distance_scale), 0.0, 1.0);
    return vec4f(input.color.rgb, input.color.a * coverage);
}
