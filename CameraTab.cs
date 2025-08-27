using Avalonia.Controls;
using static Scopie.Ext;

namespace Scopie;

internal static class CameraTab
{
    public static async Task<TabItem> Create(CameraUiBag cameraUiBag)
    {
        var croppableImage = BitmapDisplay.Create(cameraUiBag.BitmapProcessor);
        var cameraControlUi = await CameraControlUi.Create(cameraUiBag.Camera, cameraUiBag.ImageProcessor, croppableImage);

        return Tab(
            cameraUiBag.Camera.CameraId.Id,
            new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    new ScrollViewer { Content = cameraControlUi, [DockPanel.DockProperty] = Dock.Left },
                    croppableImage,
                }
            }
        );
    }
}
