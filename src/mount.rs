use crate::{dms::Angle, Result};
use std::{ffi::OsStr, fmt, fmt::Display, str, str::FromStr, time::Duration};

#[derive(Clone, Debug)]
pub enum TrackingMode {
    Off,
    AltAz,
    Equatorial,
    SiderealPec,
}

impl Default for TrackingMode {
    fn default() -> Self {
        TrackingMode::Off
    }
}

impl Display for TrackingMode {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        match *self {
            TrackingMode::Off => write!(f, "Off"),
            TrackingMode::AltAz => write!(f, "AltAz"),
            TrackingMode::Equatorial => write!(f, "Equatorial"),
            TrackingMode::SiderealPec => write!(f, "SiderealPec"),
        }
    }
}

impl FromStr for TrackingMode {
    type Err = &'static str;
    fn from_str(s: &str) -> ::std::result::Result<Self, Self::Err> {
        Ok(match s {
            "Off" => TrackingMode::Off,
            "AltAz" => TrackingMode::AltAz,
            "Equatorial" => TrackingMode::Equatorial,
            "SiderealPec" => TrackingMode::SiderealPec,
            _ => return Err("Invalid TrackingMode"),
        })
    }
}

impl From<u8> for TrackingMode {
    fn from(val: u8) -> TrackingMode {
        match val {
            0 => TrackingMode::Off,
            1 => TrackingMode::AltAz,
            2 => TrackingMode::Equatorial,
            3 => TrackingMode::SiderealPec,
            _ => TrackingMode::Off,
        }
    }
}

impl From<TrackingMode> for u8 {
    fn from(val: TrackingMode) -> u8 {
        match val {
            TrackingMode::Off => 0,
            TrackingMode::AltAz => 1,
            TrackingMode::Equatorial => 2,
            TrackingMode::SiderealPec => 3,
        }
    }
}

#[derive(Default, Clone, Debug)]
pub struct MountTime {
    hour: u8,
    minute: u8,
    second: u8,
    month: u8,
    day: u8,
    year: u8,             // current year - 2000
    time_zone_offset: i8, // hours
    dst: bool,
}

impl MountTime {
    pub fn now() -> Self {
        //let tm = time::now();
        let tm = time::OffsetDateTime::now_local();
        MountTime {
            hour: tm.hour() as u8,
            minute: tm.minute() as u8,
            second: tm.second() as u8,
            month: tm.month() as u8,
            day: tm.day() as u8,
            year: (tm.year() - 1900) as u8, // tm_year is years since 1900
            time_zone_offset: tm.offset().as_hours(),
            dst: false,
        }
    }
}

impl Display for MountTime {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(
            f,
            "{}-{}-{} {}:{}:{} tz={} dst={}",
            u32::from(self.year) + 2000,
            self.month,
            self.day,
            self.hour,
            self.minute,
            self.second,
            self.time_zone_offset,
            self.dst
        )
    }
}

pub fn autoconnect() -> Result<Mount> {
    for port in Mount::list() {
        let mut m = match Mount::new(&port) {
            Ok(ok) => ok,
            Err(_) => continue,
        };
        match m.get_ra_dec() {
            Ok(_) => (),
            Err(_) => continue,
        };
        m.set_time(MountTime::now())?;
        return Ok(m);
    }
    Err(failure::err_msg("No mount found"))
}

pub struct Mount {
    port: Box<dyn serialport::SerialPort>,
    radec_offset: (Angle, Angle),
}

impl Mount {
    pub fn list() -> Vec<String> {
        serialport::available_ports()
            .unwrap_or_else(|_| Vec::new())
            .into_iter()
            .map(|x| x.port_name)
            .collect()
    }

    pub fn new<T: AsRef<OsStr> + ?Sized>(path: &T) -> Result<Mount> {
        let mut port = serialport::open(path)?;
        port.set_timeout(Duration::from_secs(3))?;
        Ok(Mount {
            port,
            radec_offset: (Angle::from_0to1(0.0), Angle::from_0to1(0.0)),
        })
    }

    fn read(&mut self, mut data: impl AsMut<[u8]>) -> Result<()> {
        let data = data.as_mut();
        if !data.is_empty() {
            self.port.read_exact(data)?;
        }
        let mut hash = [0];
        self.port.read_exact(&mut hash)?;
        if hash == [b'#'] {
            Ok(())
        } else {
            Err(failure::err_msg("Mount reply didn't end with '#'"))
        }
    }

    fn write(&mut self, data: impl AsRef<[u8]>) -> Result<()> {
        self.port.write_all(data.as_ref())?;
        self.port.flush()?;
        Ok(())
    }

    fn interact0(&mut self, data: impl AsRef<[u8]>) -> Result<()> {
        self.write(data)?;
        self.read(&mut [])?;
        Ok(())
    }

    fn real_to_mount(&self, ra_dec: (Angle, Angle)) -> (Angle, Angle) {
        (
            ra_dec.0 + self.radec_offset.0,
            ra_dec.1 + self.radec_offset.1,
        )
    }

    fn mount_to_real(&self, ra_dec: (Angle, Angle)) -> (Angle, Angle) {
        (
            ra_dec.0 - self.radec_offset.0,
            ra_dec.1 - self.radec_offset.1,
        )
    }

    pub fn add_real_to_mount_delta(&mut self, ra: Angle, dec: Angle) {
        self.radec_offset.0 += ra;
        self.radec_offset.1 += dec;
    }

    pub fn get_ra_dec(&mut self) -> Result<(Angle, Angle)> {
        self.write([b'e'])?;
        let mut response = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        self.read(&mut response)?;
        let response = str::from_utf8(&response)?
            .split(',')
            .map(|x| u32::from_str_radix(x, 16))
            .collect::<::std::result::Result<Vec<_>, _>>()?;
        if response.len() != 2 {
            return Err(failure::err_msg("Invalid response"));
        }
        let result = (Angle::from_u32(response[0]), Angle::from_u32(response[1]));
        Ok(self.mount_to_real(result))
    }

    pub fn sync_ra_dec(&mut self, ra: Angle, dec: Angle) -> Result<()> {
        let (ra, dec) = self.real_to_mount((ra, dec));
        let ra = ra.u32();
        let dec = dec.u32();
        let msg = format!("s{:08X},{:08X}", ra, dec);
        self.interact0(msg)?;
        Ok(())
    }

    pub fn slew_ra_dec(&mut self, ra: Angle, dec: Angle) -> Result<()> {
        let (ra, dec) = self.real_to_mount((ra, dec));
        let ra = ra.u32();
        let dec = dec.u32();
        let msg = format!("r{:08X},{:08X}", ra, dec);
        self.interact0(msg)?;
        Ok(())
    }

    pub fn get_az_alt(&mut self) -> Result<(Angle, Angle)> {
        self.write([b'z'])?;
        let mut response = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        self.read(&mut response)?;
        let response = str::from_utf8(&response)?
            .split(',')
            .map(|x| u32::from_str_radix(x, 16))
            .collect::<::std::result::Result<Vec<_>, _>>()?;
        if response.len() != 2 {
            return Err(failure::err_msg("Invalid response"));
        }
        Ok((Angle::from_u32(response[0]), Angle::from_u32(response[1])))
    }

    // note: This assumes the telescope's az axis is straight up.
    // This is NOT what is reported in get_az_alt
    pub fn slew_az_alt(&mut self, az: Angle, alt: Angle) -> Result<()> {
        let az = az.u32();
        let alt = alt.u32();
        let msg = format!("b{:08X},{:08X}", az, alt);
        self.interact0(msg)?;
        Ok(())
    }

    pub fn cancel_slew(&mut self) -> Result<()> {
        self.interact0([b'M'])
    }

    pub fn tracking_mode(&mut self) -> Result<TrackingMode> {
        self.write([b't'])?;
        let mut response = [0];
        self.read(&mut response)?;
        // HAHA WHAT, it's literal char value, not ascii integer
        Ok(response[0].into())
    }

    pub fn set_tracking_mode(&mut self, mode: TrackingMode) -> Result<()> {
        self.interact0([b'T', u8::from(mode)])
    }

    fn format_lat_lon(cmd: u8, lat: Angle, lon: Angle) -> [u8; 9] {
        let (lat_sign, lat_deg, lat_min, lat_sec, _) = lat.to_dms();
        let (lon_sign, lon_deg, lon_min, lon_sec, _) = lon.to_dms();
        // The format of the location commands is: ABCDEFGH, where:
        // A is the number of degrees of latitude.
        // B is the number of minutes of latitude.
        // C is the number of seconds of latitude.
        // D is 0 for north and 1 for south.
        // E is the number of degrees of longitude.
        // F is the number of minutes of longitude.
        // G is the number of seconds of longitude.
        // H is 0 for east and 1 for west.
        [
            cmd,
            lat_deg as u8,
            lat_min as u8,
            lat_sec as u8,
            if lat_sign { 1 } else { 0 },
            lon_deg as u8,
            lon_min as u8,
            lon_sec as u8,
            if lon_sign { 1 } else { 0 },
        ]
    }

    fn parse_lat_lon(value: [u8; 8]) -> (Angle, Angle) {
        let lat_deg = f64::from(value[0]);
        let lat_min = f64::from(value[1]);
        let lat_sec = f64::from(value[2]);
        let lat_sign = value[3] == 1;
        let lon_deg = f64::from(value[4]);
        let lon_min = f64::from(value[5]);
        let lon_sec = f64::from(value[6]);
        let lon_sign = value[7] == 1;
        let lat = Angle::from_dms(lat_sign, lat_deg, lat_min, lat_sec, 0.0);
        let lon = Angle::from_dms(lon_sign, lon_deg, lon_min, lon_sec, 0.0);
        (lat, lon)
    }

    pub fn location(&mut self) -> Result<(Angle, Angle)> {
        self.write([b'w'])?;
        let mut response = [0, 0, 0, 0, 0, 0, 0, 0];
        self.read(&mut response)?;
        Ok(Self::parse_lat_lon(response))
    }

    pub fn set_location(&mut self, lat: Angle, lon: Angle) -> Result<()> {
        let to_write = Self::format_lat_lon(b'W', lat, lon);
        self.interact0(to_write)
    }

    fn parse_time(time: [u8; 8]) -> MountTime {
        let [hour, minute, second, month, day, year, time_zone_offset, dst] = time;
        MountTime {
            hour,
            minute,
            second,
            month,
            day,
            year,
            time_zone_offset: time_zone_offset as i8,
            dst: dst == 1,
        }
    }

    fn format_time(cmd: u8, time: MountTime) -> [u8; 9] {
        [
            cmd,
            time.hour,
            time.minute,
            time.second,
            time.month,
            time.day,
            time.year,
            time.time_zone_offset as u8,
            if time.dst { 1 } else { 0 },
        ]
    }

    pub fn time(&mut self) -> Result<MountTime> {
        self.write([b'h'])?;
        let mut response = [0, 0, 0, 0, 0, 0, 0, 0];
        self.read(&mut response)?;
        Ok(Self::parse_time(response))
    }

    pub fn set_time(&mut self, time: MountTime) -> Result<()> {
        let to_write = Self::format_time(b'H', time);
        self.interact0(to_write)?;
        Ok(())
    }

    pub fn aligned(&mut self) -> Result<bool> {
        self.write([b'J'])?;
        let mut response = [0];
        self.read(&mut response)?;
        Ok(response[0] != 0)
    }

    //fn split_three_bytes(value_angle: Angle) -> (u8, u8, u8) {
    //    let mut value = value_angle.value_0to1();
    //    value *= 256.0;
    //    let high = value as u8;
    //    value = value.fract();
    //    value *= 256.0;
    //    let med = value as u8;
    //    value = value.fract();
    //    value *= 256.0;
    //    let low = value as u8;
    //    (high, med, low)
    //}

    //fn p_command_three(&mut self, one: u8, two: u8, three: u8, data: Angle) -> Result<()> {
    //    let (high, med, low) = Self::split_three_bytes(data);
    //    let cmd = [b'P', one, two, three, high, med, low, 0];
    //    self.interact0(cmd)?;
    //    Ok(())
    //}

    // I think these reset like az/alt if the az axis was straight up, which like, wat
    //pub fn reset_ra(&mut self, data: Angle) -> Result<()> {
    //    self.p_command_three(4, 16, 4, data)
    //}
    //pub fn reset_dec(&mut self, data: Angle) -> Result<()> {
    //    self.p_command_three(4, 17, 4, data)
    //}
    // note: this is similar to slew_az_alt in that the axis is weird
    //pub fn slow_goto_az(&mut self, data: Angle) -> Result<()> {
    //    self.p_command_three(4, 16, 23, data)
    //}
    //pub fn slow_goto_alt(&mut self, data: Angle) -> Result<()> {
    //    self.p_command_three(4, 17, 23, data)
    //}

    fn fixed_slew_command(&mut self, one: u8, two: u8, three: u8, rate: u8) -> Result<()> {
        let cmd = [b'P', one, two, three, rate, 0, 0, 0];
        self.interact0(cmd)?;
        Ok(())
    }

    pub fn fixed_slew_ra(&mut self, speed: i32) -> Result<()> {
        if speed > 0 {
            self.fixed_slew_command(2, 16, 36, speed as u8)
        } else {
            self.fixed_slew_command(2, 16, 37, (-speed) as u8)
        }
    }

    pub fn fixed_slew_dec(&mut self, speed: i32) -> Result<()> {
        if speed > 0 {
            self.fixed_slew_command(2, 17, 36, speed as u8)
        } else {
            self.fixed_slew_command(2, 17, 37, (-speed) as u8)
        }
    }
}
