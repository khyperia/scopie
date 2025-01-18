use std::{ffi::c_void, fmt, os::raw::c_char, str::FromStr};

pub type QHYCCD = *mut c_void;

#[repr(u32)]
#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
pub enum ControlId {
    /*0*/ ControlBrightness = 0, // image brightness
    /*1*/ ControlContrast, // image contrast
    /*2*/ ControlWbr, // the red of white balance
    /*3*/ ControlWbb, // the blue of white balance
    /*4*/ ControlWbg, // the green of white balance
    /*5*/ ControlGamma, // screen gamma
    /*6*/ ControlGain, // camera gain
    /*7*/ ControlOffset, // camera offset
    /*8*/ ControlExposure, // expose time (us)
    /*9*/ ControlSpeed, // transfer speed
    /*10*/ ControlTransferbit, // image depth bits
    /*11*/ ControlChannels, // image channels
    /*12*/ ControlUsbtraffic, // hblank
    /*13*/ ControlRownoisere, // row denoise
    /*14*/ ControlCurtemp, // current cmos or ccd temprature
    /*15*/ ControlCurpwm, // current cool pwm
    /*16*/ ControlManulpwm, // set the cool pwm
    /*17*/ ControlCfwport, // control camera color filter wheel port
    /*18*/ ControlCooler, // check if camera has cooler
    /*19*/ ControlSt4port, // check if camera has st4port
    /*20*/ CamColor,
    /*21*/ CamBin1x1mode, // check if camera has bin1x1 mode
    /*22*/ CamBin2x2mode, // check if camera has bin2x2 mode
    /*23*/ CamBin3x3mode, // check if camera has bin3x3 mode
    /*24*/ CamBin4x4mode, // check if camera has bin4x4 mode
    /*25*/ CamMechanicalshutter, // mechanical shutter
    /*26*/ CamTrigerInterface, // check if camera has triger interface
    /*27*/ CamTecoverprotectInterface, // tec overprotect
    /*28*/ CamSingnalclampInterface, // singnal clamp
    /*29*/ CamFinetoneInterface, // fine tone
    /*30*/ CamShuttermotorheatingInterface, // shutter motor heating
    /*31*/ CamCalibratefpnInterface, // calibrated frame
    /*32*/ CamChipTemperatureSensorInterface, // chip temperaure sensor
    /*33*/ CamUsbReadoutSlowestInterface, // usb readout slowest

    /*34*/ Cam8bits, // 8bit depth
    /*35*/ Cam16bits, // 16bit depth
    /*36*/ CamGps, // check if camera has gps

    /*37*/ CamIgnoreOverscanInterface, // ignore overscan area

    /*38*/  //QHYCCD_3A_AUTOBALANCE,					 // auto white balance//lyl move to 1024
    /*39*/
    Qhyccd3aAutoexposure = 39, // auto exposure
    /*40*/ Qhyccd3aAutofocus,
    /*41*/ ControlAmpv, // ccd or cmos ampv
    /*42*/ ControlVcam, // Virtual Camera on off
    /*43*/ CamViewMode,

    /*44*/ ControlCfwslotsnum, // check CFW slots number
    /*45*/ IsExposingDone,
    /*46*/ ScreenStretchB,
    /*47*/ ScreenStretchW,
    /*48*/ ControlDdr,
    /*49*/ CamLightPerformanceMode,

    /*50*/ CamQhy5iiGuideMode,
    /*51*/ DdrBufferCapacity,
    /*52*/ DdrBufferReadThreshold,
    /*53*/ DefaultGain,
    /*54*/ DefaultOffset,
    /*55*/ OutputDataActualBits,
    /*56*/ OutputDataAlignment,

    /*57*/ CamSingleframemode,
    /*58*/ CamLivevideomode,
    /*59*/ CamIsColor,
    /*60*/ HasHardwareFrameCounter,
    /*61*/ ControlMaxIdError, // No Use, last max index
    /*62*/
    CamHumidity, // check if camera has humidity sensor 20191021 LYL Unified humidity function
    /*63*/ CamPressure, //check if camera has pressure sensor
    /*64*/ ControlVacuumPump,
    // if camera has VACUUM PUMP
    /*65*/
    ControlSensorChamberCyclePump,
    // air cycle pump for sensor drying
    /*66*/
    Cam32bits,
    /*67*/ CamSensorUlvoStatus,
    // Sensor working status [0:init  1:good  2:checkErr  3:monitorErr 8:good 9:powerChipErr]  410 461 411 600 268 [Eris board]
    /*68*/
    CamSensorPhaseReTrain,
    /// 2020,4040/PRO，6060,42PRO
    /*69*/
    CamInitConfigFromFlash,
    /// 2410 461 411 600 268 for now
    /*70*/
    CamTrigerMode, //check if camera has multiple triger mode
    /*71*/ CamTrigerOut, //check if camera support triger out function
    /*72*/ CamBurstMode, //check if camera support burst mode
    /*73*/ CamSpeakerLedAlarm, // for OEM-600
    /*74*/
    CamWatchDogFpga, // for _QHY5III178C Celestron, SDK have to feed this dog or it go reset

    /*75*/ CamBin6x6mode, // check if camera has bin6x6 mode
    /*76*/ CamBin8x8mode, // check if camera has bin8x8 mode
    /*77*/ CamGlobalSensorGpsled,
    ///Show GPS LED tab on sharpCap
    /*78*/
    ControlImgProc,
    /// Process image
    /*79*/
    ControlRemoveRbi,
    /// RBI, Remove single residual image
    /*80*/
    ControlGlobalReset, //image stabilization
    /*81*/ ControlFrameDetect,
    /*82*/ CamGainDbconversion, //Supports the conversion between db and gain
    /*83*/ CamCurveSystemGain, //
    /*84*/ CamCurveFullWell,
    /*85*/ CamCurveReadoutNoise,
    /*86*/ CamUseAverageBinning,
    /*87*/ ControlOutsidePumpV2, // air pump outside

    /*88*/ ControlAutoexposure, //auto exposure
    /*89*/ ControlAutoexptargetBrightness, //auto exposure Target Brightness
    /*90*/ ControlAutoexpsampleArea, //auto exposure Sample Area
    /*91*/ ControlAutoexpexpMaxMs, //auto exposure max exp(ms)
    /*92*/ ControlAutoexpgainMax, //auto exposure max gain

    /* Do not Put Item after  CONTROL_MAX_ID !! This should be the max index of the list */
    /*Last One */
    ControlMaxId,

    //TEST id name list
    /*1024*/
    ControlAutowhitebalance = 1024, //auto white balance  eg.CONTROL_TEST=1024
    ///*1025*/ CONTROL_AUTOEXPOSURE,			//auto exposure
    ///*1026*/ CONTROL_AUTOEXPTargetBrightness,//CONTROL_AUTOEXPmessureValue,
    ///*1027*/ CONTROL_AUTOEXPSampleArea,//CONTROL_AUTOEXPmessureMethod,
    ///*1028*/ CONTROL_AUTOEXPexpMaxMS,       //auto exposure max exp(ms)
    ///*1029*/ CONTROL_AUTOEXPgainMax,        //auto exposure max gain
    /*1030*/
    ControlImageStabilization, //image stabilization
    /*1031*/ ControlGaindB, //uesed to test dBGain control  //CONTROL_dB_TO_GAIN
    /*1032*/ ControlDpc, //Turn on or off the image DPC function(Remove thermal noise)
    /*1033*/ ControlDpcValue, //value the image DPC function(Remove thermal noise)
    /*1034*/
    ControlHdr, //HDR For cameras with high gain and low gain channels combined into 16 bits, set combination parameters>
    //HDR status  0:As-is output  1:Splice according to k and b values  2:Calculate k and b, only once
    /*1035*/ //CONTROL_HDR_H_k,               //HDR H k
    /*1036*/ //CONTROL_HDR_H_b,               //HDR H b
    /*1035*/
    ControlHdrLK, //HDR L k
    /*1036*/ ControlHdrLB, //HDR L b
    /*1037*/ ControlHdrX, //,                //HDR X
    /*1038*/ ControlHdrShowKb, //show HDR kb
                      //CONTROL_SHOWIMG
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
    ControlId::CamChipTemperatureSensorInterface,
    ControlId::CamUsbReadoutSlowestInterface,
    ControlId::Cam8bits,
    ControlId::Cam16bits,
    ControlId::CamGps,
    ControlId::CamIgnoreOverscanInterface,
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
    ControlId::CamSingleframemode,
    ControlId::CamLivevideomode,
    ControlId::CamIsColor,
    ControlId::HasHardwareFrameCounter,
    ControlId::ControlMaxIdError,
    ControlId::CamHumidity,
    ControlId::CamPressure,
    ControlId::ControlVacuumPump,
    ControlId::ControlSensorChamberCyclePump,
    ControlId::Cam32bits,
    ControlId::CamSensorUlvoStatus,
    ControlId::CamSensorPhaseReTrain,
    ControlId::CamInitConfigFromFlash,
    ControlId::CamTrigerMode,
    ControlId::CamTrigerOut,
    ControlId::CamBurstMode,
    ControlId::CamSpeakerLedAlarm,
    ControlId::CamWatchDogFpga,
    ControlId::CamBin6x6mode,
    ControlId::CamBin8x8mode,
    ControlId::CamGlobalSensorGpsled,
    ControlId::ControlImgProc,
    ControlId::ControlRemoveRbi,
    ControlId::ControlGlobalReset,
    ControlId::ControlFrameDetect,
    ControlId::CamGainDbconversion,
    ControlId::CamCurveSystemGain,
    ControlId::CamCurveFullWell,
    ControlId::CamCurveReadoutNoise,
    ControlId::CamUseAverageBinning,
    ControlId::ControlOutsidePumpV2,
    ControlId::ControlAutoexposure,
    ControlId::ControlAutoexptargetBrightness,
    ControlId::ControlAutoexpsampleArea,
    ControlId::ControlAutoexpexpMaxMs,
    ControlId::ControlAutoexpgainMax,
    ControlId::ControlMaxId,
    ControlId::ControlAutowhitebalance,
    ControlId::ControlImageStabilization,
    ControlId::ControlGaindB,
    ControlId::ControlDpc,
    ControlId::ControlDpcValue,
    ControlId::ControlHdr,
    ControlId::ControlHdrLK,
    ControlId::ControlHdrLB,
    ControlId::ControlHdrX,
    ControlId::ControlHdrShowKb,
];

static INTERESTING_VALUES: &[ControlId] = &[
    ControlId::ControlBrightness,
    ControlId::ControlContrast,
    ControlId::ControlGain,
    ControlId::ControlOffset,
    ControlId::ControlExposure,
    ControlId::ControlSpeed,
    ControlId::ControlTransferbit,
    ControlId::ControlUsbtraffic,
    ControlId::ControlRownoisere,
    ControlId::ControlCurtemp,
    ControlId::ControlCurpwm,
    ControlId::ControlManulpwm,
    ControlId::ControlCooler,
];

impl ControlId {
    pub fn is_interesting(id: ControlId) -> bool {
        INTERESTING_VALUES.iter().any(|&x| x == id)
    }

    pub fn values() -> &'static [ControlId] {
        VALUES
    }

    pub fn to_str(self) -> &'static str {
        match self {
            ControlId::ControlBrightness => "ControlBrightness",
            ControlId::ControlContrast => "ControlContrast",
            ControlId::ControlWbr => "ControlWbr",
            ControlId::ControlWbb => "ControlWbb",
            ControlId::ControlWbg => "ControlWbg",
            ControlId::ControlGamma => "ControlGamma",
            ControlId::ControlGain => "ControlGain",
            ControlId::ControlOffset => "ControlOffset",
            ControlId::ControlExposure => "ControlExposure",
            ControlId::ControlSpeed => "ControlSpeed",
            ControlId::ControlTransferbit => "ControlTransferbit",
            ControlId::ControlChannels => "ControlChannels",
            ControlId::ControlUsbtraffic => "ControlUsbtraffic",
            ControlId::ControlRownoisere => "ControlRownoisere",
            ControlId::ControlCurtemp => "ControlCurtemp",
            ControlId::ControlCurpwm => "ControlCurpwm",
            ControlId::ControlManulpwm => "ControlManulpwm",
            ControlId::ControlCfwport => "ControlCfwport",
            ControlId::ControlCooler => "ControlCooler",
            ControlId::ControlSt4port => "ControlSt4port",
            ControlId::CamColor => "CamColor",
            ControlId::CamBin1x1mode => "CamBin1x1mode",
            ControlId::CamBin2x2mode => "CamBin2x2mode",
            ControlId::CamBin3x3mode => "CamBin3x3mode",
            ControlId::CamBin4x4mode => "CamBin4x4mode",
            ControlId::CamMechanicalshutter => "CamMechanicalshutter",
            ControlId::CamTrigerInterface => "CamTrigerInterface",
            ControlId::CamTecoverprotectInterface => "CamTecoverprotectInterface",
            ControlId::CamSingnalclampInterface => "CamSingnalclampInterface",
            ControlId::CamFinetoneInterface => "CamFinetoneInterface",
            ControlId::CamShuttermotorheatingInterface => "CamShuttermotorheatingInterface",
            ControlId::CamCalibratefpnInterface => "CamCalibratefpnInterface",
            ControlId::CamChipTemperatureSensorInterface => "CamChipTemperatureSensorInterface",
            ControlId::CamUsbReadoutSlowestInterface => "CamUsbReadoutSlowestInterface",
            ControlId::Cam8bits => "Cam8bits",
            ControlId::Cam16bits => "Cam16bits",
            ControlId::CamGps => "CamGps",
            ControlId::CamIgnoreOverscanInterface => "CamIgnoreOverscanInterface",
            ControlId::Qhyccd3aAutoexposure => "Qhyccd3aAutoexposure",
            ControlId::Qhyccd3aAutofocus => "Qhyccd3aAutofocus",
            ControlId::ControlAmpv => "ControlAmpv",
            ControlId::ControlVcam => "ControlVcam",
            ControlId::CamViewMode => "CamViewMode",
            ControlId::ControlCfwslotsnum => "ControlCfwslotsnum",
            ControlId::IsExposingDone => "IsExposingDone",
            ControlId::ScreenStretchB => "ScreenStretchB",
            ControlId::ScreenStretchW => "ScreenStretchW",
            ControlId::ControlDdr => "ControlDdr",
            ControlId::CamLightPerformanceMode => "CamLightPerformanceMode",
            ControlId::CamQhy5iiGuideMode => "CamQhy5iiGuideMode",
            ControlId::DdrBufferCapacity => "DdrBufferCapacity",
            ControlId::DdrBufferReadThreshold => "DdrBufferReadThreshold",
            ControlId::DefaultGain => "DefaultGain",
            ControlId::DefaultOffset => "DefaultOffset",
            ControlId::OutputDataActualBits => "OutputDataActualBits",
            ControlId::OutputDataAlignment => "OutputDataAlignment",
            ControlId::CamSingleframemode => "CamSingleframemode",
            ControlId::CamLivevideomode => "CamLivevideomode",
            ControlId::CamIsColor => "CamIsColor",
            ControlId::HasHardwareFrameCounter => "HasHardwareFrameCounter",
            ControlId::ControlMaxIdError => "ControlMaxIdError",
            ControlId::CamHumidity => "CamHumidity",
            ControlId::CamPressure => "CamPressure",
            ControlId::ControlVacuumPump => "ControlVacuumPump",
            ControlId::ControlSensorChamberCyclePump => "ControlSensorChamberCyclePump",
            ControlId::Cam32bits => "Cam32bits",
            ControlId::CamSensorUlvoStatus => "CamSensorUlvoStatus",
            ControlId::CamSensorPhaseReTrain => "CamSensorPhaseReTrain",
            ControlId::CamInitConfigFromFlash => "CamInitConfigFromFlash",
            ControlId::CamTrigerMode => "CamTrigerMode",
            ControlId::CamTrigerOut => "CamTrigerOut",
            ControlId::CamBurstMode => "CamBurstMode",
            ControlId::CamSpeakerLedAlarm => "CamSpeakerLedAlarm",
            ControlId::CamWatchDogFpga => "CamWatchDogFpga",
            ControlId::CamBin6x6mode => "CamBin6x6mode",
            ControlId::CamBin8x8mode => "CamBin8x8mode",
            ControlId::CamGlobalSensorGpsled => "CamGlobalSensorGpsled",
            ControlId::ControlImgProc => "ControlImgProc",
            ControlId::ControlRemoveRbi => "ControlRemoveRbi",
            ControlId::ControlGlobalReset => "ControlGlobalReset",
            ControlId::ControlFrameDetect => "ControlFrameDetect",
            ControlId::CamGainDbconversion => "CamGainDbconversion",
            ControlId::CamCurveSystemGain => "CamCurveSystemGain",
            ControlId::CamCurveFullWell => "CamCurveFullWell",
            ControlId::CamCurveReadoutNoise => "CamCurveReadoutNoise",
            ControlId::CamUseAverageBinning => "CamUseAverageBinning",
            ControlId::ControlOutsidePumpV2 => "ControlOutsidePumpV2",
            ControlId::ControlAutoexposure => "ControlAutoexposure",
            ControlId::ControlAutoexptargetBrightness => "ControlAutoexptargetBrightness",
            ControlId::ControlAutoexpsampleArea => "ControlAutoexpsampleArea",
            ControlId::ControlAutoexpexpMaxMs => "ControlAutoexpexpMaxMs",
            ControlId::ControlAutoexpgainMax => "ControlAutoexpgainMax",
            ControlId::ControlMaxId => "ControlMaxId",
            ControlId::ControlAutowhitebalance => "ControlAutowhitebalance",
            ControlId::ControlImageStabilization => "ControlImageStabilization",
            ControlId::ControlGaindB => "ControlGaindB",
            ControlId::ControlDpc => "ControlDpc",
            ControlId::ControlDpcValue => "ControlDpcValue",
            ControlId::ControlHdr => "ControlHdr",
            ControlId::ControlHdrLK => "ControlHdrLK",
            ControlId::ControlHdrLB => "ControlHdrLB",
            ControlId::ControlHdrX => "ControlHdrX",
            ControlId::ControlHdrShowKb => "ControlHdrShowKb",
        }
    }
}

impl FromStr for ControlId {
    type Err = ();

    fn from_str(s: &str) -> std::result::Result<Self, Self::Err> {
        for &control in VALUES {
            if control.to_str().eq_ignore_ascii_case(s) {
                return Ok(control);
            }
        }
        Err(())
    }
}

impl fmt::Display for ControlId {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        write!(f, "{}", self.to_str())
    }
}

pub const EXPOSURE_FACTOR: f64 = 1_000_000.0;

#[link(name = "qhyccd")]
extern "system" {
    pub fn InitQHYCCDResource() -> u32;
    pub fn ScanQHYCCD() -> u32;
    pub fn GetQHYCCDId(index: u32, id: *mut u8) -> u32;
    pub fn OpenQHYCCD(id: *const c_char) -> QHYCCD;
    pub fn CloseQHYCCD(handle: QHYCCD) -> u32;
    pub fn SetQHYCCDStreamMode(handle: QHYCCD, mode: u8) -> u32;
    pub fn InitQHYCCD(handle: QHYCCD) -> u32;
    pub fn GetQHYCCDEffectiveArea(
        handle: QHYCCD,
        start_x: &mut u32,
        start_y: &mut u32,
        size_x: &mut u32,
        size_y: &mut u32,
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

    pub fn GetQHYCCDNumberOfReadModes(handle: QHYCCD, numModes: *mut u32) -> u32;
    pub fn GetQHYCCDReadModeName(handle: QHYCCD, modeNumber: u32, name: *mut u8) -> u32;

    pub fn GetQHYCCDFWVersion(handle: QHYCCD, buf: *mut u8) -> u32;
    pub fn GetQHYCCDFPGAVersion(handle: QHYCCD, fpga_index: u8, buf: *mut u8) -> u32;
    pub fn GetQHYCCDSDKVersion(
        year: *mut u32,
        month: *mut u32,
        day: *mut u32,
        subday: *mut u32,
    ) -> u32;

}
