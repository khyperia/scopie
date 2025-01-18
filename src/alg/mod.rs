use crate::{camera::interface::CpuTexture, Result};
use eframe::egui::{self, NumExt};
use std::{fs::File, ops::Add, path::Path};

// pub mod process;
// mod starfinder;

#[derive(Clone, Debug)]
pub struct Rect<T> {
    pub x: T,
    pub y: T,
    pub width: T,
    pub height: T,
}

impl<T> Rect<T> {
    pub fn new(x: T, y: T, width: T, height: T) -> Self {
        Self {
            x,
            y,
            width,
            height,
        }
    }

    pub fn right(&self) -> <T as Add>::Output
    where
        T: Add<T> + Copy,
    {
        self.x + self.width
    }

    pub fn bottom(&self) -> <T as Add>::Output
    where
        T: Add<T> + Copy,
    {
        self.y + self.height
    }
}

macro_rules! impl_into {
    ($x:ty) => {
        impl Rect<$x> {
            pub fn to_f32(&self) -> Rect<f32> {
                Rect {
                    x: self.x as f32,
                    y: self.y as f32,
                    width: self.width as f32,
                    height: self.height as f32,
                }
            }
        }
    };
}

impl_into!(f64);
impl_into!(usize);

pub fn clamp_aspect_ratio(image_size: [usize; 2], rect: egui::Rect) -> egui::Rect {
    if rect.width().at_least(1.0) / rect.height().at_least(1.0)
        < image_size[0].at_least(1) as f32 / image_size[1].at_least(1) as f32
    {
        rect.with_max_y(rect.min.y + rect.width() * image_size[1] as f32 / image_size[0] as f32)
    } else {
        rect.with_max_x(rect.min.x + rect.height() * image_size[0] as f32 / image_size[1] as f32)
    }
}

pub fn clamp_aspect_ratio_vec(image_size: [usize; 2], to_clamp: egui::Vec2) -> egui::Vec2 {
    if to_clamp.x.at_least(1.0) / to_clamp.y.at_least(1.0)
        < image_size[0].at_least(1) as f32 / image_size[1].at_least(1) as f32
    {
        egui::Vec2::new(
            to_clamp.x,
            to_clamp.x * image_size[1] as f32 / image_size[0] as f32,
        )
    } else {
        egui::Vec2::new(
            to_clamp.y * image_size[0] as f32 / image_size[1] as f32,
            to_clamp.y,
        )
    }
}

pub fn read_png(path: impl AsRef<Path>) -> Result<CpuTexture<u16>> {
    let mut decoder = png::Decoder::new(File::open(path)?);
    decoder.set_transformations(png::Transformations::IDENTITY);
    let mut reader = decoder.read_info()?;
    let info = reader.info();
    assert_eq!(info.bit_depth, png::BitDepth::Sixteen);
    assert_eq!(info.color_type, png::ColorType::Grayscale);
    let width = info.width;
    let height = info.height;
    let mut buf = vec![0; reader.output_buffer_size()];
    reader.next_frame(&mut buf)?;
    let mut buf16 = vec![0; width as usize * height as usize];
    for i in 0..buf16.len() {
        buf16[i] = u16::from(buf[i * 2]) << 8 | u16::from(buf[i * 2 + 1]);
    }
    Ok(CpuTexture::new(buf16, (width as usize, height as usize)))
}

pub fn write_png(path: impl AsRef<Path>, img: &CpuTexture<u16>) -> Result<()> {
    let mut encoder = png::Encoder::new(File::create(path)?, img.size.0 as u32, img.size.1 as u32);
    encoder.set_color(png::ColorType::Grayscale);
    encoder.set_depth(png::BitDepth::Sixteen);
    let mut writer = encoder.write_header()?;
    let mut output = vec![0; img.size.0 * img.size.1 * 2];
    let data = img.data();
    for i in 0..(img.size.0 * img.size.1) {
        output[i * 2] = (data[i] >> 8) as u8;
        output[i * 2 + 1] = (data[i]) as u8;
    }
    writer.write_image_data(&output)?;
    Ok(())
}

pub fn median(seq: &mut [f64]) -> f64 {
    seq.sort_unstable_by(|l, r| l.partial_cmp(&r).unwrap());
    let idx = seq.len() / 2;
    if seq.len() % 2 == 0 {
        (seq[idx - 1] + seq[idx]) / 2.0
    } else {
        seq[idx]
    }
}

pub fn mean(seq: impl IntoIterator<Item = f64>) -> f64 {
    let seq = seq.into_iter();
    let mut sum = 0.0;
    let mut count = 0;
    for item in seq {
        sum += item;
        count += 1;
    }
    sum / count as f64
}

pub fn stdev(mean: f64, seq: impl IntoIterator<Item = f64>) -> f64 {
    let seq = seq.into_iter();
    let mut sum = 0.0;
    let mut count = 0;
    for item in seq {
        let diff = mean - item;
        sum += diff * diff;
        count += 1;
    }
    (sum / count as f64).sqrt()
}

// Welford's online algorithm
pub fn mean_stdev(seq: impl IntoIterator<Item = f64>) -> (f64, f64) {
    let seq = seq.into_iter();
    let mut count = 0;
    let mut mean = 0.0;
    let mut m2 = 0.0;
    for item in seq {
        count += 1;
        let delta = item - mean;
        mean += delta / count as f64;
        let delta2 = item - mean;
        m2 += delta * delta2;
    }
    let variance = m2 / count as f64;
    (mean, variance.sqrt())
}

pub fn f64_to_u16(mut value: f64) -> u16 {
    let max_value = f64::from(u16::max_value());
    value *= max_value;
    if value >= max_value {
        u16::max_value()
    } else if value > 0.0 {
        value as u16
    } else {
        0
    }
}

pub fn u16_to_f64(value: u16) -> f64 {
    let max_value = f64::from(u16::max_value());
    value as f64 / max_value
}

pub fn f64_to_u8(mut value: f64) -> u8 {
    let max_value = f64::from(u8::max_value());
    value *= max_value;
    if value >= max_value {
        u8::max_value()
    } else if value > 0.0 {
        value as u8
    } else {
        0
    }
}

/*
fn floodfind_one<T: Copy>(
    img: &CpuTexture<T>,
    condition: &impl Fn(T) -> bool,
    coord: (usize, usize),
    mask: &mut CpuTexture<bool>,
) -> Vec<(usize, usize)> {
    let mut set = Vec::new();
    let mut next = Vec::new();
    mask[coord] = true;
    next.push(coord);
    set.push(coord);
    while let Some(coord) = next.pop() {
        let mut go = |dx, dy| {
            if let Some(coordnext) = offset(coord, (dx, dy), img.size) {
                if mask[coordnext] || !condition(img[coordnext]) {
                    return;
                }
                mask[coordnext] = true;
                next.push(coordnext);
                set.push(coordnext);
            }
        };
        go(-1, 0);
        go(1, 0);
        go(0, -1);
        go(0, 1);
    }
    set
}

pub fn floodfind<T: Copy>(
    img: &CpuTexture<T>,
    condition: impl Fn(T) -> bool,
) -> Vec<Vec<(usize, usize)>> {
    let mut mask = CpuTexture::new_val(false, img.size);
    let mut results = Vec::new();
    for coord in img.iter_index() {
        if mask[coord] {
            continue;
        }
        if condition(img[coord]) {
            let result = floodfind_one(img, &condition, coord, &mut mask);
            results.push(result);
        }
    }
    results
}
*/
