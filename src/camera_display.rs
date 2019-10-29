use crate::{camera, process::adjust_image, Image, Result};
use sdl2::{
    pixels::{Color, PixelFormatEnum},
    rect::{Point, Rect},
    render::{Texture, TextureCreator, WindowCanvas},
    video::WindowContext,
};
use std::{
    fmt::Write,
    time::{Duration, Instant},
};

pub struct CameraDisplay<'a> {
    camera: camera::Camera,
    raw: Option<Image<u16>>,
    processed: Option<Image<u8>>,
    texture: Option<Texture<'a>>,
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

impl<'a> CameraDisplay<'a> {
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
        canvas: &mut WindowCanvas,
        creator: &'a TextureCreator<WindowContext>,
        position: Rect,
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
                let query = texture.query();
                query.width as usize != processed.width || query.height as usize != processed.height
            } else {
                true
            };
            if create {
                self.texture = Some(creator.create_texture_streaming(
                    PixelFormatEnum::RGBX8888,
                    processed.width as u32,
                    processed.height as u32,
                )?);
                upload = true;
            }
        }
        if upload {
            if let Some(ref processed) = self.processed {
                if let Some(ref mut texture) = self.texture {
                    texture.update(None, &processed.data, processed.width as usize * 4)?;
                }
            }
        }
        if let Some(ref texture) = self.texture {
            let query = texture.query();
            //let (output_width, output_height) = canvas.output_size()?;
            let scale = (f64::from(position.width()) / f64::from(query.width))
                .min(f64::from(position.height()) / f64::from(query.height));
            let dst_width = (f64::from(query.width) * scale).round() as u32;
            let dst_height = (f64::from(query.height) * scale).round() as u32;
            let dst = Rect::new(position.x(), position.y(), dst_width, dst_height);
            let src = if self.zoom {
                let zoom_size = 100;
                let src_x = (query.width / 2) as i32 - (zoom_size / 2) as i32;
                let src_y = (query.height / 2) as i32 - (zoom_size / 2) as i32;
                Some(Rect::new(src_x, src_y, zoom_size, zoom_size))
            } else {
                None
            };
            canvas.copy(&texture, src, dst).map_err(failure::err_msg)?;
            if self.cross {
                let half_x = dst.x() + (dst.width() / 2) as i32;
                let half_y = dst.y() + (dst.height() / 2) as i32;
                canvas.set_draw_color(Color::RGB(255, 0, 0));
                canvas
                    .draw_line(
                        Point::new(dst.left(), half_y),
                        Point::new(dst.right(), half_y),
                    )
                    .map_err(failure::err_msg)?;
                canvas
                    .draw_line(
                        Point::new(half_x, dst.top()),
                        Point::new(half_x, dst.bottom()),
                    )
                    .map_err(failure::err_msg)?;
            }
        }
        Ok(())
    }
}
