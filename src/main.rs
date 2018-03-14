extern crate sdl2;

mod asicamera;
mod camera;
mod camera_feed;
mod display;

use camera::Camera;
use std::error::Error;
use std::io::BufRead;
use std::io::stdin;
use std::io::stdout;
use std::io::Write;
use std::sync::Arc;
use std::sync::Mutex;

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

fn repl(camera: Arc<Mutex<Camera>>) -> Result<(), Box<Error>> {
    let stdin = stdin();
    print!("> ");
    stdout().flush()?;
    for line in stdin.lock().lines() {
        let line = line?;
        let command = line.split(' ').collect::<Vec<_>>();
        match command.first() {
            Some(&"info") => {
                let camera = camera.lock().unwrap();
                for control in camera.controls() {
                    print_control(control)?;
                }
            }
            Some(cmd) if command.len() == 2 => {
                let mut ok = false;
                if let Ok(value) = command[1].parse() {
                    let camera = camera.lock().unwrap();
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
                let camera = camera.lock().unwrap();
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

fn try_main() -> Result<(), Box<Error>> {
    let num_cameras = Camera::num_cameras();
    if num_cameras == 0 {
        println!("No cameras found");
        return Ok(());
    }
    let camera = Camera::new(0)?;
    println!("Using camera: {}", camera.name());
    let camera = Arc::new(Mutex::new(camera));
    camera_feed::run_camera_feed(camera.clone(), false);
    repl(camera)?;
    Ok(())
}

fn main() {
    match try_main() {
        Ok(()) => (),
        Err(err) => println!("Error: {}", err),
    }
}
