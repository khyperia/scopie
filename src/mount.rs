use crate::{dms::Angle, Result};
use serialport;
use std::{ffi::OsStr, fmt, fmt::Display, str::FromStr, time::Duration};

pub enum TrackingMode {
    Off,
    AltAz,
    Equatorial,
    SiderealPec,
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
        let tm = time::now();
        MountTime {
            hour: tm.tm_hour as u8,
            minute: tm.tm_min as u8,
            second: tm.tm_sec as u8,
            month: (tm.tm_mon + 1) as u8,
            day: tm.tm_mday as u8,
            year: (tm.tm_year - 100) as u8, // tm_year is years since 1900
            time_zone_offset: (tm.tm_utcoff / (60 * 60)) as i8, // tm_utcoff is seconds
            dst: tm.tm_isdst != 0,
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
            Ok(_) => return Ok(m),
            Err(_) => continue,
        };
    }
    Err(failure::err_msg("No mount found"))
}

pub struct Mount {
    port: Box<dyn serialport::SerialPort>,
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
        port.set_timeout(Duration::from_secs(1))?;
        Ok(Mount { port })
    }

    fn read_bytes(&mut self) -> Result<Vec<u8>> {
        let mut buf = [0];
        let mut res = Vec::new();
        loop {
            self.port.read_exact(&mut buf)?;
            if buf[0] == b'#' {
                break;
            }
            res.push(buf[0])
        }
        Ok(res)
    }

    fn read(&mut self) -> Result<String> {
        Ok(String::from_utf8(self.read_bytes()?)?)
    }

    fn write(&mut self, data: impl AsRef<[u8]>) -> Result<()> {
        self.port.write_all(data.as_ref())?;
        self.port.flush()?;
        Ok(())
    }

    pub fn get_ra_dec(&mut self) -> Result<(Angle, Angle)> {
        self.write([b'e'])?;
        let response = self.read()?;
        let response = response
            .split(',')
            .map(|x| u32::from_str_radix(x, 16))
            .collect::<::std::result::Result<Vec<_>, _>>()?;
        if response.len() != 2 {
            return Err(failure::err_msg("Invalid response"));
        }
        Ok((
            Angle::from_0to1(f64::from(response[0]) / (f64::from(u32::max_value()) + 1.0)),
            Angle::from_0to1(f64::from(response[1]) / (f64::from(u32::max_value()) + 1.0)),
        ))
    }

    // TODO: rename to sync
    pub fn overwrite_ra_dec(&mut self, ra: Angle, dec: Angle) -> Result<()> {
        let ra = (ra.value_0to1() * (f64::from(u32::max_value()) + 1.0)) as u32;
        let dec = (dec.value_0to1() * (f64::from(u32::max_value()) + 1.0)) as u32;
        let msg = format!("s{:08X},{:08X}", ra, dec);
        self.write(msg)?;
        if self.read()? != "" {
            return Err(failure::err_msg("Invalid response"));
        }
        Ok(())
    }

    pub fn slew_ra_dec(&mut self, ra: Angle, dec: Angle) -> Result<()> {
        let ra = (ra.value_0to1() * (f64::from(u32::max_value()) + 1.0)) as u32;
        let dec = (dec.value_0to1() * (f64::from(u32::max_value()) + 1.0)) as u32;
        let msg = format!("r{:08X},{:08X}", ra, dec);
        self.write(msg)?;
        if self.read()? != "" {
            return Err(failure::err_msg("Invalid response"));
        }
        Ok(())
    }

    pub fn cancel_slew(&mut self) -> Result<()> {
        self.write([b'M'])?;
        if self.read()? != "" {
            return Err(failure::err_msg("Invalid response"));
        }
        Ok(())
    }

    pub fn tracking_mode(&mut self) -> Result<TrackingMode> {
        self.write([b't'])?;
        if let [result] = self.read_bytes()?[..] {
            // HAHA WHAT, it's literal char value, not ascii integer
            Ok(result.into())
        } else {
            Err(failure::err_msg("Invalid response"))
        }
    }

    pub fn set_tracking_mode(&mut self, mode: TrackingMode) -> Result<()> {
        self.write([b'T', u8::from(mode)])?;
        if self.read()? != "" {
            return Err(failure::err_msg("Invalid response"));
        }
        Ok(())
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

    fn parse_lat_lon(value: &[u8]) -> (Angle, Angle) {
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
        Ok(Self::parse_lat_lon(&self.read_bytes()?))
    }

    pub fn set_location(&mut self, lat: Angle, lon: Angle) -> Result<()> {
        let to_write = Self::format_lat_lon(b'W', lat, lon);
        self.write(to_write)?;
        if self.read()? != "" {
            return Err(failure::err_msg("Invalid response"));
        }
        Ok(())
    }

    fn parse_time(time: &[u8]) -> MountTime {
        let hour = time[0];
        let minute = time[1];
        let second = time[2];
        let month = time[3];
        let day = time[4];
        let year = time[5];
        let time_zone_offset = time[6] as i8;
        let dst = time[7] == 1;
        MountTime {
            hour,
            minute,
            second,
            month,
            day,
            year,
            time_zone_offset,
            dst,
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
        Ok(Self::parse_time(&self.read_bytes()?))
    }

    pub fn set_time(&mut self, time: MountTime) -> Result<()> {
        let to_write = Self::format_time(b'H', time);
        self.write(to_write)?;
        if self.read()? != "" {
            return Err(failure::err_msg("Invalid response"));
        }
        Ok(())
    }

    pub fn aligned(&mut self) -> Result<bool> {
        self.write([b'J'])?;
        if let [result] = self.read_bytes()?[..] {
            Ok(result != 0)
        } else {
            Err(failure::err_msg("Invalid response"))
        }
    }

    pub fn echo(&mut self, byte: u8) -> Result<u8> {
        self.write([b'K', byte])?;
        if let [result] = self.read_bytes()?[..] {
            Ok(result)
        } else {
            Err(failure::err_msg("Invalid response"))
        }
    }
}
