use crate::{camera::ROIImage, Result, SendUserUpdate, UserUpdate};
use khygl::texture::CpuTexture;
use std::{
    sync::{mpsc, Arc},
    thread::spawn,
    time::{Duration, Instant},
};

/*
fn median(data: &[u16]) -> f64 {
    let mut data = data.to_vec();
    data.sort_unstable();
    data[data.len() / 2] as f64
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
*/

pub struct ProcessResult {
    sorted: Vec<u16>,
    gap: u16,
    pub duration: Duration,
}

fn u16_to_f64(val: u16) -> f64 {
    val as f64 / f64::from(u16::max_value())
}

impl ProcessResult {
    fn compute(image: &CpuTexture<u16>) -> Self {
        let begin = Instant::now();
        let mut sorted = image.data().to_vec();
        sorted.sort_unstable();
        let median = sorted[sorted.len() / 2];
        let after_median_location = match sorted.binary_search(&(median + 1)) {
            Ok(v) => v,
            Err(v) => v,
        };
        let after_median = sorted.get(after_median_location).map_or(median + 1, |&v| v);
        let gap = after_median - median;
        let duration = Instant::now() - begin;
        Self {
            sorted,
            gap,
            duration,
        }
    }

    pub fn apply(&self, clip: f64, median_location: f64) -> AppliedProcessResult {
        AppliedProcessResult {
            result: self,
            clip,
            median_location,
        }
    }
}

pub struct AppliedProcessResult<'a> {
    result: &'a ProcessResult,
    clip: f64,
    median_location: f64,
}

impl AppliedProcessResult<'_> {
    fn get_clip_median(&self) -> (u16, u16) {
        let len = self.result.sorted.len() as f64;
        let clip_index = (self.clip * len).max(0.0).min(len - 1.0);
        let clip = if self.clip == 0.0 {
            0
        } else {
            self.result.sorted[clip_index as usize]
        };
        let median_index = (clip_index + (len - 1.0)) / 2.0;
        let median = self.result.sorted[median_index as usize];
        (clip, median)
    }

    pub fn get_scale_offset(&self) -> (f64, f64) {
        let (clip, mut median) = self.get_clip_median();
        if clip == median {
            median = clip + (1 << 5);
        }
        let (clip, median) = (u16_to_f64(clip), u16_to_f64(median));
        // (x - clip) * (median_location / (median - clip))
        // x * (median_location / (median - clip)) + (-clip * (median_location / (median - clip)))
        // x * a + b
        let clipped_median = median - clip;
        let scale = self.median_location / clipped_median;
        let offset = -clip * scale;
        (scale, offset)
    }
}

impl std::fmt::Display for AppliedProcessResult<'_> {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        let (clip, median) = self.get_clip_median();
        let (scale, offset) = self.get_scale_offset();
        writeln!(
            f,
            "clip: {}% median_location: {}%",
            self.clip * 100.0,
            self.median_location * 100.0,
        )?;
        writeln!(
            f,
            "median: {} -> subtract: {} scale: {:.3} offset {:.3}",
            median, clip, scale, offset
        )?;
        writeln!(f, "space between pixel values: {}", self.result.gap)?;
        writeln!(f, "image processing time: {:?}", self.result.duration)?;
        Ok(())
    }
}

pub struct Processor {
    send: mpsc::SyncSender<Arc<ROIImage>>,
    //recv: mpsc::Receiver<ProcessResult>,
}

impl Processor {
    pub fn new(send_user_update: SendUserUpdate) -> Self {
        let (send, recv) = mpsc::sync_channel::<Arc<ROIImage>>(1);
        spawn(move || {
            while let Ok(img) = recv.recv() {
                let result = UserUpdate::ProcessResult(ProcessResult::compute(&img.image));
                if send_user_update.send_event(result).is_err() {
                    break;
                }
            }
        });
        Self { send }
    }

    // true if ok, false if dropped frame
    pub fn process(&self, image: Arc<ROIImage>) -> Result<bool> {
        match self.send.try_send(image) {
            Ok(()) => Ok(true),
            Err(mpsc::TrySendError::Full(_)) => Ok(false),
            Err(mpsc::TrySendError::Disconnected(_)) => {
                Err(failure::err_msg("Processing thread disconnected"))
            }
        }
    }
}
