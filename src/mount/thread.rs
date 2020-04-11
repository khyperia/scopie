use crate::{dms::Angle, mount::interface::*, Result, SendUserUpdate, UserUpdate};
use std::{
    sync::mpsc,
    thread::spawn,
    time::{Duration, Instant},
};

enum MountCommand {
    Slew(Angle, Angle),
    Sync(Angle, Angle),
    SetRealToMount(Angle, Angle),
    SlewAzAlt(Angle, Angle),
    Cancel,
    TrackingMode(TrackingMode),
    SetLocation(Angle, Angle),
    SetTimeNow,
    FixedSlewRA(i32),
    FixedSlewDec(i32),
}

#[derive(Default, Clone, Debug)]
pub struct MountData {
    pub ra_dec_real: (Angle, Angle),
    pub ra_dec_mount: (Angle, Angle),
    pub az_alt: (Angle, Angle),
    pub aligned: bool,
    pub tracking_mode: TrackingMode,
    pub location: (Angle, Angle),
    pub time: MountTime,
}

pub struct MountSendError {}

pub struct MountAsync {
    send: mpsc::Sender<MountCommand>,
    pub data: MountData,
}

impl MountAsync {
    pub fn new(send_user_update: SendUserUpdate) -> Self {
        let (send_cmd, recv_cmd) = mpsc::channel();
        spawn(move || match run(recv_cmd, send_user_update) {
            Ok(()) => (),
            Err(err) => panic!("Mount thread error: {}", err),
        });
        Self {
            send: send_cmd,
            data: MountData::default(),
        }
    }

    pub fn user_update(&mut self, user_update: UserUpdate) {
        // if let UserUpdate::MountUpdate(mount_update) = user_update {
        //     self.data = mount_update.clone();
        // }
        if let UserUpdate::MountUpdate(mount_update) = user_update {
            self.data = mount_update;
        }
    }

    fn send(&self, cmd: MountCommand) -> std::result::Result<(), MountSendError> {
        match self.send.send(cmd) {
            Ok(()) => Ok(()),
            Err(mpsc::SendError(_)) => Err(MountSendError {}),
        }
    }

    pub fn slew(&self, ra: Angle, dec: Angle) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::Slew(ra, dec))
    }
    pub fn sync(&self, ra: Angle, dec: Angle) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::Sync(ra, dec))
    }
    pub fn set_real_to_mount(
        &self,
        ra: Angle,
        dec: Angle,
    ) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::SetRealToMount(ra, dec))
    }
    pub fn slew_azalt(&self, az: Angle, alt: Angle) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::SlewAzAlt(az, alt))
    }
    pub fn cancel(&self) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::Cancel)
    }
    pub fn set_tracking_mode(&self, mode: TrackingMode) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::TrackingMode(mode))
    }
    pub fn set_location(&self, lat: Angle, lon: Angle) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::SetLocation(lat, lon))
    }
    pub fn set_time_now(&self) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::SetTimeNow)
    }
    pub fn fixed_slew_ra(&self, speed: i32) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::FixedSlewRA(speed))
    }
    pub fn fixed_slew_dec(&self, speed: i32) -> std::result::Result<(), MountSendError> {
        self.send(MountCommand::FixedSlewDec(speed))
    }
}

fn run(recv: mpsc::Receiver<MountCommand>, send: SendUserUpdate) -> Result<()> {
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
            if !run_update(&mut mount, &send)? {
                break Ok(());
            }
        }
        let duration = next_update - now;
        match recv.recv_timeout(duration) {
            Ok(cmd) => run_one(&mut mount, cmd)?,
            Err(mpsc::RecvTimeoutError::Timeout) => (),
            Err(mpsc::RecvTimeoutError::Disconnected) => break Ok(()),
        }
    }
}

fn run_update(mount: &mut Mount, send: &SendUserUpdate) -> Result<bool> {
    let ra_dec_mount = mount.get_ra_dec_mount()?;
    let ra_dec_real = mount.mount_to_real(ra_dec_mount);
    let az_alt = mount.get_az_alt()?;
    let aligned = mount.aligned()?;
    let tracking_mode = mount.tracking_mode()?;
    let location = mount.location()?;
    let time = mount.time()?;
    let send_result = send.send_event(UserUpdate::MountUpdate(MountData {
        ra_dec_mount,
        ra_dec_real,
        az_alt,
        aligned,
        tracking_mode,
        location,
        time,
    }));
    match send_result {
        Ok(()) => Ok(true),
        Err(glutin::event_loop::EventLoopClosed(_)) => Ok(false),
    }
}

fn run_one(mount: &mut Mount, cmd: MountCommand) -> Result<()> {
    match cmd {
        // TODO: naming (real)
        MountCommand::Slew(ra, dec) => mount.slew_ra_dec_real(ra, dec)?,
        MountCommand::Sync(ra, dec) => mount.sync_ra_dec_real(ra, dec)?,
        MountCommand::SetRealToMount(ra, dec) => mount.set_real_to_mount(ra, dec),
        MountCommand::SlewAzAlt(az, alt) => mount.slew_az_alt(az, alt)?,
        MountCommand::Cancel => mount.cancel_slew()?,
        MountCommand::TrackingMode(mode) => mount.set_tracking_mode(mode)?,
        MountCommand::SetLocation(lat, lon) => mount.set_location(lat, lon)?,
        MountCommand::SetTimeNow => mount.set_time(MountTime::now())?,
        MountCommand::FixedSlewRA(speed) => mount.fixed_slew_ra(speed)?,
        MountCommand::FixedSlewDec(speed) => mount.fixed_slew_dec(speed)?,
    }
    Ok(())
}
