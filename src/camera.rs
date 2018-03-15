use asicamera;
use display;
use std::error::Error;
use std::ffi::CStr;
use std::fmt;
use std::mem;
use std::os::raw::{c_int, c_long, c_uchar};
use std::sync::mpsc;
use std::thread::sleep;
use std::time::Duration;

#[derive(Debug)]
struct AsiError {
    code: asicamera::ASI_ERROR_CODE,
}

impl fmt::Display for AsiError {
    fn fmt(&self, f: &mut fmt::Formatter) -> fmt::Result {
        let description = match self.code {
            asicamera::ASI_ERROR_CODE_ASI_SUCCESS => "success",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_INVALID_INDEX => "invalid index",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_INVALID_ID => " invalid id",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_INVALID_CONTROL_TYPE => "invalid control type",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_CAMERA_CLOSED => "camera closed",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_CAMERA_REMOVED => "camera removed",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_INVALID_PATH => "invalid path",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_INVALID_FILEFORMAT => "invalid fileformat",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_INVALID_SIZE => "invalid size",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_INVALID_IMGTYPE => "invalid imgtype",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_OUTOF_BOUNDARY => "outof boundary",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_TIMEOUT => "timeout",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_INVALID_SEQUENCE => "invalid sequence",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_BUFFER_TOO_SMALL => "buffer too small",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_VIDEO_MODE_ACTIVE => "video mode active",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_EXPOSURE_IN_PROGRESS => "exposure in progress",
            asicamera::ASI_ERROR_CODE_ASI_ERROR_GENERAL_ERROR => "general error",
            _ => "unknown error",
        };
        write!(f, "ASI error ({}): {}", self.code, description)
    }
}

impl Error for AsiError {
    fn description(&self) -> &str {
        "ASI error"
    }
}

impl From<asicamera::ASI_ERROR_CODE> for AsiError {
    fn from(code: asicamera::ASI_ERROR_CODE) -> AsiError {
        AsiError { code }
    }
}

fn check(code: c_int) -> Result<(), AsiError> {
    let code = code as asicamera::ASI_ERROR_CODE;
    if code == asicamera::ASI_ERROR_CODE_ASI_SUCCESS {
        Ok(())
    } else {
        Err(AsiError { code })
    }
}

pub struct CameraInfo {
    props: asicamera::ASI_CAMERA_INFO,
}

impl CameraInfo {
    pub fn new(id: u32) -> Result<CameraInfo, Box<Error>> {
        let result = unsafe {
            let mut props = mem::zeroed();
            check(asicamera::ASIGetCameraProperty(&mut props, id as c_int))?;
            CameraInfo { props }
        };
        Ok(result)
    }

    pub fn open(self) -> Result<Camera, Box<Error>> {
        Camera::open(self)
    }

    fn id(&self) -> c_int {
        self.props.CameraID
    }

    pub fn name(&self) -> String {
        unsafe { CStr::from_ptr(&self.props.Name as *const ::std::os::raw::c_char) }
            .to_string_lossy()
            .into_owned()
    }

    pub fn width(&self) -> u32 {
        self.props.MaxWidth as u32
    }

    pub fn height(&self) -> u32 {
        self.props.MaxHeight as u32
    }
}

pub struct Camera {
    info: CameraInfo,
    controls: Vec<Control>,
}

impl Camera {
    pub fn num_cameras() -> u32 {
        unsafe { asicamera::ASIGetNumOfConnectedCameras() as u32 }
    }

    fn open(info: CameraInfo) -> Result<Camera, Box<Error>> {
        let result = unsafe {
            check(asicamera::ASIOpenCamera(info.props.CameraID))?;
            check(asicamera::ASIInitCamera(info.props.CameraID))?;
            let controls = Self::get_controls(info.props.CameraID)?;
            Camera { info, controls }
        };
        result.set_16_bit()?;
        Ok(result)
    }

    fn id(&self) -> c_int {
        self.info.id()
    }

    pub fn name(&self) -> String {
        self.info.name()
    }

    pub fn width(&self) -> u32 {
        self.info.width()
    }

    pub fn height(&self) -> u32 {
        self.info.height()
    }

    fn get_controls(id: c_int) -> Result<Vec<Control>, Box<Error>> {
        let mut num_controls = 0;
        check(unsafe { asicamera::ASIGetNumOfControls(id, &mut num_controls) })?;
        let result = (0..num_controls)
            .map(|control| Control::new(id, control))
            .collect::<Result<Vec<_>, _>>();
        Ok(result?)
    }

    pub fn controls(&self) -> &[Control] {
        &self.controls
    }

    fn set_roi_format(
        &self,
        bin: i32,
        img_type: asicamera::ASI_IMG_TYPE,
    ) -> Result<(), Box<Error>> {
        Ok(check(unsafe {
            asicamera::ASISetROIFormat(
                self.id(),
                self.width() as c_int,
                self.height() as c_int,
                bin as c_int,
                img_type as c_int,
            )
        })?)
    }

    fn set_16_bit(&self) -> Result<(), Box<Error>> {
        self.set_roi_format(1, asicamera::ASI_IMG_TYPE_ASI_IMG_RAW16)
    }

    fn start_exposure(&self) -> Result<(), Box<Error>> {
        Ok(check(unsafe { asicamera::ASIStartExposure(self.id(), 0) })?)
    }

    fn stop_exposure(&self) -> Result<(), Box<Error>> {
        Ok(check(unsafe { asicamera::ASIStopExposure(self.id()) })?)
    }

    fn exposure_status(&self) -> Result<asicamera::ASI_EXPOSURE_STATUS, Box<Error>> {
        unsafe {
            let mut status = 0;
            check(asicamera::ASIGetExpStatus(self.id(), &mut status))?;
            Ok(status as asicamera::ASI_EXPOSURE_STATUS)
        }
    }

    fn exposure_data(&self) -> Result<Vec<u16>, Box<Error>> {
        let mut result = vec![0; self.width() as usize * self.height() as usize];
        unsafe {
            check(asicamera::ASIGetDataAfterExp(
                self.id(),
                &mut result[..] as *mut [u16] as *mut c_uchar,
                result.len() as c_long * 2,
            ))?;
            Ok(result)
        }
    }

    pub fn expose(&self) -> Result<Vec<u16>, Box<Error>> {
        if self.exposure_status()? == asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_WORKING {
            Err("Camera already exposing".to_owned())?
        }
        self.start_exposure()?;
        loop {
            match self.exposure_status()? {
                asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_IDLE => {
                    Err("Camera somehow idle during exposure".to_owned())?
                }
                asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_WORKING => (),
                asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_SUCCESS => break Ok(self.exposure_data()?),
                asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_FAILED => {
                    Err("Camera exposure failed".to_owned())?
                }
                _ => Err("Unknown camera exposure status".to_owned())?,
            }
            sleep(Duration::from_millis(1));
        }
    }

    fn start_video_capture(&self) -> Result<(), Box<Error>> {
        Ok(check(unsafe {
            asicamera::ASIStartVideoCapture(self.id())
        })?)
    }

    fn stop_video_capture(&self) -> Result<(), Box<Error>> {
        Ok(check(unsafe { asicamera::ASIStopVideoCapture(self.id()) })?)
    }

    fn exposure(&self) -> Result<i64, Box<Error>> {
        for control in &self.controls {
            if control.control_type() == asicamera::ASI_CONTROL_TYPE_ASI_EXPOSURE {
                return Ok(control.get()?.0);
            }
        }
        Err("Exposure control not found".to_owned())?
    }

    fn get_video_data(&self) -> Result<Vec<u16>, Box<Error>> {
        let exposure = self.exposure()?;
        let mut result = vec![0; self.width() as usize * self.height() as usize];
        check(unsafe {
            asicamera::ASIGetVideoData(
                self.id(),
                &mut result[..] as *mut [u16] as *mut c_uchar,
                result.len() as c_long * 2,
                (exposure / 1_000) as c_int * 2 + 500,
            )
        })?;
        Ok(result)
    }

    pub fn camera_loop(
        &self,
        sender: mpsc::Sender<display::Image>,
        adjust: fn(Vec<u16>, u32, u32) -> display::Image,
    ) -> Result<(), Box<Error>> {
        self.start_video_capture()?;
        loop {
            let data = self.get_video_data()?;
            let width = self.width();
            let height = self.height();
            let image = adjust(data, width, height);
            match sender.send(image) {
                Ok(()) => (),
                Err(mpsc::SendError(_)) => break,
            }
        }
        self.stop_video_capture()?;
        Ok(())
    }
}

impl Drop for Camera {
    fn drop(&mut self) {
        match check(unsafe { asicamera::ASICloseCamera(self.id()) }) {
            Ok(()) => (),
            Err(err) => eprintln!("Closing camera: {}", err),
        }
    }
}

pub struct Control {
    camera_id: c_int,
    control_id: c_int,
    caps: asicamera::ASI_CONTROL_CAPS,
}

impl Control {
    fn new(camera_id: c_int, control_id: c_int) -> Result<Control, Box<Error>> {
        unsafe {
            let mut caps = mem::zeroed();
            check(asicamera::ASIGetControlCaps(
                camera_id,
                control_id,
                &mut caps,
            ))?;
            Ok(Control {
                camera_id,
                control_id,
                caps,
            })
        }
    }

    pub fn name(&self) -> String {
        unsafe { CStr::from_ptr(&self.caps.Name as *const ::std::os::raw::c_char) }
            .to_string_lossy()
            .into_owned()
    }

    pub fn description(&self) -> String {
        unsafe { CStr::from_ptr(&self.caps.Description as *const ::std::os::raw::c_char) }
            .to_string_lossy()
            .into_owned()
    }

    pub fn max_value(&self) -> i64 {
        self.caps.MaxValue
    }
    pub fn min_value(&self) -> i64 {
        self.caps.MinValue
    }
    pub fn default_value(&self) -> i64 {
        self.caps.DefaultValue
    }
    pub fn is_auto_supported(&self) -> bool {
        self.caps.IsAutoSupported != asicamera::ASI_BOOL_ASI_FALSE
    }
    pub fn writable(&self) -> bool {
        self.caps.IsWritable != asicamera::ASI_BOOL_ASI_FALSE
    }
    pub fn control_type(&self) -> asicamera::ASI_CONTROL_TYPE {
        self.caps.ControlType
    }

    pub fn get(&self) -> Result<(i64, bool), Box<Error>> {
        let mut value = 0;
        let mut auto = 0;
        check(unsafe {
            asicamera::ASIGetControlValue(
                self.camera_id,
                self.caps.ControlType as c_int,
                &mut value,
                &mut auto,
            )
        })?;
        Ok((
            value as i64,
            auto as asicamera::ASI_BOOL != asicamera::ASI_BOOL_ASI_FALSE,
        ))
    }

    pub fn set(&self, value: i64, auto: bool) -> Result<(), Box<Error>> {
        Ok(check(unsafe {
            asicamera::ASISetControlValue(
                self.camera_id,
                self.caps.ControlType as c_int,
                value,
                auto as c_int,
            )
        })?)
    }
}

unsafe impl Send for Camera {}
unsafe impl Sync for Camera {}
