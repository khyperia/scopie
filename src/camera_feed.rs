use camera::Camera;
use display::Image;
use display::display;
use std::error::Error;
use std::sync::Arc;
use std::sync::mpsc;
use std::thread;

fn adjust_image(data: Vec<u16>, width: u32, height: u32) -> Image {
    let mut result = Vec::with_capacity(data.len() * 4);
    for item in data {
        let one = (item >> 8) as u8;
        result.push(one);
        result.push(one);
        result.push(one);
        result.push(255);
    }

    Image::new(result, width, height)
}

fn run_camera_exposures(sender: mpsc::Sender<Image>, camera: Camera) -> Result<(), Box<Error>> {
    let (width, height) = (camera.width(), camera.height());
    loop {
        let exposed = Camera::expose(&camera)?;
        //println!("snap");
        let converted = adjust_image(exposed, width, height);
        match sender.send(converted) {
            Ok(()) => (),
            Err(mpsc::SendError(_)) => break,
        }
    }
    Ok(())
}

pub fn run_camera_feed(camera: Arc<Camera>, block: bool) {
    let (send, recv) = mpsc::channel();
    thread::spawn(move || match display(recv) {
        Ok(()) => (),
        Err(err) => println!("Display thread error: {}", err),
    });
    let func = move || match camera.camera_loop(send, adjust_image) {
        Ok(()) => (),
        Err(err) => println!("Camera thread error: {}", err),
    };

    if block {
        func()
    } else {
        thread::spawn(func);
    }
}
