use crate::{dms::Angle, mount, Result};
use khygl::display::Key;
use std::{
    fmt::Write,
    time::{Duration, Instant},
};

pub struct MountDisplay {
    pub mount: mount::Mount,
    last_update: Instant,
    slew_speed: u32,
    cached_status: String,
}

impl MountDisplay {
    pub fn new(mount: mount::Mount) -> Self {
        Self {
            mount,
            last_update: Instant::now(),
            slew_speed: 1,
            cached_status: String::new(),
        }
    }

    pub fn cmd(&mut self, command: &[&str]) -> Result<bool> {
        match command {
            ["setpos", ra, dec] => {
                let ra = Angle::parse(ra);
                let dec = Angle::parse(dec);
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.reset_ra(ra)?;
                    self.mount.reset_dec(dec)?;
                } else {
                    return Ok(false);
                }
            }
            ["syncpos", ra, dec] => {
                let ra = Angle::parse(ra);
                let dec = Angle::parse(dec);
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.sync_ra_dec(ra, dec)?;
                } else {
                    return Ok(false);
                }
            }
            ["slew", ra, dec] => {
                let ra = Angle::parse(ra);
                let dec = Angle::parse(dec);
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.slew_ra_dec(ra, dec)?;
                } else {
                    return Ok(false);
                }
            }
            ["slowslew", ra, dec] => {
                let ra = Angle::parse(ra);
                let dec = Angle::parse(dec);
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.slow_goto_ra(ra)?;
                    self.mount.slow_goto_dec(dec)?;
                } else {
                    return Ok(false);
                }
            }
            ["azaltslew", az, alt] => {
                let az = Angle::parse(az);
                let alt = Angle::parse(alt);
                if let (Some(az), Some(alt)) = (az, alt) {
                    self.mount.slew_az_alt(az, alt)?;
                } else {
                    return Ok(false);
                }
            }
            ["cancel"] => {
                self.mount.cancel_slew()?;
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
                self.mount.set_time(mount::MountTime::now())?;
            }
            _ => return Ok(false),
        }
        Ok(true)
    }

    pub fn status(&mut self, status: &mut String) -> Result<()> {
        let now = Instant::now();
        if (now - self.last_update).as_secs() > 0 {
            self.last_update += Duration::from_secs(1);
            self.cached_status.clear();
            let (ra, dec) = self.mount.get_ra_dec()?;
            writeln!(
                self.cached_status,
                "RA/Dec: {} {}",
                ra.fmt_hours(),
                dec.fmt_degrees()
            )?;
            let (az, alt) = self.mount.get_az_alt()?;
            writeln!(
                self.cached_status,
                "Az/Alt: {} {}",
                az.fmt_degrees(),
                alt.fmt_degrees()
            )?;
            writeln!(self.cached_status, "Aligned: {}", self.mount.aligned()?)?;
            writeln!(
                self.cached_status,
                "Tracking mode: {}",
                self.mount.tracking_mode()?
            )?;
            let (lat, lon) = self.mount.location()?;
            writeln!(
                self.cached_status,
                "Location: {} {}",
                lat.fmt_degrees(),
                lon.fmt_degrees()
            )?;
            writeln!(self.cached_status, "Time: {}", self.mount.time()?)?;
        }
        write!(status, "{}", self.cached_status)?;
        writeln!(status, "Slew speed: {}", self.slew_speed)?;
        writeln!(status, "setpos [ra] [dec]")?;
        writeln!(status, "syncpos [ra] [dec]")?;
        writeln!(status, "slew [ra] [dec]")?;
        writeln!(status, "slowslew [ra] [dec]")?;
        writeln!(status, "azaltslew [az] [alt]")?;
        writeln!(status, "cancel")?;
        writeln!(status, "mode [Off|AltAz|Equatorial|SiderealPec]")?;
        writeln!(status, "location [lat] [lon]")?;
        writeln!(status, "time now")?;
        Ok(())
    }

    pub fn key_down(&mut self, key: Key) -> Result<()> {
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

    pub fn key_up(&mut self, key: Key) -> Result<()> {
        match key {
            Key::D | Key::A => self.mount.fixed_slew_ra(0)?,
            Key::W | Key::S => self.mount.fixed_slew_dec(0)?,
            _ => (),
        }
        Ok(())
    }
}
