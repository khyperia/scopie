use crate::{
    alg::Rect,
    camera::{self, qhycamera::ControlId},
    Result, UiThread,
};
use anyhow::anyhow;
use std::{
    sync::mpsc,
    thread::spawn,
    time::{Duration, Instant},
};

use super::interface::ROIImage;

enum CameraCommand {
    SetControl(ControlId, f64),
    Start,
    Stop,
    ToggleLive,
    SetROI(Option<Rect<usize>>),
}

#[derive(Clone, Debug)]
pub struct CameraData {
    pub controls: Vec<camera::interface::ControlValue>,
    pub name: String,
    pub cmd_status: String,
    pub running: bool,
    pub is_live: bool,
    pub exposure_start: Instant,
    pub exposure_duration: Duration,
    pub effective_area: Option<Rect<usize>>,
    pub current_roi: Option<Rect<usize>>,
}

pub struct CameraAsync {
    send: mpsc::Sender<CameraCommand>,
    recv: mpsc::Receiver<CameraData>,
    pub data: CameraData,
}

impl CameraAsync {
    pub fn new(send_image: mpsc::Sender<ROIImage>, ui_thread: UiThread) -> Self {
        let (send_cmd, recv_cmd) = mpsc::channel();
        let (send_result, recv_result) = mpsc::channel();
        spawn(
            move || match run(recv_cmd, send_image, send_result, ui_thread) {
                Ok(()) => (),
                Err(err) => println!("Camera thread error: {:?}", err),
            },
        );
        Self {
            send: send_cmd,
            recv: recv_result,
            data: CameraData {
                controls: Vec::new(),
                name: String::new(),
                cmd_status: String::new(),
                running: false,
                is_live: false,
                exposure_start: Instant::now(),
                exposure_duration: Duration::from_secs(0),
                effective_area: None,
                current_roi: None,
            },
        }
    }

    pub fn update(&mut self) {
        loop {
            match self.recv.try_recv() {
                Ok(data) => self.data = data,
                // TODO: Disconnected
                Err(mpsc::TryRecvError::Disconnected) => return,
                Err(mpsc::TryRecvError::Empty) => return,
            }
        }
    }

    pub fn set_control(&self, id: ControlId, value: f64) -> std::result::Result<(), ()> {
        self.send
            .send(CameraCommand::SetControl(id, value))
            .map_err(|_| ())
    }

    pub fn start(&self) -> std::result::Result<(), ()> {
        self.send.send(CameraCommand::Start).map_err(|_| ())
    }

    pub fn stop(&self) -> std::result::Result<(), ()> {
        self.send.send(CameraCommand::Stop).map_err(|_| ())
    }

    pub fn toggle_live(&self) -> std::result::Result<(), ()> {
        self.send.send(CameraCommand::ToggleLive).map_err(|_| ())
    }

    pub fn set_roi(&self, roi: Option<Rect<usize>>) -> std::result::Result<(), ()> {
        self.send.send(CameraCommand::SetROI(roi)).map_err(|_| ())
    }
}

fn run(
    recv: mpsc::Receiver<CameraCommand>,
    send_image: mpsc::Sender<ROIImage>,
    send_camera: mpsc::Sender<CameraData>,
    ui_thread: UiThread,
) -> Result<()> {
    {
        if let Ok(img) = crate::read_png("telescope.2019-11-21.19-39-54.png") {
            send_image
                .send(img.into())
                .map_err(|_| anyhow!("unable to send initial test image"))?;
        }
    }

    let mut camera = Some(camera::interface::autoconnect(false)?);
    let mut running = false;
    let mut exposure_duration = Duration::default();
    let mut cmd_status = String::new();
    let mut exposure_start = Instant::now();
    loop {
        let mut had_cmd = false;
        let mut had_bad_cmd = false;
        loop {
            match recv.try_recv() {
                Ok(cmd) => match run_command(&mut camera, cmd, &mut running) {
                    Ok(()) => had_cmd = true,
                    Err(err) => {
                        had_bad_cmd = true;
                        cmd_status = format!("{}", err);
                    }
                },
                Err(mpsc::TryRecvError::Empty) => break,
                Err(mpsc::TryRecvError::Disconnected) => return Ok(()),
            }
        }
        if had_cmd && !had_bad_cmd {
            cmd_status.clear();
        }

        let camera = camera.as_mut().unwrap();

        let values = camera.controls().iter().map(|c| c.to_value()).collect();

        let data = CameraData {
            controls: values,
            name: camera.name().to_string(),
            cmd_status: cmd_status.clone(),
            running,
            is_live: camera.use_live(),
            exposure_start,
            exposure_duration,
            effective_area: Some(camera.effective_area()),
            current_roi: Some(camera.current_roi()),
        };

        match send_camera.send(data) {
            Ok(()) => (),
            Err(_) => return Ok(()),
        }
        ui_thread.trigger();
        if running {
            if camera.use_live() {
                camera.try_start_live()?;
                loop {
                    match camera.get_live() {
                        Some(frame) => {
                            send_image.send(frame)?;
                            break;
                        }
                        None => {
                            let limit = Duration::from_millis(10);
                            if exposure_duration > limit || Instant::now() - exposure_start > limit
                            {
                                std::thread::sleep(Duration::from_millis(1));
                            }
                        }
                    }
                }
            } else {
                let single = camera.exp_single()?;
                send_image.send(single)?;
            }
            let new_exposure_start = Instant::now();
            exposure_duration = new_exposure_start - exposure_start;
            exposure_start = new_exposure_start;
        } else {
            std::thread::sleep(Duration::from_millis(1));
        }
    }
}

fn run_command(
    camera: &mut Option<camera::interface::Camera>,
    cmd: CameraCommand,
    running: &mut bool,
) -> Result<()> {
    match cmd {
        CameraCommand::SetControl(id, val) => {
            let camera = camera.as_mut().unwrap();
            camera.try_stop_live()?;
            for control in camera.controls() {
                if control.id() == id {
                    control.set(val)?;
                }
            }
        }
        CameraCommand::Start => {
            if !*running {
                *running = true;
            }
        }
        CameraCommand::Stop => {
            if *running {
                *running = false;
            }
        }
        CameraCommand::ToggleLive => {
            if let Some(camera) = camera {
                camera.try_stop_live()?;
            }
            let info = camera
                .take()
                .map(|camera| (camera.info().clone(), camera.use_live()));
            if let Some((info, old_use_live)) = info {
                *camera = Some(info.open(!old_use_live)?);
            }
        }
        CameraCommand::SetROI(roi) => {
            if let Some(ref mut camera) = camera {
                camera.try_stop_live()?;
                match roi {
                    Some(roi) => camera.set_roi(roi)?,
                    None => camera.unset_roi()?,
                }
            }
        }
    }
    Ok(())
}
