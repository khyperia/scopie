using Hjg.Pngcs;

namespace Scopie;

internal static class ImageIO
{
    public static void Save(DeviceImage image)
    {
        switch (image)
        {
            case DeviceImage<ushort> image16:
                {
                    using var file = CreateFile();
                    var imageInfo = new ImageInfo((int)image16.Width, (int)image16.Height, 16, alpha: false, grayscale: true, palette: false);
                    var writer = new PngWriter(file, imageInfo);
                    var line = new int[image16.Width];
                    for (var y = 0u; y < image16.Height; y++)
                    {
                        var row = image16.Data.AsSpan().Slice((int)(y * image16.Width), (int)image16.Width);
                        for (var i = 0; i < image16.Width; i++)
                            line[i] = row[i];
                        writer.WriteRowInt(line, (int)y);
                    }
                    writer.End();
                    Console.WriteLine($"Saved {file.Name}");
                    break;
                }
            case DeviceImage<byte> image8:
                {
                    using var file = CreateFile();
                    var imageInfo = new ImageInfo((int)image8.Width, (int)image8.Height, 8, alpha: false, grayscale: true, palette: false);
                    var writer = new PngWriter(file, imageInfo);
                    var line = new byte[image8.Width];
                    for (var y = 0u; y < image8.Height; y++)
                    {
                        var row = image8.Data.AsSpan().Slice((int)(y * image8.Width), (int)image8.Width);
                        row.CopyTo(line);
                        writer.WriteRowByte(line, (int)y);
                    }
                    writer.End();
                    Console.WriteLine($"Saved {file.Name}");
                    break;
                }
            default:
                throw new Exception("Unsupported image type " + image.GetType());
        }
    }

    public static DeviceImage Load(string filename)
    {
        using var stream = File.OpenRead(filename);
        var reader = new PngReader(stream, filename);
        var info = reader.ImgInfo;
        if (info.Alpha) throw new();
        if (!info.Greyscale) throw new();
        if (info.Channels != 1) throw new();
        switch (info.BitspPixel)
        {
            case 16:
                {
                    var result = new DeviceImage<ushort>(new ushort[info.Cols * info.Rows], (uint)info.Cols, (uint)info.Rows);
                    int[]? buffer = null;
                    for (var y = 0u; y < result.Height; y++)
                    {
                        buffer = reader.ReadRowInt(buffer, (int)y);
                        var row = result.Data.AsSpan().Slice((int)(y * result.Width), (int)result.Width);
                        for (var i = 0; i < result.Width; i++)
                            row[i] = (ushort)buffer[i];
                    }
                    reader.End();
                    return result;
                }
            case 8:
                {
                    var result = new DeviceImage<byte>(new byte[info.Cols * info.Rows], (uint)info.Cols, (uint)info.Rows);
                    byte[]? buffer = null;
                    for (var y = 0u; y < result.Height; y++)
                    {
                        buffer = reader.ReadRowByte(buffer, (int)y);
                        var row = result.Data.AsSpan().Slice((int)(y * result.Width), (int)result.Width);
                        buffer.AsSpan().CopyTo(row);
                    }
                    reader.End();
                    return result;
                }
            default:
                throw new Exception("Unsupported image type " + info);
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
            catch (IOException)
            {
            }
        }

        return new FileStream(Path.Combine(folder, $"{baseFilename}.100.png"), FileMode.CreateNew, FileAccess.Write);
    }
}
