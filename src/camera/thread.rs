use crate::{
    alg::{self, Rect},
    camera::{self, interface::TimestampImage, qhycamera::ControlId},
    Result, UiThread,
};
use anyhow::anyhow;
use std::{
    sync::mpsc,
    thread::spawn,
    time::{Duration, Instant},
};

use super::interface::CpuTexture;

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
    pub camera_id: String,
    pub cmd_status: String,
    pub running: bool,
    pub is_live: bool,
    pub current_roi: Option<Rect<usize>>,
}

pub struct CameraAsync {
    send: mpsc::Sender<CameraCommand>,
    recv: mpsc::Receiver<CameraData>,
    pub data: CameraData,
}

impl CameraAsync {
    pub fn new(
        send_image: mpsc::Sender<TimestampImage<CpuTexture<u16>>>,
        ui_thread: UiThread,
    ) -> Self {
        let (send_cmd, recv_cmd) = mpsc::channel();
        let (send_result, recv_result) = mpsc::channel();
        spawn(move || {
            match run(recv_cmd, send_image, send_result, ui_thread) {
                Ok(()) => (),
                Err(err) => println!("Camera thread error: {:?}", err),
            }
            println!("camera thread quit");
        });
        Self {
            send: send_cmd,
            recv: recv_result,
            data: CameraData {
                controls: Vec::new(),
                camera_id: String::new(),
                cmd_status: String::new(),
                running: false,
                is_live: false,
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
    send_image: mpsc::Sender<TimestampImage<CpuTexture<u16>>>,
    send_camera: mpsc::Sender<CameraData>,
    ui_thread: UiThread,
) -> Result<()> {
    {
        if let Ok(img) = alg::read_png("telescope.2019-11-21.19-39-54.png") {
            send_image
                .send(img.into())
                .map_err(|_| anyhow!("unable to send initial test image"))?;
        }
    }

    let mut camera = camera::interface::autoconnect()?;
    let mut running = false;
    let mut cmd_status = String::new();
    const UPDATE_RATE: Duration = Duration::from_millis(1);
    const IDLE_RATE: Duration = Duration::from_millis(10);
    let mut next_data_update = Instant::now();
    let mut next_idle_period = Instant::now() + IDLE_RATE;
    loop {
        let mut had_cmd = false;
        let mut had_bad_cmd = false;

        let deadline = next_idle_period;
        loop {
            let now = Instant::now();
            let duration = if deadline < now {
                Duration::ZERO
            } else {
                deadline - now
            };
            match recv.recv_timeout(duration) {
                Ok(cmd) => match run_command(&mut camera, cmd, &mut running) {
                    Ok(()) => had_cmd = true,
                    Err(err) => {
                        had_bad_cmd = true;
                        cmd_status = format!("{}", err);
                    }
                },
                Err(mpsc::RecvTimeoutError::Timeout) => break,
                Err(mpsc::RecvTimeoutError::Disconnected) => return Ok(()),
            }
        }
        if had_cmd && !had_bad_cmd {
            cmd_status.clear();
        }

        let now = Instant::now();
        next_idle_period += IDLE_RATE;
        if next_idle_period < now {
            next_idle_period = now;
        }

        if now > next_data_update {
            next_data_update += UPDATE_RATE;
            if now > next_data_update {
                next_data_update = now + UPDATE_RATE;
            }

            let values = camera.controls().iter().map(|c| c.to_value()).collect();

            let data = CameraData {
                controls: values,
                camera_id: camera.camera_id().to_string(),
                cmd_status: cmd_status.clone(),
                running,
                is_live: camera.use_live(),
                current_roi: Some(camera.current_roi()),
            };

            match send_camera.send(data) {
                Ok(()) => (),
                Err(_) => return Ok(()),
            }
            ui_thread.trigger();
        }

        if running {
            if camera.use_live() {
                camera.try_start_live()?;
                if let Some(frame) = camera.get_live() {
                    send_image.send(frame)?;
                }
            } else {
                let single = camera.exp_single()?;
                send_image.send(single)?;
            }
        }
    }
}

fn run_command(
    camera: &mut camera::interface::Camera,
    cmd: CameraCommand,
    running: &mut bool,
) -> Result<()> {
    match cmd {
        CameraCommand::SetControl(id, val) => {
            // camera.try_stop_live()?;
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
            camera.try_stop_live()?;
            let camera_id = camera.camera_id().to_string();
            let use_live = !camera.use_live();
            camera.dispose()?;
            *camera = camera::interface::Camera::open(camera_id, use_live)?;
        }
        CameraCommand::SetROI(roi) => {
            // camera.try_stop_live()?;
            match roi {
                Some(roi) => camera.set_roi(roi)?,
                None => camera.unset_roi()?,
            }
        }
    }
    Ok(())
}
