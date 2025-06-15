using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Scopie;

internal static class ImageIO
{
    public static void Save(DeviceImage image)
    {
        switch (image)
        {
            case DeviceImage<ushort> image16:
            {
                using var imageSharp = Image.LoadPixelData(MemoryMarshal.Cast<ushort, L16>((ReadOnlySpan<ushort>)image16.Data), (int)image16.Width, (int)image16.Height);
                using var file = CreateFile();
                imageSharp.Save(file, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression });
                break;
            }
            case DeviceImage<byte> image8:
            {
                using var imageSharp = Image.LoadPixelData(MemoryMarshal.Cast<byte, L8>((ReadOnlySpan<byte>)image8.Data), (int)image8.Width, (int)image8.Height);
                using var file = CreateFile();
                imageSharp.Save(file, new PngEncoder { CompressionLevel = PngCompressionLevel.BestCompression });
                break;
            }
            default:
                throw new Exception("Unsupported image type " + image.GetType());
        }
    }

    public static DeviceImage Load(string filename)
    {
        var img = Image.Load(filename);
        switch (img)
        {
            case Image<L16> img16:
            {
                var result = new DeviceImage<ushort>(new ushort[img16.Width * img16.Height], (uint)img16.Width, (uint)img16.Height);
                img16.ProcessPixelRows(d =>
                {
                    for (var y = 0; y < d.Height; y++)
                    {
                        var row = MemoryMarshal.Cast<L16, ushort>(d.GetRowSpan(y));
                        row.CopyTo(result.Data.AsSpan(y * (int)result.Width, (int)result.Width));
                    }
                });
                return result;
            }
            case Image<L8> img8:
            {
                var result = new DeviceImage<byte>(new byte[img8.Width * img8.Height], (uint)img8.Width, (uint)img8.Height);
                img8.ProcessPixelRows(d =>
                {
                    for (var y = 0; y < d.Height; y++)
                    {
                        var row = MemoryMarshal.Cast<L8, byte>(d.GetRowSpan(y));
                        row.CopyTo(result.Data.AsSpan(y * (int)result.Width, (int)result.Width));
                    }
                });
                return result;
            }
            default:
                throw new Exception("Unsupported image type " + img.GetType());
        }
    }

    private static FileStream CreateFile()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (string.IsNullOrEmpty(desktop))
            desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var now = DateTime.Now;
        var folder = Path.Combine(desktop, $"{now.Year}_{now.Month}_{now.Day}");
        Directory.CreateDirectory(folder);
        var baseFilename = $"telescope.{now.Year}-{now.Month}-{now.Day}.{now.Hour}-{now.Minute}-{now.Second}";
        try
        {
            return new FileStream(Path.Combine(folder, $"{baseFilename}.png"), FileMode.CreateNew, FileAccess.Write);
        }
        catch (IOException)
        {
        }

        baseFilename += "-" + now.Millisecond;
        try
        {
            return new FileStream(Path.Combine(folder, $"{baseFilename}.png"), FileMode.CreateNew, FileAccess.Write);
        }
        catch (IOException)
        {
        }

        for (var i = 0; i < 100; i++)
        {
            try
            {
                return new FileStream(Path.Combine(folder, $"{baseFilename}.{i}.png"), FileMode.CreateNew, FileAccess.Write);
            }
            catch (IOException e)
            {
            }
        }

        return new FileStream(Path.Combine(folder, $"{baseFilename}.100.png"), FileMode.CreateNew, FileAccess.Write);
    }
}
