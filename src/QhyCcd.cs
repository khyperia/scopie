using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Scopie
{
    class QhyCcd : IDisposable
    {
        private readonly string _name;
        private readonly IntPtr _handle;
        private readonly Control[] _controls;
        private readonly bool _useLive;
        private double _chipWidth;
        private double _chipHeight;
        private uint _width;
        private uint _height;
        private double _pixelWidth;
        private double _pixelHeight;
        private uint _bpp;
        private uint _channels = 1;

        internal int Width => (int)_width;
        internal int Height => (int)_height;

        private static void Check(uint result, [CallerMemberName] string name = "", [CallerFilePath] string filePath = "", [CallerLineNumber] int line = 0)
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
            _controls = ((CONTROL_ID[])Enum.GetValues(typeof(CONTROL_ID))).Select(x => Control.Make(_handle, x)).Where(x => x != null).ToArray();
        }

        public IReadOnlyList<Control> Controls => _controls;

        private void Init()
        {
            Check(QhyCcdDll.SetQHYCCDStreamMode(_handle, _useLive ? 1U : 0U)); // 0 == single, 1 == stream
            Check(QhyCcdDll.InitQHYCCD(_handle));
            Check(QhyCcdDll.GetQHYCCDChipInfo(_handle, ref _chipWidth, ref _chipHeight, ref _width, ref _height, ref _pixelWidth, ref _pixelHeight, ref _bpp));
            Console.WriteLine($"chip name: {_name}, chip width: {_chipWidth}, chip height: {_chipHeight}, image width: {_width}, image height: {_height}, pixel width: {_pixelWidth}, pixel height: {_pixelHeight}, bits per pixel: {_bpp}");
            Check(QhyCcdDll.IsQHYCCDControlAvailable(_handle, CONTROL_ID.CONTROL_TRANSFERBIT));
            Check(QhyCcdDll.SetQHYCCDBitsMode(_handle, 16));
            _bpp = 16;
            Check(QhyCcdDll.SetQHYCCDBinMode(_handle, 1, 1));
            Check(QhyCcdDll.SetQHYCCDResolution(_handle, 0, 0, _width, _height));
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

        public bool GetExposure(ref byte[] imgdata)
        {
            if (imgdata == null)
            {
                var mem_len = QhyCcdDll.GetQHYCCDMemLength(_handle);
                imgdata = new byte[mem_len];
            }
            if (_useLive)
            {
                var res = QhyCcdDll.GetQHYCCDLiveFrame(_handle, ref _width, ref _height, ref _bpp, ref _channels, imgdata);
                if (res == uint.MaxValue)
                {
                    return false;
                }
                else if (res == 0)
                {
                    return true;
                }
                else
                {
                    throw new Exception($"Unknown error in GetQHYCCDLiveFrame: {res}");
                }
            }
            else
            {
                Check(QhyCcdDll.GetQHYCCDSingleFrame(_handle, ref _width, ref _height, ref _bpp, ref _channels, imgdata));
                ExpSingleFrame();
                return true;
            }
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
        private readonly CONTROL_ID _id;
        private readonly double _min;
        private readonly double _max;
        private readonly double _step;

        private Control(IntPtr cameraHandle, CONTROL_ID id)
        {
            _cameraHandle = cameraHandle;
            _id = id;
            QhyCcdDll.GetQHYCCDParamMinMaxStep(cameraHandle, id, ref _min, ref _max, ref _step);
        }

        public string Name => _id.ToString().ToLower();

        public double Value
        {
            get => QhyCcdDll.GetQHYCCDParam(_cameraHandle, _id);
            set => QhyCcdDll.SetQHYCCDParam(_cameraHandle, _id, value);
        }

        public static Control Make(IntPtr cameraHandle, CONTROL_ID id)
        {
            var okay = QhyCcdDll.IsQHYCCDControlAvailable(cameraHandle, id);
            if (okay == 0)
            {
                return new Control(cameraHandle, id);
            }
            else
            {
                return null;
            }
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
        private const string _dllName = "qhyccd_x64.dll";

        [DllImport(_dllName, EntryPoint = "InitQHYCCDResource",
            CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint InitQHYCCDResource();

        [DllImport(_dllName, EntryPoint = "ReleaseQHYCCDResource",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ReleaseQHYCCDResource();

        [DllImport(_dllName, EntryPoint = "ScanQHYCCD",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ScanQHYCCD();

        [DllImport(_dllName, EntryPoint = "GetQHYCCDId",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDId(int index, StringBuilder id);

        [DllImport(_dllName, EntryPoint = "OpenQHYCCD",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr OpenQHYCCD(StringBuilder id);

        [DllImport(_dllName, EntryPoint = "InitQHYCCD",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint InitQHYCCD(IntPtr handle);

        [DllImport(_dllName, EntryPoint = "CloseQHYCCD",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint CloseQHYCCD(IntPtr handle);

        [DllImport(_dllName, EntryPoint = "SetQHYCCDBinMode",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDBinMode(IntPtr handle, uint wbin, uint hbin);

        [DllImport(_dllName, EntryPoint = "SetQHYCCDParam",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDParam(IntPtr handle, CONTROL_ID controlid, double value);

        [DllImport(_dllName, EntryPoint = "GetQHYCCDMemLength",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDMemLength(IntPtr handle);

        [DllImport(_dllName, EntryPoint = "ExpQHYCCDSingleFrame",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ExpQHYCCDSingleFrame(IntPtr handle);

        [DllImport(_dllName, EntryPoint = "CancelQHYCCDExposing",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint CancelQHYCCDExposing(IntPtr handle);

        [DllImport(_dllName, EntryPoint = "CancelQHYCCDExposingAndReadout",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint CancelQHYCCDExposingAndReadout(IntPtr handle);

        [DllImport(_dllName, EntryPoint = "GetQHYCCDSingleFrame",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDSingleFrame(IntPtr handle, ref uint w, ref uint h, ref uint bpp, ref uint channels, [Out] byte[] rawArray);

        [DllImport(_dllName, EntryPoint = "GetQHYCCDChipInfo",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDChipInfo(IntPtr handle, ref double chipw, ref double chiph, ref uint imagew, ref uint imageh, ref double pixelw, ref double pixelh, ref uint bpp);

        [DllImport(_dllName, EntryPoint = "GetQHYCCDOverScanArea",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDOverScanArea(IntPtr handle, ref uint startx, ref uint starty, ref uint sizex, ref uint sizey);

        [DllImport(_dllName, EntryPoint = "GetQHYCCDEffectiveArea",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDEffectiveArea(IntPtr handle, ref uint startx, ref uint starty, ref uint sizex, ref uint sizey);

        [DllImport(_dllName, EntryPoint = "GetQHYCCDFWVersion",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDFWVersion(IntPtr handle, [Out]byte[] verBuf);

        [DllImport(_dllName, EntryPoint = "GetQHYCCDParam",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern double GetQHYCCDParam(IntPtr handle, CONTROL_ID controlid);

        [DllImport(_dllName, EntryPoint = "GetQHYCCDParamMinMaxStep",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDParamMinMaxStep(IntPtr handle, CONTROL_ID controlid, ref double min, ref double max, ref double step);

        [DllImport(_dllName, EntryPoint = "ControlQHYCCDGuide",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ControlQHYCCDGuide(IntPtr handle, byte Direction, ushort PulseTime);

        [DllImport(_dllName, EntryPoint = "ControlQHYCCDTemp",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ControlQHYCCDTemp(IntPtr handle, double targettemp);

        [DllImport(_dllName, EntryPoint = "SendOrder2QHYCCDCFW",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SendOrder2QHYCCDCFW(IntPtr handle, string order, int length);

        [DllImport(_dllName, EntryPoint = "IsQHYCCDControlAvailable",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint IsQHYCCDControlAvailable(IntPtr handle, CONTROL_ID controlid);

        [DllImport(_dllName, EntryPoint = "ControlQHYCCDShutter",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint ControlQHYCCDShutter(IntPtr handle, byte targettemp);

        [DllImport(_dllName, EntryPoint = "SetQHYCCDResolution",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDResolution(IntPtr handle, uint startx, uint starty, uint sizex, uint sizey);

        [DllImport(_dllName, EntryPoint = "SetQHYCCDStreamMode",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDStreamMode(IntPtr handle, uint mode);

        //EXPORTFUNC uint32_t STDCALL GetQHYCCDCFWStatus(qhyccd_handle *handle,char *status)
        [DllImport(_dllName, EntryPoint = "GetQHYCCDCFWStatus",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDCFWStatus(IntPtr handle, StringBuilder cfwStatus);

        [DllImport(_dllName, EntryPoint = "SetQHYCCDBitsMode",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDBitsMode(IntPtr handle, uint bits);

        [DllImport(_dllName, EntryPoint = "BeginQHYCCDLive",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint BeginQHYCCDLive(IntPtr handle);

        [DllImport(_dllName, EntryPoint = "GetQHYCCDLiveFrame",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint GetQHYCCDLiveFrame(IntPtr handle, ref uint w, ref uint h, ref uint bpp, ref uint channels, [In, Out] byte[] imgdata);

        [DllImport(_dllName, EntryPoint = "StopQHYCCDLive",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint StopQHYCCDLive(IntPtr handle);

        [DllImport(_dllName, EntryPoint = "SetQHYCCDDebayerOnOff",
         CharSet = CharSet.Ansi, CallingConvention = CallingConvention.StdCall)]
        public static extern uint SetQHYCCDDebayerOnOff(IntPtr handle, bool onoff);
    }
}
