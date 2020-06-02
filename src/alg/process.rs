// use super::starfinder::{find_stars, Star};
use crate::{camera::interface::ROIImage, Result, SendUserUpdate, UserUpdate};
use khygl::texture::CpuTexture;
use std::{
    fmt::Write,
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

#[derive(Debug)]
pub struct ProcessResult {
    sorted: Vec<u16>,
    mean: f64,
    stdev: f64,
    duration: Duration,
    // stars: Vec<Star>,
}

fn u16_to_f64(val: u16) -> f64 {
    val as f64 / f64::from(u16::max_value())
}

impl ProcessResult {
    fn compute(image: &CpuTexture<u16>) -> Self {
        let begin = Instant::now();
        let mut sorted = image.data().to_vec();
        sorted.sort_unstable();
        let mean = mean(&sorted);
        let stdev = stdev(&sorted, mean);
        // let stars = find_stars(image, mean, stdev);
        let duration = Instant::now() - begin;
        Self {
            sorted,
            mean,
            stdev,
            duration,
            // stars,
        }
    }

    fn get_clip_median(&self, clip_perc: f64) -> (u16, u16) {
        let len = self.sorted.len() as f64;
        let clip_index = (clip_perc * len).max(0.0).min(len - 1.0);
        let clip = if clip_perc == 0.0 {
            0
        } else {
            self.sorted[clip_index as usize]
        };
        let median_index = (clip_index + (len - 1.0)) / 2.0;
        let median = self.sorted[median_index as usize];
        (clip, median)
    }

    fn median_scale_offset(&self, clip_perc: f64, median_location: f64) -> (f64, f64) {
        let (clip, mut median) = self.get_clip_median(clip_perc);
        if clip == median {
            if clip < u16::max_value() - (1 << 5) {
                median = clip + (1 << 5);
            } else {
                median = u16::max_value();
            }
        }
        let (clip, median) = (u16_to_f64(clip), u16_to_f64(median));
        // (x - clip) * (median_location / (median - clip))
        // x * (median_location / (median - clip)) + (-clip * (median_location / (median - clip)))
        // x * a + b
        let clipped_median = median - clip;
        let scale = median_location / clipped_median;
        let offset = -clip * scale;
        (scale, offset)
    }

    fn mean_scale_offset(&self, sigma: f64, mean_location: f64) -> (f64, f64) {
        // y = (x - mean) / (stdev * sigma) + mean_location
        // y = x * 1 / (stdev * sigma) - mean / (stdev * sigma) + mean_location
        let mean = self.mean / (f64::from(u16::max_value()) + 1.0);
        let stdev = self.stdev / (f64::from(u16::max_value()) + 1.0);
        let scale = 1.0 / (stdev * sigma);
        let offset = mean_location - mean / (stdev * sigma);
        (scale, offset)
    }
}

enum ProcessorType {
    Median,
    Mean,
    Linear,
}

pub struct Processor {
    send: mpsc::SyncSender<Arc<ROIImage>>,
    process_result: Option<ProcessResult>,
    processor_type: ProcessorType,

    clip: f64,
    median_location: f64,

    sigma: f64,
    mean_location: f64,

    scale: f64,
    offset: f64,
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
        Self {
            send,
            process_result: None,
            processor_type: ProcessorType::Median,

            clip: 0.01,
            median_location: 0.2,

            sigma: 3.0,
            mean_location: 0.2,

            scale: 1.0,
            offset: 0.0,
        }
    }

    // true if ok, false if dropped frame
    pub fn process(&self, image: Arc<ROIImage>) -> Result<bool> {
        match self.send.try_send(image) {
            Ok(()) => Ok(true),
            Err(mpsc::TrySendError::Full(_)) => Ok(false),
            Err(mpsc::TrySendError::Disconnected(_)) => {
                Err("Processing thread disconnected".into())
            }
        }
    }

    pub fn cmd(&mut self, command: &[&str]) -> Result<bool> {
        fn parse(key: &str, value: &str, name: &str, data: &mut f64, perc: bool) -> bool {
            if key == name {
                if let Ok(v) = value.parse::<f64>() {
                    *data = if perc { v / 100.0 } else { v };
                    return true;
                } else {
                }
            }
            false
        }
        match *command {
            ["median"] => self.processor_type = ProcessorType::Median,
            ["mean"] => self.processor_type = ProcessorType::Mean,
            ["linear"] => self.processor_type = ProcessorType::Linear,
            [key, value] => {
                let ok = parse(key, value, "clip", &mut self.clip, true)
                    || parse(
                        key,
                        value,
                        "median_location",
                        &mut self.median_location,
                        true,
                    )
                    || parse(key, value, "sigma", &mut self.sigma, false)
                    || parse(key, value, "mean_location", &mut self.mean_location, true)
                    || parse(key, value, "scale", &mut self.scale, false)
                    || parse(key, value, "offset", &mut self.offset, false);
                return Ok(ok);
            }
            _ => return Ok(false),
        }
        Ok(true)
    }

    pub fn status(&self, status: &mut String) -> Result<()> {
        match self.processor_type {
            ProcessorType::Median => {
                writeln!(status, "process: median (mean, linear)")?;
                writeln!(
                    status,
                    "clip: {}% median_location: {}%",
                    self.clip * 100.0,
                    self.median_location * 100.0,
                )?;
            }
            ProcessorType::Mean => {
                writeln!(status, "process: mean (median, linear)")?;
                writeln!(
                    status,
                    "sigma: {} mean_location: {}%",
                    self.sigma,
                    self.mean_location * 100.0,
                )?;
            }
            ProcessorType::Linear => {
                writeln!(status, "process: linear (median, mean)")?;
            }
        }
        if let Some(ref process_result) = self.process_result {
            let (_, median) = process_result.get_clip_median(0.0);
            let (scale, offset) = self
                .get_scale_offset()
                .expect("process_result should have been Some");
            writeln!(status, "scale: {:.3} offset: {:.3}", scale, offset)?;
            writeln!(
                status,
                "(median: {:.3} mean: {:.3} stdev: {:.3})",
                median, process_result.mean, process_result.stdev
            )?;
            let percmul = 100.0 / f64::from(u16::max_value());
            writeln!(
                status,
                "(median: {:.1}% mean: {:.1}% stdev: {:.1}%)",
                median as f64 * percmul,
                process_result.mean * percmul,
                process_result.stdev * percmul,
            )?;
            writeln!(
                status,
                "image processing time: {:?}",
                process_result.duration
            )?;
        }
        Ok(())
    }

    pub fn user_update(&mut self, process_result: ProcessResult) {
        self.process_result = Some(process_result);
    }

    pub fn get_scale_offset(&self) -> Option<(f64, f64)> {
        let process_result = self.process_result.as_ref()?;
        let result = match self.processor_type {
            ProcessorType::Median => {
                process_result.median_scale_offset(self.clip, self.median_location)
            }
            ProcessorType::Mean => process_result.mean_scale_offset(self.sigma, self.mean_location),
            ProcessorType::Linear => (self.scale, self.offset),
        };
        Some(result)
    }

    // pub fn get_stars(&self) -> Option<&[Star]> {
    //     Some(&self.process_result.as_ref()?.stars)
    // }
}
