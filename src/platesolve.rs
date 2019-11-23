use crate::{dms::Angle, Result, SendUserUpdate, UserUpdate};
use khygl::texture::CpuTexture;
use regex::Regex;
use std::{env::var, path::PathBuf, process::Command, sync::Once, thread};

static PARSE_ONCE: Once = Once::new();
static mut PARSE_REGEX: Option<Regex> = None;
static REGEX_STR: &str = r"RA,Dec = \((\d+\.?\d*),(\d+\.?\d*)\)";
fn parse_regex() -> &'static Regex {
    unsafe {
        PARSE_ONCE.call_once(|| {
            PARSE_REGEX = Some(Regex::new(REGEX_STR).expect("parse_once regex is malformed"))
        });
        PARSE_REGEX
            .as_ref()
            .expect("std::sync::Once didn't execute")
    }
}

pub fn platesolve(tex: &CpuTexture<u16>, send_user_update: SendUserUpdate) -> Result<()> {
    let local_app_data = var("LOCALAPPDATA")?;
    let mut windows_file_location = PathBuf::new();
    windows_file_location.push(&local_app_data);
    windows_file_location.push("cygwin_ansvr");
    windows_file_location.push("tmp");
    let ok = windows_file_location.is_dir();
    windows_file_location.push("image.png");
    let mut bash_location = PathBuf::new();
    bash_location.push(&local_app_data);
    bash_location.push("cygwin_ansvr");
    bash_location.push("bin");
    bash_location.push("bash.exe");

    if !ok {
        return Err(failure::err_msg("ANSVR not installed"));
    }

    crate::write_png(windows_file_location, tex)?;

    thread::spawn(move || {
        let downsample = 4;
        let low = 0.9;
        let high = 1.1;
        let max_objects = 100;
        let filename = "image.png";

        let cmd = format!("/usr/bin/solve-field -p -O -U none -B none -R none -M none -N none -W none -C cancel --crpix-center -z {} --objs {} -u arcsecperpix -L {} -H {} /tmp/{}", downsample, max_objects, low, high, filename);

        let output = String::from_utf8(
            Command::new(bash_location)
                .arg("--login")
                .arg("-c")
                .arg(cmd)
                .output()
                .expect("Failed to execute solve command")
                .stdout,
        )
        .expect("Solve command output was not utf8");

        let caps = parse_regex()
            .captures(&output)
            .expect("Error searching solve-field output");
        let ra = caps
            .get(1)
            .expect("solve-field regex had no group 1")
            .as_str()
            .parse()
            .expect("couldn't parse solve-field ra");
        let dec = caps
            .get(2)
            .expect("solve-field regex had no group 2")
            .as_str()
            .parse()
            .expect("couldn't parse solve-field dec");
        send_user_update
            .send_event(UserUpdate::SolveFinished(
                Angle::from_degrees(ra),
                Angle::from_degrees(dec),
            ))
            .expect("couldn't send UserUpdate::SolveFinished");
    });
    Ok(())
}
