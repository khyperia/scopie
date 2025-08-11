using System.Text;
using Avalonia;
using Avalonia.Threading;
using static Scopie.ExceptionReporter;
using static Scopie.LibQhy;

namespace Scopie;

internal record struct ScanResult(string Id, string Model);

internal interface ICamera : IPushEnumerable<DeviceImage>, IDisposable
{
    ScanResult CameraId { get; }
    event Action<List<CameraControlValue>>? OnControlsUpdated;
    bool Exposing { set; }
    Task Init();
    Task<(double chipWidth, double chipHeight, uint imageWidth, uint imageHeight, double pixelWidth, double pixelHeight, uint bitsPerPixel)> GetChipInfoAsync();
    Task<(uint effectiveStartX, uint effectiveStartY, uint effectiveSizeX, uint effectiveSizeY)> GetEffectiveAreaAsync();
    Task<string> GetFastReadoutStatusAsync();
    Task<string> GetSdkVersionAsync();
    Task<string> GetFirmwareVersionAsync();
    Task<string> GetFpgaVersionAsync();
    Task HardwareCrop(PixelRect rect);
    Task ResetHardwareCrop();
    void SetControl(CameraControl control, double value);
}

internal sealed class Camera(ScanResult cameraId) : PushEnumerable<DeviceImage>, ICamera
{
    public ScanResult CameraId => cameraId;
    private readonly Threadling _threadling = new(null);
    private readonly List<CameraControl> _cameraControls = [];
    private IntPtr _qhyHandle;
    private byte[]? _imgBuffer;
    private bool _exposing;

    public event Action<List<CameraControlValue>>? OnControlsUpdated;

    static Camera()
    {
        Check(InitQHYCCDResource());
    }

    public bool Exposing
    {
        set => _threadling.Do(() => _exposing = value);
    }

    public void Dispose()
    {
        _threadling.Dispose();
    }

    public static List<ScanResult> Scan()
    {
        var scan = ScanQHYCCD();
        if (scan > int.MaxValue)
            Check(scan);
        List<ScanResult> result = [];
        StringBuilder sb = new(256);
        for (var i = 0u; i < scan; i++)
        {
            Check(GetQHYCCDId(i, sb));
            var id = sb.ToString();
            Check(GetQHYCCDModel(id, sb));
            var model = sb.ToString();
            result.Add(new ScanResult(id, model));
        }

        return result;
    }

    public async Task Init()
    {
        await _threadling.Do(() => InitCamera(cameraId));
        _threadling.IdleAction = IdleAction;
    }

    public Task<(double chipWidth, double chipHeight, uint imageWidth, uint imageHeight, double pixelWidth, double pixelHeight, uint bitsPerPixel)> GetChipInfoAsync()
    {
        return _threadling.Do(GetChipInfo);
    }

    private (double chipWidth, double chipHeight, uint imageWidth, uint imageHeight, double pixelWidth, double pixelHeight, uint bitsPerPixel) GetChipInfo()
    {
        Check(GetQHYCCDChipInfo(_qhyHandle, out var chipWidth, out var chipHeight, out var imageWidth, out var imageHeight, out var pixelWidth, out var pixelHeight, out var bitsPerPixel));
        return (chipWidth, chipHeight, imageWidth, imageHeight, pixelWidth, pixelHeight, bitsPerPixel);
    }

    public Task<(uint effectiveStartX, uint effectiveStartY, uint effectiveSizeX, uint effectiveSizeY)> GetEffectiveAreaAsync()
    {
        return _threadling.Do(GetEffectiveArea);
    }

    private (uint effectiveStartX, uint effectiveStartY, uint effectiveSizeX, uint effectiveSizeY) GetEffectiveArea()
    {
        Check(GetQHYCCDEffectiveArea(_qhyHandle, out var effectiveStartX, out var effectiveStartY, out var effectiveSizeX, out var effectiveSizeY));
        return (effectiveStartX, effectiveStartY, effectiveSizeX, effectiveSizeY);
    }

    public Task<string> GetFastReadoutStatusAsync()
    {
        return _threadling.Do(GetFastReadoutStatus);
    }

    private string GetFastReadoutStatus()
    {
        var canFastReadout = IsQHYCCDControlAvailable(_qhyHandle, ControlId.ControlSpeed) == 0;
        if (canFastReadout)
        {
            Check(GetQHYCCDParamMinMaxStep(_qhyHandle, ControlId.ControlSpeed, out var min, out var max, out var step));
            return $"camera supports fast readout at speeds: {min}-{max} (step={step})";
        }

        Check(GetQHYCCDNumberOfReadModes(_qhyHandle, out var modes));
        StringBuilder sb = new(256);
        return string.Join(" - ", Enumerable.Range(0, (int)modes)
            .Select(i =>
            {
                Check(GetQHYCCDReadModeName(_qhyHandle, (uint)i, sb));
                return $"camera read mode {i} = {sb}";
            }));
    }

    private void InitCamera(ScanResult camera)
    {
        _qhyHandle = OpenQHYCCD(camera.Id);
        // 0 = single, 1 = live
        Check(SetQHYCCDStreamMode(_qhyHandle, 0));
        Check(InitQHYCCD(_qhyHandle));

        if (IsQHYCCDControlAvailable(_qhyHandle, ControlId.CamSensorUlvoStatus) == 0)
        {
            var status = GetQHYCCDParam(_qhyHandle, ControlId.CamSensorUlvoStatus);
            if (status is not 2 and not 9)
                throw new Exception($"CamSensorUlvoStatus = {status}");
        }

        if (IsQHYCCDControlAvailable(_qhyHandle, ControlId.Cam16Bits) == 0)
            Check(SetQHYCCDBitsMode(_qhyHandle, 16));

        Check(SetQHYCCDBinMode(_qhyHandle, 1, 1));
        var memLength = GetQHYCCDMemLength(_qhyHandle);
        _imgBuffer = new byte[memLength];

        var bayerType = IsQHYCCDControlAvailable(_qhyHandle, ControlId.CamColor);
        if (bayerType == 0)
            throw new Exception($"IsQHYCCDControlAvailable(CAM_COLOR) = {bayerType}");

        _cameraControls.Clear();
        foreach (var controlId in Enum.GetValues<ControlId>())
            if (CameraControl.TryGet(_qhyHandle, controlId, out var control))
                _cameraControls.Add(control);
    }

    public Task<string> GetSdkVersionAsync()
    {
        return _threadling.Do(GetSdkVersion);
    }

    private static string GetSdkVersion()
    {
        Check(GetQHYCCDSDKVersion(out var year, out var month, out var day, out var subday));
        return $"{year}-{month}-{day}-{subday}";
    }

    public Task<string> GetFirmwareVersionAsync()
    {
        return _threadling.Do(() => GetFirmwareVersion(_qhyHandle));
    }

    private static string GetFirmwareVersion(IntPtr handle)
    {
        var versionBuf = new byte[10];
        Check(GetQHYCCDFWVersion(handle, versionBuf));
        var ver = versionBuf[0] >> 4;
        // taken from nina and the qhyccd pdf
        var version = ver < 9 ? Convert.ToString(ver + 16) + "-" + Convert.ToString(versionBuf[0] & -241) + "-" + Convert.ToString(versionBuf[1]) : Convert.ToString(ver) + "-" + Convert.ToString(versionBuf[0] & -241) + "-" + Convert.ToString(versionBuf[1]);
        return version;
    }

    public Task<string> GetFpgaVersionAsync()
    {
        return _threadling.Do(() => GetFpgaVersion(_qhyHandle));
    }

    private static string GetFpgaVersion(IntPtr handle)
    {
        var version = "";
        var buf = new byte[4];

        for (byte i = 0; i <= 3; i++)
        {
            if (GetQHYCCDFPGAVersion(handle, i, buf) == 0)
            {
                if (i > 0)
                    version += ", ";
                version += i + ": " + Convert.ToString(buf[0]) + "-" + Convert.ToString(buf[1]) + "-" + Convert.ToString(buf[2]) + "-" + Convert.ToString(buf[3]);
            }
            else
                break;
        }

        return version;
    }

    public Task HardwareCrop(PixelRect rect)
    {
        return _threadling.Do(() =>
        {
            Check(GetQHYCCDCurrentROI(_qhyHandle, out var oldStartX, out var oldStartY, out var oldSizeX, out var oldSizeY));
            Console.WriteLine($"Changing crop from {oldStartX},{oldStartY}-{oldSizeX},{oldSizeY} to {oldStartX + (uint)rect.X},{oldStartY + (uint)rect.Y}-{(uint)rect.Width},{(uint)rect.Height}");
            if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 || (uint)rect.X + rect.Width > oldSizeX || (uint)rect.Y + rect.Height > oldSizeY)
                throw new Exception("Invalid new hardware crop size");
            Check(SetQHYCCDResolution(_qhyHandle, oldStartX + (uint)rect.X, oldStartY + (uint)rect.Y, (uint)rect.Width, (uint)rect.Height));
        });
    }

    public Task ResetHardwareCrop()
    {
        return _threadling.Do(() =>
        {
            var chipInfo = GetChipInfo();
            Console.WriteLine($"Resetting crop to {chipInfo.imageWidth},{chipInfo.imageHeight}");
            Check(SetQHYCCDResolution(_qhyHandle, 0, 0, chipInfo.imageWidth, chipInfo.imageHeight));
        });
    }

    private IdleActionResult IdleAction()
    {
        if (_exposing)
        {
            var image = ExposeSingle();
            Push(image);
        }

        List<CameraControlValue> values = new(_cameraControls.Count);
        foreach (var control in _cameraControls)
            values.Add(new CameraControlValue(control));
        Dispatcher.UIThread.Post(() => OnControlsUpdated?.Invoke(values));

        return _exposing ? IdleActionResult.LoopImmediately : IdleActionResult.WaitWithTimeout(TimeSpan.FromSeconds(1));
    }

    private DeviceImage ExposeSingle()
    {
        var expResult = ExpQHYCCDSingleFrame(_qhyHandle);
        if (expResult != 0x2001)
        {
            Check(expResult);
            // QHYCCD_READ_DIRECTLY
        }

        Check(GetQHYCCDSingleFrame(_qhyHandle, out var width, out var height, out var bpp, out var channels, _imgBuffer ?? throw new InvalidOperationException()));
        if (channels != 1)
        {
            throw new Exception($"Only single-channel images supported: channels={channels}");
        }

        if (bpp == 8)
        {
            var result = new byte[width * height];
            Array.Copy(_imgBuffer, result, result.Length);
            return new DeviceImage<byte>(result, width, height);
        }

        if (bpp == 16)
        {
            var result = new ushort[width * height];
            Buffer.BlockCopy(_imgBuffer, 0, result, 0, result.Length * sizeof(ushort));
            return new DeviceImage<ushort>(result, width, height);
        }

        throw new Exception($"Only 8 and 16bpp images supported: bpp={bpp}");
    }

    public void SetControl(CameraControl control, double value)
    {
        Try(_threadling.Do(() => control.Value = value));
    }
}
