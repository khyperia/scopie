use regex::Regex;

// isNegative, degrees, minutes, seconds, remainderSeconds
pub fn value_to_dms(mut value: f64) -> (bool, u32, u32, u32, f64) {
    let sign = value.is_sign_negative();
    value = value.abs();
    let degrees = value as u32;
    value = (value - degrees as f64) * 60.0;
    let minutes = value as u32;
    value = (value - minutes as f64) * 60.0;
    let seconds = value as u32;
    let remainder = value - seconds as f64;
    (sign, degrees, minutes, seconds, remainder)
}

pub fn dms_to_value(
    is_negative: bool,
    degrees: f64,
    minutes: f64,
    seconds: f64,
    remainder_seconds: f64,
) -> f64 {
    let sign = if is_negative { -1.0 } else { 1.0 };
    sign * (degrees + minutes / 60.0 + (seconds + remainder_seconds) / (60.0 * 60.0))
}

pub fn print_dms(val: f64, is_hours: bool) -> String {
    let (sign, degrees, minutes, seconds, _) = value_to_dms(val);
    let sign = if sign { "-" } else { "" };
    let deg_unit = if is_hours { "h" } else { "°" };
    format!(
        "{}{}{}{}′{}″",
        sign, degrees, deg_unit, minutes, seconds
    )
}

pub fn parse_dms(val: &str) -> Option<f64> {
    lazy_static! {
        static ref RE: Regex = Regex::new(r#"^\s*((?P<sign>[-+])\s*)?(?P<degrees>\d+(\.\d+)?)\s*([hHdD°]\s*((?P<minutes>\d+(\.\d+)?)\s*[mM'′]\s*)?((?P<seconds>\d+(\.\d+)?)\s*[sS""″]\s*)?)?$"#).unwrap();
    }
    let capture = match RE.captures(val) {
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
    let minutes: f64 = capture
        .name("minutes")
        .map(|x| x.as_str().parse().unwrap_or(0.0))
        .unwrap_or(0.0);
    let seconds: f64 = capture
        .name("seconds")
        .map(|x| x.as_str().parse().unwrap_or(0.0))
        .unwrap_or(0.0);
    Some(dms_to_value(sign, degrees, minutes, seconds, 0.0))
}
