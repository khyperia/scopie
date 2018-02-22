using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
#if LINUX
using LongC = System.IntPtr;
#elif WINDOWS
using LongC = int;
#else
#error Unknown platform (LINUX or WINDOWS not defined)
#endif

namespace Scopie
{
    public static class ASICameraDll
    {
        const string DllName = "ASICamera2";

        public enum ASI_CONTROL_TYPE
        {
            ASI_GAIN = 0,
            ASI_EXPOSURE,
            ASI_GAMMA,
            ASI_WB_R,
            ASI_WB_B,
            ASI_OFFSET,
            ASI_BANDWIDTHOVERLOAD,
            ASI_OVERCLOCK,
            ASI_TEMPERATURE,// return 10*temperature
            ASI_FLIP,
            ASI_AUTO_MAX_GAIN,
            ASI_AUTO_MAX_EXP,
            ASI_AUTO_MAX_BRIGHTNESS,
            ASI_HARDWARE_BIN,
            ASI_HIGH_SPEED_MODE,
            ASI_COOLER_POWER_PERC,
            ASI_TARGET_TEMP,// not need *10
            ASI_COOLER_ON,
            ASI_MONO_BIN,
            ASI_FAN_ON,
            ASI_PATTERN_ADJUST,
            ASI_ANTI_DEW_HEATER
        }

        public enum ASI_IMG_TYPE
        {
            //Supported image type
            ASI_IMG_RAW8 = 0,
            ASI_IMG_RGB24,
            ASI_IMG_RAW16,
            ASI_IMG_Y8,
            ASI_IMG_END = -1
        }

        public enum ASI_GUIDE_DIRECTION
        {
            ASI_GUIDE_NORTH = 0,
            ASI_GUIDE_SOUTH,
            ASI_GUIDE_EAST,
            ASI_GUIDE_WEST
        }

        public enum ASI_BAYER_PATTERN
        {
            ASI_BAYER_RG = 0,
            ASI_BAYER_BG,
            ASI_BAYER_GR,
            ASI_BAYER_GB
        }

        public enum ASI_EXPOSURE_STATUS
        {
            ASI_EXP_IDLE = 0,//: idle states, you can start exposure now
            ASI_EXP_WORKING,//: exposing
            ASI_EXP_SUCCESS,// exposure finished and waiting for download
            ASI_EXP_FAILED,//:exposure failed, you need to start exposure again
        }

        public enum ASI_ERROR_CODE
        { //ASI ERROR CODE
            ASI_SUCCESS = 0,
            ASI_ERROR_INVALID_INDEX, //no camera connected or index value out of boundary
            ASI_ERROR_INVALID_ID, //invalid ID
            ASI_ERROR_INVALID_CONTROL_TYPE, //invalid control type
            ASI_ERROR_CAMERA_CLOSED, //camera didn't open
            ASI_ERROR_CAMERA_REMOVED, //failed to find the camera, maybe the camera has been removed
            ASI_ERROR_INVALID_PATH, //cannot find the path of the file
            ASI_ERROR_INVALID_FILEFORMAT,
            ASI_ERROR_INVALID_SIZE, //wrong video format size
            ASI_ERROR_INVALID_IMGTYPE, //unsupported image formate
            ASI_ERROR_OUTOF_BOUNDARY, //the startpos is out of boundary
            ASI_ERROR_TIMEOUT, //timeout
            ASI_ERROR_INVALID_SEQUENCE,//stop capture first
            ASI_ERROR_BUFFER_TOO_SMALL, //buffer size is not big enough
            ASI_ERROR_VIDEO_MODE_ACTIVE,
            ASI_ERROR_EXPOSURE_IN_PROGRESS,
            ASI_ERROR_GENERAL_ERROR,//general error, eg: value is out of valid range
            ASI_ERROR_END
        }

        public enum ASI_BOOL
        {
            ASI_FALSE = 0,
            ASI_TRUE
        }

        public enum ASI_FLIP_STATUS
        {
            ASI_FLIP_NONE = 0,//: original
            ASI_FLIP_HORIZ,//: horizontal flip
            ASI_FLIP_VERT,// vertical flip
            ASI_FLIP_BOTH,//:both horizontal and vertical flip
        }

        public struct ASI_CAMERA_INFO
        {
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 64)]
            private byte[] _name; //the name of the camera, you can display this to the UI
            public int CameraID; //this is used to control everything of the camera in other functions
            public LongC MaxHeight; //the max height of the camera
            public LongC MaxWidth; //the max width of the camera
            public ASI_BOOL IsColorCam;
            public ASI_BAYER_PATTERN BayerPattern;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            public int[] SupportedBins; //1 means bin1 which is supported by every camera, 2 means bin 2 etc.. 0 is the end of supported binning method
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public ASI_IMG_TYPE[] SupportedVideoFormat;// ASI_IMG_TYPE[8]; //this array will content with the support output format type.IMG_END is the end of supported video format
            public double PixelSize; //the pixel size of the camera, unit is um. such like 5.6um
            public ASI_BOOL MechanicalShutter;
            public ASI_BOOL ST4Port;
            public ASI_BOOL IsCoolerCam;
            public ASI_BOOL IsUSB3Host;
            public ASI_BOOL IsUSB3Camera;
            public float ElecPerADU;
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 24)]
            public byte[] Unused;
            public string Name => Encoding.ASCII.GetString(_name).TrimEnd((char)0);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ASI_CONTROL_CAPS
        {
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 64)]
            private byte[] _name; //the name of the Control like Exposure, Gain etc..
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 128)]
            private byte[] _description; //description of this control
            public LongC MaxValue;
            public LongC MinValue;
            public LongC DefaultValue;
            public ASI_BOOL IsAutoSupported; //support auto set 1, don't support 0
            public ASI_BOOL IsWritable; //some control like temperature can only be read by some cameras 
            public ASI_CONTROL_TYPE ControlType;//this is used to get value and set value of the control
            [MarshalAs(UnmanagedType.ByValArray, ArraySubType = UnmanagedType.U1, SizeConst = 32)]
            public byte[] Unused;
            public string Name => Encoding.ASCII.GetString(_name).TrimEnd((char)0); public string Description => Encoding.ASCII.GetString(_description).TrimEnd((char)0);
        }

        public enum ExposureStatus
        {
            ExpIdle = 0, //: idle states, you can start exposure now
            ExpWorking, //: exposing
            ExpSuccess, // exposure finished and waiting for download
            ExpFailed, //:exposure failed, you need to start exposure again
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ASIGetNumOfConnectedCameras")]
        public static extern int GetNumOfConnectedCameras();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetCameraProperty(out ASI_CAMERA_INFO pASICameraInfo, int iCameraIndex);
        public static ASI_CAMERA_INFO GetCameraProperties(int cameraIndex)
        {
            CheckReturn(ASIGetCameraProperty(out var result, cameraIndex), cameraIndex);
            return result;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIOpenCamera(int iCameraID);
        public static void OpenCamera(int cameraId) => CheckReturn(ASIOpenCamera(cameraId), cameraId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIInitCamera(int iCameraID);
        public static void InitCamera(int cameraId) => CheckReturn(ASIInitCamera(cameraId), cameraId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASICloseCamera(int iCameraID);
        public static void CloseCamera(int cameraId) => CheckReturn(ASICloseCamera(cameraId), cameraId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetNumOfControls(int iCameraID, out int piNumberOfControls);
        public static int GetNumOfControls(int cameraId)
        {
            CheckReturn(ASIGetNumOfControls(cameraId, out var result), cameraId);
            return result;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetControlCaps(int iCameraID, int iControlIndex, out ASI_CONTROL_CAPS pControlCaps);
        public static ASI_CONTROL_CAPS GetControlCaps(int cameraId, int controlIndex)
        {
            CheckReturn(ASIGetControlCaps(cameraId, controlIndex, out var result), cameraId, controlIndex);
            return result;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetControlValue(int iCameraID, ASI_CONTROL_TYPE controlType, out LongC plValue, out ASI_BOOL pbAuto);
        public static int GetControlValue(int cameraId, ASI_CONTROL_TYPE controlType, out bool isAuto)
        {
            CheckReturn(ASIGetControlValue(cameraId, controlType, out var result, out var auto), cameraId, controlType);
            isAuto = auto != ASI_BOOL.ASI_FALSE;
            return (int)result;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASISetControlValue(int iCameraID, ASI_CONTROL_TYPE controlType, LongC lValue, ASI_BOOL bAuto);
        public static void SetControlValue(int cameraId, ASI_CONTROL_TYPE controlType, int value, bool auto) =>
            CheckReturn(ASISetControlValue(cameraId, controlType, (LongC)value, auto ? ASI_BOOL.ASI_TRUE : ASI_BOOL.ASI_FALSE), cameraId, controlType, value, auto);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASISetROIFormat(int iCameraID, int iWidth, int iHeight, int iBin, ASI_IMG_TYPE Img_type);
        public static void SetROIFormat(int cameraId, int width, int height, int bin, ASI_IMG_TYPE imageType) =>
            CheckReturn(ASISetROIFormat(cameraId, width, height, bin, imageType), cameraId, width, height, bin, imageType);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetROIFormat(int iCameraID, out int piWidth, out int piHeight, out int piBin, out ASI_IMG_TYPE pImg_type);
        public static void GetROIFormat(int cameraId, out int width, out int height, out int bin, out ASI_IMG_TYPE imageType) =>
            CheckReturn(ASIGetROIFormat(cameraId, out width, out height, out bin, out imageType), cameraId, bin);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASISetStartPos(int iCameraID, int iStartX, int iStartY);
        public static void SetStartPos(int cameraId, int x, int y) =>
            CheckReturn(ASISetStartPos(cameraId, x, y), cameraId, x, y);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetStartPos(int iCameraID, out int piStartX, out int piStartY);
        public static void GetStartPos(int cameraId, out int x, out int y) =>
            CheckReturn(ASIGetStartPos(cameraId, out x, out y), cameraId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetDroppedFrames(int iCameraID, out int piDropFrames);
        public static int GetDroppedFrames(int cameraId)
        {
            CheckReturn(ASIGetDroppedFrames(cameraId, out var result), cameraId);
            return result;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIEnableDarkSubtract(int iCameraID, [MarshalAs(UnmanagedType.LPStr)] string pcBMPPath, out ASI_BOOL bIsSubDarkWorking);
        public static bool EnableDarkSubtract(int cameraId, string darkFilePath)
        {
            CheckReturn(ASIEnableDarkSubtract(cameraId, darkFilePath, out var result), cameraId, darkFilePath);
            return result != ASI_BOOL.ASI_FALSE;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIDisableDarkSubtract(int iCameraID);
        public static void DisableDarkSubtract(int cameraId) =>
            CheckReturn(ASIDisableDarkSubtract(cameraId), cameraId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIStartVideoCapture(int iCameraID);
        public static void StartVideoCapture(int cameraId) =>
            CheckReturn(ASIStartVideoCapture(cameraId), cameraId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIStopVideoCapture(int iCameraID);
        public static void StopVideoCapture(int cameraId) =>
            CheckReturn(ASIStopVideoCapture(cameraId), cameraId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetVideoData(int iCameraID, IntPtr pBuffer, int lBuffSize, int iWaitms);
        public static bool GetVideoData(int cameraId, IntPtr buffer, int bufferSize, int waitMs)
        {
            var result = ASIGetVideoData(cameraId, buffer, bufferSize, waitMs);
            if (result == ASI_ERROR_CODE.ASI_ERROR_TIMEOUT)
            {
                return false;
            }
            CheckReturn(result, cameraId, buffer, bufferSize, waitMs);
            return true;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIPulseGuideOn(int iCameraID, ASI_GUIDE_DIRECTION direction);
        public static void PulseGuideOn(int cameraId, ASI_GUIDE_DIRECTION direction) =>
            CheckReturn(ASIPulseGuideOn(cameraId, direction), cameraId, direction);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIPulseGuideOff(int iCameraID, ASI_GUIDE_DIRECTION direction);
        public static void PulseGuideOff(int cameraId, ASI_GUIDE_DIRECTION direction) =>
            CheckReturn(ASIPulseGuideOff(cameraId, direction), cameraId, direction);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIStartExposure(int iCameraID, ASI_BOOL bIsDark);
        public static void StartExposure(int cameraId, bool isDark) =>
            CheckReturn(ASIStartExposure(cameraId, isDark ? ASI_BOOL.ASI_TRUE : ASI_BOOL.ASI_FALSE), cameraId, isDark);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIStopExposure(int iCameraID);
        public static void StopExposure(int cameraId) =>
            CheckReturn(ASIStopExposure(cameraId), cameraId);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetExpStatus(int iCameraID, out ExposureStatus pExpStatus);
        public static ExposureStatus GetExposureStatus(int cameraId)
        {
            CheckReturn(ASIGetExpStatus(cameraId, out var result), cameraId);
            return result;
        }

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        private static extern ASI_ERROR_CODE ASIGetDataAfterExp(int iCameraID, [Out] ushort[] pBuffer, int lBuffSize);
        public static bool GetDataAfterExp(int cameraId, ushort[] buffer)
        {
            var result = ASIGetDataAfterExp(cameraId, buffer, buffer.Length * sizeof(ushort));
            if (result == ASI_ERROR_CODE.ASI_ERROR_TIMEOUT)
            {
                return false;
            }
            CheckReturn(result, cameraId, buffer);
            return true;
        }

        private static void CheckReturn<T1>(ASI_ERROR_CODE errorCode, T1 p1, [CallerMemberName] string funcName = "")
        {
            if (errorCode == ASI_ERROR_CODE.ASI_SUCCESS)
            {
                return;
            }
            if (errorCode < ASI_ERROR_CODE.ASI_ERROR_END)
            {
                throw new ASICameraException(errorCode, funcName, p1);
            }
            else
            {
                throw new ArgumentOutOfRangeException("errorCode");
            }
        }

        private static void CheckReturn<T1, T2>(ASI_ERROR_CODE errorCode, T1 p1, T2 p2, [CallerMemberName] string funcName = "")
        {
            if (errorCode == ASI_ERROR_CODE.ASI_SUCCESS)
            {
                return;
            }
            if (errorCode < ASI_ERROR_CODE.ASI_ERROR_END)
            {
                throw new ASICameraException(errorCode, funcName, p1, p2);
            }
            else
            {
                throw new ArgumentOutOfRangeException("errorCode");
            }
        }

        private static void CheckReturn<T1, T2, T3>(ASI_ERROR_CODE errorCode, T1 p1, T2 p2, T3 p3, [CallerMemberName] string funcName = "")
        {
            if (errorCode == ASI_ERROR_CODE.ASI_SUCCESS)
            {
                return;
            }
            if (errorCode < ASI_ERROR_CODE.ASI_ERROR_END)
            {
                throw new ASICameraException(errorCode, funcName, p1, p2, p3);
            }
            else
            {
                throw new ArgumentOutOfRangeException("errorCode");
            }
        }

        private static void CheckReturn<T1, T2, T3, T4>(ASI_ERROR_CODE errorCode, T1 p1, T2 p2, T3 p3, T4 p4, [CallerMemberName] string funcName = "")
        {
            if (errorCode == ASI_ERROR_CODE.ASI_SUCCESS)
            {
                return;
            }
            if (errorCode < ASI_ERROR_CODE.ASI_ERROR_END)
            {
                throw new ASICameraException(errorCode, funcName, p1, p2, p3, p4);
            }
            else
            {
                throw new ArgumentOutOfRangeException("errorCode");
            }
        }

        private static void CheckReturn<T1, T2, T3, T4, T5>(ASI_ERROR_CODE errorCode, T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, [CallerMemberName] string funcName = "")
        {
            if (errorCode == ASI_ERROR_CODE.ASI_SUCCESS)
            {
                return;
            }
            if (errorCode < ASI_ERROR_CODE.ASI_ERROR_END)
            {
                throw new ASICameraException(errorCode, funcName, p1, p2, p3, p4, p5);
            }
            else
            {
                throw new ArgumentOutOfRangeException("errorCode");
            }
        }
    }

    public class ASICameraException : Exception
    {
        public ASICameraException(ASICameraDll.ASI_ERROR_CODE errorCode, string funcName, params object[] parameters)
            : base($"{funcName}({string.Join(", ", parameters)}) -> '{errorCode}'")
        {
        }
    }
}
