use crate::{dms::Angle, mount::*, Result};
use std::{
    sync::mpsc,
    thread::{spawn, JoinHandle},
};

enum MountCommand {
    RequestUpdate,
    Slew(Angle, Angle),
    SlowSlew(Angle, Angle),
    Sync(Angle, Angle),
    Reset(Angle, Angle),
    SlewAzAlt(Angle, Angle),
    Cancel,
    TrackingMode(TrackingMode),
    SetLocation(Angle, Angle),
    SetTimeNow,
    FixedSlewRA(i32),
    FixedSlewDec(i32),
}

#[derive(Default)]
pub struct MountData {
    pub ra_dec: (Angle, Angle),
    pub az_alt: (Angle, Angle),
    pub aligned: bool,
    pub tracking_mode: TrackingMode,
    pub location: (Angle, Angle),
    pub time: MountTime,
}

pub struct MountAsync {
    thread: Option<JoinHandle<Result<()>>>,
    send: mpsc::Sender<MountCommand>,
    recv: mpsc::Receiver<MountData>,
    data: MountData,
}

impl MountAsync {
    pub fn new(mount: Mount) -> Self {
        let (send_cmd, recv_cmd) = mpsc::channel();
        let (send_data, recv_data) = mpsc::channel();
        let thread = spawn(move || run(mount, recv_cmd, send_data));
        Self {
            thread: Some(thread),
            send: send_cmd,
            recv: recv_data,
            data: MountData::default(),
        }
    }

    pub fn data(&mut self) -> Result<&MountData> {
        loop {
            match self.recv.try_recv() {
                Ok(data) => self.data = data,
                Err(mpsc::TryRecvError::Empty) => break,
                // TODO
                Err(mpsc::TryRecvError::Disconnected) => self
                    .thread
                    .take()
                    .expect("data() already failed, do not call again")
                    .join()
                    .expect("Unable to join mount thread")?,
            }
        }
        Ok(&self.data)
    }

    fn send(&self, cmd: MountCommand) -> Result<()> {
        Ok(self.send.send(cmd)?)
    }

    pub fn request_update(&self) -> Result<()> {
        self.send(MountCommand::RequestUpdate)
    }
    pub fn slew(&self, ra: Angle, dec: Angle) -> Result<()> {
        self.send(MountCommand::Slew(ra, dec))
    }
    pub fn slow_slew(&self, ra: Angle, dec: Angle) -> Result<()> {
        self.send(MountCommand::SlowSlew(ra, dec))
    }
    pub fn sync(&self, ra: Angle, dec: Angle) -> Result<()> {
        self.send(MountCommand::Sync(ra, dec))
    }
    pub fn reset(&self, ra: Angle, dec: Angle) -> Result<()> {
        self.send(MountCommand::Reset(ra, dec))
    }
    pub fn slew_azalt(&self, az: Angle, alt: Angle) -> Result<()> {
        self.send(MountCommand::SlewAzAlt(az, alt))
    }
    pub fn cancel(&self) -> Result<()> {
        self.send(MountCommand::Cancel)
    }
    pub fn set_tracking_mode(&self, mode: TrackingMode) -> Result<()> {
        self.send(MountCommand::TrackingMode(mode))
    }
    pub fn set_location(&self, lat: Angle, lon: Angle) -> Result<()> {
        self.send(MountCommand::SetLocation(lat, lon))
    }
    pub fn set_time_now(&self) -> Result<()> {
        self.send(MountCommand::SetTimeNow)
    }
    pub fn fixed_slew_ra(&self, speed: i32) -> Result<()> {
        self.send(MountCommand::FixedSlewRA(speed))
    }
    pub fn fixed_slew_dec(&self, speed: i32) -> Result<()> {
        self.send(MountCommand::FixedSlewDec(speed))
    }
}

fn run(
    mut mount: Mount,
    recv: mpsc::Receiver<MountCommand>,
    send: mpsc::Sender<MountData>,
) -> Result<()> {
    loop {
        match recv.recv() {
            Ok(cmd) => {
                if !run_one(&mut mount, cmd, &send)? {
                    break Ok(());
                }
            }
            Err(mpsc::RecvError) => break Ok(()),
        }
    }
}

fn run_one(mount: &mut Mount, cmd: MountCommand, send: &mpsc::Sender<MountData>) -> Result<bool> {
    match cmd {
        MountCommand::RequestUpdate => {
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
                Ok(()) => (),
                Err(mpsc::SendError(_)) => return Ok(false),
            }
        }
        MountCommand::Slew(ra, dec) => mount.slew_ra_dec(ra, dec)?,
        MountCommand::SlowSlew(ra, dec) => {
            mount.slow_goto_ra(ra)?;
            mount.slow_goto_dec(dec)?
        }
        MountCommand::Sync(ra, dec) => mount.sync_ra_dec(ra, dec)?,
        MountCommand::Reset(ra, dec) => {
            mount.reset_ra(ra)?;
            mount.reset_dec(dec)?
        }
        MountCommand::SlewAzAlt(az, alt) => mount.slew_az_alt(az, alt)?,
        MountCommand::Cancel => mount.cancel_slew()?,
        MountCommand::TrackingMode(mode) => mount.set_tracking_mode(mode)?,
        MountCommand::SetLocation(lat, lon) => mount.set_location(lat, lon)?,
        MountCommand::SetTimeNow => mount.set_time(MountTime::now())?,
        MountCommand::FixedSlewRA(speed) => mount.fixed_slew_ra(speed)?,
        MountCommand::FixedSlewDec(speed) => mount.fixed_slew_dec(speed)?,
    }
    Ok(true)
}
