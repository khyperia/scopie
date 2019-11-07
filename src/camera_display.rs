use crate::{camera, mount::Mount, platesolve::platesolve, process::adjust_image, Result};
use dirs;
use khygl::{
    render_texture::TextureRendererU8,
    texture::{CpuTexture, Texture},
    Rect,
};
use std::{
    convert::TryInto,
    fmt::Write,
    fs::create_dir_all,
    path::PathBuf,
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
    display_sigma: f64,
    save: usize,
    last_update: Instant,
    folder: String,
    process_status: String,
    solve_status: String,
    cached_status: String,
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
            display_sigma: 3.0,
            save: 0,
            last_update: Instant::now(),
            folder: String::new(),
            process_status: String::new(),
            solve_status: String::new(),
            cached_status: String::new(),
            exposure_start: Instant::now(),
            exposure_time: Duration::new(0, 0),
            process_time: Duration::new(0, 0),
        }
    }

    pub fn cmd(&mut self, command: &[&str], mount: Option<&mut Mount>) -> Result<bool> {
        match *command {
            ["cross"] => self.cross = !self.cross,
            ["zoom"] => self.zoom = !self.zoom,
            ["median"] => {
                self.median = !self.median;
                self.processed = None;
            }
            ["sigma", sigma] => {
                if let Ok(sigma) = sigma.parse() {
                    self.display_sigma = sigma;
                    self.processed = None;
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
                        mount.reset_ra(ra)?;
                        mount.reset_dec(dec)?;
                    }
                } else {
                    return Ok(false);
                }
            }
            [name, value] => {
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

    pub fn status(&mut self, status: &mut String) -> Result<()> {
        let now = Instant::now();
        if (now - self.last_update).as_secs() > 0 {
            self.last_update += Duration::from_secs(1);
            self.cached_status.clear();
            if let Some(ref camera) = self.camera {
                for control in camera.controls() {
                    if control.interesting() {
                        let asdf = Instant::now();
                        write!(self.cached_status, "{} ", control)?;
                        writeln!(self.cached_status, "{:?}", Instant::now() - asdf)?;
                    }
                }
            }
            writeln!(
                self.cached_status,
                "Control get time: {:?}",
                Instant::now() - now
            )?;
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
        if self.running {
            let exposure = now - self.exposure_start;
            writeln!(
                status,
                "Time since exposure start: {}.{:03}",
                exposure.as_secs(),
                exposure.subsec_millis(),
            )?;
        }
        writeln!(status, "Last exposure: {:?}", self.exposure_time,)?;
        writeln!(status, "Processing time: {:?}", self.process_time,)?;
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
        write!(status, "{}", self.process_status)?;
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
        displayer_u8: &TextureRendererU8,
        screen_size: (f32, f32),
    ) -> Result<()> {
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
                    self.raw = Some(image);
                }
            }
        } else if self.raw.is_none() {
            self.raw = Some(crate::read_png("telescope.2019-10-5.21-42-57.png")?);
        }
        let mut upload = false;
        if self.processed.is_none() {
            if let Some(ref raw) = self.raw {
                let now = Instant::now();
                self.process_status.clear();
                self.processed = Some(adjust_image(
                    raw,
                    self.display_sigma,
                    self.median,
                    &mut self.process_status,
                ));
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
