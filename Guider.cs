using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using MathNet.Numerics.LinearAlgebra;
using static Scopie.Ext;

namespace Scopie;

internal sealed class GuiderSettings
{
    // TODO: Autodetect exclusion zone radius (2x search radius?)
    public uint AutodetectExclusionZone = 100;
    public double AutodetectMaximumBrightnessPercent = 0.9;
    public double TrackedStarSearchRadius = 50;
    public double TrackedStarMaxShiftDistance = 37.5;
    public uint TrackedStarParabolaFitRadius = 6;
    public double TrackedStarSigmaRejection = 50;
}

internal sealed class TrackedStar
{
    private readonly GuiderSettings _guiderSettings;
    public double X;
    public double Y;

    public TrackedStar(GuiderSettings guiderSettings, double initialX, double initialY)
    {
        _guiderSettings = guiderSettings;
        X = initialX;
        Y = initialY;
    }

    public bool Update(DeviceImage imageGeneric, [MaybeNullWhen(true)] out string failureReason, out double sigma)
    {
        if (imageGeneric is not DeviceImage<ushort> image)
            throw new Exception("TrackedStar only operates on ushort images");
        MaxSearchBox(image, X, Y, _guiderSettings.TrackedStarSearchRadius, out var maxPoint, out var edgeStats);
        sigma = MinSigma(image, maxPoint.x, maxPoint.y, edgeStats.Mean, edgeStats.Stdev, 1);
        if (sigma < _guiderSettings.TrackedStarSigmaRejection)
        {
            failureReason = "bad sigma";
            return false;
        }
        var peak = FitParabola(image, maxPoint.x, maxPoint.y, _guiderSettings.TrackedStarParabolaFitRadius);
        var fitParabolaDistance = Dist(maxPoint.x, maxPoint.y, peak.x, peak.y);
        if (fitParabolaDistance > _guiderSettings.TrackedStarParabolaFitRadius)
        {
            failureReason = $"fit parabola too far ({fitParabolaDistance:F2})";
            return false;
        }
        var shiftDistance = Dist(X, Y, peak.x, peak.y);
        if (shiftDistance > _guiderSettings.TrackedStarMaxShiftDistance)
        {
            failureReason = $"star shifted too far ({shiftDistance:F2})";
            return false;
        }

        failureReason = null;
        X = peak.x;
        Y = peak.y;
        return true;

        double Dist(double x1, double y1, double x2, double y2)
        {
            var x = x2 - x1;
            var y = y2 - y1;
            return Math.Sqrt(x * x + y * y);
        }
    }

    private static void MaxSearchBox(DeviceImage<ushort> image, double guessX, double guessY, double searchRadius, out (uint x, uint y) coords, out MeanStdev edge)
    {
        var searchBox = Box(image.Width, image.Height, guessX, guessY, searchRadius);
        edge = new MeanStdev();
        var value = ushort.MinValue;
        coords = (0, 0);
        for (var y = searchBox.minY; y < searchBox.maxY; y++)
        {
            for (var x = searchBox.minX; x < searchBox.maxX; x++)
            {
                var v = image[x, y];
                if (v > value)
                {
                    value = v;
                    coords = (x, y);
                }
                if (x == searchBox.minX || x + 1 == searchBox.maxX || y == searchBox.minY || y + 1 == searchBox.maxY)
                    edge.Feed(v);
            }
        }
    }

    // for debug viz
    public (uint minX, uint maxX, uint minY, uint maxY) SearchBox(uint width, uint height) => Box(width, height, X, Y, _guiderSettings.TrackedStarSearchRadius);

    private static (uint minX, uint maxX, uint minY, uint maxY) Box(uint width, uint height, double centerX, double centerY, double radius)
    {
        return (
            ClampDouble(centerX - radius, width),
            ClampDouble(centerX + radius, width),
            ClampDouble(centerY - radius, height),
            ClampDouble(centerY + radius, height)
        );

        uint ClampDouble(double value, uint size)
        {
            if (value < 0)
                return 0;
            var val = (uint)Math.Ceiling(value);
            return val > size ? size : val;
        }
    }

    private static double MinSigma(DeviceImage<ushort> image, uint centerX, uint centerY, double mean, double stdev, uint radius)
    {
        var minX = Clamp(centerX, true, radius, image.Width);
        var maxX = Clamp(centerX, false, radius, image.Width);
        var minY = Clamp(centerY, true, radius, image.Height);
        var maxY = Clamp(centerY, false, radius, image.Height);

        var min = double.MaxValue;
        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                var v = image[x, y];
                var sigma = (v - mean) / stdev;
                min = Math.Min(min, sigma);
            }
        }
        return min;
    }

    private static (double x, double y) FitParabola(DeviceImage<ushort> image, uint centerX, uint centerY, uint radius)
    {
        var minX = Clamp(centerX, true, radius, image.Width);
        var maxX = Clamp(centerX, false, radius, image.Width);
        var minY = Clamp(centerY, true, radius, image.Height);
        var maxY = Clamp(centerY, false, radius, image.Height);

        var rows = (maxX - minX) * (maxY - minY);
        var matrix = CreateMatrix.Dense((int)rows, 4, new double[rows * 4]);
        var vector = CreateVector.Dense(new double[rows]);
        var rowIndex = 0;
        for (var y = minY; y < maxY; y++)
        {
            for (var x = minX; x < maxX; x++)
            {
                var v = image[x, y];
                var dx = (double)x - centerX;
                var dy = (double)y - centerY;
                matrix[rowIndex, 0] = dx;
                matrix[rowIndex, 1] = dy;
                matrix[rowIndex, 2] = -(dx * dx + dy * dy);
                matrix[rowIndex, 3] = 1;
                vector[rowIndex] = v;
                rowIndex++;
            }
        }
        var result = matrix.PseudoInverse() * vector;
        var val_2ac = result[0];
        var val_2ad = result[1];
        var val_a = result[2];
        var c = val_2ac / (2.0 * val_a);
        var d = val_2ad / (2.0 * val_a);
        return (centerX + c, centerY + d);
    }

    private static uint Clamp(uint center, bool sub, uint radius, uint size)
    {
        if (sub)
        {
            if (center < radius)
                return 0;
            return center - radius;
        }
        var v = center + radius;
        return v > size ? size : v;
    }
}

internal sealed class TrackedStarUi
{
    private readonly StackPanel _stackPanel;
    private readonly TextBlock _mainText;
    public readonly TrackedStar TrackedStar;

    public (double x, double y)? ReferencePosition;

    public bool MostRecentIsValid;
    public string? FailureReason;
    public double Sigma;

    public Control Control => _stackPanel;

    public TrackedStarUi(TrackedStar trackedStar)
    {
        TrackedStar = trackedStar;
        // TODO: Add way to create new tracked stars
        // TODO: Add way to update the guess of tracked stars
        // TODO: Create UI
        // TODO: Button to remove
        // TODO: Button to temp disable
        // TODO: Button to re-enable?
        _stackPanel = new StackPanel();
        _stackPanel.Children.Add(_mainText = new());
    }

    public static bool TryCreate(GuiderSettings guiderSettings, DeviceImage image, double x, double y, [MaybeNullWhen(false)] out TrackedStarUi trackedStar, [MaybeNullWhen(true)] out string failureReason)
    {
        trackedStar = new TrackedStarUi(new TrackedStar(guiderSettings, x, y));
        trackedStar.UpdateThreaded(image);
        trackedStar.UpdateMainThread();
        failureReason = trackedStar.FailureReason;
        return trackedStar.MostRecentIsValid;
    }

    public void UpdateThreaded(DeviceImage image)
    {
        // TODO: If too many failures in a row, disable this tracked star
        MostRecentIsValid = TrackedStar.Update(image, out FailureReason, out Sigma);
    }

    public void UpdateMainThread()
    {
        _mainText.Text = Description;
    }

    public (double x, double y)? OffsetToReference =>
        this is { MostRecentIsValid: true, ReferencePosition: { x: var ox, y: var oy } }
            ? (TrackedStar.X - ox, TrackedStar.Y - oy)
            : null;

    public string Description
    {
        get
        {
            var s = $"{TrackedStar.X:F2}, {TrackedStar.Y:F2} {Sigma:F1}σ";
            if (!MostRecentIsValid)
                s += " - " + FailureReason;
            if (OffsetToReference is { x: var refX, y: var refY })
                s += $" off:{refX:F2}, {refY:F2}";
            return s;
        }
    }
}

internal sealed class Guider : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1);
    private readonly TextBlock _semaphoreStatus = new();
    private readonly DockPanel _dockPanel = new();
    private readonly StackPanel _cameraButtons = new();
    private readonly StackPanel _mountButtons = new();
    private readonly ToggleSwitch _guidingEnabled = new() { OnContent = "guiding enabled", OffContent = "guiding disabled" };
    private readonly TextBlock _currentPixelOffsetLabel = new() { Text = "pixel offset:" };
    private readonly TextBlock _currentSkyOffsetLabel = new() { Text = "offset:" };
    private readonly TextBlock _calibrationRaLabel = new() { Text = "RA: uncalibrated" };
    private readonly TextBlock _calibrationDecLabel = new() { Text = "Dec: uncalibrated" };
    private readonly TextBlock _calibrationMatrixLabel = new() { Text = "Calibration matrix" };
    public readonly GuiderSettings GuiderSettings = new();
    private readonly StackPanel _starListPanel = new();
    private GuiderOverlay? _guiderOverlay;
    private CameraUiBag? _camera;
    private Mount? _mount;
    private (double x, double y) _currentOffset;

    public readonly List<TrackedStarUi> TrackedStars = [];

    // how many pixels moving one arcsecond in RA/Dec moves the image
    private (double x, double y) _calibrationRa;
    private (double x, double y) _calibrationDec;

    public TabItem Init()
    {
        var stackPanel = new StackPanel();

        RefreshCameraButtons();
        CameraUiBag.AllCamerasChanged += RefreshCameraButtons;
        stackPanel.Children.Add(_cameraButtons);

        RefreshMountButtons();
        Mount.AllMountsChanged += RefreshMountButtons;
        stackPanel.Children.Add(_mountButtons);

        stackPanel.Children.Add(Button("Reset crop", () =>
        {
            foreach (var child in _dockPanel.Children)
                if (child is CroppableImage croppableImage)
                    croppableImage.ResetCrop();
        }));

        stackPanel.Children.Add(_semaphoreStatus);
        stackPanel.Children.Add(ParsedInput("Autoscan", Autoscan));
        stackPanel.Children.Add(FailableToggle("Reference frame", SetReferenceFrame));
        stackPanel.Children.Add(_guidingEnabled);
        stackPanel.Children.Add(new TextBlock { Text = "rate(arcsec/sec), time(sec)" });
        stackPanel.Children.Add(DoubleNumberInput("VSlew RA", (rate, time) => DoVariableSlew(true, rate, time)));
        stackPanel.Children.Add(DoubleNumberInput("VSlew Dec", (rate, time) => DoVariableSlew(false, rate, time)));
        stackPanel.Children.Add(_currentPixelOffsetLabel);
        stackPanel.Children.Add(_currentSkyOffsetLabel);
        /*
        stackPanel.Children.Add(DoubleNumberInput("Calibrate RA", (rate, time) => Calibrate(true, rate, time)));
        stackPanel.Children.Add(DoubleNumberInput("Calibrate Dec", (rate, time) => Calibrate(false, rate, time)));
        stackPanel.Children.Add(DoubleNumberInput("Calibrate both", CalibrateBoth));
        */
        stackPanel.Children.Add(_calibrationRaLabel);
        stackPanel.Children.Add(_calibrationDecLabel);
        stackPanel.Children.Add(_calibrationMatrixLabel);
        stackPanel.Children.Add(_starListPanel);

        _dockPanel.LastChildFill = true;
        _dockPanel.Children.Clear();
        var scrollViewer = new ScrollViewer { Content = stackPanel, [DockPanel.DockProperty] = Dock.Left };
        _dockPanel.Children.Add(scrollViewer);

        OnNewOffsetFeed((0, 0));

        return new TabItem
        {
            Header = "Guider",
            Content = _dockPanel,
        };
    }

    private async Task<Locker> LockAsync()
    {
        await _semaphore.WaitAsync();
        _semaphoreStatus.Text = "status: running";
        return new Locker(this);
    }

    private struct Locker(Guider guider) : IDisposable
    {
        public void Dispose()
        {
            guider._semaphoreStatus.Text = "status: waiting";
            guider._semaphore.Release();
        }
    }

    private async Task DoVariableSlew(bool ra, double arcsecondsPerSecond, double seconds)
    {
        if (_mount == null)
            return;
        using (await LockAsync())
        {
            await _mount.VariableSlewCommand(ra, arcsecondsPerSecond);
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            await _mount.VariableSlewCommand(ra, 0);
        }
    }

    /*
    private async Task CalibrateBoth(double arcsecondsPerSecond, double seconds)
    {
        await Calibrate(true, arcsecondsPerSecond, seconds);
        await Task.Delay(TimeSpan.FromSeconds(1));
        await Calibrate(false, arcsecondsPerSecond, seconds);
    }

    private async Task Calibrate(bool ra, double arcsecondsPerSecond, double seconds)
    {
        if (_mount == null)
            return;
        using (await LockAsync())
        {
            // TODO: RA sensitivity adjustment based on current Dec
            var start = _currentOffset;
            await _mount.VariableSlewCommand(ra, arcsecondsPerSecond);
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            await _mount.VariableSlewCommand(ra, 0);
            await Task.Delay(TimeSpan.FromSeconds(1));
            var finish = await WaitForNextOffset();
            var arcseconds = arcsecondsPerSecond * seconds;
            // pixels per arcsecond
            var deltaX = (finish.x - start.x) / arcseconds;
            var deltaY = (finish.y - start.y) / arcseconds;
            var l = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            // deltaX /= l;
            // deltaY /= l;
            if (ra)
            {
                var theta = Math.Atan2(deltaY, deltaX);
                _calibrationRa = (deltaX, deltaY);
                _calibrationRaLabel.Text = $"RA calibration: {1 / deltaX:F3}, {1 / deltaY:F3}, l={1 / l:F3} \u03B8={theta * (360 / Math.Tau):F3}";
            }
            else
            {
                var theta = Math.Atan2(-deltaX, deltaY); // rotate dec display by 90 degrees to align it with ra
                _calibrationDec = (deltaX, deltaY);
                _calibrationDecLabel.Text = $"Dec calibration: {1 / deltaX:F3}, {1 / deltaY:F3}, l={1 / l:F3} \u03B8={theta * (360 / Math.Tau):F3}";
            }
            var matrix = CalibrationMatrix;
            _calibrationMatrixLabel.Text = $"Calibration matrix:\n{matrix.a:F3}, {matrix.b:F3}\n{matrix.c:F3}, {matrix.d:F3}";
            await _mount.VariableSlewCommand(ra, -arcsecondsPerSecond);
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            await _mount.VariableSlewCommand(ra, 0);
        }
    }
    */

    private Matrix2x2 CalibrationMatrix
    {
        get
        {
            var mat = new Matrix2x2(_calibrationRa.x, _calibrationDec.x, _calibrationRa.y, _calibrationDec.y);
            return mat.Inverse;
        }
    }

    // private Task<(double x, double y)> WaitForNextOffset()
    // {
    //     if (_feeder is not { } feeder)
    //         return Task.FromResult(_currentOffset);

    //     TaskCompletionSource<(double x, double y)> tcs = new();
    //     feeder.MoveNext += FeederOnMoveNext;
    //     return tcs.Task;

    //     void FeederOnMoveNext((double x, double y) obj)
    //     {
    //         feeder.MoveNext -= FeederOnMoveNext;
    //         tcs.SetResult(obj);
    //     }
    // }

    private bool SetReferenceFrame(bool enabled)
    {
        if (enabled)
        {
            foreach (var star in TrackedStars)
            {
                if (!star.MostRecentIsValid)
                {
                    Clear();
                    return false;
                }
            }
            foreach (var star in TrackedStars)
            {
                star.ReferencePosition = (star.TrackedStar.X, star.TrackedStar.Y);
            }
            RedrawOverlay();
            return true;
        }
        Clear();
        return false;

        void Clear()
        {
            foreach (var star in TrackedStars)
                star.ReferencePosition = null;
            RedrawOverlay();
        }
    }

    private void Autoscan(int numberOfStars)
    {
        if (_camera?.Camera.Current is DeviceImage<ushort> image)
            Autoscan(image, numberOfStars);
    }

    private void Autoscan(DeviceImage<ushort> image, int numberOfStars)
    {
        foreach (var star in TrackedStars)
            _starListPanel.Children.Remove(star.Control);
        TrackedStars.Clear();

        Console.WriteLine("Beginning processing");
        var buf = image.Data;
        var copy = new ushort[buf.Length];
        Array.Copy(buf, copy, buf.Length);
        Console.WriteLine("Done copy");
        var indices = new uint[buf.Length];
        for (var i = 0u; i < indices.Length; i++)
            indices[i] = i;
        // TODO: Sorting takes ages, make async?
        Console.WriteLine("Now sorting");
        Array.Sort(copy, indices);
        Console.WriteLine("Now considering stars");

        List<(uint, uint)> alreadyConsidered = [];

        for (var i = copy.Length - 1; i >= copy.Length - 10000; i--)
        {
            var value = copy[i];
            var index = indices[i];
            var x = index % image.Width;
            var y = index / image.Width;
            var reject = value > ushort.MaxValue * GuiderSettings.AutodetectMaximumBrightnessPercent || Reject(x, y);
            alreadyConsidered.Add((x, y));
            if (reject)
                continue;
            TryAddStarAt(image, x, y);
            if (TrackedStars.Count >= numberOfStars)
                break;
        }

        bool Reject(uint x, uint y)
        {
            foreach (var (ex, ey) in alreadyConsidered)
            {
                if (S(x, ex) + S(y, ey) < GuiderSettings.AutodetectExclusionZone)
                    return true;

                static uint S(uint l, uint r)
                {
                    return l < r ? r - l : l - r;
                }
            }
            return false;
        }
    }

    private void TryAddStarAt(DeviceImage image, double x, double y)
    {
        if (TrackedStarUi.TryCreate(GuiderSettings, image, x, y, out var result, out var failureReason))
        {
            TrackedStars.Add(result);
            _starListPanel.Children.Add(result.Control);
            RedrawOverlay();
        }
        else
        {
            Console.WriteLine($"Failed to autoscan star at {x:F2} {y:F2}: {failureReason}");
        }
    }

    private void OnNewOffsetFeedThreaded((double x, double y) current)
    {
    }

    private void OnNewOffsetFeed((double x, double y) current)
    {
        _currentOffset = current;
        _currentPixelOffsetLabel.Text = $"pixel offset: {current.x:F3}, {current.y:F3}";

        if (Math.Abs(_calibrationRa.x) + Math.Abs(_calibrationRa.y) > 0.001 &&
            Math.Abs(_calibrationDec.x) + Math.Abs(_calibrationDec.y) > 0.001)
        {
            var offset = CalibrationMatrix * current;
            _currentSkyOffsetLabel.Text = $"offset: RA={Angle.FromDegrees(offset.x / (60 * 60)).FormatDegrees()}, Dec={Angle.FromDegrees(offset.y / (60 * 60)).FormatDegrees()}";
            if (_mount != null && _guidingEnabled.IsChecked == true)
            {
                // TODO: go! (maybe threaded tho?)
                return;
            }
        }

        if (_guidingEnabled.IsChecked == true)
        {
            _guidingEnabled.IsChecked = false;
        }
    }

    private void CameraOnMoveNext(DeviceImage image)
    {
        foreach (var trackedStar in TrackedStars)
        {
            trackedStar.UpdateThreaded(image);
        }

        var diffX = 0.0;
        var diffY = 0.0;
        var count = 0;
        foreach (var star in TrackedStars)
        {
            if (star.OffsetToReference is { x: var x, y: var y })
            {
                diffX += x;
                diffY += y;
                count++;
            }
        }
        if (count > 0)
        {
            var offset = (diffX / count, diffY / count);
            OnNewOffsetFeedThreaded(offset);

            Dispatcher.UIThread.Post(() =>
            {
                foreach (var trackedStar in TrackedStars)
                {
                    trackedStar.UpdateMainThread();
                }
                RedrawOverlay();
                OnNewOffsetFeed(offset);
            });
        }
    }

    private void RedrawOverlay()
    {
        _guiderOverlay?.InvalidateVisual();
    }

    private void RefreshCameraButtons()
    {
        _cameraButtons.Children.Clear();
        foreach (var camera in CameraUiBag.AllCameras)
        {
            _cameraButtons.Children.Add(Button($"view {camera.Camera.CameraId.Id}", () =>
            {
                _dockPanel.Children.RemoveRange(1, _dockPanel.Children.Count - 1);
                var croppableImage = BitmapDisplay.Create(camera.BitmapProcessor);
                croppableImage.Children.Add(_guiderOverlay = new GuiderOverlay(this, croppableImage));
                _dockPanel.Children.Add(croppableImage);
                if (_camera is { } c)
                    c.Camera.MoveNext -= CameraOnMoveNext;
                foreach (var star in TrackedStars)
                    _starListPanel.Children.Remove(star.Control);
                TrackedStars.Clear();
                _camera = camera;
                _camera.Camera.MoveNext += CameraOnMoveNext;
            }));
        }
    }

    private void RefreshMountButtons()
    {
        _mountButtons.Children.Clear();
        foreach (var mount in Mount.AllMounts)
            _mountButtons.Children.Add(Button($"use mount {mount.Name}", () => _mount = mount));
    }

    public void Dispose()
    {
        CameraUiBag.AllCamerasChanged -= RefreshCameraButtons;
        Mount.AllMountsChanged -= RefreshMountButtons;
        if (_camera is { } camera)
            camera.Camera.MoveNext -= CameraOnMoveNext;
        _semaphore.Dispose();
    }
}

internal class GuiderOverlay : Control
{
    private readonly Guider _guider;
    private readonly CroppableImage _image;

    public GuiderOverlay(Guider guider, CroppableImage image)
    {
        _guider = guider;
        _image = image;
        ClipToBounds = true;
    }

    private static readonly IBrush RedBrush = Brush.Parse("#ff0000");
    private static readonly IPen RedPen = new Pen(Brush.Parse("#ff0000"));
    private static readonly IPen BluePen = new Pen(Brush.Parse("#0000ff"));

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var renderSize = Bounds.Size;
        var imageSize = _image.FullSize;
        var crop = _image.CurrentCrop;
        if (crop.Width <= 0 || crop.Height <= 0)
            crop = new PixelRect(0, 0, (int)imageSize.Width, (int)imageSize.Height);
        // The 0.5 is due to avalonia having the top left of a pixel be the coordinate. We want the middle of the pixel instead.
        var imageToScreen = Matrix.CreateTranslation(-crop.X + 0.5, -crop.Y + 0.5) * Matrix.CreateScale(renderSize.Width / crop.Width, renderSize.Height / crop.Height);
        foreach (var star in _guider.TrackedStars)
        {
            var actual = new Point(star.TrackedStar.X, star.TrackedStar.Y) * imageToScreen;
            const double size = 10;
            const double sizeRoot2 = 1.414 * size;
            context.DrawLine(RedPen, new Point(actual.X - size, actual.Y), new Point(actual.X + size, actual.Y));
            context.DrawLine(RedPen, new Point(actual.X, actual.Y - size), new Point(actual.X, actual.Y + size));
            if (star.ReferencePosition is { } refPos)
            {
                var reference = new Point(refPos.x, refPos.y) * imageToScreen;
                context.DrawLine(BluePen, new Point(reference.X - sizeRoot2, reference.Y - sizeRoot2), new Point(reference.X + sizeRoot2, reference.Y + sizeRoot2));
                context.DrawLine(BluePen, new Point(reference.X - sizeRoot2, reference.Y + sizeRoot2), new Point(reference.X + sizeRoot2, reference.Y - sizeRoot2));
            }
            var searchBox = star.TrackedStar.SearchBox((uint)imageSize.Width, (uint)imageSize.Height);
            var searchRect = new Rect(new Point(searchBox.minX, searchBox.minY) * imageToScreen, new Point(searchBox.maxX, searchBox.maxY) * imageToScreen);
            context.DrawRectangle(RedPen, searchRect);
            var description = new FormattedText(star.Description, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, RedBrush);
            context.DrawText(description, new Point(actual.X - description.Width / 2, actual.Y + size + 1));
        }
    }
}
