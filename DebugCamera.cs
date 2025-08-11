using Avalonia;

namespace Scopie;

internal sealed class DebugCamera(DeviceImage[] debugImages) : PushEnumerable<DeviceImage>, ICamera
{
    private int _currentIndex;

    public void Dispose()
    {
    }

    public ScanResult CameraId => new("debug", "debug");

    public event Action<List<CameraControlValue>>? OnControlsUpdated
    {
        add { }
        remove { }
    }

    public bool Exposing
    {
        set { }
    }

    public Task Init()
    {
        return Task.CompletedTask;
    }

    public Task<(double chipWidth, double chipHeight, uint imageWidth, uint imageHeight, double pixelWidth, double pixelHeight, uint bitsPerPixel)> GetChipInfoAsync()
    {
        return Task.FromResult(((double)debugImages[_currentIndex].Width, (double)debugImages[_currentIndex].Height, debugImages[_currentIndex].Width, debugImages[_currentIndex].Height, 1.0, 1.0, (uint)debugImages[_currentIndex].Metadata.BitsPerPixel));
    }

    public Task<(uint effectiveStartX, uint effectiveStartY, uint effectiveSizeX, uint effectiveSizeY)> GetEffectiveAreaAsync()
    {
        return Task.FromResult((0u, 0u, debugImages[_currentIndex].Width, debugImages[_currentIndex].Height));
    }

    public Task<string> GetFastReadoutStatusAsync()
    {
        return Task.FromResult("fast readout status");
    }

    public Task<string> GetSdkVersionAsync()
    {
        return Task.FromResult("1.0");
    }

    public Task<string> GetFirmwareVersionAsync()
    {
        return Task.FromResult("1.0");
    }

    public Task<string> GetFpgaVersionAsync()
    {
        return Task.FromResult("1.0");
    }

    public Task HardwareCrop(PixelRect rect)
    {
        return Task.CompletedTask;
    }

    public Task ResetHardwareCrop()
    {
        return Task.CompletedTask;
    }

    public void SetControl(CameraControl control, double value)
    {
    }

    public void Push(int n)
    {
        ThreadPool.QueueUserWorkItem(_ =>
        {
            for (var i = 0; i < n; i++)
            {
                Push(debugImages[_currentIndex]);
                _currentIndex = (_currentIndex + 1).Mod(debugImages.Length);
            }
        });
    }
}
