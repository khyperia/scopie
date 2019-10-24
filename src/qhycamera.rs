use std::ffi::c_void;
use std::fmt;

pub type QHYCCD = *mut c_void;

#[repr(C)]
#[derive(Clone, Copy)]
pub enum ControlId {
    ControlBrightness = 0, // image brightness
    ControlContrast,       // image contrast
    ControlWbr,            // red of white balance
    ControlWbb,            // blue of white balance
    ControlWbg,            // the green of white balance
    ControlGamma,          // screen gamma
    ControlGain,           // camera gain
    ControlOffset,         // camera offset
    ControlExposure,       // expose time (us)
    ControlSpeed,          // transfer speed
    ControlTransferbit,    // image depth bits
    ControlChannels,       // image channels
    ControlUsbtraffic,     // hblank
    ControlRownoisere,     // row denoise
    ControlCurtemp,        // current cmos or ccd temprature
    ControlCurpwm,         // current cool pwm
    ControlManulpwm,       // set the cool pwm
    ControlCfwport,        // control camera color filter wheel port
    ControlCooler,         // check if camera has cooler
    ControlSt4port,        // check if camera has st4port
    CamColor,
    CamBin1x1mode,                     // check if camera has bin1x1 mode
    CamBin2x2mode,                     // check if camera has bin2x2 mode
    CamBin3x3mode,                     // check if camera has bin3x3 mode
    CamBin4x4mode,                     // check if camera has bin4x4 mode
    CamMechanicalshutter,              // mechanical shutter
    CamTrigerInterface,                // triger
    CamTecoverprotectInterface,        // tec overprotect
    CamSingnalclampInterface,          // singnal clamp
    CamFinetoneInterface,              // fine tone
    CamShuttermotorheatingInterface,   // shutter motor heating
    CamCalibratefpnInterface,          // calibrated frame
    CamChiptemperaturesensorInterface, // chip temperaure sensor
    CamUsbreadoutslowestInterface,     // usb readout slowest

    Cam8bits,  // 8bit depth
    Cam16bits, // 16bit depth
    CamGps,    // check if camera has gps

    CamIgnoreoverscanInterface, // ignore overscan area

    Qhyccd3aAutobalance,
    Qhyccd3aAutoexposure,
    Qhyccd3aAutofocus,
    ControlAmpv, // ccd or cmos ampv
    ControlVcam, // virtual camera on off
    CamViewMode,

    ControlCfwslotsnum, // check cfw slots number
    IsExposingDone,
    ScreenStretchB,
    ScreenStretchW,
    ControlDdr,
    CamLightPerformanceMode,

    CamQhy5iiGuideMode,
    DdrBufferCapacity,
    DdrBufferReadThreshold,
    DefaultGain,
    DefaultOffset,
    OutputDataActualBits,
    OutputDataAlignment,
}

static VALUES: &[ControlId] = &[
    ControlId::ControlBrightness,
    ControlId::ControlContrast,
    ControlId::ControlWbr,
    ControlId::ControlWbb,
    ControlId::ControlWbg,
    ControlId::ControlGamma,
    ControlId::ControlGain,
    ControlId::ControlOffset,
    ControlId::ControlExposure,
    ControlId::ControlSpeed,
    ControlId::ControlTransferbit,
    ControlId::ControlChannels,
    ControlId::ControlUsbtraffic,
    ControlId::ControlRownoisere,
    ControlId::ControlCurtemp,
    ControlId::ControlCurpwm,
    ControlId::ControlManulpwm,
    ControlId::ControlCfwport,
    ControlId::ControlCooler,
    ControlId::ControlSt4port,
    ControlId::CamColor,
    ControlId::CamBin1x1mode,
    ControlId::CamBin2x2mode,
    ControlId::CamBin3x3mode,
    ControlId::CamBin4x4mode,
    ControlId::CamMechanicalshutter,
    ControlId::CamTrigerInterface,
    ControlId::CamTecoverprotectInterface,
    ControlId::CamSingnalclampInterface,
    ControlId::CamFinetoneInterface,
    ControlId::CamShuttermotorheatingInterface,
    ControlId::CamCalibratefpnInterface,
    ControlId::CamChiptemperaturesensorInterface,
    ControlId::CamUsbreadoutslowestInterface,
    ControlId::Cam8bits,
    ControlId::Cam16bits,
    ControlId::CamGps,
    ControlId::CamIgnoreoverscanInterface,
    ControlId::Qhyccd3aAutobalance,
    ControlId::Qhyccd3aAutoexposure,
    ControlId::Qhyccd3aAutofocus,
    ControlId::ControlAmpv,
    ControlId::ControlVcam,
    ControlId::CamViewMode,
    ControlId::ControlCfwslotsnum,
    ControlId::IsExposingDone,
    ControlId::ScreenStretchB,
    ControlId::ScreenStretchW,
    ControlId::ControlDdr,
    ControlId::CamLightPerformanceMode,
    ControlId::CamQhy5iiGuideMode,
    ControlId::DdrBufferCapacity,
    ControlId::DdrBufferReadThreshold,
    ControlId::DefaultGain,
    ControlId::DefaultOffset,
    ControlId::OutputDataActualBits,
    ControlId::OutputDataAlignment,
];

impl ControlId {
    pub fn values() -> &'static [ControlId] {
        VALUES
    }
}

impl fmt::Display for ControlId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            ControlId::ControlBrightness => write!(f, "ControlBrightness"),
            ControlId::ControlContrast => write!(f, "ControlContrast"),
            ControlId::ControlWbr => write!(f, "ControlWbr"),
            ControlId::ControlWbb => write!(f, "ControlWbb"),
            ControlId::ControlWbg => write!(f, "ControlWbg"),
            ControlId::ControlGamma => write!(f, "ControlGamma"),
            ControlId::ControlGain => write!(f, "ControlGain"),
            ControlId::ControlOffset => write!(f, "ControlOffset"),
            ControlId::ControlExposure => write!(f, "ControlExposure"),
            ControlId::ControlSpeed => write!(f, "ControlSpeed"),
            ControlId::ControlTransferbit => write!(f, "ControlTransferbit"),
            ControlId::ControlChannels => write!(f, "ControlChannels"),
            ControlId::ControlUsbtraffic => write!(f, "ControlUsbtraffic"),
            ControlId::ControlRownoisere => write!(f, "ControlRownoisere"),
            ControlId::ControlCurtemp => write!(f, "ControlCurtemp"),
            ControlId::ControlCurpwm => write!(f, "ControlCurpwm"),
            ControlId::ControlManulpwm => write!(f, "ControlManulpwm"),
            ControlId::ControlCfwport => write!(f, "ControlCfwport"),
            ControlId::ControlCooler => write!(f, "ControlCooler"),
            ControlId::ControlSt4port => write!(f, "ControlSt4port"),
            ControlId::CamColor => write!(f, "CamColor"),
            ControlId::CamBin1x1mode => write!(f, "CamBin1x1mode"),
            ControlId::CamBin2x2mode => write!(f, "CamBin2x2mode"),
            ControlId::CamBin3x3mode => write!(f, "CamBin3x3mode"),
            ControlId::CamBin4x4mode => write!(f, "CamBin4x4mode"),
            ControlId::CamMechanicalshutter => write!(f, "CamMechanicalshutter"),
            ControlId::CamTrigerInterface => write!(f, "CamTrigerInterface"),
            ControlId::CamTecoverprotectInterface => write!(f, "CamTecoverprotectInterface"),
            ControlId::CamSingnalclampInterface => write!(f, "CamSingnalclampInterface"),
            ControlId::CamFinetoneInterface => write!(f, "CamFinetoneInterface"),
            ControlId::CamShuttermotorheatingInterface => {
                write!(f, "CamShuttermotorheatingInterface")
            }
            ControlId::CamCalibratefpnInterface => write!(f, "CamCalibratefpnInterface"),
            ControlId::CamChiptemperaturesensorInterface => {
                write!(f, "CamChiptemperaturesensorInterface")
            }
            ControlId::CamUsbreadoutslowestInterface => write!(f, "CamUsbreadoutslowestInterface"),
            ControlId::Cam8bits => write!(f, "Cam8bits"),
            ControlId::Cam16bits => write!(f, "Cam16bits"),
            ControlId::CamGps => write!(f, "CamGps"),
            ControlId::CamIgnoreoverscanInterface => write!(f, "CamIgnoreoverscanInterface"),
            ControlId::Qhyccd3aAutobalance => write!(f, "Qhyccd3aAutobalance"),
            ControlId::Qhyccd3aAutoexposure => write!(f, "Qhyccd3aAutoexposure"),
            ControlId::Qhyccd3aAutofocus => write!(f, "Qhyccd3aAutofocus"),
            ControlId::ControlAmpv => write!(f, "ControlAmpv"),
            ControlId::ControlVcam => write!(f, "ControlVcam"),
            ControlId::CamViewMode => write!(f, "CamViewMode"),
            ControlId::ControlCfwslotsnum => write!(f, "ControlCfwslotsnum"),
            ControlId::IsExposingDone => write!(f, "IsExposingDone"),
            ControlId::ScreenStretchB => write!(f, "ScreenStretchB"),
            ControlId::ScreenStretchW => write!(f, "ScreenStretchW"),
            ControlId::ControlDdr => write!(f, "ControlDdr"),
            ControlId::CamLightPerformanceMode => write!(f, "CamLightPerformanceMode"),
            ControlId::CamQhy5iiGuideMode => write!(f, "CamQhy5iiGuideMode"),
            ControlId::DdrBufferCapacity => write!(f, "DdrBufferCapacity"),
            ControlId::DdrBufferReadThreshold => write!(f, "DdrBufferReadThreshold"),
            ControlId::DefaultGain => write!(f, "DefaultGain"),
            ControlId::DefaultOffset => write!(f, "DefaultOffset"),
            ControlId::OutputDataActualBits => write!(f, "OutputDataActualBits"),
            ControlId::OutputDataAlignment => write!(f, "OutputDataAlignment"),
        }
    }
}

#[link(name = "qhyccd")]
extern "C" {
    pub fn InitQHYCCDResource() -> u32;
    pub fn ReleaseQHYCCDResource() -> u32;
    pub fn ScanQHYCCD() -> u32;
    pub fn GetQHYCCDId(index: u32, id: *mut u8) -> u32;
    pub fn GetQHYCCDModel(id: *const u8, model: *mut u8) -> u32;
    pub fn OpenQHYCCD(id: *const u8) -> QHYCCD;
    pub fn CloseQHYCCD(handle: QHYCCD) -> u32;
    pub fn SetQHYCCDStreamMode(handle: QHYCCD, mode: u8) -> u32;
    pub fn InitQHYCCD(handle: QHYCCD) -> u32;
    pub fn GetQHYCCDChipInfo(
        handle: QHYCCD,
        chipw: &mut f64,
        chiph: &mut f64,
        imagew: &mut u32,
        imageh: &mut u32,
        pixelw: &mut f64,
        pixelh: &mut f64,
        bpp: &mut u32,
    ) -> u32;
    pub fn IsQHYCCDControlAvailable(handle: QHYCCD, control_id: ControlId) -> u32;
    pub fn SetQHYCCDParam(handle: QHYCCD, control_id: ControlId, value: f64) -> u32;
    pub fn GetQHYCCDParam(handle: QHYCCD, control_id: ControlId) -> f64;
    pub fn GetQHYCCDParamMinMaxStep(
        handle: QHYCCD,
        control_id: ControlId,
        min: *mut f64,
        max: *mut f64,
        step: *mut f64,
    ) -> u32;
    pub fn SetQHYCCDResolution(handle: QHYCCD, x: u32, y: u32, width: u32, height: u32) -> u32;
    pub fn GetQHYCCDMemLength(handle: QHYCCD) -> u32;
    pub fn ExpQHYCCDSingleFrame(handle: QHYCCD) -> u32;
    pub fn GetQHYCCDSingleFrame(
        handle: QHYCCD,
        width: *mut u32,
        height: *mut u32,
        bpp: *mut u32,
        channels: *mut u32,
        imgdata: *mut u8,
    ) -> u32;
    pub fn CancelQHYCCDExposing(handle: QHYCCD) -> u32;
    pub fn CancelQHYCCDExposingAndReadout(handle: QHYCCD) -> u32;
    pub fn BeginQHYCCDLive(handle: QHYCCD) -> u32;
    pub fn GetQHYCCDLiveFrame(
        handle: QHYCCD,
        width: *mut u32,
        height: *mut u32,
        bpp: *mut u32,
        channels: *mut u32,
        imgdata: *mut u8,
    ) -> u32;
    pub fn StopQHYCCDLive(handle: QHYCCD) -> u32;
    pub fn SetQHYCCDBinMode(handle: QHYCCD, wbin: u32, hbin: u32) -> u32;
    pub fn SetQHYCCDBitsMode(handle: QHYCCD, bits: u32) -> u32;
    pub fn ControlQHYCCDTemp(handle: QHYCCD, target_temp: f64) -> u32;
    pub fn ControlQHYCCDGuide(handle: QHYCCD, direction: u32, duration: u16) -> u32;
    pub fn GetQHYCCDExposureRemaining(handle: QHYCCD) -> u32;
    pub fn GetQHYCCDReadingProgress(handle: QHYCCD) -> f64;
    pub fn SetQHYCCDLogLevel(log_level: u8);
}
