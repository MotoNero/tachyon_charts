// The planes that cut the scene, one per column. A point lies on the far side of a plane when its row turns negative.
fn is_cut(place: vec3<f32>, cuts: mat4x4<f32>) -> bool {
    return any((vec4(place, 1.0) * cuts) < vec4(0.0));
}

