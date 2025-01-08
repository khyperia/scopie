fn main() {
    let target = std::env::var("TARGET").expect("TARGET was not set");
    let out_dir = std::env::var("CARGO_MANIFEST_DIR").unwrap();
    println!("cargo:rustc-link-search=native={}", out_dir);
    if target.contains("linux") {
        println!("cargo:rustc-link-lib=usb-1.0");
        println!("cargo:rustc-link-lib=static=stdc++");
    }
}
