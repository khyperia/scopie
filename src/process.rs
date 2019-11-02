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

fn do_median(data: &[u16]) -> Vec<[u8; 4]> {
    let mut stuff = data.iter().enumerate().collect::<Vec<_>>();
    stuff.sort_unstable_by_key(|(_, &v)| v);
    let mut result = vec![[0, 0, 0, 0]; data.len()];
    for (ind, &(orig, _)) in stuff.iter().enumerate() {
        let val = (255 * ind / data.len()) as u8;
        result[orig as usize] = [255, val, val, val];
    }
    result
}

pub fn adjust_image(image: &CpuTexture<u16>, median: bool, status: &mut String) -> CpuTexture<[u8; 4]> {
    if median {
        CpuTexture::new(
            do_median(&image.data[..(image.size.0 * image.size.1)]),
            image.size,
        )
    } else {
        let mean = mean(&image.data);
        let stdev = stdev(&image.data, mean);
        writeln!(status, "{:.3} {:.3} ({:.2}%)", mean, stdev, mean * 100.0 / f64::from(u16::max_value())).expect("Could not format adjust_image status");
        // ((x - mean) / (stdev * size) + 0.5) * 255
        // ((x / (stdev * size) - mean / (stdev * size)) + 0.5) * 255
        // (x / (stdev * size) - mean / (stdev * size) + 0.5) * 255
        // x / (stdev * size) * 255 - mean / (stdev * size) * 255 + 0.5 * 255
        // x * (255 / (stdev * size)) + (-mean / (stdev * size) + 0.5) * 255
        // x * a + b
        const SIZE: f64 = 3.0;
        let a = 255.0 / (stdev * SIZE);
        let b = 255.0 * (-mean / (stdev * SIZE) + 0.5);
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
