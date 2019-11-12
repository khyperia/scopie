use regex::Regex;
use std::sync::Once;

#[derive(Default, Clone, Copy)]
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
        let capture = match dms_parse_regex().captures(val) {
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
fn dms_parse_regex() -> &'static Regex {
    unsafe {
        DMS_PARSE_ONCE.call_once(|| {
            DMS_PARSE_REGEX =
                Some(Regex::new(DMS_PARSE_REGEX_STR).expect("dms_parse_once regex is malformed"))
        });
        DMS_PARSE_REGEX
            .as_ref()
            .expect("std::sync::Once didn't execute")
    }
}

static DMS_PARSE_ONCE: Once = Once::new();
static mut DMS_PARSE_REGEX: Option<Regex> = None;
