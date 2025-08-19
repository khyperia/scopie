using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Scopie;

internal sealed class CroppableImage : Panel
{
    private static readonly IBrush Outline = Brush.Parse("#ff0000");
    private readonly Image _image = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
    private readonly CroppedBitmap _croppedBitmap = new();
    private readonly Rectangle _rectangle = new();

    private Point? _pressed;
    private Point _current;

    public CroppableImage()
    {
        RenderOptions.SetBitmapInterpolationMode(_image, BitmapInterpolationMode.None);
        _image.Source = _croppedBitmap;
        Children.Add(_image);
        Children.Add(_rectangle);
    }

    public IImage? Bitmap
    {
        get => _croppedBitmap.Source;
        set
        {
            if (_croppedBitmap.Source is { } oldSource)
            {
                if (value == null || oldSource.Size != value.Size)
                    ResetCropInternal();
                // if (oldSource is IDisposable disposable)
                //     disposable.Dispose();
            }
            _croppedBitmap.Source = value;
            _image.Source = _croppedBitmap;
            _image.InvalidateMeasure(); // dunno why needed
        }
    }

    public PixelRect CurrentCrop => _croppedBitmap.SourceRect;
    public Size FullSize => _croppedBitmap.Source?.Size ?? new Size(0, 0);

    protected override Size MeasureOverride(Size availableSize)
    {
        Size? desiredSize = null;
        foreach (var control in Children)
        {
            if (desiredSize is { } size)
                control.Measure(size);
            else
            {
                // First control is the image. Use its desired size for the rest.
                control.Measure(availableSize);
                desiredSize = control.DesiredSize;
            }
        }
        return desiredSize ?? availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _image.Arrange(new Rect(new Point(0, 0), finalSize));
        var bounds = _image.Bounds;
        if (_pressed.HasValue)
        {
            var rect = DragRect;
            _rectangle.Arrange(new Rect(rect.Position + bounds.Position, rect.Size));
            _rectangle.Stroke = Outline;
            _rectangle.StrokeThickness = 1;
        }
        else
        {
            _rectangle.Stroke = null;
            _rectangle.Arrange(new Rect(new Point(0, 0), finalSize));
        }
        for (var i = 2; i < Children.Count; i++)
            Children[i].Arrange(bounds);
        return finalSize;
    }

    public void ResetCrop()
    {
        ResetCropInternal();
        _image.Source = _croppedBitmap;
    }

    private void ResetCropInternal()
    {
        _croppedBitmap.SourceRect = new PixelRect(0, 0, 0, 0);
        InvalidateArrange();
        _image.InvalidateArrange(); // idk why needed
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
        _pressed = _current = e.GetPosition(_image);
        InvalidateArrange();
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_pressed.HasValue)
        {
            var bitmapSize = _croppedBitmap.Size;
            var currentDisplaySize = _image.Bounds.Size;
            var rect = DragRect / new Vector(currentDisplaySize.Width, currentDisplaySize.Height) * new Vector(bitmapSize.Width, bitmapSize.Height);
            var currentPos = _croppedBitmap.SourceRect.Position;
            var pix = new PixelRect((int)Math.Round(rect.X) + currentPos.X, (int)Math.Round(rect.Y) + currentPos.Y, (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
            _croppedBitmap.SourceRect = pix;
            _image.Source = _croppedBitmap;
            InvalidateArrange();
            _image.InvalidateArrange(); // idk why needed
        }
        _pressed = null;
        base.OnPointerReleased(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var position = e.GetPosition(_image);
        if (_current != position)
        {
            _current = position;
            if (_pressed.HasValue)
                InvalidateArrange();
        }
        base.OnPointerMoved(e);
    }
}
