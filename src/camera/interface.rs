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
    time::{Duration, Instant},
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
pub struct TimestampImage<T> {
    pub image: T,
    pub time: Instant,
    pub duration: Duration,
}

impl From<CpuTexture<u16>> for TimestampImage<CpuTexture<u16>> {
    fn from(image: CpuTexture<u16>) -> TimestampImage<CpuTexture<u16>> {
        TimestampImage::<CpuTexture<u16>> {
            image,
            time: Instant::now(),
            duration: Duration::ZERO,
        }
    }
}

static INIT_QHYCCD_RESOURCE: Once = Once::new();

fn init_qhyccd_resource() {
    INIT_QHYCCD_RESOURCE.call_once(|| unsafe {
        check(qhy::InitQHYCCDResource()).expect("Failed to init QHY resources");
    })
}

pub fn autoconnect() -> Result<Camera> {
    init_qhyccd_resource();
    let mut best = None;
    for index in 0..Camera::num_cameras() {
        let camera_id = Camera::camera_id_from_index(index)?;
        let is163 = camera_id.contains("163");
        if best.is_none() || is163 {
            best = Some(camera_id);
            if is163 {
                break;
            }
        }
    }
    if let Some(camera_id) = best {
        let live = !camera_id.contains("163");
        Camera::open(camera_id, live)
    } else {
        Err(anyhow!("No QHY cameras found"))
    }
}

pub struct Camera {
    handle: QHYCCD,
    camera_id: String,
    controls: Vec<Control>,
    use_live: bool,
    is_live_running: Option<Instant>,
    effective_area: Rect<usize>,
    current_roi: Rect<usize>,
    qhyccd_mem_length_u16: usize,
}

impl Camera {
    pub fn num_cameras() -> u32 {
        init_qhyccd_resource();
        unsafe { qhy::ScanQHYCCD() }
    }

    pub fn camera_id_from_index(index: u32) -> Result<String> {
        init_qhyccd_resource();
        let result = unsafe {
            let mut data = vec![0; 512];
            check(qhy::GetQHYCCDId(index, data.as_mut_ptr()))?;
            CStr::from_ptr(&data[0] as *const u8 as *const i8)
                .to_string_lossy()
                .to_string()
        };
        Ok(result)
    }

    pub fn open(camera_id: String, use_live: bool) -> Result<Camera> {
        unsafe {
            let cstring = CString::new(&camera_id as &str)?;
            let handle = qhy::OpenQHYCCD(cstring.as_ptr());
            if handle.is_null() {
                return Err(anyhow!("OpenQHYCCD returned null"));
            }

            let mut year = 0;
            let mut month = 0;
            let mut day = 0;
            let mut subday = 0;
            check(qhy::GetQHYCCDSDKVersion(
                &mut year,
                &mut month,
                &mut day,
                &mut subday,
            ))?;
            println!("SDK version: {year}-{month}-{day}-{subday}");

            let mut fwv = [0; 32];
            check(qhy::GetQHYCCDFWVersion(handle, &mut fwv[0]))?;
            if (fwv[0] >> 4) <= 9 {
                println!(
                    "FW version (winusb): {}-{}-{}",
                    (fwv[0] >> 4) + 0x10,
                    fwv[0] & !0xf0,
                    fwv[1]
                );
            } else {
                println!(
                    "FW version (cyusb): {}-{}-{}",
                    fwv[0] >> 4,
                    fwv[0] & !0xf0,
                    fwv[1]
                );
            }
            for fpga_index in 0..=3 {
                let mut ver = [0; 32];
                let ok = qhy::GetQHYCCDFPGAVersion(handle, fpga_index, &mut ver[0]) as i32;
                println!(
                    "FPGA version ({fpga_index} ret={ok}): {}-{}-{}-{}",
                    ver[0], ver[1], ver[2], ver[3]
                );
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
                camera_id,
                controls,
                use_live,
                is_live_running: None,
                effective_area: current_roi.clone(),
                current_roi,
                qhyccd_mem_length_u16: len_u16,
            })
        }
    }

    pub fn camera_id(&self) -> &str {
        &self.camera_id
    }

    pub fn use_live(&self) -> bool {
        self.use_live
    }

    pub fn current_roi(&self) -> Rect<usize> {
        self.current_roi.clone()
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

    pub fn exp_single(&self) -> Result<TimestampImage<CpuTexture<u16>>> {
        let start = Instant::now();
        let single = unsafe { qhy::ExpQHYCCDSingleFrame(self.handle) };
        // QHYCCD_READ_DIRECTLY
        if single != 0x2001 {
            check(single)?;
        }

        // GetQHYCCDExposureRemaining seems to be unreliable, so just block
        let mut width = 0;
        let mut height = 0;
        let mut bpp = 0;
        let mut channels = 0;
        let mut data = vec![0; self.qhyccd_mem_length_u16];
        unsafe {
            check(qhy::GetQHYCCDSingleFrame(
                self.handle,
                &mut width,
                &mut height,
                &mut bpp,
                &mut channels,
                data.as_mut_ptr() as _,
            ))?;
        }
        assert_eq!(bpp, 16);
        assert_eq!(channels, 1);
        assert_eq!(width as usize, self.current_roi.width);
        assert_eq!(height as usize, self.current_roi.height);
        let end = Instant::now();
        let image = TimestampImage::<CpuTexture<u16>> {
            image: CpuTexture::new(data, (width as usize, height as usize)),
            time: end,
            duration: end - start,
        };
        // unsafe {
        //     check(qhy::CancelQHYCCDExposingAndReadout(self.handle))?;
        // }
        Ok(image)
    }

    pub fn try_start_live(&mut self) -> Result<()> {
        if self.is_live_running.is_none() {
            unsafe { check(qhy::BeginQHYCCDLive(self.handle))? }
            self.is_live_running = Some(Instant::now());
        }
        Ok(())
    }

    pub fn try_stop_live(&mut self) -> Result<()> {
        if self.is_live_running.is_some() {
            unsafe { check(qhy::StopQHYCCDLive(self.handle))? }
            self.is_live_running = None;
        }
        Ok(())
    }

    pub fn get_live(&mut self) -> Option<TimestampImage<CpuTexture<u16>>> {
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
                let now = Instant::now();
                let duration = self.is_live_running.map_or(Duration::ZERO, |o| now - o);
                self.is_live_running = Some(now);
                Some(TimestampImage {
                    image: CpuTexture::new(data, (width as usize, height as usize)),
                    time: now,
                    duration,
                })
            }
        }
    }

    pub fn dispose(&mut self) -> Result<()> {
        if self.handle != std::ptr::null_mut() {
            let res = check(unsafe { qhy::CloseQHYCCD(self.handle) });
            self.handle = std::ptr::null_mut();
            return res;
        }
        Ok(())
    }
}

impl Drop for Camera {
    fn drop(&mut self) {
        self.dispose().expect("Failed to close QHY camera in Drop")
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
