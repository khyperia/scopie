mod alg;
mod camera;
mod dms;
mod mount;
// mod platesolve;

use camera::interface::CpuTexture;
use eframe::egui::{self, Color32, Ui};
use std::{fs::File, path::Path};

pub use anyhow::Result;

fn read_png(path: impl AsRef<Path>) -> Result<CpuTexture<u16>> {
    let mut decoder = png::Decoder::new(File::open(path)?);
    decoder.set_transformations(png::Transformations::IDENTITY);
    let mut reader = decoder.read_info()?;
    let info = reader.info();
    assert_eq!(info.bit_depth, png::BitDepth::Sixteen);
    assert_eq!(info.color_type, png::ColorType::Grayscale);
    let width = info.width;
    let height = info.height;
    let mut buf = vec![0; reader.output_buffer_size()];
    reader.next_frame(&mut buf)?;
    let mut buf16 = vec![0; width as usize * height as usize];
    for i in 0..buf16.len() {
        buf16[i] = u16::from(buf[i * 2]) << 8 | u16::from(buf[i * 2 + 1]);
    }
    Ok(CpuTexture::new(buf16, (width as usize, height as usize)))
}

fn write_png(path: impl AsRef<Path>, img: &CpuTexture<u16>) -> Result<()> {
    let mut encoder = png::Encoder::new(File::create(path)?, img.size.0 as u32, img.size.1 as u32);
    encoder.set_color(png::ColorType::Grayscale);
    encoder.set_depth(png::BitDepth::Sixteen);
    let mut writer = encoder.write_header()?;
    let mut output = vec![0; img.size.0 * img.size.1 * 2];
    let data = img.data();
    for i in 0..(img.size.0 * img.size.1) {
        output[i * 2] = (data[i] >> 8) as u8;
        output[i * 2 + 1] = (data[i]) as u8;
    }
    writer.write_image_data(&output)?;
    Ok(())
}

fn main() -> eframe::Result {
    let native_options = eframe::NativeOptions::default();
    eframe::run_native(
        "scopie",
        native_options,
        Box::new(|cc| Ok(Box::new(ScopieApp::new(cc)))),
    )
}

#[derive(Clone)]
struct UiThread {
    ctx: egui::Context,
}

impl UiThread {
    pub fn trigger(&self) {
        self.ctx.request_repaint();
    }
}

#[derive(PartialEq, Eq)]
enum Tab {
    Camera,
    Mount,
}

struct ScopieApp {
    error: String,
    camera: camera::camera_display::CameraDisplay,
    mount: mount::display::MountDisplay,
    tab: Tab,
}

impl ScopieApp {
    fn new(cc: &eframe::CreationContext<'_>) -> Self {
        // Customize egui here with cc.egui_ctx.set_fonts and cc.egui_ctx.set_visuals.
        // Restore app state using cc.storage (requires the "persistence" feature).
        // Use the cc.gl (a glow::Context) to create graphics shaders and buffers that you can use
        // for e.g. egui::PaintCallback.
        let ui_thread = UiThread {
            ctx: cc.egui_ctx.clone(),
        };
        Self {
            error: String::new(),
            camera: camera::camera_display::CameraDisplay::new(ui_thread.clone()),
            mount: mount::display::MountDisplay::new(ui_thread),
            tab: Tab::Camera,
        }
    }
}

impl eframe::App for ScopieApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.camera.update(ctx);
        self.mount.update();
        ctx.request_repaint();
        if !self.error.is_empty() {
            egui::TopBottomPanel::bottom("error_panel").show(ctx, |ui| {
                if ui.colored_label(Color32::RED, &self.error).clicked() {
                    self.error = String::new();
                }
            });
        }
        egui::TopBottomPanel::top("tab_panel").show(ctx, |ui| {
            ui.horizontal(|ui| {
                fn tab(s: &mut ScopieApp, ui: &mut Ui, t: Tab, text: &str) {
                    ui.style_mut().visuals.override_text_color = if s.tab == t {
                        Some(Color32::BLACK)
                    } else {
                        None
                    };
                    if ui.button(text).clicked() {
                        s.tab = t;
                    }
                }
                tab(self, ui, Tab::Camera, "Camera");
                tab(self, ui, Tab::Mount, "Mount");
            });
        });
        let result = egui::CentralPanel::default()
            .show(ctx, |ui| {
                // if ui.button("Scan for cameras").clicked() {}
                match self.tab {
                    Tab::Camera => self.camera.ui(ui),
                    Tab::Mount => self.mount.ui(ui, &self.camera.image),
                }
            })
            .inner;
        if let Err(result) = result {
            self.error = format!("{}", result);
        }
    }
}
