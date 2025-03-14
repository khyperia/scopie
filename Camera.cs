using System.Runtime.CompilerServices;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using WatneyAstrometry.Core.Image;
using static Scopie.ExceptionReporter;
using static Scopie.LibQhy;
using IImage = Avalonia.Media.IImage;

namespace Scopie;

public record struct ScanResult(string Id, string Model);

internal abstract record DeviceImage(uint Width, uint Height) : WatneyAstrometry.Core.Image.IImage
{
    public void Dispose()
    {
    }

    public abstract Stream PixelDataStream { get; }
    public abstract long PixelDataStreamOffset { get; }
    public abstract long PixelDataStreamLength { get; }
    public abstract Metadata Metadata { get; }
}

internal sealed record DeviceImage<T>(T[] Data, uint Width, uint Height) : DeviceImage(Width, Height) where T : unmanaged
{
    public override Stream PixelDataStream
    {
        get
        {
            if (typeof(T) == typeof(ushort))
            {
                // Watney expects big endian
                var us = (ushort[])(object)Data;
                var tmp = new byte[Data.Length * 2];
                for (var i = 0; i < Data.Length; i++)
                {
                    var idx = i * 2;
                    tmp[idx] = (byte)(us[i] >> 8);
                    tmp[idx + 1] = (byte)us[i];
                }

                return new MemoryStream(tmp, false);
            }

            if (typeof(T) == typeof(byte))
                return new MemoryStream((byte[])(object)Data, false);

            throw new Exception("Unsupported datatype " + typeof(T));
        }
    }

    public override long PixelDataStreamOffset => 0;

    public override long PixelDataStreamLength
    {
        get
        {
            unsafe
            {
                return Data.Length * sizeof(T);
            }
        }
    }

    public override Metadata Metadata
    {
        get
        {
            int sizeofT;
            unsafe
            {
                sizeofT = sizeof(T);
            }

            return new Metadata
            {
                BitsPerPixel = sizeofT * 8,
                ImageWidth = (int)Width,
                ImageHeight = (int)Height,
            };
        }
    }
}

internal sealed class Camera(ScanResult cameraId) : IDisposable
{
    public static readonly List<Camera> AllCameras = [];
    public static event Action? AllCamerasChanged;

    public ScanResult CameraId => cameraId;
    private readonly Threadling _threadling = new(null);
    private readonly List<CameraControl> _cameraControls = [];
    private readonly Image _image = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
    private readonly StackPanel _controlsStackPanel = new();
    private readonly ImageProcessor _imageProcessor = new();
    private IntPtr _qhyHandle;
    private byte[]? _imgBuffer;
    private bool _exposing;
    private bool _save;

    private ImageProcessor.Settings _imageProcessorSettings = new(null, false);

    private ImageProcessor.Settings ImageProcessorSettings
    {
        get => _imageProcessorSettings;
        set
        {
            if (_imageProcessorSettings != value)
            {
                _imageProcessorSettings = value;
                Try(RefreshImage(value));
            }
        }
    }

    public DeviceImage? DeviceImage => _imageProcessorSettings.Input;

    public event Action<IImage>? NewBitmap;
    public IImage? Bitmap => _image.Source;

    static Camera()
    {
        Check(InitQHYCCDResource());
    }

    public void Dispose()
    {
        AllCameras.Remove(this);
        _threadling.Dispose();
        AllCamerasChanged?.Invoke();
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

    public async Task<TabItem> Init()
    {
        await _threadling.Do(() => InitCamera(cameraId));

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
        stackPanel.Children.Add(new Label { Content = cameraId.Id });
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
        stackPanel.Children.Add(Toggle("Sort stretch", v => ImageProcessorSettings = ImageProcessorSettings with { SortStretch = v }));

        _ = new Platesolver(stackPanel, () => _imageProcessorSettings.Input);

        stackPanel.Children.Add(_controlsStackPanel);

        var horiz = new StackPanel { Orientation = Orientation.Horizontal };
        horiz.Children.Add(new ScrollViewer { Content = stackPanel });
        horiz.Children.Add(_image);

        _threadling.IdleAction = IdleAction;

        AllCameras.Add(this);
        AllCamerasChanged?.Invoke();

        return new TabItem
        {
            Header = cameraId.Id,
            Content = horiz
        };
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
            if (_save)
                Try(Task.Run(() => ImageIO.Save(image)));
            Dispatcher.UIThread.Post(() => ImageProcessorSettings = ImageProcessorSettings with { Input = image });
        }
        else if (_debugLoad)
        {
            _debugLoad = false;

            string DebugFile([CallerFilePath] string? s = null) => Path.Combine(Path.GetDirectoryName(s) ?? throw new(), "telescope.2019-11-21.19-39-54.png");
            var debugFile = DebugFile();
            if (File.Exists(debugFile))
            {
                var image = ImageIO.Load(debugFile);
                Dispatcher.UIThread.Post(() => ImageProcessorSettings = ImageProcessorSettings with { Input = image });
            }
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
            var index = _controlsStackPanel.Children.Count;
            var horiz = new StackPanel { Orientation = Orientation.Horizontal };
            horiz.Children.Add(new Label());
            var textBox = new TextBox();
            textBox.KeyDown += (_, args) =>
            {
                if (textBox.IsFocused && args.Key == Key.Enter && TrySetControl(index, textBox.Text))
                    textBox.Text = "";
            };
            horiz.Children.Add(textBox);
            _controlsStackPanel.Children.Add(horiz);
        }

        for (var i = 0; i < controls.Count; i++)
        {
            var horiz = (StackPanel)_controlsStackPanel.Children[i];
            var label = (Label)horiz.Children[0];
            label.Content = controls[i].ToString();
        }
    }

    private bool TrySetControl(int controlIndex, string? text)
    {
        if (!double.TryParse(text, out var v))
            return false;
        if (controlIndex >= _cameraControls.Count)
            return false;
        var c = _cameraControls[controlIndex];
        Try(_threadling.Do(() => c.Value = v));
        return true;
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

    private async Task RefreshImage(ImageProcessor.Settings settings)
    {
        try
        {
            OnNewBitmap(ToBitmap(await _imageProcessor.Process(settings)));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnNewBitmap(Bitmap bitmap)
    {
        if (_image.Source is IDisposable disposable)
            disposable.Dispose();
        _image.Source = bitmap;
        NewBitmap?.Invoke(bitmap);
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
