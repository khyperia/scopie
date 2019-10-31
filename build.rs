use std::{env, fs::copy, path::PathBuf};

fn main() {
    let out_dir = env::var("OUT_DIR").unwrap();
    println!("cargo:rustc-link-search=native={}", out_dir);
    let files = [
        ("lib/qhyccd/ftd2xx64.dll", "ftd2xx64.dll"),
        ("lib/qhyccd/tbb_x64.dll", "tbb_x64.dll"),
        ("lib/qhyccd/qhyccd_x64.dll", "qhyccd.dll"),
        ("lib/qhyccd/lib/qhyccd_x64.lib", "qhyccd_x64.lib"),
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
