use crate::Result;
use khygl::texture::CpuTexture;
use std::{
    fmt::Write,
    sync::{mpsc, Arc},
    thread::spawn,
    time::Instant,
};

// TODO: use f64::clamp() once stable
// Alternatively, saturating casts: https://github.com/rust-lang/rust/issues/10184
#[inline]
pub fn saturating_cast_f64_u8(x: f64) -> u8 {
    if x > 255.0 {
        255
    } else if x > 0.0 {
        x as u8
    } else {
        0
    }
}

pub fn mean(data: &[u16]) -> f64 {
    let mut sum = 0;
    for &datum in data {
        sum += u64::from(datum);
    }
    sum as f64 / data.len() as f64
}

pub fn stdev(data: &[u16], mean: f64) -> f64 {
    let mut sum = 0.0;
    for &datum in data {
        let diff = mean - f64::from(datum);
        sum += diff * diff;
    }
    (sum / data.len() as f64).sqrt()
}

fn do_median(data: &[u16], status: &mut String) -> Vec<[u8; 4]> {
    let mut stuff = data.iter().copied().enumerate().collect::<Vec<_>>();
    stuff.sort_unstable_by_key(|&(_, v)| v);
    let mut result = vec![[0, 0, 0, 0]; data.len()];
    let mut last_under_95p = 0;
    const P95: u16 = (u16::max_value() as u32 * 19 / 20) as u16;
    for (ind, (orig, value)) in stuff.iter().copied().enumerate() {
        if value < P95 {
            last_under_95p = ind;
        }
        let val = (255 * ind / data.len()) as u8;
        result[orig as usize] = [255, val, val, val];
    }
    let saturated_float = 1.0 - last_under_95p as f64 / (data.len() - 1) as f64;
    let saturated_int = data.len() - last_under_95p - 1;
    writeln!(
        status,
        "saturated pixels: {:.5}% ({})",
        saturated_float * 100.0,
        saturated_int
    )
    .expect("Could not format adjust_image status");
    result
}

fn adjust_image(
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
    writeln!(
        status,
        "mean:{:.3} stdev:{:.3} (sigma={:.2})",
        mean, stdev, sigma
    )
    .expect("Could not format adjust_image status");
    writeln!(status, "{:.2}% -> {:.2}x for 20%", percent, mul20)
        .expect("Could not format adjust_image status");
    if median {
        CpuTexture::new(do_median(&image.data, status), image.size)
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
                let one = saturating_cast_f64_u8(mapped);
                [one, one, one, 255]
            })
            .collect();
        CpuTexture::new(result, image.size)
    }
}

pub struct Processor {
    send: mpsc::SyncSender<(Arc<CpuTexture<u16>>, f64, bool)>,
    recv: mpsc::Receiver<(CpuTexture<[u8; 4]>, String)>,
}

impl Processor {
    pub fn new() -> Self {
        let (send_u16, recv_u16) = mpsc::sync_channel::<(Arc<CpuTexture<u16>>, f64, bool)>(1);
        let (send_u8, recv_u8) = mpsc::channel();
        spawn(move || {
            while let Ok((img, sigma, median)) = recv_u16.recv() {
                let mut status = String::new();
                let now = Instant::now();
                let result = adjust_image(&img, sigma, median, &mut status);
                let process_time = Instant::now() - now;
                writeln!(status, "Processing time: {:?}", process_time)
                    .expect("Could not format adjust_image status");
                if send_u8.send((result, status)).is_err() {
                    break;
                }
            }
        });
        Self {
            send: send_u16,
            recv: recv_u8,
        }
    }

    // true if ok, false if dropped frame
    pub fn process(&self, image: Arc<CpuTexture<u16>>, sigma: f64, median: bool) -> Result<bool> {
        match self.send.try_send((image, sigma, median)) {
            Ok(()) => Ok(true),
            Err(mpsc::TrySendError::Full(_)) => Ok(false),
            Err(mpsc::TrySendError::Disconnected(_)) => {
                Err(failure::err_msg("Processing thread disconnected"))
            }
        }
    }

    pub fn get(&self) -> Result<Option<(CpuTexture<[u8; 4]>, String)>> {
        match self.recv.try_recv() {
            Ok(img) => Ok(Some(img)),
            Err(mpsc::TryRecvError::Empty) => Ok(None),
            Err(mpsc::TryRecvError::Disconnected) => {
                Err(failure::err_msg("Processing thread disconnected"))
            }
        }
    }
}
