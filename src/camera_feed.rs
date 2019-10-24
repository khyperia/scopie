use crate::camera::Camera;
use crate::camera::CameraInfo;
use crate::display::display;
use crate::Image;
use crate::Result;
use std::sync::mpsc;
use std::sync::Arc;
use std::sync::Mutex;
use std::thread;

#[derive(Clone, Default)]
pub struct ImageAdjustOptions {
    pub zoom: bool,
    pub cross: bool,
}

pub struct CameraFeed {
    info: CameraInfo,
    live: bool,
    options: Mutex<ImageAdjustOptions>,
}

impl CameraFeed {
    pub fn run(camera_index: u32, live: bool) -> Result<Arc<CameraFeed>> {
        let info = CameraInfo::new(camera_index)?;
        let options = Mutex::new(ImageAdjustOptions::default());
        Ok(Arc::new(CameraFeed {
            info,
            live,
            options,
        }))
    }

    pub fn init(feed: &Arc<CameraFeed>) -> Result<()> {
        let (send, recv) = mpsc::channel();
        let second_feed = feed.clone();
        println!("Init CameraFeed");
        thread::spawn(move || match display(&recv) {
            Ok(()) => (),
            Err(err) => println!("Display thread error: {}", err),
        });
        thread::spawn(move || {
            let camera = second_feed.info.open(second_feed.live);
            let result = second_feed.run_camera_exposures(camera, &send);
            match result {
                Ok(()) => (),
                Err(err) => println!("Camera thread error: {}", err),
            }
        });
        Ok(())
    }

    pub fn image_adjust_options(&self) -> &Mutex<ImageAdjustOptions> {
        &self.options
    }

    fn run_camera_exposures(&self, camera: Camera, sender: &mpsc::Sender<Image<u8>>) -> Result<()> {
        camera.start()?;
        loop {
            let exposed = camera.get()?;
            let converted = self.adjust_image(exposed);
            match sender.send(converted) {
                Ok(()) => (),
                Err(mpsc::SendError(_)) => break,
            }
        }
        camera.stop()?;
        Ok(())
    }
}
