use asicamera;

use std::error::Error;
use std::fmt;
use std::mem;
use std::os::raw::{c_int, c_long, c_uchar};
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

struct Camera {
	id: c_int,
	props: asicamera::ASI_CAMERA_INFO,
}

impl Camera {
	fn num_cameras() -> u32 {
		unsafe { asicamera::ASIGetNumOfConnectedCameras() as u32 }
	}

	fn new(id: u32) -> Result<Camera, Box<Error>> {
		let result = unsafe {
			let mut props = mem::zeroed();
			check(asicamera::ASIGetCameraProperty(&mut props, id as c_int))?;
			check(asicamera::ASIOpenCamera(id as c_int))?;
			check(asicamera::ASIInitCamera(id as c_int))?;
			Camera {
				id: id as c_int,
				props,
			}
		};
		result.set_16_bit();
		Ok(result)
	}

	fn get_controls(id: c_int) -> Result<Vec<Control>, Box<Error>> {
		let mut num_controls = 0;
		check(unsafe { asicamera::ASIGetNumOfControls(id, &mut num_controls) })?;
		let result = (0..num_controls)
			.map(|control| Control::new(id, control))
			.collect::<Result<Vec<_>, _>>();
		Ok(result?)
	}

	fn set_roi_format(&self, bin: i32, img_type: asicamera::ASI_IMG_TYPE) -> Result<(), Box<Error>> {
		Ok(check(unsafe {
			asicamera::ASISetROIFormat(
				self.id,
				self.props.MaxWidth as c_int,
				self.props.MaxHeight as c_int,
				bin as c_int,
				img_type as c_int,
			)
		})?)
	}

	fn set_16_bit(&self) -> Result<(), Box<Error>> {
		self.set_roi_format(1, asicamera::ASI_IMG_TYPE_ASI_IMG_RAW16)
	}

	fn start_exposure(&self) -> Result<(), Box<Error>> {
		Ok(check(unsafe { asicamera::ASIStartExposure(self.id, 0) })?)
	}

	fn stop_exposure(&self) -> Result<(), Box<Error>> {
		Ok(check(unsafe { asicamera::ASIStopExposure(self.id) })?)
	}

	fn exposure_status(&self) -> Result<asicamera::ASI_EXPOSURE_STATUS, Box<Error>> {
		unsafe {
			let mut status = 0;
			check(asicamera::ASIGetExpStatus(self.id, &mut status))?;
			Ok(status as asicamera::ASI_EXPOSURE_STATUS)
		}
	}

	fn exposure_data(&self) -> Result<Vec<u16>, Box<Error>> {
		let mut result = vec![0; self.props.MaxWidth as usize * self.props.MaxHeight as usize];
		unsafe {
			check(asicamera::ASIGetDataAfterExp(self.id, &mut result[..] as *mut [u16] as *mut c_uchar, result.len() as c_long * 2))?;
			Ok(result)
		}
	}

	fn expose(&self) -> Result<Vec<u16>, Box<Error>> {
		if self.exposure_status()? == asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_WORKING {
			Err("Camera already exposing".to_owned())?
		}
		self.start_exposure()?;
		loop {
			match self.exposure_status()? {
				asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_IDLE => Err("Camera somehow idle during exposure".to_owned())?,
				asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_WORKING => (),
				asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_SUCCESS => break,
				asicamera::ASI_EXPOSURE_STATUS_ASI_EXP_FAILED => Err("Camera exposure failed".to_owned())?,
				_ => Err("Unknown camera exposure status".to_owned())?,
			}
			sleep(Duration::from_millis(1));
		}
		Ok(self.exposure_data()?)
	}
}

impl Drop for Camera {
	fn drop(&mut self) {
		match check(unsafe { asicamera::ASICloseCamera(self.id as c_int) }) {
			Ok(()) => (),
			Err(err) => eprintln!("Closing camera: {}", err),
		}
	}
}

struct Control {
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

	fn get(self) -> Result<(i64, bool), Box<Error>> {
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

	fn set(self, value: i64, auto: bool) -> Result<(), Box<Error>> {
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
