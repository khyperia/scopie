use crate::{camera::interface::ROIImage, Result};
use khygl::{render_texture::TextureRenderer, texture::Texture, Rect};
use std::sync::Arc;

pub struct ImageDisplay {
    raw: Option<Arc<ROIImage>>,
    texture: Option<Texture<u16>>,
    displayer: TextureRenderer,
    pub scale_offset: (f32, f32),
    pub cross: bool,
    pub bin: bool,
}

pub struct Mapping {
    pub scale: (f64, f64),
    pub offset: (f64, f64),
}

impl ImageDisplay {
    pub fn new() -> Self {
        // TODO: Cache new_binning()
        Self {
            raw: None,
            texture: None,
            displayer: TextureRenderer::new_binning()
                .expect("failed to build binning texture renderer"),
            scale_offset: (1.0, 0.0),
            cross: false,
            bin: true,
        }
    }

    pub fn raw(&self) -> &Option<Arc<ROIImage>> {
        &self.raw
    }

    pub fn set_raw(&mut self, raw: Arc<ROIImage>) -> Result<()> {
        let create = self
            .texture
            .as_ref()
            .map_or(true, |texture| texture.size != raw.image.size);
        if create {
            self.texture = Some({
                let tex = Texture::new(raw.image.size)?;
                tex.set_swizzle([gl::RED, gl::RED, gl::RED, gl::ONE])?;
                tex
            });
        }
        self.texture.as_mut().unwrap().upload(&raw.image)?;
        self.raw = Some(raw);
        Ok(())
    }

    fn rect_into_space(object_0to1: Rect<f64>, space: Rect<f64>) -> Rect<f64> {
        Rect::new(
            object_0to1.x * space.width + space.x,
            object_0to1.y * space.height + space.y,
            object_0to1.width * space.width,
            object_0to1.height * space.height,
        )
    }

    fn rect_from_space(object: Rect<f64>, space: Rect<f64>) -> Rect<f64> {
        Rect::new(
            (object.x - space.x) / space.width,
            (object.y - space.y) / space.height,
            object.width / space.width,
            object.height / space.height,
        )
    }

    fn space_transform(object: Rect<f64>, from: Rect<f64>, to: Rect<f64>) -> Rect<f64> {
        Self::rect_into_space(Self::rect_from_space(object, from), to)
    }

    pub fn draw(
        &mut self,
        pos: Rect<usize>,
        displayer: &TextureRenderer,
        screen_size: (f32, f32),
        roi: &crate::camera::display::ROIThing,
    ) -> Result<(Rect<usize>, Mapping)> {
        if let (Some(texture), Some(raw)) = (self.texture.as_ref(), self.raw.as_ref()) {
            let roi_unclamped = roi.get_roi_unclamped(&raw.original);
            let roi_clamped =
                crate::camera::display::ROIThing::clamp(roi_unclamped.clone(), &raw.location);

            let scale = ((pos.width as f64) / (roi_unclamped.width as f64))
                .min((pos.height as f64) / (roi_unclamped.height as f64));
            let screenspace_width = (roi_unclamped.width as f64) * scale;
            let screenspace_height = (roi_unclamped.height as f64) * scale;
            let screenspace_area = Rect::new(
                (pos.x + pos.width) as f64 - screenspace_width,
                pos.y as f64,
                screenspace_width,
                screenspace_height,
            );

            let roi_clamped_f64 = Rect::new(
                roi_clamped.x as f64,
                roi_clamped.y as f64,
                roi_clamped.width as f64,
                roi_clamped.height as f64,
            );
            let roi_unclamped_f64 = Rect::new(
                roi_unclamped.x as f64,
                roi_unclamped.y as f64,
                roi_unclamped.width as f64,
                roi_unclamped.height as f64,
            );

            let roi_clamped_in_screenspace =
                Self::space_transform(roi_clamped_f64, roi_unclamped_f64, screenspace_area);

            let src = Rect::new(
                roi_clamped.x - raw.location.x,
                roi_clamped.y - raw.location.y,
                roi_clamped.width,
                roi_clamped.height,
            );
            let dst = Rect::new(
                roi_clamped_in_screenspace.x.round(),
                roi_clamped_in_screenspace.y.round(),
                roi_clamped_in_screenspace.width.round(),
                roi_clamped_in_screenspace.height.round(),
            );

            let disp = if self.bin { &self.displayer } else { displayer };
            disp.render(texture, screen_size)
                .src(src.to_f32())
                .dst(dst.to_f32())
                .scale_offset(self.scale_offset)
                .go()?;
            if self.cross {
                let half_x = dst.x + (dst.width / 2.0);
                let half_y = dst.y + (dst.height / 2.0);
                displayer.line_x(
                    dst.x as usize,
                    dst.right() as usize,
                    half_y as usize,
                    [255.0, 0.0, 0.0, 255.0],
                    screen_size,
                )?;
                displayer.line_y(
                    half_x as usize,
                    dst.y as usize,
                    dst.bottom() as usize,
                    [255.0, 0.0, 0.0, 255.0],
                    screen_size,
                )?;
            }
            let (scale_x, offset_x) =
                remap_to_scale_offset(src.x as f64, src.right() as f64, dst.x, dst.right());
            let (scale_y, offset_y) =
                remap_to_scale_offset(src.y as f64, src.bottom() as f64, dst.y, dst.bottom());
            Ok((
                src,
                Mapping {
                    scale: (scale_x, scale_y),
                    offset: (offset_x, offset_y),
                },
            ))
        } else {
            Ok((
                Rect::new(0, 0, 1, 1),
                Mapping {
                    scale: (1.0, 1.0),
                    offset: (0.0, 0.0),
                },
            ))
        }
    }
}

fn remap_to_scale_offset(from_start: f64, from_end: f64, to_start: f64, to_end: f64) -> (f64, f64) {
    // lerp(to_start, to_end, invlerp(from_start, from_end, t))
    // invLerp(a, b, t) = (t - a) / (b - a)
    // lerp(a, b, t) = a + (b - a) * t
    // lerp(to_start, to_end, (t - from_start) / (from_end - from_start))
    // to_start + (to_end - to_start) * (t - from_start) / (from_end - from_start)
    // to_start + (to_end - to_start) * -from_start / (from_end - from_start) + (to_end - to_start) * t / (from_end - from_start)
    // to_start - from_start * (to_end - to_start) / (from_end - from_start) + t * (to_end - to_start) / (from_end - from_start)
    let slope = (to_end - to_start) / (from_end - from_start);
    let offset = to_start - from_start * slope;
    (slope, offset)
}
