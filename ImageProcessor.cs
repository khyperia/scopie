using static Scopie.ExceptionReporter;

namespace Scopie;

internal sealed class ImageProcessor(IPushEnumerable<DeviceImage> input) : PushProcessor<DeviceImage, DeviceImage>(input)
{
    private readonly SemaphoreSlim _semaphore = new(Math.Max(Environment.ProcessorCount - 1, 1));

    private bool _sortStretch;

    private ulong _currentPushResult;
    private ulong _currentProcessing;

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

    public override void Dispose()
    {
        base.Dispose();
        _semaphore.Dispose();
    }

    protected override void Process(DeviceImage image)
    {
        var currentId = Interlocked.Add(ref _currentProcessing, 1);
        if (!SortStretch)
        {
            TryPush(currentId, image);
        }
        else
        {
            Try(Task.Run(() => Process(currentId, image)));
        }
    }

    private async Task Process(ulong currentId, DeviceImage input)
    {
        await _semaphore.WaitAsync().ConfigureAwait(false);

        try
        {
            lock (this)
            {
                var currentProcessing = Interlocked.Read(ref _currentProcessing);
                if (currentId + 1 < currentProcessing)
                {
                    Console.WriteLine($"Skip image process push before process (newer image in queue): {currentId} + 1 < {currentProcessing}");
                }
                if (currentId <= _currentPushResult)
                {
                    Console.WriteLine($"Skip image process push before process (newer result pushed out): {currentId} <= {_currentPushResult}");
                    return;
                }
            }

            var result = DoSortStretch(input);
            TryPush(currentId, result);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void TryPush(ulong version, DeviceImage result)
    {
        lock (this)
        {
            if (version > _currentPushResult)
            {
                _currentPushResult = version;
                Push(result);
            }
            else
            {
                Console.WriteLine($"Skip image process push after process: {version} > {_currentPushResult}");
            }
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
