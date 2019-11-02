use crate::{camera, process::adjust_image, Result};
use khygl::{
    render_texture::TextureRendererU8,
    texture::{CpuTexture, Texture},
    Rect,
};
use std::{
    convert::TryInto,
    fmt::Write,
    time::{Duration, Instant},
};

pub struct CameraDisplay {
    camera: Option<camera::Camera>,
    raw: Option<CpuTexture<u16>>,
    processed: Option<CpuTexture<[u8; 4]>>,
    texture: Option<Texture<[u8; 4]>>,
    running: bool,
    zoom: bool,
    cross: bool,
    median: bool,
    save: usize,
    last_update: Instant,
    cached_status: String,
    status: String,
    exposure_start: Instant,
    exposure_time: Duration,
    process_time: Duration,
}

impl CameraDisplay {
    pub fn new(camera: Option<camera::Camera>) -> Self {
        Self {
            camera,
            raw: None,
            processed: None,
            texture: None,
            running: false,
            zoom: false,
            cross: false,
            median: false,
            save: 0,
            last_update: Instant::now(),
            cached_status: String::new(),
            status: String::new(),
            exposure_start: Instant::now(),
            exposure_time: Duration::new(0, 0),
            process_time: Duration::new(0, 0),
        }
    }

    pub fn cmd(&mut self, command: &[&str]) -> Result<bool> {
        match command {
            ["cross"] => self.cross = !self.cross,
            ["zoom"] => self.zoom = !self.zoom,
            ["median"] => {
                self.median = !self.median;
                self.processed = None;
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
            ["save"] => {
                self.save += 1;
            }
            ["save", n] => {
                if let Ok(n) = n.parse::<isize>() {
                    self.save = (self.save as isize + n).try_into().unwrap_or(0);
                } else {
                    return Ok(false);
                }
            }
            &[name, value] => {
                if let Some(ref camera) = self.camera {
                    if let Ok(value) = value.parse() {
                        for control in camera.controls() {
                            if control.name() == name {
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

    pub fn status(&mut self) -> Result<&str> {
        let now = Instant::now();
        if (now - self.last_update).as_secs() > 0 {
            self.last_update += Duration::from_secs(1);
            self.cached_status.clear();
            if let Some(ref camera) = self.camera {
                for control in camera.controls() {
                    writeln!(self.cached_status, "{}", control)?;
                }
            }
        }
        self.status.clear();
        if self.running {
            let exposure = now - self.exposure_start;
            writeln!(
                self.status,
                "Time since exposure start: {}.{:03}",
                exposure.as_secs(),
                exposure.subsec_millis(),
            )?;
        }
        writeln!(
            self.status,
            "Last exposure: {}.{:03}",
            self.exposure_time.as_secs(),
            self.exposure_time.subsec_millis(),
        )?;
        writeln!(
            self.status,
            "Processing time: {}.{:03}",
            self.process_time.as_secs(),
            self.process_time.subsec_millis(),
        )?;
        if self.save > 0 {
            writeln!(self.status, "Saving: {}", self.save)?;
        }
        write!(self.status, "{}", self.cached_status)?;
        Ok(&self.status)
    }

    fn save_png(data: &CpuTexture<u16>) {
        let tm = time::now();
        let filename = format!(
            "telescope.{}-{:02}-{:02}.{:02}-{:02}-{:02}.png",
            tm.tm_year + 1900,
            tm.tm_mon + 1,
            tm.tm_mday,
            tm.tm_hour,
            tm.tm_min,
            tm.tm_sec
        );
        crate::write_png(filename, data);
    }

    pub fn draw(
        &mut self,
        pos: Rect<usize>,
        displayer_u8: &TextureRendererU8,
        screen_size: (f32, f32),
    ) -> Result<()> {
        if let Some(ref camera) = self.camera {
            if let Some(image) = camera.try_get()? {
                let now = Instant::now();
                self.exposure_time = now - self.exposure_start;
                self.exposure_start = now;
                if self.save > 0 {
                    self.save -= 1;
                    Self::save_png(&image);
                }
                self.raw = Some(image);
            }
        } else if self.raw.is_none() {
            self.raw = Some(crate::read_png("telescope.2019-10-5.21-9-8.png"));
        }
        let mut upload = false;
        if self.processed.is_none() {
            if let Some(ref raw) = self.raw {
                let now = Instant::now();
                self.processed = Some(adjust_image(raw, self.median));
                self.process_time = Instant::now() - now;
                upload = true;
            }
        }
        if let Some(ref processed) = self.processed {
            let create = if let Some(ref texture) = self.texture {
                texture.size != processed.size
            } else {
                true
            };
            if create {
                self.texture = Some(Texture::new(processed.size)?);
                upload = true;
            }
        }
        if upload {
            if let Some(ref processed) = self.processed {
                if let Some(ref mut texture) = self.texture {
                    texture.upload(&processed)?;
                }
            }
        }
        if let Some(ref texture) = self.texture {
            let scale = ((pos.width as f32) / (texture.size.0 as f32))
                .min((pos.height as f32) / (texture.size.1 as f32));
            let dst_width = ((texture.size.0 as f32) * scale).round();
            let dst_height = ((texture.size.1 as f32) * scale).round();
            let dst = Rect::new(pos.x as f32, pos.y as f32, dst_width, dst_height);
            let src = if self.zoom {
                let zoom_size = 512.0;
                let src_x = texture.size.0 as f32 / 2.0 - zoom_size / 2.0;
                let src_y = texture.size.1 as f32 / 2.0 - zoom_size / 2.0;
                Some(Rect::new(src_x, src_y, zoom_size, zoom_size))
            } else {
                None
            };
            displayer_u8.render(texture, src, dst.clone(), None, screen_size)?;
            if self.cross {
                let half_x = dst.x + (dst.width / 2.0);
                let half_y = dst.y + (dst.height / 2.0);
                displayer_u8.line_x(
                    dst.x as usize,
                    dst.right() as usize,
                    half_y as usize,
                    [255.0, 0.0, 0.0, 255.0],
                    screen_size,
                )?;
                displayer_u8.line_y(
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