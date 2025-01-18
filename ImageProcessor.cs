namespace Scopie;

internal static class ImageProcessor
{
    public static DeviceImage SortStretch(DeviceImage deviceImage)
    {
        return deviceImage switch
        {
            DeviceImage<ushort> deviceImage16 => SortStretch(deviceImage16),
            DeviceImage<byte> deviceImage8 => SortStretch(deviceImage8),
            _ => deviceImage
        };
    }

    private static DeviceImage<ushort> SortStretch(DeviceImage<ushort> deviceImage)
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

    private static DeviceImage<byte> SortStretch(DeviceImage<byte> deviceImage)
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
