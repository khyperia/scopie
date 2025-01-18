use crate::{dms::Angle, mount::interface::*, Result, UiThread};
use anyhow::anyhow;
use std::{
    sync::mpsc,
    thread::spawn,
    time::{Duration, Instant},
};

type MountCommand = Box<dyn FnOnce(&mut Mount) -> Result<()> + Send>;

#[derive(Default, Clone, Debug)]
pub struct MountData {
    pub ra_dec: (Angle, Angle),
    pub az_alt: (Angle, Angle),
    pub aligned: bool,
    pub tracking_mode: TrackingMode,
    pub location: (Angle, Angle),
    pub time: MountTime,
}

pub struct MountAsync {
    send: mpsc::Sender<MountCommand>,
    recv: mpsc::Receiver<MountData>,
    pub data: MountData,
}

impl MountAsync {
    pub fn new(ui_thread: UiThread) -> Self {
        let (send_cmd, recv_cmd) = mpsc::channel();
        let (send_data, recv_data) = mpsc::channel();
        spawn(move || {
            match run(recv_cmd, send_data, ui_thread) {
                Ok(()) => (),
                Err(err) => println!("Mount thread error: {}", err),
            }
            println!("mount thread quit");
        });
        Self {
            send: send_cmd,
            recv: recv_data,
            data: MountData::default(),
        }
    }

    pub fn update(&mut self) {
        while let Ok(data) = self.recv.try_recv() {
            self.data = data;
        }
    }

    fn send(&self, cmd: impl FnOnce(&mut Mount) -> Result<()> + Send + 'static) -> Result<()> {
        match self.send.send(Box::new(cmd)) {
            Ok(()) => Ok(()),
            Err(mpsc::SendError(_)) => Err(anyhow!("mount send error")),
        }
    }

    pub fn slew(&self, ra: Angle, dec: Angle) -> Result<()> {
        self.send(move |mount| mount.slew_ra_dec(ra, dec))
    }
    pub fn sync(&self, ra: Angle, dec: Angle) -> Result<()> {
        self.send(move |mount| mount.sync_ra_dec(ra, dec))
    }
    pub fn slew_azalt(&self, az: Angle, alt: Angle) -> Result<()> {
        self.send(move |mount| mount.slew_az_alt(az, alt))
    }
    pub fn cancel(&self) -> Result<()> {
        self.send(move |mount| mount.cancel_slew())
    }
    pub fn set_tracking_mode(&self, mode: TrackingMode) -> Result<()> {
        self.send(move |mount| mount.set_tracking_mode(mode))
    }
    pub fn set_location(&self, lat: Angle, lon: Angle) -> Result<()> {
        self.send(move |mount| mount.set_location(lat, lon))
    }
    pub fn set_time_now(&self) -> Result<()> {
        self.send(move |mount| mount.set_time(MountTime::now()))
    }
    pub fn fixed_slew_ra(&self, speed: i32) -> Result<()> {
        println!("fixed_slew_ra {}", speed);
        self.send(move |mount| mount.fixed_slew_ra(speed))
    }
    pub fn fixed_slew_dec(&self, speed: i32) -> Result<()> {
        println!("fixed_slew_dec {}", speed);
        self.send(move |mount| mount.fixed_slew_dec(speed))
    }
}

fn run(
    recv: mpsc::Receiver<MountCommand>,
    send: mpsc::Sender<MountData>,
    ui_thread: UiThread,
) -> Result<()> {
    let mut mount = match autoconnect() {
        Ok(ok) => ok,
        Err(err) => {
            println!("Error connecting to mount: {}", err);
            return Ok(());
        }
    };
    let update_rate = Duration::from_secs(1);
    let mut next_update = Instant::now() + update_rate;
    loop {
        let now = Instant::now();
        if now > next_update {
            next_update += update_rate;
            if now > next_update {
                // dropped frames
                next_update = now + update_rate;
            }
            if !run_update(&mut mount, &send, &ui_thread)? {
                break Ok(());
            }
        }
        let duration = next_update - now;
        match recv.recv_timeout(duration) {
            Ok(cmd) => cmd(&mut mount)?,
            Err(mpsc::RecvTimeoutError::Timeout) => (),
            Err(mpsc::RecvTimeoutError::Disconnected) => break Ok(()),
        }
    }
}

fn run_update(
    mount: &mut Mount,
    send: &mpsc::Sender<MountData>,
    ui_thread: &UiThread,
) -> Result<bool> {
    let ra_dec = mount.get_ra_dec()?;
    let az_alt = mount.get_az_alt()?;
    let aligned = mount.aligned()?;
    let tracking_mode = mount.tracking_mode()?;
    let location = mount.location()?;
    let time = mount.time()?;
    let send_result = send.send(MountData {
        ra_dec,
        az_alt,
        aligned,
        tracking_mode,
        location,
        time,
    });
    match send_result {
        Ok(()) => {
            ui_thread.trigger();
            Ok(true)
        }
        Err(mpsc::SendError(_)) => Ok(false),
    }
}
