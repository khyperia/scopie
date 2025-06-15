using Avalonia.Controls;
using Avalonia.Layout;

namespace Scopie;

internal static class CameraTab
{
    public static async Task<TabItem> Create(CameraUiBag cameraUiBag)
    {
        var croppableImage = BitmapDisplay.Create(cameraUiBag.BitmapProcessor);
        var cameraControlUi = await CameraControlUi.Create(cameraUiBag, croppableImage);

        return new TabItem
        {
            Header = cameraUiBag.Camera.CameraId.Id,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    new ScrollViewer { Content = cameraControlUi },
                    croppableImage,
                }
            }
        };
    }
}
