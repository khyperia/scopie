use crate::{
    camera_async,
    image_display::ImageDisplay,
    mount_async, mount_display,
    platesolve::platesolve,
    process,
    qhycamera::{ControlId, EXPOSURE_FACTOR},
    Key, Result, SendUserUpdate, UserUpdate,
};
use khygl::{render_texture::TextureRenderer, texture::CpuTexture, Rect};
use std::{
    collections::HashMap, convert::TryInto, fmt::Write, fs::create_dir_all, path::PathBuf,
    time::Instant,
};

// TODO: make saving images async
pub struct CameraDisplay {
    camera: Option<camera_async::CameraAsync>,
    send_user_update: SendUserUpdate,
    image_display: ImageDisplay,
    processor: process::Processor,
    roi_thing: ROIThing,
    display_interesting: bool,
    save: usize,
    folder: String,
    solve_status: String,
    cached_status: String,
}

impl CameraDisplay {
    pub fn new(send_user_update: SendUserUpdate) -> Self {
        if let Ok(img) = crate::read_png("telescope.2019-11-21.19-39-54.png") {
            send_user_update
                .send_event(UserUpdate::CameraData(std::sync::Arc::new(img.into())))
                .unwrap();
        }
        Self {
            camera: Some(camera_async::CameraAsync::new(send_user_update.clone())),
            send_user_update: send_user_update.clone(),
            image_display: ImageDisplay::new(),
            processor: process::Processor::new(send_user_update),
            roi_thing: ROIThing::new(),
            display_interesting: true,
            save: 0,
            folder: String::new(),
            solve_status: String::new(),
            cached_status: String::new(),
        }
    }

    pub fn cmd(&mut self, command: &[&str]) -> Result<bool> {
        if self.processor.cmd(command)? {
            return Ok(true);
        }
        match *command {
            ["cross"] => {
                self.image_display.cross = !self.image_display.cross;
            }
            ["bin"] => {
                self.image_display.bin = !self.image_display.bin;
            }
            ["interesting"] => {
                self.display_interesting = !self.display_interesting;
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
                    self.save_png(&raw.image)?;
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
                    platesolve(&raw.image, self.send_user_update.clone())?;
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
        op: impl FnOnce(&camera_async::CameraAsync) -> std::result::Result<(), ()>,
    ) {
        let ok = match self.camera {
            Some(ref mut camera) => op(camera).is_ok(),
            None => true,
        };
        if !ok {
            self.camera = None;
        }
    }

    pub fn update(&mut self) -> bool {
        self.roi_thing.update()
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
            "cross:{}|bin:{}",
            self.image_display.cross, self.image_display.bin,
        )?;
        writeln!(status, "interesting: {}", self.display_interesting)?;
        writeln!(status, "save|save [n]: {}", self.save)?;
        if !self.solve_status.is_empty() {
            writeln!(status, "solve: {}", self.solve_status)?;
        }
        if self.folder.is_empty() {
            writeln!(status, "folder: None")?;
        } else {
            writeln!(status, "folder: {}", self.folder)?;
        }
        self.processor.status(status)?;
        write!(status, "{}", self.cached_status)?;
        Ok(())
    }

    fn save_png(&self, data: &CpuTexture<u16>) -> Result<()> {
        let tm = time::OffsetDateTime::now_local();
        let dirname = tm.format("%Y_%m_%d");
        let filename = tm.format("telescope.%Y-%m-%d.%H-%M-%S.png");
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
        if let Some(scale_offset) = self.processor.get_scale_offset() {
            self.image_display.scale_offset = (scale_offset.0 as f32, scale_offset.1 as f32);
        };
        self.roi_thing.update();
        self.image_display
            .draw(pos, displayer, screen_size, &self.roi_thing)?;
        Ok(())
    }

    pub fn user_update(
        &mut self,
        user_update: UserUpdate,
        mount: &mut Option<mount_display::MountDisplay>,
    ) -> Result<()> {
        match user_update {
            UserUpdate::SolveFinished(ra, dec) => {
                self.solve_status = format!("{} {}", ra.fmt_hours(), dec.fmt_degrees());
                if let Some(smount) = mount {
                    let old_mount_radec = smount.mount.data.ra_dec;
                    let delta_ra = old_mount_radec.0 - ra;
                    let delta_dec = old_mount_radec.1 - dec;
                    match smount.mount.add_real_to_mount_delta(delta_ra, delta_dec) {
                        Ok(()) => (),
                        Err(mount_async::MountSendError {}) => {
                            *mount = None;
                        }
                    }
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
                    self.save_png(&image.image)?;
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
                    //println!("Dropped processing frame");
                }
            }
            UserUpdate::ProcessResult(process_result) => self.processor.user_update(process_result),
            user_update => {
                if let Some(ref mut camera) = self.camera {
                    camera.user_update(user_update);
                }
            }
        }
        Ok(())
    }

    #[allow(clippy::float_cmp)]
    pub fn key_down(&mut self, key: Key) {
        if key == Key::G {
            self.roi_thing.update();
            if self.roi_thing.zoom == 1.0 {
                self.camera_op(|c| c.set_roi(None));
            } else if let Some(raw) = self.image_display.raw() {
                let roi = ROIThing::clamp(
                    self.roi_thing.get_roi_unclamped(&raw.original),
                    &raw.original,
                );
                self.camera_op(move |c| c.set_roi(Some(roi)));
            }
        }
        self.roi_thing.key_down(key);
    }

    pub fn key_up(&mut self, key: Key) {
        self.roi_thing.key_up(key);
    }
}

pub struct ROIThing {
    pressed_keys: HashMap<Key, Instant>,
    position: (f64, f64),
    zoom: f64,
}

impl ROIThing {
    pub fn new() -> Self {
        Self {
            pressed_keys: HashMap::new(),
            position: (0.5, 0.5),
            zoom: 1.0,
        }
    }

    fn tf(&self, point: (f64, f64)) -> (f64, f64) {
        (
            (point.0 - 0.5) * self.zoom + self.position.0,
            (point.1 - 0.5) * self.zoom + self.position.1,
        )
    }

    fn tf_space(&self, point: (f64, f64), space: &Rect<usize>) -> (isize, isize) {
        let point = self.tf(point);
        (
            (point.0 * space.width as f64 + space.x as f64) as isize,
            (point.1 * space.height as f64 + space.y as f64) as isize,
        )
    }

    fn clamp1(val: isize, low: usize, high: usize) -> usize {
        if val < low as isize {
            low
        } else if val >= high as isize {
            high - 1
        } else {
            val as usize
        }
    }

    pub fn clamp(area: Rect<isize>, clamp: &Rect<usize>) -> Rect<usize> {
        let clampedx = Self::clamp1(area.x, clamp.x, clamp.right() - 1);
        let clampedy = Self::clamp1(area.y, clamp.y, clamp.bottom() - 1);
        // result.right() < clamp.right()
        // result.x + result.width < clamp.right()
        // result.width < clamp.right() - result.x
        Rect::new(
            clampedx,
            clampedy,
            Self::clamp1(area.width, 1, clamp.right() - clampedx),
            Self::clamp1(area.height, 1, clamp.bottom() - clampedy),
        )
    }

    pub fn get_roi_unclamped(&self, reference: &Rect<usize>) -> Rect<isize> {
        let zerozero = self.tf_space((0.0, 0.0), reference);
        let oneone = self.tf_space((1.0, 1.0), reference);
        let size = (oneone.0 - zerozero.0, oneone.1 - zerozero.1);
        Rect::new(zerozero.0, zerozero.1, size.0, size.1)
    }

    #[allow(clippy::float_cmp)]
    pub fn update(&mut self) -> bool {
        let mut any_key = false;
        for (key, time) in &mut self.pressed_keys {
            let now = Instant::now();
            let dt = (now - *time).as_secs_f64();
            let mut handled_key = true;
            match *key {
                Key::D => self.position.0 += self.zoom * dt * 0.25,
                Key::A => self.position.0 -= self.zoom * dt * 0.25,
                Key::S => self.position.1 += self.zoom * dt * 0.25,
                Key::W => self.position.1 -= self.zoom * dt * 0.25,
                Key::R => self.zoom *= (-dt).exp2(),
                Key::F => {
                    self.zoom = (self.zoom * dt.exp2()).min(1.0);
                    if self.zoom == 1.0 {
                        self.position = (0.5, 0.5);
                    }
                }
                _ => handled_key = false,
            }
            any_key |= handled_key;
            *time = now;
        }
        any_key
    }

    pub fn key_down(&mut self, key: Key) {
        self.pressed_keys.entry(key).or_insert_with(Instant::now);
    }

    pub fn key_up(&mut self, key: Key) {
        self.update();
        self.pressed_keys.remove(&key);
    }
}
