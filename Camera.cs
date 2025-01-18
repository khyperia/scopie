using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Scopie;

internal sealed class Camera : IDisposable, INotifyPropertyChanged
{
    private string _asdf = "asdf";

    private readonly CameraThread _camera;
    private bool _doExposeLoop;
    private bool _doExposeLoopThreaded;
    private Bitmap? _bitmap;

    public ObservableCollection<QhySdk.ScanResult> Cameras { get; set; } = [];

    public string Asdf
    {
        get => _asdf;
        set => SetField(ref _asdf, value);
    }

    public bool DoExposeLoop
    {
        get => _doExposeLoop;
        set
        {
            SetField(ref _doExposeLoop, value);
            _camera.DoSdk((ref QhySdk? _, ref Qhy? _) => _doExposeLoopThreaded = value);
        }
    }

    public Bitmap? Bitmap
    {
        get => _bitmap;
        set => SetField(ref _bitmap, value);
    }

    public Camera()
    {
        _camera = new CameraThread(IdleAction);
    }

    public void Dispose()
    {
        _camera.Dispose();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private IdleActionResult IdleAction(Qhy qhy)
    {
        if (!_doExposeLoopThreaded)
            return IdleActionResult.WaitForNextEvent;

        var image = qhy.ExposeSingle();
        Dispatcher.UIThread.Post(() => SetImage(image));

        return IdleActionResult.LoopImmediately;
    }

    private void SetImage(DeviceImage image)
    {
        Bitmap?.Dispose();
        switch (image)
        {
            case DeviceImage<ushort> gray16:
                unsafe
                {
                    fixed (ushort* data = gray16.Data)
                    {
                        Bitmap = new Bitmap(PixelFormats.Gray16, AlphaFormat.Opaque, (nint)data, new PixelSize((int)image.Width, (int)image.Height), new Vector(96, 96), (int)image.Width * 2);
                    }
                }

                break;
            case DeviceImage<byte> gray8:
                unsafe
                {
                    fixed (byte* data = gray8.Data)
                    {
                        Bitmap = new Bitmap(PixelFormats.Gray8, AlphaFormat.Opaque, (nint)data, new PixelSize((int)image.Width, (int)image.Height), new Vector(96, 96), (int)image.Width);
                    }
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(image), image, "image must be DeviceImage<ushort> or DeviceImage<byte>");
        }
    }

    public async Task ScanCameras()
    {
        var list = await _camera.DoSdkAsync((ref QhySdk? sdk, ref Qhy? qhy) =>
        {
            sdk ??= new QhySdk();
            return sdk.Scan();
        });
        Cameras.Clear();
        foreach (var item in list)
            Cameras.Add(item);
    }

    public void SelectCamera(QhySdk.ScanResult scanResult)
    {
        _camera.DoSdk((ref QhySdk? sdk, ref Qhy? qhy) =>
        {
            qhy?.Dispose();
            qhy = new Qhy(scanResult.Id);
            qhy.Init();
        });
    }
}
