use crate::{dms::Angle, mount, Key, Result, UserUpdate};
use std::{collections::HashSet, fmt::Write};

pub struct MountDisplay {
    pub mount: mount::thread::MountAsync,
    pressed_keys: HashSet<Key>,
    slew_speed: u32,
}

impl MountDisplay {
    pub fn new(mount: mount::thread::MountAsync) -> Self {
        Self {
            mount,
            pressed_keys: HashSet::new(),
            slew_speed: 1,
        }
    }

    pub fn cmd(
        &mut self,
        command: &[&str],
    ) -> std::result::Result<bool, mount::thread::MountSendError> {
        match command {
            ["syncpos", ra, dec] => {
                let ra = Angle::parse(ra);
                let dec = Angle::parse(dec);
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.sync_real(ra, dec)?;
                } else {
                    return Ok(false);
                }
            }
            ["slew", ra, dec] => {
                let ra = Angle::parse(ra);
                let dec = Angle::parse(dec);
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.slew_real(ra, dec)?;
                } else {
                    return Ok(false);
                }
            }
            ["azaltslew", az, alt] => {
                let az = Angle::parse(az);
                let alt = Angle::parse(alt);
                if let (Some(az), Some(alt)) = (az, alt) {
                    self.mount.slew_azalt(az, alt)?;
                } else {
                    return Ok(false);
                }
            }
            ["cancel"] => {
                self.mount.cancel()?;
            }
            ["mode", mode] => match mode.parse() {
                Ok(mode) => {
                    self.mount.set_tracking_mode(mode)?;
                }
                Err(_) => return Ok(false),
            },
            ["location", lat, lon] => {
                let lat = Angle::parse(lat);
                let lon = Angle::parse(lon);
                if let (Some(lat), Some(lon)) = (lat, lon) {
                    self.mount.set_location(lat, lon)?;
                } else {
                    return Ok(false);
                }
            }
            ["time", "now"] => {
                self.mount.set_time_now()?;
            }
            _ => return Ok(false),
        }
        Ok(true)
    }

    pub fn status(&mut self, status: &mut String) -> Result<()> {
        let data = &self.mount.data;
        let (ra_real, dec_real) = data.ra_dec_real;
        writeln!(
            status,
            "RA/Dec real: {} {}",
            ra_real.fmt_hours(),
            dec_real.fmt_degrees()
        )?;
        let (ra_mount, dec_mount) = data.ra_dec_mount;
        writeln!(
            status,
            "RA/Dec mount: {} {}",
            ra_mount.fmt_hours(),
            dec_mount.fmt_degrees()
        )?;
        let (az, alt) = data.az_alt;
        writeln!(status, "Az/Alt: {} {}", az.fmt_degrees(), alt.fmt_degrees())?;
        writeln!(status, "aligned: {}", data.aligned)?;
        writeln!(status, "tracking mode: {}", data.tracking_mode,)?;
        let (lat, lon) = data.location;
        writeln!(
            status,
            "location: {} {}",
            lat.fmt_degrees(),
            lon.fmt_degrees()
        )?;
        writeln!(status, "time: {}", data.time)?;
        writeln!(status, "slew speed: {}", self.slew_speed)?;
        writeln!(status, "syncpos [ra] [dec]")?;
        writeln!(status, "slew [ra] [dec]")?;
        writeln!(status, "azaltslew [az] [alt]")?;
        writeln!(status, "cancel")?;
        writeln!(status, "mode [Off|AltAz|Equatorial|SiderealPec]")?;
        writeln!(status, "location [lat] [lon]")?;
        writeln!(status, "time now")?;
        Ok(())
    }

    pub fn key_down(&mut self, key: Key) -> std::result::Result<(), mount::thread::MountSendError> {
        if !self.pressed_keys.insert(key) {
            return Ok(());
        }
        match key {
            Key::D => self.mount.fixed_slew_ra(self.slew_speed as i32)?,
            Key::A => self.mount.fixed_slew_ra(-(self.slew_speed as i32))?,
            Key::W => self.mount.fixed_slew_dec(self.slew_speed as i32)?,
            Key::S => self.mount.fixed_slew_dec(-(self.slew_speed as i32))?,
            Key::R => self.slew_speed = (self.slew_speed + 1).min(9),
            Key::F => self.slew_speed = (self.slew_speed - 1).max(1),
            _ => (),
        }
        Ok(())
    }

    pub fn key_up(&mut self, key: Key) -> std::result::Result<(), mount::thread::MountSendError> {
        if !self.pressed_keys.remove(&key) {
            return Ok(());
        }
        match key {
            Key::D | Key::A => self.mount.fixed_slew_ra(0)?,
            Key::W | Key::S => self.mount.fixed_slew_dec(0)?,
            _ => (),
        }
        Ok(())
    }

    pub fn user_update(&mut self, user_update: UserUpdate) {
        self.mount.user_update(user_update)
    }
}
