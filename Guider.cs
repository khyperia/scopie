using System.Diagnostics;
using System.Numerics;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using static Scopie.ExceptionReporter;
using static Scopie.Ext;

namespace Scopie;

internal interface IGuiderOffset
{
    (double x, double y) Offset(DeviceImage image);
}

internal sealed class FftGuiderOffset : IGuiderOffset
{
    private readonly uint _size;
    private readonly FFT2d _fft2d;
    private readonly DeviceImage<Complex> _reference;
    private readonly DeviceImage<Complex> _scratch;

    public FftGuiderOffset(uint size, DeviceImage reference)
    {
        size = Math.Min(size, 1u << BitOperations.Log2(Math.Min(reference.Width, reference.Height)));

        _size = size;
        _fft2d = new FFT2d(size, size);
        _reference = new DeviceImage<Complex>(new Complex[size * size], size, size);
        _scratch = new DeviceImage<Complex>(new Complex[size * size], size, size);
        Crop(reference, _reference);
        _fft2d.Run(_reference, _reference);
    }

    public (double x, double y) Offset(DeviceImage image)
    {
        Crop(image, _scratch);
        _fft2d.Run(_scratch, _scratch);
        AutocorrelationMultiply(_scratch.Data, _reference.Data);
        _fft2d.Run(_scratch, _scratch);
        return FindPeak(_scratch);
    }

    private static void AutocorrelationMultiply(Complex[] left, Complex[] right)
    {
        Debug.Assert(left.Length == right.Length);
        for (var i = 0; i < left.Length; i++)
            left[i] *= Complex.Conjugate(right[i]);
    }

    private void Crop(DeviceImage input, DeviceImage<Complex> output)
    {
        var startX = input.Width / 2 - _size / 2;
        var startY = input.Height / 2 - _size / 2;
        switch (input)
        {
            case DeviceImage<ushort> inputU:
            {
                for (var dy = 0u; dy < _size; dy++)
                    for (var dx = 0u; dx < _size; dx++)
                        output[dx, dy] = inputU[startX + dx, startY + dy];
                break;
            }
            case DeviceImage<byte> inputB:
            {
                for (var dy = 0u; dy < _size; dy++)
                    for (var dx = 0u; dx < _size; dx++)
                        output[dx, dy] = inputB[startX + dx, startY + dy];
                break;
            }
            default:
                throw new Exception("unsupported image type " + input.GetType());
        }
    }

    private static (double x, double y) FindPeak(DeviceImage<Complex> image)
    {
        // Note: There is a strong tendency to align the noise patterns rather than the data patterns in astronomy images (i.e. returns nearly 0,0 offset).
        // Wipe the data from 0,0 to hopefully reduce that.
        NukeZeroZero(image);

        var max = Max(image.Data);
        var xInt = max % (int)image.Width;
        var yInt = max / (int)image.Width;
        var center = Index(image, xInt, yInt);
        var x = xInt + CenterOfParabola(Index(image, xInt - 1, yInt), center, Index(image, xInt + 1, yInt));
        var y = yInt + CenterOfParabola(Index(image, xInt, yInt - 1), center, Index(image, xInt, yInt + 1));
        if (x > image.Width / 2.0)
            x -= image.Width;
        if (y > image.Height / 2.0)
            y -= image.Height;
        return (x, y);

        static int Max(Complex[] data)
        {
            var maxValue = double.MinValue;
            var index = -1;
            for (var i = 0; i < data.Length; i++)
            {
                var v = data[i];
                var v2 = v.Real * v.Real + v.Imaginary * v.Imaginary;
                if (v2 > maxValue)
                {
                    maxValue = v2;
                    index = i;
                }
            }
            return index;
        }

        static double Index(DeviceImage<Complex> image, int x, int y) => IndexC(image, x, y).Magnitude;

        static Complex IndexC(DeviceImage<Complex> image, int x, int y)
        {
            x = x.Mod((int)image.Width);
            y = y.Mod((int)image.Height);
            return image[(uint)x, (uint)y];
        }

        // given three points:
        // (-1, n)
        // (0, z)
        // (1, p)
        // return the X coordinate of the center of the parabola going through the three points
        static double CenterOfParabola(double n, double z, double p) => (n - p) / (2 * (n + p - 2 * z));

        static void NukeZeroZero(DeviceImage<Complex> image)
        {
            var n1X = IndexC(image, -1, 0);
            var n1Y = IndexC(image, 0, -1);
            var p1X = IndexC(image, 1, 0);
            var p1Y = IndexC(image, 0, 1);
            var v = (n1X + n1Y + p1X + p1Y) * 0.25;
            image[0, 0] = v;
        }
    }
}

internal sealed class GuiderOffsetFeeder : CpuHeavySkippablePushProcessor<DeviceImage, (double x, double y)>
{
    private readonly IGuiderOffset _computer;

    public GuiderOffsetFeeder(IGuiderOffset computer, IPushEnumerable<DeviceImage> input) : base(input)
    {
        _computer = computer;
    }

    protected override bool ProcessSlowThreaded(DeviceImage item, out (double x, double y) result)
    {
        var offset = _computer.Offset(item);
        result = offset;
        return true;
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
    private CameraUiBag? _camera;
    private Mount? _mount;
    private GuiderOffsetFeeder? _feeder;
    private (double x, double y) _currentOffset;

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

        stackPanel.Children.Add(Button("Begin", Begin));
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
                _calibrationRaLabel.Text = $"RA calibration: {deltaX}, {deltaY}, l={l} \u03B8={theta * (360 / Math.Tau)}";
            }
            else
            {
                var theta = Math.Atan2(-deltaX, deltaY); // rotate dec display by 90 degrees to align it with ra
                _calibrationDec = (deltaX, deltaY);
                _calibrationDecLabel.Text = $"Dec calibration: {deltaX}, {deltaY}, l={l} \u03B8={theta * (360 / Math.Tau)}";
            }
            var matrix = CalibrationMatrix;
            _calibrationMatrixLabel.Text = $"Calibration matrix:\n{matrix.a}, {matrix.b}\n{matrix.c}, {matrix.d}";
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

    private Task<(double x, double y)> WaitForNextOffset()
    {
        if (_feeder is not { } feeder)
            return Task.FromResult(_currentOffset);

        TaskCompletionSource<(double x, double y)> tcs = new();
        feeder.MoveNext += FeederOnMoveNext;
        return tcs.Task;

        void FeederOnMoveNext((double x, double y) obj)
        {
            feeder.MoveNext -= FeederOnMoveNext;
            tcs.SetResult(obj);
        }
    }

    private static StackPanel StringButtonInput(string buttonName, Func<string, Task> onSet)
    {
        var first = new TextBox();
        var button = new Button { Content = buttonName };
        button.Click += (_, _) =>
        {
            if (first.Text is { } text)
                Try(onSet(text));
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                first,
                button
            }
        };
    }

    private static StackPanel DoubleNumberInput(string buttonName, Func<double, double, Task> onSet)
    {
        var first = new TextBox();
        var second = new TextBox();
        var button = new Button { Content = buttonName };
        var reentrant = false;
        first.TextChanged += TextChanged;
        second.TextChanged += TextChanged;
        button.Click += (_, _) =>
        {
            if (double.TryParse(first.Text, out var f) && double.TryParse(second.Text, out var s))
                Try(onSet(f, s));
        };

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                first,
                second,
                button
            }
        };

        void TextChanged(object? sender, TextChangedEventArgs e)
        {
            if (reentrant)
                return;
            try
            {
                reentrant = true;
                var firstText = first.Text;
                if (firstText != null)
                {
                    var idx = firstText.IndexOf(',');
                    if (idx != -1)
                    {
                        first.Text = firstText[..idx].Trim();
                        second.Text = firstText[(idx + 1)..].Trim();
                        if (first.IsFocused)
                            second.Focus();
                    }
                }

                button.IsEnabled = double.TryParse(first.Text, out _) && double.TryParse(second.Text, out _);
            }
            finally
            {
                reentrant = false;
            }
        }
    }

    private void Begin()
    {
        if (_camera?.Camera.Current is { } reference)
            Begin(new FftGuiderOffset(1 << 28, reference));
    }

    private void Begin(IGuiderOffset guiderOffset)
    {
        if (_feeder is { } feeder)
        {
            feeder.MoveNext -= OnNewOffsetFeedThreaded;
            feeder.Dispose();
        }

        var camera = _camera?.Camera;
        if (camera?.Current == null)
            return;
        _feeder = new GuiderOffsetFeeder(guiderOffset, camera);
        OnNewOffsetFeed(_feeder.Current);
        _feeder.MoveNext += OnNewOffsetFeedThreaded;
    }

    private void OnNewOffsetFeedThreaded((double x, double y) current)
    {
        Dispatcher.UIThread.Invoke(() => OnNewOffsetFeed(current));
    }

    private void OnNewOffsetFeed((double x, double y) current)
    {
        _currentOffset = current;
        _currentPixelOffsetLabel.Text = $"pixel offset: {current.x}, {current.y}";

        if (Math.Abs(_calibrationRa.x) + Math.Abs(_calibrationRa.y) > 0.001 &&
            Math.Abs(_calibrationDec.x) + Math.Abs(_calibrationDec.y) > 0.001)
        {
            var offset = CalibrationMatrix * current;
            _currentSkyOffsetLabel.Text = $"offset: RA={Angle.FromDegrees(offset.x * 60 * 60).FormatDegrees()}, Dec={Angle.FromDegrees(offset.y * 60 * 60).FormatDegrees()}";
            if (_mount != null && _guidingEnabled.IsChecked == true)
            {
                // go!
                return;
            }
        }

        if (_guidingEnabled.IsChecked == true)
        {
            _guidingEnabled.IsChecked = false;
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
                _camera = camera;
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
        if (_feeder is { } feeder)
        {
            feeder.MoveNext -= OnNewOffsetFeedThreaded;
            feeder.Dispose();
        }
    }
}
