using WatneyAstrometry.Core.Image;

namespace Scopie;

internal abstract record DeviceImage(uint Width, uint Height) : IImage
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
    public T this[int x, int y]
    {
        get => Data[y * Width + x];
        set => Data[y * Width + x] = value;
    }

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
