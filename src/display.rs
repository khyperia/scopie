use crate::camera::Camera;
use crate::Image;
use crate::Result;
use sdl2::event::Event;
use sdl2::init;
use sdl2::pixels::Color;
use sdl2::pixels::PixelFormatEnum;
use sdl2::rect::Rect;
use sdl2::render::{Texture, TextureCreator};
use std::sync::mpsc;

// let image = match camera.try_get()? {
//     Some(x) => x,
//     None => return Ok(()),
// };
fn update_tex<'a, T>(
    image: &Image<u8>,
    creator: &'a TextureCreator<T>,
    tex: &mut Texture<'a>,
) -> Result<()> {
    let tex_info = tex.query();
    if tex_info.width as usize != image.width || tex_info.height as usize != image.height {
        *tex = creator.create_texture_streaming(
            PixelFormatEnum::RGBX8888,
            image.width as u32,
            image.height as u32,
        )?;
    }
    tex.update(None, &image.data, image.width as usize * 4)?;
    Ok(())
}

pub fn display(image_stream: &mpsc::Receiver<Image<u8>>) -> Result<()> {
    let mut width = 400;
    let mut height = 400;
    let sdl = init()?;
    let video = sdl.video()?;
    let window = video
        .window("Scopie", width as u32, height as u32)
        .resizable()
        .build()?;
    let mut canvas = window.into_canvas().present_vsync().build()?;
    let creator = canvas.texture_creator();
    let mut texture =
        creator.create_texture_streaming(PixelFormatEnum::RGBX8888, width as u32, height as u32)?;
    let mut event_pump = sdl.event_pump()?;

    loop {
        while let Some(event) = event_pump.poll_event() {
            if let Event::Quit { .. } = event {
                return Ok(());
            }
        }

        loop {
            let image = match image_stream.try_recv() {
                Ok(image) => image,
                Err(mpsc::TryRecvError::Empty) => break,
                Err(mpsc::TryRecvError::Disconnected) => return Ok(()),
            };
            if width != image.width || height != image.height {
                width = image.width;
                height = image.height;
                texture = creator.create_texture_streaming(
                    PixelFormatEnum::RGBX8888,
                    width as u32,
                    height as u32,
                )?;
            }
            texture.update(None, &image.data, image.width as usize * 4)?;
        }

        let (output_width, output_height) = canvas.output_size()?;
        let scale = (output_width as f64 / width as f64).min(output_height as f64 / height as f64);
        let dst_width = (width as f64 * scale).round() as u32;
        let dst_height = (height as f64 * scale).round() as u32;
        let dst = Rect::new(0, 0, dst_width, dst_height);
        canvas.set_draw_color(Color::RGB(0, 0, 0));
        canvas.clear();
        canvas.copy(&texture, None, dst)?;
        canvas.present();
    }
}
