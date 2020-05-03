use super::floodfind;
use khygl::texture::CpuTexture;

/*
Alternate algorithm:

1) Find brightest pixel
2) Flood fill out to brightest neighbor, until certain number of pixels in size, or threshhold is reached (median of surroundings?)
3) Mask out star, goto 1, until N stars are found
*/

#[derive(Debug)]
pub struct Star {
    pub x: f64,
    pub y: f64,
    // flux isn't *totally* correct, since the fringes of the star are cut off and not added
    pub flux: f64,
    pub hfr: f64,
}

impl Star {
    pub fn new(x: f64, y: f64, flux: f64, hfr: f64) -> Self {
        Self { x, y, flux, hfr }
    }
}

pub fn find_stars(img: &CpuTexture<u16>, mean: f64, stdev: f64) -> Vec<Star> {
    let noise_sigma = 3.0;
    let floor = mean + stdev * noise_sigma;
    let floor_u16 = floor as u16;
    let min_size = 5;
    let max_size = 100;
    let mut stars = Vec::new();
    for star in floodfind(img, |v| v > floor_u16) {
        if star.len() > max_size {
            continue;
        }
        if star.len() < min_size {
            continue;
        }
        let mut sum = (0.0, 0.0);
        let mut total_flux_above = 0.0;
        for &pixel in &star {
            let flux_above = img[pixel] as f64 - floor;
            sum = (
                sum.0 + pixel.0 as f64 * flux_above,
                sum.1 + pixel.1 as f64 * flux_above,
            );
            total_flux_above += flux_above;
        }
        let location = (sum.0 / total_flux_above, sum.1 / total_flux_above);
        // https://en.wikipedia.org/wiki/Half_flux_diameter
        let mut hfr_weird_sum = 0.0;
        for pixel in star {
            let flux_above = img[pixel] as f64 - floor;
            let dist_x = pixel.0 as f64 - location.0;
            let dist_y = pixel.1 as f64 - location.1;
            let dist = (dist_x * dist_x + dist_y * dist_y).sqrt();
            hfr_weird_sum += flux_above * dist;
        }
        let hfr = hfr_weird_sum / total_flux_above;
        stars.push(Star::new(location.0, location.1, total_flux_above, hfr));
    }
    stars
}
