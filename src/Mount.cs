using System;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Scopie
{
    public class Mount
    {
        private readonly SerialPort _port;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);
        private readonly byte[] _readBuffer = new byte[1];

        public static Mount Create()
        {
            var ports = SerialPort.GetPortNames();
            if (ports.Length != 1)
            {
                return null;
            }
            return new Mount(ports[0]);
        }

        public static string[] Ports() => SerialPort.GetPortNames();

        public Mount(string port)
        {
            _port = new SerialPort(port, 9600, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };
            _port.Open();
        }

        private async Task<string> Interact(string command)
        {
            void Debug(string prefix, string value)
            {
                var nums = string.Join(", ", value.Select(c => (int)c));
                if (value.Length > 0 && value.All(c => c >= 32 && c < 128))
                {
                    nums += " (" + value + ")";
                }
                Console.WriteLine(prefix + " " + nums);
            }

            Task Write(string cmd)
            {
                Debug("  >", cmd);
                var array = new byte[cmd.Length];
                var i = 0;
                foreach (var chr in cmd)
                {
                    array[i++] = (byte)chr;
                }
                return _port.BaseStream.WriteAsync(array, 0, array.Length);
            }

            async Task<string> ReadLine()
            {
                var builder = new StringBuilder();
                while (true)
                {
                    var length = await _port.BaseStream.ReadAsync(_readBuffer, 0, 1);
                    var data = (char)_readBuffer[0];
                    // all responses end with #
                    if (length <= 0 || data == '#')
                    {
                        break;
                    }
                    builder.Append(data);
                }
                var result = builder.ToString();
                Debug("  <", result);
                return result;
            }

            await _semaphore.WaitAsync();
            try
            {
                await Write(command);
                return await ReadLine();
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private static double Mod(double x, double y)
        {
            var r = x % y;
            if (r < 0)
            {
                r += y;
            }
            return r;
        }

        public async Task<(double, double)> GetRaDec()
        {
            var line = await Interact("e");
            var split = line.Split(',');
            if (split.Length != 2)
            {
                throw new Exception($"Invalid response to 'e': {line}");
            }
            var ra = Convert.ToUInt32(split[0], 16) / (uint.MaxValue + 1.0);
            var dec = Convert.ToUInt32(split[1], 16) / (uint.MaxValue + 1.0);
            ra = ra * 24;
            dec = dec * 360;
            return (ra, dec);
        }

        private static string ToMountHex(double value)
        {
            var intval = (uint)(value * (uint.MaxValue + 1.0));
            intval &= 0xffffff00;
            return intval.ToString("X8");
        }

        public async Task OverwriteRaDec(double ra, double dec)
        {
            ra = Mod(ra / 24, 1);
            dec = Mod(dec / 360, 1);
            var res = await Interact($"s{ToMountHex(ra)},{ToMountHex(dec)}");
            if (res != "")
            {
                throw new Exception($"Overwrite RA/DEC failed: {res}");
            }
        }

        public async Task Slew(double ra, double dec)
        {
            ra = Mod(ra / 24, 1);
            dec = Mod(dec / 360, 1);
            var res = await Interact($"r{ToMountHex(ra)},{ToMountHex(dec)}");
            if (res != "")
            {
                throw new Exception($"Slew RA/DEC failed: {res}");
            }
        }

        public async Task<(double, double)> GetAzAlt()
        {
            var line = await Interact("z");
            var split = line.Split(',');
            if (split.Length != 2)
            {
                throw new Exception($"Invalid response to 'z': {line}");
            }
            var az = Convert.ToUInt32(split[0], 16) / (uint.MaxValue + 1.0);
            var alt = Convert.ToUInt32(split[1], 16) / (uint.MaxValue + 1.0);
            az = az * 360;
            alt = alt * 360;
            return (az, alt);
        }

        public async Task SlewAzAlt(double az, double alt)
        {
            az = Mod(az / 360, 1);
            alt = Mod(alt / 360, 1);
            var res = await Interact($"b{ToMountHex(az)},{ToMountHex(alt)}");
            if (res != "")
            {
                throw new Exception($"Slew az/alt failed: {res}");
            }
        }

        public async Task CancelSlew()
        {
            var res = await Interact("M");
            if (res != "")
            {
                throw new Exception($"Cancel slew failed: {res}");
            }
        }

        public enum TrackingMode
        {
            Off = 0,
            AltAz = 1,
            Equatorial = 2,
            SiderealPec = 3,
        }

        public async Task<TrackingMode> GetTrackingMode()
        {
            var result = await Interact("t");
            return (TrackingMode)(int)result[0];
        }

        public async Task SetTrackingMode(TrackingMode mode)
        {
            var modeStr = (char)(int)mode;
            var result = await Interact($"T{modeStr}");
            if (result != "")
            {
                throw new Exception($"Set tracking mode failed: {result}");
            }
        }

        private string FormatLatLon(double lat, double lon)
        {
            var (latSign, latDeg, latMin, latSec, _) = new Dms(lat).DegreesMinutesSeconds;
            var (lonSign, lonDeg, lonMin, lonSec, _) = new Dms(lon).DegreesMinutesSeconds;
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

        private (double, double) ParseLatLon(string value)
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
            var lat = new Dms(latSign, latDeg, latMin, latSec).Value;
            var lon = new Dms(lonSign, lonDeg, lonMin, lonSec).Value;
            return (lat, lon);
        }

        public async Task<(double lat, double lon)> GetLocation()
        {
            var result = await Interact("w");
            return ParseLatLon(result);
        }

        public async Task SetLocation(double lat, double lon)
        {
            var location = FormatLatLon(lat, lon);
            var result = await Interact($"W{location}");
            if (result != "")
            {
                throw new Exception($"Set location failed: {result}");
            }
        }

        private DateTime ParseTime(string time)
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

        private string FormatTime(DateTime time)
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

        public async Task<DateTime> GetTime()
        {
            var result = await Interact("h");
            return ParseTime(result);
        }

        public async Task SetTime(DateTime time)
        {
            var location = FormatTime(time);
            var result = await Interact($"H{location}");
            if (result != "")
            {
                throw new Exception($"Set time failed: {result}");
            }
        }

        public async Task<bool> IsAligned()
        {
            var aligned = await Interact("J");
            return aligned != "0";
        }

        public async Task<char> Echo(char c)
        {
            var echo = await Interact($"K{c}");
            return echo[0];
        }
    }
}
