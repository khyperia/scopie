use regex::Regex;
use std::sync::LazyLock;

#[derive(Default, Clone, Copy, Debug)]
pub struct Angle {
    value: f64,
}

impl Angle {
    pub fn from_0to1(value: f64) -> Self {
        Self {
            value: value.rem_euclid(1.0),
        }
    }

    pub fn value_0to1(self) -> f64 {
        self.value
    }

    pub fn from_u32(value: u32) -> Self {
        Self::from_0to1(f64::from(value) / (f64::from(u32::max_value()) + 1.0))
    }

    pub fn u32(self) -> u32 {
        (self.value_0to1() * (f64::from(u32::max_value()) + 1.0)) as u32
    }

    pub fn from_degrees(deg: f64) -> Self {
        Self::from_0to1(deg / 360.0)
    }

    pub fn degrees(self) -> f64 {
        self.value * 360.0
    }

    pub fn from_hours(hours: f64) -> Self {
        Self::from_0to1(hours / 24.0)
    }

    pub fn hours(self) -> f64 {
        self.value * 24.0
    }

    fn merge_xms(
        is_negative: bool,
        degrees: f64,
        minutes: f64,
        seconds: f64,
        remainder_seconds: f64,
    ) -> f64 {
        let sign = if is_negative { -1.0 } else { 1.0 };
        sign * (degrees + minutes / 60.0 + (seconds + remainder_seconds) / (60.0 * 60.0))
    }

    fn value_to_xms(mut value: f64) -> (bool, u32, u32, u32, f64) {
        let sign = value.is_sign_negative();
        value = value.abs();
        let degrees = value as u32;
        value = (value - f64::from(degrees)) * 60.0;
        let minutes = value as u32;
        value = (value - f64::from(minutes)) * 60.0;
        let seconds = value as u32;
        let remainder = value - f64::from(seconds);
        (sign, degrees, minutes, seconds, remainder)
    }

    pub fn from_dms(
        is_negative: bool,
        degrees: f64,
        minutes: f64,
        seconds: f64,
        remainder_seconds: f64,
    ) -> Self {
        Self::from_degrees(Self::merge_xms(
            is_negative,
            degrees,
            minutes,
            seconds,
            remainder_seconds,
        ))
    }

    pub fn to_dms(self) -> (bool, u32, u32, u32, f64) {
        Self::value_to_xms(self.degrees())
    }

    pub fn from_hms(
        is_negative: bool,
        hours: f64,
        minutes: f64,
        seconds: f64,
        remainder_seconds: f64,
    ) -> Self {
        Self::from_hours(Self::merge_xms(
            is_negative,
            hours,
            minutes,
            seconds,
            remainder_seconds,
        ))
    }

    pub fn to_hms(self) -> (bool, u32, u32, u32, f64) {
        Self::value_to_xms(self.hours())
    }

    pub fn fmt_degrees(self) -> String {
        let (sign, degrees, minutes, seconds, _) = self.to_dms();
        let sign = if sign { "-" } else { "" };
        format!("{}{}°{}′{}″", sign, degrees, minutes, seconds)
    }

    pub fn fmt_hours(self) -> String {
        let (sign, degrees, minutes, seconds, _) = self.to_hms();
        let sign = if sign { "-" } else { "" };
        format!("{}{}h{}′{}″", sign, degrees, minutes, seconds)
    }

    pub fn parse(val: &str) -> Option<Self> {
        let capture = match DMS_PARSE_REGEX.captures(val) {
            Some(x) => x,
            None => return None,
        };
        let sign = capture
            .name("sign")
            .map(|x| x.as_str() == "-")
            .unwrap_or(false);
        let degrees: f64 = capture
            .name("degrees")
            .map(|x| x.as_str().parse().unwrap_or(0.0))
            .unwrap_or(0.0);
        let unit: &str = capture.name("unit").map(|x| x.as_str()).unwrap_or("d");
        let minutes: f64 = capture
            .name("minutes")
            .map(|x| x.as_str().parse().unwrap_or(0.0))
            .unwrap_or(0.0);
        let seconds: f64 = capture
            .name("seconds")
            .map(|x| x.as_str().parse().unwrap_or(0.0))
            .unwrap_or(0.0);
        if unit == "h" || unit == "H" {
            Some(Self::from_hms(sign, degrees, minutes, seconds, 0.0))
        } else {
            Some(Self::from_dms(sign, degrees, minutes, seconds, 0.0))
        }
    }
}

static DMS_PARSE_REGEX_STR: &str = r#"^\s*((?P<sign>[-+])\s*)?(?P<degrees>\d+(\.\d+)?)\s*(?P<unit>[hHdD°])\s*(((?P<minutes>\d+(\.\d+)?)\s*[mM'′]\s*)?((?P<seconds>\d+(\.\d+)?)\s*[sS""″]\s*)?)?$"#;
static DMS_PARSE_REGEX: LazyLock<Regex> =
    LazyLock::new(|| Regex::new(DMS_PARSE_REGEX_STR).expect("dms_parse_once regex is malformed"));

impl std::ops::Add for Angle {
    type Output = Self;
    fn add(self, rhs: Self) -> Self {
        Self::from_0to1(self.value_0to1() + rhs.value_0to1())
    }
}

impl std::ops::Sub for Angle {
    type Output = Self;
    fn sub(self, rhs: Self) -> Self {
        Self::from_0to1(self.value_0to1() - rhs.value_0to1())
    }
}

impl std::ops::AddAssign for Angle {
    fn add_assign(&mut self, rhs: Self) {
        *self = *self + rhs;
    }
}

impl std::ops::SubAssign for Angle {
    fn sub_assign(&mut self, rhs: Self) {
        *self = *self - rhs;
    }
}
