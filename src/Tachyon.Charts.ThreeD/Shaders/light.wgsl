// Light for all surfaces. Colors stay exact where the light falls straight on, so they can be read against a color bar.
const shade = 0.62;

// The material holds gloss, sharpness, opacity, and 1 if light falls on the surface.
fn light_surface(color: vec3<f32>, place: vec3<f32>, surface_normal: vec3<f32>, eye: vec3<f32>, material: vec4<f32>) -> vec3<f32> {
    if (material.w == 0.0) {
        return color;
    }

    let view = normalize(eye - place);
    let light = normalize(view + vec3(0.25, 0.25, 0.6));
    // Both sides of a surface show, so the normal always faces the eye.
    let normal = normalize(surface_normal) * sign(dot(surface_normal, view) + 1e-6);
    let diffuse = shade + ((1.0 - shade) * max(dot(normal, light), 0.0));
    let halfway = normalize(light + view);
    let highlight = material.x * pow(max(dot(normal, halfway), 0.0), material.y);
    return (color * diffuse) + vec3(highlight);
}

struct Weighted {
    @location(0) accumulated: vec4<f32>,
    @location(1) revealed: f32,
}

// Weighted blended order-independent transparency: surfaces that cut through each other need no sorting.
fn weigh(color: vec3<f32>, alpha: f32, depth: f32) -> Weighted {
    let weight = clamp(pow(min(1.0, alpha * 10.0) + 0.01, 3.0) * 1e8 * pow(1.0 - (depth * 0.9), 3.0), 1e-2, 3e3);
    var output: Weighted;
    output.accumulated = vec4(color * alpha, alpha) * weight;
    output.revealed = alpha;
    return output;
}
