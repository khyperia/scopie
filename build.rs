use std::{env, fs::copy, path::PathBuf};

fn main() {
    let out_dir = env::var("CARGO_MANIFEST_DIR").unwrap();
    println!("cargo:rustc-link-search=native={}", out_dir);
    let files = [
        ("lib/qhyccd/x64/ftd2xx.dll", "ftd2xx.dll"),
        ("lib/qhyccd/x64/msvcr90.dll", "msvcr90.dll"),
        ("lib/qhyccd/x64/qhyccd.dll", "qhyccd.dll"),
        ("lib/qhyccd/x64/qhyccd.lib", "qhyccd.lib"),
        ("lib/qhyccd/x64/tbb.dll", "tbb.dll"),
        ("lib/qhyccd/x64/winusb.dll", "winusb.dll"),
    ];
    for (file, dest) in &files {
        println!("cargo:rerun-if-changed={}", file);
        let mut path = PathBuf::new();
        path.push(&out_dir);
        path.push(dest);
        eprintln!("copying {:?} to {:?}", file, path);
        copy(file, path).unwrap();
    }
}
