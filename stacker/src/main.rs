use rustfft::num_complex::Complex64;
use rustfft::num_traits::identities::Zero;
use std::env::current_dir;
use std::f64;
use std::fs::read_dir;
use std::fs::File;
use std::path::Path;

fn main() {
    println!("Reading");
    let mut pngs: Vec<ComplexImage> = Vec::new();
    for file in read_dir(current_dir().unwrap()).unwrap() {
        let file = file.unwrap();
        let filename = file.file_name();
        let name: &str = filename.to_str().unwrap();
        if name.ends_with(".png") && name != "out.png" {
            let new_png = read_png(file.path());
            if let Some(first) = pngs.first() {
                if first.width != new_png.width || first.height != new_png.height {
                    println!("Error: not all PNGs are same size");
                    return;
                }
            }
            pngs.push(new_png.to_complex());
        }
    }
    // let asdf = ComplexImage::new(
    //     pngs[0]
    //         .pixels
    //         .iter()
    //         .zip(pngs[1].pixels.iter())
    //         .map(|(a, b)| a + b)
    //         .collect(),
    //     pngs[0].width,
    //     pngs[0].height,
    // );
    // write_png("out.png", asdf.to_image_norm());
    println!("FFT");
    let ffts = pngs.iter().map(|img| img.fft(false)).collect::<Vec<_>>();
    if ffts.len() == 2 {
        println!("Weird mul");
        let weird = ffts[0].weird_mul(&ffts[1]);
        let output = weird.fft(true);
        println!("Writing");
        write_png("out.png", output.to_image_norm());
    }
}

struct Image {
    pixels: Vec<u16>,
    width: usize,
    height: usize,
}

impl Image {
    fn new(pixels: Vec<u16>, width: usize, height: usize) -> Self {
        Self {
            pixels,
            width,
            height,
        }
    }

    fn to_complex(&self) -> ComplexImage {
        ComplexImage::new(
            self.pixels
                .iter()
                .map(|&x| (x as f64 / u16::max_value() as f64).into())
                .collect(),
            self.width,
            self.height,
        )
    }
}

fn read_png(path: impl AsRef<Path>) -> Image {
    let mut decoder = png::Decoder::new(File::open(path).unwrap());
    decoder.set_transformations(png::Transformations::IDENTITY);
    let (info, mut reader) = decoder.read_info().unwrap();
    assert_eq!(info.bit_depth, png::BitDepth::Sixteen);
    assert_eq!(info.color_type, png::ColorType::Grayscale);
    let mut buf = vec![0; info.buffer_size()];
    reader.next_frame(&mut buf).unwrap();
    let mut buf16 = vec![0; info.width as usize * info.height as usize];
    for i in 0..buf16.len() {
        buf16[i] = (buf[i * 2] as u16) << 8 | (buf[i * 2 + 1] as u16);
    }
    Image::new(buf16, info.width as usize, info.height as usize)
}

fn write_png(path: impl AsRef<Path>, img: Image) {
    let mut encoder = png::Encoder::new(
        File::create(path).unwrap(),
        img.width as u32,
        img.height as u32,
    );
    encoder.set_color(png::ColorType::Grayscale);
    encoder.set_depth(png::BitDepth::Sixteen);
    let mut writer = encoder.write_header().unwrap();
    let mut output = vec![0; img.width * img.height * 2];
    for i in 0..(img.width * img.height) {
        output[i * 2] = (img.pixels[i] >> 8) as u8;
        output[i * 2 + 1] = (img.pixels[i]) as u8;
    }
    writer.write_image_data(&output).unwrap();
}

struct ComplexImage {
    pixels: Vec<Complex64>,
    width: usize,
    height: usize,
}

fn ilerp(a: f64, b: f64, v: f64) -> f64 {
    // TODO: Use clamp() once stabilized
    let res = (v - a) / (b - a);
    if res < 0.0 {
        0.0
    } else if res > 1.0 {
        1.0
    } else {
        res
    }
}

impl ComplexImage {
    fn new(pixels: Vec<Complex64>, width: usize, height: usize) -> Self {
        Self {
            pixels,
            width,
            height,
        }
    }

    fn to_image_norm(&self) -> Image {
        println!("0,0 = {}", self[(0, 0)]);
        let invalids: usize = self
            .pixels
            .iter()
            .map(|x| if x.re.is_finite() { 0 } else { 1 })
            .sum();
        println!("num invalids: {}", invalids);
        let mut sorted = self.pixels.iter().map(|x| x.re.max(1.0).ln()).collect::<Vec<_>>();
        sorted.sort_by(|a, b| a.partial_cmp(b).unwrap());
        let max = sorted[sorted.len() - 2];
        let min = sorted[sorted.len() - 10000];
        for x in 0..100 {
            println!("{}", sorted[sorted.len() - 100 + x]);
        }
        println!("min: {}", min);
        println!("max: {}", max);
        Image::new(
            self.pixels
                .iter()
                .map(|&x| (ilerp(min, max, x.re.max(1.0).ln()) * (u16::max_value() as f64)) as u16)
                .collect(),
            self.width,
            self.height,
        )
    }

    fn transpose(&self) -> Self {
        let mut output = Self::new(
            vec![Complex64::zero(); self.pixels.len()],
            self.height,
            self.width,
        );
        for y in 0..self.height {
            for x in 0..self.width {
                output[(y, x)] = self[(x, y)];
            }
        }
        output
    }

    fn fft_x(&mut self, inverse: bool) {
        let f = rustfft::FFTplanner::new(inverse).plan_fft(self.width);
        let mut output = vec![Complex64::zero(); self.pixels.len()];
        f.process_multi(&mut self.pixels, &mut output);
        self.pixels = output;
    }

    fn fft(&self, inverse: bool) -> Self {
        println!("FFT step 1");
        let mut res = self.transpose();
        println!("FFT step 2");
        res.fft_x(inverse);
        println!("FFT step 3");
        res = res.transpose();
        println!("FFT step 4");
        res.fft_x(inverse);
        println!("FFT done");
        res
    }

    fn weird_mul(&self, other: &Self) -> Self {
        let out = self
            .pixels
            .iter()
            .zip(other.pixels.iter())
            .map(|(a, b)| {
                let prod = a * b.conj();
                prod / prod.norm()
            })
            .collect::<Vec<_>>();
        Self::new(out, self.width, self.height)
    }
}

impl std::ops::Index<(usize, usize)> for ComplexImage {
    type Output = Complex64;

    fn index(&self, index: (usize, usize)) -> &Complex64 {
        &self.pixels[index.1 * self.width + index.0]
    }
}

impl std::ops::IndexMut<(usize, usize)> for ComplexImage {
    fn index_mut(&mut self, index: (usize, usize)) -> &mut Complex64 {
        &mut self.pixels[index.1 * self.width + index.0]
    }
}
