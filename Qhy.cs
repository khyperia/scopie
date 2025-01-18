using System.Text;
using static Scopie.LibQhy;

namespace Scopie;

internal abstract record DeviceImage(uint Width, uint Height);

internal record DeviceImage<T>(T[] Data, uint Width, uint Height) : DeviceImage(Width, Height);

internal sealed class QhySdk : IDisposable
{
    public record struct ScanResult(string Id, string Model);

    public QhySdk()
    {
        Check(InitQHYCCDResource());
    }

    public void Dispose()
    {
        Check(ReleaseQHYCCDResource());
    }

    public List<ScanResult> Scan()
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
}

internal sealed class Qhy : IDisposable
{
    private readonly IntPtr handle;
    private double chipWidth;
    private double chipHeight;
    private uint imageWidth;
    private uint imageHeight;
    private double pixelWidth;
    private double pixelHeight;
    private uint bitsPerPixel;
    private uint effectiveStartX;
    private uint effectiveStartY;
    private uint effectiveSizeX;
    private uint effectiveSizeY;
    private bool canFastReadout;
    private byte[]? imgbuffer;

    public Qhy(string id)
    {
        handle = OpenQHYCCD(id);
        if (handle == IntPtr.Zero)
            throw new Exception("Could not open camera " + id);
    }

    public void Init()
    {
        // 0 = single, 1 = live
        Check(SetQHYCCDStreamMode(handle, 0));

        Check(InitQHYCCD(handle));

        Check(GetQHYCCDSDKVersion(out var year, out var month, out var day, out var subday));
        Console.WriteLine($"SDK version {year}-{month}-{day}-{subday}");
        Console.WriteLine($"Firmware version {GetFirmwareVersion()}");
        Console.WriteLine($"FPGA version {GetFpgaVersion()}");

        if (IsQHYCCDControlAvailable(handle, ControlId.CamSensorUlvoStatus) != 0)
        {
            var status = GetQHYCCDParam(handle, ControlId.CamSensorUlvoStatus);
            if (status is not 2 and not 9)
                throw new Exception($"CamSensorUlvoStatus = {status}");
        }

        Check(SetQHYCCDBinMode(handle, 1, 1));
        Check(GetQHYCCDChipInfo(handle, out chipWidth, out chipHeight, out imageWidth, out imageHeight, out pixelWidth, out pixelHeight, out bitsPerPixel));
        Check(GetQHYCCDEffectiveArea(handle, out effectiveStartX, out effectiveStartY, out effectiveSizeX, out effectiveSizeY));
        imgbuffer = new byte[(imageWidth * imageHeight * bitsPerPixel + 7) / 8];

        var bayerType = IsQHYCCDControlAvailable(handle, ControlId.CamColor);
        if (bayerType != 0)
            throw new Exception($"IsQHYCCDControlAvailable(CAM_COLOR) = {bayerType}");
        canFastReadout = IsQHYCCDControlAvailable(handle, ControlId.ControlSpeed) != 0;
        if (canFastReadout)
        {
            Check(GetQHYCCDParamMinMaxStep(handle, ControlId.ControlSpeed, out var min, out var max, out var step));
            Console.WriteLine($"camera supports fast readout at speeds: {min}-{max} (step={step})");
        }
        else
        {
            Check(GetQHYCCDNumberOfReadModes(handle, out var modes));
            StringBuilder sb = new(256);
            for (var i = 0u; i < modes; i++)
            {
                Check(GetQHYCCDReadModeName(handle, i, sb));
                Console.WriteLine($"camera read mode {i} = {sb}");
            }
        }
    }

    private string GetFirmwareVersion()
    {
        var versionBuf = new byte[10];
        Check(GetQHYCCDFWVersion(handle, versionBuf));
        var ver = versionBuf[0] >> 4;
        // taken from nina and the qhyccd pdf
        var version = ver < 9 ? Convert.ToString(ver + 16) + "-" + Convert.ToString(versionBuf[0] & -241) + "-" + Convert.ToString(versionBuf[1]) : Convert.ToString(ver) + "-" + Convert.ToString(versionBuf[0] & -241) + "-" + Convert.ToString(versionBuf[1]);
        return version;
    }

    private string GetFpgaVersion()
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

    public void Dispose()
    {
        // Check(SetQHYCCDParam(handle, ControlId.ControlManulpwm, 0));
        Check(CloseQHYCCD(handle));
    }

    public DeviceImage ExposeSingle()
    {
        var expResult = ExpQHYCCDSingleFrame(handle);
        if (expResult != 0x2001)
        {
            Check(expResult);
            // QHYCCD_READ_DIRECTLY
        }

        Check(GetQHYCCDSingleFrame(handle, out var width, out var height, out var bpp, out var channels, imgbuffer ?? throw new InvalidOperationException()));
        if (channels != 1)
        {
            throw new Exception($"Only single-channel images supported: channels={channels}");
        }

        if (bpp == 8)
        {
            var result = new byte[width * height];
            Array.Copy(imgbuffer, result, result.Length);
            return new DeviceImage<byte>(result, width, height);
        }

        if (bpp == 16)
        {
            var result = new ushort[width * height];
            Buffer.BlockCopy(imgbuffer, 0, result, 0, result.Length * sizeof(ushort));
            return new DeviceImage<ushort>(result, width, height);
        }

        throw new Exception($"Only 8 and 16bpp images supported: bpp={bpp}");
    }
}
