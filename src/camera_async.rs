use crate::{camera, qhycamera::ControlId, Result, SendUserUpdate, UserUpdate};
use glutin::event_loop::EventLoopClosed;
use std::{
    sync::{mpsc, Arc},
    thread::spawn,
    time::{Duration, Instant},
};

enum CameraCommand {
    SetControl(ControlId, f64),
    Start,
    Stop,
    ToggleLive,
}

#[derive(Clone)]
pub struct CameraData {
    pub controls: Vec<camera::ControlValue>,
    pub name: String,
    pub cmd_status: String,
    pub running: bool,
    pub is_live: bool,
    pub exposure_start: Instant,
    pub exposure_duration: Duration,
}

pub struct CameraAsync {
    send: mpsc::Sender<CameraCommand>,
    pub data: CameraData,
}

impl CameraAsync {
    pub fn new(send_user_update: SendUserUpdate) -> Self {
        let (send_cmd, recv_cmd) = mpsc::channel();
        spawn(move || match run(recv_cmd, send_user_update) {
            Ok(()) => (),
            Err(err) => println!("Mount thread error: {}", err),
        });
        Self {
            send: send_cmd,
            data: CameraData {
                controls: Vec::new(),
                name: String::new(),
                cmd_status: String::new(),
                running: false,
                is_live: false,
                exposure_start: Instant::now(),
                exposure_duration: Duration::from_secs(0),
            },
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

    pub fn user_update(&mut self, user_update: UserUpdate) {
        if let UserUpdate::CameraUpdate(data) = user_update {
            self.data = data;
        }
    }
}

fn run(recv: mpsc::Receiver<CameraCommand>, send: SendUserUpdate) -> Result<()> {
    let mut camera = Some(camera::autoconnect(false)?);
    let mut running = false;
    let mut exposure_duration = Duration::default();
    let mut cmd_status = String::new();
    loop {
        let mut had_cmd = false;
        let mut had_bad_cmd = false;
        loop {
            match recv.try_recv() {
                Ok(cmd) => match run_one(&mut camera, cmd, &mut running) {
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
        let values = camera
            .as_mut()
            .unwrap()
            .controls()
            .iter()
            .map(|c| c.to_value())
            .collect();
        // this is slightly too early, but eh, close enough
        let exposure_start = Instant::now();

        let data = CameraData {
            controls: values,
            name: camera
                .as_ref()
                .map_or(String::new(), |c| c.name().to_string()),
            cmd_status: cmd_status.clone(),
            running,
            is_live: camera.as_mut().unwrap().use_live(),
            exposure_start,
            exposure_duration,
        };
        match send.send_event(UserUpdate::CameraUpdate(data)) {
            Ok(()) => (),
            Err(EventLoopClosed) => return Ok(()),
        }
        if running {
            if camera.as_mut().unwrap().use_live() {
                let exposure_start = Instant::now();
                loop {
                    match camera.as_mut().unwrap().get_live() {
                        Some(frame) => {
                            exposure_duration = Instant::now() - exposure_start;
                            send.send_event(UserUpdate::CameraData(Arc::new(frame)))?;
                            break;
                        }
                        None => {
                            std::thread::sleep(Duration::from_millis(1));
                        }
                    }
                }
            } else {
                camera.as_mut().unwrap().start_single()?;
                let single = camera.as_mut().unwrap().get_single()?;
                exposure_duration = Instant::now() - exposure_start;
                send.send_event(UserUpdate::CameraData(Arc::new(single)))?;
            }
        } else {
            std::thread::sleep(Duration::from_millis(1));
        }
    }
}

fn run_one(
    camera: &mut Option<camera::Camera>,
    cmd: CameraCommand,
    running: &mut bool,
) -> Result<()> {
    match cmd {
        CameraCommand::SetControl(id, val) => {
            for control in camera.as_mut().unwrap().controls() {
                if control.id() == id {
                    // TODO: Error message feedback
                    control.set(val)?;
                }
            }
        }
        CameraCommand::Start => {
            if !*running {
                *running = true;
                camera.as_mut().unwrap().start()?;
            }
        }
        CameraCommand::Stop => {
            if *running {
                *running = false;
                camera.as_mut().unwrap().stop()?;
            }
        }
        CameraCommand::ToggleLive => {
            if let Some(ref camera) = camera {
                if *running {
                    camera.stop()?;
                }
            }
            let info = camera
                .take()
                .map(|camera| (camera.info().clone(), camera.use_live()));
            if let Some((info, old_use_live)) = info {
                *camera = Some(info.open(!old_use_live)?);
            }
            if let Some(ref camera) = camera {
                if *running {
                    camera.start()?;
                }
            }
        }
    }
    Ok(())
}
