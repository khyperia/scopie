use crate::{camera, process::adjust_image, Result};
use khygl::{
    render_texture::TextureRenderer,
    render_texture::TextureRendererKindU8,
    texture::{CpuTexture, Texture},
    Rect,
};
use std::{
    fmt::Write,
    time::{Duration, Instant},
};

pub struct CameraDisplay {
    camera: camera::Camera,
    raw: Option<CpuTexture<u16>>,
    processed: Option<CpuTexture<[u8; 4]>>,
    texture: Option<Texture<[u8; 4]>>,
    running: bool,
    zoom: bool,
    cross: bool,
    last_update: Instant,
    cached_status: String,
    status: String,
    exposure_start: Instant,
    exposure_time: Duration,
    process_time: Duration,
}

impl CameraDisplay {
    pub fn new(camera: camera::Camera) -> Self {
        Self {
            camera,
            raw: None,
            processed: None,
            texture: None,
            running: false,
            zoom: false,
            cross: false,
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
            ["open"] if !self.running => {
                self.camera.start()?;
                self.running = true;
                self.exposure_start = Instant::now();
            }
            ["close"] if self.running => {
                self.camera.stop()?;
                self.running = false;
            }
            &[name] => {
                for control in self.camera.controls() {
                    if control.name() == name {
                        println!("{}", control)
                    }
                }
            }
            &[name, value] => {
                if let Ok(value) = value.parse() {
                    for control in self.camera.controls() {
                        if control.name() == name {
                            control.set(value)?;
                            println!("{}", control)
                        }
                    }
                } else {
                    return Ok(false);
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
            for control in self.camera.controls() {
                writeln!(self.cached_status, "{}", control)?;
            }
        }
        self.status.clear();
        if self.running {
            let exposure = now - self.exposure_start;
            writeln!(
                self.status,
                "Time since exposure start: {}.{:03}",
                exposure.as_secs(),
                exposure.subsec_millis()
            )?;
        }
        writeln!(
            self.status,
            "Last exposure: {}.{:03}",
            self.exposure_time.as_secs(),
            self.exposure_time.subsec_millis()
        )?;
        writeln!(
            self.status,
            "Processing time: {}.{:03}",
            self.process_time.as_secs(),
            self.process_time.subsec_millis()
        )?;
        write!(self.status, "{}", self.cached_status)?;
        Ok(&self.status)
    }

    pub fn draw(
        &mut self,
        pos: Rect<usize>,
        displayer_u8: &TextureRenderer<TextureRendererKindU8>,
        screen_size: (f32, f32),
    ) -> Result<()> {
        if let Some(image) = self.camera.try_get()? {
            let now = Instant::now();
            self.exposure_time = now - self.exposure_start;
            self.exposure_start = now;
            self.raw = Some(image);
        }
        let mut upload = false;
        if self.processed.is_none() {
            if let Some(ref raw) = self.raw {
                let now = Instant::now();
                self.processed = Some(adjust_image(raw));
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
            //let (output_width, output_height) = canvas.output_size()?;
            let scale = ((pos.width as f32) / (texture.size.0 as f32))
                .min((pos.height as f32) / (texture.size.1 as f32));
            let dst_width = ((texture.size.0 as f32) * scale).round();
            let dst_height = ((texture.size.1 as f32) * scale).round();
            let dst = Rect::new(pos.x as f32, pos.y as f32, dst_width, dst_height);
            let src = if self.zoom {
                let zoom_size = 100.0;
                let src_x = texture.size.0 as f32 / 2.0 - zoom_size / 2.0;
                let src_y = texture.size.1 as f32 / 2.0 - zoom_size / 2.0;
                Some(Rect::new(src_x, src_y, zoom_size, zoom_size))
            } else {
                None
            };
            displayer_u8.render(texture, src, dst, screen_size)?;
            // TODO: cross
            // if self.cross {
            //     let half_x = dst.x() + (dst.width() / 2) as i32;
            //     let half_y = dst.y() + (dst.height() / 2) as i32;
            //     canvas.set_draw_color(Color::RGB(255, 0, 0));
            //     canvas
            //         .draw_line(
            //             Point::new(dst.left(), half_y),
            //             Point::new(dst.right(), half_y),
            //         )
            //         .map_err(failure::err_msg)?;
            //     canvas
            //         .draw_line(
            //             Point::new(half_x, dst.top()),
            //             Point::new(half_x, dst.bottom()),
            //         )
            //         .map_err(failure::err_msg)?;
            // }
        }
        Ok(())
    }
}
