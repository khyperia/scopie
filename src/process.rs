use khygl::texture::CpuTexture;
use std::fmt::Write;

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

fn do_median(data: &[u16], status: &mut String) -> Vec<[u8; 4]> {
    let mut stuff = data.iter().enumerate().collect::<Vec<_>>();
    stuff.sort_unstable_by_key(|(_, &v)| v);
    let mut result = vec![[0, 0, 0, 0]; data.len()];
    let mut last_under_95p = 0;
    const P95: u16 = (u16::max_value() as u32 * 19/20) as u16;
    for (ind, &(orig, &value)) in stuff.iter().enumerate() {
        if value < P95 {
            last_under_95p = ind;
        }
        let val = (255 * ind / data.len()) as u8;
        result[orig as usize] = [255, val, val, val];
    }
    let saturated_float = 1.0 - last_under_95p as f64 / (data.len() - 1) as f64;
    let saturated_int = data.len() - last_under_95p - 1;
    writeln!(status, "saturated pixels: {:.5}% ({})", saturated_float * 100.0, saturated_int)
        .expect("Could not format adjust_image status");
    result
}

pub fn adjust_image(
    image: &CpuTexture<u16>,
    sigma: f64,
    median: bool,
    status: &mut String,
) -> CpuTexture<[u8; 4]> {
    let mean = mean(&image.data);
    let stdev = stdev(&image.data, mean);
    let frac = mean / f64::from(u16::max_value());
    let percent = frac * 100.0;
    let mul20 = 0.2 / frac;
    writeln!(status, "mean:{:.3} stdev:{:.3} (sigma={:.2})", mean, stdev, sigma)
        .expect("Could not format adjust_image status");
    writeln!(status, "{:.2}% -> {:.2}x for 20%", percent, mul20)
        .expect("Could not format adjust_image status");
    if median {
        CpuTexture::new(
            do_median(&image.data[..(image.size.0 * image.size.1)], status),
            image.size,
        )
    } else {
        // ((x - mean) / (stdev * sigma) + offset) * 255
        // ((x / (stdev * sigma) - mean / (stdev * sigma)) + offset) * 255
        // (x / (stdev * sigma) - mean / (stdev * sigma) + offset) * 255
        // x / (stdev * sigma) * 255 - mean / (stdev * sigma) * 255 + offset * 255
        // x * (255 / (stdev * sigma)) + (-mean / (stdev * sigma) + offset) * 255
        // x * a + b
        const MEAN_OFFSET: f64 = 0.2;
        let a = 255.0 / (stdev * sigma);
        let b = 255.0 * (-mean / (stdev * sigma) + MEAN_OFFSET);
        let result = image
            .data
            .iter()
            .map(|&item| {
                let mapped = f64::from(item).mul_add(a, b);
                let one = clamp(mapped, 0.0, 255.0) as u8;
                [255, one, one, one]
            })
            .collect();
        CpuTexture::new(result, image.size)
    }
}
