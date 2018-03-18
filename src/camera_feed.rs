use camera::Camera;
use camera::CameraInfo;
use display::Image;
use display::display;
use std::error::Error;
use std::sync::Arc;
use std::sync::Mutex;
use std::sync::mpsc;
use std::thread;

#[derive(Clone, Copy, Default)]
pub struct ImageAdjustOptions {
    pub zoom: bool,
    pub cross: bool,
}

pub struct CameraFeed {
    camera: Camera,
    options: Mutex<ImageAdjustOptions>,
}

impl CameraFeed {
    pub fn run(camera_index: u32) -> Result<Arc<CameraFeed>, Box<Error>> {
        let camera = CameraInfo::new(camera_index)?.open()?;
        let options = Mutex::new(ImageAdjustOptions::default());
        let (send, recv) = mpsc::channel();
        let feed = Arc::new(CameraFeed::new(camera, options));
        let second_feed = feed.clone();
        thread::spawn(move || match display(&recv) {
            Ok(()) => (),
            Err(err) => println!("Display thread error: {}", err),
        });
        thread::spawn(move || match second_feed.run_camera_exposures_video(send) {
            Ok(()) => (),
            Err(err) => println!("Camera thread error: {}", err),
        });
        Ok(feed)
    }

    fn new(
        camera: Camera,
        options: Mutex<ImageAdjustOptions>,
    ) -> CameraFeed {
        CameraFeed {
            camera,
            options,
        }
    }

    pub fn camera(&self) -> &Camera {
        &self.camera
    }

    pub fn image_adjust_options(&self) -> &Mutex<ImageAdjustOptions> {
        &self.options
    }

    fn adjust_image(&self, data: Vec<u16>, width: u32, height: u32) -> Image {
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

    fn run_camera_exposures(&self, sender: mpsc::Sender<Image>) -> Result<(), Box<Error>> {
        let (width, height) = (self.camera.width(), self.camera.height());
        loop {
            let exposed = Camera::expose(&self.camera)?;
            let converted = self.adjust_image(exposed, width, height);
            match sender.send(converted) {
                Ok(()) => (),
                Err(mpsc::SendError(_)) => break,
            }
        }
        Ok(())
    }

    fn run_camera_exposures_video(&self, sender: mpsc::Sender<Image>) -> Result<(), Box<Error>> {
        let (width, height) = (self.camera.width(), self.camera.height());
        self.camera.start_video_capture()?;
        loop {
            let data = self.camera.get_video_data()?;
            let image = self.adjust_image(data, width, height);
            match sender.send(image) {
                Ok(()) => (),
                Err(mpsc::SendError(_)) => break,
            }
        }
        self.camera.stop_video_capture()?;
        Ok(())
    }
}
