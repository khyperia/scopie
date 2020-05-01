use crate::{dms::Angle, Result, SendUserUpdate, UserUpdate};
use khygl::texture::CpuTexture;
use regex::Regex;
use std::{env::var, ffi::OsString, path::PathBuf, process::Command, sync::Once, thread};

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

// note: missing filename at end, must append
const COMMAND: &[&str] = &[
    "/usr/bin/solve-field",
    "-p",
    "-O",
    "-U",
    "none",
    "-B",
    "none",
    "-R",
    "none",
    "-M",
    "none",
    "-N",
    "none",
    "-W",
    "none",
    "-C",
    "cancel",
    "--crpix-center",
    "-z",
    "4",
    "--objs",
    "100",
    "-u",
    "arcsecperpix",
    "-L",
    "0.9",
    "-H",
    "1.1",
];

pub fn platesolve(tex: &CpuTexture<u16>, send_user_update: SendUserUpdate) -> Result<()> {
    let linux_file_location = "/tmp/image.png";
    let (cmd, args) = if cfg!(windows) {
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
            return Err("ANSVR not installed".into());
        }

        crate::write_png(windows_file_location, tex)?;

        let linux_command = format!("{} {}", COMMAND.join(" "), linux_file_location);

        (
            OsString::from(bash_location),
            vec!["--login".to_string(), "-c".to_string(), linux_command],
        )
    } else {
        crate::write_png(linux_file_location, tex)?;

        (
            OsString::from(COMMAND[0]),
            COMMAND[1..]
                .iter()
                .copied()
                .chain(std::iter::once(linux_file_location))
                .map(|c| c.to_string())
                .collect(),
        )
    };

    thread::spawn(move || {
        let output = String::from_utf8(
            Command::new(cmd)
                .args(args)
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
