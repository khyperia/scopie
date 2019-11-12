use crate::{
    camera,
    mount_async::MountAsync,
    platesolve::platesolve,
    process,
    qhycamera::{ControlId, EXPOSURE_FACTOR},
    Result,
};
use dirs;
use khygl::{
    render_texture::TextureRenderer,
    texture::{CpuTexture, Texture},
    Rect,
};
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
    raw: Option<Arc<CpuTexture<u16>>>,
    texture: Option<Texture<u16>>,
    processor: process::Processor,
    process_result: Option<process::ProcessResult>,
    running: bool,
    zoom: bool,
    cross: bool,
    display_sigma: f64,
    save: usize,
    folder: String,
    solve_status: String,
    cached_status: String,
    exposure_start: Instant,
    exposure_time: Duration,
}

impl CameraDisplay {
    pub fn new(camera: Option<camera::Camera>) -> Self {
        Self {
            camera,
            raw: None,
            texture: None,
            processor: process::Processor::new(),
            process_result: None,
            running: false,
            zoom: false,
            cross: false,
            display_sigma: 3.0,
            save: 0,
            folder: String::new(),
            solve_status: String::new(),
            cached_status: String::new(),
            exposure_start: Instant::now(),
            exposure_time: Duration::new(0, 0),
        }
    }

    pub fn cmd(&mut self, command: &[&str], mount: Option<&mut MountAsync>) -> Result<bool> {
        match *command {
            ["cross"] => {
                self.cross = !self.cross;
            }
            ["zoom"] => {
                self.zoom = !self.zoom;
            }
            ["sigma", sigma] => {
                if let Ok(sigma) = sigma.parse() {
                    self.display_sigma = sigma;
                } else {
                    return Ok(false);
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
            ["save", "now"] if self.raw.is_some() => {
                if let Some(ref raw) = self.raw {
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
                if let Some(ref img) = self.raw {
                    let (ra, dec) = platesolve(img)?;
                    self.solve_status = format!("{} {}", ra.fmt_hours(), dec.fmt_degrees());
                    if let Some(mount) = mount {
                        mount.reset(ra, dec)?;
                    }
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
                if let Some(ref camera) = self.camera {
                    if let Ok(value) = value.parse() {
                        for control in camera.controls() {
                            if control.name().eq_ignore_ascii_case(name) {
                                control.set(value)?;
                            }
                        }
                    } else {
                        return Ok(false);
                    }
                }
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
        writeln!(status, "cross|zoom|median")?;
        if self.running {
            writeln!(status, "close (currently running)")?;
        } else {
            writeln!(status, "open (currently paused)")?;
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
            writeln!(status, "Folder: None")?;
        } else {
            writeln!(status, "Folder: {}", self.folder)?;
        }
        if let Some(ref process_result) = self.process_result {
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

    pub fn update(&mut self) -> Result<bool> {
        let mut redraw = false;
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
                    self.raw = Some(Arc::new(image));
                    new_raw = true;
                }
            }
        } else if self.raw.is_none() {
            self.raw = Some(Arc::new(crate::read_png(
                "telescope.2019-10-5.21-42-57.png",
            )?));
            new_raw = true;
        }
        if new_raw {
            if let Some(ref raw) = self.raw {
                let ok = self.processor.process(raw.clone())?;
                if !ok {
                    // TODO
                    // println!("Dropped frame");
                }
                let mut upload = false;
                let create = if let Some(ref texture) = self.texture {
                    texture.size != raw.size
                } else {
                    true
                };
                if create {
                    self.texture = Some({
                        let tex = Texture::new(raw.size)?;
                        tex.set_swizzle([gl::RED, gl::RED, gl::RED, gl::ONE])?;
                        tex
                    });
                    upload = true;
                }
                if upload {
                    if let Some(ref mut texture) = self.texture {
                        texture.upload(&raw)?;
                        redraw = true;
                    }
                }
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
        if let Some(ref texture) = self.texture {
            let src = if self.zoom {
                let zoom_size = 512.0;
                let src_x = texture.size.0 as f32 / 2.0 - zoom_size / 2.0;
                let src_y = texture.size.1 as f32 / 2.0 - zoom_size / 2.0;
                Rect::new(src_x, src_y, zoom_size, zoom_size)
            } else {
                Rect::new(0.0, 0.0, texture.size.0 as f32, texture.size.1 as f32)
            };

            let scale = ((pos.width as f32) / (src.width as f32))
                .min((pos.height as f32) / (src.height as f32));
            let dst_width = ((src.width as f32) * scale).round();
            let dst_height = ((src.height as f32) * scale).round();
            let dst = Rect::new(
                (pos.x + pos.width) as f32 - dst_width,
                pos.y as f32,
                dst_width,
                dst_height,
            );
            let render = displayer
                .render(texture, screen_size)
                .src(src)
                .dst(dst.clone());
            if let Some(ref process_result) = self.process_result {
                let (scale, offset) = process_result.get_scale_offset(self.display_sigma);
                render.scale_offset((scale as f32, offset as f32))
            } else {
                render
            }
            .go()?;
            if self.cross {
                let half_x = dst.x + (dst.width / 2.0);
                let half_y = dst.y + (dst.height / 2.0);
                displayer.line_x(
                    dst.x as usize,
                    dst.right() as usize,
                    half_y as usize,
                    [255.0, 0.0, 0.0, 255.0],
                    screen_size,
                )?;
                displayer.line_y(
                    half_x as usize,
                    dst.y as usize,
                    dst.bottom() as usize,
                    [255.0, 0.0, 0.0, 255.0],
                    screen_size,
                )?;
            }
        }
        Ok(())
    }
}
