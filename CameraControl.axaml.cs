using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Scopie;

public sealed partial class CameraControl : UserControl
{
    public CameraControl()
    {
        InitializeComponent();
    }

    private new Camera DataContext => (Camera)(base.DataContext ?? throw new InvalidOperationException());

    private async void ScanCameras(object? sender, RoutedEventArgs e)
    {
        await DataContext.ScanCameras();
    }

    private void SelectCamera(object? sender, RoutedEventArgs e)
    {
        var scanResult = (QhySdk.ScanResult)(((Button?)sender)?.DataContext ?? throw new InvalidOperationException());
        DataContext.SelectCamera(scanResult);
    }
}
