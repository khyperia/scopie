mod alg;
mod camera;
mod dms;
mod mount;
// mod platesolve;

use eframe::egui::{self, Color32, Ui};

pub use anyhow::Result;

fn main() -> eframe::Result {
    let native_options = eframe::NativeOptions::default();
    let res = eframe::run_native(
        "scopie",
        native_options,
        Box::new(|cc| Ok(Box::new(ScopieApp::new(cc)))),
    );
    println!("main thread quit");
    res
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
                    Tab::Mount => self
                        .mount
                        .ui(ui, self.camera.image.as_ref().map(|i| &i.image)),
                }
            })
            .inner;
        if let Err(result) = result {
            self.error = format!("{}", result);
        }
    }
}
