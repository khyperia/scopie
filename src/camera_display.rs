use crate::{camera, process::adjust_image, Image, Result};
use sdl2::{
    pixels::{Color, PixelFormatEnum},
    rect::{Point, Rect},
    render::{Texture, TextureCreator, WindowCanvas},
    video::WindowContext,
};

pub struct CameraDisplay<'a> {
    camera: camera::Camera,
    raw: Option<Image<u16>>,
    proc: Option<Image<u8>>,
    texture: Option<Texture<'a>>,
    running: bool,
    zoom: bool,
    cross: bool,
}

impl<'a> CameraDisplay<'a> {
    pub fn new(camera: camera::Camera) -> Self {
        Self {
            camera,
            raw: None,
            proc: None,
            texture: None,
            running: false,
            zoom: false,
            cross: false,
        }
    }

    pub fn cmd(&mut self, command: &[&str]) -> Result<bool> {
        match command {
            ["cross"] => self.cross = !self.cross,
            ["zoom"] => self.zoom = !self.zoom,
            ["open"] if !self.running => {
                self.camera.start()?;
                self.running = true;
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

    pub fn draw(
        &mut self,
        canvas: &mut WindowCanvas,
        creator: &'a TextureCreator<WindowContext>,
    ) -> Result<()> {
        if let Some(image) = self.camera.try_get()? {
            self.raw = Some(image);
        }
        let mut upload = false;
        if self.proc.is_none() {
            if let Some(ref raw) = self.raw {
                self.proc = Some(adjust_image(raw));
                upload = true;
            }
        }
        if let Some(ref proc) = self.proc {
            let create = if let Some(ref texture) = self.texture {
                let query = texture.query();
                query.width as usize != proc.width || query.height as usize != proc.height
            } else {
                true
            };
            if create {
                self.texture = Some(creator.create_texture_streaming(
                    PixelFormatEnum::RGBX8888,
                    proc.width as u32,
                    proc.height as u32,
                )?);
                upload = true;
            }
        }
        if upload {
            if let Some(ref proc) = self.proc {
                if let Some(ref mut texture) = self.texture {
                    texture.update(None, &proc.data, proc.width as usize * 4)?;
                }
            }
        }
        if let Some(ref texture) = self.texture {
            let query = texture.query();
            let (output_width, output_height) = canvas.output_size()?;
            let scale = (output_width as f64 / query.width as f64)
                .min(output_height as f64 / query.height as f64);
            let dst_width = (query.width as f64 * scale).round() as u32;
            let dst_height = (query.height as f64 * scale).round() as u32;
            let dst = Rect::new(0, 0, dst_width, dst_height);
            let src = if self.zoom {
                let zoom_size = 100;
                let src_x = (query.width / 2) as i32 - (zoom_size / 2) as i32;
                let src_y = (query.height / 2) as i32 - (zoom_size / 2) as i32;
                Some(Rect::new(src_x, src_y, zoom_size, zoom_size))
            } else {
                None
            };
            canvas.copy(&texture, src, dst)?;
            if self.cross {
                let half_x = dst.x() + (dst.width() / 2) as i32;
                let half_y = dst.y() + (dst.height() / 2) as i32;
                canvas.set_draw_color(Color::RGB(255, 0, 0));
                canvas.draw_line(
                    Point::new(dst.left(), half_y),
                    Point::new(dst.right(), half_y),
                )?;
                canvas.draw_line(
                    Point::new(half_x, dst.top()),
                    Point::new(half_x, dst.bottom()),
                )?;
            }
        }
        Ok(())
    }
}
