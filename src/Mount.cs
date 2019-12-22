using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace Scopie
{
    public sealed class Mount : IDisposable
    {
        private readonly SerialPort _port;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        public static Mount? Create()
        {
            var ports = SerialPort.GetPortNames();
            return ports.Length == 1 ? new Mount(ports[0]) : null;
        }

        public static string[] Ports() => SerialPort.GetPortNames();

        public static Mount? AutoConnect()
        {
            foreach (var port in Ports())
            {
                Mount? mount = null;
                try
                {
#pragma warning disable IDE0068 // Use recommended dispose pattern
                    mount = new Mount(port);
#pragma warning restore IDE0068 // Use recommended dispose pattern
                    _ = mount.GetRaDec();
                    return mount;
                }
                catch
                {
                    mount?.Dispose();
                }
            }
            return null;
        }

        public Mount(string port)
        {
            _port = new SerialPort(port, 9600, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000,
            };
            _port.Open();
        }

        private byte[] StrToByteArr(string str)
        {
            var array = new byte[str.Length];
            var i = 0;
            foreach (var chr in str)
            {
                array[i++] = (byte)chr;
            }
            return array;
        }

        private string ByteArrayToStr(byte[] bytes) => Encoding.ASCII.GetString(bytes);

        private byte[] Interact(byte[] command, int responseLength)
        {
            _semaphore.Wait();
            try
            {
                _port.Write(command, 0, command.Length);
                var result = new byte[responseLength];
                for (var i = 0; i < responseLength; i++)
                {
                    result[i] = (byte)_port.ReadByte();
                }
                if (_port.ReadByte() != '#')
                {
                    throw new Exception("Mount response did not end in '#'");
                }
                return result;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private void InteractEmpty(byte[] command) => _ = Interact(command, 0);

        private string InteractString(string command, int responseLength) =>
            ByteArrayToStr(Interact(StrToByteArr(command), responseLength));

        private void InteractStringEmpty(string command) => InteractEmpty(StrToByteArr(command));

        public (Dms, Dms) GetRaDec()
        {
            var line = InteractString("e", 17);
            var split = line.Split(',');
            if (split.Length != 2)
            {
                throw new Exception($"Invalid response to 'e': {line}");
            }
            var ra = Convert.ToUInt32(split[0], 16) / (uint.MaxValue + 1.0);
            var dec = Convert.ToUInt32(split[1], 16) / (uint.MaxValue + 1.0);
            return (Dms.From0to1(ra), Dms.From0to1(dec));
        }

        private static string ToMountHex(Dms value)
        {
            var intval = (uint)(value.Value0to1 * (uint.MaxValue + 1.0));
            intval &= 0xffffff00;
            return intval.ToString("X8");
        }

        public void SyncRaDec(Dms ra, Dms dec) => InteractStringEmpty($"s{ToMountHex(ra)},{ToMountHex(dec)}");

        public void Slew(Dms ra, Dms dec) => InteractStringEmpty($"r{ToMountHex(ra)},{ToMountHex(dec)}");

        public (Dms, Dms) GetAzAlt()
        {
            var line = InteractString("z", 17);
            var split = line.Split(',');
            if (split.Length != 2)
            {
                throw new Exception($"Invalid response to 'z': {line}");
            }
            var az = Convert.ToUInt32(split[0], 16) / (uint.MaxValue + 1.0);
            var alt = Convert.ToUInt32(split[1], 16) / (uint.MaxValue + 1.0);
            return (Dms.From0to1(az), Dms.From0to1(alt));
        }

        public void SlewAzAlt(Dms az, Dms alt) => InteractStringEmpty($"b{ToMountHex(az)},{ToMountHex(alt)}");

        public void CancelSlew() => InteractStringEmpty("M");

        public enum TrackingMode
        {
            Off = 0,
            AltAz = 1,
            Equatorial = 2,
            SiderealPec = 3,
        }

        public TrackingMode GetTrackingMode()
        {
            var result = Interact(new[] { (byte)'t' }, 1);
            return (TrackingMode)result[0];
        }

        public void SetTrackingMode(TrackingMode mode) => InteractStringEmpty($"T{(char)(int)mode}");

        private static string FormatLatLon(Dms lat, Dms lon)
        {
            var (latSign, latDeg, latMin, latSec, _) = lat.DegreesMinutesSeconds;
            var (lonSign, lonDeg, lonMin, lonSec, _) = lon.DegreesMinutesSeconds;
            // The format of the location commands is: ABCDEFGH, where:
            // A is the number of degrees of latitude.
            // B is the number of minutes of latitude.
            // C is the number of seconds of latitude.
            // D is 0 for north and 1 for south.
            // E is the number of degrees of longitude.
            // F is the number of minutes of longitude.
            // G is the number of seconds of longitude.
            // H is 0 for east and 1 for west.
            var builder = new StringBuilder(8);
            builder.Append((char)latDeg);
            builder.Append((char)latMin);
            builder.Append((char)latSec);
            builder.Append(latSign ? (char)1 : (char)0);
            builder.Append((char)lonDeg);
            builder.Append((char)lonMin);
            builder.Append((char)lonSec);
            builder.Append(lonSign ? (char)1 : (char)0);
            return builder.ToString();
        }

        private static (Dms, Dms) ParseLatLon(byte[] value)
        {
            if (value.Length != 8)
            {
                throw new Exception($"Invalid lat/lon: {value}");
            }
            var latDeg = (int)value[0];
            var latMin = (int)value[1];
            var latSec = (int)value[2];
            var latSign = value[3] == 1;
            var lonDeg = (int)value[4];
            var lonMin = (int)value[5];
            var lonSec = (int)value[6];
            var lonSign = value[7] == 1;
            var lat = Dms.FromDms(latSign, latDeg, latMin, latSec);
            var lon = Dms.FromDms(lonSign, lonDeg, lonMin, lonSec);
            return (lat, lon);
        }

        public (Dms lat, Dms lon) GetLocation() => ParseLatLon(Interact(new[] { (byte)'w' }, 8));

        public void SetLocation(Dms lat, Dms lon) => InteractStringEmpty($"W{FormatLatLon(lat, lon)}");

        private static DateTime ParseTime(byte[] time)
        {
            if (time.Length != 8)
            {
                throw new Exception($"Invalid time: {time}");
            }
            var hour = (int)time[0];
            var minute = (int)time[1];
            var second = (int)time[2];
            var month = (int)time[3];
            var day = (int)time[4];
            var year = (int)time[5] + 2000;
            var timeZoneOffset = (int)time[6];
            var dst = time[7] == 1;

            if (dst)
            {
                timeZoneOffset -= 1;
            }
            if (timeZoneOffset >= 128)
            {
                timeZoneOffset -= 256;
            }

            var res = new DateTime(year, month, day, hour, minute, second, 0, DateTimeKind.Local);

            var currentTimeZoneOffset = TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).Hours;
            if (currentTimeZoneOffset != timeZoneOffset)
            {
                var delta = timeZoneOffset - currentTimeZoneOffset;
                Console.WriteLine("Mount thinks it's in a different timezone?");
                Console.WriteLine($"Mount thinks: {timeZoneOffset}");
                Console.WriteLine($"Computer thinks: {currentTimeZoneOffset}");
                Console.WriteLine($"Adding {delta} to reported hour");
                res.AddHours(delta);
            }
            return res;
        }

        private static string FormatTime(DateTime time)
        {
            var timeZoneOffset = TimeZoneInfo.Local.GetUtcOffset(time).Hours;
            if (timeZoneOffset < 0)
            {
                timeZoneOffset += 256;
            }
            // Q is the hour (24 hour clock).
            // R is the minutes.
            // S is the seconds.
            // T is the month.
            // U is the day.
            // V is the year (century assumed as 20).
            // W is the offset from GMT for the time zone. Note: if zone is negative, use 256 - zone.
            // X is 1 to enable Daylight Savings and 0 for Standard Time
            var builder = new StringBuilder();
            builder.Append((char)time.Hour);
            builder.Append((char)time.Minute);
            builder.Append((char)time.Second);
            builder.Append((char)time.Month);
            builder.Append((char)time.Day);
            builder.Append((char)(time.Year - 2000));
            builder.Append((char)timeZoneOffset);
            builder.Append((char)0); // DST is already calculated in .net
            return builder.ToString();
        }

        public DateTime GetTime() => ParseTime(Interact(new[] { (byte)'h' }, 8));

        public void SetTime(DateTime time) => InteractStringEmpty($"H{FormatTime(time)}");

        // this is 0 or 1...
        public bool IsAligned() => Interact(new[] { (byte)'J' }, 1)[0] != 0;

        // ... but this is '0' or '1'???
        public bool IsDoingGoto() => Interact(new[] { (byte)'L' }, 1)[0] != '0';

        public char Echo(char c) => InteractString($"K{c}", 1)[0];

        /*
        static void SplitThreeBytes(Dms valueDms, out byte high, out byte med, out byte low)
        {
            var value = valueDms.Value0to1;
            value *= 256;
            high = (byte)value;
            value = Dms.Mod(value, 1);
            value *= 256;
            med = (byte)value;
            value = Dms.Mod(value, 1);
            value *= 256;
            low = (byte)value;
        }

        void PCommandThree(byte one, byte two, byte three, Dms data)
        {
            SplitThreeBytes(data, out var high, out var med, out var low);
            var cmd = new char[8];
            cmd[0] = 'P';
            cmd[1] = (char)one;
            cmd[2] = (char)two;
            cmd[3] = (char)three;
            cmd[4] = (char)high;
            cmd[5] = (char)med;
            cmd[6] = (char)low;
            cmd[7] = (char)0;
            InteractStringEmpty(new string(cmd));
        }

        // these are fucky and confusing and just, no
        public void ResetRA(Dms data) => PCommandThree(4, 16, 4, data);

        public void ResetDec(Dms data) => PCommandThree(4, 17, 4, data);

        public void SlowGotoRA(Dms data) => PCommandThree(4, 16, 23, data);

        public void SlowGotoDec(Dms data) => PCommandThree(4, 17, 23, data);
        */

        void FixedSlewCommand(byte one, byte two, byte three, byte rate)
        {
            var cmd = new byte[8];
            cmd[0] = (byte)'P';
            cmd[1] = one;
            cmd[2] = two;
            cmd[3] = three;
            cmd[4] = rate;
            cmd[5] = 0;
            cmd[6] = 0;
            cmd[7] = 0;
            InteractEmpty(cmd);
        }

        public void FixedSlewRA(int speed)
        {
            if (speed >= 0)
            {
                FixedSlewCommand(2, 16, 36, (byte)speed);
            }
            else
            {
                FixedSlewCommand(2, 16, 37, (byte)-speed);
            }
        }

        public void FixedSlewDec(int speed)
        {
            if (speed >= 0)
            {
                FixedSlewCommand(2, 17, 36, (byte)speed);
            }
            else
            {
                FixedSlewCommand(2, 17, 37, (byte)-speed);
            }
        }

        void VariableSlewCommand(byte one, byte two, byte three, double arcsecondsPerSecond)
        {
            var mul4 = (int)(arcsecondsPerSecond * 4);
            if (mul4 >= 256 * 256)
            {
                mul4 = 256 * 256 - 1;
            }
            var trackRateHigh = (byte)(mul4 / 256);
            var trackRateLow = (byte)(mul4 % 256);

            var cmd = new byte[8];
            cmd[0] = (byte)'P';
            cmd[1] = one;
            cmd[2] = two;
            cmd[3] = three;
            cmd[4] = trackRateHigh;
            cmd[5] = trackRateLow;
            cmd[6] = 0;
            cmd[7] = 0;
            InteractEmpty(cmd);
        }

        public void VariableSlewRA(double arcsecondsPerSecond)
        {
            if (arcsecondsPerSecond >= 0)
            {
                VariableSlewCommand(3, 16, 6, arcsecondsPerSecond);
            }
            else
            {
                VariableSlewCommand(3, 16, 7, -arcsecondsPerSecond);
            }
        }

        public void VariableSlewDec(double arcsecondsPerSecond)
        {
            if (arcsecondsPerSecond >= 0)
            {
                VariableSlewCommand(3, 17, 6, arcsecondsPerSecond);
            }
            else
            {
                VariableSlewCommand(3, 17, 7, -arcsecondsPerSecond);
            }
        }

        public void Dispose()
        {
            _semaphore.Dispose();
            _port.Dispose();
        }
    }
}
