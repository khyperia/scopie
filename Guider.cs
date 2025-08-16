using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Threading;
using MathNet.Numerics.LinearAlgebra;
using static Scopie.Ext;

namespace Scopie;

internal sealed class GuiderSettings
{
    // TODO: Autodetect exclusion zone radius (2x search radius?)
    public uint AutodetectExclusionZone = 100;
    public double TrackedStarSearchRadius = 50;
    public double TrackedStarMaxShiftDistance = 37.5;
    public uint TrackedStarParabolaFitRadius = 2;
    public double TrackedStarSigmaRejection = 5;
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
        MaxSearchBox(image, X, Y, _guiderSettings.TrackedStarSearchRadius, out var maxPoint, out var maxValue, out var edgeStats);
        sigma = (maxValue - edgeStats.Mean) / edgeStats.Stdev;
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

    private static void MaxSearchBox(DeviceImage<ushort> image, double guessX, double guessY, double searchRadius, out (uint x, uint y) coords, out ushort value, out MeanStdev edge)
    {
        var searchBox = Box(image, guessX, guessY, searchRadius);
        edge = new MeanStdev();
        value = ushort.MinValue;
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
                if (x == searchBox.minX || x == searchBox.maxX || y == searchBox.minY || y == searchBox.maxY)
                    edge.Feed(v);
            }
        }
    }

    private static (uint minX, uint maxX, uint minY, uint maxY) Box(DeviceImage image, double centerX, double centerY, double radius)
    {
        return (
            Clamp(centerX - radius, image.Width),
            Clamp(centerX + radius, image.Width),
            Clamp(centerY - radius, image.Height),
            Clamp(centerY + radius, image.Height)
        );

        uint Clamp(double value, uint size)
        {
            if (value < 0)
                return 0;
            var val = (uint)value;
            return val > size ? size : val;
        }
    }

    private static (double x, double y) FitParabola(DeviceImage<ushort> image, uint centerX, uint centerY, uint radius)
    {
        var minX = Clamp(centerX, true, image.Width);
        var maxX = Clamp(centerX, false, image.Width);
        var minY = Clamp(centerY, true, image.Height);
        var maxY = Clamp(centerY, false, image.Height);

        var rows = (maxX - minX) * (maxY - minY);
        var matrix = CreateMatrix.Dense((int)rows, 4, new double[rows * 4]);
        var vector = CreateVector.Dense(new double[rows * 4]);
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
                matrix[rowIndex, 2] = -(x * x + y * y);
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
        return (centerX + c, centerX + d);

        uint Clamp(uint center, bool sub, uint size)
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
}

internal sealed class TrackedStarUi
{
    private readonly StackPanel _stackPanel;
    private readonly TextBlock _mainText;
    public TrackedStar TrackedStar;
    public bool MostRecentIsValid { get; private set; }

    public (double x, double y)? ReferencePosition;

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

    public static bool TryCreate(GuiderSettings guiderSettings, DeviceImage image, double x, double y, [MaybeNullWhen(false)] out TrackedStarUi trackedStar)
    {
        trackedStar = new TrackedStarUi(new TrackedStar(guiderSettings, x, y));
        trackedStar.Update(image);
        return trackedStar.MostRecentIsValid;
    }

    public void Update(DeviceImage image)
    {
        // TODO: If too many failures in a row, disable this tracked star
        MostRecentIsValid = TrackedStar.Update(image, out var failureReason, out var sigma);
        var s = $"{TrackedStar.X:F2},{TrackedStar.Y:F2} {sigma:F1}σ";
        if (!MostRecentIsValid)
            s += " - " + failureReason;
        if (ReferencePosition is { x: var refX, y: var refY })
            s += $" off:{refX:F2},{refY:F2}";
        _mainText.Text = s;
    }
}

/*
internal sealed class StarGuiderOffset
{
    private readonly List<TrackedStarUi> _trackedStars;
    private readonly Dictionary<TrackedStar, (double x, double y)> _initial;

    public StarGuiderOffset(List<TrackedStarUi> trackedStars, Dictionary<TrackedStar, (double x, double y)> initial)
    {
        _trackedStars = trackedStars;
        _initial = initial;
    }

    public static bool TryCreate(List<TrackedStarUi> trackedStars, [MaybeNullWhen(false)] out StarGuiderOffset result)
    {
        Dictionary<TrackedStar, (double x, double y)> initial = new();
        foreach (var star in trackedStars)
        {
            if (!star.MostRecentIsValid)
            {
                result = null;
                return false;
            }
            initial.Add(star.TrackedStar, (star.TrackedStar.X, star.TrackedStar.Y));
        }
        result = new StarGuiderOffset(trackedStars, initial);
        return true;
    }

    public bool Offset(out (double x, double y) result)
    {
        var diffX = 0.0;
        var diffY = 0.0;
        var count = 0;
        foreach (var star in _trackedStars)
        {
            if (star.MostRecentIsValid && _initial.TryGetValue(star.TrackedStar, out var original))
            {
                diffX += star.TrackedStar.X - original.x;
                diffY += star.TrackedStar.Y - original.y;
                count++;
            }
        }
        if (count == 0)
        {
            result = default;
            return false;
        }
        result = (diffX / count, diffY / count);
        return true;
    }
}
*/

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
    private readonly GuiderSettings _guiderSettings = new();
    private readonly StackPanel _starListPanel = new();
    private CameraUiBag? _camera;
    private Mount? _mount;
    private (double x, double y) _currentOffset;

    private readonly List<TrackedStarUi> _trackedStars = [];

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
        stackPanel.Children.Add(DoubleNumberInput("Calibrate RA", (rate, time) => Calibrate(true, rate, time)));
        stackPanel.Children.Add(DoubleNumberInput("Calibrate Dec", (rate, time) => Calibrate(false, rate, time)));
        stackPanel.Children.Add(DoubleNumberInput("Calibrate both", CalibrateBoth));
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
            foreach (var star in _trackedStars)
            {
                if (!star.MostRecentIsValid)
                {
                    Clear();
                    return false;
                }
            }
            foreach (var star in _trackedStars)
            {
                star.ReferencePosition = (star.TrackedStar.X, star.TrackedStar.Y);
            }
            return true;
        }
        Clear();
        return false;

        void Clear()
        {
            foreach (var star in _trackedStars)
                star.ReferencePosition = null;
        }
    }

    private void Autoscan(int numberOfStars)
    {
        if (_camera?.Camera.Current is DeviceImage<ushort> image)
            Autoscan(image, numberOfStars);
    }

    private void Autoscan(DeviceImage<ushort> image, int numberOfStars)
    {
        foreach (var star in _trackedStars)
            _starListPanel.Children.Remove(star.Control);
        _trackedStars.Clear();

        List<(uint, uint)> alreadyFound = [];
        for (var i = 0; i < numberOfStars * 2; i++)
        {
            var v = Max();
            alreadyFound.Add(v);
            TryAddStarAt(image, v.x, v.y);
            if (_trackedStars.Count >= numberOfStars)
                break;
        }
        return;

        (uint x, uint y) Max()
        {
            var value = ushort.MinValue;
            (uint x, uint y) coords = default;
            for (var y = 0u; y < image.Height; y++)
            {
                for (var x = 0u; x < image.Width; x++)
                {
                    if (Reject(x, y))
                        continue;
                    var v = image[x, y];
                    if (v > value)
                    {
                        value = v;
                        coords = (x, y);
                    }
                }
            }
            return coords;
        }

        bool Reject(uint x, uint y)
        {
            foreach (var (ex, ey) in alreadyFound)
            {
                if (S(x, ex) + S(y, ey) < _guiderSettings.AutodetectExclusionZone)
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
        if (TrackedStarUi.TryCreate(_guiderSettings, image, x, y, out var result))
        {
            _trackedStars.Add(result);
            _starListPanel.Children.Add(result.Control);
        }
    }

    private void OnNewOffsetFeedThreaded((double x, double y) current)
    {
        Dispatcher.UIThread.Invoke(() => OnNewOffsetFeed(current));
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
        foreach (var trackedStar in _trackedStars)
        {
            trackedStar.Update(image);
        }

        var diffX = 0.0;
        var diffY = 0.0;
        var count = 0;
        foreach (var star in _trackedStars)
        {
            if (star is { MostRecentIsValid: true, ReferencePosition: { x: var ox, y: var oy } })
            {
                diffX += star.TrackedStar.X - ox;
                diffY += star.TrackedStar.Y - oy;
                count++;
            }
        }
        if (count > 0)
        {
            var offset = (diffX / count, diffY / count);
            OnNewOffsetFeedThreaded(offset);
        }
    }

    private void RefreshCameraButtons()
    {
        _cameraButtons.Children.Clear();
        foreach (var camera in CameraUiBag.AllCameras)
        {
            _cameraButtons.Children.Add(Button($"view {camera.Camera.CameraId.Id}", () =>
            {
                _dockPanel.Children.RemoveRange(1, _dockPanel.Children.Count - 1);
                _dockPanel.Children.Add(BitmapDisplay.Create(camera.BitmapProcessor));
                if (_camera is { } c)
                    c.Camera.MoveNext -= CameraOnMoveNext;
                foreach (var star in _trackedStars)
                    _starListPanel.Children.Remove(star.Control);
                _trackedStars.Clear();
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
