use crate::{
    alg::Rect,
    camera::qhycamera::{self as qhy, ControlId, QHYCCD},
    Result,
};
use anyhow::anyhow;
use std::{
    error::Error,
    ffi::{CStr, CString},
    fmt, str,
    sync::Once,
};

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

fn check(code: u32) -> Result<()> {
    if code == 0 {
        Ok(())
    } else {
        Err(anyhow!(QhyError { code }))
    }
}

// fn check(code: u32) -> ::std::result::Result<(), QhyError> {
//     if code == 0 {
//         Ok(())
//     } else {
//         Err(QhyError { code })
//     }
// }

#[derive(Debug)]
pub struct CpuTexture<T> {
    data: Vec<T>,
    pub size: (usize, usize),
}
impl<T> CpuTexture<T> {
    pub fn new(data: Vec<T>, size: (usize, usize)) -> Self {
        assert!(data.len() >= size.0 * size.1);
        Self { data, size }
    }

    pub fn data(&self) -> &[T] {
        &self.data[..self.size.0 * self.size.1]
    }
}

#[derive(Debug)]
pub struct ROIImage {
    pub image: CpuTexture<u16>,
    // the ROI, i.e. bounds of image
    pub location: Rect<usize>,
    // the original sensor size
    pub original: Rect<usize>,
}

impl From<CpuTexture<u16>> for ROIImage {
    fn from(image: CpuTexture<u16>) -> ROIImage {
        let original = Rect::new(0, 0, image.size.0, image.size.1);
        ROIImage {
            image,
            location: original.clone(),
            original,
        }
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
        Err(anyhow!("No QHY cameras found"))
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
    effective_area: Rect<usize>,
    current_roi: Rect<usize>,
    qhyccd_mem_length_u16: usize,
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
                return Err(anyhow!("OpenQHYCCD returned null"));
            }

            check(qhy::SetQHYCCDStreamMode(
                handle,
                if use_live { 1 } else { 0 },
            ))?; // 0 == single, 1 == stream
            check(qhy::InitQHYCCD(handle))?;
            if qhy::IsQHYCCDControlAvailable(handle, ControlId::CamSensorUlvoStatus) == 0 {
                let ulvo_status = qhy::GetQHYCCDParam(handle, ControlId::CamSensorUlvoStatus);
                if ulvo_status != 2.0 && ulvo_status != 9.0 {
                    return Err(anyhow!(format!("camera ULVO status is {ulvo_status}")));
                }
            } else {
                println!("No ULVO status");
            }
            check(qhy::IsQHYCCDControlAvailable(
                handle,
                ControlId::ControlTransferbit,
            ))?;
            check(qhy::SetQHYCCDBitsMode(handle, 16))?;
            check(qhy::SetQHYCCDBinMode(handle, 1, 1))?;
            if use_live {
                check(qhy::IsQHYCCDControlAvailable(
                    handle,
                    ControlId::ControlUsbtraffic,
                ))?;
                check(qhy::SetQHYCCDParam(
                    handle,
                    ControlId::ControlUsbtraffic,
                    0.0,
                ))?;
            }
            if qhy::IsQHYCCDControlAvailable(handle, ControlId::ControlSpeed) == 0 {
                println!("ControlSpeed is available");
            } else {
                let mut num_modes = 0;
                qhy::GetQHYCCDNumberOfReadModes(handle, &mut num_modes);
                println!("Read Modes is available: {}", num_modes);
                for mode in 0..num_modes {
                    let mut name = [0u8; 128];
                    qhy::GetQHYCCDReadModeName(handle, mode, &mut name[0]);
                    println!(
                        "mode {} = {:?}",
                        mode,
                        CStr::from_ptr(&name[0] as *const u8 as *const i8).to_str()
                    );
                }
            }
            let mut x = 0;
            let mut y = 0;
            let mut width = 0;
            let mut height = 0;
            check(qhy::GetQHYCCDEffectiveArea(
                handle,
                &mut x,
                &mut y,
                &mut width,
                &mut height,
            ))?;
            let current_roi = Rect::new(x as usize, y as usize, width as usize, height as usize);

            let controls = Self::get_controls(handle);

            let len_u8 = qhy::GetQHYCCDMemLength(handle) as usize;
            let len_u16 = len_u8 / 2;

            Ok(Camera {
                handle,
                info,
                controls,
                use_live,
                effective_area: current_roi.clone(),
                current_roi,
                qhyccd_mem_length_u16: len_u16,
            })
        }
    }

    pub fn info(&self) -> &CameraInfo {
        &self.info
    }

    pub fn use_live(&self) -> bool {
        self.use_live
    }

    pub fn effective_area(&self) -> Rect<usize> {
        self.effective_area.clone()
    }

    pub fn current_roi(&self) -> Rect<usize> {
        self.current_roi.clone()
    }

    pub fn name(&self) -> &str {
        &self.info().name()
    }

    pub fn set_roi(&mut self, roi: Rect<usize>) -> Result<()> {
        self.current_roi = roi.clone();
        unsafe {
            Ok(check(qhy::SetQHYCCDResolution(
                self.handle,
                roi.x as u32,
                roi.y as u32,
                roi.width as u32,
                roi.height as u32,
            ))?)
        }
    }

    pub fn unset_roi(&mut self) -> Result<()> {
        self.current_roi = self.effective_area.clone();
        unsafe {
            Ok(check(qhy::SetQHYCCDResolution(
                self.handle,
                self.effective_area.x as u32,
                self.effective_area.y as u32,
                self.effective_area.width as u32,
                self.effective_area.height as u32,
            ))?)
        }
    }

    fn get_controls(handle: QHYCCD) -> Vec<Control> {
        const BANNED: [ControlId; 3] = [
            ControlId::ControlCfwport,
            ControlId::ControlCfwslotsnum,
            ControlId::ControlDdr,
        ];
        ControlId::values()
            .iter()
            .cloned()
            .filter(|&id| !BANNED.iter().any(|&x| id == x))
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

    pub fn get_single(&self) -> Result<ROIImage> {
        unsafe {
            // GetQHYCCDExposureRemaining seems to be unreliable, so just block
            let mut width = 0;
            let mut height = 0;
            let mut bpp = 0;
            let mut channels = 0;
            let mut data = vec![0; self.qhyccd_mem_length_u16];
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
            assert_eq!(width as usize, self.current_roi.width);
            assert_eq!(height as usize, self.current_roi.height);
            Ok(ROIImage {
                image: CpuTexture::new(data, (width as usize, height as usize)),
                location: self.current_roi.clone(),
                original: self.effective_area.clone(),
            })
        }
    }

    pub fn start_live(&self) -> Result<()> {
        unsafe { Ok(check(qhy::BeginQHYCCDLive(self.handle))?) }
    }

    pub fn stop_live(&self) -> Result<()> {
        unsafe { Ok(check(qhy::StopQHYCCDLive(self.handle))?) }
    }

    pub fn get_live(&self) -> Option<ROIImage> {
        unsafe {
            let mut width = 0;
            let mut height = 0;
            let mut bpp = 0;
            let mut channels = 0;
            let mut data = vec![0; self.qhyccd_mem_length_u16];
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
                assert_eq!(width as usize, self.current_roi.width);
                assert_eq!(height as usize, self.current_roi.height);
                Some(ROIImage {
                    image: CpuTexture::new(data, (width as usize, height as usize)),
                    location: self.current_roi.clone(),
                    original: self.effective_area.clone(),
                })
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
    constant_value: f64,
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
            let constant = ControlId::is_constant(control);
            let mut res = Self {
                handle,
                control,
                min,
                max,
                step,
                constant_value: std::f64::NAN,
                readonly,
                interesting,
            };
            if constant {
                res.constant_value = res.get();
            }
            res
        }
    }

    pub fn id(&self) -> ControlId {
        self.control
    }

    pub fn get(&self) -> f64 {
        if self.constant_value.is_finite() {
            return self.constant_value;
        }
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

#[derive(Clone, Debug)]
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
