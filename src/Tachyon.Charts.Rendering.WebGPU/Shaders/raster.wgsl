// A texture of values on a rectangle, colored by a table. Changing the colors only needs another table.
// textureLoad picks the nearest texel, like the Skia backend, so that both show the same pixels.

struct Params {
    target_size: vec2f,
    is_interpolated: f32,
    // x: 0 nearest, 1 mean, 2 maximum.
    reduction: vec4f,
    scale: vec2f,
    offset: vec2f,
    is_log: f32,
    row_shift: u32,
    value_min: f32,
    value_max: f32,
    section_origin: vec2f,
    section_size: vec2f,
    missing_color: vec4f,
    // A negative alpha stands for the end of the table.
    below_color: vec4f,
    above_color: vec4f,
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
@group(1) @binding(0) var values: texture_2d<f32>;
@group(2) @binding(0) var colormap: texture_2d<f32>;

struct VertexOutput {
    @builtin(position) position: vec4f,
    @location(0) section: vec2f,
}

@vertex
fn vs_main(@builtin(vertex_index) vertex_index: u32) -> VertexOutput {
    var corners = array<vec2f, 6>(
        vec2f(0.0, 0.0), vec2f(1.0, 0.0), vec2f(1.0, 1.0),
        vec2f(0.0, 0.0), vec2f(1.0, 1.0), vec2f(0.0, 1.0),
    );

    let corner = corners[vertex_index];
    let position = params.offset + params.scale * corner;
    let ndc = turned(position) / params.target_size * 2.0 - 1.0;

    var output: VertexOutput;
    output.position = vec4f(ndc.x, -ndc.y, 0.0, 1.0);
    output.section = params.section_origin + params.section_size * corner;
    return output;
}

fn table_color(position: f32) -> vec4f {
    let last = i32(textureDimensions(colormap).x) - 1;
    let index = clamp(i32(floor(position * f32(last) + 0.5)), 0, last);
    return textureLoad(colormap, vec2i(index, 0), 0);
}

fn load_value(position: vec2i, size: vec2i) -> f32 {
    var texel = clamp(position, vec2i(0), size - 1);
    texel.y = i32((u32(texel.y) + params.row_shift) % u32(size.y));
    return textureLoad(values, texel, 0).r;
}

// A comparison could be optimized away, because shaders may assume that there is no NaN.
fn is_nan(value: f32) -> bool {
    return (bitcast<u32>(value) & 0x7fffffffu) > 0x7f800000u;
}

// Reads up to this many texels per direction, spread over the pixel.
const max_samples = 8;

fn reduce_value(section: vec2f, size: vec2i) -> f32 {
    let footprint = abs(params.section_size) * vec2f(size) / params.scale;
    let samples = clamp(vec2i(ceil(footprint)), vec2i(1), vec2i(max_samples));
    let start = section * vec2f(size) - footprint * 0.5;
    let spacing = footprint / vec2f(samples);
    var sum = 0.0;
    var highest = 0.0;
    var found = 0;
    for (var y = 0; y < samples.y; y++) {
        for (var x = 0; x < samples.x; x++) {
            let position = start + (vec2f(f32(x), f32(y)) + 0.5) * spacing;
            let value = load_value(vec2i(floor(position)), size);
            if !is_nan(value) {
                highest = select(max(highest, value), value, found == 0);
                sum += value;
                found += 1;
            }
        }
    }

    if found == 0 {
        return bitcast<f32>(0x7fc00000u);
    }

    return select(sum / f32(found), highest, params.reduction.x > 1.5);
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4f {
    let size = vec2i(textureDimensions(values));
    var value = 0.0;
    if params.is_interpolated > 0.5 {
        let position = input.section * vec2f(size) - 0.5;
        let cell = vec2i(floor(position));
        let part = position - floor(position);
        let a = load_value(cell, size);
        let b = load_value(cell + vec2i(1, 0), size);
        let c = load_value(cell + vec2i(0, 1), size);
        let d = load_value(cell + vec2i(1, 1), size);
        if is_nan(a) || is_nan(b) || is_nan(c) || is_nan(d) {
            return params.missing_color;
        }

        value = mix(mix(a, b, part.x), mix(c, d, part.x), part.y);
    } else {
        value = select(load_value(vec2i(floor(input.section * vec2f(size))), size), reduce_value(input.section, size), params.reduction.x > 0.5);
        if is_nan(value) {
            return params.missing_color;
        }
    }

    if value < params.value_min || (params.is_log > 0.5 && value <= 0.0) {
        return select(table_color(0.0), params.below_color, params.below_color.a >= 0.0);
    }

    if value > params.value_max {
        return select(table_color(1.0), params.above_color, params.above_color.a >= 0.0);
    }

    var position = (value - params.value_min) / (params.value_max - params.value_min);
    if params.is_log > 0.5 {
        position = log(value / params.value_min) / log(params.value_max / params.value_min);
    }

    return table_color(position);
}
