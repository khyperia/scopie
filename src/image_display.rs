use crate::Result;
use khygl::{
    render_texture::TextureRenderer,
    texture::{CpuTexture, Texture},
    Rect,
};
use std::sync::Arc;

pub struct ImageDisplay {
    raw: Option<Arc<CpuTexture<u16>>>,
    texture: Option<Texture<u16>>,
    displayer: TextureRenderer,
    pub scale_offset: (f32, f32),
    pub zoom: bool,
    pub cross: bool,
    pub bin: bool,
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
            zoom: false,
            cross: false,
            bin: true,
        }
    }

    pub fn raw(&self) -> &Option<Arc<CpuTexture<u16>>> {
        &self.raw
    }

    pub fn set_raw(&mut self, raw: Arc<CpuTexture<u16>>) -> Result<()> {
        let create = self
            .texture
            .as_ref()
            .map_or(true, |texture| texture.size != raw.size);
        if create {
            self.texture = Some({
                let tex = Texture::new(raw.size)?;
                tex.set_swizzle([gl::RED, gl::RED, gl::RED, gl::ONE])?;
                tex
            });
        }
        self.texture.as_mut().unwrap().upload(&raw)?;
        self.raw = Some(raw);
        Ok(())
    }

    pub fn draw(
        &mut self,
        pos: Rect<usize>,
        displayer: &TextureRenderer,
        screen_size: (f32, f32),
    ) -> Result<()> {
        if let Some(ref texture) = self.texture {
            let src = if self.zoom {
                let zoom_size = 512.0;
                let src_x = texture.size.0 as f32 / 2.0 - zoom_size / 2.0;
                let src_y = texture.size.1 as f32 / 2.0 - zoom_size / 2.0;
                Rect::new(src_x, src_y, zoom_size, zoom_size)
            } else {
                Rect::new(0.0, 0.0, texture.size.0 as f32, texture.size.1 as f32)
            };

            let scale = ((pos.width as f32) / (src.width as f32))
                .min((pos.height as f32) / (src.height as f32));
            let dst_width = ((src.width as f32) * scale).round();
            let dst_height = ((src.height as f32) * scale).round();
            let dst = Rect::new(
                (pos.x + pos.width) as f32 - dst_width,
                pos.y as f32,
                dst_width,
                dst_height,
            );
            let disp = if self.bin { &self.displayer } else { displayer };
            disp.render(texture, screen_size)
                .src(src)
                .dst(dst.clone())
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
        }
        Ok(())
    }
}
