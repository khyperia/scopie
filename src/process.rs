use crate::Result;
use khygl::texture::CpuTexture;
use std::{
    sync::{mpsc, Arc},
    thread::spawn,
    time::{Duration, Instant},
};

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

pub struct ProcessResult {
    pub mean: f64,
    pub stdev: f64,
    pub duration: Duration,
}

impl ProcessResult {
    fn new(mean: f64, stdev: f64, duration: Duration) -> Self {
        Self {
            mean,
            stdev,
            duration,
        }
    }

    fn compute(image: &CpuTexture<u16>) -> Self {
        let begin = Instant::now();
        let mean = mean(&image.data);
        let stdev = stdev(&image.data, mean);
        let duration = Instant::now() - begin;
        Self::new(mean, stdev, duration)
    }

    pub fn get_scale_offset(&self, sigma: f64) -> (f64, f64) {
        // (x - mean) / (stdev * sigma) + offset
        // (x / (stdev * sigma) - mean / (stdev * sigma)) + offset
        // x / (stdev * sigma) - mean / (stdev * sigma) + offset
        // x / (stdev * sigma) + offset - mean / (stdev * sigma)
        // x * (1.0 / (stdev * sigma)) + (offset - mean / (stdev * sigma))
        // x * a + b
        let mean = self.mean / f64::from(u16::max_value());
        let stdev = self.stdev / f64::from(u16::max_value());
        const MEAN_OFFSET: f64 = 0.2;
        let a = 1.0 / (stdev * sigma);
        let b = MEAN_OFFSET - mean / (stdev * sigma);
        (a, b)
    }
}

impl std::fmt::Display for ProcessResult {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let frac = self.mean / f64::from(u16::max_value());
        let percent = frac * 100.0;
        let mul20 = 0.2 / frac;
        // TODO: Display sigma
        writeln!(f, "mean:{:.3} stdev:{:.3}", self.mean, self.stdev)?;
        writeln!(f, "{:.2}% -> {:.2}x for 20%", percent, mul20)?;
        writeln!(f, "Processing time: {:?}", self.duration)?;
        Ok(())
    }
}

/*
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
*/

pub struct Processor {
    send: mpsc::SyncSender<Arc<CpuTexture<u16>>>,
    recv: mpsc::Receiver<ProcessResult>,
}

impl Processor {
    pub fn new() -> Self {
        let (send_u16, recv_u16) = mpsc::sync_channel::<Arc<CpuTexture<u16>>>(1);
        let (send_u8, recv_u8) = mpsc::channel();
        spawn(move || {
            while let Ok(img) = recv_u16.recv() {
                if send_u8.send(ProcessResult::compute(&img)).is_err() {
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
    pub fn process(&self, image: Arc<CpuTexture<u16>>) -> Result<bool> {
        match self.send.try_send(image) {
            Ok(()) => Ok(true),
            Err(mpsc::TrySendError::Full(_)) => Ok(false),
            Err(mpsc::TrySendError::Disconnected(_)) => {
                Err(failure::err_msg("Processing thread disconnected"))
            }
        }
    }

    pub fn get(&self) -> Result<Option<ProcessResult>> {
        match self.recv.try_recv() {
            Ok(img) => Ok(Some(img)),
            Err(mpsc::TryRecvError::Empty) => Ok(None),
            Err(mpsc::TryRecvError::Disconnected) => {
                Err(failure::err_msg("Processing thread disconnected"))
            }
        }
    }
}
