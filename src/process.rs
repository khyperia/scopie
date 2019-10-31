use khygl::texture::CpuTexture;

// TODO: use f64::clamp() once stable
#[inline]
pub fn clamp(mut x: f64, min: f64, max: f64) -> f64 {
    if x < min {
        x = min;
    }
    if x > max {
        x = max;
    }
    x
}

fn mean(data: &[u16]) -> f64 {
    let mut sum = 0;
    for &datum in data {
        sum += u64::from(datum);
    }
    sum as f64 / data.len() as f64
}

fn stdev(data: &[u16], mean: f64) -> f64 {
    let mut sum = 0.0;
    for &datum in data {
        let diff = mean - f64::from(datum);
        sum += diff * diff;
    }
    (sum / data.len() as f64).sqrt()
}

pub fn adjust_image(image: &CpuTexture<u16>) -> CpuTexture<[u8; 4]> {
    let mean = mean(&image.data);
    let stdev = stdev(&image.data, mean);
    let mut result = Vec::with_capacity(image.data.len());
    // ((x - mean) / (stdev * size) + 0.5) * 255
    // ((x / (stdev * size) - mean / (stdev * size)) + 0.5) * 255
    // (x / (stdev * size) - mean / (stdev * size) + 0.5) * 255
    // x / (stdev * size) * 255 - mean / (stdev * size) * 255 + 0.5 * 255
    // x * (255 / (stdev * size)) + (-mean / (stdev * size) + 0.5) * 255
    // x * a + b
    const SIZE: f64 = 3.0;
    let a = 255.0 / (stdev * SIZE);
    let b = 255.0 * (-mean / (stdev * SIZE) + 0.5);
    for &item in &image.data {
        let mapped = f64::from(item) * a + b;
        let one = clamp(mapped, 0.0, 255.0) as u8;
        result.push([255, one, one, one]);
    }
    CpuTexture::new(result, image.size)
}
