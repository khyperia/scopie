use crate::{dms::Angle, mount, Result};
use sdl2::{
    render::{TextureCreator, WindowCanvas},
    video::WindowContext,
};
use std::time::Instant;

pub struct MountDisplay {
    mount: mount::Mount,
}

impl MountDisplay {
    pub fn new(mount: mount::Mount) -> Self {
        Self { mount }
    }

    pub fn cmd(&mut self, command: &[&str]) -> Result<bool> {
        match command {
            ["pos"] => {
                let (ra, dec) = self.mount.get_ra_dec()?;
                println!("{} {}", ra.fmt_hours(), dec.fmt_degrees());
            }
            ["setpos", ra, dec] => {
                let ra = Angle::parse(ra);
                let dec = Angle::parse(dec);
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.overwrite_ra_dec(ra, dec)?;
                    println!("ok");
                } else {
                    return Ok(false);
                }
            }
            ["slew", ra, dec] => {
                let ra = Angle::parse(ra);
                let dec = Angle::parse(dec);
                if let (Some(ra), Some(dec)) = (ra, dec) {
                    self.mount.slew_ra_dec(ra, dec)?;
                    println!("ok");
                } else {
                    return Ok(false);
                }
            }
            ["cancel"] => {
                self.mount.cancel_slew()?;
                println!("ok");
            }
            ["mode"] => println!("{}", self.mount.tracking_mode()?),
            ["mode", mode] => match mode.parse() {
                Ok(mode) => {
                    self.mount.set_tracking_mode(mode)?;
                    println!("ok");
                }
                Err(err) => println!("{}", err),
            },
            ["location"] => {
                let (lat, lon) = self.mount.location()?;
                println!("{} {}", lat.fmt_degrees(), lon.fmt_degrees());
            }
            ["location", lat, lon] => {
                let lat = Angle::parse(lat);
                let lon = Angle::parse(lon);
                if let (Some(lat), Some(lon)) = (lat, lon) {
                    self.mount.set_location(lat, lon)?;
                    println!("ok");
                } else {
                    return Ok(false);
                }
            }
            ["time"] => println!("{}", self.mount.time()?),
            ["time", "now"] => {
                self.mount.set_time(mount::MountTime::now())?;
                println!("ok");
            }
            ["aligned"] => {
                let aligned = self.mount.aligned()?;
                println!("{}", aligned);
            }
            ["ping"] => {
                let now = Instant::now();
                let ok = self.mount.echo('U' as u8)? == 'U' as u8;
                let duration = now.elapsed();
                let duration_seconds =
                    duration.as_secs() as f32 + duration.subsec_nanos() as f32 * 1e-9;
                println!("{} seconds (ok={})", duration_seconds, ok);
            }
            _ => return Ok(false),
        }
        Ok(true)
    }

    pub fn draw(&mut self, canvas: &WindowCanvas) -> Result<()> {
        // TODO
        Ok(())
    }
}
