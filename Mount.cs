﻿using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using static Scopie.ExceptionReporter;

namespace Scopie;

internal readonly struct Lockie<T> : IDisposable
{
    private readonly T _value;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public Lockie(T value)
    {
        _value = value;
    }

    public readonly struct LockGuard(T value, SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
        private T Value => value;
        public static implicit operator T(LockGuard guard) => guard.Value;
    }

    public async Task<LockGuard> Lock()
    {
        await _semaphore.WaitAsync();
        return new LockGuard(_value, _semaphore);
    }

    public LockGuard LockSync()
    {
        _semaphore.Wait();
        return new LockGuard(_value, _semaphore);
    }

    public void Dispose() => _semaphore.Dispose();
}

internal sealed class Mount : IDisposable
{
    private readonly Lockie<SerialPort> _lockie;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly CancellationToken _ct;
    private readonly byte[] _hash = new byte[1];
    private readonly Image _image = new() { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.Both };
    private readonly StackPanel _cameraButtons = new();
    private Camera? _currentlyDisplaying;

    public Mount(string portName)
    {
        _ct = _cancellationTokenSource.Token;
        var port = new SerialPort(portName);
        port.Open();
        _lockie = new Lockie<SerialPort>(port);
    }

    public async Task<TabItem> Init()
    {
        var getRaDec = new Label();
        var getAzAlt = new Label();
        var trackingMode = new Label();
        var location = new Label();
        var time = new Label();
        var aligned = new Label();

        var stackPanel = new StackPanel();
        stackPanel.Children.Add(getRaDec);
        stackPanel.Children.Add(getAzAlt);
        stackPanel.Children.Add(trackingMode);
        stackPanel.Children.Add(location);
        stackPanel.Children.Add(time);
        stackPanel.Children.Add(aligned);
        stackPanel.Children.Add(DoubleAngleInput("slew", SlewRaDec));
        stackPanel.Children.Add(DoubleAngleInput("slew az/alt", SlewAzAlt));
        stackPanel.Children.Add(DoubleAngleInput("sync ra/dec", SyncRaDec));
        var cancel = new Button { Content = "cancel slew" };
        cancel.Click += (_, _) => Try(CancelSlew());
        stackPanel.Children.Add(cancel);
        foreach (var mode in Enum.GetValues<TrackingMode>())
        {
            var radio = new RadioButton { GroupName = nameof(Scopie.TrackingMode), Content = $"tracking mode {mode}" };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked ?? false)
                    Try(SetTrackingMode(mode));
            };
        }

        stackPanel.Children.Add(DoubleAngleInput("location", SetLocation));
        var setTimeNow = new Button { Content = "set time now" };
        setTimeNow.Click += (_, _) => Try(SetTime(MountTime.Now));
        stackPanel.Children.Add(setTimeNow);

        SlewButtons(stackPanel);

        RefreshCameraButtons();
        Camera.AllCamerasChanged += RefreshCameraButtons;
        stackPanel.Children.Add(_cameraButtons);

        _ = new Platesolver(stackPanel, () => _currentlyDisplaying?.DeviceImage);

        await UpdateStatus();

        Try(UpdateStatusLoop());

        return new TabItem
        {
            Header = "Mount",
            Content = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                Children =
                {
                    stackPanel,
                    _image,
                }
            }
        };

        StackPanel DoubleAngleInput(string buttonName, Func<Angle, Angle, Task> onSet)
        {
            var first = new TextBox();
            var second = new TextBox();
            var button = new Button { Content = buttonName };
            first.TextChanged += TextChanged;
            second.TextChanged += TextChanged;
            button.Click += (_, _) =>
            {
                if (Angle.TryParse(first.Text, out var f) && Angle.TryParse(second.Text, out var s))
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
                button.IsEnabled = Angle.TryParse(first.Text, out _) && Angle.TryParse(second.Text, out _);
            }
        }

        async Task UpdateStatus()
        {
            var (ra, dec) = await GetRaDec();
            getRaDec.Content = $"ra/dec: {ra.FormatHours()} {dec.FormatDegrees()}";
            var (az, alt) = await GetAzAlt();
            getAzAlt.Content = $"az/alt: {az.FormatDegrees()} {alt.FormatDegrees()}";
            trackingMode.Content = $"tracking mode: {await TrackingMode()}";
            aligned.Content = $"aligned: {await Aligned()}";
            var (lat, lon) = await Location();
            location.Content = $"location: {lat.FormatDegrees()} {lon.FormatDegrees()}";
            time.Content = $"time: {await Time()}";
        }

        async Task UpdateStatusLoop()
        {
            while (!_ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), _ct);
                await UpdateStatus();
            }
        }
    }

    private void SlewButtons(StackPanel stackPanel)
    {
        var slewSpeed = 1;
        var slewSpeedLabel = new Label { Content = "slew speed: 1" };
        var slewSpeedPlus = new Button { Content = "+" };
        var slewSpeedMinus = new Button { Content = "-" };
        var raPlus = new Button { Content = "ra+" };
        var raMinus = new Button { Content = "ra-" };
        var decPlus = new Button { Content = "dec+" };
        var decMinus = new Button { Content = "dec-" };

        slewSpeedPlus.Click += (_, _) =>
        {
            slewSpeed = Math.Min(slewSpeed + 1, 9);
            slewSpeedLabel.Content = $"slew speed: {slewSpeed}";
        };
        slewSpeedMinus.Click += (_, _) =>
        {
            slewSpeed = Math.Max(slewSpeed - 1, 1);
            slewSpeedLabel.Content = $"slew speed: {slewSpeed}";
        };
        raPlus.PointerPressed += (_, _) => Try(FixedSlewRa(slewSpeed));
        raPlus.PointerReleased += (_, _) => Try(FixedSlewRa(0));
        raMinus.PointerPressed += (_, _) => Try(FixedSlewRa(-slewSpeed));
        raMinus.PointerReleased += (_, _) => Try(FixedSlewRa(0));
        decPlus.PointerPressed += (_, _) => Try(FixedSlewDec(slewSpeed));
        decPlus.PointerReleased += (_, _) => Try(FixedSlewDec(0));
        decMinus.PointerPressed += (_, _) => Try(FixedSlewDec(-slewSpeed));
        decMinus.PointerReleased += (_, _) => Try(FixedSlewDec(0));

        stackPanel.Children.Add(new StackPanel()
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                slewSpeedLabel,
                slewSpeedMinus,
                slewSpeedPlus,
            }
        });
        stackPanel.Children.Add(decPlus);
        stackPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                raMinus,
                raPlus,
            }
        });
        stackPanel.Children.Add(raMinus);
        stackPanel.Children.Add(decMinus);
    }

    private void RefreshCameraButtons()
    {
        _cameraButtons.Children.Clear();
        foreach (var camera in Camera.AllCameras)
        {
            var button = new Button { Content = $"view {camera.CameraId.Id}" };
            button.Click += (_, _) =>
            {
                if (_currentlyDisplaying != null)
                    _currentlyDisplaying.NewBitmap -= NewBitmap;
                _currentlyDisplaying = camera;
                _currentlyDisplaying.NewBitmap += NewBitmap;
                if (_currentlyDisplaying.Bitmap is { } bitmap)
                    NewBitmap(bitmap);
            };
        }
    }

    private void NewBitmap(IImage bitmap) => _image.Source = bitmap;

    public void Dispose()
    {
        Camera.AllCamerasChanged -= RefreshCameraButtons;
        if (_currentlyDisplaying != null)
            _currentlyDisplaying.NewBitmap -= NewBitmap;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        using var guard = _lockie.LockSync();
        SerialPort port = guard;
        if (port.IsOpen)
            port.Close();
        port.Dispose();
        _lockie.Dispose();
    }

    private async Task Read(SerialPort port, byte[] data)
    {
        await port.BaseStream.ReadExactlyAsync(data, _ct);
        await port.BaseStream.ReadExactlyAsync(_hash, _ct);
        if (_hash[0] != '#')
            throw new Exception("Mount reply didn't end with '#'");
    }

    private async Task Write(SerialPort port, byte[] data)
    {
        await port.BaseStream.WriteAsync(data, _ct);
        await port.BaseStream.FlushAsync(_ct);
    }

    private async Task<byte[]> Interact(byte[] data, int responseLength)
    {
        using var guard = await _lockie.Lock();
        SerialPort port = guard;
        await Write(port, data);
        var result = new byte[responseLength];
        await Read(port, result);
        return data;
    }

    private async Task<(Angle, Angle)> GetAngleCommand(char command)
    {
        var result = await Interact([(byte)command], 17);
        var str = Encoding.UTF8.GetString(result);
        if (str[8] != ',')
            throw new Exception("Bad comma in GetAngleCommand: " + str);
        var ra = Convert.ToUInt32(str[..8], 16);
        var dec = Convert.ToUInt32(str[9..], 16);
        return (Angle.FromUint(ra), Angle.FromUint(dec));
    }

    private Task<byte[]> SendAngleCommand(char command, Angle first, Angle second)
    {
        var str = $"{command}{first.Uint:X8},{second.Uint:X8}";
        var bytes = Encoding.UTF8.GetBytes(str);
        if (bytes.Length != 17)
            throw new Exception($"Internal error in SendAngleCommand ({bytes.Length}): {bytes}");
        return Interact(bytes, 0);
    }

    private Task<(Angle ra, Angle dec)> GetRaDec() => GetAngleCommand('e');

    private Task SyncRaDec(Angle ra, Angle dec) => SendAngleCommand('s', ra, dec);

    private Task SlewRaDec(Angle ra, Angle dec) => SendAngleCommand('r', ra, dec);

    private Task<(Angle ra, Angle dec)> GetAzAlt() => GetAngleCommand('z');

    // note: This assumes the telescope's az axis is straight up.
    // This is NOT what is reported in GetAzAlt
    private Task SlewAzAlt(Angle ra, Angle dec) => SendAngleCommand('s', ra, dec);

    private Task CancelSlew() => Interact([(byte)'M'], 0);

    private async Task<TrackingMode> TrackingMode()
    {
        var res = await Interact([(byte)'t'], 1);
        return (TrackingMode)res[0];
    }

    private Task SetTrackingMode(TrackingMode mode) => Interact([(byte)'T', (byte)mode], 0);

    private static byte[] FormatLatLon(char cmd, Angle lat, Angle lon)
    {
        var (latSign, latDeg, latMin, latSec, _) = lat.Dms;
        var (lonSign, lonDeg, lonMin, lonSec, _) = lon.Dms;
        // The format of the location commands is: ABCDEFGH, where:
        // A is the number of degrees of latitude.
        // B is the number of minutes of latitude.
        // C is the number of seconds of latitude.
        // D is 0 for north and 1 for south.
        // E is the number of degrees of longitude.
        // F is the number of minutes of longitude.
        // G is the number of seconds of longitude.
        // H is 0 for east and 1 for west.
        return
        [
            (byte)cmd,
            (byte)latDeg,
            (byte)latMin,
            (byte)latSec,
            latSign ? (byte)1 : (byte)0,
            (byte)lonDeg,
            (byte)lonMin,
            (byte)lonSec,
            lonSign ? (byte)1 : (byte)0,
        ];
    }

    private static (Angle lat, Angle lon) ParseLatLon(byte[] value)
    {
        var latDeg = (double)value[0];
        var latMin = (double)value[1];
        var latSec = (double)value[2];
        var latSign = value[3] == 1;
        var lonDeg = (double)value[4];
        var lonMin = (double)value[5];
        var lonSec = (double)value[6];
        var lonSign = value[7] == 1;
        var lat = Angle.FromDms(latSign, latDeg, latMin, latSec, 0.0);
        var lon = Angle.FromDms(lonSign, lonDeg, lonMin, lonSec, 0.0);
        return (lat, lon);
    }

    private async Task<(Angle lat, Angle lon)> Location()
    {
        var result = await Interact([(byte)'w'], 8);
        return ParseLatLon(result);
    }

    private Task SetLocation(Angle lat, Angle lon) => Interact(FormatLatLon('W', lat, lon), 0);

    private async Task<MountTime> Time()
    {
        var result = await Interact([(byte)'h'], 8);
        return MountTime.ParseTime(result);
    }

    private Task SetTime(MountTime mountTime) => Interact(mountTime.FormatTime('H'), 0);

    private async Task<bool> Aligned()
    {
        var result = await Interact([(byte)'J'], 1);
        return result[0] != 0;
    }

    private Task<byte[]> FixedSlewCommand(byte one, byte two, byte three, byte rate) => Interact([(byte)'P', one, two, three, rate, 0, 0, 0], 0);

    private Task FixedSlewRa(int speed) => speed > 0
        ? FixedSlewCommand(2, 16, 36, (byte)speed)
        : FixedSlewCommand(2, 16, 37, (byte)-speed);

    private Task FixedSlewDec(int speed) => speed > 0
        ? FixedSlewCommand(2, 17, 36, (byte)speed)
        : FixedSlewCommand(2, 17, 37, (byte)-speed);
}

internal enum TrackingMode
{
    Off,
    AltAz,
    Equatorial,
    SiderealPec,
}

internal readonly partial struct Angle(double value)
{
    private const double MaxUint = (double)uint.MaxValue + 1;
    public static Angle FromUint(uint angle) => new(angle / MaxUint);
    public uint Uint => (uint)((value - Math.Floor(value)) * MaxUint);

    private double Degrees => value * 360.0;
    public static Angle FromDegrees(double value) => new(value / 360.0);
    private static Angle FromHours(double value) => new(value / 24.0);
    private double Hours => value * 24.0;

    public (bool, uint, uint, uint, double) Dms => ValueToXms(Degrees);
    private (bool, uint, uint, uint, double) Hms => ValueToXms(Hours);

    public static Angle FromDms(bool isNegative, double degrees, double minutes, double seconds, double remainderSeconds) =>
        FromDegrees(MergeXms(isNegative, degrees, minutes, seconds, remainderSeconds));

    private static Angle FromHms(bool isNegative, double hours, double minutes, double seconds, double remainderSeconds) =>
        FromHours(MergeXms(isNegative, hours, minutes, seconds, remainderSeconds));

    private static (bool, uint, uint, uint, double) ValueToXms(double value)
    {
        var sign = value < 0;
        value = Math.Abs(value);
        var degrees = (uint)value;
        value = (value - degrees) * 60.0;
        var minutes = (uint)value;
        value = (value - minutes) * 60.0;
        var seconds = (uint)value;
        var remainder = value - seconds;
        return (sign, degrees, minutes, seconds, remainder);
    }

    private static double MergeXms(bool isNegative, double degrees, double minutes, double seconds, double remainderSeconds) =>
        (isNegative ? -1.0 : 1.0) * (degrees + minutes / 60.0 + (seconds + remainderSeconds) / (60.0 * 60.0));

    public string FormatDegrees()
    {
        var (signB, degrees, minutes, seconds, _) = Dms;
        return $"{(signB ? "-" : "")}{degrees}°{minutes}′{seconds}″";
    }

    public string FormatHours()
    {
        var (signB, degrees, minutes, seconds, _) = Hms;
        return $"{(signB ? "-" : "")}{degrees}h{minutes}′{seconds}″";
    }

    [GeneratedRegex(@"^\s*((?<sign>[-+])\s*)?(?<degrees>\d+(\.\d+)?)\s*(?<unit>[hHdD°])\s*(((?<minutes>\d+(\.\d+)?)\s*[mM'′]\s*)?((?<seconds>\d+(\.\d+)?)\s*[sS""″]\s*)?)?$")]
    private static partial Regex ParseRegex();

    public static bool TryParse(string? s, out Angle angle)
    {
        if (s == null)
        {
            angle = default;
            return false;
        }

        var thing = ParseRegex().Match(s);
        if (!thing.Success)
        {
            angle = default;
            return false;
        }

        var groups = thing.Groups;
        var isNegative = groups["sign"] is { Success: true, Value: "-" };
        var isHours = groups["unit"] is { Success: true, Value: "h" or "H" };
        var degrees = V(groups["degrees"]);
        var minutes = V(groups["degrees"]);
        var seconds = V(groups["degrees"]);

        angle = isHours
            ? FromHms(isNegative, degrees, minutes, seconds, 0.0)
            : FromDms(isNegative, degrees, minutes, seconds, 0.0);

        return true;

        double V(Group group) => group.Success ? double.Parse(group.Value) : 0.0;
    }
}

internal readonly struct MountTime(byte hour, byte minute, byte second, byte month, byte day, byte year, sbyte timeZoneOffset, bool dst)
{
    public static MountTime Now
    {
        get
        {
            var now = DateTimeOffset.Now;
            return new MountTime((byte)now.Hour, (byte)now.Minute, (byte)now.Second, (byte)now.Month, (byte)now.Day, (byte)now.Year, (sbyte)now.Offset.Hours, false);
        }
    }

    public static MountTime ParseTime(byte[] time)
    {
        return new MountTime
        (
            time[0],
            time[1],
            time[2],
            time[3],
            time[4],
            time[5],
            (sbyte)time[6],
            time[7] != 0
        );
    }

    public byte[] FormatTime(char cmd)
    {
        return
        [
            (byte)cmd,
            hour,
            minute,
            second,
            month,
            day,
            year,
            (byte)timeZoneOffset,
            dst ? (byte)1 : (byte)0,
        ];
    }

    public override string ToString()
    {
        return $"{year}-{month}-{day} {hour}:{minute}:{second} {timeZoneOffset}{(dst ? " dst" : "")}";
    }
}
