using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using static Scopie.ExceptionReporter;
using static Scopie.LibQhy;

namespace Scopie;

public record struct ScanResult(string Id, string Model);

internal abstract record DeviceImage(uint Width, uint Height);

internal sealed record DeviceImage<T>(T[] Data, uint Width, uint Height) : DeviceImage(Width, Height);

internal sealed class Camera(TabControl tabs)
{
    private readonly Threadling _threadling = new(null);
    private readonly List<CameraControl> _cameraControls = [];
    private readonly Image _image = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
    private readonly StackPanel _controlsStackPanel = new();
    private DeviceImage? _deviceImage;
    private IntPtr _qhyHandle;
    private byte[]? _imgBuffer;
    private bool _exposing;
    private bool _save;
    private bool _sortStretch;

    public static void Init()
    {
        Check(InitQHYCCDResource());
    }

    public static void DeInit()
    {
        Check(ReleaseQHYCCDResource());
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

    public async Task Init(ScanResult camera)
    {
        await _threadling.Do(() => InitCamera(camera));
        _threadling.IdleAction = IdleAction;

        var (chipWidth, chipHeight, imageWidth, imageHeight, pixelWidth, pixelHeight, bitsPerPixel) = await _threadling.Do(() =>
        {
            Check(GetQHYCCDChipInfo(_qhyHandle, out var chipWidth, out var chipHeight, out var imageWidth, out var imageHeight, out var pixelWidth, out var pixelHeight, out var bitsPerPixel));
            return (chipWidth, chipHeight, imageWidth, imageHeight, pixelWidth, pixelHeight, bitsPerPixel);
        });
        var (effectiveStartX, effectiveStartY, effectiveSizeX, effectiveSizeY) = await _threadling.Do(() =>
        {
            Check(GetQHYCCDEffectiveArea(_qhyHandle, out var effectiveStartX, out var effectiveStartY, out var effectiveSizeX, out var effectiveSizeY));
            return (effectiveStartX, effectiveStartY, effectiveSizeX, effectiveSizeY);
        });

        var fastReadoutStatus = await _threadling.Do(() =>
        {
            var canFastReadout = IsQHYCCDControlAvailable(_qhyHandle, ControlId.ControlSpeed) == 0;
            if (canFastReadout)
            {
                Check(GetQHYCCDParamMinMaxStep(_qhyHandle, ControlId.ControlSpeed, out var min, out var max, out var step));
                return $"camera supports fast readout at speeds: {min}-{max} (step={step})";
            }

            Check(GetQHYCCDNumberOfReadModes(_qhyHandle, out var modes));
            StringBuilder sb = new(256);
            return string.Join(" - ", Enumerable.Range(0, (int)modes).Select(i =>
            {
                Check(GetQHYCCDReadModeName(_qhyHandle, (uint)i, sb));
                return $"camera read mode {i} = {sb}";
            }));
        });

        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new Label { Content = camera.Id });
        stackPanel.Children.Add(new Label { Content = $"sdk version: {await _threadling.Do(GetSdkVersion)}" });
        stackPanel.Children.Add(new Label { Content = $"firmware version: {await _threadling.Do(() => GetFirmwareVersion(_qhyHandle))}" });
        stackPanel.Children.Add(new Label { Content = $"fpga version: {await _threadling.Do(() => GetFpgaVersion(_qhyHandle))}" });
        stackPanel.Children.Add(new Label { Content = $"chip size: {chipWidth} - {chipHeight}" });
        stackPanel.Children.Add(new Label { Content = $"image size: {imageWidth} - {imageHeight}" });
        stackPanel.Children.Add(new Label { Content = $"pixel size: {pixelWidth} - {pixelHeight}" });
        stackPanel.Children.Add(new Label { Content = $"bits/pixel: {bitsPerPixel}" });
        stackPanel.Children.Add(new Label { Content = $"effective area: x={effectiveStartX} y={effectiveStartY} w={effectiveSizeX} h={effectiveSizeY}" });
        stackPanel.Children.Add(new Label { Content = fastReadoutStatus });
        stackPanel.Children.Add(ToggleThreadling("Exposing", v => _exposing = v));
        stackPanel.Children.Add(Toggle("Save", v => _save = v));
        stackPanel.Children.Add(Toggle("Sort stretch", v =>
        {
            _sortStretch = v;
            RefreshImage();
        }));
        stackPanel.Children.Add(_controlsStackPanel);
        stackPanel.DetachedFromVisualTree += (_, _) => _threadling.Dispose();

        var horiz = new StackPanel { Orientation = Orientation.Horizontal };
        horiz.Children.Add(stackPanel);
        horiz.Children.Add(_image);

        tabs.Items.Add(new TabItem
        {
            Header = camera.Id,
            Content = horiz
        });
    }

    private static ToggleSwitch Toggle(string name, Action<bool> checkedChange)
    {
        var result = new ToggleSwitch { OnContent = name, OffContent = name };
        result.IsCheckedChanged += (_, _) =>
        {
            if (result.IsChecked is { } isChecked)
                checkedChange(isChecked);
        };
        return result;
    }

    private ToggleSwitch ToggleThreadling(string name, Action<bool> threadedCheckedChange)
    {
        var result = new ToggleSwitch { OnContent = name, OffContent = name };
        result.IsCheckedChanged += (_, _) =>
        {
            if (result.IsChecked is { } isChecked)
                Try(_threadling.Do(() => threadedCheckedChange(isChecked)));
        };
        return result;
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

    private static string GetSdkVersion()
    {
        Check(GetQHYCCDSDKVersion(out var year, out var month, out var day, out var subday));
        return $"{year}-{month}-{day}-{subday}";
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

    private static bool _debugLoad = true;

    private IdleActionResult IdleAction()
    {
        if (_exposing)
        {
            var image = ExposeSingle();
            Dispatcher.UIThread.Post(() => SetImage(image));
        }
        else if (_debugLoad)
        {
            _debugLoad = false;
            var image = ImageIO.Load("telescope.2019-11-21.19-39-54.png");
            Dispatcher.UIThread.Post(() => SetImage(image));
        }

        List<CameraControlValue> values = new(_cameraControls.Count);
        foreach (var control in _cameraControls)
            values.Add(new CameraControlValue(control));
        Dispatcher.UIThread.Post(() => SetControls(values));

        return _exposing ? IdleActionResult.LoopImmediately : IdleActionResult.WaitWithTimeout(TimeSpan.FromSeconds(1));
    }

    private void SetControls(List<CameraControlValue> controls)
    {
        if (_controlsStackPanel.Children.Count > controls.Count)
            _controlsStackPanel.Children.RemoveRange(controls.Count, _controlsStackPanel.Children.Count - controls.Count);

        while (_controlsStackPanel.Children.Count < controls.Count)
        {
            var horiz = new StackPanel { Orientation = Orientation.Horizontal };
            horiz.Children.Add(new Label());
            _controlsStackPanel.Children.Add(horiz);
        }

        for (var i = 0; i < controls.Count; i++)
        {
            var horiz = (StackPanel)_controlsStackPanel.Children[i];
            var label = (Label)horiz.Children[0];
            label.Content = controls[i].ToString();
        }
    }

    private void SetImage(DeviceImage deviceImage)
    {
        if (_image.Source is IDisposable disposable)
            disposable.Dispose();
        if (_save)
            Try(Task.Run(() => ImageIO.Save(deviceImage)));
        _deviceImage = deviceImage;
        RefreshImage();
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

    private void RefreshImage()
    {
        if (_deviceImage == null)
            return;
        if (_sortStretch)
        {
            Try(DoSortStretch());

            async Task DoSortStretch()
            {
                var img = _deviceImage;
                var result = await Task.Run(() => ImageProcessor.SortStretch(img));
                _image.Source = ToBitmap(result);
            }
        }
        else
            _image.Source = ToBitmap(_deviceImage);
    }

    private static Bitmap ToBitmap(DeviceImage image)
    {
        switch (image)
        {
            case DeviceImage<ushort> gray16:
                unsafe
                {
                    fixed (ushort* data = gray16.Data)
                    {
                        return new Bitmap(PixelFormats.Gray16, AlphaFormat.Opaque, (nint)data, new PixelSize((int)image.Width, (int)image.Height), new Vector(96, 96), (int)image.Width * 2);
                    }
                }

            case DeviceImage<byte> gray8:
                unsafe
                {
                    fixed (byte* data = gray8.Data)
                    {
                        return new Bitmap(PixelFormats.Gray8, AlphaFormat.Opaque, (nint)data, new PixelSize((int)image.Width, (int)image.Height), new Vector(96, 96), (int)image.Width);
                    }
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(image), image, "image must be DeviceImage<ushort> or DeviceImage<byte>");
        }
    }
}
