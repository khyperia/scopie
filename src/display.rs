use sdl2::event::Event;
use sdl2::init;
use sdl2::pixels::PixelFormatEnum;
use sdl2::rect::Rect;
use std::error::Error;
use std::sync::mpsc;

pub struct Image {
    data: Vec<u8>,
    width: u32,
    height: u32,
}

impl Image {
    pub fn new(data: Vec<u8>, width: u32, height: u32) -> Image {
        Image {
            data,
            width,
            height,
        }
    }
}

pub fn display(image_stream: &mpsc::Receiver<Image>) -> Result<(), Box<Error>> {
    let mut width = 400;
    let mut height = 400;
    let sdl = init()?;
    let video = sdl.video()?;
    let window = video.window("Scopie", width, height).resizable().build()?;
    let mut canvas = window.into_canvas().present_vsync().build()?;
    let creator = canvas.texture_creator();
    let mut texture = creator.create_texture_streaming(PixelFormatEnum::RGBX8888, width, height)?;
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
                texture = creator.create_texture_streaming(None, width, height)?;
            }
            texture.update(None, &image.data, image.width as usize * 4)?;
            //println!("frame");
        }

        let (output_width, output_height) = canvas.output_size()?;
        let scale = (f64::from(output_width) / f64::from(width))
            .min(f64::from(output_height) / f64::from(height));
        let dst_width = (f64::from(width) * scale).round() as u32;
        let dst_height = (f64::from(height) * scale).round() as u32;
        let dst = Rect::new(0, 0, dst_width, dst_height);
        canvas.copy(&texture, None, dst)?;
        canvas.present();
    }
}
