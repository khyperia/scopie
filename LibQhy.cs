using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Scopie;

internal struct QHYCamMinMaxStepValue
{
    [MarshalAs(UnmanagedType.LPStr)]
    public string name;
    public double min;
    public double max;
    public double step;
}

internal static unsafe class LibQhy
{
    private const CallingConvention CallConv = CallingConvention.StdCall;

    public static void Check(uint errorCode, [CallerArgumentExpression(nameof(errorCode))] string expression = null!)
    {
        if (errorCode != 0)
            throw new Exception($"QHYCCD SDK returned code {(int)errorCode}: {expression}");
    }

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void OutputQHYCCDDebug([MarshalAs(UnmanagedType.LPStr)] string strOutput);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void SetQHYCCDAutoDetectCamera(bool enable);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void SetQHYCCDLogLevel(byte logLevel);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void EnableQHYCCDMessage(bool enable);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void set_histogram_equalization(bool enable);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void EnableQHYCCDLogFile(bool enable);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDSingleFrameTimeOut(IntPtr h, uint time);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    [return: MarshalAs(UnmanagedType.LPStr)]
    public static extern string GetTimeStamp();

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint InitQHYCCDResource();

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint ReleaseQHYCCDResource();

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint ScanQHYCCD();

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDId(uint index, [MarshalAs(UnmanagedType.LPStr)] StringBuilder id);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDModel([MarshalAs(UnmanagedType.LPStr)] string id, [MarshalAs(UnmanagedType.LPStr)] StringBuilder model);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern IntPtr OpenQHYCCD([MarshalAs(UnmanagedType.LPStr)] string id);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint CloseQHYCCD(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDStreamMode(IntPtr handle, byte mode);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint InitQHYCCD(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint IsQHYCCDControlAvailable(IntPtr handle, ControlId controlId);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDControlName(IntPtr handle, ControlId controlId, [MarshalAs(UnmanagedType.LPStr)] StringBuilder IDname);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDParam(IntPtr handle, ControlId controlId, double value);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern double GetQHYCCDParam(IntPtr handle, ControlId controlId);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDParamMinMaxStep(IntPtr handle, ControlId controlId, out double min, out double max, out double step);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDResolution(IntPtr handle, uint x, uint y, uint xsize, uint ysize);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDMemLength(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint ExpQHYCCDSingleFrame(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDSingleFrame(IntPtr handle, out uint w, out uint h, out uint bpp, out uint channels, [Out] byte[] imgdata);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint CancelQHYCCDExposing(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint CancelQHYCCDExposingAndReadout(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint BeginQHYCCDLive(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDLiveFrame(IntPtr handle, out uint w, out uint h, out uint bpp, out uint channels, byte* imgdata);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint StopQHYCCDLive(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDPcieRecv(IntPtr handle, void* data, int len, ulong timeout);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDPcieDDRNum(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDBinMode(IntPtr handle, uint wbin, uint hbin);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDBitsMode(IntPtr handle, uint bits);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint ControlQHYCCDTemp(IntPtr handle, double targettemp);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint ControlQHYCCDGuide(IntPtr handle, uint direction, ushort duration);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SendOrder2QHYCCDCFW(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string order, uint length);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDCFWStatus(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] StringBuilder status);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint IsQHYCCDCFWPlugged(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDTrigerInterfaceNumber(IntPtr handle, out uint modeNumber);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDTrigerInterfaceName(IntPtr handle, uint modeNumber, [MarshalAs(UnmanagedType.LPStr)] StringBuilder name);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDTrigerInterface(IntPtr handle, uint trigerMode);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDTrigerFunction(IntPtr h, bool value);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDTrigerMode(IntPtr handle, uint trigerMode);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint EnableQHYCCDTrigerOut(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint EnableQHYCCDTrigerOutA(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SendSoftTriger2QHYCCDCam(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDTrigerFilterOnOff(IntPtr handle, bool onoff);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDTrigerFilterTime(IntPtr handle, uint time);

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // public static extern void Bits16ToBits8(QhyccdHandle h, byte* InputData16, byte* OutputData8, uint imageX, uint imageY, ushort B, ushort W);

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // public static extern void HistInfo192x130(QhyccdHandle h, uint x, uint y, byte* InBuf, byte* OutBuf);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint OSXInitQHYCCDFirmware([MarshalAs(UnmanagedType.LPStr)] string path);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint OSXInitQHYCCDFirmwareArray();

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint OSXInitQHYCCDAndroidFirmwareArray(int idVendor, int idProduct, int fileDescriptor);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDChipInfo(IntPtr h, out double chipw, out double chiph, out uint imagew, out uint imageh, out double pixelw, out double pixelh, out uint bpp);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDEffectiveArea(IntPtr h, out uint startX, out uint startY, out uint sizeX, out uint sizeY);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDOverScanArea(IntPtr h, out uint startX, out uint startY, out uint sizeX, out uint sizeY);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDCurrentROI(IntPtr handle, out uint startX, out uint startY, out uint sizeX, out uint sizeY);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDImageStabilizationGravity(IntPtr handle, out int GravityX, out int GravityY);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDFocusSetting(IntPtr h, uint focusCenterX, uint focusCenterY);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDExposureRemaining(IntPtr h);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDFWVersion(IntPtr h, [Out] byte[] buf);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDFPGAVersion(IntPtr h, byte fpga_index, [Out] byte[] buf);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDInterCamSerialParam(IntPtr h, uint opt);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDInterCamSerialTX(IntPtr h, [MarshalAs(UnmanagedType.LPStr)] string buf, uint length);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDInterCamSerialRX(IntPtr h, [MarshalAs(UnmanagedType.LPStr)] StringBuilder buf);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDInterCamOledOnOff(IntPtr handle, byte onoff);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDInterCamOledBrightness(IntPtr handle, byte brightness);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SendFourLine2QHYCCDInterCamOled(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string messagetemp, [MarshalAs(UnmanagedType.LPStr)] string messageinfo, [MarshalAs(UnmanagedType.LPStr)] string messagetime, [MarshalAs(UnmanagedType.LPStr)] string messagemode);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SendTwoLine2QHYCCDInterCamOled(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string messageTop, [MarshalAs(UnmanagedType.LPStr)] string messageBottom);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SendOneLine2QHYCCDInterCamOled(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] string messageTop);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDCameraStatus(IntPtr h, byte* buf);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDShutterStatus(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint ControlQHYCCDShutter(IntPtr handle, byte status);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDPressure(IntPtr handle, out double pressure);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDHumidity(IntPtr handle, out double hd);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDI2CTwoWrite(IntPtr handle, ushort addr, ushort value);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDI2CTwoRead(IntPtr handle, ushort addr);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern double GetQHYCCDReadingProgress(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint TestQHYCCDPIDParas(IntPtr h, double p, double i, double d);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint DownloadFX3FirmWare(ushort vid, ushort pid, [MarshalAs(UnmanagedType.LPStr)] string imgpath);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDType(IntPtr h);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDDebayerOnOff(IntPtr h, bool onoff);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDFineTone(IntPtr h, byte setshporshd, byte shdloc, byte shploc, byte shwidth);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDGPSVCOXFreq(IntPtr handle, ushort i);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDGPSLedCalMode(IntPtr handle, byte i);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void SetQHYCCDGPSLedCal(IntPtr handle, uint pos, byte width);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void SetQHYCCDGPSPOSA(IntPtr handle, byte is_slave, uint pos, byte width);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void SetQHYCCDGPSPOSB(IntPtr handle, byte is_slave, uint pos, byte width);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDGPSMasterSlave(IntPtr handle, byte i);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void SetQHYCCDGPSSlaveModeParameter(IntPtr handle, uint target_sec, uint target_us, uint deltaT_sec, uint deltaT_us, uint expTime);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void SetQHYCCDQuit();

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDVendRequestWrite(IntPtr h, byte req, ushort value, ushort index1, uint length, byte* data);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDVendRequestRead(IntPtr h, byte req, ushort value, ushort index1, uint length, byte* data);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDReadUSB_SYNC(IntPtr pDevHandle, byte endpoint, uint length, byte* data, uint timeout);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDLibusbBulkTransfer(IntPtr pDevHandle, byte endpoint, byte* data, uint length, out int transferred, uint timeout);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDSDKVersion(out uint year, out uint month, out uint day, out uint subday);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDNumberOfReadModes(IntPtr h, out uint numModes);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDReadModeResolution(IntPtr h, uint modeNumber, out uint width, out uint height);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDReadModeName(IntPtr h, uint modeNumber, [MarshalAs(UnmanagedType.LPStr)] StringBuilder name);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDReadMode(IntPtr h, uint modeNumber);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDReadMode(IntPtr h, out uint modeNumber);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDBeforeOpenParam(ref QHYCamMinMaxStepValue p, ControlId controlId);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint EnableQHYCCDBurstMode(IntPtr h, bool i);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDBurstModeStartEnd(IntPtr h, ushort start, ushort end);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint EnableQHYCCDBurstCountFun(IntPtr h, bool i);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint ResetQHYCCDFrameCounter(IntPtr h);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDBurstIDLE(IntPtr h);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint ReleaseQHYCCDBurstIDLE(IntPtr h);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDBurstModePatchNumber(IntPtr h, uint value);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDEnableLiveModeAntiRBI(IntPtr h, uint value);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDWriteFPGA(IntPtr h, byte number, byte regindex, byte regvalue);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDWriteCMOS(IntPtr h, byte number, ushort regindex, ushort regvalue);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDTwoChannelCombineParameter(IntPtr handle, double x, double ah, double bh, double al, double bl);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint EnableQHYCCDImageOSD(IntPtr h, uint i);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDPreciseExposureInfo(IntPtr h, out uint PixelPeriod_ps, out uint LinePeriod_ns, out uint FramePeriod_us, out uint ClocksPerLine, out uint LinesPerFrame, out uint ActualExposureTime, out byte isLongExposureMode);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDRollingShutterEndOffset(IntPtr h, uint row, out double offset);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDQuit();

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // public static extern QHYDWORD SetQHYCCDCallBack(QHYCCDProcCallBack ProcCallBack, int Flag);

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // public static extern void RegisterPnpEventIn(void (*in_pnp_event_in_func)([MarshalAs(UnmanagedType.LPStr)] stringid));

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // public static extern void RegisterPnpEventOut(void (*in_pnp_event_out_func)([MarshalAs(UnmanagedType.LPStr)] stringid));

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint resetDev([MarshalAs(UnmanagedType.LPStr)] string deviceID, uint readModeIndex, byte streamMode, IntPtr devHandle, out uint imageWidth, out uint imageHigh, uint bitDepth);

    public delegate void DataEventFunc([MarshalAs(UnmanagedType.LPStr)] string id, byte* imgdata);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void RegisterDataEventSingle(DataEventFunc func);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void RegisterDataEventLive(DataEventFunc func);

    public delegate void TransferEventErrorFunc();

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void RegisterTransferEventError(TransferEventErrorFunc func);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint PCIEClearDDR(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetReadModesNumber([MarshalAs(UnmanagedType.LPStr)] string deviceID, out uint numModes);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetReadModeName([MarshalAs(UnmanagedType.LPStr)] string deviceID, uint modeIndex, [MarshalAs(UnmanagedType.LPStr)] StringBuilder modeName);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDSensorPhaseReTrain(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDReadInitConfigFlash(IntPtr handle, byte* configString_raw64);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDEraseInitConfigFlash(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDResetFlashULVOError(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDTestFlashULVOError(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDSetFlashInitPWM(IntPtr handle, byte pwm);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDGetDebugDataD3(IntPtr handle, byte* debugData_raw64);

    // TODO: This is missing EXPORTFUNC
    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDGetDebugControlID(ControlId controlId, bool hasValue, bool isSetValue, double value);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDEqualizeHistogram(byte* pdata, int width, int height, int bpp);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCD_fpga_open(int id);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCD_fpga_close();

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern int QHYCCD_fpga_send(int chnl, void* data, int len, int destoff, int last, ulong timeout);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern int QHYCCD_fpga_recv(int chnl, void* data, int len, ulong timeout);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCD_fpga_reset();

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDLoadCalibrationFrames(IntPtr handle, uint ImgW, uint ImgH, uint ImgBits, uint ImgChannel, [MarshalAs(UnmanagedType.LPStr)] string DarkFile, [MarshalAs(UnmanagedType.LPStr)] string FlatFile, [MarshalAs(UnmanagedType.LPStr)] string BiasFile);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDCalibrationOnOff(IntPtr handle, bool onoff);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDFrameDetectPos(IntPtr handle, uint pos);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDFrameDetectCode(IntPtr handle, byte code);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint SetQHYCCDFrameDetectOnOff(IntPtr handle, bool onoff);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint GetQHYCCDSensorName(IntPtr handle, [MarshalAs(UnmanagedType.LPStr)] StringBuilder name);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern byte GetCameraIsSuperSpeedFromID([MarshalAs(UnmanagedType.LPStr)] string id);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void EnableSupportOICamera([MarshalAs(UnmanagedType.LPStr)] string password);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void QHYCCDResetEMMC(IntPtr handle, bool reset);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDReadEMMCState(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDReadEMMCAddress(IntPtr handle);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDReadEMMCFPGAData(IntPtr handle, uint* data);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDOpenEMMCMode(IntPtr handle, bool open);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDReadEMMC(IntPtr handle, uint address, uint length, byte* buffer);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCDWriteEMMC(IntPtr handle, uint address, uint length, byte* buffer);

    // missing EXPORTFUNC
    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // void call_pnp_event();

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // void call_data_event_live([MarshalAs(UnmanagedType.LPStr)] string id, byte* imgdata);

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // void call_transfer_event_error();

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // void call_critical_event_error(QhyccdHandle h);

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // public static extern void RegisterPnpEventIn(void (*in_pnp_event_in_func)([MarshalAs(UnmanagedType.LPStr)] stringid));

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // public static extern void RegisterPnpEventOut(void (*in_pnp_event_out_func)([MarshalAs(UnmanagedType.LPStr)] stringid));

    // [DllImport("qhyccd", CallingConvention = CallConv)]
    // public static extern void RegisterTransferEventError(void (*transfer_event_error_func)());

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern void SetPCIECardInfo(IntPtr handle, byte index, byte value);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint PCIEWriteCameraRegister1(IntPtr handle, byte idx, byte val);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint PCIEWriteCameraRegister2(IntPtr handle, byte idx, byte val);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCD_DbGainToGainValue(IntPtr h, double dbgain, out double gainvalue);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCD_GainValueToDbGain(IntPtr h, double gainvalue, out double dbgain);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCD_curveSystemGain(IntPtr handle, double gainV, out double systemgain);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCD_curveFullWell(IntPtr handle, double gainV, out double fullwell);

    [DllImport("qhyccd", CallingConvention = CallConv)]
    public static extern uint QHYCCD_curveReadoutNoise(IntPtr handle, double gainV, out double readoutnoise);
}

internal enum ControlId : int
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
    ///*1025*/ CONTROL_AUTOEXPOSURE,			//!<auto exposure
    ///*1026*/ CONTROL_AUTOEXPTargetBrightness,//CONTROL_AUTOEXPmessureValue,
    ///*1027*/ CONTROL_AUTOEXPSampleArea,//CONTROL_AUTOEXPmessureMethod,
    ///*1028*/ CONTROL_AUTOEXPexpMaxMS,       //!<auto exposure max exp(ms)
    ///*1029*/ CONTROL_AUTOEXPgainMax,        //!<auto exposure max gain
/*1030*/ ControlImageStabilization, //!<image stabilization      
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
    ControlHdrLK, //!<HDR L k
/*1036*/
    ControlHdrLB, //!<HDR L b
/*1037*/
    ControlHdrX, //,                //!<HDR X
/*1038*/
    ControlHdrShowKb //!show HDR kb
//CONTROL_SHOWIMG
};
