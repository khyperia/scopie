use crate::{
    qhycamera as qhy,
    qhycamera::{ControlId, QHYCCD},
    Result,
};
use khygl::texture::CpuTexture;
use std::{error::Error, ffi::CString, fmt, str, sync::Once};

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

#[derive(Clone)]
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
        init_qhyccd_resource();
        unsafe { qhy::ScanQHYCCD() }
    }

    fn open(info: CameraInfo, use_live: bool) -> Result<Camera> {
        unsafe {
            let cstring = CString::new(&info.name as &str)?;
            let handle = qhy::OpenQHYCCD(cstring.as_ptr());
            if handle.is_null() {
                return Err(failure::err_msg("OpenQHYCCD returned null"));
            }

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

    pub fn use_live(&self) -> bool {
        self.use_live
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

    pub fn start_single(&self) -> Result<()> {
        let single = unsafe { qhy::ExpQHYCCDSingleFrame(self.handle) };
        // QHYCCD_READ_DIRECTLY
        if single != 0x2001 {
            check(single)?;
        }
        Ok(())
    }

    pub fn stop_single(&self) -> Result<()> {
        unsafe { Ok(check(qhy::CancelQHYCCDExposingAndReadout(self.handle))?) }
    }

    pub fn get_single(&self) -> Result<CpuTexture<u16>> {
        unsafe {
            // GetQHYCCDExposureRemaining seems to be unreliable, so just block
            let len_u8 = qhy::GetQHYCCDMemLength(self.handle) as usize;
            let len_u16 = len_u8 / 2;
            let mut width = 0;
            let mut height = 0;
            let mut bpp = 0;
            let mut channels = 0;
            let mut data = vec![0; len_u16];
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
            Ok(CpuTexture::new(data, (width as usize, height as usize)))
        }
    }

    pub fn start_live(&self) -> Result<()> {
        unsafe { Ok(check(qhy::BeginQHYCCDLive(self.handle))?) }
    }

    pub fn stop_live(&self) -> Result<()> {
        unsafe { Ok(check(qhy::StopQHYCCDLive(self.handle))?) }
    }

    pub fn get_live(&self) -> Option<CpuTexture<u16>> {
        unsafe {
            let len_u8 = qhy::GetQHYCCDMemLength(self.handle) as usize;
            let len_u16 = len_u8 / 2;
            let mut width = 0;
            let mut height = 0;
            let mut bpp = 0;
            let mut channels = 0;
            let mut data = vec![0; len_u16];
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
                None
            } else {
                assert_eq!(bpp, 16);
                assert_eq!(channels, 1);
                Some(CpuTexture::new(data, (width as usize, height as usize)))
            }
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

    // pub fn try_get(&self) -> Result<Option<CpuTexture<u16>>> {
    //     if self.use_live {
    //         Ok(self.get_live())
    //     } else {
    //         let res = self.get_single()?;
    //         self.start_single()?;
    //         Ok(Some(res))
    //     }
    // }
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
    interesting: bool,
}

impl Control {
    fn new(handle: QHYCCD, control: ControlId) -> Control {
        unsafe {
            let mut min = 0.0;
            let mut max = 0.0;
            let mut step = 0.0;
            let res = qhy::GetQHYCCDParamMinMaxStep(handle, control, &mut min, &mut max, &mut step);
            let readonly = res != 0;
            let interesting = ControlId::is_interesting(control);
            Self {
                handle,
                control,
                min,
                max,
                step,
                readonly,
                interesting,
            }
        }
    }

    pub fn id(&self) -> ControlId {
        self.control
    }

    // pub fn max_value(&self) -> f64 {
    //     self.max
    // }
    // pub fn min_value(&self) -> f64 {
    //     self.min
    // }
    // pub fn step_value(&self) -> f64 {
    //     self.step
    // }
    // pub fn readonly(&self) -> bool {
    //     self.readonly
    // }
    // pub fn interesting(&self) -> bool {
    //     self.interesting
    // }

    pub fn get(&self) -> f64 {
        unsafe { qhy::GetQHYCCDParam(self.handle, self.control) }
    }

    pub fn set(&self, value: f64) -> Result<()> {
        check(unsafe { qhy::SetQHYCCDParam(self.handle, self.control, value) })?;
        Ok(())
    }

    pub fn to_value(&self) -> ControlValue {
        ControlValue::new(self)
    }
}

// impl fmt::Display for Control {
//     fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
//         if self.readonly() {
//             write!(f, "{} = {} (readonly)", self.control, self.get())
//         } else {
//             write!(
//                 f,
//                 "{} = {} ({}-{} by {})",
//                 self.control,
//                 self.get(),
//                 self.min_value(),
//                 self.max_value(),
//                 self.step_value(),
//             )
//         }
//     }
// }

#[derive(Clone)]
pub struct ControlValue {
    pub id: ControlId,
    pub value: f64,
    pub min: f64,
    pub max: f64,
    pub step: f64,
    pub readonly: bool,
    pub interesting: bool,
}

impl ControlValue {
    fn new(control: &Control) -> Self {
        Self {
            id: control.control,
            value: control.get(),
            min: control.min,
            max: control.max,
            step: control.step,
            readonly: control.readonly,
            interesting: control.interesting,
        }
    }

    pub fn name(&self) -> &'static str {
        self.id.to_str()
    }
}

impl fmt::Display for ControlValue {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        #[allow(clippy::float_cmp)]
        let value = if self.value == f64::from(u32::max_value()) {
            -1.0
        } else {
            self.value
        };
        if self.readonly {
            write!(f, "{} = {} (readonly)", self.id, value)
        } else {
            write!(
                f,
                "{} = {} ({}-{} by {})",
                self.id, value, self.min, self.max, self.step,
            )
        }
    }
}
