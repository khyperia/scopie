using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Scopie;

internal sealed class ImageToBitmapProcessor(IPushEnumerable<DeviceImage> input) : PushProcessor<DeviceImage, Bitmap>(input)
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
