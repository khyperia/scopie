namespace Scopie;

internal sealed class CameraUiBag : IDisposable
{
    public static readonly List<CameraUiBag> AllCameras = [];
    public static event Action? AllCamerasChanged;

    public readonly ICamera Camera;
    public readonly ImageProcessor ImageProcessor;
    public readonly ImageToBitmapProcessor BitmapProcessor;

    public CameraUiBag(ICamera camera)
    {
        Camera = camera;
        ImageProcessor = new ImageProcessor(Camera);
        BitmapProcessor = new ImageToBitmapProcessor(ImageProcessor);

        AllCameras.Add(this);
        AllCamerasChanged?.Invoke();
    }

    public Task Init() => Camera.Init();

    public void Dispose()
    {
        BitmapProcessor.Dispose();
        ImageProcessor.Dispose();
        Camera.Dispose();

        AllCameras.Remove(this);
        AllCamerasChanged?.Invoke();
    }
}
