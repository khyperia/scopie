#[macro_use]
extern crate lazy_static;
extern crate regex;
extern crate sdl2;
extern crate serialport;
extern crate time;

mod camera;
mod camera_display;
mod dms;
mod mount;
mod mount_display;
mod process;
mod qhycamera;

use camera_display::CameraDisplay;
use mount_display::MountDisplay;
use sdl2::{
    event::Event,
    init,
    pixels::Color,
    render::{TextureCreator, WindowCanvas},
    video::WindowContext,
    EventPump,
};
use std::error::Error;

type Result<T> = std::result::Result<T, Box<dyn Error>>;

pub struct Image<T> {
    data: Vec<T>,
    width: usize,
    height: usize,
}

impl<T> Image<T> {
    pub fn new(data: Vec<T>, width: usize, height: usize) -> Self {
        Self {
            data,
            width,
            height,
        }
    }
}

fn display(camera: Option<camera::Camera>, mount: Option<mount::Mount>) -> Result<()> {
    let width = 400;
    let height = 400;
    let sdl = init()?;
    let video = sdl.video()?;
    let window = video
        .window("Scopie", width as u32, height as u32)
        .resizable()
        .build()?;
    let mut canvas = window.into_canvas().present_vsync().build()?;
    let creator = canvas.texture_creator();
    let mut event_pump = sdl.event_pump()?;
    let mut camera_display = camera.map(CameraDisplay::new);
    let mut mount_display = mount.map(MountDisplay::new);
    loop {
        while let Some(event) = event_pump.poll_event() {
            match event {
                Event::Quit { .. } => return Ok(()),
                Event::Window { win_event, .. } => match win_event {
                    // WindowEvent::Resized(new_width, new_height)
                    //     if new_width > 0 && new_height > 0 =>
                    // {
                    //     self.width = new_width as usize;
                    //     self.height = new_height as usize;
                    // }
                    _ => (),
                },
                _ => (),
            }
        }

        canvas.set_draw_color(Color::RGB(0, 0, 0));
        canvas.clear();
        if let Some(ref mut camera_display) = camera_display {
            camera_display.draw(&mut canvas, &creator)?;
        }
        if let Some(ref mut mount_display) = mount_display {
            mount_display.draw(&canvas)?;
        }
        canvas.present();
    }
}

fn main() {
    let live = false;
    let camera = match camera::autoconnect(live) {
        Ok(ok) => Some(ok),
        Err(err) => {
            println!("Error connecting to camera: {}", err);
            None
        }
    };
    let mount = match mount::autoconnect() {
        Ok(ok) => Some(ok),
        Err(err) => {
            println!("Error connecting to mount: {}", err);
            None
        }
    };
    match display(camera, mount) {
        Ok(ok) => ok,
        Err(err) => println!("Error: {}", err),
    }
}
