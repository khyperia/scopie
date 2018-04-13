#[macro_use]
extern crate lazy_static;
extern crate regex;
extern crate sdl2;
extern crate serialport;
extern crate time;

mod asicamera;
mod camera;
mod camera_feed;
mod display;
mod dms;
mod mount;

use camera::Camera;
use camera::CameraInfo;
use camera_feed::CameraFeed;
use mount::Mount;
use std::error::Error;
use std::io::BufRead;
use std::io::Write;
use std::io::stdin;
use std::io::stdout;
use std::sync::Arc;

type Result<T> = std::result::Result<T, Box<Error>>;

fn print_control(control: &camera::Control) -> Result<()> {
    let (value, auto) = control.get()?;
    println!(
        "{} = {} ({}-{}; {}) a={}({}) w={}: {}",
        control.name(),
        value,
        control.min_value(),
        control.max_value(),
        control.default_value(),
        auto,
        control.is_auto_supported(),
        control.writable(),
        control.description(),
    );
    Ok(())
}

fn repl_camera(command: &[&str], camera: &CameraFeed) -> Result<bool> {
    let good_command = match command.first() {
        Some(&"info") if command.len() == 1 => {
            for control in camera.camera().controls() {
                print_control(control)?;
            }
            true
        }
        Some(&"zoom") if command.len() == 1 => {
            // invert
            camera.image_adjust_options().lock().unwrap().zoom ^= true;
            true
        }
        Some(&"cross") if command.len() == 1 => {
            camera.image_adjust_options().lock().unwrap().cross ^= true;
            true
        }
        Some(cmd) if command.len() == 2 => {
            let mut ok = false;
            if let Ok(value) = command[1].parse() {
                for control in camera.camera().controls() {
                    if control.name().eq_ignore_ascii_case(cmd) {
                        control.set(value, false)?;
                        ok = true;
                        break;
                    }
                }
            }
            ok
        }
        Some(cmd) if command.len() == 1 => {
            let mut ok = false;
            for control in camera.camera().controls() {
                if control.name().eq_ignore_ascii_case(cmd) {
                    print_control(control)?;
                    ok = true;
                    break;
                }
            }
            ok
        }
        Some(_) => false,
        None => true,
    };
    Ok(good_command)
}

fn repl_mount(command: &[&str], mount: &mut Mount) -> Result<bool> {
    let good_command = match command.first() {
        Some(&"help") if command.len() == 1 => {
            println!("hecking what");
            true
        }
        Some(&"pos") if command.len() == 1 => {
            let (ra, dec) = mount.get_ra_dec()?;
            println!(
                "{} {}",
                dms::print_dms(ra, true),
                dms::print_dms(dec, false)
            );
            true
        }
        Some(&"setpos") if command.len() == 3 => {
            let ra = dms::parse_dms(command[1]);
            let dec = dms::parse_dms(command[2]);
            if let (Some(ra), Some(dec)) = (ra, dec) {
                mount.overwrite_ra_dec(ra, dec)?;
                println!("ok");
                true
            } else {
                false
            }
        }
        Some(&"slew") if command.len() == 3 => {
            let ra = dms::parse_dms(command[1]);
            let dec = dms::parse_dms(command[2]);
            if let (Some(ra), Some(dec)) = (ra, dec) {
                mount.slew_ra_dec(ra, dec)?;
                println!("ok");
                true
            } else {
                false
            }
        }
        Some(&"cancel") if command.len() == 1 => {
            mount.cancel_slew()?;
            println!("ok");
            true
        }
        Some(&"mode") if command.len() == 1 => {
            println!("{}", mount.tracking_mode()?);
            true
        }
        Some(&"mode") if command.len() == 2 => {
            match command[1].parse() {
                Ok(mode) => {
                    mount.set_tracking_mode(mode)?;
                    println!("ok");
                }
                Err(err) => println!("{}", err),
            }
            true
        }
        Some(&"location") if command.len() == 1 => {
            let (lat, lon) = mount.location()?;
            println!(
                "{} {}",
                dms::print_dms(lat, false),
                dms::print_dms(lon, false)
            );
            true
        }
        Some(&"location") if command.len() == 3 => {
            let lat = dms::parse_dms(command[1]);
            let lon = dms::parse_dms(command[2]);
            if let (Some(lat), Some(lon)) = (lat, lon) {
                mount.set_location(lat, lon)?;
                println!("ok");
                true
            } else {
                false
            }
        }
        Some(&"time") if command.len() == 1 => {
            println!("{}", mount.time()?);
            true
        }
        Some(&"time") if command.len() == 2 && command[1] == "now" => {
            mount.set_time(mount::MountTime::now())?;
            println!("ok");
            true
        }
        Some(_) => false,
        None => true,
    };
    Ok(good_command)
}

fn repl_one(
    command: &[&str],
    camera: &mut Option<Arc<CameraFeed>>,
    mount: &mut Option<Mount>,
) -> Result<bool> {
    if let Some(camera) = camera.as_ref() {
        return Ok(repl_camera(command, camera)?);
    }
    if let Some(mount) = mount.as_mut() {
        return Ok(repl_mount(command, mount)?);
    }
    let good_command = match command.first() {
        Some(&"list") if command.len() == 1 => {
            let num_cameras = Camera::num_cameras();
            for i in 0..num_cameras {
                let camera = CameraInfo::new(i)?;
                println!("cameras[{}] = {}", i, camera.name());
            }
            for port in Mount::list() {
                println!("serial: {}", port);
            }
            true
        }
        Some(&"open") if command.len() == 2 => if let Ok(value) = command[1].parse() {
            let new_camera = CameraFeed::run(value, false)?;
            println!("Opened: {}", new_camera.camera().name());
            *camera = Some(new_camera);
            true
        } else {
            false
        },
        Some(&"mount") if command.len() == 2 => {
            let new_mount = Mount::new(command[1])?;
            println!("Opened mount connection");
            *mount = Some(new_mount);
            true
        }
        Some(_) => false,
        None => true,
    };
    Ok(good_command)
}

fn try_main() -> Result<()> {
    let mut camera = None;
    let mut mount = None;
    let stdin = stdin();
    print!("> ");
    stdout().flush()?;
    for line in stdin.lock().lines() {
        let line = line?;
        let command = line.split(' ').collect::<Vec<_>>();
        // maybe we should catch/print error here, instead of exiting
        let ok = repl_one(&command, &mut camera, &mut mount)?;
        if !ok {
            println!("Unknown command: {}", line);
        }
        print!("> ");
        stdout().flush()?;
    }
    Ok(())
}

fn main() {
    match try_main() {
        Ok(()) => (),
        Err(err) => println!("Error: {}", err),
    }
}
