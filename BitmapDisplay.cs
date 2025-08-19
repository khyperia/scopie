using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Scopie;

internal static class BitmapDisplay
{
    public static CroppableImage Create(PushEnumerable<Bitmap> bitmapStream)
    {
        CroppableImage image = new();
        image.AttachedToLogicalTree += (_, _) =>
        {
            image.Bitmap = bitmapStream.Current;
            bitmapStream.MoveNext += MoveNext;
        };
        image.DetachedFromLogicalTree += (_, _) => { bitmapStream.MoveNext -= MoveNext; };

        return image;

        void MoveNext(Bitmap bitmap)
        {
            Dispatcher.UIThread.Post(() => image.Bitmap = bitmap);
        }
    }
}
