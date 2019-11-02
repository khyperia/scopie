use crate::{
    qhycamera as qhy,
    qhycamera::{ControlId, QHYCCD},
    Result,
};
use khygl::texture::CpuTexture;
use std::{error::Error, fmt, str, sync::Once};

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

static INIT_QHYCCD_RESOURCE: Once = Once::new();

fn init_qhyccd_resource() {
    INIT_QHYCCD_RESOURCE.call_once(|| unsafe {
        check(qhy::InitQHYCCDResource()).expect("Failed to init QHY resources")
    })
}

pub fn autoconnect(live: bool) -> Result<Camera> {
    init_qhyccd_resource();
    let mut best = None;
    for id in 0..Camera::num_cameras() {
        let info = CameraInfo::new(id)?;
        let is163 = info.name.contains("163");
        if best.is_none() || is163 {
            best = Some(info);
            if is163 {
                break;
            }
        }
    }
    if let Some(best) = best {
        Ok(best.open(live)?)
    } else {
        Err(failure::err_msg("No QHY cameras found"))
    }
}

pub struct CameraInfo {
    name: String,
}

impl CameraInfo {
    pub fn new(id: u32) -> Result<CameraInfo> {
        init_qhyccd_resource();
        let result = unsafe {
            let mut data = vec![0; 512];
            check(qhy::GetQHYCCDId(id, data.as_mut_ptr()))?;
            let name = str::from_utf8(&data[..data.iter().position(|&c| c == 0).unwrap()])
                .unwrap()
                .to_string();
            CameraInfo { name }
        };
        Ok(result)
    }

    pub fn open(self, live: bool) -> Result<Camera> {
        Camera::open(self, live)
    }

    pub fn name(&self) -> &str {
        &self.name
    }
}

pub struct Camera {
    handle: QHYCCD,
    info: CameraInfo,
    controls: Vec<Control>,
    use_live: bool,
}

impl Camera {
    pub fn num_cameras() -> u32 {
        unsafe { qhy::ScanQHYCCD() }
    }

    fn open(info: CameraInfo, use_live: bool) -> Result<Camera> {
        unsafe {
            let handle = qhy::OpenQHYCCD(info.name.as_ptr());

            check(qhy::SetQHYCCDStreamMode(
                handle,
                if use_live { 1 } else { 0 },
            ))?; // 0 == single, 1 == stream
            check(qhy::InitQHYCCD(handle))?;
            check(qhy::IsQHYCCDControlAvailable(
                handle,
                ControlId::ControlTransferbit,
            ))?;
            check(qhy::SetQHYCCDBitsMode(handle, 16))?;
            check(qhy::SetQHYCCDBinMode(handle, 1, 1))?;
            if use_live {
                // TODO: Assert this?
                check(qhy::SetQHYCCDParam(
                    handle,
                    ControlId::ControlUsbtraffic,
                    0.0,
                ))?;
            }

            let controls = Self::get_controls(handle);

            Ok(Camera {
                handle,
                info,
                controls,
                use_live,
            })
        }
    }

    pub fn info(&self) -> &CameraInfo {
        &self.info
    }

    pub fn name(&self) -> &str {
        &self.info().name()
    }

    fn get_controls(handle: QHYCCD) -> Vec<Control> {
        ControlId::values()
            .iter()
            .cloned()
            .filter(|&id| unsafe { qhy::IsQHYCCDControlAvailable(handle, id) } == 0)
            .map(|id| Control::new(handle, id))
            .collect::<Vec<_>>()
    }

    pub fn controls(&self) -> &[Control] {
        &self.controls
    }

    fn start_single(&self) -> Result<()> {
        unsafe { Ok(check(qhy::ExpQHYCCDSingleFrame(self.handle))?) }
    }

    fn stop_single(&self) -> Result<()> {
        unsafe { Ok(check(qhy::CancelQHYCCDExposingAndReadout(self.handle))?) }
    }

    fn get_single(&self) -> Result<Option<CpuTexture<u16>>> {
        unsafe {
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
            Ok(Some(CpuTexture::new(
                data,
                (width as usize, height as usize),
            )))
        }
    }

    fn start_live(&self) -> Result<()> {
        unsafe { Ok(check(qhy::BeginQHYCCDLive(self.handle))?) }
    }

    fn stop_live(&self) -> Result<()> {
        unsafe { Ok(check(qhy::StopQHYCCDLive(self.handle))?) }
    }

    fn get_live(&self) -> Result<Option<CpuTexture<u16>>> {
        unsafe {
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
            Ok(Some(CpuTexture::new(
                data,
                (width as usize, height as usize),
            )))
        }
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

    pub fn try_get(&self) -> Result<Option<CpuTexture<u16>>> {
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
        check(unsafe { qhy::CloseQHYCCD(self.handle) }).expect("Failed to close QHY camera in Drop")
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
        unsafe {
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
    }

    pub fn name(&self) -> &'static str {
        self.control.to_str()
    }

    pub fn max_value(&self) -> f64 {
        self.max
    }
    pub fn min_value(&self) -> f64 {
        self.min
    }
    pub fn step_value(&self) -> f64 {
        self.step
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
        if self.readonly() {
            write!(f, "{} = {} (readonly)", self.control, self.get())
        } else {
            write!(
                f,
                "{} = {} ({}-{} by {})",
                self.control,
                self.get(),
                self.min_value(),
                self.max_value(),
                self.step_value(),
            )
        }
    }
}
