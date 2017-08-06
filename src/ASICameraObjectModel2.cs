using Eto.Forms;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Scopie
{
    public class Camera : IDisposable
    {
        public static int NumCameras => ASICameraDll.GetNumOfConnectedCameras();

        private readonly ASICameraDll.ASI_CAMERA_INFO _info;
        private readonly string _cachedName;
        private readonly List<CameraControl> _controls;

        public Camera(int cameraIndex)
        {
            _info = ASICameraDll.GetCameraProperties(cameraIndex);
            _cachedName = _info.Name;
            ASICameraDll.OpenCamera(CameraId);
            ASICameraDll.InitCamera(CameraId);

            ASICameraDll.SetROIFormat(CameraId, Width, Height, 1, ASICameraDll.ASI_IMG_TYPE.ASI_IMG_RAW16);

            var cc = ASICameraDll.GetNumOfControls(Info.CameraID);
            _controls = new List<CameraControl>();
            for (var i = 0; i < cc; i++)
            {
                _controls.Add(new CameraControl(Info.CameraID, i));
            }
        }

        public void Dispose() => ASICameraDll.CloseCamera(Info.CameraID);

        private ASICameraDll.ASI_CAMERA_INFO Info => _info;
        public string Name => _cachedName;
        public bool IsColor => Info.IsColorCam != ASICameraDll.ASI_BOOL.ASI_FALSE;
        public bool HasST4 => Info.ST4Port != ASICameraDll.ASI_BOOL.ASI_FALSE;
        public bool HasShutter => Info.MechanicalShutter != ASICameraDll.ASI_BOOL.ASI_FALSE;
        public bool HasCooler => Info.IsCoolerCam != ASICameraDll.ASI_BOOL.ASI_FALSE;
        public bool IsUSB3 => Info.IsUSB3Host != ASICameraDll.ASI_BOOL.ASI_FALSE;
        public int CameraId => Info.CameraID;
        public ASICameraDll.ASI_BAYER_PATTERN BayerPattern => Info.BayerPattern;
        public int Width => Info.MaxWidth;
        public int Height => Info.MaxHeight;
        public double PixelSize => Info.PixelSize;
        public List<int> SupportedBinFactors => Info.SupportedBins.TakeWhile(x => x != 0).ToList();
        public List<ASICameraDll.ASI_IMG_TYPE> SupportedImageTypes => Info.SupportedVideoFormat.TakeWhile(x => x != ASICameraDll.ASI_IMG_TYPE.ASI_IMG_END).ToList();
        public ASICameraDll.ExposureStatus ExposureStatus => ASICameraDll.GetExposureStatus(Info.CameraID);
        public List<CameraControl> Controls => _controls;
        public int DroppedFrames => ASICameraDll.GetDroppedFrames(Info.CameraID);
        public bool EnableDarkSubtract(string darkImageFilePath) => ASICameraDll.EnableDarkSubtract(Info.CameraID, darkImageFilePath);
        public void DisableDarkSubtract() => ASICameraDll.DisableDarkSubtract(Info.CameraID);
        public void StartVideoCapture() => ASICameraDll.StartVideoCapture(Info.CameraID);
        public void StopVideoCapture() => ASICameraDll.StopVideoCapture(Info.CameraID);
        public bool GetVideoData(IntPtr buffer, int bufferSize, int waitMs) => ASICameraDll.GetVideoData(Info.CameraID, buffer, bufferSize, waitMs);
        public void PulseGuideOn(ASICameraDll.ASI_GUIDE_DIRECTION direction) => ASICameraDll.PulseGuideOn(Info.CameraID, direction);
        public void PulseGuideOff(ASICameraDll.ASI_GUIDE_DIRECTION direction) => ASICameraDll.PulseGuideOff(Info.CameraID, direction);
        public void StartExposure(bool isDark) => ASICameraDll.StartExposure(Info.CameraID, isDark);
        public void StopExposure() => ASICameraDll.StopExposure(Info.CameraID);
        public bool GetExposureData(ushort[] buffer) => ASICameraDll.GetDataAfterExp(Info.CameraID, buffer);
        public CameraControl GetControl(ASICameraDll.ASI_CONTROL_TYPE controlType) => Controls.FirstOrDefault(x => x.ControlType == controlType);

        // public Point StartPos
        // {
        //     get { return ASICameraDll.GetStartPos(_cameraId); }
        //     set { ASICameraDll.SetStartPos(_cameraId, value); }
        // }

        public CaptureAreaInfo CaptureAreaInfo
        {
            get
            {
                ASICameraDll.GetROIFormat(Info.CameraID, out int width, out int height, out int bin, out ASICameraDll.ASI_IMG_TYPE imageType);
                return new CaptureAreaInfo(width, height, bin, imageType);
            }
            set => ASICameraDll.SetROIFormat(Info.CameraID, value.Width, value.Height, value.Binning, value.ImageType);
        }
    }

    public class CaptureAreaInfo
    {
        public int Width
        {
            get; set;
        }
        public int Height
        {
            get; set;
        }
        public int Binning
        {
            get; set;
        }
        public ASICameraDll.ASI_IMG_TYPE ImageType
        {
            get; set;
        }

        public CaptureAreaInfo(int width, int height, int binning, ASICameraDll.ASI_IMG_TYPE imageType)
        {
            Width = width;
            Height = height;
            Binning = binning;
            ImageType = imageType;
        }
    }

    public class CameraControl
    {
        private readonly int _cameraId;
        private ASICameraDll.ASI_CONTROL_CAPS _props;
        private bool _isAuto;

        public CameraControl(int cameraId, int controlIndex)
        {
            _cameraId = cameraId;
            _props = ASICameraDll.GetControlCaps(_cameraId, controlIndex);

            // ugh
            if (ControlType == ASICameraDll.ASI_CONTROL_TYPE.ASI_HIGH_SPEED_MODE)
            {
                //Value = 1;
            }
        }

        public string Name => _props.Name;
        public string Description => _props.Description;
        public int MinValue => _props.MinValue;
        public int MaxValue => _props.MaxValue;
        public int DefaultValue => _props.DefaultValue;
        public ASICameraDll.ASI_CONTROL_TYPE ControlType => _props.ControlType;
        public bool IsAutoAvailable => _props.IsAutoSupported != ASICameraDll.ASI_BOOL.ASI_FALSE;
        public bool Writeable => _props.IsWritable != ASICameraDll.ASI_BOOL.ASI_FALSE;
        public int Value
        {
            get => ASICameraDll.GetControlValue(_cameraId, _props.ControlType, out _isAuto);
            set
            {
                var changed = Value != value;
                ASICameraDll.SetControlValue(_cameraId, _props.ControlType, value, _isAuto);
                if (changed)
                {
                    if (Program.IsMainThread)
                    {
                        ValueChanged?.Invoke();
                    }
                    else if (ValueChanged != null)
                    {
                        Application.Instance.AsyncInvoke(ValueChanged);
                    }
                }
            }
        }

        public event Action ValueChanged;
    }
}
