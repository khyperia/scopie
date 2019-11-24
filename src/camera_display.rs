use crate::{
    camera_async,
    image_display::ImageDisplay,
    mount_async::MountAsync,
    platesolve::platesolve,
    process,
    qhycamera::{ControlId, EXPOSURE_FACTOR},
    Result, SendUserUpdate, UserUpdate,
};
use dirs;
use khygl::{render_texture::TextureRenderer, texture::CpuTexture, Rect};
use std::{convert::TryInto, fmt::Write, fs::create_dir_all, path::PathBuf, time::Instant};

pub struct CameraDisplay {
    camera: Option<camera_async::CameraAsync>,
    send_user_update: SendUserUpdate,
    image_display: ImageDisplay,
    processor: process::Processor,
    process_result: Option<process::ProcessResult>,
    display_clip: f64,
    display_median_location: f64,
    display_interesting: bool,
    save: usize,
    folder: String,
    solve_status: String,
    cached_status: String,
}

impl CameraDisplay {
    pub fn new(send_user_update: SendUserUpdate) -> Self {
        Self {
            camera: Some(camera_async::CameraAsync::new(send_user_update.clone())),
            send_user_update: send_user_update.clone(),
            image_display: ImageDisplay::new(),
            processor: process::Processor::new(send_user_update),
            process_result: None,
            display_clip: 0.01,
            display_median_location: 0.2,
            display_interesting: true,
            save: 0,
            folder: String::new(),
            solve_status: String::new(),
            cached_status: String::new(),
        }
    }

    pub fn cmd(&mut self, command: &[&str]) -> Result<bool> {
        match *command {
            ["cross"] => {
                self.image_display.cross = !self.image_display.cross;
            }
            ["zoom"] => {
                self.image_display.zoom = !self.image_display.zoom;
            }
            ["bin"] => {
                self.image_display.bin = !self.image_display.bin;
            }
            ["interesting"] => {
                self.display_interesting = !self.display_interesting;
            }
            ["clip", clip] => {
                if let Ok(clip) = clip.parse::<f64>() {
                    self.display_clip = clip / 100.0;
                } else {
                    return Ok(false);
                }
            }
            ["median_location", median_location] => {
                if let Ok(median_location) = median_location.parse::<f64>() {
                    self.display_median_location = median_location / 100.0;
                } else {
                    return Ok(false);
                }
            }
            ["live"] => {
                self.camera_op(|c| c.toggle_live());
            }
            ["open"] if self.camera.as_ref().map_or(false, |c| !c.data.running) => {
                self.camera_op(|c| c.start());
            }
            ["close"] if self.camera.as_ref().map_or(false, |c| c.data.running) => {
                self.camera_op(|c| c.stop());
            }
            ["folder"] => {
                self.folder = String::new();
            }
            ["folder", name] => {
                self.folder = name.to_string();
            }
            ["save"] => {
                self.save += 1;
            }
            ["save", "now"] if self.image_display.raw().is_some() => {
                if let Some(ref raw) = self.image_display.raw() {
                    self.save_png(raw)?;
                }
            }
            ["save", n] => {
                if let Ok(n) = n.parse::<isize>() {
                    self.save = (self.save as isize + n).try_into().unwrap_or(0);
                } else {
                    return Ok(false);
                }
            }
            ["solve"] => {
                if let Some(ref raw) = self.image_display.raw() {
                    platesolve(raw, self.send_user_update.clone())?;
                } else {
                    return Ok(false);
                }
            }
            ["exposure", value] => {
                if let Ok(value) = value.parse::<f64>() {
                    self.camera_op(|c| {
                        c.set_control(ControlId::ControlExposure, value * EXPOSURE_FACTOR)
                    })
                }
            }
            ["gain", value] => {
                if let Ok(value) = value.parse::<f64>() {
                    self.camera_op(|c| c.set_control(ControlId::ControlGain, value))
                }
            }
            [name, value] => {
                let mut ok = false;
                if let (Ok(id), Ok(value)) = (name.parse(), value.parse()) {
                    self.camera_op(|c| c.set_control(id, value));
                    ok = true;
                }
                return Ok(ok);
            }
            _ => return Ok(false),
        }
        Ok(true)
    }

    fn camera_op(
        &mut self,
        op: impl Fn(&camera_async::CameraAsync) -> std::result::Result<(), ()>,
    ) {
        let ok = match self.camera {
            Some(ref mut camera) => op(camera).is_ok(),
            None => true,
        };
        if !ok {
            self.camera = None;
        }
    }

    pub fn status(&mut self, status: &mut String, infrequent_update: bool) -> Result<()> {
        if infrequent_update {
            self.cached_status.clear();
            if let Some(ref camera) = self.camera {
                if camera.data.running {
                    let exposure = Instant::now() - camera.data.exposure_start;
                    writeln!(
                        &mut self.cached_status,
                        "Time since exposure start: {:.1}s",
                        exposure.as_secs_f64()
                    )?;
                }
            }
            if let Some(ref camera) = self.camera {
                for control in &camera.data.controls {
                    if control.id == ControlId::ControlExposure {
                        writeln!(
                            self.cached_status,
                            "exposure = {} ({}-{} by {})",
                            control.value / EXPOSURE_FACTOR,
                            control.min / EXPOSURE_FACTOR,
                            control.max / EXPOSURE_FACTOR,
                            control.step / EXPOSURE_FACTOR,
                        )?;
                    } else if !self.display_interesting || control.interesting {
                        writeln!(self.cached_status, "{}", control)?;
                    }
                }
            }
        }
        if let Some(ref camera) = self.camera {
            writeln!(status, "{}", camera.data.name)?;
            if camera.data.running {
                writeln!(status, "close: (running)")?;
            } else {
                writeln!(status, "open: (paused)")?;
            }
            if camera.data.is_live {
                writeln!(status, "live: (enabled)")?;
            } else {
                writeln!(status, "live: (not live)")?;
            }
            writeln!(status, "Last exposure: {:?}", camera.data.exposure_duration)?;
            if !camera.data.cmd_status.is_empty() {
                writeln!(status, "Camera error: {}", camera.data.cmd_status)?;
            }
        }
        writeln!(
            status,
            "cross:{}|zoom:{}|bin:{}",
            self.image_display.cross, self.image_display.zoom, self.image_display.bin,
        )?;
        writeln!(status, "interesting: {}", self.display_interesting,)?;
        writeln!(status, "save|save [n]: {}", self.save)?;
        if !self.solve_status.is_empty() {
            writeln!(status, "solve: {}", self.solve_status)?;
        }
        if self.folder.is_empty() {
            writeln!(status, "folder: None")?;
        } else {
            writeln!(status, "folder: {}", self.folder)?;
        }
        if let Some(ref process_result) = self.process_result {
            let process_result =
                process_result.apply(self.display_clip, self.display_median_location);
            write!(status, "{}", process_result)?;
        }
        write!(status, "{}", self.cached_status)?;
        Ok(())
    }

    fn save_png(&self, data: &CpuTexture<u16>) -> Result<()> {
        let tm = time::now();
        let dirname = format!(
            "{}_{:02}_{:02}",
            tm.tm_year + 1900,
            tm.tm_mon + 1,
            tm.tm_mday,
        );
        let filename = format!(
            "telescope.{}-{:02}-{:02}.{:02}-{:02}-{:02}.png",
            tm.tm_year + 1900,
            tm.tm_mon + 1,
            tm.tm_mday,
            tm.tm_hour,
            tm.tm_min,
            tm.tm_sec
        );
        let mut filepath = dirs::desktop_dir().unwrap_or_else(PathBuf::new);
        filepath.push(dirname);
        if !self.folder.is_empty() {
            filepath.push(&self.folder);
        }
        if !filepath.exists() {
            create_dir_all(&filepath)?;
        }
        filepath.push(filename);
        crate::write_png(filepath, data)?;
        Ok(())
    }

    pub fn draw(
        &mut self,
        pos: Rect<usize>,
        displayer: &TextureRenderer,
        screen_size: (f32, f32),
    ) -> Result<()> {
        if let Some(ref process_result) = self.process_result {
            let scale_offset = process_result
                .apply(self.display_clip, self.display_median_location)
                .get_scale_offset();
            self.image_display.scale_offset = (scale_offset.0 as f32, scale_offset.1 as f32);
        };
        self.image_display.draw(pos, displayer, screen_size)?;
        Ok(())
    }

    pub fn user_update(
        &mut self,
        user_update: UserUpdate,
        mount: Option<&mut MountAsync>,
    ) -> Result<()> {
        match user_update {
            UserUpdate::SolveFinished(ra, dec) => {
                self.solve_status = format!("{} {}", ra.fmt_hours(), dec.fmt_degrees());
                if let Some(mount) = mount {
                    let old_mount_radec = mount.data.ra_dec;
                    let delta_ra = old_mount_radec.0 - ra;
                    let delta_dec = old_mount_radec.1 - dec;
                    mount.add_real_to_mount_delta(delta_ra, delta_dec)?;
                    self.solve_status = format!(
                        "{} -> Δ {} {}",
                        self.solve_status,
                        delta_ra.fmt_hours(),
                        delta_dec.fmt_degrees()
                    );
                }
            }
            UserUpdate::CameraData(image) => {
                if self.save > 0 {
                    self.save -= 1;
                    self.save_png(&image)?;
                }
                self.image_display.set_raw(image)?;
                let ok = self.processor.process(
                    self.image_display
                        .raw()
                        .as_ref()
                        .expect("new_raw and no raw")
                        .clone(),
                )?;
                if !ok {
                    // TODO
                    // println!("Dropped frame");
                }
            }
            UserUpdate::ProcessResult(process_result) => self.process_result = Some(process_result),
            user_update => {
                if let Some(ref mut camera) = self.camera {
                    camera.user_update(user_update);
                }
            }
        }
        Ok(())
    }
}
