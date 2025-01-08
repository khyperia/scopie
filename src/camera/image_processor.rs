use crate::{camera::interface::ROIImage, Result, UiThread};
use eframe::egui::{self, ColorImage};
use std::{
    fs::create_dir_all,
    path::PathBuf,
    sync::{mpsc, Arc, Mutex},
    thread,
};
use time::macros::format_description;

pub struct ImageProcessor {
    pub image: Option<ColorImage>,
    pub save: u32,
    pub status: String,
}

impl ImageProcessor {
    pub fn new(ui_thread: UiThread, recv: mpsc::Receiver<ROIImage>) -> Arc<Mutex<Self>> {
        let result = Arc::new(Mutex::new(Self {
            image: None,
            save: 0,
            status: String::new(),
        }));
        let result2 = result.clone();
        thread::spawn(move || {
            if let Err(err) = Self::run_thread(result2, ui_thread, recv) {
                println!("image processor thread error {:?}", err)
            }
        });
        result
    }

    fn run_thread(
        me: Arc<Mutex<Self>>,
        ui_thread: UiThread,
        recv: mpsc::Receiver<ROIImage>,
    ) -> Result<()> {
        for item in recv.iter() {
            let (save, setui) = {
                let mut me = me.lock().unwrap();
                let save = me.save > 0;
                if save {
                    me.save -= 1;
                }
                (save, me.image.is_none())
            };
            if setui {
                let convert = Self::to_display(&item);
                me.lock().unwrap().image = Some(convert);
                ui_thread.trigger();
            } else {
                me.lock().unwrap().status = "Warning! UI can't keep up with new images!".into();
            }
            if save {
                match Self::get_filename().and_then(|fname| {
                    crate::write_png(&fname, &item.image)?;
                    Ok(fname)
                }) {
                    Ok(fname) => {
                        me.lock().unwrap().status = format!(
                            "Saved image: {}",
                            fname.file_name().unwrap_or_default().to_string_lossy()
                        )
                    }
                    Err(err) => me.lock().unwrap().status = format!("Error saving image: {}", err),
                }
            }
        }
        Ok(())
    }

    fn to_display(image: &ROIImage) -> egui::ColorImage {
        egui::ColorImage {
            size: [image.image.size.0, image.image.size.1],
            pixels: image
                .image
                .data()
                .iter()
                .map(|v| egui::Color32::from_gray((v >> 8) as u8))
                .collect(),
        }
    }

    fn get_filename() -> Result<PathBuf> {
        let tm = time::OffsetDateTime::now_local()?;
        // let dirname = tm.format("%Y_%m_%d");
        let dirname = tm.format(format_description!("[year]_[month]_[day]"))?;
        let mut filepath = dirs::desktop_dir().unwrap_or_else(PathBuf::new);
        filepath.push(dirname);
        // if !self.folder.is_empty() {
        //     filepath.push(&self.folder);
        // }
        if !filepath.exists() {
            create_dir_all(&filepath)?;
        }
        // let filepath1 = filepath.join(tm.format("telescope.%Y-%m-%d.%H-%M-%S.png"));
        let filepath1 = filepath.join(tm.format(format_description!(
            "telescope.[year]-[month]-[day].[hour repr:24]-[minute]-[second].png"
        ))?);
        if !filepath1.exists() {
            return Ok(filepath1);
        }
        let filepath2 = filepath.join(tm.format(format_description!("telescope.[year]-[month]-[day].[hour repr:24]-[minute]-[second].[subsecond digits:3].png"))?);
        if !filepath2.exists() {
            return Ok(filepath2);
        }
        let base3 = tm.format(format_description!(
            "telescope.[year]-[month]-[day].[hour repr:24]-[minute]-[second].[subsecond digits:3]."
        ))?;
        for i in 1.. {
            let filepath3 = filepath.join(format!("{}{}.png", base3, i));
            if !filepath3.exists() {
                return Ok(filepath3);
            }
        }
        panic!("Unable to find free file");
    }
}
