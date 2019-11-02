mod camera;
mod camera_display;
mod dms;
mod mount;
mod mount_display;
mod platesolve;
mod process;
mod qhycamera;

use camera_display::CameraDisplay;
use khygl::{
    display::Key,
    render_text::TextRenderer,
    render_texture::{TextureRendererF32, TextureRendererU8},
    texture::CpuTexture,
    Rect,
};
use mount_display::MountDisplay;
use std::{convert::TryInto, fmt::Write, fs::File, path::Path};

type Result<T> = std::result::Result<T, failure::Error>;

fn read_png(path: impl AsRef<Path>) -> CpuTexture<u16> {
    let mut decoder = png::Decoder::new(File::open(path).unwrap());
    decoder.set_transformations(png::Transformations::IDENTITY);
    let (info, mut reader) = decoder.read_info().unwrap();
    assert_eq!(info.bit_depth, png::BitDepth::Sixteen);
    assert_eq!(info.color_type, png::ColorType::Grayscale);
    let mut buf = vec![0; info.buffer_size()];
    reader.next_frame(&mut buf).unwrap();
    let mut buf16 = vec![0; info.width as usize * info.height as usize];
    for i in 0..buf16.len() {
        buf16[i] = u16::from(buf[i * 2]) << 8 | u16::from(buf[i * 2 + 1]);
    }
    CpuTexture::new(buf16, (info.width as usize, info.height as usize))
}

fn write_png(path: impl AsRef<Path>, img: &CpuTexture<u16>) {
    let mut encoder = png::Encoder::new(
        File::create(path).expect("Unable to create png file"),
        img.size.0 as u32,
        img.size.1 as u32,
    );
    encoder.set_color(png::ColorType::Grayscale);
    encoder.set_depth(png::BitDepth::Sixteen);
    let mut writer = encoder.write_header().unwrap();
    let mut output = vec![0; img.size.0 * img.size.1 * 2];
    for i in 0..(img.size.0 * img.size.1) {
        output[i * 2] = (img.data[i] >> 8) as u8;
        output[i * 2 + 1] = (img.data[i]) as u8;
    }
    writer.write_image_data(&output).unwrap();
}

struct Display {
    camera_display: Option<camera_display::CameraDisplay>,
    mount_display: Option<mount_display::MountDisplay>,
    window_size: (usize, usize),
    status: String,
    input_text: String,
    input_error: String,
    texture_renderer_u8: TextureRendererU8,
    texture_renderer_f32: TextureRendererF32,
    text_renderer: TextRenderer,
    command_okay: bool,
}

impl Display {
    fn window_size_f32(&self) -> (f32, f32) {
        (self.window_size.0 as f32, self.window_size.1 as f32)
    }

    fn run_cmd(&mut self) -> Result<()> {
        self.input_error.clear();
        let cmd = self.input_text.split_whitespace().collect::<Vec<_>>();
        self.command_okay = cmd.is_empty();
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

impl khygl::display::Display for Display {
    fn setup(window_size: (usize, usize)) -> Result<Self> {
        let texture_renderer_u8 = TextureRendererU8::new()?;
        let texture_renderer_f32 = TextureRendererF32::new()?;
        let text_renderer = TextRenderer::new()?;
        let live = false;
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
        let camera_display = Some(CameraDisplay::new(camera));
        let mount_display = mount.map(MountDisplay::new);
        Ok(Self {
            camera_display,
            mount_display,
            window_size,
            status: String::new(),
            input_text: String::new(),
            input_error,
            texture_renderer_u8,
            texture_renderer_f32,
            text_renderer,
            command_okay,
        })
    }

    fn render(&mut self) -> Result<()> {
        let window_size_f32 = self.window_size_f32();
        unsafe {
            gl::ClearColor(0.0, 0.0, 0.0, 1.0);
            gl::Clear(gl::COLOR_BUFFER_BIT | gl::DEPTH_BUFFER_BIT);
        }
        self.status.clear();
        if let Some(ref mut camera_display) = self.camera_display {
            camera_display.status(&mut self.status)?;
        }
        if let Some(ref mut mount_display) = self.mount_display {
            mount_display.status(&mut self.status)?;
        }
        if !self.command_okay {
            write!(&mut self.status, "{}", self.input_error)?;
        }
        let text_size = self.text_renderer.render(
            &self.texture_renderer_f32,
            &self.status,
            [1.0, 1.0, 1.0, 1.0],
            (10, 10),
            self.window_size,
        )?;
        let input_pos_y = self.window_size.1 as isize - self.text_renderer.spacing as isize;
        let input_pos_y = input_pos_y.try_into().unwrap_or(0);
        let input_pos = (10, input_pos_y);
        if let Some(ref mut camera_display) = self.camera_display {
            let width = (self.window_size.0 as isize - text_size.right() as isize)
                .try_into()
                .unwrap_or(1);
            let camera_rect = Rect::new(text_size.right(), 0, width, input_pos_y);
            camera_display.draw(camera_rect, &self.texture_renderer_u8, window_size_f32)?;
        }
        self.text_renderer.render(
            &self.texture_renderer_f32,
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
        self.texture_renderer_u8.rect(
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
        if let Some(ref mut mount_display) = self.mount_display {
            mount_display.key_up(key)?;
        }
        Ok(())
    }

    fn key_down(&mut self, key: Key) -> Result<()> {
        if let Some(ref mut mount_display) = self.mount_display {
            mount_display.key_down(key)?;
        }
        match key {
            Key::Back => {
                self.input_text.pop();
            }
            Key::Return => match self.run_cmd() {
                Ok(()) => (),
                Err(err) => {
                    self.input_error = format!("Command error: {}\n", err);
                    self.command_okay = false;
                }
            },
            _ => (),
        }
        Ok(())
    }

    fn received_character(&mut self, ch: char) -> Result<()> {
        if ch >= 32 as char {
            self.input_text.push(ch);
        }
        Ok(())
    }
}

fn main() -> Result<()> {
    khygl::display::run::<Display>((600.0, 600.0))
}
