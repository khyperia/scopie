use crate::Image;

fn adjust_image(image: Image<u16>, zoom: bool, cross: bool) -> Image<u8> {
    let mut result = if zoom && image.width >= 100 && image.height >= 100 {
        let mut result = Vec::with_capacity(100 * 100 * 4);
        let off_x = image.width / 2 - 50;
        let off_y = image.height / 2 - 50;
        for y in 0..100 {
            let base = (off_y + y) * image.width + off_x;
            for item in &image.data[base..(base + 100)] {
                let one = (item >> 8) as u8;
                result.push(255);
                result.push(one);
                result.push(one);
                result.push(one);
            }
        }
        Image::new(result, 100, 100)
    } else {
        let mut result = Vec::with_capacity(image.data.len() * 4);
        for item in image.data {
            let one = (item >> 8) as u8;
            result.push(255);
            result.push(one);
            result.push(one);
            result.push(one);
        }
        Image::new(result, image.width, image.height)
    };
    if cross {
        // red (first element)
        let half_width = result.width / 2;
        for y in 0..(result.height) {
            result.data[(y * result.width + half_width) * 4 + 3] = 255;
        }
        let half_height = result.height / 2;
        for x in 0..(result.width) {
            result.data[(half_height * result.width + x) * 4 + 3] = 255;
        }
    }    result
}
