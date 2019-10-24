use std::env;
use std::fs::copy;
use std::fs::read_dir;
use std::path::PathBuf;

fn main() {
    let out_dir = env::var("OUT_DIR").unwrap();
    for dir in &["lib", "lib/qhyccd"] {
        println!("cargo:rustc-link-search=native={}", dir);
        println!("cargo:rerun-if-changed={}", dir);
        for file in read_dir(dir).unwrap() {
            let file = file.unwrap();
            if file.file_type().unwrap().is_file()
                && file.file_name().to_str().unwrap().ends_with(".dll")
            {
                let mut path = PathBuf::new();
                path.push(&out_dir);
                path.push(file.file_name());
                copy(file.path(), path).unwrap();
            }
        }
    }
}
