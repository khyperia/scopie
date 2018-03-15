fn main() {
    println!("cargo:rustc-link-lib=stdc++");
    println!("cargo:rustc-link-lib=usb-1.0");
    println!("cargo:rustc-link-search=native=.");
}
