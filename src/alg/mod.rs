use std::ops::Add;

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
    let mut M2 = 0.0;
    for item in seq {
        count += 1;
        let delta = item - mean;
        mean += delta / count as f64;
        let delta2 = item - mean;
        M2 += delta * delta2;
    }
    let variance = M2 / count as f64;
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

pub fn u16_to_f64(mut value: u16) -> f64 {
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
