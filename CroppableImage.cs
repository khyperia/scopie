using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Scopie;

public class CroppableImage : Panel
{
    private static readonly IBrush Outline = Brush.Parse("#ffffff");
    private readonly Image _image = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
    private readonly CroppedBitmap _croppedBitmap = new();
    private readonly Rectangle _rectangle = new();

    private Point? _pressed;
    private Point _current;

    public CroppableImage()
    {
        _image.Source = _croppedBitmap;
        Children.Add(_image);
        Children.Add(_rectangle);
    }

    public IImage? Bitmap
    {
        get => _croppedBitmap.Source;
        set
        {
            if (_croppedBitmap.Source is IDisposable disposable)
                disposable.Dispose();
            _croppedBitmap.Source = value;
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _image.Measure(availableSize);
        _rectangle.Measure(availableSize);
        return _image.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _image.Arrange(new Rect(new Point(0, 0), finalSize));
        if (_pressed.HasValue)
        {
            _rectangle.Arrange(DragRect);
            _rectangle.Stroke = Outline;
        }
        else
        {
            _rectangle.Stroke = null;
            _rectangle.Arrange(new Rect(new Point(0, 0), finalSize));
        }
        return finalSize;
    }

    public void ResetCrop()
    {
        _croppedBitmap.SourceRect = new PixelRect(0, 0, 0, 0);
    }

    private Rect DragRect
    {
        get
        {
            if (_pressed is not { } pressed)
                return new Rect(0, 0, 0, 0);
            var min = new Point(Math.Min(pressed.X, _current.X), Math.Min(pressed.Y, _current.Y));
            var max = new Point(Math.Max(pressed.X, _current.X), Math.Max(pressed.Y, _current.Y));
            return new Rect(min, max);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _pressed = _current = e.GetPosition(this);
        InvalidateVisual();
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_pressed.HasValue)
        {
            var bitmapSize = _croppedBitmap.Source?.Size ?? new Size(1, 1);
            var rect = DragRect / new Vector(_image.Width, _image.Height) * new Vector(bitmapSize.Width, bitmapSize.Height);
            var pix = new PixelRect((int)Math.Round(rect.X), (int)Math.Round(rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
            _croppedBitmap.SourceRect = pix;
        }
        _pressed = null;
        base.OnPointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var position = e.GetPosition(this);
        if (_current != position)
        {
            _current = position;
            InvalidateVisual();
        }
        base.OnPointerMoved(e);
    }
}
