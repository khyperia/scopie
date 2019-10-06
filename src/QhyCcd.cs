using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Scopie
{
    struct Frame
    {
        public ushort[] Imgdata;
        public uint Width;
        public uint Height;

        public Frame(ushort[] imgdata, uint width, uint height)
        {
            Imgdata = imgdata;
            Width = width;
            Height = height;
        }
    }

    class QhyCcd : IDisposable
    {
        private readonly string _name;
        private readonly IntPtr _handle;
        private readonly Control[] _controls;
        private readonly bool _useLive;
        private uint _originalWidth;
        private uint _originalHeight;

        private byte[]? _imgdataByte;
        private ushort[]? _imgdataShort;

        public static void Check(uint result, [CallerMemberName] string name = "", [CallerFilePath] string filePath = "", [CallerLineNumber] int line = 0)
        {
            if (result != 0)
            {
                throw new Exception($"QHY error: {result} (0x{result:x}) - {name}() {filePath}:{line}");
            }
        }

        static QhyCcd()
        {
            Check(QhyCcdDll.InitQHYCCDResource());
        }

        public static uint NumCameras() => QhyCcdDll.ScanQHYCCD();

        public static string CameraName(int index)
        {
            var builder = new StringBuilder(512);
            Check(QhyCcdDll.GetQHYCCDId(index, builder));
            return builder.ToString();
        }

        public QhyCcd(bool useLive, int index)
        {
            var num = NumCameras();
            if (index >= num)
            {
                throw new Exception($"Camera index out of range: {index} >= {num}");
            }
            var builder = new StringBuilder(512);
            Check(QhyCcdDll.GetQHYCCDId(index, builder));
            _name = builder.ToString();
            _handle = QhyCcdDll.OpenQHYCCD(builder);
            _useLive = useLive;
            Init();
            var control_ids = (CONTROL_ID[])Enum.GetValues(typeof(CONTROL_ID));
            var controls = control_ids.Select(x => Control.Make(_handle, x));
            _controls = controls.Where(x => x != null).ToArray()!;
        }

        public IReadOnlyList<Control> Controls => _controls;

        private bool _zoomChanged;
        private bool _zoom;

        public bool Zoom
        {
            get => _zoom;
            set
            {
                _zoomChanged = true;
                _zoom = value;
            }
        }

        private bool _binChanged;
        private bool _bin;

        public bool Bin
        {
            get => _bin;
            set
            {
                _binChanged = true;
                _bin = value;
            }
        }

        private void Init()
        {
            uint bpp = 0;
            double chipWidth = 0, chipHeight = 0, pixelWidth = 0, pixelHeight = 0;
            Check(QhyCcdDll.SetQHYCCDStreamMode(_handle, _useLive ? 1U : 0U)); // 0 == single, 1 == stream
            Check(QhyCcdDll.InitQHYCCD(_handle));
            Check(QhyCcdDll.GetQHYCCDChipInfo(_handle, ref chipWidth, ref chipHeight, ref _originalWidth, ref _originalHeight, ref pixelWidth, ref pixelHeight, ref bpp));
            Console.WriteLine($"chip name: {_name}, chip width: {chipWidth}, chip height: {chipHeight}, image width: {_originalWidth}, image height: {_originalHeight}, pixel width: {pixelWidth}, pixel height: {pixelHeight}, bits per pixel: {bpp}");
            Check(QhyCcdDll.IsQHYCCDControlAvailable(_handle, CONTROL_ID.CONTROL_TRANSFERBIT));
            Check(QhyCcdDll.SetQHYCCDBitsMode(_handle, 16));
            Check(QhyCcdDll.SetQHYCCDBinMode(_handle, 1, 1));
            Check(QhyCcdDll.SetQHYCCDResolution(_handle, 0, 0, _originalWidth, _originalHeight));
        }

        private void ExpSingleFrame()
        {
            var res = QhyCcdDll.ExpQHYCCDSingleFrame(_handle);
            if (res == 0x2001)
            {
                // QHYCCD_READ_DIRECTLY = 0x2001
                return;
            }
            Check(res);
        }

        public void StartExposure()
        {
            if (_useLive)
            {
                Check(QhyCcdDll.BeginQHYCCDLive(_handle));
            }
            else
            {
                ExpSingleFrame();
            }
        }

        private void DoZoomAdjustment()
        {
            if (_zoomChanged || _binChanged)
            {
                Console.WriteLine("Changing zoom and/or bin");
                var bin = _bin ? 2u : 1;
                if (_zoom)
                {
                    var zoom_amount = CameraDisplay.ZOOM_AMOUNT / bin;
                    var widthZoom = _originalWidth / (bin * zoom_amount);
                    var heightZoom = _originalHeight / (bin * zoom_amount);
                    var xstart = _originalWidth / (bin * 2) - widthZoom / 2;
                    var ystart = _originalHeight / (bin * 2) - heightZoom / 2;
                    Check(QhyCcdDll.SetQHYCCDResolution(_handle, xstart, ystart, widthZoom, heightZoom));
                    Check(QhyCcdDll.SetQHYCCDBinMode(_handle, bin, bin));
                }
                else
                {
                    var width = _originalWidth / bin;
                    var height = _originalHeight / bin;
                    Check(QhyCcdDll.SetQHYCCDBinMode(_handle, bin, bin));
                    Check(QhyCcdDll.SetQHYCCDResolution(_handle, 0, 0, width, height));
                }
                _zoomChanged = false;
                _binChanged = false;
            }
        }

        public Frame GetExposure()
        {
            uint width = 0, height = 0, bpp = 0, channels = 0;
            if (_useLive)
            {
                DoZoomAdjustment();
            }

            var mem_len = QhyCcdDll.GetQHYCCDMemLength(_handle);
            if (_imgdataByte == null || _imgdataByte.Length != mem_len)
            {
                _imgdataByte = new byte[mem_len];
            }
            if (_useLive)
            {
                while (true)
                {
                    var res = QhyCcdDll.GetQHYCCDLiveFrame(_handle, ref width, ref height, ref bpp, ref channels, _imgdataByte);
                    if (res == uint.MaxValue)
                    {
                        continue;
                    }
                    else if (res == 0)
                    {
                        break;
                    }
                    else
                    {
                        throw new Exception($"Unknown error in GetQHYCCDLiveFrame: {res}");
                    }
                }
            }
            else
            {
                Check(QhyCcdDll.GetQHYCCDSingleFrame(_handle, ref width, ref height, ref bpp, ref channels, _imgdataByte));
                DoZoomAdjustment();
                ExpSingleFrame();
            }

            if (_imgdataShort == null || _imgdataShort.Length != width * height)
            {
                _imgdataShort = new ushort[width * height];
            }
            Buffer.BlockCopy(_imgdataByte, 0, _imgdataShort, 0, (int)width * (int)height * 2);
            return new Frame(_imgdataShort, width, height);
        }

        public void StopExposure()
        {
            if (_useLive)
            {
                Check(QhyCcdDll.StopQHYCCDLive(_handle));
            }
            else
            {
                Check(QhyCcdDll.CancelQHYCCDExposing(_handle));
            }
        }

        public void Dispose() => Check(QhyCcdDll.CloseQHYCCD(_handle));
    }

    class Control
    {
        private readonly IntPtr _cameraHandle;
        private readonly double _min;
        private readonly double _max;
        private readonly double _step;

        private Control(IntPtr cameraHandle, CONTROL_ID id)
        {
            _cameraHandle = cameraHandle;
            Id = id;
            QhyCcdDll.GetQHYCCDParamMinMaxStep(cameraHandle, id, ref _min, ref _max, ref _step);
        }

        public CONTROL_ID Id { get; }
        public string Name => Id.ToString().ToLower();

        public double Value
        {
            get => QhyCcdDll.GetQHYCCDParam(_cameraHandle, Id);
            set => QhyCcd.Check(QhyCcdDll.SetQHYCCDParam(_cameraHandle, Id, value));
        }

        public static Control? Make(IntPtr cameraHandle, CONTROL_ID id)
        {
            var okay = QhyCcdDll.IsQHYCCDControlAvailable(cameraHandle, id);
            return okay == 0 ? new Control(cameraHandle, id) : null;
        }

        public override string ToString() => $"{Name} = {Value} ({_min}-{_max} by {_step})";
    }

    public enum CONTROL_ID
    {
        CONTROL_BRIGHTNESS = 0, //!< image brightness
        CONTROL_CONTRAST,       //!< image contrast
        CONTROL_WBR,            //!< red of white balance
        CONTROL_WBB,            //!< blue of white balance
        CONTROL_WBG,            //!< the green of white balance
        CONTROL_GAMMA,          //!< screen gamma
        CONTROL_GAIN,           //!< camera gain
        CONTROL_OFFSET,         //!< camera offset
        CONTROL_EXPOSURE,       //!< expose time (us)
        CONTROL_SPEED,          //!< transfer speed
        CONTROL_TRANSFERBIT,    //!< image depth bits
        CONTROL_CHANNELS,       //!< image channels
        CONTROL_USBTRAFFIC,     //!< hblank
        CONTROL_ROWNOISERE,     //!< row denoise
        CONTROL_CURTEMP,        //!< current cmos or ccd temprature
        CONTROL_CURPWM,         //!< current cool pwm
        CONTROL_MANULPWM,       //!< set the cool pwm
        CONTROL_CFWPORT,        //!< control camera color filter wheel port
        CONTROL_COOLER,         //!< check if camera has cooler
        CONTROL_ST4PORT,        //!< check if camera has st4port
        CAM_COLOR,
        CAM_BIN1X1MODE,         //!< check if camera has bin1x1 mode
        CAM_BIN2X2MODE,         //!< check if camera has bin2x2 mode
        CAM_BIN3X3MODE,         //!< check if camera has bin3x3 mode
        CAM_BIN4X4MODE,         //!< check if camera has bin4x4 mode
        CAM_MECHANICALSHUTTER,                   //!< mechanical shutter
        CAM_TRIGER_INTERFACE,                    //!< triger
        CAM_TECOVERPROTECT_INTERFACE,            //!< tec overprotect
        CAM_SINGNALCLAMP_INTERFACE,              //!< singnal clamp
        CAM_FINETONE_INTERFACE,                  //!< fine tone
        CAM_SHUTTERMOTORHEATING_INTERFACE,       //!< shutter motor heating
        CAM_CALIBRATEFPN_INTERFACE,              //!< calibrated frame
        CAM_CHIPTEMPERATURESENSOR_INTERFACE,     //!< chip temperaure sensor
        CAM_USBREADOUTSLOWEST_INTERFACE,         //!< usb readout slowest

        CAM_8BITS,                               //!< 8bit depth
        CAM_16BITS,                              //!< 16bit depth
        CAM_GPS,                                 //!< check if camera has gps

        CAM_IGNOREOVERSCAN_INTERFACE,            //!< ignore overscan area

        QHYCCD_3A_AUTOBALANCE,
        QHYCCD_3A_AUTOEXPOSURE,
        QHYCCD_3A_AUTOFOCUS,
        CONTROL_AMPV,                            //!< ccd or cmos ampv
        CONTROL_VCAM,                            //!< Virtual Camera on off
        CAM_VIEW_MODE,

        CONTROL_CFWSLOTSNUM,         //!< check CFW slots number
        IS_EXPOSING_DONE,
        ScreenStretchB,
        ScreenStretchW,
        CONTROL_DDR,
        CAM_LIGHT_PERFORMANCE_MODE,

        CAM_QHY5II_GUIDE_MODE,
        DDR_BUFFER_CAPACITY,
        DDR_BUFFER_READ_THRESHOLD
    }

    public enum BAYER_ID
    {
        BAYER_GB = 1,
        BAYER_GR,
        BAYER_BG,
        BAYER_RG
    }

    static class QhyCcdDll
    {
        private const string DLL_NAME = "qhyccd_x64.dll";

        [DllImport(DLL_NAME, EntryPoint = "InitQHYCCDResource",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint InitQHYCCDResource();

        [DllImport(DLL_NAME, EntryPoint = "ReleaseQHYCCDResource",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ReleaseQHYCCDResource();

        [DllImport(DLL_NAME, EntryPoint = "ScanQHYCCD",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ScanQHYCCD();

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDId",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDId(int index, StringBuilder id);

        [DllImport(DLL_NAME, EntryPoint = "OpenQHYCCD",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr OpenQHYCCD(StringBuilder id);

        [DllImport(DLL_NAME, EntryPoint = "InitQHYCCD",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint InitQHYCCD(IntPtr handle);

        [DllImport(DLL_NAME, EntryPoint = "CloseQHYCCD",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint CloseQHYCCD(IntPtr handle);

        [DllImport(DLL_NAME, EntryPoint = "SetQHYCCDBinMode",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDBinMode(IntPtr handle, uint wbin, uint hbin);

        [DllImport(DLL_NAME, EntryPoint = "SetQHYCCDParam",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDParam(IntPtr handle, CONTROL_ID controlid, double value);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDMemLength",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDMemLength(IntPtr handle);

        [DllImport(DLL_NAME, EntryPoint = "ExpQHYCCDSingleFrame",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ExpQHYCCDSingleFrame(IntPtr handle);

        [DllImport(DLL_NAME, EntryPoint = "CancelQHYCCDExposing",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint CancelQHYCCDExposing(IntPtr handle);

        [DllImport(DLL_NAME, EntryPoint = "CancelQHYCCDExposingAndReadout",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint CancelQHYCCDExposingAndReadout(IntPtr handle);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDSingleFrame",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDSingleFrame(IntPtr handle, ref uint w, ref uint h, ref uint bpp, ref uint channels, [Out] byte[] rawArray);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDChipInfo",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDChipInfo(IntPtr handle, ref double chipw, ref double chiph, ref uint imagew, ref uint imageh, ref double pixelw, ref double pixelh, ref uint bpp);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDOverScanArea",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDOverScanArea(IntPtr handle, ref uint startx, ref uint starty, ref uint sizex, ref uint sizey);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDEffectiveArea",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDEffectiveArea(IntPtr handle, ref uint startx, ref uint starty, ref uint sizex, ref uint sizey);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDFWVersion",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDFWVersion(IntPtr handle, [Out] byte[] verBuf);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDParam",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern double GetQHYCCDParam(IntPtr handle, CONTROL_ID controlid);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDParamMinMaxStep",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDParamMinMaxStep(IntPtr handle, CONTROL_ID controlid, ref double min, ref double max, ref double step);

        [DllImport(DLL_NAME, EntryPoint = "ControlQHYCCDGuide",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ControlQHYCCDGuide(IntPtr handle, byte Direction, ushort PulseTime);

        [DllImport(DLL_NAME, EntryPoint = "ControlQHYCCDTemp",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ControlQHYCCDTemp(IntPtr handle, double targettemp);

        [DllImport(DLL_NAME, EntryPoint = "SendOrder2QHYCCDCFW",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SendOrder2QHYCCDCFW(IntPtr handle, string order, int length);

        [DllImport(DLL_NAME, EntryPoint = "IsQHYCCDControlAvailable",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint IsQHYCCDControlAvailable(IntPtr handle, CONTROL_ID controlid);

        [DllImport(DLL_NAME, EntryPoint = "ControlQHYCCDShutter",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ControlQHYCCDShutter(IntPtr handle, byte targettemp);

        [DllImport(DLL_NAME, EntryPoint = "SetQHYCCDResolution",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDResolution(IntPtr handle, uint startx, uint starty, uint sizex, uint sizey);

        [DllImport(DLL_NAME, EntryPoint = "SetQHYCCDStreamMode",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDStreamMode(IntPtr handle, uint mode);

        //EXPORTFUNC uint32_t STDCALL GetQHYCCDCFWStatus(qhyccd_handle *handle,char *status)
        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDCFWStatus",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDCFWStatus(IntPtr handle, StringBuilder cfwStatus);

        [DllImport(DLL_NAME, EntryPoint = "SetQHYCCDBitsMode",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDBitsMode(IntPtr handle, uint bits);

        [DllImport(DLL_NAME, EntryPoint = "BeginQHYCCDLive",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint BeginQHYCCDLive(IntPtr handle);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDLiveFrame",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDLiveFrame(IntPtr handle, ref uint w, ref uint h, ref uint bpp, ref uint channels, [In, Out] byte[] imgdata);

        [DllImport(DLL_NAME, EntryPoint = "StopQHYCCDLive",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint StopQHYCCDLive(IntPtr handle);

        [DllImport(DLL_NAME, EntryPoint = "SetQHYCCDDebayerOnOff",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDDebayerOnOff(IntPtr handle, bool onoff);

        [DllImport(DLL_NAME, EntryPoint = "GetQHYCCDSDKVersion",
           CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDSDKVersion(ref uint year, ref uint month, ref uint day, ref uint subday);
    }
}
