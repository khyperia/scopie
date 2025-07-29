using Avalonia.Controls;

namespace Scopie;

internal static class CameraTab
{
    public static async Task<TabItem> Create(CameraUiBag cameraUiBag)
    {
        var croppableImage = BitmapDisplay.Create(cameraUiBag.BitmapProcessor);
        var cameraControlUi = await CameraControlUi.Create(cameraUiBag.Camera, cameraUiBag.ImageProcessor, croppableImage);

        return new TabItem
        {
            Header = cameraUiBag.Camera.CameraId.Id,
            Content = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new ScrollViewer { Content = cameraControlUi, [DockPanel.DockProperty] = Dock.Left },
                    croppableImage,
                }
            }
        };
    }
}
