use crate::{
    alg,
    dms::Angle,
    mount::{self, interface::TrackingMode, thread::MountAsync},
    Result, UiThread,
};
use eframe::egui::{load::SizedTexture, TextureHandle, Ui};

pub struct MountDisplay {
    pub mount: mount::thread::MountAsync,
    // pressed_keys: HashSet<Key>,
    slew_speed: u32,
    input_ra: String,
    input_dec: String,
    slewing_ra: bool,
    slewing_dec: bool,
}

impl MountDisplay {
    pub fn new(ui_thread: UiThread) -> Self {
        Self {
            mount: MountAsync::new(ui_thread),
            input_ra: String::new(),
            input_dec: String::new(),
            slew_speed: 1,
            slewing_ra: false,
            slewing_dec: false,
        }
    }

    pub fn update(&mut self) {
        self.mount.update();
    }

    pub fn ui(&mut self, ui: &mut Ui, camera_image: &Option<TextureHandle>) -> Result<()> {
        ui.horizontal_top(|ui| {
            ui.vertical(|ui| self.ui_controls(ui)).inner?;
            self.ui_image(ui, camera_image);
            Ok(())
        })
        .inner
    }

    pub fn ui_controls(&mut self, ui: &mut Ui) -> Result<()> {
        ui.horizontal(|ui| {
            ui.spacing_mut().text_edit_width = 100.0;
            ui.text_edit_singleline(&mut self.input_ra);
            ui.text_edit_singleline(&mut self.input_dec);
        });
        let ra = Angle::parse(&self.input_ra);
        let dec = Angle::parse(&self.input_dec);
        let enabled = ra.is_some() && dec.is_some();
        ui.add_enabled_ui(enabled, |ui| {
            if ui.button("sync pos").clicked() {
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.sync_real(ra, dec)?;
                }
            }
            if ui.button("slew").clicked() {
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.slew_real(ra, dec)?;
                }
            }
            if ui.button("azaltslew").clicked() {
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.slew_azalt(ra, dec)?;
                }
            }
            if ui.button("set location").clicked() {
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.set_location(ra, dec)?;
                }
            }
            anyhow::Ok(())
        })
        .inner?;
        if ui.button("set time now").clicked() {
            self.mount.set_time_now()?;
        }
        if ui.button("cancel").clicked() {
            self.mount.cancel()?;
        }
        fn mode(s: &MountDisplay, ui: &mut Ui, m: TrackingMode, text: &str) -> Result<()> {
            let mut curmode = s.mount.data.tracking_mode;
            ui.radio_value(&mut curmode, m, text);
            if curmode != s.mount.data.tracking_mode {
                s.mount.set_tracking_mode(m)?;
            }
            Ok(())
        }
        mode(self, ui, TrackingMode::Off, "mode: off")?;
        mode(self, ui, TrackingMode::AltAz, "mode: AltAz")?;
        mode(self, ui, TrackingMode::Equatorial, "mode: Equatorial")?;
        mode(self, ui, TrackingMode::SiderealPec, "mode: SiderealPec")?;

        let data = &self.mount.data;
        let (ra_real, dec_real) = data.ra_dec_real;
        ui.label(format!(
            "RA/Dec real: {} {}",
            ra_real.fmt_hours(),
            dec_real.fmt_degrees()
        ));
        let (ra_mount, dec_mount) = data.ra_dec_mount;
        ui.label(format!(
            "RA/Dec mount: {} {}",
            ra_mount.fmt_hours(),
            dec_mount.fmt_degrees()
        ));
        let (az, alt) = data.az_alt;
        ui.label(format!(
            "Az/Alt: {} {}",
            az.fmt_degrees(),
            alt.fmt_degrees()
        ));
        ui.label(format!("aligned: {}", data.aligned));
        ui.label(format!("tracking mode: {}", data.tracking_mode,));
        let (lat, lon) = data.location;
        ui.label(format!(
            "location: {} {}",
            lat.fmt_degrees(),
            lon.fmt_degrees()
        ));
        ui.label(format!("time: {}", data.time));
        ui.horizontal(|ui| {
            ui.label(format!("slew speed: {}", self.slew_speed));
            if ui.button("+").clicked() {
                self.slew_speed = (self.slew_speed + 1).min(9);
            }
            if ui.button("-").clicked() {
                self.slew_speed = (self.slew_speed - 1).max(1);
            }
        });

        let dec_p = ui.button("dec+").is_pointer_button_down_on();
        let (ra_p, ra_n) = ui
            .horizontal(|ui| {
                let ra_n = ui.button("ra-").is_pointer_button_down_on();
                let ra_p = ui.button("ra+").is_pointer_button_down_on();
                (ra_p, ra_n)
            })
            .inner;
        let dec_n = ui.button("dec-").is_pointer_button_down_on();
        if !ra_p && !ra_n {
            if self.slewing_ra {
                self.slewing_ra = false;
                self.mount.fixed_slew_ra(0)?;
            }
        }
        if ra_p {
            if !self.slewing_ra {
                self.slewing_ra = true;
                self.mount.fixed_slew_ra(self.slew_speed as i32)?;
            }
        } else if ra_n {
            if !self.slewing_ra {
                self.slewing_ra = true;
                self.mount.fixed_slew_ra(-(self.slew_speed as i32))?;
            }
        }
        if !dec_p && !dec_n {
            if self.slewing_dec {
                self.slewing_dec = false;
                self.mount.fixed_slew_dec(0)?;
            }
        }
        if dec_p {
            if !self.slewing_dec {
                self.slewing_dec = true;
                self.mount.fixed_slew_dec(self.slew_speed as i32)?;
            }
        } else if dec_n {
            if !self.slewing_dec {
                self.slewing_dec = true;
                self.mount.fixed_slew_dec(-(self.slew_speed as i32))?;
            }
        }
        Ok(())
    }

    pub fn ui_image(&mut self, ui: &mut Ui, image: &Option<TextureHandle>) {
        if let Some(image) = image {
            let size = ui.available_size();
            let size = alg::clamp_aspect_ratio_vec(image.size(), size);
            ui.image(SizedTexture::new(image, size));
        }
    }
}
