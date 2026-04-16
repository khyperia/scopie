using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Scopie;

internal static unsafe partial class LibQhy
{
    public static void Check(uint errorCode, [CallerArgumentExpression(nameof(errorCode))] string expression = null!)
    {
        if (errorCode != 0)
            throw new Exception($"QHYCCD SDK returned code {(int)errorCode}: {expression}");
    }

    [LibraryImport("qhyccd")]
    public static partial void OutputQHYCCDDebug([MarshalAs(UnmanagedType.LPStr)] string strOutput);

    [LibraryImport("qhyccd")]
    public static partial void SetQHYCCDAutoDetectCamera([MarshalAs(UnmanagedType.I1)] bool enable);

    [LibraryImport("qhyccd")]
    public static partial void SetQHYCCDLogLevel(byte logLevel);

    [LibraryImport("qhyccd")]
    public static partial void EnableQHYCCDMessage([MarshalAs(UnmanagedType.I1)] bool enable);

    [LibraryImport("qhyccd")]
    public static partial void set_histogram_equalization([MarshalAs(UnmanagedType.I1)] bool enable);

    [LibraryImport("qhyccd")]
    public static partial void EnableQHYCCDLogFile([MarshalAs(UnmanagedType.I1)] bool enable);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDSingleFrameTimeOut(IntPtr h, uint time);

    [LibraryImport("qhyccd")]
    [return: MarshalAs(UnmanagedType.LPStr)]
    public static partial string GetTimeStamp();

    [LibraryImport("qhyccd")]
    public static partial uint InitQHYCCDResource();

    [LibraryImport("qhyccd")]
    public static partial uint ReleaseQHYCCDResource();

    [LibraryImport("qhyccd")]
    public static partial uint ScanQHYCCD();

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDId(uint index, [Out] byte[] id);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDModel([MarshalAs(UnmanagedType.LPStr)] string id, [Out] byte[] model);

    [LibraryImport("qhyccd")]
    public static partial IntPtr OpenQHYCCD([MarshalAs(UnmanagedType.LPStr)] string id);

    [LibraryImport("qhyccd")]
    public static partial uint CloseQHYCCD(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDStreamMode(IntPtr handle, byte mode);

    [LibraryImport("qhyccd")]
    public static partial uint InitQHYCCD(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint IsQHYCCDControlAvailable(IntPtr handle, ControlId controlId);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDControlName(IntPtr handle, ControlId controlId, [Out] byte[] idName);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDParam(IntPtr handle, ControlId controlId, double value);

    [LibraryImport("qhyccd")]
    public static partial double GetQHYCCDParam(IntPtr handle, ControlId controlId);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDParamMinMaxStep(IntPtr handle, ControlId controlId, out double min, out double max, out double step);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDResolution(IntPtr handle, uint x, uint y, uint xsize, uint ysize);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDMemLength(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint ExpQHYCCDSingleFrame(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDSingleFrame(IntPtr handle, out uint w, out uint h, out uint bpp, out uint channels, [Out] byte[] imgdata);

    [LibraryImport("qhyccd")]
    public static partial uint CancelQHYCCDExposing(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint CancelQHYCCDExposingAndReadout(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint BeginQHYCCDLive(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDLiveFrame(IntPtr handle, out uint w, out uint h, out uint bpp, out uint channels, byte* imgdata);

    [LibraryImport("qhyccd")]
    public static partial uint StopQHYCCDLive(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDPcieRecv(IntPtr handle, void* data, int len, ulong timeout);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDPcieDDRNum(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDBinMode(IntPtr handle, uint wbin, uint hbin);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDBitsMode(IntPtr handle, uint bits);

    [LibraryImport("qhyccd")]
    public static partial uint ControlQHYCCDTemp(IntPtr handle, double targettemp);

    [LibraryImport("qhyccd")]
    public static partial uint ControlQHYCCDGuide(IntPtr handle, uint direction, ushort duration);

    [LibraryImport("qhyccd")]
    public static partial uint SendOrder2QHYCCDCFW(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string order, uint length);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDCFWStatus(IntPtr handle, [Out] byte[] status);

    [LibraryImport("qhyccd")]
    public static partial uint IsQHYCCDCFWPlugged(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDTrigerInterfaceNumber(IntPtr handle, out uint modeNumber);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDTrigerInterfaceName(IntPtr handle, uint modeNumber, [Out] byte[] name);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDTrigerInterface(IntPtr handle, uint trigerMode);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDTrigerFunction(IntPtr h, [MarshalAs(UnmanagedType.I1)] bool value);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDTrigerMode(IntPtr handle, uint trigerMode);

    [LibraryImport("qhyccd")]
    public static partial uint EnableQHYCCDTrigerOut(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint EnableQHYCCDTrigerOutA(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint SendSoftTriger2QHYCCDCam(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDTrigerFilterOnOff(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool onoff);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDTrigerFilterTime(IntPtr handle, uint time);

    // [LibraryImport("qhyccd")]
    // public static partial void Bits16ToBits8(QhyccdHandle h, byte* InputData16, byte* OutputData8, uint imageX, uint imageY, ushort B, ushort W);

    // [LibraryImport("qhyccd")]
    // public static partial void HistInfo192x130(QhyccdHandle h, uint x, uint y, byte* InBuf, byte* OutBuf);

    [LibraryImport("qhyccd")]
    public static partial uint OSXInitQHYCCDFirmware([MarshalAs(UnmanagedType.LPStr)] string path);

    [LibraryImport("qhyccd")]
    public static partial uint OSXInitQHYCCDFirmwareArray();

    [LibraryImport("qhyccd")]
    public static partial uint OSXInitQHYCCDAndroidFirmwareArray(int idVendor, int idProduct, int fileDescriptor);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDChipInfo(IntPtr h, out double chipw, out double chiph, out uint imagew, out uint imageh, out double pixelw, out double pixelh, out uint bpp);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDEffectiveArea(IntPtr h, out uint startX, out uint startY, out uint sizeX, out uint sizeY);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDOverScanArea(IntPtr h, out uint startX, out uint startY, out uint sizeX, out uint sizeY);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDCurrentROI(IntPtr handle, out uint startX, out uint startY, out uint sizeX, out uint sizeY);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDImageStabilizationGravity(IntPtr handle, out int gravityX, out int gravityY);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDFocusSetting(IntPtr h, uint focusCenterX, uint focusCenterY);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDExposureRemaining(IntPtr h);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDFWVersion(IntPtr h, [Out] byte[] buf);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDFPGAVersion(IntPtr h, byte fpgaIndex, [Out] byte[] buf);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDInterCamSerialParam(IntPtr h, uint opt);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDInterCamSerialTX(IntPtr h, [MarshalAs(UnmanagedType.LPStr)] string buf, uint length);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDInterCamSerialRX(IntPtr h, [Out] byte[] buf);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDInterCamOledOnOff(IntPtr handle, byte onoff);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDInterCamOledBrightness(IntPtr handle, byte brightness);

    [LibraryImport("qhyccd")]
    public static partial uint SendFourLine2QHYCCDInterCamOled(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string messagetemp, [MarshalAs(UnmanagedType.LPStr)] string messageinfo, [MarshalAs(UnmanagedType.LPStr)] string messagetime, [MarshalAs(UnmanagedType.LPStr)] string messagemode);

    [LibraryImport("qhyccd")]
    public static partial uint SendTwoLine2QHYCCDInterCamOled(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string messageTop, [MarshalAs(UnmanagedType.LPStr)] string messageBottom);

    [LibraryImport("qhyccd")]
    public static partial uint SendOneLine2QHYCCDInterCamOled(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string messageTop);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDCameraStatus(IntPtr h, byte* buf);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDShutterStatus(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint ControlQHYCCDShutter(IntPtr handle, byte status);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDPressure(IntPtr handle, out double pressure);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDHumidity(IntPtr handle, out double hd);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDI2CTwoWrite(IntPtr handle, ushort addr, ushort value);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDI2CTwoRead(IntPtr handle, ushort addr);

    [LibraryImport("qhyccd")]
    public static partial double GetQHYCCDReadingProgress(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint TestQHYCCDPIDParas(IntPtr h, double p, double i, double d);

    [LibraryImport("qhyccd")]
    public static partial uint DownloadFX3FirmWare(ushort vid, ushort pid, [MarshalAs(UnmanagedType.LPStr)] string imgpath);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDType(IntPtr h);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDDebayerOnOff(IntPtr h, [MarshalAs(UnmanagedType.I1)] bool onoff);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDFineTone(IntPtr h, byte setshporshd, byte shdloc, byte shploc, byte shwidth);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDGPSVCOXFreq(IntPtr handle, ushort i);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDGPSLedCalMode(IntPtr handle, byte i);

    [LibraryImport("qhyccd")]
    public static partial void SetQHYCCDGPSLedCal(IntPtr handle, uint pos, byte width);

    [LibraryImport("qhyccd")]
    public static partial void SetQHYCCDGPSPOSA(IntPtr handle, byte isSlave, uint pos, byte width);

    [LibraryImport("qhyccd")]
    public static partial void SetQHYCCDGPSPOSB(IntPtr handle, byte isSlave, uint pos, byte width);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDGPSMasterSlave(IntPtr handle, byte i);

    [LibraryImport("qhyccd")]
    public static partial void SetQHYCCDGPSSlaveModeParameter(IntPtr handle, uint targetSec, uint targetUs, uint deltaTSec, uint deltaTUs, uint expTime);

    [LibraryImport("qhyccd")]
    public static partial void SetQHYCCDQuit();

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDVendRequestWrite(IntPtr h, byte req, ushort value, ushort index1, uint length, byte* data);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDVendRequestRead(IntPtr h, byte req, ushort value, ushort index1, uint length, byte* data);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDReadUSB_SYNC(IntPtr pDevHandle, byte endpoint, uint length, byte* data, uint timeout);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDLibusbBulkTransfer(IntPtr pDevHandle, byte endpoint, byte* data, uint length, out int transferred, uint timeout);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDSDKVersion(out uint year, out uint month, out uint day, out uint subday);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDNumberOfReadModes(IntPtr h, out uint numModes);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDReadModeResolution(IntPtr h, uint modeNumber, out uint width, out uint height);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDReadModeName(IntPtr h, uint modeNumber, [Out] byte[] name);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDReadMode(IntPtr h, uint modeNumber);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDReadMode(IntPtr h, out uint modeNumber);

    // [LibraryImport("qhyccd")]
    // public static partial uint GetQHYCCDBeforeOpenParam(ref QHYCamMinMaxStepValue p, ControlId controlId);

    [LibraryImport("qhyccd")]
    public static partial uint EnableQHYCCDBurstMode(IntPtr h, [MarshalAs(UnmanagedType.I1)] bool i);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDBurstModeStartEnd(IntPtr h, ushort start, ushort end);

    [LibraryImport("qhyccd")]
    public static partial uint EnableQHYCCDBurstCountFun(IntPtr h, [MarshalAs(UnmanagedType.I1)] bool i);

    [LibraryImport("qhyccd")]
    public static partial uint ResetQHYCCDFrameCounter(IntPtr h);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDBurstIDLE(IntPtr h);

    [LibraryImport("qhyccd")]
    public static partial uint ReleaseQHYCCDBurstIDLE(IntPtr h);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDBurstModePatchNumber(IntPtr h, uint value);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDEnableLiveModeAntiRBI(IntPtr h, uint value);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDWriteFPGA(IntPtr h, byte number, byte regindex, byte regvalue);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDWriteCMOS(IntPtr h, byte number, ushort regindex, ushort regvalue);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDTwoChannelCombineParameter(IntPtr handle, double x, double ah, double bh, double al, double bl);

    [LibraryImport("qhyccd")]
    public static partial uint EnableQHYCCDImageOSD(IntPtr h, uint i);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDPreciseExposureInfo(IntPtr h, out uint pixelPeriodPs, out uint linePeriodNs, out uint framePeriodUs, out uint clocksPerLine, out uint linesPerFrame, out uint actualExposureTime, out byte isLongExposureMode);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDRollingShutterEndOffset(IntPtr h, uint row, out double offset);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDQuit();

    // [LibraryImport("qhyccd")]
    // public static partial QHYDWORD SetQHYCCDCallBack(QHYCCDProcCallBack ProcCallBack, int Flag);

    // [LibraryImport("qhyccd")]
    // public static partial void RegisterPnpEventIn(void (*in_pnp_event_in_func)([MarshalAs(UnmanagedType.LPStr)] stringid));

    // [LibraryImport("qhyccd")]
    // public static partial void RegisterPnpEventOut(void (*in_pnp_event_out_func)([MarshalAs(UnmanagedType.LPStr)] stringid));

    [LibraryImport("qhyccd")]
    public static partial uint resetDev([MarshalAs(UnmanagedType.LPStr)] string deviceId, uint readModeIndex, byte streamMode, IntPtr devHandle, out uint imageWidth, out uint imageHigh, uint bitDepth);

    [LibraryImport("qhyccd")]
    public static partial void RegisterDataEventSingle(delegate* unmanaged<byte*, byte*> func);

    [LibraryImport("qhyccd")]
    public static partial void RegisterDataEventLive(delegate* unmanaged<byte*, byte*> func);

    public delegate void TransferEventErrorFunc();

    [LibraryImport("qhyccd")]
    public static partial void RegisterTransferEventError(TransferEventErrorFunc func);

    [LibraryImport("qhyccd")]
    public static partial uint PCIEClearDDR(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint GetReadModesNumber([MarshalAs(UnmanagedType.LPStr)] string deviceId, out uint numModes);

    [LibraryImport("qhyccd")]
    public static partial uint GetReadModeName([MarshalAs(UnmanagedType.LPStr)] string deviceId, uint modeIndex, [Out] byte[] modeName);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDSensorPhaseReTrain(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDReadInitConfigFlash(IntPtr handle, byte* configStringRaw64);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDEraseInitConfigFlash(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDResetFlashULVOError(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDTestFlashULVOError(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDSetFlashInitPWM(IntPtr handle, byte pwm);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDGetDebugDataD3(IntPtr handle, byte* debugDataRaw64);

    // This is missing EXPORTFUNC
    [LibraryImport("qhyccd")]
    public static partial void QHYCCDGetDebugControlID(ControlId controlId, [MarshalAs(UnmanagedType.I1)] bool hasValue, [MarshalAs(UnmanagedType.I1)] bool isSetValue, double value);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDEqualizeHistogram(byte* pdata, int width, int height, int bpp);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCD_fpga_open(int id);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCD_fpga_close();

    [LibraryImport("qhyccd")]
    public static partial int QHYCCD_fpga_send(int chnl, void* data, int len, int destoff, int last, ulong timeout);

    [LibraryImport("qhyccd")]
    public static partial int QHYCCD_fpga_recv(int chnl, void* data, int len, ulong timeout);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCD_fpga_reset();

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDLoadCalibrationFrames(IntPtr handle, uint imgW, uint imgH, uint imgBits, uint imgChannel, [MarshalAs(UnmanagedType.LPStr)] string darkFile, [MarshalAs(UnmanagedType.LPStr)] string flatFile, [MarshalAs(UnmanagedType.LPStr)] string biasFile);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDCalibrationOnOff(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool onoff);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDFrameDetectPos(IntPtr handle, uint pos);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDFrameDetectCode(IntPtr handle, byte code);

    [LibraryImport("qhyccd")]
    public static partial uint SetQHYCCDFrameDetectOnOff(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool onoff);

    [LibraryImport("qhyccd")]
    public static partial uint GetQHYCCDSensorName(IntPtr handle, [Out] byte[] name);

    [LibraryImport("qhyccd")]
    public static partial byte GetCameraIsSuperSpeedFromID([MarshalAs(UnmanagedType.LPStr)] string id);

    [LibraryImport("qhyccd")]
    public static partial void EnableSupportOICamera([MarshalAs(UnmanagedType.LPStr)] string password);

    [LibraryImport("qhyccd")]
    public static partial void QHYCCDResetEMMC(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool reset);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDReadEMMCState(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDReadEMMCAddress(IntPtr handle);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDReadEMMCFPGAData(IntPtr handle, uint* data);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDOpenEMMCMode(IntPtr handle, [MarshalAs(UnmanagedType.I1)] bool open);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDReadEMMC(IntPtr handle, uint address, uint length, byte* buffer);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCDWriteEMMC(IntPtr handle, uint address, uint length, byte* buffer);

    // missing EXPORTFUNC
    // [LibraryImport("qhyccd")]
    // void call_pnp_event();

    // [LibraryImport("qhyccd")]
    // void call_data_event_live([MarshalAs(UnmanagedType.LPStr)] string id, byte* imgdata);

    // [LibraryImport("qhyccd")]
    // void call_transfer_event_error();

    // [LibraryImport("qhyccd")]
    // void call_critical_event_error(QhyccdHandle h);

    // [LibraryImport("qhyccd")]
    // public static partial void RegisterPnpEventIn(void (*in_pnp_event_in_func)([MarshalAs(UnmanagedType.LPStr)] stringid));

    // [LibraryImport("qhyccd")]
    // public static partial void RegisterPnpEventOut(void (*in_pnp_event_out_func)([MarshalAs(UnmanagedType.LPStr)] stringid));

    // [LibraryImport("qhyccd")]
    // public static partial void RegisterTransferEventError(void (*transfer_event_error_func)());

    [LibraryImport("qhyccd")]
    public static partial void SetPCIECardInfo(IntPtr handle, byte index, byte value);

    [LibraryImport("qhyccd")]
    public static partial uint PCIEWriteCameraRegister1(IntPtr handle, byte idx, byte val);

    [LibraryImport("qhyccd")]
    public static partial uint PCIEWriteCameraRegister2(IntPtr handle, byte idx, byte val);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCD_DbGainToGainValue(IntPtr h, double dbgain, out double gainvalue);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCD_GainValueToDbGain(IntPtr h, double gainvalue, out double dbgain);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCD_curveSystemGain(IntPtr handle, double gainV, out double systemgain);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCD_curveFullWell(IntPtr handle, double gainV, out double fullwell);

    [LibraryImport("qhyccd")]
    public static partial uint QHYCCD_curveReadoutNoise(IntPtr handle, double gainV, out double readoutnoise);
}

internal enum ControlId
{
    /*0*/
    ControlBrightness = 0, //!< image brightness
    /*1*/
    ControlContrast, //!< image contrast
    /*2*/
    ControlWbr, //!< the red of white balance
    /*3*/
    ControlWbb, //!< the blue of white balance
    /*4*/
    ControlWbg, //!< the green of white balance
    /*5*/
    ControlGamma, //!< screen gamma
    /*6*/
    ControlGain, //!< camera gain
    /*7*/
    ControlOffset, //!< camera offset
    /*8*/
    ControlExposure, //!< expose time (us)
    /*9*/
    ControlSpeed, //!< transfer speed
    /*10*/
    ControlTransferbit, //!< image depth bits
    /*11*/
    ControlChannels, //!< image channels
    /*12*/
    ControlUsbtraffic, //!< hblank
    /*13*/
    ControlRownoisere, //!< row denoise
    /*14*/
    ControlCurtemp, //!< current cmos or ccd temprature
    /*15*/
    ControlCurpwm, //!< current cool pwm
    /*16*/
    ControlManulpwm, //!< set the cool pwm
    /*17*/
    ControlCfwport, //!< control camera color filter wheel port
    /*18*/
    ControlCooler, //!< check if camera has cooler
    /*19*/
    ControlSt4Port, //!< check if camera has st4port
    /*20*/
    CamColor, // FIXME!  CAM_IS_COLOR CAM_COLOR conflict
    /*21*/
    CamBin1X1Mode, //!< check if camera has bin1x1 mode
    /*22*/
    CamBin2X2Mode, //!< check if camera has bin2x2 mode
    /*23*/
    CamBin3X3Mode, //!< check if camera has bin3x3 mode
    /*24*/
    CamBin4X4Mode, //!< check if camera has bin4x4 mode
    /*25*/
    CamMechanicalshutter, //!< mechanical shutter
    /*26*/
    CamTrigerInterface, //!< check if camera has triger interface
    /*27*/
    CamTecoverprotectInterface, //!< tec overprotect
    /*28*/
    CamSingnalclampInterface, //!< singnal clamp
    /*29*/
    CamFinetoneInterface, //!< fine tone
    /*30*/
    CamShuttermotorheatingInterface, //!< shutter motor heating
    /*31*/
    CamCalibratefpnInterface, //!< calibrated frame
    /*32*/
    CamChiptemperaturesensorInterface, //!< chip temperaure sensor
    /*33*/
    CamUsbreadoutslowestInterface, //!< usb readout slowest

    /*34*/
    Cam8Bits, //!< 8bit depth
    /*35*/
    Cam16Bits, //!< 16bit depth
    /*36*/
    CamGps, //!< check if camera has gps

    /*37*/
    CamIgnoreoverscanInterface, //!< ignore overscan area

    /*38*/ //QHYCCD_3A_AUTOBALANCE,					 //!< auto white balance//lyl move to 1024
    /*39*/
    Qhyccd3AAutoexposure = 39, //!< auto exposure
    /*40*/
    Qhyccd3AAutofocus,
    /*41*/
    ControlAmpv, //!< ccd or cmos ampv
    /*42*/
    ControlVcam, //!< Virtual Camera on off
    /*43*/
    CamViewMode,

    /*44*/
    ControlCfwslotsnum, //!< check CFW slots number
    /*45*/
    IsExposingDone,
    /*46*/
    ScreenStretchB,
    /*47*/
    ScreenStretchW,
    /*48*/
    ControlDdr,
    /*49*/
    CamLightPerformanceMode,

    /*50*/
    CamQhy5IiGuideMode,
    /*51*/
    DdrBufferCapacity,
    /*52*/
    DdrBufferReadThreshold,
    /*53*/
    DefaultGain,
    /*54*/
    DefaultOffset,
    /*55*/
    OutputDataActualBits,
    /*56*/
    OutputDataAlignment,

    /*57*/
    CamSingleframemode,
    /*58*/
    CamLivevideomode,
    /*59*/
    CamIsColor,
    /*60*/
    HasHardwareFrameCounter,
    /*61*/
    ControlMaxIdError, //** No Use , last max index */
    /*62*/
    CamHumidity, //!<check if camera has	 humidity sensor  20191021 LYL Unified humidity function
    /*63*/
    CamPressure, //check if camera has pressure sensor
    /*64*/
    ControlVacuumPump, // if camera has VACUUM PUMP
    /*65*/
    ControlSensorChamberCyclePump, // air cycle pump for sensor drying
    /*66*/
    Cam32Bits,
    /*67*/
    CamSensorUlvoStatus, // Sensor working status [0:init  1:good  2:checkErr  3:monitorErr 8:good 9:powerChipErr]  410 461 411 600 268 [Eris board]
    /*68*/
    CamSensorPhaseReTrain, // 2020,4040/PRO，6060,42PRO
    /*69*/
    CamInitConfigFromFlash, // 2410 461 411 600 268 for now
    /*70*/
    CamTrigerMode, //check if camera has multiple triger mode
    /*71*/
    CamTrigerOut, //check if camera support triger out function
    /*72*/
    CamBurstMode, //check if camera support burst mode
    /*73*/
    CamSpeakerLedAlarm, // for OEM-600
    /*74*/
    CamWatchDogFpga, // for _QHY5III178C Celestron, SDK have to feed this dog or it go reset
    /*75*/
    CamBin6X6Mode, //!< check if camera has bin6x6 mode
    /*76*/
    CamBin8X8Mode, //!< check if camera has bin8x8 mode
    /*77*/
    CamGlobalSensorGpsled, // Show GPS LED tab on sharpCap
    /*78*/
    ControlImgProc, // Process image
    /*79*/
    ControlRemoveRbi, // RBI, Remove single residual image
    /*80*/
    ControlGlobalReset, //!<image stabilization
    /*81*/
    ControlFrameDetect,
    /*82*/
    CamGainDbConversion, //!<Supports the conversion between db and gain
    /*83*/
    CamCurveSystemGain, //!
    /*84*/
    CamCurveFullWell,
    /*85*/
    CamCurveReadoutNoise,
    /*86*/
    CamUseAverageBinning,
    /*87*/
    ControlOutsidePumpV2, // air pump outside

    /*88*/
    ControlAutoexposure, //!<auto exposure
    /*89*/
    ControlAutoexpTargetBrightness, //!<auto exposure Target Brightness
    /*90*/
    ControlAutoexpSampleArea, //!<auto exposure Sample Area
    /*91*/
    ControlAutoexPexpMaxMs, //!<auto exposure max exp(ms)
    /*92*/
    ControlAutoexPgainMax, //!<auto exposure max gain

    /* Do not Put Item after  CONTROL_MAX_ID !! This should be the max index of the list */
    /*Last One */
    ControlMaxId,

    //TEST id name list
    /*1024*/
    ControlAutowhitebalance = 1024, //!<auto white balance  eg.CONTROL_TEST=1024
                                    // /*1025*/ CONTROL_AUTOEXPOSURE,			//!<auto exposure
                                    // /*1026*/ CONTROL_AUTOEXPTargetBrightness,//CONTROL_AUTOEXPmessureValue,
                                    // /*1027*/ CONTROL_AUTOEXPSampleArea,//CONTROL_AUTOEXPmessureMethod,
                                    // /*1028*/ CONTROL_AUTOEXPexpMaxMS,       //!<auto exposure max exp(ms)
                                    // /*1029*/ CONTROL_AUTOEXPgainMax,        //!<auto exposure max gain
    /*1030*/
    ControlImageStabilization, //!<image stabilization
    /*1031*/
    ControlGaiNdB, //!<uesed to test dBGain control  //CONTROL_dB_TO_GAIN
    /*1032*/
    ControlDpc, //!<Turn on or off the image DPC function(Remove thermal noise)
    /*1033*/
    ControlDpcValue, //!<value the image DPC function(Remove thermal noise)
    /*1034*/
    ControlHdr, //!<HDR For cameras with high gain and low gain channels combined into 16 bits, set combination parameters>
                //!<HDR status  0:As-is output  1:Splice according to k and b values  2:Calculate k and b, only once
    /*1035*/ //CONTROL_HDR_H_k,               //!<HDR H k
    /*1036*/ //CONTROL_HDR_H_b,               //!<HDR H b
    /*1035*/
    ControlHdrLk, //!<HDR L k
    /*1036*/
    ControlHdrLb, //!<HDR L b
    /*1037*/
    ControlHdrX, //,                //!<HDR X
    /*1038*/
    ControlHdrShowKb //!show HDR kb
                     //CONTROL_SHOWIMG
};
