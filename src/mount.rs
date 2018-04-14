use Result;
use dms;
use serialport;
use std::ffi::OsStr;
use std::fmt::Display;
use std::fmt;
use std::str::FromStr;
use std::time::Duration;
use time;

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
    pub fn now() -> MountTime {
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
        write!(f, "{}-{}-{} {}:{}:{} tz={} dst={}", self.year as u32 + 2000, self.month, self.day, self.hour, self.minute, self.second, self.time_zone_offset, self.dst)
    }
}

fn mod_positive(value: f64) -> f64 {
    let fract = value.fract();
    if fract > 0.0 {
        fract
    } else {
        fract + 1.0
    }
}

pub struct Mount {
    port: Box<serialport::SerialPort>,
}

impl Mount {
    pub fn list() -> Vec<String> {
        serialport::available_ports()
            .unwrap_or(Vec::new())
            .into_iter()
            .map(|x| x.port_name)
            .collect()
    }

    pub fn new<T: AsRef<OsStr> + ?Sized>(path: &T) -> Result<Mount> {
        let mut port = serialport::open(path)?;
        port.set_timeout(Duration::from_secs(5))?;
        Ok(Mount { port })
    }

    fn read_bytes(&mut self) -> Result<Vec<u8>> {
        let mut buf = [0];
        let mut res = Vec::new();
        loop {
            self.port.read_exact(&mut buf)?;
            if buf[0] == ('#' as u8) {
                break;
            }
            res.push(buf[0])
        }
        Ok(res)
    }

    fn read(&mut self) -> Result<String> {
        Ok(String::from_utf8(self.read_bytes()?)?)
    }

    pub fn get_ra_dec(&mut self) -> Result<(f64, f64)> {
        write!(self.port, "e")?;
        self.port.flush()?;
        let response = self.read()?;
        let response = response
            .split(',')
            .map(|x| u32::from_str_radix(x, 16))
            .collect::<::std::result::Result<Vec<_>, _>>()?;
        if response.len() != 2 {
            return Err("Invalid response".to_owned())?;
        }
        Ok((
            response[0] as f64 / (u32::max_value() as f64 + 1.0) * 24.0,
            response[1] as f64 / (u32::max_value() as f64 + 1.0) * 360.0,
        ))
    }

    pub fn overwrite_ra_dec(&mut self, ra: f64, dec: f64) -> Result<()> {
        let ra = (mod_positive(ra / 24.0) * (u32::max_value() as f64 + 1.0)) as u32;
        let dec = (mod_positive(dec / 360.0) * (u32::max_value() as f64 + 1.0)) as u32;
        write!(self.port, "s{:08X},{:08X}", ra, dec)?;
        self.port.flush()?;
        if self.read()? != "" {
            return Err("Invalid response".to_owned())?;
        }
        Ok(())
    }

    pub fn slew_ra_dec(&mut self, ra: f64, dec: f64) -> Result<()> {
        let ra = (mod_positive(ra / 24.0) * (u32::max_value() as f64 + 1.0)) as u32;
        let dec = (mod_positive(dec / 360.0) * (u32::max_value() as f64 + 1.0)) as u32;
        write!(self.port, "r{:08X},{:08X}", ra, dec)?;
        self.port.flush()?;
        if self.read()? != "" {
            return Err("Invalid response".to_owned())?;
        }
        Ok(())
    }

    pub fn cancel_slew(&mut self) -> Result<()> {
        write!(self.port, "M")?;
        self.port.flush()?;
        if self.read()? != "" {
            return Err("Invalid response".to_owned())?;
        }
        Ok(())
    }

    pub fn tracking_mode(&mut self) -> Result<TrackingMode> {
        write!(self.port, "t")?;
        self.port.flush()?;
        // HAHA WHAT, it's literal char value, not ascii integer
        Ok(self.read_bytes()?[0].into())
    }

    pub fn set_tracking_mode(&mut self, mode: TrackingMode) -> Result<()> {
        self.port.write(&['T' as u8, u8::from(mode)])?;
        self.port.flush()?;
        if self.read()? != "" {
            return Err("Invalid response".to_owned())?;
        }
        Ok(())
    }

    fn format_lat_lon(lat: f64, lon: f64) -> Vec<u8> {
        let (lat_sign, lat_deg, lat_min, lat_sec, _) = dms::value_to_dms(lat);
        let (lon_sign, lon_deg, lon_min, lon_sec, _) = dms::value_to_dms(lon);
        // The format of the location commands is: ABCDEFGH, where:
        // A is the number of degrees of latitude.
        // B is the number of minutes of latitude.
        // C is the number of seconds of latitude.
        // D is 0 for north and 1 for south.
        // E is the number of degrees of longitude.
        // F is the number of minutes of longitude.
        // G is the number of seconds of longitude.
        // H is 0 for east and 1 for west.
        let mut builder = Vec::new();
        builder.push(lat_deg as u8);
        builder.push(lat_min as u8);
        builder.push(lat_sec as u8);
        builder.push(if lat_sign { 1 } else { 0 });
        builder.push(lon_deg as u8);
        builder.push(lon_min as u8);
        builder.push(lon_sec as u8);
        builder.push(if lon_sign { 1 } else { 0 });
        builder
    }

    fn parse_lat_lon(value: &[u8]) -> (f64, f64) {
        let lat_deg = value[0] as _;
        let lat_min = value[1] as _;
        let lat_sec = value[2] as _;
        let lat_sign = value[3] == 1;
        let lon_deg = value[4] as _;
        let lon_min = value[5] as _;
        let lon_sec = value[6] as _;
        let lon_sign = value[7] == 1;
        let lat = dms::dms_to_value(lat_sign, lat_deg, lat_min, lat_sec, 0.0);
        let lon = dms::dms_to_value(lon_sign, lon_deg, lon_min, lon_sec, 0.0);
        (lat, lon)
    }

    pub fn location(&mut self) -> Result<(f64, f64)> {
        write!(self.port, "w")?;
        self.port.flush()?;
        Ok(Self::parse_lat_lon(&self.read_bytes()?))
    }

    pub fn set_location(&mut self, lat: f64, lon: f64) -> Result<()> {
        let mut to_write = Self::format_lat_lon(lat, lon);
        to_write.insert(0, 'W' as u8);
        self.port.write(&to_write)?;
        self.port.flush()?;
        if self.read()? != "" {
            return Err("Invalid response".to_owned())?;
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

    fn format_time(time: MountTime) -> Vec<u8> {
        vec![
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
        write!(self.port, "h")?;
        self.port.flush()?;
        Ok(Self::parse_time(&self.read_bytes()?))
    }

    pub fn set_time(&mut self, time: MountTime) -> Result<()> {
        let mut to_write = Self::format_time(time);
        to_write.insert(0, 'H' as u8);
        self.port.write(&to_write)?;
        self.port.flush()?;
        if self.read()? != "" {
            return Err("Invalid response".to_owned())?;
        }
        Ok(())
    }

    pub fn aligned(&mut self) -> Result<bool> {
        write!(self.port, "J")?;
        self.port.flush()?;
        Ok(self.read_bytes()?[0] != 0)
    }

    pub fn echo(&mut self, byte: u8) -> Result<u8> {
        self.port.write(&['K' as u8, byte])?;
        self.port.flush()?;
        Ok(self.read_bytes()?[0])
    }
}
