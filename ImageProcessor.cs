using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using static Scopie.ExceptionReporter;

namespace Scopie;

internal class ImageProcessor(IPushEnumerable<DeviceImage> input) : PushProcessor<DeviceImage, DeviceImage>(input)
{
    private bool _sortStretch;

    public bool SortStretch
    {
        get => _sortStretch;
        set
        {
            if (_sortStretch != value)
            {
                _sortStretch = value;
                if (Input.Current is { } current)
                    Process(current);
            }
        }
    }

    protected override void Process(DeviceImage image)
    {
        if (!SortStretch)
        {
            Push(image);
        }
        else
        {
            // TODO: Out of order execution here
            Try(Task.Run(() => Push(DoSortStretch(image))));
        }
    }

    private static DeviceImage DoSortStretch(DeviceImage deviceImage)
    {
        return deviceImage switch
        {
            DeviceImage<ushort> deviceImage16 => DoSortStretch(deviceImage16),
            DeviceImage<byte> deviceImage8 => DoSortStretch(deviceImage8),
            _ => deviceImage
        };
    }

    private static DeviceImage<ushort> DoSortStretch(DeviceImage<ushort> deviceImage)
    {
        var buf = deviceImage.Data;
        var copy = new ushort[buf.Length];
        Array.Copy(buf, copy, buf.Length);
        var indices = new int[buf.Length];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = i;
        Array.Sort(copy, indices);
        var mul = (float)ushort.MaxValue / buf.Length;
        for (var i = 0; i < copy.Length; i++)
            copy[indices[i]] = (ushort)(i * mul);
        return new DeviceImage<ushort>(copy, deviceImage.Width, deviceImage.Height);
    }

    private static DeviceImage<byte> DoSortStretch(DeviceImage<byte> deviceImage)
    {
        var buf = deviceImage.Data;
        var copy = new byte[buf.Length];
        Array.Copy(buf, copy, buf.Length);
        var indices = new int[buf.Length];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = i;
        Array.Sort(copy, indices);
        var mul = (float)byte.MaxValue / buf.Length;
        for (var i = 0; i < copy.Length; i++)
            copy[indices[i]] = (byte)(i * mul);
        return new DeviceImage<byte>(copy, deviceImage.Width, deviceImage.Height);
    }
}

internal class ImageToBitmapProcessor(IPushEnumerable<DeviceImage> input) : PushProcessor<DeviceImage, Bitmap>(input)
{
    protected override void Process(DeviceImage item)
    {
        Push(ToBitmap(item));
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
