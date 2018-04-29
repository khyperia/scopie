use camera::Camera;
use camera::CameraInfo;
use display::Image;
use display::display;
use std::error::Error;
use std::sync::Arc;
use std::sync::Mutex;
use std::sync::mpsc;
use std::thread;

#[derive(Clone, Default)]
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
        Ok(Arc::new(CameraFeed::new(camera, options)))
    }

    pub fn init(feed: &Arc<CameraFeed>, individual: bool) -> Result<(), Box<Error>> {
        feed.camera.init()?;
        let (send, recv) = mpsc::channel();
        let second_feed = feed.clone();
        thread::spawn(move || match display(&recv) {
            Ok(()) => (),
            Err(err) => println!("Display thread error: {}", err),
        });
        thread::spawn(move || {
            let result = if individual {
                second_feed.run_camera_exposures_individual(&send)
            } else {
                second_feed.run_camera_exposures_video(&send)
            };
            match result {
                Ok(()) => (),
                Err(err) => println!("Camera thread error: {}", err),
            }
        });
        Ok(())
    }

    fn new(camera: Camera, options: Mutex<ImageAdjustOptions>) -> CameraFeed {
        CameraFeed { camera, options }
    }

    pub fn camera(&self) -> &Camera {
        &self.camera
    }

    pub fn image_adjust_options(&self) -> &Mutex<ImageAdjustOptions> {
        &self.options
    }

    fn adjust_image(&self, data: Vec<u16>, width: u32, height: u32) -> Image {
        let options = self.options.lock().unwrap().clone();
        let (mut result, width, height) = if options.zoom && width >= 100 && height >= 100 {
            let mut result = Vec::with_capacity(100 * 100 * 4);
            let off_x = width as usize / 2 - 50;
            let off_y = height as usize / 2 - 50;
            for y in 0..100 {
                let base = (off_y + y) * width as usize + off_x;
                for item in &data[base..(base + 100)] {
                    let one = (item >> 8) as u8;
                    result.push(255);
                    result.push(one);
                    result.push(one);
                    result.push(one);
                }
            }
            (result, 100, 100)
        } else {
            let mut result = Vec::with_capacity(data.len() * 4);
            for item in data {
                let one = (item >> 8) as u8;
                result.push(255);
                result.push(one);
                result.push(one);
                result.push(one);
            }
            (result, width, height)
        };
        if options.cross {
            // red (first element)
            let half_width = width as usize / 2;
            for y in 0..(height as usize) {
                result[(y * width as usize + half_width) * 4 + 3] = 255;
            }
            let half_height = height as usize / 2;
            for x in 0..(width as usize) {
                result[(half_height * width as usize + x) * 4 + 3] = 255;
            }
        }
        Image::new(result, width, height)
    }

    fn run_camera_exposures_individual(
        &self,
        sender: &mpsc::Sender<Image>,
    ) -> Result<(), Box<Error>> {
        let (width, height) = (self.camera.width(), self.camera.height());
        loop {
            let exposed = Camera::expose(&self.camera)?;
            let converted = self.adjust_image(exposed, width, height);
            match sender.send(converted) {
                Ok(()) => (),
                Err(mpsc::SendError(_)) => break,
            }
        }
        self.camera.stop_exposure()?;
        Ok(())
    }

    fn run_camera_exposures_video(&self, sender: &mpsc::Sender<Image>) -> Result<(), Box<Error>> {
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
