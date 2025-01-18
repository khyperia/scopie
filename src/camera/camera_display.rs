use crate::{
    alg,
    camera::{
        image_processor::ImageProcessor,
        interface::TimestampImage,
        qhycamera::{ControlId, EXPOSURE_FACTOR},
        thread::CameraAsync,
    },
    Result, UiThread,
};
use anyhow::anyhow;
use core::f32;
use eframe::{
    egui::{
        self, load::SizedTexture, Color32, ColorImage, Id, Image, Pos2, Rect, TextEdit,
        TextureHandle, Ui, Vec2,
    },
    emath::{self, TSTransform},
};
use std::{
    collections::HashMap,
    sync::{mpsc, Arc, Mutex},
    time::{Duration, Instant},
};

pub struct CameraDisplay {
    camera: CameraAsync,
    image_processor: Arc<Mutex<ImageProcessor>>,
    pub image: Option<TimestampImage<TextureHandle>>,
    transform: TSTransform,
    display_interesting: bool,
    roi_select: bool,
    control_text_values: HashMap<ControlId, String>,
    roi_select_start: Option<Pos2>,
}

impl CameraDisplay {
    pub fn new(ui_thread: UiThread) -> Self {
        let (send_image, recv_image) = mpsc::channel();
        Self {
            camera: CameraAsync::new(send_image, ui_thread.clone()),
            image_processor: ImageProcessor::new(ui_thread, recv_image),
            image: None,
            transform: TSTransform::default(),
            display_interesting: true,
            roi_select: false,
            control_text_values: HashMap::new(),
            roi_select_start: None,
        }
    }

    pub fn update(&mut self, ctx: &egui::Context) {
        self.camera.update();
        let image = self.image_processor.lock().unwrap().image.take();
        if let Some(image) = image {
            self.set_image(ctx, image);
        }
    }

    fn set_image(&mut self, ctx: &egui::Context, color_image: TimestampImage<ColorImage>) {
        match &mut self.image {
            None => {
                self.image = Some(TimestampImage {
                    image: ctx.load_texture(
                        "main image",
                        color_image.image,
                        egui::TextureOptions::NEAREST,
                    ),
                    time: color_image.time,
                    duration: color_image.duration,
                })
            }
            Some(image) => {
                image.image.set(
                    color_image.image,
                    egui::TextureOptions {
                        magnification: egui::TextureFilter::Nearest,
                        minification: egui::TextureFilter::Linear,
                        wrap_mode: egui::TextureWrapMode::ClampToEdge,
                        mipmap_mode: Some(egui::TextureFilter::Linear),
                    },
                );
                image.time = color_image.time;
                image.duration = color_image.duration;
            }
        }
    }

    pub fn ui(&mut self, ui: &mut Ui) -> Result<()> {
        ui.horizontal_top(|ui| {
            ui.vertical(|ui| self.ui_controls(ui)).inner?;
            self.ui_image(ui)?;
            Ok(())
        })
        .inner
    }

    fn ui_image(&mut self, ui: &mut Ui) -> Result<()> {
        let image = if let Some(image) = &self.image {
            &image.image
        } else {
            self.transform = TSTransform::default();
            return Ok(());
        };

        let (id, rect) = ui.allocate_space(ui.available_size());

        let (afterfunc, new_roi) = if self.roi_select {
            Self::ui_roi_select(
                &mut self.roi_select_start,
                &mut self.roi_select,
                &self.transform,
                image.size(),
                ui,
                id,
                rect,
            )
        } else {
            Self::navigate_image(&mut self.transform, ui, id, rect);
            (None, None)
        };

        ui.scope(|ui| {
            ui.set_clip_rect(rect);

            let draw_at_rect = self.transform * alg::clamp_aspect_ratio(image.size(), rect);
            ui.put(
                draw_at_rect,
                Image::new(SizedTexture::new(image, draw_at_rect.size())),
            );
        });
        if let Some(afterfunc) = afterfunc {
            afterfunc(ui);
        }
        if let Some(sub_roi) = new_roi {
            if let Some(old_roi) = &self.camera.data.current_roi {
                let new_x = old_roi.x + sub_roi.x;
                let new_y = old_roi.y + sub_roi.y;
                let new_width = sub_roi
                    .width
                    .min((old_roi.x + old_roi.width).saturating_sub(old_roi.x));
                let new_height = sub_roi
                    .height
                    .min((old_roi.y + old_roi.height).saturating_sub(old_roi.y));
                let new_roi = alg::Rect::new(new_x, new_y, new_width, new_height);
                if new_roi.width == 0 || new_roi.height == 0 {
                    self.camera
                        .set_roi(None)
                        .map_err(|()| anyhow!("Unable to reset ROI"))?;
                } else {
                    self.camera
                        .set_roi(Some(new_roi))
                        .map_err(|()| anyhow!("Unable to set ROI"))?;
                }
            } else {
                return Err(anyhow!("Unable to set ROI (camera not online?)"));
            }
        }
        Ok(())
    }

    fn navigate_image(transform: &mut TSTransform, ui: &mut Ui, id: Id, rect: Rect) {
        let response = ui.interact(rect, id, egui::Sense::click_and_drag());
        if response.dragged() {
            transform.translation += response.drag_delta();
        }
        if response.double_clicked() {
            *transform = TSTransform::default();
        }

        if let Some(pointer) = ui.ctx().input(|i| i.pointer.hover_pos()) {
            if response.hovered() {
                let pointer_in_layer = transform.inverse() * pointer;
                let zoom_delta = ui.ctx().input(|i| i.zoom_delta());
                let scroll_delta = ui.ctx().input(|i| i.smooth_scroll_delta);
                // zoom
                if zoom_delta != 1.0 {
                    *transform = *transform
                        * (TSTransform::from_translation(pointer_in_layer.to_vec2())
                            * TSTransform::from_scaling(zoom_delta)
                            * TSTransform::from_translation(-pointer_in_layer.to_vec2()));
                }
                // scroll
                if scroll_delta != Vec2::ZERO {
                    *transform = TSTransform::from_translation(scroll_delta) * *transform;
                }
            }
        }
        // TODO: Clamp transform to be inside screen
    }

    fn ui_roi_select<'a>(
        roi_select_start: &mut Option<Pos2>,
        roi_select: &mut bool,
        transform: &TSTransform,
        image_size: [usize; 2],
        ui: &mut Ui,
        id: Id,
        rect: Rect,
    ) -> (Option<impl FnOnce(&'a Ui)>, Option<alg::Rect<usize>>) {
        let response = ui.interact(rect, id, egui::Sense::drag());

        if response.drag_started() {
            if let Some(pointer) = ui.ctx().input(|i| i.pointer.hover_pos()) {
                *roi_select_start = Some(pointer);
            }
        }

        if response.drag_stopped() {
            if let Some(roi_select_end) = ui.ctx().input(|i| i.pointer.hover_pos()) {
                if let Some(roi_select_start) = *roi_select_start {
                    let selected = Rect::from_two_pos(roi_select_start, roi_select_end);
                    let draw_at_rect = *transform * alg::clamp_aspect_ratio(image_size, rect);
                    let minx = emath::remap_clamp(
                        selected.min.x,
                        draw_at_rect.min.x..=draw_at_rect.max.x,
                        0.0..=image_size[0] as f32,
                    );
                    let maxx = emath::remap_clamp(
                        selected.max.x,
                        draw_at_rect.min.x..=draw_at_rect.max.x,
                        0.0..=image_size[0] as f32,
                    );
                    let miny = emath::remap_clamp(
                        selected.min.y,
                        draw_at_rect.min.y..=draw_at_rect.max.y,
                        0.0..=image_size[1] as f32,
                    );
                    let maxy = emath::remap_clamp(
                        selected.max.y,
                        draw_at_rect.min.y..=draw_at_rect.max.y,
                        0.0..=image_size[1] as f32,
                    );
                    let roi_f32 = Rect::from_min_max(Pos2::new(minx, miny), Pos2::new(maxx, maxy));
                    let roi = alg::Rect::<usize>::new(
                        roi_f32.min.x as usize,
                        roi_f32.min.y as usize,
                        roi_f32.width() as usize,
                        roi_f32.height() as usize,
                    );
                    *roi_select = false;
                    return (None, Some(roi));
                }
            }
            *roi_select = false;
        }

        if response.dragged() {
            if let Some(roi_select_end) = ui.ctx().input(|i| i.pointer.hover_pos()) {
                if let Some(roi_select_start) = *roi_select_start {
                    let rect = Rect::from_two_pos(roi_select_start, roi_select_end);
                    return (
                        Some(move |ui: &Ui| {
                            let stroke = (1.0, Color32::RED);
                            let p = ui.painter();
                            p.line_segment([rect.left_top(), rect.right_top()], stroke);
                            p.line_segment([rect.right_top(), rect.right_bottom()], stroke);
                            p.line_segment([rect.right_bottom(), rect.left_bottom()], stroke);
                            p.line_segment([rect.left_bottom(), rect.left_top()], stroke);
                        }),
                        None,
                    );
                }
            }
        }
        (None, None)
    }

    fn ui_controls(&mut self, ui: &mut Ui) -> Result<()> {
        ui.label(&self.camera.data.camera_id);

        {
            let mut running = self.camera.data.running;
            ui.checkbox(&mut running, "running");
            if running != self.camera.data.running {
                if running {
                    self.camera
                        .start()
                        .map_err(|()| anyhow!("couldn't start camera"))?;
                } else {
                    self.camera
                        .stop()
                        .map_err(|()| anyhow!("couldn't stop camera"))?;
                }
            }
        }
        {
            let mut live = self.camera.data.is_live;
            ui.checkbox(&mut live, "live");
            if live != self.camera.data.is_live {
                self.camera
                    .toggle_live()
                    .map_err(|()| anyhow!("couldn't toggle live"))?;
            }
        }

        ui.label(format!(
            "Last exposure: {:?}",
            self.image.as_ref().map_or(Duration::ZERO, |i| i.duration),
        ));
        if !self.camera.data.cmd_status.is_empty() {
            ui.label(format!("Camera error: {}", self.camera.data.cmd_status));
        }
        // ui.label("cross:{}|bin:{}", self.image_display.cross, self.image_display.bin);
        ui.checkbox(&mut self.display_interesting, "interesting");
        ui.checkbox(&mut self.roi_select, "select ROI");
        // ui.label("save|save [n]: {}", self.save);
        // if !self.solve_status.is_empty() {
        //     ui.label("solve: {}", self.solve_status);
        // }
        // if self.folder.is_empty() {
        //     ui.label("folder: None");
        // } else {
        //     ui.label("folder: {}", self.folder);
        // }
        // self.processor.status(status);
        if self.camera.data.running {
            let exposure = self
                .image
                .as_ref()
                .map_or(Duration::ZERO, |i| Instant::now() - i.time);
            ui.label(format!(
                "Time since exposure start: {:.1}s",
                exposure.as_secs_f64()
            ));
        }
        ui.horizontal(|ui| {
            let mut image_processor = self.image_processor.lock().unwrap();
            ui.label(format!("save: {}", image_processor.save));
            if ui.button("save").clicked() {
                image_processor.save += 1;
            }
            if ui.button("clear").clicked() {
                image_processor.save = 0;
            }
        });
        for control in &self.camera.data.controls {
            let edit_text = self.control_text_values.entry(control.id).or_default();
            let camera = &self.camera;
            if self.display_interesting && !control.interesting {
                continue;
            }
            ui.horizontal(|ui| {
                ui.set_max_width(400.0);
                if control.id == ControlId::ControlExposure {
                    ui.label(format!(
                        "exposure = {} ({}-{} by {})",
                        control.value / EXPOSURE_FACTOR,
                        control.min / EXPOSURE_FACTOR,
                        control.max / EXPOSURE_FACTOR,
                        control.step / EXPOSURE_FACTOR
                    ));
                } else {
                    ui.label(format!("{}", control));
                }
                let set = ui
                    .add_enabled_ui(edit_text.parse::<f64>().is_ok(), |ui| {
                        ui.button("set").clicked()
                    })
                    .inner;

                ui.add(TextEdit::singleline(edit_text).desired_width(f32::INFINITY));
                if set {
                    if let Ok(mut value) = edit_text.parse::<f64>() {
                        if control.id == ControlId::ControlExposure {
                            value *= EXPOSURE_FACTOR;
                        }
                        camera.set_control(control.id, value).map_err(|()| {
                            anyhow!(format!("couldn't set control {} to {}", control.id, value))
                        })?;
                        *edit_text = String::new();
                    }
                }
                Result::<()>::Ok(())
            });
        }
        Ok(())
    }
}
