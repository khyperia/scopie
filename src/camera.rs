use crate::qhycamera as qhy;
use crate::qhycamera::ControlId;
use crate::qhycamera::QHYCCD;
use crate::Image;
use crate::Result;
use std::error::Error;
use std::fmt;
use std::str;

#[derive(Debug)]
struct QhyError {
    code: u32,
}

impl fmt::Display for QhyError {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        write!(f, "QHY error: {}", self.code as i32)
    }
}

impl Error for QhyError {
    fn description(&self) -> &str {
        "QHY error"
    }
}

fn check(code: u32) -> ::std::result::Result<(), QhyError> {
    if code == 0 {
        Ok(())
    } else {
        Err(QhyError { code })
    }
}

pub struct CameraInfo {
    name: String,
}

impl CameraInfo {
    pub fn new(id: u32) -> Result<CameraInfo> {
        // TODO: InitQHYCCDResource
        qhy::InitQHYCCDResource();
        let result = unsafe {
            let mut data = vec![0; 512];
            check(qhy::GetQHYCCDId(id, data.as_mut_ptr()));
            let name = str::from_utf8(&data[..data.iter().position(|&c| c == 0).unwrap()])
                .unwrap()
                .to_string();
            CameraInfo { name }
        };
        Ok(result)
    }

    pub fn open(self, live: bool) -> Camera {
        Camera::open(self, live)
    }

    pub fn name(&self) -> &str {
        &self.name
    }
}

pub struct Camera {
    handle: QHYCCD,
    name: String,
    controls: Vec<Control>,
    size: (u32, u32),
    use_live: bool,
}

impl Camera {
    pub fn num_cameras() -> u32 {
        unsafe { qhy::ScanQHYCCD() }
    }

    fn open(info: CameraInfo, use_live: bool) -> Camera {
        unsafe {
            let handle = qhy::OpenQHYCCD(info.name.as_ptr());
            let controls = Self::get_controls(handle);

            let mut bpp = 0;
            let mut chip_width = 0.0;
            let mut chip_height = 0.0;
            let mut original_width = 0;
            let mut original_height = 0;
            let mut pixel_width = 0.0;
            let mut pixel_height = 0.0;
            check(qhy::SetQHYCCDStreamMode(
                handle,
                if use_live { 1 } else { 0 },
            )); // 0 == single, 1 == stream
            check(qhy::InitQHYCCD(handle));
            check(qhy::GetQHYCCDChipInfo(
                handle,
                &mut chip_width,
                &mut chip_height,
                &mut original_width,
                &mut original_height,
                &mut pixel_width,
                &mut pixel_height,
                &mut bpp,
            ));
            println!("chip name: {}, chip width: {}, chip height: {}, image width: {}, image height: {}, pixel width: {}, pixel height: {}, bits per pixel: {}", info.name, chip_width, chip_height, original_width, original_height, pixel_width, pixel_height, bpp);
            check(qhy::IsQHYCCDControlAvailable(
                handle,
                ControlId::ControlTransferbit,
            ));
            check(qhy::SetQHYCCDBitsMode(handle, 16));
            check(qhy::SetQHYCCDBinMode(handle, 1, 1));
            check(qhy::SetQHYCCDResolution(
                handle,
                0,
                0,
                original_width,
                original_height,
            ));

            Camera {
                handle,
                name: info.name,
                controls,
                size: (original_width, original_height),
                use_live,
            }
        }
    }

    pub fn size(&self) -> (u32, u32) {
        self.size
    }

    pub fn name(&self) -> &str {
        &self.name
    }

    fn get_controls(handle: QHYCCD) -> Vec<Control> {
        ControlId::values()
            .iter()
            .cloned()
            .filter(|&id| qhy::IsQHYCCDControlAvailable(handle, id) == 0)
            .map(|id| Control::new(handle, id))
            .collect::<Vec<_>>()
    }

    pub fn controls(&self) -> &[Control] {
        &self.controls
    }

    fn start_single(&self) -> Result<()> {
        Ok(check(qhy::ExpQHYCCDSingleFrame(self.handle))?)
    }

    fn stop_single(&self) -> Result<()> {
        Ok(check(qhy::CancelQHYCCDExposingAndReadout(self.handle))?)
    }

    fn get_single(&self) -> Result<Option<Image<u16>>> {
        let remaining_ms = qhy::GetQHYCCDExposureRemaining(self.handle);
        // QHY recommends 100 in qhyccd.h, I guess?
        if remaining_ms > 100 {
            return Ok(None);
        }
        let len = qhy::GetQHYCCDMemLength(self.handle) as usize;
        let mut width = 0;
        let mut height = 0;
        let mut bpp = 0;
        let mut channels = 0;
        let mut data = vec![0; len];
        check(qhy::GetQHYCCDSingleFrame(
            self.handle,
            &mut width,
            &mut height,
            &mut bpp,
            &mut channels,
            data.as_mut_ptr() as _,
        ))?;
        assert_eq!(bpp, 16);
        assert_eq!(channels, 1);
        Ok(Some(Image::new(data, width as usize, height as usize)))
    }

    fn start_live(&self) -> Result<()> {
        Ok(check(qhy::BeginQHYCCDLive(self.handle))?)
    }

    fn stop_live(&self) -> Result<()> {
        Ok(check(qhy::StopQHYCCDLive(self.handle))?)
    }

    fn get_live(&self) -> Result<Option<Image<u16>>> {
        let len = qhy::GetQHYCCDMemLength(self.handle) as usize;
        let mut width = 0;
        let mut height = 0;
        let mut bpp = 0;
        let mut channels = 0;
        let mut data = vec![0; len];
        let res = qhy::GetQHYCCDLiveFrame(
            self.handle,
            &mut width,
            &mut height,
            &mut bpp,
            &mut channels,
            data.as_mut_ptr() as _,
        );
        if res != 0 {
            // function will fail if image isn't ready yet
            return Ok(None);
        }
        assert_eq!(bpp, 16);
        assert_eq!(channels, 1);
        Ok(Some(Image::new(data, width as usize, height as usize)))
    }

    pub fn start(&self) -> Result<()> {
        if self.use_live {
            self.start_live()
        } else {
            self.start_single()
        }
    }

    pub fn stop(&self) -> Result<()> {
        if self.use_live {
            self.stop_live()
        } else {
            self.stop_single()
        }
    }

    pub fn try_get(&self) -> Result<Option<Image<u16>>> {
        if self.use_live {
            self.get_live()
        } else {
            let res = self.get_single()?;
            self.start_single()?;
            Ok(res)
        }
    }
}

impl Drop for Camera {
    fn drop(&mut self) {
        match check(unsafe { qhy::CloseQHYCCD(self.handle) }) {
            Ok(()) => (),
            Err(err) => eprintln!("Closing camera: {}", err),
        }
    }
}

pub struct Control {
    handle: QHYCCD,
    control: ControlId,
    min: f64,
    max: f64,
    step: f64,
    readonly: bool,
}

impl Control {
    fn new(handle: QHYCCD, control: ControlId) -> Control {
        let mut min = 0.0;
        let mut max = 0.0;
        let mut step = 0.0;
        let res = qhy::GetQHYCCDParamMinMaxStep(handle, control, &mut min, &mut max, &mut step);
        let readonly = res != 0;
        Self {
            handle,
            control,
            min,
            max,
            step,
            readonly,
        }
    }

    pub fn name(&self) -> String {
        format!("{}", self.control)
    }

    pub fn max_value(&self) -> f64 {
        self.max
    }
    pub fn min_value(&self) -> f64 {
        self.min
    }
    pub fn readonly(&self) -> bool {
        self.readonly
    }

    pub fn get(&self) -> f64 {
        unsafe { qhy::GetQHYCCDParam(self.handle, self.control) }
    }

    pub fn set(&self, value: f64) -> Result<()> {
        check(unsafe { qhy::SetQHYCCDParam(self.handle, self.control, value) })?;
        Ok(())
    }
}

impl fmt::Display for Control {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        if self.readonly {
            write!(f, "{} = {} (readonly)", self.control, self.get())
        } else {
            write!(
                f,
                "{} = {} ({}-{} by {})",
                self.control,
                self.get(),
                self.min,
                self.max,
                self.step
            )
        }
    }
}
