extern crate sdl2;

mod asicamera;
mod camera;
mod camera_feed;
mod display;

use camera::Camera;
use camera::CameraInfo;
use camera_feed::CameraFeed;
use std::error::Error;
use std::io::BufRead;
use std::io::Write;
use std::io::stdin;
use std::io::stdout;
use std::sync::Arc;

fn print_control(control: &camera::Control) -> Result<(), Box<Error>> {
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

fn repl_camera(command: Vec<&str>, camera: &CameraFeed) -> Result<bool, Box<Error>> {
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

fn repl_one(command: Vec<&str>, camera: &mut Option<Arc<CameraFeed>>) -> Result<bool, Box<Error>> {
    if let Some(camera) = camera.as_ref() {
        return Ok(repl_camera(command, camera)?);
    }
    let good_command = match command.first() {
        Some(&"list") if command.len() == 1 => {
            let num_cameras = Camera::num_cameras();
            for i in 0..num_cameras {
                let camera = CameraInfo::new(i)?;
                println!("cameras[{}] = {}", i, camera.name());
            }
            true
        }
        Some(&"open") if command.len() == 2 => if let Ok(value) = command[1].parse() {
            let new_camera = CameraFeed::run(value)?;
            *camera = Some(new_camera);
            true
        } else {
            false
        },
        Some(_) => false,
        None => true,
    };
    Ok(good_command)
}

fn try_main() -> Result<(), Box<Error>> {
    let mut camera = None;
    let stdin = stdin();
    print!("> ");
    stdout().flush()?;
    for line in stdin.lock().lines() {
        let line = line?;
        let command = line.split(' ').collect::<Vec<_>>();
        // maybe we should catch/print error here, instead of exiting
        let ok = repl_one(command, &mut camera)?;
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
