mod camera;
mod camera_display;
mod dms;
mod mount;
mod mount_async;
mod mount_display;
mod platesolve;
mod process;
mod qhycamera;

use camera_display::CameraDisplay;
use glutin::{
    self,
    event::{ElementState, Event, VirtualKeyCode as Key, WindowEvent},
    event_loop::{ControlFlow, EventLoop},
    window::WindowBuilder,
    ContextBuilder,
};
use khygl::{
    check_gl, gl_register_debug, render_text::TextRenderer, render_texture::TextureRenderer,
    texture::CpuTexture, Rect,
};
use mount_async::MountAsync;
use mount_display::MountDisplay;
use std::{
    convert::TryInto,
    fmt::Write,
    fs::File,
    path::Path,
    time::{Duration, Instant},
};

type Result<T> = std::result::Result<T, failure::Error>;

fn read_png(path: impl AsRef<Path>) -> Result<CpuTexture<u16>> {
    let mut decoder = png::Decoder::new(File::open(path)?);
    decoder.set_transformations(png::Transformations::IDENTITY);
    let (info, mut reader) = decoder.read_info()?;
    assert_eq!(info.bit_depth, png::BitDepth::Sixteen);
    assert_eq!(info.color_type, png::ColorType::Grayscale);
    let mut buf = vec![0; info.buffer_size()];
    reader.next_frame(&mut buf)?;
    let mut buf16 = vec![0; info.width as usize * info.height as usize];
    for i in 0..buf16.len() {
        buf16[i] = u16::from(buf[i * 2]) << 8 | u16::from(buf[i * 2 + 1]);
    }
    Ok(CpuTexture::new(
        buf16,
        (info.width as usize, info.height as usize),
    ))
}

fn write_png(path: impl AsRef<Path>, img: &CpuTexture<u16>) -> Result<()> {
    let mut encoder = png::Encoder::new(File::create(path)?, img.size.0 as u32, img.size.1 as u32);
    encoder.set_color(png::ColorType::Grayscale);
    encoder.set_depth(png::BitDepth::Sixteen);
    let mut writer = encoder.write_header()?;
    let mut output = vec![0; img.size.0 * img.size.1 * 2];
    for i in 0..(img.size.0 * img.size.1) {
        output[i * 2] = (img.data[i] >> 8) as u8;
        output[i * 2 + 1] = (img.data[i]) as u8;
    }
    writer.write_image_data(&output)?;
    Ok(())
}

struct Display {
    camera_display: Option<camera_display::CameraDisplay>,
    mount_display: Option<mount_display::MountDisplay>,
    next_frequent_update: Instant,
    next_infrequent_update: Instant,
    window_size: (usize, usize),
    status: String,
    old_status: String,
    input_text: String,
    input_error: String,
    texture_renderer: TextureRenderer,
    text_renderer: TextRenderer,
    command_okay: bool,
    wasd_mode: bool,
}

impl Display {
    fn window_size_f32(&self) -> (f32, f32) {
        (self.window_size.0 as f32, self.window_size.1 as f32)
    }

    fn run_cmd(&mut self) -> Result<()> {
        self.input_error.clear();
        let cmd = self.input_text.split_whitespace().collect::<Vec<_>>();
        self.command_okay = cmd.is_empty();
        if let ["wasd"] = &cmd as &[&str] {
            self.wasd_mode = true;
            self.command_okay |= true;
        }
        if let Some(ref mut camera_display) = self.camera_display {
            let mount = self.mount_display.as_mut().map(|m| &mut m.mount);
            self.command_okay |= camera_display.cmd(&cmd, mount)?;
        }
        if let Some(ref mut mount_display) = self.mount_display {
            self.command_okay |= mount_display.cmd(&cmd)?;
        }
        self.input_text.clear();
        Ok(())
    }
}

impl Display {
    fn setup(
        camera: Option<camera::Camera>,
        mount: Option<mount::Mount>,
        input_error: String,
        command_okay: bool,
        window_size: (usize, usize),
        dpi: f64,
    ) -> Result<Self> {
        let texture_renderer = TextureRenderer::new()?;
        let height = 20.0 * dpi as f32;
        let text_renderer = TextRenderer::new(height)?;
        let camera_display = Some(CameraDisplay::new(camera));
        let mount_display = mount.map(MountAsync::new).map(MountDisplay::new);
        Ok(Self {
            camera_display,
            mount_display,
            next_frequent_update: Instant::now(),
            next_infrequent_update: Instant::now(),
            window_size,
            status: String::new(),
            old_status: String::new(),
            input_text: String::new(),
            input_error,
            texture_renderer,
            text_renderer,
            command_okay,
            wasd_mode: false,
        })
    }

    // (wait until, redraw now)
    fn update(&mut self) -> Result<(Instant, bool)> {
        let now = Instant::now();
        let frequent_update_rate = Duration::from_millis(50);
        let is_next_frequent_update = now >= self.next_frequent_update;
        if is_next_frequent_update {
            self.next_frequent_update += frequent_update_rate;
            let too_fast = now >= self.next_frequent_update + frequent_update_rate * 3;
            if too_fast {
                self.next_frequent_update = now + frequent_update_rate;
                // TODO
                // println!("Warning: target FPS too fast");
            }
        } else {
            return Ok((self.next_frequent_update, false));
        }

        let infrequent_update = now >= self.next_infrequent_update;
        if infrequent_update {
            self.next_infrequent_update += Duration::from_secs(1);
        }
        self.status.clear();
        if let Some(ref mut camera_display) = self.camera_display {
            camera_display.status(&mut self.status, infrequent_update)?;
        }
        if let Some(ref mut mount_display) = self.mount_display {
            mount_display.status(&mut self.status)?;
        }
        if self.wasd_mode {
            write!(
                &mut self.status,
                "WASD/RF mount control mode (esc to cancel)"
            )?;
        }
        if !self.command_okay {
            write!(&mut self.status, "{}", self.input_error)?;
        }
        let mut redraw = false;
        if self.old_status != self.status {
            self.old_status = self.status.clone();
            redraw = true;
        }
        if let Some(ref mut camera_display) = self.camera_display {
            redraw |= camera_display.update()?;
        }
        Ok((self.next_frequent_update, redraw))
    }

    fn render(&mut self) -> Result<()> {
        let window_size_f32 = self.window_size_f32();
        unsafe {
            gl::ClearColor(0.0, 0.0, 0.0, 1.0);
            gl::Clear(gl::COLOR_BUFFER_BIT | gl::DEPTH_BUFFER_BIT);
        }
        let text_size = self.text_renderer.render(
            &self.texture_renderer,
            &self.status,
            [1.0, 1.0, 1.0, 1.0],
            (10, 10),
            self.window_size,
        )?;
        let input_pos_y = self.window_size.1 as isize - self.text_renderer.spacing as isize - 1;
        let input_pos_y = input_pos_y.try_into().unwrap_or(0);
        let input_pos = (10, input_pos_y);
        if let Some(ref mut camera_display) = self.camera_display {
            let width = (self.window_size.0 as isize - text_size.right() as isize)
                .try_into()
                .unwrap_or(1);
            let camera_rect = Rect::new(text_size.right(), 0, width, input_pos_y);
            camera_display.draw(camera_rect, &self.texture_renderer, window_size_f32)?;
        }
        self.text_renderer.render(
            &self.texture_renderer,
            &self.input_text,
            [1.0, 1.0, 1.0, 1.0],
            input_pos,
            self.window_size,
        )?;
        let command_color = if self.command_okay {
            [0.5, 0.5, 0.5, 1.0]
        } else {
            [1.0, 0.5, 0.5, 1.0]
        };
        self.texture_renderer.rect(
            Rect::new(
                input_pos.0,
                input_pos.1,
                (self.window_size.0 as isize - input_pos.0 as isize * 2)
                    .try_into()
                    .unwrap_or(2),
                self.text_renderer.spacing,
            ),
            command_color,
            self.window_size_f32(),
        )?;
        Ok(())
    }

    fn resize(&mut self, size: (usize, usize)) -> Result<()> {
        self.window_size = size;
        Ok(())
    }

    fn key_up(&mut self, key: Key) -> Result<()> {
        if self.wasd_mode {
            if let Some(ref mut mount_display) = self.mount_display {
                mount_display.key_up(key)?;
            }
        }
        Ok(())
    }

    fn key_down(&mut self, key: Key) -> Result<()> {
        if self.wasd_mode {
            if let Some(ref mut mount_display) = self.mount_display {
                mount_display.key_down(key)?;
            }
        }
        match key {
            Key::Escape if self.wasd_mode => {
                self.wasd_mode = false;
            }
            Key::Back if !self.wasd_mode => {
                self.input_text.pop();
            }
            Key::Return if !self.wasd_mode => match self.run_cmd() {
                Ok(()) => (),
                Err(err) => {
                    self.input_error = format!("Command error: {}\n", err);
                    self.command_okay = false;
                }
            },
            _ => return Ok(()),
        }
        Ok(())
    }

    fn received_character(&mut self, ch: char) -> Result<()> {
        if !self.wasd_mode && ch >= 32 as char {
            self.input_text.push(ch);
        }
        Ok(())
    }
}

fn handle<T>(res: Result<T>) -> T {
    match res {
        Ok(ok) => ok,
        Err(err) => panic!("{:?}", err),
    }
}

fn main() -> Result<()> {
    let live = true;
    let mut command_okay = true;
    let mut input_error = String::new();
    let camera = match camera::autoconnect(live) {
        Ok(ok) => Some(ok),
        Err(err) => {
            command_okay = false;
            writeln!(&mut input_error, "Error connecting to camera: {}", err)?;
            None
        }
    };
    let mount = match mount::autoconnect() {
        Ok(ok) => Some(ok),
        Err(err) => {
            command_okay = false;
            writeln!(&mut input_error, "Error connecting to mount: {}", err)?;
            None
        }
    };

    let el = EventLoop::new();
    let wb = WindowBuilder::new()
        .with_title("clam5")
        .with_inner_size(glutin::dpi::LogicalSize::new(800.0, 800.0));
    let windowed_context = ContextBuilder::new()
        .with_vsync(true)
        .build_windowed(wb, &el)?;

    let windowed_context = unsafe { windowed_context.make_current().map_err(|(_, e)| e)? };

    let dpi = windowed_context.window().hidpi_factor();
    let initial_size = windowed_context.window().inner_size().to_physical(dpi);

    gl::load_with(|symbol| windowed_context.get_proc_address(symbol) as *const _);

    if !gl::GetError::is_loaded() {
        return Err(failure::err_msg("glGetError not loaded"));
    }

    if cfg!(debug_assertions) {
        unsafe { gl::Enable(gl::DEBUG_OUTPUT_SYNCHRONOUS) };
        check_gl()?;
        gl_register_debug()?;
    }

    let mut display = Some(Display::setup(
        camera,
        mount,
        input_error,
        command_okay,
        (initial_size.width as usize, initial_size.height as usize),
        dpi,
    )?);

    el.run(move |event, _, control_flow| match event {
        Event::WindowEvent { event, .. } => match event {
            WindowEvent::Resized(logical_size)
                if logical_size.width > 0.0 && logical_size.height > 0.0 =>
            {
                if let Some(ref mut display) = display {
                    let dpi_factor = windowed_context.window().hidpi_factor();
                    let physical = logical_size.to_physical(dpi_factor);
                    handle(display.resize((physical.width as usize, physical.height as usize)));
                    unsafe { gl::Viewport(0, 0, physical.width as i32, physical.height as i32) };
                }
            }
            WindowEvent::KeyboardInput { input, .. } => {
                if let Some(ref mut display) = display {
                    if let Some(code) = input.virtual_keycode {
                        match input.state {
                            ElementState::Pressed => handle(display.key_down(code)),
                            ElementState::Released => handle(display.key_up(code)),
                        }
                        windowed_context.window().request_redraw();
                    }
                }
            }
            WindowEvent::ReceivedCharacter(ch) => {
                if let Some(ref mut display) = display {
                    handle(display.received_character(ch));
                    windowed_context.window().request_redraw();
                }
            }
            WindowEvent::RedrawRequested => {
                if let Some(ref mut display) = display {
                    handle(display.render());
                    handle(windowed_context.swap_buffers().map_err(|e| e.into()));
                }
            }
            WindowEvent::CloseRequested => *control_flow = ControlFlow::Exit,
            _ => (),
        },
        Event::EventsCleared => {
            if *control_flow == ControlFlow::Exit {
                display = None;
            } else {
                let wait_until = if let Some(ref mut display) = display {
                    let (wait_until, redraw) = handle(display.update());
                    if redraw {
                        windowed_context.window().request_redraw();
                    }
                    wait_until
                } else {
                    Instant::now() + Duration::from_millis(10)
                };
                *control_flow = ControlFlow::WaitUntil(wait_until);
            }
        }
        _ => (),
    })
}
