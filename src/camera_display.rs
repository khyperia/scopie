use crate::{
    camera,
    image_display::ImageDisplay,
    mount_async::MountAsync,
    platesolve::platesolve,
    process,
    qhycamera::{ControlId, EXPOSURE_FACTOR},
    Result, SendUserUpdate, UserUpdate,
};
use dirs;
use khygl::{render_texture::TextureRenderer, texture::CpuTexture, Rect};
use std::{
    convert::TryInto,
    fmt::Write,
    fs::create_dir_all,
    path::PathBuf,
    sync::Arc,
    time::{Duration, Instant},
};

pub struct CameraDisplay {
    camera: Option<camera::Camera>,
    send_user_update: SendUserUpdate,
    image_display: ImageDisplay,
    processor: process::Processor,
    process_result: Option<process::ProcessResult>,
    running: bool,
    display_clip: f64,
    display_median_location: f64,
    save: usize,
    folder: String,
    solve_status: String,
    cached_status: String,
    exposure_start: Instant,
    exposure_time: Duration,
}

impl CameraDisplay {
    pub fn new(camera: Option<camera::Camera>, send_user_update: SendUserUpdate) -> Self {
        Self {
            camera,
            send_user_update,
            image_display: ImageDisplay::new(),
            processor: process::Processor::new(),
            process_result: None,
            running: false,
            display_clip: 0.01,
            display_median_location: 0.2,
            save: 0,
            folder: String::new(),
            solve_status: String::new(),
            cached_status: String::new(),
            exposure_start: Instant::now(),
            exposure_time: Duration::new(0, 0),
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
                if let Some(ref camera) = self.camera {
                    if self.running {
                        camera.stop()?;
                    }
                }
                let info = self
                    .camera
                    .take()
                    .map(|camera| (camera.info().clone(), camera.use_live()));
                if let Some((info, old_use_live)) = info {
                    self.camera = Some(info.open(!old_use_live)?);
                }
                if let Some(ref camera) = self.camera {
                    if self.running {
                        camera.stop()?;
                    }
                }
            }
            ["open"] if !self.running => {
                if let Some(ref camera) = self.camera {
                    camera.start()?;
                }
                self.running = true;
                self.exposure_start = Instant::now();
            }
            ["close"] if self.running => {
                if let Some(ref camera) = self.camera {
                    camera.stop()?;
                }
                self.running = false;
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
                if let Some(ref camera) = self.camera {
                    if let Ok(value) = value.parse::<f64>() {
                        for control in camera.controls() {
                            if control.id() == ControlId::ControlExposure {
                                control.set(value * EXPOSURE_FACTOR)?;
                            }
                        }
                    }
                }
            }
            ["gain", value] => {
                if let Some(ref camera) = self.camera {
                    if let Ok(value) = value.parse::<f64>() {
                        for control in camera.controls() {
                            if control.id() == ControlId::ControlGain {
                                control.set(value)?;
                            }
                        }
                    }
                }
            }
            [name, value] => {
                let mut ok = false;
                if let Some(ref camera) = self.camera {
                    if let Ok(value) = value.parse() {
                        for control in camera.controls() {
                            if control.name().eq_ignore_ascii_case(name) {
                                control.set(value)?;
                                ok = true;
                            }
                        }
                    }
                }
                return Ok(ok);
            }
            _ => return Ok(false),
        }
        Ok(true)
    }

    pub fn status(&mut self, status: &mut String, infrequent_update: bool) -> Result<()> {
        if infrequent_update {
            self.cached_status.clear();
            if self.running {
                let exposure = Instant::now() - self.exposure_start;
                writeln!(
                    &mut self.cached_status,
                    "Time since exposure start: {:.1}s",
                    exposure.as_secs_f64()
                )?;
            }
            if let Some(ref camera) = self.camera {
                for control in camera.controls() {
                    if control.id() == ControlId::ControlExposure {
                        writeln!(
                            self.cached_status,
                            "exposure = {} ({}-{} by {})",
                            control.get() / EXPOSURE_FACTOR,
                            control.min_value() / EXPOSURE_FACTOR,
                            control.max_value() / EXPOSURE_FACTOR,
                            control.step_value() / EXPOSURE_FACTOR,
                        )?;
                    } else if control.interesting() {
                        writeln!(self.cached_status, "{}", control)?;
                    }
                }
            }
        }
        if let Some(ref camera) = self.camera {
            writeln!(status, "{}", camera.name())?;
        }
        writeln!(
            status,
            "cross:{}|zoom:{}|bin:{}",
            self.image_display.cross, self.image_display.zoom, self.image_display.bin,
        )?;
        if self.running {
            writeln!(status, "close (currently running)")?;
        } else {
            writeln!(status, "open (currently paused)")?;
        }
        let is_live = self
            .camera
            .as_ref()
            .map_or(false, |camera| camera.use_live());
        if is_live {
            writeln!(status, "live (enabled)")?;
        } else {
            writeln!(status, "live (not live)")?;
        }
        writeln!(status, "save|save [n]")?;
        writeln!(status, "Last exposure: {:?}", self.exposure_time)?;
        if !self.solve_status.is_empty() {
            writeln!(status, "Platesolve: {}", self.solve_status)?;
        }
        if self.save > 0 {
            writeln!(status, "Saving: {}", self.save)?;
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

    fn obtain_image(&mut self) -> Result<bool> {
        let mut new_raw = false;
        if let Some(ref camera) = self.camera {
            if self.running {
                if let Some(image) = camera.try_get()? {
                    let now = Instant::now();
                    self.exposure_time = now - self.exposure_start;
                    self.exposure_start = now;
                    if self.save > 0 {
                        self.save -= 1;
                        self.save_png(&image)?;
                    }
                    self.image_display.set_raw(Arc::new(image))?;
                    new_raw = true;
                }
            }
        } else if self.image_display.raw().is_none() {
            self.image_display.set_raw(Arc::new(crate::read_png(
                "telescope.2019-10-5.21-42-57.png",
            )?))?;
            new_raw = true;
        }
        Ok(new_raw)
    }

    pub fn update(&mut self) -> Result<bool> {
        let mut redraw = false;
        let new_raw = self.obtain_image()?;
        if new_raw {
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
        while let Some(process_result) = self.processor.get()? {
            self.process_result = Some(process_result);
            redraw = true;
        }
        Ok(redraw)
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
        user_update: &UserUpdate,
        mount: Option<&mut MountAsync>,
    ) -> Result<()> {
        if let UserUpdate::SolveFinished(ra, dec) = *user_update {
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
        Ok(())
    }
}
