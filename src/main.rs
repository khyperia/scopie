extern crate sdl2;

mod asicamera;
mod camera;
mod camera_feed;
mod display;

use camera::Camera;
use camera::CameraInfo;
use std::env::args;
use std::error::Error;
use std::io::BufRead;
use std::io::Write;
use std::io::stdin;
use std::io::stdout;

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

fn repl(camera: &Camera) -> Result<(), Box<Error>> {
    let stdin = stdin();
    print!("> ");
    stdout().flush()?;
    for line in stdin.lock().lines() {
        let line = line?;
        let command = line.split(' ').collect::<Vec<_>>();
        match command.first() {
            Some(&"info") => for control in camera.controls() {
                print_control(control)?;
            },
            Some(cmd) if command.len() == 2 => {
                let mut ok = false;
                if let Ok(value) = command[1].parse() {
                    for control in camera.controls() {
                        if control.name().eq_ignore_ascii_case(cmd) {
                            control.set(value, false)?;
                            ok = true;
                            break;
                        }
                    }
                }
                if !ok {
                    println!("Unknown command: {}", line);
                }
            }
            Some(cmd) if command.len() == 1 => {
                let mut ok = false;
                for control in camera.controls() {
                    if control.name().eq_ignore_ascii_case(cmd) {
                        print_control(control)?;
                        ok = true;
                        break;
                    }
                }
                if !ok {
                    println!("Unknown command: {}", line);
                }
            }
            Some(cmd) => {
                println!("Unknown command: {}", line);
            }
            None => (),
        }
        print!("> ");
        stdout().flush()?;
    }
    Ok(())
}

fn list_cameras() -> Result<(), Box<Error>> {
    let num_cameras = Camera::num_cameras();
    for i in 0..num_cameras {
        let camera = CameraInfo::new(i)?;
        println!("cameras[{}] = {}", i, camera.name());
    }
    if num_cameras == 0 {
        Err("No cameras found".to_owned())?
    } else {
        Ok(())
    }
}

fn try_main() -> Result<(), Box<Error>> {
    let args = args().collect::<Vec<_>>();
    if args.len() == 1 {
        list_cameras()?;
        return Ok(());
    }
    if args.len() != 2 {
        println!("Usage: ./scopie [camera ID]");
        return Ok(());
    }

    let num_cameras = Camera::num_cameras();
    if num_cameras == 0 {
        println!("No cameras found");
        return Ok(());
    }
    let camera = CameraInfo::new(0)?.open()?;
    println!("Using camera: {}", camera.name());
    let camera = ::std::sync::Arc::new(camera);
    camera_feed::run_camera_feed(camera.clone(), false);
    repl(&camera)?;
    Ok(())
}

fn main() {
    match try_main() {
        Ok(()) => (),
        Err(err) => println!("Error: {}", err),
    }
}
