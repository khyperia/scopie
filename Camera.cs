using System.Text;
using Avalonia.Controls;
using static Scopie.LibQhy;

namespace Scopie;

public record struct ScanResult(string Id, string Model);

internal abstract record DeviceImage(uint Width, uint Height);

internal sealed record DeviceImage<T>(T[] Data, uint Width, uint Height) : DeviceImage(Width, Height);

internal sealed class Camera
{
    private readonly TabControl _tabs;
    private readonly Threadling _threadling;
    private IntPtr _qhyHandle;

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

    public Camera(TabControl tabs)
    {
        _tabs = tabs;
        _threadling = new Threadling(null);
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
        stackPanel.DetachedFromVisualTree += (_, _) => _threadling.Dispose();

        _tabs.Items.Add(new TabItem
        {
            Header = camera.Id,
            Content = stackPanel
        });
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
        // imgbuffer = new byte[(imageWidth * imageHeight * bitsPerPixel + 7) / 8];

        var bayerType = IsQHYCCDControlAvailable(_qhyHandle, ControlId.CamColor);
        if (bayerType == 0)
            throw new Exception($"IsQHYCCDControlAvailable(CAM_COLOR) = {bayerType}");
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

    private IdleActionResult IdleAction()
    {
        // if (!_doExposeLoopThreaded)
        return IdleActionResult.WaitForNextEvent;

        // var image = _qhy.ExposeSingle();
        // Dispatcher.UIThread.Post(() => SetImage(image));

        // return IdleActionResult.LoopImmediately;
    }

    /*
    private void SetImage(DeviceImage image)
    {
        Bitmap?.Dispose();
        switch (image)
        {
            case DeviceImage<ushort> gray16:
                unsafe
                {
                    fixed (ushort* data = gray16.Data)
                    {
                        Bitmap = new Bitmap(PixelFormats.Gray16, AlphaFormat.Opaque, (nint)data, new PixelSize((int)image.Width, (int)image.Height), new Vector(96, 96), (int)image.Width * 2);
                    }
                }

                break;
            case DeviceImage<byte> gray8:
                unsafe
                {
                    fixed (byte* data = gray8.Data)
                    {
                        Bitmap = new Bitmap(PixelFormats.Gray8, AlphaFormat.Opaque, (nint)data, new PixelSize((int)image.Width, (int)image.Height), new Vector(96, 96), (int)image.Width);
                    }
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(image), image, "image must be DeviceImage<ushort> or DeviceImage<byte>");
        }
    }
    */
}
