namespace Scopie;

internal class ImageProcessor : IDisposable
{
    private readonly List<CancellationTokenSource> _cancellationTokenSources = [];
    private Settings _mostRecentSettings;

    public readonly record struct Settings(DeviceImage? Input, bool SortStretch);

    public async Task<DeviceImage> Process(Settings settings)
    {
        _mostRecentSettings = settings;
        var result = await ProcessInternal(settings);
        // TODO: This is wrong (we want old results if this is the most up to date result)
        if (_mostRecentSettings == settings)
            return result;
        throw new OperationCanceledException();
    }

    private Task<DeviceImage> ProcessInternal(Settings settings)
    {
        if (settings.Input == null)
            throw new Exception("Cannot process null image");

        if (settings.SortStretch)
            return ThreadedProcess(settings);

        return Task.FromResult(settings.Input);
    }

    private void Cancel()
    {
        foreach (var cts in _cancellationTokenSources)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _cancellationTokenSources.Clear();
    }

    public void Dispose() => Cancel();

    private Task<DeviceImage> ThreadedProcess(Settings settings)
    {
        Cancel();
        var source = new CancellationTokenSource();
        _cancellationTokenSources.Add(source);
        var ct = source.Token;
        return Task.Run(() =>
        {
            if (settings.SortStretch)
                return SortStretch(settings.Input, ct);
            throw new Exception("Bad threaded process");
        }, ct);
    }

    public static DeviceImage SortStretch(DeviceImage deviceImage, CancellationToken ct)
    {
        return deviceImage switch
        {
            DeviceImage<ushort> deviceImage16 => SortStretch(deviceImage16, ct),
            DeviceImage<byte> deviceImage8 => SortStretch(deviceImage8, ct),
            _ => deviceImage
        };
    }

    private static DeviceImage<ushort> SortStretch(DeviceImage<ushort> deviceImage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var buf = deviceImage.Data;
        var copy = new ushort[buf.Length];
        Array.Copy(buf, copy, buf.Length);
        ct.ThrowIfCancellationRequested();
        var indices = new int[buf.Length];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = i;
        ct.ThrowIfCancellationRequested();
        Array.Sort(copy, indices);
        ct.ThrowIfCancellationRequested();
        var mul = (float)ushort.MaxValue / buf.Length;
        for (var i = 0; i < copy.Length; i++)
            copy[indices[i]] = (ushort)(i * mul);
        ct.ThrowIfCancellationRequested();
        return new DeviceImage<ushort>(copy, deviceImage.Width, deviceImage.Height);
    }

    private static DeviceImage<byte> SortStretch(DeviceImage<byte> deviceImage, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var buf = deviceImage.Data;
        var copy = new byte[buf.Length];
        Array.Copy(buf, copy, buf.Length);
        ct.ThrowIfCancellationRequested();
        var indices = new int[buf.Length];
        for (var i = 0; i < indices.Length; i++)
            indices[i] = i;
        ct.ThrowIfCancellationRequested();
        Array.Sort(copy, indices);
        ct.ThrowIfCancellationRequested();
        var mul = (float)byte.MaxValue / buf.Length;
        for (var i = 0; i < copy.Length; i++)
            copy[indices[i]] = (byte)(i * mul);
        ct.ThrowIfCancellationRequested();
        return new DeviceImage<byte>(copy, deviceImage.Width, deviceImage.Height);
    }
}
