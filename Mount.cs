using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using static Scopie.ExceptionReporter;
using static Scopie.Ext;

namespace Scopie;

internal sealed class Mount : IDisposable
{
    private readonly SerialPort _port;
    private readonly StackPanel _cameraButtons = new();
    private readonly DockPanel _dockPanel = new();
    private Threadling? _threadling;
    private CameraUiBag? _currentlyDisplaying;
    private char _lastPointingState;
    private (Angle ra, Angle dec) _slewOffset;

    public static readonly List<Mount> AllMounts = [];
    public static event Action? AllMountsChanged;

    public string Name => _port.PortName;

    public Mount(string portName)
    {
        _port = new SerialPort(portName);
        _port.ReadTimeout = 5000;
        _port.WriteTimeout = 5000;
        _port.Open();
    }

    public async Task<TabItem> Init()
    {
        var getRaDec = new TextBlock();
        var getRaDecOffset = new TextBlock();
        var getAzAlt = new TextBlock();
        var trackingMode = new TextBlock();
        var location = new TextBlock();
        var time = new TextBlock();
        var aligned = new TextBlock();
        var tentativeNewPlatesolveOffset = new TextBlock();
        var platesolvePositionToTrueMountPosition = new TextBlock();
        var slewOffset = new TextBlock();

        SetSlewOffset(new Angle(0), new Angle(0));

        var stackPanel = new StackPanel();
        stackPanel.Children.Add(new TextBlock { Text = $"Hand control version {await HandControlVersion()}" });
        stackPanel.Children.Add(new TextBlock { Text = $"RA motor version {await MotorVersion(false)}" });
        stackPanel.Children.Add(new TextBlock { Text = $"Dec motor version {await MotorVersion(true)}" });
        stackPanel.Children.Add(new TextBlock { Text = $"Model {await Model()}" });
        await SetTime(MountTime.Now);
        var initialTrackingMode = await TrackingMode();
        _lastPointingState = await PointingState();
        stackPanel.Children.Add(getRaDec);
        stackPanel.Children.Add(getRaDecOffset);
        stackPanel.Children.Add(platesolvePositionToTrueMountPosition);
        stackPanel.Children.Add(getAzAlt);
        stackPanel.Children.Add(trackingMode);
        stackPanel.Children.Add(location);
        stackPanel.Children.Add(time);
        stackPanel.Children.Add(aligned);
        stackPanel.Children.Add(slewOffset);
        stackPanel.Children.Add(DoubleAngleInput("slew", SlewRaDec));
        stackPanel.Children.Add(DoubleAngleInput("slew az/alt", SlewAzAlt));
        stackPanel.Children.Add(DoubleAngleInput("sync ra/dec", SyncRaDec));
        stackPanel.Children.Add(DoubleAngleInput("set slew offset", (ra, dec) =>
        {
            SetSlewOffset(ra, dec);
            return Task.CompletedTask;
        }));
        stackPanel.Children.Add(Button("cancel slew", () => Try(CancelSlew())));
        stackPanel.Children.Add(Button("park", () => Try(SlewAzAlt(new Angle(0), Angle.FromDegrees(90)))));
        Action<TrackingMode>? trackingModeChanged = null;
        foreach (var mode in Enum.GetValues<TrackingMode>())
        {
            var listen = true;
            var radio = new RadioButton { GroupName = nameof(Scopie.TrackingMode), Content = $"tracking mode {mode}", IsChecked = initialTrackingMode == mode };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (listen && (radio.IsChecked ?? false))
                    Try(SetTrackingMode(mode));
            };
            stackPanel.Children.Add(radio);
            trackingModeChanged += m =>
            {
                listen = false;
                try
                {
                    var should = m == mode;
                    if (should != (radio.IsChecked ?? false))
                        radio.IsChecked = should;
                }
                finally
                {
                    listen = true;
                }
            };
        }

        stackPanel.Children.Add(DoubleAngleInput("location", SetLocation));
        var setTimeNow = new Button { Content = "set time now" };
        setTimeNow.Click += (_, _) => Try(SetTime(MountTime.Now));
        stackPanel.Children.Add(setTimeNow);

        SlewButtons(stackPanel);

        RefreshCameraButtons();
        CameraUiBag.AllCamerasChanged += RefreshCameraButtons;
        stackPanel.Children.Add(_cameraButtons);

        stackPanel.Children.Add(Button("Reset crop", () =>
        {
            foreach (var child in _dockPanel.Children)
                if (child is CroppableImage croppableImage)
                    croppableImage.ResetCrop();
        }));

        var platesolver = new Platesolver(stackPanel, () => _currentlyDisplaying?.Camera.Current);

        stackPanel.Children.Add(tentativeNewPlatesolveOffset);
        stackPanel.Children.Add(Button("use platesolve as slew offset", () => Try(PlatesolveToSlewOffset())));

        UpdateStatus();

        _threadling = new Threadling(UpdateStatus);

        _dockPanel.LastChildFill = true;
        _dockPanel.Children.Clear();
        var scrollViewer = new ScrollViewer { Content = stackPanel, [DockPanel.DockProperty] = Dock.Left };
        _dockPanel.Children.Add(scrollViewer);

        AllMounts.Add(this);
        AllMountsChanged?.Invoke();

        return new TabItem
        {
            Header = "Mount",
            Content = _dockPanel,
        };

        void SetSlewOffset(Angle ra, Angle dec)
        {
            _slewOffset = (ra, dec);
            slewOffset.Text = $"slew offset: {ra.FormatHours()} {dec.FormatDegrees()}";
        }


        IdleActionResult UpdateStatus()
        {
            T Get<T>(Task<T> t)
            {
                if (!t.IsCompleted)
                    throw new Exception("Should be sync result!");
                return t.Result;
            }

            var (ra, dec) = Get(GetRaDec());
            var (az, alt) = Get(GetAzAlt());
            var mode = Get(TrackingMode());
            var a = Get(Aligned());
            var gotoInProgress = Get(GotoInProgress());
            var pointingState = Get(PointingState());
            var (lat, lon) = Get(Location());
            var mountTime = Get(Time());

            Dispatcher.UIThread.Post(() =>
            {
                getRaDec.Text = $"ra/dec: {ra.FormatHours()} {dec.FormatDegrees()}";
                getRaDecOffset.Text = $"ra/dec: {(ra - _slewOffset.ra).FormatHours()} {(dec - _slewOffset.dec).FormatDegrees()} (offset)";
                getAzAlt.Text = $"az/alt: {az.FormatDegrees()} {alt.FormatDegrees()}";
                trackingMode.Text = $"tracking mode: {mode}";
                trackingModeChanged?.Invoke(mode);
                _lastPointingState = pointingState;
                aligned.Text = $"aligned: {a}, goto in progress: {gotoInProgress}, pointing state: {pointingState}";
                location.Text = $"location: {lon.FormatDegrees()} {lat.FormatDegrees()}";
                time.Text = $"time: {mountTime}";
                if (platesolver.LatestSolve is { ra: var platesolveRa, dec: var platesolveDec })
                {
                    var raOffWithSlewOff = (ra - _slewOffset.ra - platesolveRa).Wrapped180;
                    var decOffWithSlewOff = (dec - _slewOffset.dec - platesolveDec).Wrapped180;
                    platesolvePositionToTrueMountPosition.Text = $"platesolve offset: {raOffWithSlewOff.FormatHours()} {decOffWithSlewOff.FormatDegrees()}";
                    var raOff = (ra - platesolveRa).Wrapped180;
                    var decOff = (dec - platesolveDec).Wrapped180;
                    tentativeNewPlatesolveOffset.Text = $"tentative new platesolve offset: {raOff.FormatHours()} {decOff.FormatDegrees()}";
                }
            });

            return IdleActionResult.WaitWithTimeout(TimeSpan.FromSeconds(1));
        }

        async Task PlatesolveToSlewOffset()
        {
            if (platesolver.LatestSolve is { ra: var platesolveRa, dec: var platesolveDec })
            {
                var (ra, dec) = await GetRaDec();
                var raOff = (ra - platesolveRa).Wrapped180;
                var decOff = (dec - platesolveDec).Wrapped180;
                SetSlewOffset(raOff, decOff);
            }
        }
    }

    private void SlewButtons(StackPanel stackPanel)
    {
        var slewSpeed = 1;
        var slewSpeedLabel = new TextBlock { Text = "slew speed: 1" };
        var slewSpeedPlus = new Button { Content = "+" };
        var slewSpeedMinus = new Button { Content = "-" };
        var raPlus = new Button { Content = "ra+" };
        var raMinus = new Button { Content = "ra-" };
        var decPlus = new Button { Content = "dec+" };
        var decMinus = new Button { Content = "dec-" };

        slewSpeedPlus.Click += (_, _) =>
        {
            slewSpeed = Math.Min(slewSpeed + 1, 9);
            slewSpeedLabel.Text = $"slew speed: {slewSpeed}";
        };
        slewSpeedMinus.Click += (_, _) =>
        {
            slewSpeed = Math.Max(slewSpeed - 1, 1);
            slewSpeedLabel.Text = $"slew speed: {slewSpeed}";
        };
        raPlus.AddHandler(InputElement.PointerPressedEvent, (_, _) => Try(FixedSlewRa(slewSpeed)), handledEventsToo: true);
        raPlus.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Try(FixedSlewRa(0)), handledEventsToo: true);
        raMinus.AddHandler(InputElement.PointerPressedEvent, (_, _) => Try(FixedSlewRa(-slewSpeed)), handledEventsToo: true);
        raMinus.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Try(FixedSlewRa(0)), handledEventsToo: true);
        decPlus.AddHandler(InputElement.PointerPressedEvent, (_, _) => Try(FixedSlewDec(slewSpeed)), handledEventsToo: true);
        decPlus.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Try(FixedSlewDec(0)), handledEventsToo: true);
        decMinus.AddHandler(InputElement.PointerPressedEvent, (_, _) => Try(FixedSlewDec(-slewSpeed)), handledEventsToo: true);
        decMinus.AddHandler(InputElement.PointerReleasedEvent, (_, _) => Try(FixedSlewDec(0)), handledEventsToo: true);

        stackPanel.Children.Add(new StackPanel
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
        stackPanel.Children.Add(decMinus);
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
                _currentlyDisplaying = camera;
            }));
        }
    }

    public void Dispose()
    {
        AllMounts.Remove(this);
        AllMountsChanged?.Invoke();

        CameraUiBag.AllCamerasChanged -= RefreshCameraButtons;
        if (_threadling != null)
        {
            _threadling.Do(() =>
            {
                if (_port.IsOpen)
                    _port.Close();
                _port.Dispose();
            });
            _threadling.Dispose();
        }
        else
        {
            if (_port.IsOpen)
                _port.Close();
            _port.Dispose();
        }
    }

    private Task<byte[]> Interact(byte[] data, int responseLength)
    {
        if (_threadling == null)
        {
            try
            {
                return Task.FromResult(Func());
            }
            catch (Exception e)
            {
                return Task.FromException<byte[]>(e);
            }
        }

        return _threadling.Do((Func<byte[]>)Func);

        byte[] Func()
        {
            _port.Write(data, 0, data.Length);
            var result = new byte[responseLength];
            var index = 0;
            while (index < responseLength)
                index += _port.Read(result, index, responseLength - index);
            if (_port.ReadByte() != '#')
                throw new Exception("Mount reply didn't end with '#'");
            return result;
        }
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
        if (bytes.Length != 18)
            throw new Exception($"Internal error in SendAngleCommand ({bytes.Length}): {bytes}");
        return Interact(bytes, 0);
    }

    private Task<(Angle ra, Angle dec)> GetRaDec() => GetAngleCommand('e');

    private Task SyncRaDec(Angle ra, Angle dec) => SendAngleCommand('s', ra, dec);

    /*
    // TODO: Haven't figured out how to math out an RA/Dec to a raw motor position.
    // Have to take into account pointing state (east/west), etc.

    // Note: the P,4,16,4 command sets local RA (i.e. park position is 0), not actual RA
    // Setting ResetRa(0) ResetDec(90) tells the mount it is currently in park position
    // Setting ResetRa(x) ResetDec(y) tells the mount it is currently pointing here
    // Setting ResetDec to a value >90 means pointing state west, <90 is pointing state east (or something like that)
    // Note: Tracking mode must be off for this to function
    private Task ResetRawMotorPositionRa(Angle localRa)
    {
        localRa.To3Byte(out var high, out var med, out var low);
        return Interact([(byte)'P', 4, 16, 4, high, med, low, 0], 0);
    }

    private Task ResetRawMotorPositionDec(Angle dec)
    {
        dec.To3Byte(out var high, out var med, out var low);
        return Interact([(byte)'P', 4, 17, 4, high, med, low, 0], 0);
    }
    */

    private Task SlewRaDec(Angle ra, Angle dec) => SendAngleCommand('r', ra + _slewOffset.ra, dec + _slewOffset.dec);

    private Task<(Angle ra, Angle dec)> GetAzAlt() => GetAngleCommand('z');

    // note: This assumes the telescope's az axis is straight up.
    // This is NOT what is reported in GetAzAlt
    private Task SlewAzAlt(Angle az, Angle alt) => SendAngleCommand('b', az, alt);

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

    private async Task<char> GotoInProgress()
    {
        var result = await Interact([(byte)'L'], 1);
        return (char)result[0];
    }

    private async Task<char> PointingState()
    {
        var result = await Interact([(byte)'p'], 1);
        return (char)result[0];
    }

    private Task FixedSlewCommand(bool ra, int speed)
    {
        // These slew in the actual physical motor direction, not the true direction (flips with east/west pointing state).
        // When in West, dec is inverted. Ra is always inverted. I think.
        if (ra || _lastPointingState == 'W')
            speed = -speed;

        var axis = ra ? (byte)16 : (byte)17;

        byte three;
        if (speed < 0)
        {
            three = 37;
            speed = -speed;
        }
        else
            three = 36;

        return Interact([(byte)'P', 2, axis, three, (byte)speed, 0, 0, 0], 0);
    }

    private Task FixedSlewRa(int speed) => FixedSlewCommand(true, speed);
    private Task FixedSlewDec(int speed) => FixedSlewCommand(false, speed);

    private Task VariableSlewCommand(bool ra, int trackRate)
    {
        byte three;
        if (trackRate < 0)
        {
            three = 7;
            trackRate = -trackRate;
        }
        else
            three = 6;

        var high = trackRate / 256;
        var low = trackRate % 256;

        var axis = ra ? (byte)16 : (byte)17;
        return Interact([(byte)'P', 3, axis, three, (byte)high, (byte)low, 0, 0], 0);
    }

    public Task VariableSlewCommand(bool ra, double arcsecondsPerSecond) => VariableSlewCommand(ra, (int)(arcsecondsPerSecond * 4));

    private async Task<string> HandControlVersion()
    {
        var r = Encoding.UTF8.GetString(await Interact([(byte)'V'], 6));
        var v = Convert.ToUInt32(r, 16);
        return $"{(v >> 16) & 0xff}.{(v >> 8) & 0xff}.{v & 0xff}";
    }

    private async Task<string> MotorVersion(bool dec)
    {
        var r = await Interact([(byte)'P', 1, dec ? (byte)17 : (byte)16, 254, 0, 0, 0, 2], 2);
        return $"{r[0]}.{r[1]}";
    }

    private async Task<byte> Model()
    {
        return (await Interact([(byte)'m'], 1))[0];
    }

    /*
    private static double GreenwichSiderealTime(DateTime utcNow)
    {
        const int daysPer100Years = 36524;
        const long ticksPerDay = 864000000000L;
        // 12 hours ahead of specified epoch, because the algorithm expects the julian date to end in 0.5, not 0.0,
        // so do the subtraction from 12 hours ahead then add 0.5 days
        var epoch = new DateTime(2000, 1, 2, 0, 0, 0);
        var tickDiff = utcNow.Ticks - epoch.Ticks;
        var wholeDays = Math.DivRem(tickDiff, ticksPerDay, out var remainder);
        var julianOffsetFromEpoch = wholeDays + 0.5;
        var centuries = tickDiff / (double)(ticksPerDay * daysPer100Years);
        var result = 6.697374558 +
                     0.06570982439425051 * julianOffsetFromEpoch +
                     24.0 / ticksPerDay * 1.002737909 * remainder +
                     0.000025862 * (centuries * centuries);
        return Mod(result, 24);
    }

    private static Angle LocalSiderealTime(DateTime utcNow, Angle longitude) =>
        Angle.FromHours(Mod(GreenwichSiderealTime(utcNow) + longitude.Degrees / 15, 24));
    */
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
    private double Value => value;

    private const double MaxUint = (double)uint.MaxValue + 1;
    public static Angle FromUint(uint angle) => new(angle / MaxUint);
    public uint Uint => (uint)((value - Math.Floor(value)) * MaxUint);
    public Angle Wrapped => new(value - Math.Floor(value));
    public Angle Wrapped180 => new(value - Math.Round(value));

    public double Degrees => value * 360.0;
    public static Angle FromDegrees(double value) => new(value / 360.0);
    public static Angle FromHours(double value) => new(value / 24.0);
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

    public static Angle operator +(Angle left, Angle right) => new(left.Value + right.Value);
    public static Angle operator -(Angle left, Angle right) => new(left.Value - right.Value);

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

    public static bool TryParse(string? s, IFormatProvider? formatProvider, out Angle angle)
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
        var degrees = double.Parse(groups["degrees"].Value, formatProvider);
        var minutes = V(groups["minutes"]);
        var seconds = V(groups["seconds"]);

        angle = isHours
            ? FromHms(isNegative, degrees, minutes, seconds, 0.0)
            : FromDms(isNegative, degrees, minutes, seconds, 0.0);

        return true;

        double V(Group group) => group.Success ? double.Parse(group.Value, formatProvider) : 0.0;
    }

    public void To3Byte(out byte high, out byte med, out byte low)
    {
        var i = (uint)((value - Math.Floor(value)) * 0x1_00_00_00);
        high = (byte)(i >> 16);
        med = (byte)(i >> 8);
        low = (byte)i;
    }
}

internal readonly struct MountTime(byte hour, byte minute, byte second, byte month, byte day, byte year, sbyte timeZoneOffset, bool dst)
{
    public static MountTime Now
    {
        get
        {
            var now = DateTimeOffset.Now;
            return new MountTime((byte)now.Hour, (byte)now.Minute, (byte)now.Second, (byte)now.Month, (byte)now.Day, (byte)(now.Year - 2000), (sbyte)now.Offset.Hours, false);
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
