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
    keyboard::Keycode,
    pixels::Color,
    rect::{Point, Rect},
    render::{TextureCreator, WindowCanvas},
    ttf,
    video::WindowContext,
};
use std::{convert::TryInto, path::Path};

type Result<T> = std::result::Result<T, failure::Error>;

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

/*
fn format_duration(duration: Duration) -> String {
    format!("{}.{:03}", duration.as_secs(), duration.subsec_millis())
}
*/

fn find_font() -> Result<&'static Path> {
    let locations: [&'static Path; 6] = [
        "/usr/share/fonts/TTF/FiraMono-Regular.ttf".as_ref(),
        "/usr/share/fonts/TTF/FiraSans-Regular.ttf".as_ref(),
        "C:\\Windows\\Fonts\\arial.ttf".as_ref(),
        "/usr/share/fonts/TTF/DejaVuSans.ttf".as_ref(),
        "/usr/share/fonts/TTF/LiberationSans-Regular.ttf".as_ref(),
        "/Library/Fonts/Andale Mono.ttf".as_ref(),
    ];
    for &location in &locations {
        if location.exists() {
            return Ok(location);
        }
    }
    Err(failure::err_msg("No font found"))
}

fn render_line(
    font: &ttf::Font,
    creator: &TextureCreator<WindowContext>,
    canvas: &mut WindowCanvas,
    color: Color,
    pos: Point,
    text: &str,
) -> Result<Rect> {
    if text.is_empty() {
        Ok(Rect::new(pos.x(), pos.y(), 0, 0))
    } else {
        let rendered = font.render(text).solid(color)?;
        let width = rendered.width();
        let height = rendered.height();
        let tex = creator.create_texture_from_surface(rendered)?;
        let dest = Rect::new(pos.x(), pos.y(), width, height);
        canvas.copy(&tex, None, dest).map_err(failure::err_msg)?;
        Ok(dest)
    }
}

fn render_block(
    font: &ttf::Font,
    creator: &TextureCreator<WindowContext>,
    canvas: &mut WindowCanvas,
    color: Color,
    pos: Point,
    text: &str,
) -> Result<Rect> {
    let spacing = font.recommended_line_spacing();

    let mut current_y = pos.y();
    let mut max_coords = (pos.x(), pos.y());

    for line in text.lines() {
        let line_pos = Point::new(pos.x(), current_y);
        let line_rect = render_line(font, creator, canvas, color, line_pos, line)?;
        max_coords = (
            max_coords.0.max(line_rect.right()),
            max_coords.1.max(line_rect.bottom()),
        );
        current_y += spacing;
    }
    Ok(Rect::new(
        pos.x(),
        pos.y(),
        (max_coords.0 - pos.x()) as u32,
        (max_coords.1 - pos.y()) as u32,
    ))
}

fn display(camera: Option<camera::Camera>, mount: Option<mount::Mount>) -> Result<()> {
    let sdl = init().map_err(failure::err_msg)?;
    let video = sdl.video().map_err(failure::err_msg)?;
    // let input = video.text_input();
    let ttf = ttf::init()?;
    let font = ttf.load_font(find_font()?, 20).map_err(failure::err_msg)?;
    let window = video.window("Scopie", 400, 400).resizable().build()?;
    let mut canvas = window.into_canvas().present_vsync().build()?;
    let creator = canvas.texture_creator();
    let mut event_pump = sdl.event_pump().map_err(failure::err_msg)?;
    let mut camera_display = camera.map(CameraDisplay::new);
    let mut mount_display = mount.map(MountDisplay::new);
    let mut status = String::new();
    let mut input_text = String::new();
    let mut command_okay = true;
    // input.start();
    'outer: loop {
        while let Some(event) = event_pump.poll_event() {
            eprintln!("{:?}", event);
            match event {
                Event::Quit { .. } => break 'outer,
                Event::TextEditing {
                    text,
                    start,
                    length,
                    ..
                } => input_text.replace_range((start as usize)..((start + length) as usize), &text),
                Event::TextInput { text, .. } => input_text.push_str(&text),
                Event::KeyDown {
                    keycode: Some(keycode),
                    ..
                } => match keycode {
                    Keycode::Backspace => {
                        input_text.pop();
                    }
                    Keycode::Return => {
                        let cmd = input_text.split_whitespace().collect::<Vec<_>>();
                        command_okay = cmd.is_empty();
                        if let Some(ref mut camera_display) = camera_display {
                            command_okay |= camera_display.cmd(&cmd)?;
                        }
                        if let Some(ref mut mount_display) = mount_display {
                            command_okay |= mount_display.cmd(&cmd)?;
                        }
                        input_text.clear();
                    }
                    _ => (),
                },
                _ => (),
            }
        }

        canvas.set_draw_color(Color::RGB(0, 0, 0));
        canvas.clear();
        let (window_width, window_height) = canvas.output_size().map_err(failure::err_msg)?;
        status.clear();
        //status.push_str("wau");
        if let Some(ref mut camera_display) = camera_display {
            status.push_str(camera_display.status()?);
        }
        if let Some(ref mut mount_display) = mount_display {
            status.push_str(mount_display.status()?);
        }
        let text_size = render_block(
            &font,
            &creator,
            &mut canvas,
            Color::RGB(255, 128, 128),
            Point::new(10, 10),
            &status,
        )?;
        if let Some(ref mut camera_display) = camera_display {
            let width = (window_width as i32 - text_size.right())
                .try_into()
                .unwrap_or(1);
            let camera_rect = Rect::new(text_size.right(), 0, width, window_height);
            camera_display.draw(&mut canvas, &creator, camera_rect)?;
        }
        let input_height = window_height as i32 - font.recommended_line_spacing();
        let input_pos = Point::new(10, input_height.try_into().unwrap_or(0));
        render_line(
            &font,
            &creator,
            &mut canvas,
            Color::RGB(255, 128, 128),
            input_pos,
            &input_text,
        )?;
        if command_okay {
            canvas.set_draw_color(Color::RGB(128, 128, 128));
        } else {
            canvas.set_draw_color(Color::RGB(255, 255, 255));
        }
        canvas
            .draw_rect(Rect::new(
                input_pos.x() - 1,
                input_pos.y() - 1,
                (window_width as i32 - input_pos.x() * 2)
                    .try_into()
                    .unwrap_or(2),
                font.recommended_line_spacing() as u32,
            ))
            .map_err(failure::err_msg)?;
        canvas.present();
    }
    // input.stop();
    Ok(())
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
        Err(err) => println!("Error: {:?}", err),
    }
}
